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

    private readonly IConfiguration _config;
    private IDisposable? _executionSubscription;
    private readonly ConcurrentDictionary<long, DateTime> _lastPnLBroadcast = new();
    private static readonly TimeSpan PnLThrottleInterval = TimeSpan.FromSeconds(2);

    // Margin monitoring state
    private readonly SemaphoreSlim _marginCheckLock = new(1, 1);
    private DateTime _lastMarginCheck = DateTime.MinValue;
    private DateTime _lastMarginWarning = DateTime.MinValue;
    private bool _stopOutInProgress;
    private static readonly TimeSpan MarginCheckInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MarginWarningCooldown = TimeSpan.FromSeconds(30);

    public event EventHandler<PositionEventArgs>? OnPositionOpened;
    public event EventHandler<PositionEventArgs>? OnPositionClosed;
    public event EventHandler<PositionEventArgs>? OnPositionModified;

    public CTraderAccountStream(
        ICTraderConnectionManager connectionManager,
        ICTraderSymbolResolver symbolResolver,
        ICTraderPriceStream priceStream,
        IHubContext<TradingHub, ITradingHubClient> hubContext,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<CTraderAccountStream> logger)
    {
        _connectionManager = connectionManager;
        _symbolResolver = symbolResolver;
        _priceStream = priceStream;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _config = config;
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

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // Handle deal if present (for recording in deals table)
            if (evt.Deal != null)
            {
                await UpsertDealAsync(db, account.Id, evt.Deal, symbolName, moneyDigits);
            }

            // Detect close: position status or deal with ClosePositionDetail
            var isClosing = protoPos.PositionStatus == ProtoOAPositionStatus.PositionStatusClosed
                || (evt.Deal?.ClosePositionDetail != null);

            if (isClosing)
            {
                await HandlePositionClosedInternalAsync(db, account.Id, protoPos, symbolName, moneyDigits, evt.Deal);
            }
            else
            {
                await HandlePositionOpenOrUpdateAsync(db, account.Id, protoPos, symbolName, moneyDigits);
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
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
        var contractSize = _symbolResolver.GetContractSize(symbolName);
        var newVolume = CTraderConversions.VolumeToLots(protoPos.TradeData.Volume, contractSize);
        var newPrice = protoPos.HasPrice ? (decimal)protoPos.Price : 0m;

        // Only overwrite volume/price if the incoming values are non-zero,
        // to avoid clobbering good data with empty values from amendments or partial events
        if (newVolume > 0 || isNew)
            position.Volume = newVolume;
        if (newPrice > 0 || isNew)
            position.EntryPrice = newPrice;
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
            var contractSize = _symbolResolver.GetContractSize(symbolName);
            var closeVolume = CTraderConversions.VolumeToLots(protoPos.TradeData.Volume, contractSize);
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
            using var journalScope = _scopeFactory.CreateScope();
            var journalService = journalScope.ServiceProvider
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
            Volume = CTraderConversions.VolumeToLots(protoDeal.FilledVolume, _symbolResolver.GetContractSize(symbolName)),
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
        order.Volume = CTraderConversions.VolumeToLots(protoOrder.TradeData.Volume, _symbolResolver.GetContractSize(symbolName));
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

        // Look up contract size for this symbol (all positions here share the same symbol)
        var contractSize = _symbolResolver.GetContractSize(symbol);

        var anyUpdated = false;
        foreach (var position in positions)
        {
            // Throttle: max once per 2 seconds per position
            var now = DateTime.UtcNow;
            if (_lastPnLBroadcast.TryGetValue(position.Id, out var lastBroadcast)
                && now - lastBroadcast < PnLThrottleInterval)
                continue;

            _lastPnLBroadcast[position.Id] = now;
            anyUpdated = true;

            // Calculate unrealized PnL: lots × contractSize × priceChange
            var pnl = position.Direction == TradeDirection.Buy
                ? (currentPrice - position.EntryPrice) * position.Volume * contractSize
                : (position.EntryPrice - currentPrice) * position.Volume * contractSize;

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

        // Broadcast account update and check margin after P&L changes
        if (anyUpdated)
        {
            await BroadcastAccountUpdateAsync(db);
            await EvaluateMarginAsync(db);
        }
    }

    private async Task BroadcastAccountUpdateAsync(AppDbContext db)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.IsActive);
        if (account is null) return;

        var allPositions = await db.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .ToListAsync();

        var unrealizedPnL = allPositions.Sum(p => p.UnrealizedPnL);
        var equity = account.Balance + unrealizedPnL;
        var leverage = account.Leverage > 0 ? account.Leverage : 1;
        var usedMargin = allPositions.Sum(p =>
        {
            var cs = _symbolResolver.GetContractSize(p.Symbol);
            return p.Volume * cs * p.EntryPrice / leverage;
        });
        var freeMargin = equity - usedMargin;

        // Update account in DB
        account.Equity = equity;
        account.UnrealizedPnL = unrealizedPnL;
        account.FreeMargin = freeMargin;
        account.Margin = usedMargin;
        account.MarginLevel = usedMargin > 0 ? (equity / usedMargin) * 100m : 0m;
        account.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await _hubContext.Clients.All.ReceiveAccountUpdate(new AccountUpdate(
            account.Balance, equity, unrealizedPnL, usedMargin, freeMargin,
            account.MarginLevel));
    }

    private async Task EvaluateMarginAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        if (now - _lastMarginCheck < MarginCheckInterval) return;
        if (_stopOutInProgress) return;
        if (!await _marginCheckLock.WaitAsync(0)) return;

        try
        {
            _lastMarginCheck = now;

            var account = await db.Accounts.FirstOrDefaultAsync(a => a.IsActive);
            if (account is null) return;

            var allPositions = await db.Positions
                .Where(p => p.Status == PositionStatus.Open)
                .ToListAsync();

            if (allPositions.Count == 0) return;

            var unrealizedPnL = allPositions.Sum(p => p.UnrealizedPnL);
            var equity = account.Balance + unrealizedPnL;
            var leverage = account.Leverage > 0 ? account.Leverage : 1;
            var usedMargin = allPositions.Sum(p =>
            {
                var cs = _symbolResolver.GetContractSize(p.Symbol);
                return p.Volume * cs * p.EntryPrice / leverage;
            });

            if (usedMargin <= 0) return;

            var marginLevel = (equity / usedMargin) * 100m;

            var stopOutLevel = _config.GetValue<decimal>("Risk:StopOutLevel", 50m);
            var marginCallLevel = _config.GetValue<decimal>("Risk:MarginCallLevel", 100m);

            // Stop-out: close largest losing position
            if (marginLevel <= stopOutLevel)
            {
                _stopOutInProgress = true;
                _logger.LogCritical(
                    "STOP-OUT triggered! Margin level {MarginLevel:F1}% <= {StopOutLevel}%. Equity={Equity:F2}, UsedMargin={UsedMargin:F2}",
                    marginLevel, stopOutLevel, equity, usedMargin);

                // Close position with largest loss first
                var worstPosition = allPositions
                    .OrderBy(p => p.UnrealizedPnL)
                    .First();

                await _hubContext.Clients.All.ReceiveMarginWarning(new MarginWarning(
                    "StopOut", marginLevel, equity, usedMargin, equity - usedMargin,
                    $"Stop-out at {marginLevel:F1}%. Closing {worstPosition.Symbol} ({worstPosition.UnrealizedPnL:F2} P&L)"));

                try
                {
                    using var closeScope = _scopeFactory.CreateScope();
                    var executor = closeScope.ServiceProvider.GetRequiredService<ICTraderOrderExecutor>();
                    var result = await executor.ClosePositionAsync(worstPosition.CTraderPositionId);

                    if (result.Success)
                    {
                        _logger.LogWarning(
                            "Stop-out closed position {PositionId} ({Symbol}, P&L={PnL:F2}). Margin level was {MarginLevel:F1}%",
                            worstPosition.CTraderPositionId, worstPosition.Symbol,
                            worstPosition.UnrealizedPnL, marginLevel);
                    }
                    else
                    {
                        _logger.LogError("Stop-out failed to close position {PositionId}: {Error}",
                            worstPosition.CTraderPositionId, result.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Stop-out exception closing position {PositionId}",
                        worstPosition.CTraderPositionId);
                }
                finally
                {
                    _stopOutInProgress = false;
                }
            }
            // Margin call warning
            else if (marginLevel <= marginCallLevel && now - _lastMarginWarning > MarginWarningCooldown)
            {
                _lastMarginWarning = now;
                _logger.LogWarning(
                    "Margin call warning! Margin level {MarginLevel:F1}% <= {MarginCallLevel}%. Equity={Equity:F2}, UsedMargin={UsedMargin:F2}",
                    marginLevel, marginCallLevel, equity, usedMargin);

                await _hubContext.Clients.All.ReceiveMarginWarning(new MarginWarning(
                    "MarginCall", marginLevel, equity, usedMargin, equity - usedMargin,
                    $"Margin call warning! Margin level at {marginLevel:F1}%. Consider closing positions."));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating margin level");
            _stopOutInProgress = false;
        }
        finally
        {
            _marginCheckLock.Release();
        }
    }

    private async Task HandlePositionOpened(PositionEventArgs args)
    {
        _logger.LogInformation("Position opened: {Symbol} {Direction} {Volume}",
            args.Symbol, args.Direction, args.Volume);

        // Ensure the price stream is subscribed to this symbol so P&L updates flow
        await _priceStream.SubscribeAsync(args.Symbol);

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
