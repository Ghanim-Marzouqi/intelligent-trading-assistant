namespace TradingAssistant.Api.Models.Trading;

public class WatchlistSymbol
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
}
