namespace TradingAssistant.Api.Models.Alerts;

public class AlertCondition
{
    public long Id { get; set; }
    public long AlertRuleId { get; set; }
    public ConditionType Type { get; set; }
    public string Indicator { get; set; } = string.Empty;
    public ComparisonOperator Operator { get; set; }
    public decimal Value { get; set; }
    public decimal? SecondaryValue { get; set; }
    public string? Timeframe { get; set; }
    public int? Period { get; set; }
    public LogicalOperator? CombineWith { get; set; }
    public DateTime CreatedAt { get; set; }

    public AlertRule AlertRule { get; set; } = null!;
}

public enum ConditionType
{
    PriceLevel,
    PriceChange,
    IndicatorValue,
    IndicatorCrossover,
    Time
}

public enum ComparisonOperator
{
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual,
    CrossesAbove,
    CrossesBelow,
    Equals
}

public enum LogicalOperator
{
    And,
    Or
}
