namespace TradingAssistant.Api.Models.Trading;

public class Deal
{
    public long Id { get; set; }
    public long CTraderDealId { get; set; }
    public long AccountId { get; set; }
    public long? PositionId { get; set; }
    public long? OrderId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; }
    public decimal Volume { get; set; }
    public decimal ExecutionPrice { get; set; }
    public decimal Commission { get; set; }
    public decimal Swap { get; set; }
    public decimal PnL { get; set; }
    public DealType Type { get; set; }
    public DateTime ExecutedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum DealType
{
    Open,
    Close,
    PartialClose,
    Deposit,
    Withdrawal
}
