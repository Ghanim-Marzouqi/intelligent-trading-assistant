using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Journal;

namespace TradingAssistant.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JournalController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<JournalController> _logger;

    public JournalController(AppDbContext db, ILogger<JournalController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TradeEntry>>> GetTrades(
        [FromQuery] string? symbol = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0)
    {
        var query = _db.TradeEntries.AsQueryable();

        if (!string.IsNullOrEmpty(symbol))
            query = query.Where(t => t.Symbol == symbol.ToUpperInvariant());

        if (from.HasValue)
            query = query.Where(t => t.CloseTime >= from.Value);

        if (to.HasValue)
            query = query.Where(t => t.CloseTime <= to.Value);

        return await query
            .OrderByDescending(t => t.CloseTime)
            .Skip(offset)
            .Take(limit)
            .Include(t => t.Tags)
            .ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TradeEntry>> GetTrade(long id)
    {
        var trade = await _db.TradeEntries
            .Include(t => t.Tags)
            .Include(t => t.Notes)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (trade is null)
            return NotFound();

        return trade;
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTrade(long id, UpdateTradeRequest request)
    {
        var trade = await _db.TradeEntries.FindAsync(id);
        if (trade is null)
            return NotFound();

        if (request.Strategy is not null)
            trade.Strategy = request.Strategy;
        if (request.Setup is not null)
            trade.Setup = request.Setup;
        if (request.Emotion is not null)
            trade.Emotion = request.Emotion;
        if (request.Rating.HasValue)
            trade.Rating = request.Rating.Value;

        trade.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id}/tags")]
    public async Task<IActionResult> AddTag(long id, AddTagRequest request)
    {
        var trade = await _db.TradeEntries.FindAsync(id);
        if (trade is null)
            return NotFound();

        var tag = new TradeTag
        {
            TradeEntryId = id,
            Name = request.Tag,
            CreatedAt = DateTime.UtcNow
        };

        _db.TradeTags.Add(tag);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}/tags/{tagId}")]
    public async Task<IActionResult> DeleteTag(long id, long tagId)
    {
        var tag = await _db.TradeTags
            .FirstOrDefaultAsync(t => t.Id == tagId && t.TradeEntryId == id);

        if (tag is null)
            return NotFound();

        _db.TradeTags.Remove(tag);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id}/notes")]
    public async Task<IActionResult> AddNote(long id, AddNoteRequest request)
    {
        var trade = await _db.TradeEntries.FindAsync(id);
        if (trade is null)
            return NotFound();

        var note = new TradeNote
        {
            TradeEntryId = id,
            Content = request.Content,
            CreatedAt = DateTime.UtcNow
        };

        _db.TradeNotes.Add(note);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Added note to trade {TradeId}", id);

        return NoContent();
    }

    [HttpGet("stats/daily")]
    public async Task<ActionResult<IEnumerable<DailyStatsDto>>> GetDailyStats(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        var trades = await _db.TradeEntries
            .Where(t => t.CloseTime >= fromDate && t.CloseTime <= toDate)
            .ToListAsync();

        var dailyStats = trades
            .GroupBy(t => t.CloseTime.Date)
            .Select(g => new DailyStatsDto(
                g.Key,
                g.Count(),
                g.Count(t => t.PnL > 0),
                g.Sum(t => t.PnL),
                g.Count() > 0 ? (decimal)g.Count(t => t.PnL > 0) / g.Count() * 100 : 0
            ))
            .OrderBy(d => d.Date)
            .ToList();

        return dailyStats;
    }
}

public record UpdateTradeRequest(string? Strategy, string? Setup, string? Emotion, int? Rating);
public record AddTagRequest(string Tag);
public record AddNoteRequest(string Content);
public record DailyStatsDto(DateTime Date, int TotalTrades, int WinningTrades, decimal TotalPnL, decimal WinRate);
