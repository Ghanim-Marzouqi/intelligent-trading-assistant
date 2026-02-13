using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenAPI.Net;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Trading;

namespace TradingAssistant.Api.Services.CTrader;

public class CTraderApiAdapter : BackgroundService
{
    private readonly ICTraderConnectionManager _connectionManager;
    private readonly ICTraderSymbolResolver _symbolResolver;
    private readonly ICTraderPriceStream _priceStream;
    private readonly ICTraderAccountStream _accountStream;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CTraderApiAdapter> _logger;
    private readonly ReconnectionPolicy _reconnectionPolicy;

    public CTraderApiAdapter(
        ICTraderConnectionManager connectionManager,
        ICTraderSymbolResolver symbolResolver,
        ICTraderPriceStream priceStream,
        ICTraderAccountStream accountStream,
        IServiceScopeFactory scopeFactory,
        ILogger<CTraderApiAdapter> logger)
    {
        _connectionManager = connectionManager;
        _symbolResolver = symbolResolver;
        _priceStream = priceStream;
        _accountStream = accountStream;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _reconnectionPolicy = new ReconnectionPolicy();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("cTrader API Adapter starting...");

        // Wait briefly so the rest of the app can initialize
        await Task.Delay(2000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Step 1: Establish connection (handles auth)
                var client = await _connectionManager.GetClientAsync(stoppingToken);
                var accountId = _connectionManager.AccountId;

                // Step 2: Initialize symbol resolver
                await _symbolResolver.InitializeAsync(client, accountId, stoppingToken);

                // Step 3: Initial sync — account info
                await SyncAccountAsync(client, accountId, stoppingToken);

                // Step 4: Initial sync — reconcile positions and orders
                await ReconcileAsync(client, accountId, stoppingToken);

                // Step 5: Start streams
                await _priceStream.StartAsync(stoppingToken);
                await _accountStream.StartAsync(stoppingToken);

                // Step 6: Subscribe to prices for open positions
                await SubscribeOpenPositionPricesAsync(stoppingToken);

                _reconnectionPolicy.Reset();

                _logger.LogInformation("cTrader API Adapter fully connected and streaming");

                // Step 7: Periodic sync loop
                await PeriodicSyncLoopAsync(client, accountId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("OAuth tokens"))
            {
                // No tokens yet — wait for the user to authorize via /api/auth/ctrader
                _logger.LogWarning("Waiting for cTrader OAuth authorization. " +
                    "Visit http://localhost:5000/api/auth/ctrader to authorize.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "cTrader connection error");

                try
                {
                    await _priceStream.StopAsync();
                    await _accountStream.StopAsync();
                }
                catch { }

                var delay = _reconnectionPolicy.GetNextDelay();
                _logger.LogWarning("Reconnecting in {Delay}...", delay);

                try
                {
                    await _connectionManager.DisconnectAsync();
                }
                catch { }

                await Task.Delay(delay, stoppingToken);
            }
        }

        _logger.LogInformation("cTrader API Adapter stopped");
    }

    private async Task SyncAccountAsync(OpenClient client, long accountId, CancellationToken ct)
    {
        _logger.LogInformation("Syncing account info...");

        var traderReq = new ProtoOATraderReq { CtidTraderAccountId = accountId };
        await client.SendMessage(traderReq, ProtoOAPayloadType.ProtoOaTraderReq);

        var traderRes = await client.OfType<ProtoOATraderRes>()
            .FirstAsync()
            .ToTask(ct);

        var trader = traderRes.Trader;
        var moneyDigits = trader.HasMoneyDigits ? (int)trader.MoneyDigits : 2;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = await db.Accounts.FirstOrDefaultAsync(
            a => a.CTraderAccountId == accountId, ct);

        if (account is null)
        {
            account = new Account
            {
                CTraderAccountId = accountId,
                CreatedAt = DateTime.UtcNow
            };
            db.Accounts.Add(account);
        }

        account.Balance = CTraderConversions.MoneyToDecimal(trader.Balance, moneyDigits);

        // Compute equity from open positions; default to balance when none exist
        var openPositions = await db.Positions
            .Where(p => p.AccountId == account.Id && p.Status == PositionStatus.Open)
            .ToListAsync(ct);
        var unrealizedPnL = openPositions.Sum(p => p.UnrealizedPnL);
        account.UnrealizedPnL = unrealizedPnL;
        account.Equity = account.Balance + unrealizedPnL;
        account.FreeMargin = account.Equity - openPositions.Sum(p => p.Volume * 1000m);

        account.Currency = trader.HasDepositAssetId
            ? _symbolResolver.GetAssetName(trader.DepositAssetId)
            : "USD";
        account.Leverage = trader.HasLeverageInCents ? (int)(trader.LeverageInCents / 100) : 0;
        account.IsLive = false; // demo environment
        account.IsActive = true;
        account.LastSyncAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        if (trader.HasTraderLogin)
            account.AccountNumber = trader.TraderLogin.ToString();

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Account synced. Balance={Balance} {Currency}",
            account.Balance, account.Currency);
    }

