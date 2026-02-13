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

        var settings = await GetSettingsAsync();

        return new WatchlistResponse(
            symbols,
            settings.ScheduleHours,
            settings.MinConfidence,
            settings.MaxOpenPositions,
            settings.MaxTotalVolume,
            settings.MaxPositionsPerSymbol,
            settings.MaxDailyLossPercent);
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

        // Validate risk limits
        if (request.MaxOpenPositions is not null && (request.MaxOpenPositions < 1 || request.MaxOpenPositions > 20))
            return BadRequest(new { error = "Max open positions must be between 1 and 20" });

        if (request.MaxTotalVolume is not null && (request.MaxTotalVolume < 0.01m || request.MaxTotalVolume > 100m))
            return BadRequest(new { error = "Max total volume must be between 0.01 and 100" });

        if (request.MaxPositionsPerSymbol is not null && (request.MaxPositionsPerSymbol < 1 || request.MaxPositionsPerSymbol > 10))
            return BadRequest(new { error = "Max positions per symbol must be between 1 and 10" });

        if (request.MaxDailyLossPercent is not null && (request.MaxDailyLossPercent < 0.5m || request.MaxDailyLossPercent > 20m))
            return BadRequest(new { error = "Max daily loss percent must be between 0.5 and 20" });

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

        if (request.MaxOpenPositions is not null)
            settings.MaxOpenPositions = request.MaxOpenPositions.Value;
        if (request.MaxTotalVolume is not null)
            settings.MaxTotalVolume = request.MaxTotalVolume.Value;
        if (request.MaxPositionsPerSymbol is not null)
            settings.MaxPositionsPerSymbol = request.MaxPositionsPerSymbol.Value;
        if (request.MaxDailyLossPercent is not null)
            settings.MaxDailyLossPercent = request.MaxDailyLossPercent.Value;

        settings.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated analysis settings: hours={Hours}, confidence={Confidence}, maxPositions={MaxPositions}",
            settings.ScheduleUtcHoursJson, settings.AutoPrepareMinConfidence, settings.MaxOpenPositions);

        return new WatchlistSettingsResponse(
            hours,
            settings.AutoPrepareMinConfidence,
            settings.MaxOpenPositions,
            settings.MaxTotalVolume,
            settings.MaxPositionsPerSymbol,
            settings.MaxDailyLossPercent);
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

    private async Task<ResolvedSettings> GetSettingsAsync()
    {
        var settings = await _db.AnalysisSettings.FirstOrDefaultAsync();
        if (settings is not null)
        {
            var hours = JsonSerializer.Deserialize<int[]>(settings.ScheduleUtcHoursJson) ?? [];
            return new ResolvedSettings(
                hours,
                settings.AutoPrepareMinConfidence,
                settings.MaxOpenPositions,
                settings.MaxTotalVolume,
                settings.MaxPositionsPerSymbol,
                settings.MaxDailyLossPercent);
        }

        // Fall back to config
        var configHours = _config.GetSection("Analysis:ScheduleUtcHours").Get<int[]>() ?? [];
        var configConfidence = _config.GetValue<int>("Analysis:AutoPrepareMinConfidence", 70);
        return new ResolvedSettings(
            configHours,
            configConfidence,
            _config.GetValue<int>("Risk:MaxOpenPositions", 3),
            _config.GetValue<decimal>("Risk:MaxTotalVolume", 10m),
            _config.GetValue<int>("Risk:MaxPositionsPerSymbol", 3),
            _config.GetValue<decimal>("Risk:MaxDailyLossPercent", 5m));
    }

    private record ResolvedSettings(
        int[] ScheduleHours,
        int MinConfidence,
        int MaxOpenPositions,
        decimal MaxTotalVolume,
        int MaxPositionsPerSymbol,
        decimal MaxDailyLossPercent);
}

public record AddWatchlistSymbolRequest(string Symbol);
public record UpdateSettingsRequest(
    int[]? ScheduleUtcHours,
    int AutoPrepareMinConfidence,
    int? MaxOpenPositions = null,
    decimal? MaxTotalVolume = null,
    int? MaxPositionsPerSymbol = null,
    decimal? MaxDailyLossPercent = null);
public record WatchlistResponse(
    IEnumerable<WatchlistSymbol> Symbols,
    int[] ScheduleUtcHours,
    int AutoPrepareMinConfidence,
    int MaxOpenPositions,
    decimal MaxTotalVolume,
    int MaxPositionsPerSymbol,
    decimal MaxDailyLossPercent);
public record WatchlistSettingsResponse(
    int[] ScheduleUtcHours,
    int AutoPrepareMinConfidence,
    int MaxOpenPositions,
    decimal MaxTotalVolume,
    int MaxPositionsPerSymbol,
    decimal MaxDailyLossPercent);
