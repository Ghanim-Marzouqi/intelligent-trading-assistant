using System.Reactive.Linq;
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

            // Resolve cTrader position ID from our DB ID
            var ctraderPositionId = await ResolveCTraderPositionIdAsync(positionId);

            var req = new ProtoOAAmendPositionSLTPReq
            {
                CtidTraderAccountId = accountId,
                PositionId = ctraderPositionId
            };

            if (stopLoss.HasValue)
                req.StopLoss = (double)stopLoss.Value;
            if (takeProfit.HasValue)
                req.TakeProfit = (double)takeProfit.Value;

            await client.SendMessage(req, ProtoOAPayloadType.ProtoOaAmendPositionSltpReq);

            var response = await client.OfType<ProtoOAExecutionEvent>()
                .Where(e => e.Position != null && e.Position.PositionId == ctraderPositionId)
                .Timeout(ResponseTimeout)
                .FirstAsync();

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

            var position = await db.Positions.FirstOrDefaultAsync(p => p.Id == positionId);
            if (position is null)
                return new OrderResult { Success = false, ErrorMessage = "Position not found" };

            var req = new ProtoOAClosePositionReq
            {
                CtidTraderAccountId = accountId,
                PositionId = position.CTraderPositionId,
                Volume = CTraderConversions.LotsToCents(position.Volume)
            };

            await client.SendMessage(req, ProtoOAPayloadType.ProtoOaClosePositionReq);

            var response = await client.OfType<ProtoOAExecutionEvent>()
                .Where(e => e.Position != null && e.Position.PositionId == position.CTraderPositionId)
                .Timeout(ResponseTimeout)
                .FirstAsync();

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

            await client.SendMessage(req, ProtoOAPayloadType.ProtoOaCancelOrderReq);

            var response = await client.OfType<ProtoOAExecutionEvent>()
                .Where(e => e.Order != null && e.Order.OrderId == order.CTraderOrderId)
                .Timeout(ResponseTimeout)
                .FirstAsync();

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
            if (stopLoss.HasValue)
                req.StopLoss = (double)stopLoss.Value;
            if (takeProfit.HasValue)
                req.TakeProfit = (double)takeProfit.Value;

            await client.SendMessage(req, ProtoOAPayloadType.ProtoOaNewOrderReq);

            // Wait for execution event
            var response = await client.OfType<ProtoOAExecutionEvent>()
                .Where(e => e.Order != null && e.Order.TradeData.SymbolId == symbolId)
                .Timeout(ResponseTimeout)
                .FirstAsync();

            if (response.HasErrorCode)
            {
                return new OrderResult
                {
                    Success = false,
                    ErrorMessage = response.ErrorCode,
                    ErrorCode = response.ErrorCode
                };
            }

            return new OrderResult
            {
                Success = true,
                OrderId = response.Order?.OrderId,
                PositionId = response.Position?.PositionId
            };
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

    private async Task<long> ResolveCTraderPositionIdAsync(long dbPositionId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var position = await db.Positions.FirstOrDefaultAsync(p => p.Id == dbPositionId);
        if (position is null)
            throw new InvalidOperationException($"Position {dbPositionId} not found in database");
        return position.CTraderPositionId;
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
