using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Alerts;
using TradingAssistant.Api.Models.Trading;
using TradingAssistant.Api.Services.Alerts;
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

        // Retry GetMe with exponential backoff
        var attemptCount = 0;
        const int maxDelaySeconds = 60;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var me = await _botClient.GetMe(stoppingToken);
                _logger.LogInformation("Telegram bot started: @{Username}", me.Username);
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attemptCount), maxDelaySeconds));
                attemptCount++;
                _logger.LogError(ex, "Failed to connect to Telegram, retrying in {Delay}...", delay);
                await Task.Delay(delay, stoppingToken);
            }
        }

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
            await _botClient!.SendMessage(
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

            await _botClient!.AnswerCallbackQuery(callback.Id, resultText, cancellationToken: ct);
        }
        else if (data.StartsWith("reject:"))
        {
            var token = data[7..];
            using var scope = _serviceProvider.CreateScope();
            var orderManager = scope.ServiceProvider.GetRequiredService<IOrderManager>();

            await orderManager.RejectOrderAsync(token);

            await _botClient!.AnswerCallbackQuery(callback.Id, "Order rejected", cancellationToken: ct);
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

        try
        {
            await _botClient.SendMessage(
                _authorizedChatId,
                message,
                parseMode: ParseMode.Markdown);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram message");
        }
    }

    public async Task SendAlertAsync(AlertTrigger alert)
    {
        if (_botClient is null) return;

        try
        {
            var message = $"""
                *Alert Triggered*
                {alert.Symbol} @ {alert.TriggerPrice}

                {alert.Message}

                {(alert.AiEnrichment is not null ? $"_Context: {alert.AiEnrichment}_" : "")}
                """;

            await _botClient.SendMessage(
                _authorizedChatId,
                message,
                parseMode: ParseMode.Markdown);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram alert for {Symbol}", alert.Symbol);
        }
    }

    public async Task SendOrderApprovalAsync(PreparedOrder order)
    {
        if (_botClient is null) return;

        try
        {
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

            await _botClient.SendMessage(
                _authorizedChatId,
                message,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram order approval for {Symbol}", order.Symbol);
        }
    }

    private async Task<string> GetStatusAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var account = await db.Accounts
                .OrderByDescending(a => a.LastSyncAt)
                .FirstOrDefaultAsync();

            if (account is null)
                return "*Account Status*\n_No account data available_";

            var pnlSign = account.UnrealizedPnL >= 0 ? "+" : "";
            return $"""
                *Account Status*
                Balance: {account.Currency} {account.Balance:N2}
                Equity: {account.Currency} {account.Equity:N2}
                Margin: {account.Currency} {account.Margin:N2}
                Free Margin: {account.Currency} {account.FreeMargin:N2}
                Open P&L: {pnlSign}{account.Currency} {account.UnrealizedPnL:N2}
                _Last sync: {account.LastSyncAt:yyyy-MM-dd HH:mm} UTC_
                """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching account status");
            return "*Account Status*\n_Error fetching data_";
        }
    }

    private async Task<string> GetPositionsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var positions = await db.Positions
                .Where(p => p.Status == PositionStatus.Open)
                .OrderByDescending(p => p.OpenTime)
                .ToListAsync();

            if (positions.Count == 0)
                return "*No open positions*";

            var sb = new StringBuilder("*Open Positions*\n\n");
            var totalPnL = 0m;

            foreach (var p in positions)
            {
                var pnlSign = p.UnrealizedPnL >= 0 ? "+" : "";
                sb.AppendLine($"*{p.Symbol}* {p.Direction} {p.Volume} lots");
                sb.AppendLine($"  Entry: {p.EntryPrice} | Current: {p.CurrentPrice}");
                sb.AppendLine($"  PnL: {pnlSign}${p.UnrealizedPnL:N2}");
                sb.AppendLine();
                totalPnL += p.UnrealizedPnL;
            }

            var totalSign = totalPnL >= 0 ? "+" : "";
            sb.AppendLine($"*Total:* {totalSign}${totalPnL:N2} ({positions.Count} positions)");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching positions");
            return "*Positions*\n_Error fetching data_";
        }
    }

    private async Task<string> GetAlertsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IAlertRuleRepository>();

            var rules = (await repo.GetActiveRulesAsync()).ToList();

            if (rules.Count == 0)
                return "*No active alerts*";

            var sb = new StringBuilder($"*Active Alerts ({rules.Count})*\n\n");

            foreach (var r in rules)
            {
                sb.AppendLine($"#{r.Id} *{r.Name}*");
                sb.AppendLine($"  {r.Symbol} | {r.Type} | Triggered: {r.TriggerCount}x");
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching alerts");
            return "*Alerts*\n_Error fetching data_";
        }
    }

    private async Task<string> GetTodaySummaryAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var today = DateTime.UtcNow.Date;

            var stats = await db.DailyStats
                .Where(s => s.Date == today)
                .FirstOrDefaultAsync();

            if (stats is not null)
            {
                var pnlSign = stats.TotalPnL >= 0 ? "+" : "";
                return $"""
                    *Today's Summary*
                    Trades: {stats.TotalTrades} ({stats.WinningTrades}W / {stats.LosingTrades}L)
                    Win Rate: {stats.WinRate:N1}%
                    P&L: {pnlSign}${stats.TotalPnL:N2}
                    Best: +${stats.LargestWin:N2}
                    Worst: -${Math.Abs(stats.LargestLoss):N2}
                    """;
            }

            // Fallback: aggregate from TradeEntries closed today
            var trades = await db.TradeEntries
                .Where(t => t.CloseTime >= today)
                .ToListAsync();

            if (trades.Count == 0)
                return "*Today: No trades yet*";

            var wins = trades.Count(t => t.NetPnL > 0);
            var losses = trades.Count(t => t.NetPnL <= 0);
            var totalPnL = trades.Sum(t => t.NetPnL);
            var best = trades.Max(t => t.NetPnL);
            var worst = trades.Min(t => t.NetPnL);
            var winRate = trades.Count > 0 ? (decimal)wins / trades.Count * 100 : 0;
            var pnl = totalPnL >= 0 ? "+" : "";

            return $"""
                *Today's Summary*
                Trades: {trades.Count} ({wins}W / {losses}L)
                Win Rate: {winRate:N1}%
                P&L: {pnl}${totalPnL:N2}
                Best: ${best:N2}
                Worst: ${worst:N2}
                """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching today's summary");
            return "*Today*\n_Error fetching data_";
        }
    }

    private async Task<string> GetWeekSummaryAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var weekStart = DateTime.UtcNow.Date.AddDays(-6);

            var dailyStats = await db.DailyStats
                .Where(s => s.Date >= weekStart)
                .OrderBy(s => s.Date)
                .ToListAsync();

            if (dailyStats.Count > 0)
            {
                var totalTrades = dailyStats.Sum(s => s.TotalTrades);
                var totalWins = dailyStats.Sum(s => s.WinningTrades);
                var totalLosses = dailyStats.Sum(s => s.LosingTrades);
                var totalPnL = dailyStats.Sum(s => s.TotalPnL);
                var winRate = totalTrades > 0 ? (decimal)totalWins / totalTrades * 100 : 0;
                var bestDay = dailyStats.MaxBy(s => s.TotalPnL)!;
                var worstDay = dailyStats.MinBy(s => s.TotalPnL)!;
                var pnlSign = totalPnL >= 0 ? "+" : "";

                return $"""
                    *This Week (7 days)*
                    Trades: {totalTrades} ({totalWins}W / {totalLosses}L)
                    Win Rate: {winRate:N1}%
                    P&L: {pnlSign}${totalPnL:N2}
                    Best Day: {bestDay.Date:ddd MM/dd} ({(bestDay.TotalPnL >= 0 ? "+" : "")}${bestDay.TotalPnL:N2})
                    Worst Day: {worstDay.Date:ddd MM/dd} ({(worstDay.TotalPnL >= 0 ? "+" : "")}${worstDay.TotalPnL:N2})
                    """;
            }

            // Fallback: aggregate from TradeEntries closed this week
            var trades = await db.TradeEntries
                .Where(t => t.CloseTime >= weekStart)
                .ToListAsync();

            if (trades.Count == 0)
                return "*This Week: No trades yet*";

            var w = trades.Count(t => t.NetPnL > 0);
            var l = trades.Count(t => t.NetPnL <= 0);
            var pnl = trades.Sum(t => t.NetPnL);
            var wr = trades.Count > 0 ? (decimal)w / trades.Count * 100 : 0;
            var sign = pnl >= 0 ? "+" : "";

            return $"""
                *This Week (7 days)*
                Trades: {trades.Count} ({w}W / {l}L)
                Win Rate: {wr:N1}%
                P&L: {sign}${pnl:N2}
                """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching week summary");
            return "*This Week*\n_Error fetching data_";
        }
    }

    private async Task<string> GetCalendarAsync()
    {
        // No data source exists for economic calendar yet
        return "*No high-impact events today*";
    }

    private static readonly Regex AlertPattern = new(
        @"^(\w+)\s*(>=|<=|>|<)\s*(\d+\.?\d*)$",
        RegexOptions.Compiled);

    private async Task<string> CreateQuickAlertAsync(string alertSpec)
    {
        try
        {
            var match = AlertPattern.Match(alertSpec.Trim());
            if (!match.Success)
                return "_Invalid format. Use:_ `/alert EURUSD > 1.0900`";

            var symbol = match.Groups[1].Value.ToUpperInvariant();
            var op = match.Groups[2].Value;
            var value = decimal.Parse(match.Groups[3].Value);

            var compOp = op switch
            {
                ">" => ComparisonOperator.GreaterThan,
                "<" => ComparisonOperator.LessThan,
                ">=" => ComparisonOperator.GreaterOrEqual,
                "<=" => ComparisonOperator.LessOrEqual,
                _ => ComparisonOperator.GreaterThan
            };

            var rule = new AlertRule
            {
                Symbol = symbol,
                Name = $"{symbol} {op} {value}",
                Type = AlertType.Price,
                IsActive = true,
                NotifyTelegram = true,
                NotifyDashboard = true,
                MaxTriggers = 1,
                Conditions = [
                    new AlertCondition
                    {
                        Type = ConditionType.PriceLevel,
                        Indicator = "Price",
                        Operator = compOp,
                        Value = value
                    }
                ]
            };

            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IAlertRuleRepository>();
            var created = await repo.CreateAsync(rule);

            var alertEngine = _serviceProvider.GetRequiredService<AlertEngine>();
            alertEngine.AddRule(created);

            return $"""
                *Alert Created* #{created.Id}
                {symbol} {op} {value}
                _One-shot alert — will trigger once then deactivate_
                """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating quick alert from: {Spec}", alertSpec);
            return "_Error creating alert. Check the format:_ `/alert EURUSD > 1.0900`";
        }
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
