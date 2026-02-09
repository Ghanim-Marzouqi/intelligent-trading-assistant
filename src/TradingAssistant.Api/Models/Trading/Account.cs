namespace TradingAssistant.Api.Models.Trading;

public class Account
{
    public long Id { get; set; }
    public long CTraderAccountId { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public decimal Equity { get; set; }
    public decimal Margin { get; set; }
    public decimal FreeMargin { get; set; }
    public decimal MarginLevel { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public int Leverage { get; set; }
    public bool IsLive { get; set; }
    public bool IsActive { get; set; }
    public DateTime LastSyncAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
