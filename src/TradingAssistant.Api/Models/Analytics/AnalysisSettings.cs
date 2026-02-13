namespace TradingAssistant.Api.Models.Analytics;

public class AnalysisSettings
{
    public long Id { get; set; }
    public string ScheduleUtcHoursJson { get; set; } = "[]";
    public int AutoPrepareMinConfidence { get; set; } = 70;
    public DateTime UpdatedAt { get; set; }
}