    private async Task ReconcileAsync(OpenClient client, long accountId, CancellationToken ct)
    {
        _logger.LogInformation("Reconciling positions and orders...");

        var reconcileReq = new ProtoOAReconcileReq { CtidTraderAccountId = accountId };
        await client.SendMessage(reconcileReq, ProtoOAPayloadType.ProtoOaReconcileReq);

        var reconcileRes = await client.OfType<ProtoOAReconcileRes>()
            .FirstAsync()
            .ToTask(ct);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.Accounts.FirstOrDefaultAsync(
            a => a.CTraderAccountId == accountId, ct);

        if (account is null) return;

        // Sync positions
        var openPositionIds = new HashSet<long>();
        foreach (var protoPos in reconcileRes.Position)
        {
            openPositionIds.Add(protoPos.PositionId);
            var symbolName = _symbolResolver.GetSymbolName(protoPos.TradeData.SymbolId);
            var moneyDigits = protoPos.HasMoneyDigits ? (int)protoPos.MoneyDigits : 2;

            var position = await db.Positions.FirstOrDefaultAsync(
                p => p.CTraderPositionId == protoPos.PositionId, ct);

            if (position is null)
            {
                position = new Position
                {
                    CTraderPositionId = protoPos.PositionId,
                    AccountId = account.Id,
                    CreatedAt = DateTime.UtcNow
                };
                db.Positions.Add(position);
            }

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
        }

        // Mark positions no longer open as closed
        var existingOpen = await db.Positions
            .Where(p => p.AccountId == account.Id && p.Status == PositionStatus.Open)
            .ToListAsync(ct);

        foreach (var pos in existingOpen)
        {
            if (!openPositionIds.Contains(pos.CTraderPositionId))
            {
                pos.Status = PositionStatus.Closed;
                pos.CloseTime = DateTime.UtcNow;
                pos.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Sync pending orders
        foreach (var protoOrder in reconcileRes.Order)
        {
            var symbolName = _symbolResolver.GetSymbolName(protoOrder.TradeData.SymbolId);

            var order = await db.Orders.FirstOrDefaultAsync(
                o => o.CTraderOrderId == protoOrder.OrderId, ct);

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
            order.Comment = protoOrder.TradeData.HasComment ? protoOrder.TradeData.Comment : null;
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Reconciled {Positions} positions and {Orders} orders",
            reconcileRes.Position.Count, reconcileRes.Order.Count);
    }

    private async Task SubscribeOpenPositionPricesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var openSymbols = await db.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .Select(p => p.Symbol)
            .Distinct()
            .ToListAsync(ct);

        foreach (var symbol in openSymbols)
        {
            await _priceStream.SubscribeAsync(symbol);
        }

        _logger.LogInformation("Subscribed to prices for {Count} symbols with open positions",
            openSymbols.Count);
    }

    private async Task PeriodicSyncLoopAsync(OpenClient client, long accountId, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await SyncAccountAsync(client, accountId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Periodic account sync failed");
                throw; // trigger reconnect
            }
        }
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

public class ReconnectionPolicy
{
    private int _attemptCount;
    private readonly int _maxDelaySeconds = 60;

    public TimeSpan GetNextDelay()
    {
        var delay = Math.Min(Math.Pow(2, _attemptCount), _maxDelaySeconds);
        _attemptCount++;
        return TimeSpan.FromSeconds(delay);
    }

    public void Reset() => _attemptCount = 0;
}
