namespace TradingAssistant.Api.Models.Analytics;

public class AnalysisSnapshot
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Bias { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public decimal Support { get; set; }
    public decimal Resistance { get; set; }

    // Trade suggestion (nullable — only present when recommendation is buy/sell)
    public string? TradeDirection { get; set; }
    public decimal? TradeEntry { get; set; }
    public decimal? TradeStopLoss { get; set; }
    public decimal? TradeTakeProfit { get; set; }
    public decimal? TradeLotSize { get; set; }
    public decimal? TradeRiskReward { get; set; }
    public decimal? MarginRequired { get; set; }
    public string? LeverageWarning { get; set; }

    public string Source { get; set; } = string.Empty; // "manual", "scheduled", "alert"
    public DateTime CreatedAt { get; set; }
}
