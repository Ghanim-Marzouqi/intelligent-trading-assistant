using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TradingAssistant.Api.Models.Alerts;
using TradingAssistant.Api.Services.Orders;

namespace TradingAssistant.Api.Services.Notifications;

public class TelegramBotService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TelegramBotService> _logger;
    private TelegramBotClient? _botClient;
    private long _authorizedChatId;

    public TelegramBotService(
        IConfiguration config,
        IServiceProvider serviceProvider,
        ILogger<TelegramBotService> logger)
    {
        _config = config;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = _config["Telegram:BotToken"];
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Telegram bot token not configured");
            return;
        }

        _authorizedChatId = _config.GetValue<long>("Telegram:ChatId");
        _botClient = new TelegramBotClient(token);

        var me = await _botClient.GetMeAsync(stoppingToken);
        _logger.LogInformation("Telegram bot started: @{Username}", me.Username);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
        };

        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message is { } message)
            {
                if (message.Chat.Id != _authorizedChatId)
                {
                    _logger.LogWarning("Unauthorized chat: {ChatId}", message.Chat.Id);
                    return;
                }

                await HandleCommandAsync(message, ct);
            }
            else if (update.CallbackQuery is { } callback)
            {
                if (callback.Message?.Chat.Id != _authorizedChatId)
                    return;

                await HandleCallbackAsync(callback, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Telegram update");
        }
    }

    private async Task HandleCommandAsync(Message message, CancellationToken ct)
    {
        var text = message.Text ?? string.Empty;
        var command = text.Split(' ')[0].ToLowerInvariant();

        var response = command switch
        {
            "/status" => await GetStatusAsync(),
            "/positions" => await GetPositionsAsync(),
            "/alerts" => await GetAlertsAsync(),
            "/today" => await GetTodaySummaryAsync(),
            "/week" => await GetWeekSummaryAsync(),
            "/calendar" => await GetCalendarAsync(),
            "/help" => GetHelpText(),
            _ when text.StartsWith("/alert ") => await CreateQuickAlertAsync(text[7..]),
            _ => null
        };

        if (response is not null)
        {
            await _botClient!.SendTextMessageAsync(
                message.Chat.Id,
                response,
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
        }
    }

    private async Task HandleCallbackAsync(CallbackQuery callback, CancellationToken ct)
    {
        var data = callback.Data ?? string.Empty;

        if (data.StartsWith("approve:"))
        {
            var token = data[8..];
            using var scope = _serviceProvider.CreateScope();
            var orderManager = scope.ServiceProvider.GetRequiredService<IOrderManager>();

            var success = await orderManager.ApproveOrderAsync(token);
            var resultText = success ? "Order approved and executed" : "Order approval failed or expired";

            await _botClient!.AnswerCallbackQueryAsync(callback.Id, resultText, cancellationToken: ct);
        }
        else if (data.StartsWith("reject:"))
        {
            var token = data[7..];
            using var scope = _serviceProvider.CreateScope();
            var orderManager = scope.ServiceProvider.GetRequiredService<IOrderManager>();

            await orderManager.RejectOrderAsync(token);

            await _botClient!.AnswerCallbackQueryAsync(callback.Id, "Order rejected", cancellationToken: ct);
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, "Telegram bot error");
        return Task.CompletedTask;
    }

    public async Task SendMessageAsync(string message)
    {
        if (_botClient is null) return;

        await _botClient.SendTextMessageAsync(
            _authorizedChatId,
            message,
            parseMode: ParseMode.Markdown);
    }

    public async Task SendAlertAsync(AlertTrigger alert)
    {
        if (_botClient is null) return;

        var message = $"""
            *Alert Triggered*
            {alert.Symbol} @ {alert.TriggerPrice}

            {alert.Message}

            {(alert.AiEnrichment is not null ? $"_Context: {alert.AiEnrichment}_" : "")}
            """;

        await _botClient.SendTextMessageAsync(
            _authorizedChatId,
            message,
            parseMode: ParseMode.Markdown);
    }

    public async Task SendOrderApprovalAsync(PreparedOrder order)
    {
        if (_botClient is null) return;

        var message = $"""
            *Order Ready for Approval*

            {order.Symbol} {order.Direction}
            Volume: {order.Volume} lots
            Entry: {order.EntryPrice}
            SL: {order.StopLoss}
            TP: {order.TakeProfit}
            Risk: {order.RiskPercent}%

            _Expires in 5 minutes_
            """;

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Approve", $"approve:{order.ApprovalToken}"),
                InlineKeyboardButton.WithCallbackData("Reject", $"reject:{order.ApprovalToken}")
            }
        });

        await _botClient.SendTextMessageAsync(
            _authorizedChatId,
            message,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);
    }

    private async Task<string> GetStatusAsync()
    {
        // TODO: Fetch real account data
        return """
            *Account Status*
            Balance: $10,000.00
            Equity: $10,150.00
            Margin: $500.00
            Free Margin: $9,650.00
            Open P&L: +$150.00
            """;
    }

    private async Task<string> GetPositionsAsync()
    {
        // TODO: Fetch real positions
        return "*No open positions*";
    }

    private async Task<string> GetAlertsAsync()
    {
        // TODO: Fetch real alerts
        return "*No active alerts*";
    }

    private async Task<string> GetTodaySummaryAsync()
    {
        // TODO: Fetch real stats
        return "*Today: No trades yet*";
    }

    private async Task<string> GetWeekSummaryAsync()
    {
        // TODO: Fetch real stats
        return "*This Week: No trades yet*";
    }

    private async Task<string> GetCalendarAsync()
    {
        // TODO: Integrate economic calendar
        return "*No high-impact events today*";
    }

    private async Task<string> CreateQuickAlertAsync(string alertSpec)
    {
        // Parse: EURUSD > 1.0900
        // TODO: Implement quick alert creation
        return $"_Alert creation not yet implemented: {alertSpec}_";
    }

    private string GetHelpText() => """
        *Available Commands*

        /status - Account balance and equity
        /positions - Open positions
        /alerts - Active alert rules
        /alert EURUSD > 1.0900 - Quick alert
        /today - Today's trading summary
        /week - This week's summary
        /calendar - Economic events
        /help - Show this help
        """;
}
