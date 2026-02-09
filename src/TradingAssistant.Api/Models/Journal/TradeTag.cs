namespace TradingAssistant.Api.Models.Journal;

public class TradeTag
{
    public long Id { get; set; }
    public long TradeEntryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public TradeEntry TradeEntry { get; set; } = null!;
}
