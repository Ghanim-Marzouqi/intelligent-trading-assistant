using Microsoft.AspNetCore.SignalR;
using TradingAssistant.Api.Hubs;

namespace TradingAssistant.Api.Services.CTrader;

public interface ICTraderAccountStream
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    event EventHandler<PositionEventArgs>? OnPositionOpened;
    event EventHandler<PositionEventArgs>? OnPositionClosed;
    event EventHandler<PositionEventArgs>? OnPositionModified;
    event EventHandler<OrderEventArgs>? OnOrderFilled;
}

public class CTraderAccountStream : ICTraderAccountStream
{
    private readonly IHubContext<TradingHub, ITradingHubClient> _hubContext;
    private readonly ILogger<CTraderAccountStream> _logger;

    public event EventHandler<PositionEventArgs>? OnPositionOpened;
    public event EventHandler<PositionEventArgs>? OnPositionClosed;
    public event EventHandler<PositionEventArgs>? OnPositionModified;
    public event EventHandler<OrderEventArgs>? OnOrderFilled;

    public CTraderAccountStream(
        IHubContext<TradingHub, ITradingHubClient> hubContext,
        ILogger<CTraderAccountStream> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Account stream starting...");

        // TODO: Subscribe to account events via cTrader gRPC
        // ProtoOASubscribeSpotsReq, ProtoOAExecutionEvent, etc.

        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Account stream stopping...");
        await Task.CompletedTask;
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
        _logger.LogInformation("Position closed: {Symbol} PnL={PnL}",
            args.Symbol, args.PnL);

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
}

public class PositionEventArgs : EventArgs
{
    public long PositionId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal Volume { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal CurrentPrice { get; init; }
    public decimal PnL { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
}

public class OrderEventArgs : EventArgs
{
    public long OrderId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string OrderType { get; init; } = string.Empty;
    public decimal Volume { get; init; }
    public decimal ExecutionPrice { get; init; }
}
