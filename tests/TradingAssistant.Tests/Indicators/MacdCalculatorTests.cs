using TradingAssistant.Api.Services.Alerts.Indicators;

namespace TradingAssistant.Tests.Indicators;

public class MacdCalculatorTests
{
    private readonly MacdCalculator _calculator = new();

    [Fact]
    public void Calculate_InsufficientData_ReturnsZeros()
    {
        var prices = Enumerable.Range(0, 10).Select(i => 1.0m + i * 0.01m).ToList();

        var result = _calculator.Calculate(prices);

        Assert.Equal(0m, result.MacdLine);
        Assert.Equal(0m, result.SignalLine);
        Assert.Equal(0m, result.Histogram);
    }

    [Fact]
    public void Calculate_FlatPrices_NearZero()
    {
        var prices = Enumerable.Repeat(1.5000m, 30).ToList();

        var result = _calculator.Calculate(prices);

        Assert.True(Math.Abs(result.MacdLine) < 0.001m);
    }

    [Fact]
    public void Calculate_Uptrend_PositiveMacd()
    {
        var prices = Enumerable.Range(0, 30).Select(i => 1.0m + i * 0.01m).ToList();

        var result = _calculator.Calculate(prices);

        Assert.True(result.MacdLine > 0);
    }

    [Fact]
    public void Calculate_Signal_Equals_MacdTimes09()
    {
        var prices = Enumerable.Range(0, 30).Select(i => 1.0m + i * 0.005m).ToList();

        var result = _calculator.Calculate(prices);

        // The simplified signal calculation multiplies MACD by 0.9
        Assert.Equal(result.MacdLine * 0.9m, result.SignalLine);
    }

    [Fact]
    public void Calculate_Histogram_IsMacdMinusSignal()
    {
        var prices = Enumerable.Range(0, 30).Select(i => 1.0m + i * 0.005m).ToList();

        var result = _calculator.Calculate(prices);

        Assert.Equal(result.MacdLine - result.SignalLine, result.Histogram);
    }
}
