using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Services.CTrader;

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

        // Get symbol volume constraints from DB (stored in cTrader volume units)
        var symbolInfo = await _db.Symbols.FirstOrDefaultAsync(s => s.Name == symbol && s.IsActive);
        var contractSize = symbolInfo?.ContractSize > 0 ? symbolInfo.ContractSize : 100_000m;
        var minLot = CTraderConversions.DbVolumeToLots(symbolInfo?.MinVolume ?? 1000m, contractSize);
        var maxLot = CTraderConversions.DbVolumeToLots(symbolInfo?.MaxVolume ?? 10_000_000m, contractSize);
        var volumeStep = CTraderConversions.DbVolumeToLots(symbolInfo?.VolumeStep ?? 1000m, contractSize);

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

        // Calculate lot size from risk
        // Formula: Lot Size = Risk Amount / (Pips at Risk × Pip Value)
        var lotSize = riskAmount / (pipsAtRisk * pipValue);

        // Also cap lot size to what available margin can support
        var leverage = account.Leverage > 0 ? account.Leverage : 100;
        var freeMargin = account.FreeMargin > 0 ? account.FreeMargin
            : account.Equity > 0 ? account.Equity - account.Margin
            : account.Balance;

        // Use 80% of free margin as cap to leave headroom
        var marginBudget = freeMargin * 0.8m;
        if (contractSize > 0 && entryPrice > 0)
        {
            var marginPerLot = contractSize * entryPrice / leverage;
            if (marginPerLot > 0)
            {
                var maxLotByMargin = marginBudget / marginPerLot;
                maxLotByMargin = RoundToLotStep(maxLotByMargin, volumeStep);
                if (maxLotByMargin < lotSize)
                {
                    _logger.LogWarning(
                        "Margin cap: {Symbol} risk-based={RiskLots} lots, margin-capped={MarginLots} lots (freeMargin={FreeMargin}, marginPerLot={MarginPerLot}, leverage={Leverage})",
                        symbol, lotSize, maxLotByMargin, freeMargin, marginPerLot, leverage);
                    lotSize = maxLotByMargin;
                }
            }
        }

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
            "Position size calculated: {Symbol} Risk={RiskPercent}% SL={SlDistance} pips → {LotSize} lots (min={MinLot}, max={MaxLot}, step={Step}, freeMargin={FreeMargin})",
            symbol, riskPercent, pipsAtRisk, lotSize, minLot, maxLot, volumeStep, freeMargin);

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
        // Pip value per standard lot (1 lot)
        // For crypto: 1 pip move on 1 lot = contractSize * pipSize in quote currency
        // For forex pairs where quote = account currency: $10 per pip per lot
        if (IsCrypto(symbol))
            return 1m; // 1 lot crypto = 1 unit, pip = $1 for USD-quoted

        if (symbol.Contains("XAU") || symbol.Contains("GOLD"))
            return 10m; // Gold: $10 per 0.1 pip per lot

        if (symbol.Contains("XAG") || symbol.Contains("SILVER"))
            return 50m;

        // Standard forex: $10 per pip per standard lot
        return 10m;
    }

    private decimal GetPipSize(string symbol)
    {
        if (symbol.Contains("JPY"))
            return 0.01m;

        if (symbol.Contains("XAU") || symbol.Contains("GOLD"))
            return 0.1m;

        if (IsCrypto(symbol))
            return 1.0m; // Crypto: 1 pip = $1

        return 0.0001m; // Standard forex
    }

    private static bool IsCrypto(string symbol)
    {
        var cryptoBases = new[] { "BTC", "ETH", "LTC", "XRP", "BCH", "ADA", "DOT", "SOL", "DOGE", "BNB", "AVAX", "LINK", "MATIC" };
        return cryptoBases.Any(c => symbol.StartsWith(c, StringComparison.OrdinalIgnoreCase));
    }

    private decimal RoundToLotStep(decimal lotSize, decimal volumeStep)
    {
        if (volumeStep <= 0) volumeStep = 0.01m;
        return Math.Floor(lotSize / volumeStep) * volumeStep;
    }
}
