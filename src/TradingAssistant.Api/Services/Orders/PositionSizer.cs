using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;

namespace TradingAssistant.Api.Services.Orders;

public interface IPositionSizer
{
    Task<decimal> CalculateAsync(string symbol, decimal riskPercent, decimal entryPrice, decimal stopLoss);
    Task<MarginInfo> CalculateMarginAsync(string symbol, decimal lotSize, decimal price);
}

public record MarginInfo(decimal Required, decimal FreeMargin, int Leverage, bool Sufficient);

public class PositionSizer : IPositionSizer
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<PositionSizer> _logger;

    public PositionSizer(AppDbContext db, IConfiguration config, ILogger<PositionSizer> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task<decimal> CalculateAsync(string symbol, decimal riskPercent, decimal entryPrice, decimal stopLoss)
    {
        // Get account balance
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.IsActive);
        if (account is null)
            throw new InvalidOperationException("No active account found");

        // Get symbol volume constraints from DB (already stored in lots)
        var symbolInfo = await _db.Symbols.FirstOrDefaultAsync(s => s.Name == symbol && s.IsActive);
        var minLot = symbolInfo?.MinVolume ?? 0.01m;
        var maxLot = symbolInfo?.MaxVolume ?? 100m;
        var volumeStep = symbolInfo?.VolumeStep ?? 0.01m;

        var accountBalance = account.Balance;
        var riskAmount = accountBalance * (riskPercent / 100m);

        // Calculate stop loss distance in price
        var slDistance = Math.Abs(entryPrice - stopLoss);
        if (slDistance == 0)
            throw new InvalidOperationException("Stop loss cannot be at entry price");

        // Get pip value for the symbol
        var pipValue = GetPipValue(symbol, account.Currency);
        var pipSize = GetPipSize(symbol);

        // Calculate pips at risk
        var pipsAtRisk = slDistance / pipSize;

        // Calculate lot size
        // Formula: Lot Size = Risk Amount / (Pips at Risk × Pip Value)
        var lotSize = riskAmount / (pipsAtRisk * pipValue);

        // Round to broker's lot step and clamp to min/max
        var rawLotSize = RoundToLotStep(lotSize, volumeStep);
        lotSize = Math.Max(minLot, Math.Min(maxLot, rawLotSize));

        if (rawLotSize < minLot)
        {
            _logger.LogWarning(
                "Calculated lot size {RawLots} below minimum {MinLot} for {Symbol} — clamped to minimum. Risk will exceed {RiskPercent}%",
                rawLotSize, minLot, symbol, riskPercent);
        }

        _logger.LogInformation(
            "Position size calculated: {Symbol} Risk={RiskPercent}% SL={SlDistance} pips → {LotSize} lots (min={MinLot}, max={MaxLot}, step={Step})",
            symbol, riskPercent, pipsAtRisk, lotSize, minLot, maxLot, volumeStep);

        return lotSize;
    }

    public async Task<MarginInfo> CalculateMarginAsync(string symbol, decimal lotSize, decimal price)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.IsActive);
        if (account is null)
            return new MarginInfo(0, 0, 1, false);

        var leverage = account.Leverage > 0 ? account.Leverage : 100;

        // Get contract size from symbol info (default 100,000 for forex)
        var symbolInfo = await _db.Symbols.FirstOrDefaultAsync(s => s.Name == symbol && s.IsActive);
        var contractSize = symbolInfo?.ContractSize > 0 ? symbolInfo.ContractSize : 100_000m;

        var marginRequired = Math.Round(lotSize * contractSize * price / leverage, 2);
        var freeMargin = account.FreeMargin > 0 ? account.FreeMargin
            : account.Equity > 0 ? account.Equity - account.Margin
            : account.Balance;

        return new MarginInfo(marginRequired, freeMargin, leverage, marginRequired <= freeMargin);
    }

    private decimal GetPipValue(string symbol, string accountCurrency)
    {
        // Simplified pip value calculation
        // In reality, this needs to account for cross rates
        if (symbol.EndsWith(accountCurrency))
            return 10m; // Standard lot pip value for USD account with USD quote currency

        // USD-only account: standard lot pip value is $10
        return 10m;
    }

    private decimal GetPipSize(string symbol)
    {
        if (symbol.Contains("JPY"))
            return 0.01m;

        if (symbol.Contains("XAU") || symbol.Contains("GOLD"))
            return 0.1m;

        return 0.0001m;
    }

    private decimal RoundToLotStep(decimal lotSize, decimal volumeStep)
    {
        if (volumeStep <= 0) volumeStep = 0.01m;
        return Math.Floor(lotSize / volumeStep) * volumeStep;
    }
}
