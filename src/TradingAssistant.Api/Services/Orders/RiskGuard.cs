using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Trading;

namespace TradingAssistant.Api.Services.Orders;

public interface IRiskGuard
{
    Task<RiskValidation> ValidateAsync(string symbol, decimal volume, string direction);
}

public class RiskGuard : IRiskGuard
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<RiskGuard> _logger;

    public RiskGuard(AppDbContext db, IConfiguration config, ILogger<RiskGuard> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task<RiskValidation> ValidateAsync(string symbol, decimal volume, string direction)
    {
        var settings = await _db.AnalysisSettings.FirstOrDefaultAsync();

        var openPositions = await _db.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .ToListAsync();

        // Check 0: Maximum total open positions
        var maxOpenPositions = settings?.MaxOpenPositions
            ?? _config.GetValue<int>("Risk:MaxOpenPositions", 3);
        if (openPositions.Count >= maxOpenPositions)
        {
            return RiskValidation.Invalid($"Max open positions reached: {openPositions.Count}/{maxOpenPositions}");
        }

        // Check 1: Maximum total position size
        var maxTotalVolume = settings?.MaxTotalVolume
            ?? _config.GetValue<decimal>("Risk:MaxTotalVolume", 10m);
        var currentTotalVolume = openPositions.Sum(p => p.Volume);
        if (currentTotalVolume + volume > maxTotalVolume)
        {
            return RiskValidation.Invalid($"Max total volume exceeded: {currentTotalVolume + volume} > {maxTotalVolume}");
        }

        // Check 2: Maximum positions per symbol
        var maxPositionsPerSymbol = settings?.MaxPositionsPerSymbol
            ?? _config.GetValue<int>("Risk:MaxPositionsPerSymbol", 3);
        var symbolPositions = openPositions.Count(p => p.Symbol == symbol);
        if (symbolPositions >= maxPositionsPerSymbol)
        {
            return RiskValidation.Invalid($"Max positions for {symbol} reached: {symbolPositions}");
        }

        // Check 3: Daily loss limit
        var maxDailyLoss = settings?.MaxDailyLossPercent
            ?? _config.GetValue<decimal>("Risk:MaxDailyLossPercent", 5m);
        var todayPnL = await GetTodayPnLAsync();
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.IsActive);
        if (account is not null)
        {
            var dailyLossPercent = (todayPnL / account.Balance) * 100;
            if (dailyLossPercent <= -maxDailyLoss)
            {
                return RiskValidation.Invalid($"Daily loss limit reached: {dailyLossPercent:F2}%");
            }
        }

        // Check 4: Correlation check (avoid overexposure to correlated pairs)
        var correlatedSymbols = GetCorrelatedSymbols(symbol);
        var correlatedVolume = openPositions
            .Where(p => correlatedSymbols.Contains(p.Symbol) && p.Direction.ToString() == direction)
            .Sum(p => p.Volume);

        var maxCorrelatedVolume = _config.GetValue<decimal>("Risk:MaxCorrelatedVolume", 3m);
        if (correlatedVolume + volume > maxCorrelatedVolume)
        {
            return RiskValidation.Invalid($"Correlated exposure too high: {correlatedVolume + volume} > {maxCorrelatedVolume}");
        }

        _logger.LogDebug("Risk validation passed for {Symbol} {Volume} lots (limits from {Source})",
            symbol, volume, settings is not null ? "DB" : "config");

        return RiskValidation.Valid();
    }

    private async Task<decimal> GetTodayPnLAsync()
    {
        var today = DateTime.UtcNow.Date;

        var closedPnL = await _db.TradeEntries
            .Where(t => t.CloseTime.Date == today)
            .SumAsync(t => t.NetPnL);

        var unrealizedPnL = await _db.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .SumAsync(p => p.UnrealizedPnL);

        return closedPnL + unrealizedPnL;
    }

    private HashSet<string> GetCorrelatedSymbols(string symbol)
    {
        // Define correlation groups
        var correlationGroups = new List<HashSet<string>>
        {
            new() { "EURUSD", "GBPUSD", "AUDUSD", "NZDUSD" }, // USD weakness group
            new() { "USDJPY", "USDCHF", "USDCAD" },           // USD strength group
            new() { "EURJPY", "GBPJPY", "AUDJPY" },           // JPY crosses
            new() { "XAUUSD", "XAGUSD" },                      // Precious metals
        };

        foreach (var group in correlationGroups)
        {
            if (group.Contains(symbol))
                return group;
        }

        return new HashSet<string> { symbol };
    }
}

public class RiskValidation
{
    public bool IsValid { get; private set; }
    public string? Reason { get; private set; }

    public static RiskValidation Valid() => new() { IsValid = true };
    public static RiskValidation Invalid(string reason) => new() { IsValid = false, Reason = reason };
}
