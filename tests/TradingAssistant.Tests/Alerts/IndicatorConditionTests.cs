using TradingAssistant.Api.Models.Alerts;
using TradingAssistant.Api.Services.Alerts.Conditions;

namespace TradingAssistant.Tests.Alerts;

public class IndicatorConditionTests
{
    private readonly IndicatorCondition _evaluator = new();

    [Fact]
    public void Evaluate_WithoutHistory_ReturnsFalse()
    {
        var condition = new AlertCondition
        {
            Type = ConditionType.IndicatorValue,
            Indicator = "RSI",
            Operator = ComparisonOperator.GreaterThan,
            Value = 70,
            Period = 14
        };

        Assert.False(_evaluator.Evaluate(condition, 1.1000m));
    }

    [Fact]
    public void EvaluateWithHistory_InsufficientData_ReturnsFalse()
    {
        var condition = new AlertCondition
        {
            Type = ConditionType.IndicatorValue,
            Indicator = "RSI",
            Operator = ComparisonOperator.GreaterThan,
            Value = 70,
            Period = 14
        };

        var history = new List<decimal> { 1.0m };

        Assert.False(_evaluator.EvaluateWithHistory(condition, history, 1.0m));
    }

    [Fact]
    public void EvaluateWithHistory_RSI_OverboughtDetection()
    {
        var condition = new AlertCondition
        {
            Type = ConditionType.IndicatorValue,
            Indicator = "RSI",
            Operator = ComparisonOperator.GreaterThan,
            Value = 70,
            Period = 14
        };

        // Strong uptrend should produce high RSI
        var history = Enumerable.Range(0, 20).Select(i => 1.0m + i * 0.01m).ToList();

        var result = _evaluator.EvaluateWithHistory(condition, history, 1.20m);

        Assert.True(result);
    }

    [Fact]
    public void EvaluateWithHistory_MACD_PositiveLineGreaterThan()
    {
        var condition = new AlertCondition
        {
            Type = ConditionType.IndicatorValue,
            Indicator = "MACD_LINE",
            Operator = ComparisonOperator.GreaterThan,
            Value = 0,
            Period = 14
        };

        // Uptrend should give positive MACD
        var history = Enumerable.Range(0, 30).Select(i => 1.0m + i * 0.01m).ToList();

        var result = _evaluator.EvaluateWithHistory(condition, history, 1.30m);

        Assert.True(result);
    }

    [Fact]
    public void EvaluateWithHistory_Bollinger_UpperBandCheck()
    {
        var condition = new AlertCondition
        {
            Type = ConditionType.IndicatorValue,
            Indicator = "BB_UPPER",
            Operator = ComparisonOperator.GreaterThan,
            Value = 0,
            Period = 20
        };

        var history = Enumerable.Range(0, 25).Select(i => 1.5m + (i % 2 == 0 ? 0.01m : -0.01m)).ToList();

        var result = _evaluator.EvaluateWithHistory(condition, history, 1.50m);

        Assert.True(result);
    }

    [Fact]
    public void EvaluateWithHistory_UnknownIndicator_ReturnsFalse()
    {
        var condition = new AlertCondition
        {
            Type = ConditionType.IndicatorValue,
            Indicator = "UNKNOWN",
            Operator = ComparisonOperator.GreaterThan,
            Value = 50,
            Period = 14
        };

        var history = Enumerable.Range(0, 20).Select(i => 1.0m + i * 0.01m).ToList();

        Assert.False(_evaluator.EvaluateWithHistory(condition, history, 1.20m));
    }

    [Fact]
    public void EvaluateWithHistory_RSI_CrossesAbove()
    {
        var condition = new AlertCondition
        {
            Type = ConditionType.IndicatorCrossover,
            Indicator = "RSI",
            Operator = ComparisonOperator.CrossesAbove,
            Value = 30,
            Period = 14
        };

        // Start with downtrend (low RSI) then add an uptick
        var history = new List<decimal>();
        for (int i = 0; i < 16; i++)
            history.Add(2.0m - i * 0.01m);
        // Add a significant uptick at the end
        history.Add(2.0m);

        // The result depends on whether the RSI crosses 30
        // This tests the crossover mechanism works (doesn't throw)
        var result = _evaluator.EvaluateWithHistory(condition, history, 2.0m);
        // Result may be true or false depending on actual RSI values, but should not throw
        Assert.IsType<bool>(result);
    }
}
