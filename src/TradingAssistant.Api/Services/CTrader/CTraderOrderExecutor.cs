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
    private readonly ICTraderAuthService _authService;
    private readonly ILogger<CTraderOrderExecutor> _logger;

    public CTraderOrderExecutor(
        ICTraderAuthService authService,
        ILogger<CTraderOrderExecutor> logger)
    {
        _authService = authService;
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

        // TODO: Send ProtoOANewOrderReq to cTrader via gRPC
        // OrderType = MARKET

        await Task.CompletedTask;

        return new OrderResult { Success = true, OrderId = 0 };
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

        // TODO: Send ProtoOANewOrderReq with OrderType = LIMIT

        await Task.CompletedTask;

        return new OrderResult { Success = true, OrderId = 0 };
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

        // TODO: Send ProtoOANewOrderReq with OrderType = STOP

        await Task.CompletedTask;

        return new OrderResult { Success = true, OrderId = 0 };
    }

    public async Task<OrderResult> ModifyPositionAsync(long positionId, decimal? stopLoss, decimal? takeProfit)
    {
        _logger.LogInformation("Modifying position {PositionId}: SL={StopLoss}, TP={TakeProfit}",
            positionId, stopLoss, takeProfit);

        // TODO: Send ProtoOAAmendPositionSLTPReq

        await Task.CompletedTask;

        return new OrderResult { Success = true };
    }

    public async Task<OrderResult> ClosePositionAsync(long positionId)
    {
        _logger.LogInformation("Closing position {PositionId}", positionId);

        // TODO: Send ProtoOAClosePositionReq

        await Task.CompletedTask;

        return new OrderResult { Success = true };
    }

    public async Task<OrderResult> CancelOrderAsync(long orderId)
    {
        _logger.LogInformation("Cancelling order {OrderId}", orderId);

        // TODO: Send ProtoOACancelOrderReq

        await Task.CompletedTask;

        return new OrderResult { Success = true };
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
