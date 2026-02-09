namespace TradingAssistant.Api.Models.Alerts;

public class AlertTrigger
{
    public long Id { get; set; }
    public long AlertRuleId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public decimal TriggerPrice { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? AiEnrichment { get; set; }
    public bool NotifiedTelegram { get; set; }
    public bool NotifiedWhatsApp { get; set; }
    public bool NotifiedDashboard { get; set; }
    public DateTime TriggeredAt { get; set; }

    public AlertRule AlertRule { get; set; } = null!;
}
