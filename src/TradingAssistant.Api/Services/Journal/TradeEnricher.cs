using TradingAssistant.Api.Models.Journal;

namespace TradingAssistant.Api.Services.Journal;

public interface ITradeEnricher
{
    Task EnrichAsync(TradeEntry entry);
}

public class TradeEnricher : ITradeEnricher
{
    private readonly ILogger<TradeEnricher> _logger;

    public TradeEnricher(ILogger<TradeEnricher> logger)
    {
        _logger = logger;
    }

    public Task EnrichAsync(TradeEntry entry)
    {
        // Calculate PnL in pips
        entry.PnLPips = CalculatePips(entry.Symbol, entry.EntryPrice, entry.ExitPrice, entry.Direction);

        // Calculate net PnL (after commission and swap)
        entry.NetPnL = entry.PnL - entry.Commission - entry.Swap;

        // Calculate trade duration
        entry.Duration = entry.CloseTime - entry.OpenTime;

        // Calculate risk/reward ratio if SL and TP are set
        if (entry.StopLoss.HasValue && entry.TakeProfit.HasValue)
        {
            entry.RiskRewardRatio = CalculateRiskReward(
                entry.EntryPrice,
                entry.StopLoss.Value,
                entry.TakeProfit.Value,
                entry.Direction);
        }

        _logger.LogDebug("Enriched trade {TradeId}: {PnLPips} pips, R:R {RR}",
            entry.Id, entry.PnLPips, entry.RiskRewardRatio);

        return Task.CompletedTask;
    }

    private decimal CalculatePips(string symbol, decimal entry, decimal exit, string direction)
    {
        var pipSize = GetPipSize(symbol);
        var diff = direction.Equals("Buy", StringComparison.OrdinalIgnoreCase)
            ? exit - entry
            : entry - exit;

        return Math.Round(diff / pipSize, 1);
    }

    private decimal CalculateRiskReward(decimal entry, decimal stopLoss, decimal takeProfit, string direction)
    {
        decimal risk, reward;

        if (direction.Equals("Buy", StringComparison.OrdinalIgnoreCase))
        {
            risk = entry - stopLoss;
            reward = takeProfit - entry;
        }
        else
        {
            risk = stopLoss - entry;
            reward = entry - takeProfit;
        }

        if (risk <= 0) return 0;

        return Math.Round(reward / risk, 2);
    }

    private decimal GetPipSize(string symbol)
    {
        // JPY pairs have different pip size
        if (symbol.Contains("JPY"))
            return 0.01m;

        // Indices and commodities vary
        if (symbol.Contains("XAU") || symbol.Contains("GOLD"))
            return 0.1m;

        // Standard forex pairs
        return 0.0001m;
    }
}
