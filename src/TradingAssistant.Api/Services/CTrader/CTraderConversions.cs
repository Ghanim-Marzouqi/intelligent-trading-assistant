namespace TradingAssistant.Api.Services.CTrader;

public static class CTraderConversions
{
    /// <summary>
    /// Convert cTrader volume units to lots (100,000 units = 1 standard lot).
    /// </summary>
    public static decimal CentsToLots(long volumeInCents)
        => volumeInCents / 100_000m;

    /// <summary>
    /// Convert lots to cTrader volume units (1 lot = 100,000 units).
    /// </summary>
    public static long LotsToCents(decimal lots)
        => (long)(lots * 100_000m);

    /// <summary>
    /// Convert a cTrader money value (balance, commission, swap, profit) to decimal.
    /// Money values are int64 scaled by 10^moneyDigits (typically moneyDigits=2).
    /// </summary>
    public static decimal MoneyToDecimal(long moneyValue, int moneyDigits)
        => moneyValue / (decimal)Math.Pow(10, moneyDigits);

    /// <summary>
    /// Convert a cTrader spot price (uint64) to decimal using the symbol's digits.
    /// </summary>
    public static decimal PriceToDecimal(ulong price, int digits)
        => (decimal)price / (decimal)Math.Pow(10, digits);

    /// <summary>
    /// Convert a decimal price to cTrader uint64 spot price.
    /// </summary>
    public static long DecimalToPrice(decimal price, int digits)
        => (long)(price * (decimal)Math.Pow(10, digits));

    /// <summary>
    /// Convert a Unix timestamp in milliseconds to DateTime UTC.
    /// </summary>
    public static DateTime FromUnixMs(long timestampMs)
        => DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).UtcDateTime;

    /// <summary>
    /// Convert DateTime UTC to Unix timestamp in milliseconds.
    /// </summary>
    public static long ToUnixMs(DateTime dateTimeUtc)
        => new DateTimeOffset(dateTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();
}
