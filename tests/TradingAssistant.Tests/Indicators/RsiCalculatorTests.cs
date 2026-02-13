using TradingAssistant.Api.Services.Alerts.Indicators;

namespace TradingAssistant.Tests.Indicators;

public class RsiCalculatorTests
{
    private readonly RsiCalculator _calculator = new(14);

    [Fact]
    public void Calculate_InsufficientData_Returns50()
    {
        var prices = new List<decimal> { 1.0m, 1.1m, 1.2m }; // Only 3 data points, need 15

        var result = _calculator.Calculate(prices);

        Assert.Equal(50m, result);
    }

    [Fact]
    public void Calculate_AllGains_ReturnsNear100()
    {
        // 16 prices, each higher than the last
        var prices = Enumerable.Range(0, 16).Select(i => 1.0m + i * 0.01m).ToList();

        var result = _calculator.Calculate(prices);

        Assert.Equal(100m, result);
    }

    [Fact]
    public void Calculate_AllLosses_ReturnsNear0()
    {
        // 16 prices, each lower than the last
        var prices = Enumerable.Range(0, 16).Select(i => 2.0m - i * 0.01m).ToList();

        var result = _calculator.Calculate(prices);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void Calculate_KnownSeries_ReturnsExpectedRange()
    {
        // Mix of gains and losses
        var prices = new List<decimal>
        {
            44.34m, 44.09m, 44.15m, 43.61m, 44.33m, 44.83m, 45.10m, 45.42m,
            45.84m, 46.08m, 45.89m, 46.03m, 45.61m, 46.28m, 46.28m, 46.00m
        };

        var result = _calculator.Calculate(prices);

        // RSI should be between 0 and 100
        Assert.InRange(result, 0m, 100m);
    }

    [Fact]
    public void Calculate_CustomPeriod_Works()
    {
        var calculator = new RsiCalculator(7);
        var prices = Enumerable.Range(0, 10).Select(i => 1.0m + i * 0.005m).ToList();

        var result = calculator.Calculate(prices);

        Assert.InRange(result, 0m, 100m);
    }
}
