namespace TradingAssistant.Api.Models.Trading;

public class Position
{
    public long Id { get; set; }
    public long CTraderPositionId { get; set; }
    public long AccountId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; }
    public decimal Volume { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal Swap { get; set; }
    public decimal Commission { get; set; }
    public PositionStatus Status { get; set; }
    public DateTime OpenTime { get; set; }
    public DateTime? CloseTime { get; set; }
    public decimal? ClosePrice { get; set; }
    public decimal? RealizedPnL { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public enum TradeDirection
{
    Buy,
    Sell
}

public enum PositionStatus
{
    Open,
    Closed,
    PartialClose
}
