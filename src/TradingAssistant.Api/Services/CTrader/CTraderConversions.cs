namespace TradingAssistant.Api.Services.CTrader;

public static class CTraderConversions
{
    /// <summary>
    /// Convert cTrader volume to lots using the symbol's contract size.
    /// cTrader volume = quantity_in_base_currency × 100, so:
    /// lots = volume / (contractSize × 100).
    /// </summary>
    public static decimal VolumeToLots(long volume, decimal contractSize)
        => contractSize > 0 ? volume / (contractSize * 100m) : volume / 100_000m;

    /// <summary>
    /// Convert lots to cTrader volume using the symbol's contract size.
    /// cTrader volume = lots × contractSize × 100.
    /// </summary>
    public static long LotsToVolume(decimal lots, decimal contractSize)
        => (long)(lots * contractSize * 100m);

    /// <summary>
    /// Convert a cTrader min/max/step volume to lots using the symbol's contract size.
    /// Same formula as VolumeToLots but accepts decimal input for DB-stored values.
    /// </summary>
    public static decimal DbVolumeToLots(decimal volume, decimal contractSize)
        => contractSize > 0 ? volume / (contractSize * 100m) : volume / 100_000m;

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
