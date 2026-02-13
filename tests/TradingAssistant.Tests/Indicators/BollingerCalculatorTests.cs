using TradingAssistant.Api.Services.Alerts.Indicators;

namespace TradingAssistant.Tests.Indicators;

public class BollingerCalculatorTests
{
    private readonly BollingerCalculator _calculator = new(20, 2m);

    [Fact]
    public void Calculate_InsufficientData_ReturnsZeros()
    {
        var prices = Enumerable.Range(0, 10).Select(i => 1.0m + i * 0.01m).ToList();

        var result = _calculator.Calculate(prices);

        Assert.Equal(0m, result.Upper);
        Assert.Equal(0m, result.Middle);
        Assert.Equal(0m, result.Lower);
    }

    [Fact]
    public void Calculate_FlatPrices_EqualBands()
    {
        var prices = Enumerable.Repeat(1.5000m, 20).ToList();

        var result = _calculator.Calculate(prices);

        // With zero variance, all bands should equal the price
        Assert.Equal(result.Upper, result.Middle);
        Assert.Equal(result.Middle, result.Lower);
        Assert.Equal(1.5000m, result.Middle);
    }

    [Fact]
    public void Calculate_UpperGreaterThanMiddle_GreaterThanLower()
    {
        var prices = new List<decimal>();
        for (int i = 0; i < 20; i++)
        {
            prices.Add(1.5m + (i % 2 == 0 ? 0.01m : -0.01m));
        }

        var result = _calculator.Calculate(prices);

        Assert.True(result.Upper > result.Middle);
        Assert.True(result.Middle > result.Lower);
    }

    [Fact]
    public void Calculate_HighVolatility_WiderBands()
    {
        // Low volatility
        var lowVol = Enumerable.Range(0, 20).Select(i => 1.5m + (i % 2 == 0 ? 0.001m : -0.001m)).ToList();
        var lowResult = _calculator.Calculate(lowVol);

        // High volatility
        var highVol = Enumerable.Range(0, 20).Select(i => 1.5m + (i % 2 == 0 ? 0.05m : -0.05m)).ToList();
        var highResult = _calculator.Calculate(highVol);

        var lowBandWidth = lowResult.Upper - lowResult.Lower;
        var highBandWidth = highResult.Upper - highResult.Lower;

        Assert.True(highBandWidth > lowBandWidth);
    }

    [Fact]
    public void Calculate_CustomPeriod_Works()
    {
        var calculator = new BollingerCalculator(10, 1.5m);
        var prices = Enumerable.Range(0, 15).Select(i => 1.0m + i * 0.005m).ToList();

        var result = calculator.Calculate(prices);

        Assert.True(result.Upper > 0);
        Assert.True(result.Middle > 0);
        Assert.True(result.Lower > 0);
    }
}
