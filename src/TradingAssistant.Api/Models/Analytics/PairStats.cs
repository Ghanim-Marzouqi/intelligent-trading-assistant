namespace TradingAssistant.Api.Models.Analytics;

public class PairStats
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal AveragePnL { get; set; }
    public decimal TotalVolume { get; set; }
    public TimeSpan AverageDuration { get; set; }
    public decimal BestTrade { get; set; }
    public decimal WorstTrade { get; set; }
    public DateTime FirstTradeAt { get; set; }
    public DateTime LastTradeAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
