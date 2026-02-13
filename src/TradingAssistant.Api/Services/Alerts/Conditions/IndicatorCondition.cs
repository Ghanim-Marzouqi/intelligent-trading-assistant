using TradingAssistant.Api.Models.Alerts;
using TradingAssistant.Api.Services.Alerts.Indicators;

namespace TradingAssistant.Api.Services.Alerts.Conditions;

public class IndicatorCondition : IConditionEvaluator
{
    public bool Evaluate(AlertCondition condition, decimal currentPrice, decimal? previousPrice = null)
    {
        // Indicator conditions require price history; fall back to false without it
        return false;
    }

    public bool EvaluateWithHistory(AlertCondition condition, IReadOnlyList<decimal> priceHistory,
        decimal currentPrice, decimal? previousPrice = null)
    {
        // Ensure enough history for calculation (Period + 1 buffer)
        var requiredCount = (condition.Period ?? 14) + 1;
        if (priceHistory.Count < requiredCount)
            return false;

        var indicatorValue = ComputeIndicatorValue(condition, priceHistory);
        if (indicatorValue is null)
            return false;

        // For crossover detection, compute indicator at previous price set too
        if (condition.Operator is ComparisonOperator.CrossesAbove or ComparisonOperator.CrossesBelow)
        {
            var previousHistory = priceHistory.Take(priceHistory.Count - 1).ToList();
            if (previousHistory.Count < 2)
                return false;

            var previousIndicatorValue = ComputeIndicatorValue(condition, previousHistory);
            if (previousIndicatorValue is null)
                return false;

            return condition.Operator switch
            {
                ComparisonOperator.CrossesAbove => previousIndicatorValue.Value <= condition.Value
                                                   && indicatorValue.Value > condition.Value,
                ComparisonOperator.CrossesBelow => previousIndicatorValue.Value >= condition.Value
                                                   && indicatorValue.Value < condition.Value,
                _ => false
            };
        }

        return condition.Operator switch
        {
            ComparisonOperator.GreaterThan => indicatorValue.Value > condition.Value,
            ComparisonOperator.LessThan => indicatorValue.Value < condition.Value,
            ComparisonOperator.GreaterOrEqual => indicatorValue.Value >= condition.Value,
            ComparisonOperator.LessOrEqual => indicatorValue.Value <= condition.Value,
            _ => false
        };
    }

    private static decimal? ComputeIndicatorValue(AlertCondition condition, IReadOnlyList<decimal> priceHistory)
    {
        if (string.IsNullOrEmpty(condition.Indicator))
            return null;

        var period = condition.Period ?? 14;

        return condition.Indicator.ToUpperInvariant() switch
        {
            "RSI" => new RsiCalculator(period).Calculate(priceHistory),

            "MACD_LINE" => new MacdCalculator().Calculate(priceHistory).MacdLine,
            "MACD_SIGNAL" => new MacdCalculator().Calculate(priceHistory).SignalLine,
            "MACD_HISTOGRAM" => new MacdCalculator().Calculate(priceHistory).Histogram,

            "BB_UPPER" => new BollingerCalculator(period).Calculate(priceHistory).Upper,
            "BB_MIDDLE" => new BollingerCalculator(period).Calculate(priceHistory).Middle,
            "BB_LOWER" => new BollingerCalculator(period).Calculate(priceHistory).Lower,

            _ => null
        };
    }
}
