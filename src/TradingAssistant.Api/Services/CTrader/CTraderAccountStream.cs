using System.Collections.Concurrent;
using System.Reactive.Linq;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenAPI.Net;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Hubs;
using TradingAssistant.Api.Models.Trading;
using TradingAssistant.Api.Services.Journal;

namespace TradingAssistant.Api.Services.CTrader;

public interface ICTraderAccountStream
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    event EventHandler<PositionEventArgs>? OnPositionOpened;
    event EventHandler<PositionEventArgs>? OnPositionClosed;
    event EventHandler<PositionEventArgs>? OnPositionModified;
}

public class CTraderAccountStream : ICTraderAccountStream
{
    private readonly ICTraderConnectionManager _connectionManager;
    private readonly ICTraderSymbolResolver _symbolResolver;
    private readonly ICTraderPriceStream _priceStream;
    private readonly IHubContext<TradingHub, ITradingHubClient> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CTraderAccountStream> _logger;

    private IDisposable? _executionSubscription;
    private readonly ConcurrentDictionary<long, DateTime> _lastPnLBroadcast = new();
    private static readonly TimeSpan PnLThrottleInterval = TimeSpan.FromSeconds(2);

    public event EventHandler<PositionEventArgs>? OnPositionOpened;
    public event EventHandler<PositionEventArgs>? OnPositionClosed;
    public event EventHandler<PositionEventArgs>? OnPositionModified;

    public CTraderAccountStream(
        ICTraderConnectionManager connectionManager,
        ICTraderSymbolResolver symbolResolver,
        ICTraderPriceStream priceStream,
        IHubContext<TradingHub, ITradingHubClient> hubContext,
        IServiceScopeFactory scopeFactory,
        ILogger<CTraderAccountStream> logger)
    {
        _connectionManager = connectionManager;
        _symbolResolver = symbolResolver;
        _priceStream = priceStream;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Account stream starting...");

        var client = await _connectionManager.GetClientAsync(cancellationToken);

        _executionSubscription = client.OfType<ProtoOAExecutionEvent>().Subscribe(
            async executionEvent =>
            {
                try
                {
                    await HandleExecutionEventAsync(executionEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling execution event: {ExecutionType}",
                        executionEvent.ExecutionType);
                }
            },
            error => _logger.LogError(error, "Account stream error"));

        // Subscribe to price updates to broadcast real-time P&L for open positions
        _priceStream.OnPriceUpdate += async (_, args) =>
        {
            try
            {
                await BroadcastPositionPnLAsync(args.Symbol, args.Bid);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error broadcasting P&L for {Symbol}", args.Symbol);
            }
        };

        _logger.LogInformation("Account stream started — listening for execution events and price updates");
    }

    public Task StopAsync()
    {
        _logger.LogInformation("Account stream stopping...");
        _executionSubscription?.Dispose();
        _executionSubscription = null;
        return Task.CompletedTask;
    }

    private async Task HandleExecutionEventAsync(ProtoOAExecutionEvent evt)
    {
        _logger.LogDebug("Execution event: {Type}, HasPosition={HasPos}, HasOrder={HasOrder}, HasDeal={HasDeal}",
            evt.ExecutionType, evt.Position != null, evt.Order != null, evt.Deal != null);

        switch (evt.ExecutionType)
        {
            case ProtoOAExecutionType.OrderFilled:
            case ProtoOAExecutionType.OrderPartialFill:
                await HandleOrderFilledAsync(evt);
                break;

            case ProtoOAExecutionType.OrderAccepted:
            case ProtoOAExecutionType.OrderReplaced:
                await HandleOrderUpdateAsync(evt);
                break;

            case ProtoOAExecutionType.OrderCancelled:
            case ProtoOAExecutionType.OrderExpired:
            case ProtoOAExecutionType.OrderRejected:
                await HandleOrderTerminatedAsync(evt);
                break;

            case ProtoOAExecutionType.Swap:
                if (evt.Position != null)
                    await HandlePositionSwapAsync(evt);
                break;
        }
    }

