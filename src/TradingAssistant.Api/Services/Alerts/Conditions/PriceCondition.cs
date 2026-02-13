using TradingAssistant.Api.Models.Alerts;

namespace TradingAssistant.Api.Services.Alerts.Conditions;

public interface IConditionEvaluator
{
    bool Evaluate(AlertCondition condition, decimal currentPrice, decimal? previousPrice = null);

    bool EvaluateWithHistory(AlertCondition condition, IReadOnlyList<decimal> priceHistory,
        decimal currentPrice, decimal? previousPrice = null)
        => Evaluate(condition, currentPrice, previousPrice);
}

public class PriceCondition : IConditionEvaluator
{
    public bool Evaluate(AlertCondition condition, decimal currentPrice, decimal? previousPrice = null)
    {
        return condition.Operator switch
        {
            ComparisonOperator.GreaterThan => currentPrice > condition.Value,
            ComparisonOperator.LessThan => currentPrice < condition.Value,
            ComparisonOperator.GreaterOrEqual => currentPrice >= condition.Value,
            ComparisonOperator.LessOrEqual => currentPrice <= condition.Value,
            ComparisonOperator.CrossesAbove => previousPrice.HasValue &&
                                               previousPrice.Value <= condition.Value &&
                                               currentPrice > condition.Value,
            ComparisonOperator.CrossesBelow => previousPrice.HasValue &&
                                               previousPrice.Value >= condition.Value &&
                                               currentPrice < condition.Value,
            _ => false
        };
    }
}
