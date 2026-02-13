using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;

namespace TradingAssistant.Api.Services.Orders;

public interface IPositionSizer
{
    Task<decimal> CalculateAsync(string symbol, decimal riskPercent, decimal entryPrice, decimal stopLoss);
}

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

        // Round to standard lot increments
        lotSize = RoundToLotStep(lotSize, symbol);

        // Apply min/max limits
        var minLot = GetMinLot(symbol);
        var maxLot = GetMaxLot(symbol);
        lotSize = Math.Max(minLot, Math.Min(maxLot, lotSize));

        _logger.LogInformation(
            "Position size calculated: {Symbol} Risk={RiskPercent}% SL={SlDistance} pips → {LotSize} lots",
            symbol, riskPercent, pipsAtRisk, lotSize);

        return lotSize;
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

    private decimal RoundToLotStep(decimal lotSize, string symbol)
    {
        var step = 0.01m; // Standard micro lot step
        return Math.Floor(lotSize / step) * step;
    }

    private decimal GetMinLot(string symbol) => 0.01m;
    private decimal GetMaxLot(string symbol) => 100m;
}