    private async Task HandleOrderFilledAsync(ProtoOAExecutionEvent evt)
    {
        if (evt.Position == null) return;

        var protoPos = evt.Position;
        var symbolName = _symbolResolver.GetSymbolName(protoPos.TradeData.SymbolId);
        var moneyDigits = protoPos.HasMoneyDigits ? (int)protoPos.MoneyDigits : 2;
        var accountId = _connectionManager.AccountId;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.CTraderAccountId == accountId);
        if (account is null) return;

        // Handle deal if present (for recording in deals table)
        if (evt.Deal != null)
        {
            await UpsertDealAsync(db, account.Id, evt.Deal, symbolName, moneyDigits);
        }

        var isClosing = protoPos.PositionStatus == ProtoOAPositionStatus.PositionStatusClosed;

        if (isClosing)
        {
            await HandlePositionClosedInternalAsync(db, account.Id, protoPos, symbolName, moneyDigits, evt.Deal);
        }
        else
        {
            await HandlePositionOpenOrUpdateAsync(db, account.Id, protoPos, symbolName, moneyDigits);
        }

        await db.SaveChangesAsync();
    }

    private async Task HandlePositionOpenOrUpdateAsync(
        AppDbContext db, long dbAccountId, ProtoOAPosition protoPos, string symbolName, int moneyDigits)
    {
        var existing = await db.Positions.FirstOrDefaultAsync(
            p => p.CTraderPositionId == protoPos.PositionId);

        var isNew = existing is null;
        var position = existing ?? new Position
        {
            CTraderPositionId = protoPos.PositionId,
            AccountId = dbAccountId,
            CreatedAt = DateTime.UtcNow
        };

        if (isNew)
            db.Positions.Add(position);

        position.Symbol = symbolName;
        position.Direction = protoPos.TradeData.TradeSide == ProtoOATradeSide.Buy
            ? TradeDirection.Buy : TradeDirection.Sell;
        position.Volume = CTraderConversions.CentsToLots(protoPos.TradeData.Volume);
        position.EntryPrice = protoPos.HasPrice ? (decimal)protoPos.Price : 0m;
        position.StopLoss = protoPos.HasStopLoss ? (decimal?)protoPos.StopLoss : null;
        position.TakeProfit = protoPos.HasTakeProfit ? (decimal?)protoPos.TakeProfit : null;
        position.Swap = CTraderConversions.MoneyToDecimal(protoPos.Swap, moneyDigits);
        position.Commission = protoPos.HasCommission
            ? CTraderConversions.MoneyToDecimal(protoPos.Commission, moneyDigits)
            : 0m;
        position.Status = PositionStatus.Open;
        position.OpenTime = protoPos.TradeData.HasOpenTimestamp
            ? CTraderConversions.FromUnixMs(protoPos.TradeData.OpenTimestamp)
            : DateTime.UtcNow;
        position.UpdatedAt = DateTime.UtcNow;

        var args = new PositionEventArgs
        {
            PositionId = protoPos.PositionId,
            Symbol = symbolName,
            Direction = position.Direction.ToString(),
            Volume = position.Volume,
            EntryPrice = position.EntryPrice,
            CurrentPrice = position.EntryPrice,
            PnL = 0m,
            StopLoss = position.StopLoss,
            TakeProfit = position.TakeProfit
        };

        if (isNew)
        {
            _logger.LogInformation("Position opened: {Symbol} {Direction} {Volume} @ {Price}",
                symbolName, position.Direction, position.Volume, position.EntryPrice);
            await HandlePositionOpened(args);
        }
        else
        {
            _logger.LogInformation("Position modified: {Symbol} {Direction} Volume={Volume}",
                symbolName, position.Direction, position.Volume);
            OnPositionModified?.Invoke(this, args);
        }
    }

    private async Task HandlePositionClosedInternalAsync(
        AppDbContext db, long dbAccountId, ProtoOAPosition protoPos, string symbolName, int moneyDigits,
        ProtoOADeal? deal)
    {
        var position = await db.Positions.FirstOrDefaultAsync(
            p => p.CTraderPositionId == protoPos.PositionId);

        if (position is null)
        {
            position = new Position
            {
                CTraderPositionId = protoPos.PositionId,
                AccountId = dbAccountId,
                CreatedAt = DateTime.UtcNow
            };
            db.Positions.Add(position);
        }

        // Only set fields that aren't already populated — the close event's TradeData
        // may have Volume=0 and Price=0 for a fully-closed position, which would
        // overwrite the correct values saved when the position was opened.
        if (string.IsNullOrEmpty(position.Symbol))
            position.Symbol = symbolName;
        if (position.Volume == 0m)
        {
            var closeVolume = CTraderConversions.CentsToLots(protoPos.TradeData.Volume);
            if (closeVolume > 0) position.Volume = closeVolume;
        }
        if (position.EntryPrice == 0m && protoPos.HasPrice && (decimal)protoPos.Price > 0)
            position.EntryPrice = (decimal)protoPos.Price;
        if (position.Direction == default)
        {
            position.Direction = protoPos.TradeData.TradeSide == ProtoOATradeSide.Buy
                ? TradeDirection.Buy : TradeDirection.Sell;
        }
        position.Swap = CTraderConversions.MoneyToDecimal(protoPos.Swap, moneyDigits);
        position.Commission = protoPos.HasCommission
            ? CTraderConversions.MoneyToDecimal(protoPos.Commission, moneyDigits)
            : 0m;
        position.Status = PositionStatus.Closed;
        position.CloseTime = DateTime.UtcNow;
        position.UpdatedAt = DateTime.UtcNow;

        // Extract close details from the deal
        decimal closePrice = 0m;
        decimal realizedPnl = 0m;

        if (deal?.ClosePositionDetail != null)
        {
            var closeDetail = deal.ClosePositionDetail;
            closePrice = (decimal)closeDetail.EntryPrice; // this is actually the close execution price
            realizedPnl = CTraderConversions.MoneyToDecimal(closeDetail.GrossProfit, moneyDigits);
            position.ClosePrice = deal.HasExecutionPrice ? (decimal)deal.ExecutionPrice : closePrice;
            position.RealizedPnL = realizedPnl
                + CTraderConversions.MoneyToDecimal(closeDetail.Swap, moneyDigits)
                + CTraderConversions.MoneyToDecimal(closeDetail.Commission, moneyDigits);
        }
        else if (deal is not null && deal.HasExecutionPrice)
        {
            position.ClosePrice = (decimal)deal.ExecutionPrice;
        }

        var args = new PositionEventArgs
        {
            PositionId = protoPos.PositionId,
            AccountId = dbAccountId,
            Symbol = symbolName,
            Direction = position.Direction.ToString(),
            Volume = position.Volume,
            EntryPrice = position.EntryPrice,
            CurrentPrice = position.ClosePrice ?? position.EntryPrice,
            PnL = position.RealizedPnL ?? realizedPnl,
            StopLoss = position.StopLoss,
            TakeProfit = position.TakeProfit,
            OpenTime = position.OpenTime,
            Commission = position.Commission,
            Swap = position.Swap
        };

        _logger.LogInformation("Position closed: {Symbol} PnL={PnL}", symbolName, args.PnL);
        await HandlePositionClosed(args);

        // Record in trade journal
        try
        {
            var journalService = _scopeFactory.CreateScope().ServiceProvider
                .GetRequiredService<ITradeJournalService>();
            await journalService.RecordTradeAsync(args);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record trade in journal");
        }
    }

    private async Task UpsertDealAsync(
        AppDbContext db, long dbAccountId, ProtoOADeal protoDeal, string symbolName, int moneyDigits)
    {
        var existing = await db.Deals.FirstOrDefaultAsync(d => d.CTraderDealId == protoDeal.DealId);
        if (existing is not null) return;

        var deal = new Deal
        {
            CTraderDealId = protoDeal.DealId,
            AccountId = dbAccountId,
            PositionId = protoDeal.PositionId,
            OrderId = protoDeal.OrderId,
            Symbol = symbolName,
            Direction = protoDeal.TradeSide == ProtoOATradeSide.Buy
                ? TradeDirection.Buy : TradeDirection.Sell,
            Volume = CTraderConversions.CentsToLots(protoDeal.FilledVolume),
            ExecutionPrice = protoDeal.HasExecutionPrice ? (decimal)protoDeal.ExecutionPrice : 0m,
            Commission = protoDeal.HasCommission
                ? CTraderConversions.MoneyToDecimal(protoDeal.Commission, moneyDigits)
                : 0m,
            PnL = protoDeal.ClosePositionDetail != null
                ? CTraderConversions.MoneyToDecimal(protoDeal.ClosePositionDetail.GrossProfit, moneyDigits)
                : 0m,
            Swap = protoDeal.ClosePositionDetail != null
                ? CTraderConversions.MoneyToDecimal(protoDeal.ClosePositionDetail.Swap, moneyDigits)
                : 0m,
            Type = protoDeal.ClosePositionDetail != null ? DealType.Close : DealType.Open,
            ExecutedAt = CTraderConversions.FromUnixMs(protoDeal.ExecutionTimestamp),
            CreatedAt = DateTime.UtcNow
        };

        db.Deals.Add(deal);
    }

    private async Task HandleOrderUpdateAsync(ProtoOAExecutionEvent evt)
    {
        if (evt.Order == null) return;

        var protoOrder = evt.Order;
        var symbolName = _symbolResolver.GetSymbolName(protoOrder.TradeData.SymbolId);
        var accountId = _connectionManager.AccountId;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.CTraderAccountId == accountId);
        if (account is null) return;

        var order = await db.Orders.FirstOrDefaultAsync(
            o => o.CTraderOrderId == protoOrder.OrderId);

        if (order is null)
        {
            order = new Order
            {
                CTraderOrderId = protoOrder.OrderId,
                AccountId = account.Id,
                CreatedAt = DateTime.UtcNow
            };
            db.Orders.Add(order);
        }

        order.Symbol = symbolName;
        order.Type = MapOrderType(protoOrder.OrderType);
        order.Direction = protoOrder.TradeData.TradeSide == ProtoOATradeSide.Buy
            ? TradeDirection.Buy : TradeDirection.Sell;
        order.Volume = CTraderConversions.CentsToLots(protoOrder.TradeData.Volume);
        order.LimitPrice = protoOrder.HasLimitPrice ? (decimal?)protoOrder.LimitPrice : null;
        order.StopPrice = protoOrder.HasStopPrice ? (decimal?)protoOrder.StopPrice : null;
        order.StopLoss = protoOrder.HasStopLoss ? (decimal?)protoOrder.StopLoss : null;
        order.TakeProfit = protoOrder.HasTakeProfit ? (decimal?)protoOrder.TakeProfit : null;
        order.Status = MapOrderStatus(protoOrder.OrderStatus);

        // SL/TP amendments arrive as OrderAccepted/OrderReplaced with position data.
        // Update the position's SL/TP so it stays in sync without waiting for reconciliation.
        if (evt.Position != null)
        {
            var protoPos = evt.Position;
            var position = await db.Positions.FirstOrDefaultAsync(
                p => p.CTraderPositionId == protoPos.PositionId);
            if (position != null)
            {
                position.StopLoss = protoPos.HasStopLoss ? (decimal?)protoPos.StopLoss : null;
                position.TakeProfit = protoPos.HasTakeProfit ? (decimal?)protoPos.TakeProfit : null;
                position.UpdatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task HandleOrderTerminatedAsync(ProtoOAExecutionEvent evt)
    {
        if (evt.Order == null) return;

        var protoOrder = evt.Order;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = await db.Orders.FirstOrDefaultAsync(
            o => o.CTraderOrderId == protoOrder.OrderId);

        if (order is not null)
        {
            order.Status = evt.ExecutionType switch
            {
                ProtoOAExecutionType.OrderCancelled => OrderStatus.Cancelled,
                ProtoOAExecutionType.OrderExpired => OrderStatus.Expired,
                ProtoOAExecutionType.OrderRejected => OrderStatus.Rejected,
                _ => order.Status
            };
            order.CancelledAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    private async Task HandlePositionSwapAsync(ProtoOAExecutionEvent evt)
    {
        if (evt.Position == null) return;

        var protoPos = evt.Position;
        var moneyDigits = protoPos.HasMoneyDigits ? (int)protoPos.MoneyDigits : 2;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var position = await db.Positions.FirstOrDefaultAsync(
            p => p.CTraderPositionId == protoPos.PositionId);

        if (position is not null)
        {
            position.Swap = CTraderConversions.MoneyToDecimal(protoPos.Swap, moneyDigits);
            position.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    private async Task BroadcastPositionPnLAsync(string symbol, decimal currentPrice)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var positions = await db.Positions
            .Where(p => p.Symbol == symbol && p.Status == PositionStatus.Open)
            .ToListAsync();

        if (positions.Count == 0) return;

        foreach (var position in positions)
        {
            // Throttle: max once per 2 seconds per position
            var now = DateTime.UtcNow;
            if (_lastPnLBroadcast.TryGetValue(position.Id, out var lastBroadcast)
                && now - lastBroadcast < PnLThrottleInterval)
                continue;

            _lastPnLBroadcast[position.Id] = now;

            // Calculate unrealized PnL
            var pnl = position.Direction == TradeDirection.Buy
                ? (currentPrice - position.EntryPrice) * position.Volume * 100_000m
                : (position.EntryPrice - currentPrice) * position.Volume * 100_000m;

            // Update DB
            position.CurrentPrice = currentPrice;
            position.UnrealizedPnL = pnl;
            position.UpdatedAt = now;

            // Broadcast via SignalR
            await _hubContext.Clients.All.ReceivePositionUpdate(new PositionUpdate(
                position.CTraderPositionId,
                position.Symbol,
                position.Direction.ToString(),
                position.Volume,
                position.EntryPrice,
                currentPrice,
                pnl));
        }

        await db.SaveChangesAsync();
    }

    private async Task HandlePositionOpened(PositionEventArgs args)
    {
        _logger.LogInformation("Position opened: {Symbol} {Direction} {Volume}",
            args.Symbol, args.Direction, args.Volume);

        OnPositionOpened?.Invoke(this, args);

        await _hubContext.Clients.All.ReceivePositionUpdate(new PositionUpdate(
            args.PositionId,
            args.Symbol,
            args.Direction,
            args.Volume,
            args.EntryPrice,
            args.CurrentPrice,
            args.PnL));
    }

    private async Task HandlePositionClosed(PositionEventArgs args)
    {
        OnPositionClosed?.Invoke(this, args);

        await _hubContext.Clients.All.ReceiveTradeExecuted(new TradeNotification(
            args.PositionId,
            args.Symbol,
            args.Direction,
            args.Volume,
            args.EntryPrice,
            args.CurrentPrice,
            args.PnL));
    }

    private static OrderType MapOrderType(ProtoOAOrderType proto) => proto switch
    {
        ProtoOAOrderType.Market => OrderType.Market,
        ProtoOAOrderType.Limit => OrderType.Limit,
        ProtoOAOrderType.Stop => OrderType.Stop,
        ProtoOAOrderType.StopLimit => OrderType.StopLimit,
        _ => OrderType.Market
    };

    private static OrderStatus MapOrderStatus(ProtoOAOrderStatus proto) => proto switch
    {
        ProtoOAOrderStatus.OrderStatusAccepted => OrderStatus.Pending,
        ProtoOAOrderStatus.OrderStatusFilled => OrderStatus.Filled,
        ProtoOAOrderStatus.OrderStatusRejected => OrderStatus.Rejected,
        ProtoOAOrderStatus.OrderStatusExpired => OrderStatus.Expired,
        ProtoOAOrderStatus.OrderStatusCancelled => OrderStatus.Cancelled,
        _ => OrderStatus.Pending
    };
}

public class PositionEventArgs : EventArgs
{
    public long PositionId { get; init; }
    public long AccountId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal Volume { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal CurrentPrice { get; init; }
    public decimal PnL { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
    public DateTime OpenTime { get; init; }
    public decimal Commission { get; init; }
    public decimal Swap { get; init; }
}

public class OrderEventArgs : EventArgs
{
    public long OrderId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string OrderType { get; init; } = string.Empty;
    public decimal Volume { get; init; }
    public decimal ExecutionPrice { get; init; }
}
