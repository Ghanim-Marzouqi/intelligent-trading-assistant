namespace TradingAssistant.Api.Models.Analytics;

public class AnalysisSettings
{
    public long Id { get; set; }
    public string ScheduleUtcHoursJson { get; set; } = "[]";
    public int AutoPrepareMinConfidence { get; set; } = 70;
    public int MaxOpenPositions { get; set; } = 3;
    public decimal MaxTotalVolume { get; set; } = 10m;
    public int MaxPositionsPerSymbol { get; set; } = 3;
    public decimal MaxDailyLossPercent { get; set; } = 5m;
    public DateTime UpdatedAt { get; set; }
}
