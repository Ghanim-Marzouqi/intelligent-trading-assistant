using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Analytics;
using TradingAssistant.Api.Models.Trading;

namespace TradingAssistant.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WatchlistController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<WatchlistController> _logger;

    public WatchlistController(AppDbContext db, IConfiguration config, ILogger<WatchlistController> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<WatchlistResponse>> GetWatchlist()
    {
        var symbols = await _db.WatchlistSymbols
            .OrderBy(w => w.AddedAt)
            .ToListAsync();

        var (scheduleHours, minConfidence) = await GetSettingsAsync();

        return new WatchlistResponse(symbols, scheduleHours, minConfidence);
    }

    [HttpPut("settings")]
    public async Task<ActionResult<WatchlistSettingsResponse>> UpdateSettings(UpdateSettingsRequest request)
    {
        // Validate hours
        if (request.ScheduleUtcHours is not null)
        {
            if (request.ScheduleUtcHours.Any(h => h < 0 || h > 23))
                return BadRequest(new { error = "Hours must be between 0 and 23" });

            if (request.ScheduleUtcHours.Distinct().Count() != request.ScheduleUtcHours.Length)
                return BadRequest(new { error = "Duplicate hours are not allowed" });
        }

        // Validate confidence
        if (request.AutoPrepareMinConfidence < 0 || request.AutoPrepareMinConfidence > 100)
            return BadRequest(new { error = "Confidence must be between 0 and 100" });

        var settings = await _db.AnalysisSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new AnalysisSettings();
            _db.AnalysisSettings.Add(settings);
        }

        var hours = request.ScheduleUtcHours ?? [];
        Array.Sort(hours);
        settings.ScheduleUtcHoursJson = JsonSerializer.Serialize(hours);
        settings.AutoPrepareMinConfidence = request.AutoPrepareMinConfidence;
        settings.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated analysis settings: hours={Hours}, confidence={Confidence}",
            settings.ScheduleUtcHoursJson, settings.AutoPrepareMinConfidence);

        return new WatchlistSettingsResponse(hours, settings.AutoPrepareMinConfidence);
    }

    [HttpPost]
    public async Task<ActionResult<WatchlistSymbol>> AddSymbol(AddWatchlistSymbolRequest request)
    {
        var symbol = request.Symbol.ToUpperInvariant();

        var exists = await _db.WatchlistSymbols.AnyAsync(w => w.Symbol == symbol);
        if (exists)
            return Conflict(new { error = $"{symbol} is already on the watchlist" });

        var entry = new WatchlistSymbol
        {
            Symbol = symbol,
            AddedAt = DateTime.UtcNow
        };

        _db.WatchlistSymbols.Add(entry);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Added {Symbol} to watchlist", symbol);

        return CreatedAtAction(nameof(GetWatchlist), entry);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> RemoveSymbol(long id)
    {
        var entry = await _db.WatchlistSymbols.FindAsync(id);
        if (entry is null)
            return NotFound();

        _db.WatchlistSymbols.Remove(entry);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Removed {Symbol} from watchlist", entry.Symbol);

        return NoContent();
    }

    private async Task<(int[] Hours, int Confidence)> GetSettingsAsync()
    {
        var settings = await _db.AnalysisSettings.FirstOrDefaultAsync();
        if (settings is not null)
        {
            var hours = JsonSerializer.Deserialize<int[]>(settings.ScheduleUtcHoursJson) ?? [];
            return (hours, settings.AutoPrepareMinConfidence);
        }

        // Fall back to config
        var configHours = _config.GetSection("Analysis:ScheduleUtcHours").Get<int[]>() ?? [];
        var configConfidence = _config.GetValue<int>("Analysis:AutoPrepareMinConfidence", 70);
        return (configHours, configConfidence);
    }
}

public record AddWatchlistSymbolRequest(string Symbol);
public record UpdateSettingsRequest(int[]? ScheduleUtcHours, int AutoPrepareMinConfidence);
public record WatchlistResponse(
    IEnumerable<WatchlistSymbol> Symbols,
    int[] ScheduleUtcHours,
    int AutoPrepareMinConfidence);
public record WatchlistSettingsResponse(int[] ScheduleUtcHours, int AutoPrepareMinConfidence);
