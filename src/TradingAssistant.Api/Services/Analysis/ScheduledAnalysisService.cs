using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Analytics;
using TradingAssistant.Api.Services.AI;
using TradingAssistant.Api.Services.Notifications;
using TradingAssistant.Api.Services.Orders;

namespace TradingAssistant.Api.Services.Analysis;

public class ScheduledAnalysisService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INotificationService _notificationService;
    private readonly IConfiguration _config;
    private readonly ILogger<ScheduledAnalysisService> _logger;

    private readonly HashSet<int> _completedHours = new();

    public ScheduledAnalysisService(
        IServiceProvider serviceProvider,
        INotificationService notificationService,
        IConfiguration config,
        ILogger<ScheduledAnalysisService> logger)
    {
        _serviceProvider = serviceProvider;
        _notificationService = notificationService;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled analysis service started — reading schedule dynamically from DB");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var utcNow = DateTime.UtcNow;
                var currentHour = utcNow.Hour;

                // Reset completed hours at the start of each new day
                if (currentHour == 0 && _completedHours.Count > 0)
                    _completedHours.Clear();

                // Read schedule from DB on each tick (with config fallback)
                var (scheduleHours, minConfidence) = await GetScheduleAsync();

                if (scheduleHours.Length == 0)
                    continue;

                // Check if current hour matches schedule and hasn't run yet
                if (!scheduleHours.Contains(currentHour) || _completedHours.Contains(currentHour))
                    continue;

                _completedHours.Add(currentHour);

                // Read watchlist from DB; fall back to config if empty
                var watchlist = await GetWatchlistAsync();
                if (watchlist.Length == 0)
                {
                    _logger.LogWarning("No watchlist symbols found in DB or config — skipping analysis cycle");
                    continue;
                }

                _logger.LogInformation("Running scheduled analysis for {Count} symbols at {Hour}:00 UTC",
                    watchlist.Length, currentHour);

                await RunAnalysisCycleAsync(watchlist, minConfidence, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduled analysis cycle");
            }
        }
    }

    private async Task<(int[] Hours, int Confidence)> GetScheduleAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var settings = await db.AnalysisSettings.FirstOrDefaultAsync();
            if (settings is not null)
            {
                var hours = JsonSerializer.Deserialize<int[]>(settings.ScheduleUtcHoursJson) ?? [];
                return (hours, settings.AutoPrepareMinConfidence);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read schedule from DB, falling back to config");
        }

        // Fall back to config
        var configHours = _config.GetSection("Analysis:ScheduleUtcHours").Get<int[]>() ?? [];
        var configConfidence = _config.GetValue<int>("Analysis:AutoPrepareMinConfidence", 70);
        return (configHours, configConfidence);
    }

    private async Task<string[]> GetWatchlistAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var dbSymbols = await db.WatchlistSymbols
                .Select(w => w.Symbol)
                .ToArrayAsync();

            if (dbSymbols.Length > 0)
                return dbSymbols;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read watchlist from DB, falling back to config");
        }

        return _config.GetSection("Analysis:WatchlistSymbols").Get<string[]>() ?? [];
    }

    private async Task SaveSnapshotAsync(IServiceScope scope, MarketAnalysis analysis, string source)
    {
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AnalysisSnapshots.Add(new AnalysisSnapshot
            {
                Symbol = analysis.Pair,
                Bias = analysis.Bias,
                Confidence = analysis.Confidence,
                Recommendation = analysis.Recommendation,
                Reasoning = analysis.Reasoning,
                Support = analysis.KeyLevels.Support,
                Resistance = analysis.KeyLevels.Resistance,
                TradeDirection = analysis.Trade?.Direction,
                TradeEntry = analysis.Trade?.Entry,
                TradeStopLoss = analysis.Trade?.StopLoss,
                TradeTakeProfit = analysis.Trade?.TakeProfit,
                TradeLotSize = analysis.Trade?.LotSize,
                TradeRiskReward = analysis.Trade?.RiskRewardRatio,
                MarginRequired = analysis.Trade?.MarginRequired,
                LeverageWarning = analysis.Trade?.LeverageWarning,
                Source = source,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save analysis snapshot for {Symbol}", analysis.Pair);
        }
    }

    private async Task RunAnalysisCycleAsync(string[] watchlist, int minConfidence, CancellationToken ct)
    {
        var briefing = new StringBuilder();
        briefing.AppendLine("*Scheduled Market Analysis*");
        briefing.AppendLine($"_{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC_\n");

        var tradeOpportunities = new List<(string Symbol, MarketAnalysis Analysis)>();

        foreach (var symbol in watchlist)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var aiService = scope.ServiceProvider.GetRequiredService<IAiAnalysisService>();

                var analysis = await aiService.AnalyzeMarketAsync(symbol);

                // Save analysis snapshot
                await SaveSnapshotAsync(scope, analysis, "scheduled");

                var confidencePct = (int)(analysis.Confidence * 100);
                var emoji = analysis.Recommendation switch
                {
                    "buy" => "BUY",
                    "sell" => "SELL",
                    _ => analysis.Recommendation?.ToUpperInvariant() ?? "N/A"
                };

                briefing.AppendLine($"*{symbol}* — {emoji} ({confidencePct}%)");
                briefing.AppendLine($"  Bias: {analysis.Bias} | S: {analysis.KeyLevels.Support} | R: {analysis.KeyLevels.Resistance}");

                if (analysis.Trade is not null)
                {
                    briefing.AppendLine($"  Entry: {analysis.Trade.Entry} | SL: {analysis.Trade.StopLoss} | TP: {analysis.Trade.TakeProfit}");
                    briefing.AppendLine($"  R:R {analysis.Trade.RiskRewardRatio}:1 | {analysis.Trade.LotSize} lots");
                }

                briefing.AppendLine();

                // Track trade opportunities above confidence threshold
                if (analysis.Trade is not null
                    && confidencePct >= minConfidence
                    && analysis.Recommendation is "buy" or "sell")
                {
                    tradeOpportunities.Add((symbol, analysis));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze {Symbol} during scheduled scan", symbol);
                briefing.AppendLine($"*{symbol}* — _analysis failed_\n");
            }
        }

        // Send the briefing via notifications
        try
        {
            await _notificationService.SendMessageAsync(briefing.ToString(), NotificationChannel.Telegram);
            _logger.LogInformation("Scheduled analysis briefing sent for {Count} symbols", watchlist.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send scheduled analysis briefing");
        }

        // Auto-prepare orders for high-confidence trade opportunities
        foreach (var (symbol, analysis) in tradeOpportunities)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var orderManager = scope.ServiceProvider.GetRequiredService<IOrderManager>();

                var trade = analysis.Trade!;
                var request = new OrderRequest
                {
                    Symbol = symbol,
                    Direction = trade.Direction.Equals("buy", StringComparison.OrdinalIgnoreCase) ? "Buy" : "Sell",
                    EntryPrice = trade.Entry,
                    StopLoss = trade.StopLoss,
                    TakeProfit = trade.TakeProfit,
                    RiskPercent = trade.RiskPercent
                };

                await orderManager.PrepareOrderAsync(request);

                _logger.LogInformation(
                    "Auto-prepared order from scheduled analysis: {Symbol} {Direction} ({Confidence}% confidence)",
                    symbol, request.Direction, (int)(analysis.Confidence * 100));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-prepare order for {Symbol}", symbol);
            }
        }
    }
}
