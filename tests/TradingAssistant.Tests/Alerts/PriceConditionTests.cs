using TradingAssistant.Api.Models.Alerts;
using TradingAssistant.Api.Services.Alerts.Conditions;

namespace TradingAssistant.Tests.Alerts;

public class PriceConditionTests
{
    private readonly PriceCondition _evaluator = new();

    private static AlertCondition MakeCondition(ComparisonOperator op, decimal value) => new()
    {
        Type = ConditionType.PriceLevel,
        Operator = op,
        Value = value
    };

    [Fact]
    public void Evaluate_GreaterThan_WhenAbove_ReturnsTrue()
    {
        var condition = MakeCondition(ComparisonOperator.GreaterThan, 1.1000m);
        Assert.True(_evaluator.Evaluate(condition, 1.1001m));
    }

    [Fact]
    public void Evaluate_GreaterThan_WhenEqual_ReturnsFalse()
    {
        var condition = MakeCondition(ComparisonOperator.GreaterThan, 1.1000m);
        Assert.False(_evaluator.Evaluate(condition, 1.1000m));
    }

    [Fact]
    public void Evaluate_LessThan_WhenBelow_ReturnsTrue()
    {
        var condition = MakeCondition(ComparisonOperator.LessThan, 1.1000m);
        Assert.True(_evaluator.Evaluate(condition, 1.0999m));
    }

    [Fact]
    public void Evaluate_GreaterOrEqual_WhenEqual_ReturnsTrue()
    {
        var condition = MakeCondition(ComparisonOperator.GreaterOrEqual, 1.1000m);
        Assert.True(_evaluator.Evaluate(condition, 1.1000m));
    }

    [Fact]
    public void Evaluate_LessOrEqual_WhenEqual_ReturnsTrue()
    {
        var condition = MakeCondition(ComparisonOperator.LessOrEqual, 1.1000m);
        Assert.True(_evaluator.Evaluate(condition, 1.1000m));
    }

    [Fact]
    public void Evaluate_CrossesAbove_WithPreviousBelow_ReturnsTrue()
    {
        var condition = MakeCondition(ComparisonOperator.CrossesAbove, 1.1000m);
        Assert.True(_evaluator.Evaluate(condition, 1.1001m, previousPrice: 1.0999m));
    }

    [Fact]
    public void Evaluate_CrossesAbove_WithPreviousAbove_ReturnsFalse()
    {
        var condition = MakeCondition(ComparisonOperator.CrossesAbove, 1.1000m);
        Assert.False(_evaluator.Evaluate(condition, 1.1002m, previousPrice: 1.1001m));
    }

    [Fact]
    public void Evaluate_CrossesAbove_WithoutPreviousPrice_ReturnsFalse()
    {
        var condition = MakeCondition(ComparisonOperator.CrossesAbove, 1.1000m);
        Assert.False(_evaluator.Evaluate(condition, 1.1001m));
    }

    [Fact]
    public void Evaluate_CrossesBelow_WithPreviousAbove_ReturnsTrue()
    {
        var condition = MakeCondition(ComparisonOperator.CrossesBelow, 1.1000m);
        Assert.True(_evaluator.Evaluate(condition, 1.0999m, previousPrice: 1.1001m));
    }

    [Fact]
    public void Evaluate_CrossesBelow_WithPreviousBelow_ReturnsFalse()
    {
        var condition = MakeCondition(ComparisonOperator.CrossesBelow, 1.1000m);
        Assert.False(_evaluator.Evaluate(condition, 1.0998m, previousPrice: 1.0999m));
    }

    [Fact]
    public void Evaluate_Equals_WhenEqual_ReturnsFalse_UnsupportedOperator()
    {
        var condition = MakeCondition(ComparisonOperator.Equals, 1.1000m);
        // Equals is not implemented in the switch, should return false
        Assert.False(_evaluator.Evaluate(condition, 1.1000m));
    }
}
