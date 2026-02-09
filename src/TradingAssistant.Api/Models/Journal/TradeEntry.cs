namespace TradingAssistant.Api.Models.Journal;

public class TradeEntry
{
    public long Id { get; set; }
    public long PositionId { get; set; }
    public long AccountId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal Volume { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal PnL { get; set; }
    public decimal PnLPips { get; set; }
    public decimal Commission { get; set; }
    public decimal Swap { get; set; }
    public decimal NetPnL { get; set; }
    public decimal? RiskRewardRatio { get; set; }
    public decimal? RiskPercent { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime OpenTime { get; set; }
    public DateTime CloseTime { get; set; }
    public string? Strategy { get; set; }
    public string? Setup { get; set; }
    public string? Emotion { get; set; }
    public int? Rating { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<TradeTag> Tags { get; set; } = [];
    public ICollection<TradeNote> Notes { get; set; } = [];
}
