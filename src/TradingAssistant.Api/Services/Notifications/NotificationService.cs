using Microsoft.AspNetCore.SignalR;
using TradingAssistant.Api.Hubs;
using TradingAssistant.Api.Models.Alerts;
using TradingAssistant.Api.Services.CTrader;
using TradingAssistant.Api.Services.Orders;

namespace TradingAssistant.Api.Services.Notifications;

public interface INotificationService
{
    Task SendAlertAsync(AlertTrigger alert);
    Task SendOrderApprovalRequestAsync(PreparedOrder order);
    Task SendOrderExecutedAsync(PreparedOrder order, OrderResult result);
    Task SendOrderFailedAsync(PreparedOrder order, OrderResult result);
    Task SendDailySummaryAsync(DailySummary summary);
    Task SendMessageAsync(string message, NotificationChannel channels = NotificationChannel.All);
}

[Flags]
public enum NotificationChannel
{
    None = 0,
    Telegram = 1,
    WhatsApp = 2,
    Dashboard = 4,
    All = Telegram | WhatsApp | Dashboard
}

public class NotificationService : INotificationService
{
    private readonly IHubContext<TradingHub, ITradingHubClient> _hubContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IHubContext<TradingHub, ITradingHubClient> hubContext,
        IServiceProvider serviceProvider,
        ILogger<NotificationService> logger)
    {
        _hubContext = hubContext;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task SendAlertAsync(AlertTrigger alert)
    {
        _logger.LogInformation("Sending alert notification: {Symbol} at {Price}",
            alert.Symbol, alert.TriggerPrice);

        // Dashboard notification via SignalR
        await _hubContext.Clients.All.ReceiveAlert(new AlertNotification(
            alert.Id,
            alert.Symbol,
            alert.Message,
            "info",
            alert.TriggeredAt));

        // Telegram notification
        using var scope = _serviceProvider.CreateScope();
        var telegram = scope.ServiceProvider.GetRequiredService<TelegramBotService>();
        await telegram.SendAlertAsync(alert);

        // WhatsApp for critical alerts only (optional)
        // var whatsapp = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
        // await whatsapp.SendMessageAsync(FormatAlertMessage(alert));
    }

    public async Task SendOrderApprovalRequestAsync(PreparedOrder order)
    {
        _logger.LogInformation("Sending order approval request: {Symbol} {Direction}",
            order.Symbol, order.Direction);

        using var scope = _serviceProvider.CreateScope();
        var telegram = scope.ServiceProvider.GetRequiredService<TelegramBotService>();
        await telegram.SendOrderApprovalAsync(order);
    }

    public async Task SendOrderExecutedAsync(PreparedOrder order, OrderResult result)
    {
        var message = $"""
            Order Executed
            {order.Symbol} {order.Direction}
            Volume: {order.Volume} lots
            Order ID: {result.OrderId}
            """;

        await SendMessageAsync(message);
    }

    public async Task SendOrderFailedAsync(PreparedOrder order, OrderResult result)
    {
        var message = $"""
            Order Failed
            {order.Symbol} {order.Direction}
            Error: {result.ErrorMessage}
            """;

        await SendMessageAsync(message, NotificationChannel.Telegram);
    }

    public async Task SendDailySummaryAsync(DailySummary summary)
    {
        var message = $"""
            Daily Summary - {summary.Date:d}

            Trades: {summary.TotalTrades}
            Win Rate: {summary.WinRate:F1}%
            P&L: {summary.TotalPnL:+0.00;-0.00} ({summary.TotalPnLPercent:+0.00;-0.00}%)

            Best: {summary.BestTrade:+0.00}
            Worst: {summary.WorstTrade:+0.00}
            """;

        await SendMessageAsync(message);
    }

    public async Task SendMessageAsync(string message, NotificationChannel channels = NotificationChannel.All)
    {
        _logger.LogDebug("Sending notification: {Message}", message);

        if (channels.HasFlag(NotificationChannel.Dashboard))
        {
            // Could implement a general notification hub method
        }

        if (channels.HasFlag(NotificationChannel.Telegram))
        {
            using var scope = _serviceProvider.CreateScope();
            var telegram = scope.ServiceProvider.GetRequiredService<TelegramBotService>();
            await telegram.SendMessageAsync(message);
        }

        if (channels.HasFlag(NotificationChannel.WhatsApp))
        {
            using var scope = _serviceProvider.CreateScope();
            var whatsapp = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
            await whatsapp.SendMessageAsync(message);
        }
    }
}

public class DailySummary
{
    public DateTime Date { get; set; }
    public int TotalTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal TotalPnLPercent { get; set; }
    public decimal BestTrade { get; set; }
    public decimal WorstTrade { get; set; }
}
