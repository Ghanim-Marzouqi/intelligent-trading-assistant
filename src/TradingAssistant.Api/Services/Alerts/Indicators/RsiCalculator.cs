namespace TradingAssistant.Api.Services.Alerts.Indicators;

public interface IIndicatorCalculator
{
    decimal Calculate(IReadOnlyList<decimal> prices);
}

public class RsiCalculator : IIndicatorCalculator
{
    private readonly int _period;

    public RsiCalculator(int period = 14)
    {
        _period = period;
    }

    public decimal Calculate(IReadOnlyList<decimal> prices)
    {
        if (prices.Count < _period + 1)
            return 50m; // Neutral if insufficient data

        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i <= _period; i++)
        {
            var change = prices[prices.Count - i] - prices[prices.Count - i - 1];
            if (change > 0)
            {
                gains.Add(change);
                losses.Add(0);
            }
            else
            {
                gains.Add(0);
                losses.Add(Math.Abs(change));
            }
        }

        var avgGain = gains.Average();
        var avgLoss = losses.Average();

        if (avgLoss == 0)
            return 100m;

        var rs = avgGain / avgLoss;
        var rsi = 100m - (100m / (1m + rs));

        return Math.Round(rsi, 2);
    }
}

public class MacdCalculator
{
    private readonly int _fastPeriod;
    private readonly int _slowPeriod;
    private readonly int _signalPeriod;

    public MacdCalculator(int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        _fastPeriod = fastPeriod;
        _slowPeriod = slowPeriod;
        _signalPeriod = signalPeriod;
    }

    public MacdResult Calculate(IReadOnlyList<decimal> prices)
    {
        if (prices.Count < _slowPeriod)
            return new MacdResult(0, 0, 0);

        var fastEma = CalculateEma(prices, _fastPeriod);
        var slowEma = CalculateEma(prices, _slowPeriod);
        var macdLine = fastEma - slowEma;

        // Simplified: signal line would need historical MACD values
        var signalLine = macdLine * 0.9m;
        var histogram = macdLine - signalLine;

        return new MacdResult(macdLine, signalLine, histogram);
    }

    private decimal CalculateEma(IReadOnlyList<decimal> prices, int period)
    {
        var multiplier = 2m / (period + 1);
        var ema = prices[0];

        for (int i = 1; i < prices.Count; i++)
        {
            ema = (prices[i] - ema) * multiplier + ema;
        }

        return ema;
    }
}

public record MacdResult(decimal MacdLine, decimal SignalLine, decimal Histogram);

public class BollingerCalculator
{
    private readonly int _period;
    private readonly decimal _standardDeviations;

    public BollingerCalculator(int period = 20, decimal standardDeviations = 2m)
    {
        _period = period;
        _standardDeviations = standardDeviations;
    }

    public BollingerResult Calculate(IReadOnlyList<decimal> prices)
    {
        if (prices.Count < _period)
            return new BollingerResult(0, 0, 0);

        var recentPrices = prices.TakeLast(_period).ToList();
        var middle = recentPrices.Average();

        var squaredDiffs = recentPrices.Select(p => (p - middle) * (p - middle));
        var variance = squaredDiffs.Average();
        var stdDev = (decimal)Math.Sqrt((double)variance);

        var upper = middle + (_standardDeviations * stdDev);
        var lower = middle - (_standardDeviations * stdDev);

        return new BollingerResult(upper, middle, lower);
    }
}

public record BollingerResult(decimal Upper, decimal Middle, decimal Lower);
