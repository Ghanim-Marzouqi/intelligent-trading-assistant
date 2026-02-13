using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenAPI.Net;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Trading;

namespace TradingAssistant.Api.Services.CTrader;

public interface ICTraderOrderExecutor
{
    Task<OrderResult> PlaceMarketOrderAsync(string symbol, string direction, decimal volume, decimal? stopLoss, decimal? takeProfit);
    Task<OrderResult> PlaceLimitOrderAsync(string symbol, string direction, decimal volume, decimal price, decimal? stopLoss, decimal? takeProfit);
    Task<OrderResult> PlaceStopOrderAsync(string symbol, string direction, decimal volume, decimal price, decimal? stopLoss, decimal? takeProfit);
    Task<OrderResult> ModifyPositionAsync(long positionId, decimal? stopLoss, decimal? takeProfit);
    Task<OrderResult> ClosePositionAsync(long positionId);
    Task<OrderResult> CancelOrderAsync(long orderId);
}

public class CTraderOrderExecutor : ICTraderOrderExecutor
{
    private readonly ICTraderConnectionManager _connectionManager;
    private readonly ICTraderSymbolResolver _symbolResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CTraderOrderExecutor> _logger;

    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(30);

    public CTraderOrderExecutor(
        ICTraderConnectionManager connectionManager,
        ICTraderSymbolResolver symbolResolver,
        IServiceScopeFactory scopeFactory,
        ILogger<CTraderOrderExecutor> logger)
    {
        _connectionManager = connectionManager;
        _symbolResolver = symbolResolver;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<OrderResult> PlaceMarketOrderAsync(
        string symbol,
        string direction,
        decimal volume,
        decimal? stopLoss,
        decimal? takeProfit)
    {
        _logger.LogInformation("Placing market order: {Symbol} {Direction} {Volume}",
            symbol, direction, volume);

        return await PlaceOrderInternalAsync(
            symbol, direction, volume, ProtoOAOrderType.Market,
            limitPrice: null, stopPrice: null, stopLoss, takeProfit);
    }

    public async Task<OrderResult> PlaceLimitOrderAsync(
        string symbol,
        string direction,
        decimal volume,
        decimal price,
        decimal? stopLoss,
        decimal? takeProfit)
    {
        _logger.LogInformation("Placing limit order: {Symbol} {Direction} {Volume} @ {Price}",
            symbol, direction, volume, price);

        return await PlaceOrderInternalAsync(
            symbol, direction, volume, ProtoOAOrderType.Limit,
            limitPrice: price, stopPrice: null, stopLoss, takeProfit);
    }

    public async Task<OrderResult> PlaceStopOrderAsync(
        string symbol,
        string direction,
        decimal volume,
        decimal price,
        decimal? stopLoss,
        decimal? takeProfit)
    {
        _logger.LogInformation("Placing stop order: {Symbol} {Direction} {Volume} @ {Price}",
            symbol, direction, volume, price);

        return await PlaceOrderInternalAsync(
            symbol, direction, volume, ProtoOAOrderType.Stop,
            limitPrice: null, stopPrice: price, stopLoss, takeProfit);
    }

    public async Task<OrderResult> ModifyPositionAsync(long positionId, decimal? stopLoss, decimal? takeProfit)
    {
        _logger.LogInformation("Modifying position {PositionId}: SL={StopLoss}, TP={TakeProfit}",
            positionId, stopLoss, takeProfit);

        try
        {
            var client = await _connectionManager.GetClientAsync();
            var accountId = _connectionManager.AccountId;

            // positionId is already the cTrader position ID (controller resolves it)
            var req = new ProtoOAAmendPositionSLTPReq
            {
                CtidTraderAccountId = accountId,
                PositionId = positionId
            };

            if (stopLoss.HasValue)
                req.StopLoss = (double)stopLoss.Value;
            if (takeProfit.HasValue)
                req.TakeProfit = (double)takeProfit.Value;

            // Subscribe BEFORE sending to avoid race condition
            var responseTask = client.OfType<ProtoOAExecutionEvent>()
                .Where(e => e.Position != null && e.Position.PositionId == positionId)
                .Timeout(ResponseTimeout)
                .FirstAsync()
                .ToTask();

            await client.SendMessage(req, ProtoOAPayloadType.ProtoOaAmendPositionSltpReq);

            await responseTask;

            return new OrderResult
            {
                Success = true,
                PositionId = positionId
            };
        }
        catch (TimeoutException)
        {
            return new OrderResult
            {
                Success = false,
                ErrorMessage = "Timeout waiting for position modification response"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to modify position {PositionId}", positionId);
            return new OrderResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<OrderResult> ClosePositionAsync(long positionId)
    {
        _logger.LogInformation("Closing position {PositionId}", positionId);

        try
        {
            var client = await _connectionManager.GetClientAsync();
            var accountId = _connectionManager.AccountId;

            // Resolve the cTrader position ID and get volume
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var position = await db.Positions.FirstOrDefaultAsync(p => p.CTraderPositionId == positionId);
            if (position is null)
                return new OrderResult { Success = false, ErrorMessage = "Position not found" };

            var req = new ProtoOAClosePositionReq
            {
                CtidTraderAccountId = accountId,
                PositionId = positionId,
                Volume = CTraderConversions.LotsToCents(position.Volume)
            };

            // Subscribe BEFORE sending to avoid race condition
            var responseTask = client.OfType<ProtoOAExecutionEvent>()
                .Where(e => e.Position != null && e.Position.PositionId == positionId)
                .Timeout(ResponseTimeout)
                .FirstAsync()
                .ToTask();

            await client.SendMessage(req, ProtoOAPayloadType.ProtoOaClosePositionReq);

            await responseTask;

            return new OrderResult
            {
                Success = true,
                PositionId = positionId
            };
        }
        catch (TimeoutException)
        {
            return new OrderResult
            {
                Success = false,
                ErrorMessage = "Timeout waiting for close position response"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close position {PositionId}", positionId);
            return new OrderResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<OrderResult> CancelOrderAsync(long orderId)
    {
        _logger.LogInformation("Cancelling order {OrderId}", orderId);

        try
        {
            var client = await _connectionManager.GetClientAsync();
            var accountId = _connectionManager.AccountId;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order is null)
                return new OrderResult { Success = false, ErrorMessage = "Order not found" };

            var req = new ProtoOACancelOrderReq
            {
                CtidTraderAccountId = accountId,
                OrderId = order.CTraderOrderId
            };

            // Subscribe BEFORE sending to avoid race condition
            var responseTask = client.OfType<ProtoOAExecutionEvent>()
                .Where(e => e.Order != null && e.Order.OrderId == order.CTraderOrderId)
                .Timeout(ResponseTimeout)
                .FirstAsync()
                .ToTask();

            await client.SendMessage(req, ProtoOAPayloadType.ProtoOaCancelOrderReq);

            await responseTask;

            return new OrderResult
            {
                Success = true,
                OrderId = orderId
            };
        }
        catch (TimeoutException)
        {
            return new OrderResult
            {
                Success = false,
                ErrorMessage = "Timeout waiting for cancel order response"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel order {OrderId}", orderId);
            return new OrderResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<OrderResult> PlaceOrderInternalAsync(
        string symbol, string direction, decimal volume, ProtoOAOrderType orderType,
        decimal? limitPrice, decimal? stopPrice, decimal? stopLoss, decimal? takeProfit)
    {
        try
        {
            var client = await _connectionManager.GetClientAsync();
            var accountId = _connectionManager.AccountId;
            var symbolId = _symbolResolver.GetSymbolId(symbol);

            var tradeSide = direction.Equals("Buy", StringComparison.OrdinalIgnoreCase)
                ? ProtoOATradeSide.Buy
                : ProtoOATradeSide.Sell;

            var req = new ProtoOANewOrderReq
            {
                CtidTraderAccountId = accountId,
                SymbolId = symbolId,
                OrderType = orderType,
                TradeSide = tradeSide,
                Volume = CTraderConversions.LotsToCents(volume)
            };

            if (limitPrice.HasValue)
                req.LimitPrice = (double)limitPrice.Value;
            if (stopPrice.HasValue)
                req.StopPrice = (double)stopPrice.Value;

            // For non-market orders, SL/TP can be absolute prices.
            // For market orders, SL/TP will be set after fill via position modification.
            if (orderType != ProtoOAOrderType.Market)
            {
                if (stopLoss.HasValue)
                    req.StopLoss = (double)stopLoss.Value;
                if (takeProfit.HasValue)
                    req.TakeProfit = (double)takeProfit.Value;
            }

            // Subscribe to both execution events AND order errors BEFORE sending
            // to avoid race conditions and catch rejections.
            // For market orders, wait for OrderFilled (not just OrderAccepted)
            // so the position is ready for SL/TP modification.
            var executionTask = client.OfType<ProtoOAExecutionEvent>()
                .Where(e => e.Order != null && e.Order.TradeData.SymbolId == symbolId
                    && (orderType != ProtoOAOrderType.Market
                        || e.ExecutionType == ProtoOAExecutionType.OrderFilled
                        || e.ExecutionType == ProtoOAExecutionType.OrderPartialFill
                        || e.ExecutionType == ProtoOAExecutionType.OrderRejected))
                .Select(e => (object)e)
                .FirstAsync()
                .ToTask();

            var errorTask = client.OfType<ProtoOAOrderErrorEvent>()
                .Select(e => (object)e)
                .FirstAsync()
                .ToTask();

            _logger.LogDebug("Sending {OrderType} order for {Symbol} (ID={SymbolId}), volume={Volume} cents",
                orderType, symbol, symbolId, CTraderConversions.LotsToCents(volume));

            await client.SendMessage(req, ProtoOAPayloadType.ProtoOaNewOrderReq);

            // Wait for whichever comes first: execution event or error, with timeout
            var completedTask = await Task.WhenAny(executionTask, errorTask, Task.Delay(ResponseTimeout));

            if (completedTask == errorTask)
            {
                var orderError = (ProtoOAOrderErrorEvent)await errorTask;
                _logger.LogWarning("Order rejected: {ErrorCode} - {Description}",
                    orderError.ErrorCode, orderError.Description);
                return new OrderResult
                {
                    Success = false,
                    ErrorMessage = $"{orderError.ErrorCode}: {orderError.Description}",
                    ErrorCode = orderError.ErrorCode
                };
            }

            if (completedTask != executionTask)
            {
                return new OrderResult
                {
                    Success = false,
                    ErrorMessage = "Timeout waiting for order execution response"
                };
            }

            var response = (ProtoOAExecutionEvent)await executionTask;

            if (response.HasErrorCode)
            {
                return new OrderResult
                {
                    Success = false,
                    ErrorMessage = response.ErrorCode,
                    ErrorCode = response.ErrorCode
                };
            }

            var result = new OrderResult
            {
                Success = true,
                OrderId = response.Order?.OrderId,
                PositionId = response.Position?.PositionId
            };

            // For market orders, set SL/TP via position modification after fill
            if (orderType == ProtoOAOrderType.Market && response.Position != null
                && (stopLoss.HasValue || takeProfit.HasValue))
            {
                try
                {
                    var slTpReq = new ProtoOAAmendPositionSLTPReq
                    {
                        CtidTraderAccountId = accountId,
                        PositionId = response.Position.PositionId
                    };
                    if (stopLoss.HasValue)
                        slTpReq.StopLoss = (double)stopLoss.Value;
                    if (takeProfit.HasValue)
                        slTpReq.TakeProfit = (double)takeProfit.Value;

                    var slTpResponseTask = client.OfType<ProtoOAExecutionEvent>()
                        .Where(e => e.Position != null
                            && e.Position.PositionId == response.Position.PositionId)
                        .Timeout(ResponseTimeout)
                        .FirstAsync()
                        .ToTask();

                    var slTpErrorTask = client.OfType<ProtoOAOrderErrorEvent>()
                        .Timeout(ResponseTimeout)
                        .FirstAsync()
                        .ToTask();

                    await client.SendMessage(slTpReq, ProtoOAPayloadType.ProtoOaAmendPositionSltpReq);

                    var slTpCompleted = await Task.WhenAny(slTpResponseTask, slTpErrorTask);
                    if (slTpCompleted == slTpErrorTask)
                    {
                        var slTpError = await slTpErrorTask;
                        _logger.LogWarning("Failed to set SL/TP: {ErrorCode} - {Description}",
                            slTpError.ErrorCode, slTpError.Description);
                        throw new InvalidOperationException($"{slTpError.ErrorCode}: {slTpError.Description}");
                    }
                    await slTpResponseTask;

                    _logger.LogInformation("Set SL/TP on position {PositionId}: SL={StopLoss}, TP={TakeProfit}",
                        response.Position.PositionId, stopLoss, takeProfit);

                    // Update DB immediately so the position shows SL/TP without waiting for reconciliation
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var dbPosition = await db.Positions
                            .FirstOrDefaultAsync(p => p.CTraderPositionId == response.Position.PositionId);
                        if (dbPosition != null)
                        {
                            if (stopLoss.HasValue) dbPosition.StopLoss = stopLoss;
                            if (takeProfit.HasValue) dbPosition.TakeProfit = takeProfit;
                            dbPosition.UpdatedAt = DateTime.UtcNow;
                            await db.SaveChangesAsync();
                        }
                    }
                    catch (Exception dbEx)
                    {
                        _logger.LogWarning(dbEx, "Failed to update SL/TP in database (will sync on next reconciliation)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Order filled but failed to set SL/TP on position {PositionId}",
                        response.Position.PositionId);
                    // Order still succeeded, just SL/TP failed — report partial success
                    return new OrderResult
                    {
                        Success = true,
                        OrderId = result.OrderId,
                        PositionId = result.PositionId,
                        ErrorMessage = "Order filled but failed to set SL/TP — set them manually"
                    };
                }
            }

            return result;
        }
        catch (TimeoutException)
        {
            return new OrderResult
            {
                Success = false,
                ErrorMessage = "Timeout waiting for order execution response"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to place {OrderType} order for {Symbol}", orderType, symbol);
            return new OrderResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

}

public class OrderResult
{
    public bool Success { get; init; }
    public long? OrderId { get; init; }
    public long? PositionId { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
}
