namespace TradingAssistant.Api.Models.Analytics;

public class DailyStats
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public DateTime Date { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossLoss { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public decimal LargestWin { get; set; }
    public decimal LargestLoss { get; set; }
    public decimal StartingBalance { get; set; }
    public decimal EndingBalance { get; set; }
    public DateTime CreatedAt { get; set; }
}
