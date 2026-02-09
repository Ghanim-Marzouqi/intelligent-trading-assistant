namespace TradingAssistant.Api.Models.Analytics;

public class EquitySnapshot
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public decimal Balance { get; set; }
    public decimal Equity { get; set; }
    public decimal Margin { get; set; }
    public decimal FreeMargin { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public int OpenPositions { get; set; }
    public DateTime Timestamp { get; set; }
}
