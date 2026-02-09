using Microsoft.AspNetCore.SignalR;

namespace TradingAssistant.Api.Hubs;

public interface ITradingHubClient
{
    Task ReceivePriceUpdate(PriceUpdate update);
    Task ReceiveAlert(AlertNotification alert);
    Task ReceivePositionUpdate(PositionUpdate position);
    Task ReceiveAccountUpdate(AccountUpdate account);
    Task ReceiveTradeExecuted(TradeNotification trade);
}

public class TradingHub : Hub<ITradingHubClient>
{
    private readonly ILogger<TradingHub> _logger;

    public TradingHub(ILogger<TradingHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SubscribeToSymbol(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"symbol:{symbol}");
        _logger.LogDebug("Client {ConnectionId} subscribed to {Symbol}", Context.ConnectionId, symbol);
    }

    public async Task UnsubscribeFromSymbol(string symbol)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"symbol:{symbol}");
        _logger.LogDebug("Client {ConnectionId} unsubscribed from {Symbol}", Context.ConnectionId, symbol);
    }
}

public record PriceUpdate(string Symbol, decimal Bid, decimal Ask, DateTime Timestamp);
public record AlertNotification(long AlertId, string Symbol, string Message, string Severity, DateTime TriggeredAt);
public record PositionUpdate(long PositionId, string Symbol, string Direction, decimal Volume, decimal EntryPrice, decimal CurrentPrice, decimal PnL);
public record AccountUpdate(decimal Balance, decimal Equity, decimal Margin, decimal FreeMargin, decimal MarginLevel);
public record TradeNotification(long TradeId, string Symbol, string Direction, decimal Volume, decimal EntryPrice, decimal? ExitPrice, decimal PnL);
