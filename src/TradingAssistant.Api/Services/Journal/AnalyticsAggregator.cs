using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Analytics;
using TradingAssistant.Api.Models.Journal;

namespace TradingAssistant.Api.Services.Journal;

public interface IAnalyticsAggregator
{
    Task UpdateDailyStatsAsync(TradeEntry trade);
    Task UpdatePairStatsAsync(TradeEntry trade);
    Task RecalculateAllAsync(DateTime? since = null);
}

public class AnalyticsAggregator : IAnalyticsAggregator
{
    private readonly AppDbContext _db;
    private readonly ILogger<AnalyticsAggregator> _logger;

    public AnalyticsAggregator(AppDbContext db, ILogger<AnalyticsAggregator> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task UpdateDailyStatsAsync(TradeEntry trade)
    {
        var date = trade.CloseTime.Date;

        var stats = await _db.DailyStats
            .FirstOrDefaultAsync(s => s.Date == date && s.AccountId == trade.AccountId);

        if (stats is null)
        {
            stats = new DailyStats
            {
                AccountId = trade.AccountId,
                Date = date,
                CreatedAt = DateTime.UtcNow
            };
            _db.DailyStats.Add(stats);
        }

        // Recalculate from all trades for that day
        var dayTrades = await _db.TradeEntries
            .Where(t => t.CloseTime.Date == date && t.AccountId == trade.AccountId)
            .ToListAsync();

        stats.TotalTrades = dayTrades.Count;
        stats.WinningTrades = dayTrades.Count(t => t.NetPnL > 0);
        stats.LosingTrades = dayTrades.Count(t => t.NetPnL < 0);
        stats.WinRate = stats.TotalTrades > 0
            ? (decimal)stats.WinningTrades / stats.TotalTrades * 100
            : 0;
        stats.TotalPnL = dayTrades.Sum(t => t.NetPnL);
        stats.GrossProfit = dayTrades.Where(t => t.NetPnL > 0).Sum(t => t.NetPnL);
        stats.GrossLoss = Math.Abs(dayTrades.Where(t => t.NetPnL < 0).Sum(t => t.NetPnL));
        stats.ProfitFactor = stats.GrossLoss > 0 ? stats.GrossProfit / stats.GrossLoss : 0;
        stats.AverageWin = stats.WinningTrades > 0
            ? dayTrades.Where(t => t.NetPnL > 0).Average(t => t.NetPnL)
            : 0;
        stats.AverageLoss = stats.LosingTrades > 0
            ? Math.Abs(dayTrades.Where(t => t.NetPnL < 0).Average(t => t.NetPnL))
            : 0;
        stats.LargestWin = dayTrades.Any() ? dayTrades.Max(t => t.NetPnL) : 0;
        stats.LargestLoss = dayTrades.Any() ? dayTrades.Min(t => t.NetPnL) : 0;

        await _db.SaveChangesAsync();

        _logger.LogDebug("Updated daily stats for {Date}: {Trades} trades, {WinRate}% win rate",
            date, stats.TotalTrades, stats.WinRate);
    }

    public async Task UpdatePairStatsAsync(TradeEntry trade)
    {
        var stats = await _db.PairStats
            .FirstOrDefaultAsync(s => s.Symbol == trade.Symbol && s.AccountId == trade.AccountId);

        if (stats is null)
        {
            stats = new PairStats
            {
                AccountId = trade.AccountId,
                Symbol = trade.Symbol,
                FirstTradeAt = trade.CloseTime,
                CreatedAt = DateTime.UtcNow
            };
            _db.PairStats.Add(stats);
        }

        // Recalculate from all trades for that pair
        var pairTrades = await _db.TradeEntries
            .Where(t => t.Symbol == trade.Symbol && t.AccountId == trade.AccountId)
            .ToListAsync();

        stats.TotalTrades = pairTrades.Count;
        stats.WinningTrades = pairTrades.Count(t => t.NetPnL > 0);
        stats.WinRate = stats.TotalTrades > 0
            ? (decimal)stats.WinningTrades / stats.TotalTrades * 100
            : 0;
        stats.TotalPnL = pairTrades.Sum(t => t.NetPnL);
        stats.AveragePnL = pairTrades.Average(t => t.NetPnL);
        stats.TotalVolume = pairTrades.Sum(t => t.Volume);
        stats.BestTrade = pairTrades.Max(t => t.NetPnL);
        stats.WorstTrade = pairTrades.Min(t => t.NetPnL);
        stats.LastTradeAt = trade.CloseTime;
        stats.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    public async Task RecalculateAllAsync(DateTime? since = null)
    {
        var fromDate = since ?? DateTime.UtcNow.AddMonths(-12);

        _logger.LogInformation("Recalculating all analytics since {Date}", fromDate);

        var trades = await _db.TradeEntries
            .Where(t => t.CloseTime >= fromDate)
            .OrderBy(t => t.CloseTime)
            .ToListAsync();

        foreach (var trade in trades)
        {
            await UpdateDailyStatsAsync(trade);
            await UpdatePairStatsAsync(trade);
        }

        _logger.LogInformation("Recalculated analytics for {Count} trades", trades.Count);
    }
}
