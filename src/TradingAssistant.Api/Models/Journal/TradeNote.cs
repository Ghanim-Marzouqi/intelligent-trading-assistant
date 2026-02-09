namespace TradingAssistant.Api.Models.Journal;

public class TradeNote
{
    public long Id { get; set; }
    public long TradeEntryId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public TradeEntry TradeEntry { get; set; } = null!;
}
