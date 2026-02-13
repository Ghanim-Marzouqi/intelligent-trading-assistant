namespace TradingAssistant.Api.Models.Alerts;

public class AlertRule
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AlertType Type { get; set; }
    public bool IsActive { get; set; }
    public bool NotifyTelegram { get; set; } = true;
    public bool NotifyWhatsApp { get; set; }
    public bool NotifyDashboard { get; set; } = true;
    public bool AiEnrichEnabled { get; set; } = true;
    public int? MaxTriggers { get; set; }
    public int TriggerCount { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<AlertCondition> Conditions { get; set; } = [];
}

public enum AlertType
{
    Price,
    Indicator,
    Composite,
    TimeBased
}
