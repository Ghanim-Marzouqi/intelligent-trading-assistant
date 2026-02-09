namespace TradingAssistant.Api.Models.Trading;

public class Order
{
    public long Id { get; set; }
    public long CTraderOrderId { get; set; }
    public long AccountId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public OrderType Type { get; set; }
    public TradeDirection Direction { get; set; }
    public decimal Volume { get; set; }
    public decimal? LimitPrice { get; set; }
    public decimal? StopPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Comment { get; set; }
}

public enum OrderType
{
    Market,
    Limit,
    Stop,
    StopLimit
}

public enum OrderStatus
{
    Pending,
    Filled,
    PartiallyFilled,
    Cancelled,
    Rejected,
    Expired
}
