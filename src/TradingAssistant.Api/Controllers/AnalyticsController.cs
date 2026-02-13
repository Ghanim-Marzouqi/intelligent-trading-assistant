using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;

namespace TradingAssistant.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AnalyticsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("overview")]
    public async Task<ActionResult<PerformanceOverview>> GetOverview(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        var trades = await _db.TradeEntries
            .Where(t => t.CloseTime >= fromDate && t.CloseTime <= toDate)
            .ToListAsync();

        if (!trades.Any())
        {
            return new PerformanceOverview(0, 0, 0, 0, 0, 0, 0, []);
        }

        var winningTrades = trades.Where(t => t.PnL > 0).ToList();
        var losingTrades = trades.Where(t => t.PnL < 0).ToList();

        var avgWin = winningTrades.Any() ? winningTrades.Average(t => t.PnL) : 0;
        var avgLoss = losingTrades.Any() ? Math.Abs(losingTrades.Average(t => t.PnL)) : 0;
        var profitFactor = avgLoss > 0 ? avgWin / avgLoss : 0;

        var pairPerformance = trades
            .GroupBy(t => t.Symbol)
            .Select(g => new PairPerformance(
                g.Key,
                g.Count(),
                g.Sum(t => t.PnL),
                g.Count() > 0 ? (decimal)g.Count(t => t.PnL > 0) / g.Count() * 100 : 0
            ))
            .OrderByDescending(p => p.TotalPnL)
            .ToList();

        return new PerformanceOverview(
            TotalTrades: trades.Count,
            WinRate: (decimal)winningTrades.Count / trades.Count * 100,
            TotalPnL: trades.Sum(t => t.PnL),
            AverageWin: avgWin,
            AverageLoss: avgLoss,
            ProfitFactor: profitFactor,
            LargestWin: trades.Any() ? trades.Max(t => t.PnL) : 0,
            PairPerformance: pairPerformance
        );
    }

    [HttpGet("equity-curve")]
    public async Task<ActionResult<IEnumerable<EquityPoint>>> GetEquityCurve(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        var snapshots = await _db.EquitySnapshots
            .Where(s => s.Timestamp >= fromDate && s.Timestamp <= toDate)
            .OrderBy(s => s.Timestamp)
            .Select(s => new EquityPoint(s.Timestamp, s.Equity, s.Balance))
            .ToListAsync();

        return snapshots;
    }

    [HttpGet("by-day-of-week")]
    public async Task<ActionResult<IEnumerable<DayOfWeekStats>>> GetStatsByDayOfWeek(
        [FromQuery] DateTime? from = null)
    {
        var fromDate = from ?? DateTime.UtcNow.AddMonths(-3);

        var trades = await _db.TradeEntries
            .Where(t => t.CloseTime >= fromDate)
            .ToListAsync();

        var stats = trades
            .GroupBy(t => t.CloseTime.DayOfWeek)
            .Select(g => new DayOfWeekStats(
                g.Key.ToString(),
                g.Count(),
                g.Sum(t => t.PnL),
                g.Count() > 0 ? (decimal)g.Count(t => t.PnL > 0) / g.Count() * 100 : 0
            ))
            .OrderBy(s => (int)Enum.Parse<DayOfWeek>(s.Day))
            .ToList();

        return stats;
    }

    [HttpGet("by-hour")]
    public async Task<ActionResult<IEnumerable<HourlyStats>>> GetStatsByHour(
        [FromQuery] DateTime? from = null)
    {
        var fromDate = from ?? DateTime.UtcNow.AddMonths(-3);

        var trades = await _db.TradeEntries
            .Where(t => t.CloseTime >= fromDate)
            .ToListAsync();

        var stats = trades
            .GroupBy(t => t.OpenTime.Hour)
            .Select(g => new HourlyStats(
                g.Key,
                g.Count(),
                g.Sum(t => t.PnL),
                g.Count() > 0 ? (decimal)g.Count(t => t.PnL > 0) / g.Count() * 100 : 0
            ))
            .OrderBy(s => s.Hour)
            .ToList();

        return stats;
    }
}

public record PerformanceOverview(
    int TotalTrades,
    decimal WinRate,
    decimal TotalPnL,
    decimal AverageWin,
    decimal AverageLoss,
    decimal ProfitFactor,
    decimal LargestWin,
    List<PairPerformance> PairPerformance);

public record PairPerformance(string Symbol, int Trades, decimal TotalPnL, decimal WinRate);
public record EquityPoint(DateTime Timestamp, decimal Equity, decimal Balance);
public record DayOfWeekStats(string Day, int Trades, decimal TotalPnL, decimal WinRate);
public record HourlyStats(int Hour, int Trades, decimal TotalPnL, decimal WinRate);
