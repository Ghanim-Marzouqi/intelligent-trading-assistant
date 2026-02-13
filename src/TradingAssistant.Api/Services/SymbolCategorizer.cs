using System.Text.RegularExpressions;

namespace TradingAssistant.Api.Services;

public static class SymbolCategorizer
{
    private static readonly HashSet<string> MetalCodes = new(StringComparer.OrdinalIgnoreCase)
        { "XAU", "XAG", "XPT", "XPD" };

    private static readonly HashSet<string> EnergyCodes = new(StringComparer.OrdinalIgnoreCase)
        { "XTI", "XBR", "XNG" };

    private static readonly HashSet<string> CryptoCodes = new(StringComparer.OrdinalIgnoreCase)
        { "BTC", "ETH", "LTC", "XRP", "SOL", "ADA", "DOT", "DOGE", "AVAX", "LINK", "MATIC", "UNI", "SHIB", "BNB" };

    private static readonly HashSet<string> FiatCodes = new(StringComparer.OrdinalIgnoreCase)
        { "USD", "EUR", "GBP", "JPY", "CHF", "AUD", "NZD", "CAD", "SEK", "NOK", "DKK", "SGD", "HKD", "TRY",
          "ZAR", "MXN", "PLN", "CZK", "HUF", "CNH", "CNY", "INR", "THB", "ILS", "KRW", "BRL", "CLP", "COP", "PEN" };

    private static readonly Regex IndexPattern = new(@"^[A-Z]{2,3}\d{2,3}$", RegexOptions.Compiled);

    public static string Categorize(string name, string baseCurrency, string quoteCurrency)
    {
        if (MetalCodes.Contains(baseCurrency))
            return "Metals";

        if (EnergyCodes.Contains(baseCurrency) ||
            name.Contains("OIL", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("BRENT", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("WTI", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("NATGAS", StringComparison.OrdinalIgnoreCase))
            return "Energies";

        if (CryptoCodes.Contains(baseCurrency))
            return "Crypto";

        if (IndexPattern.IsMatch(name))
            return "Indices";

        if (FiatCodes.Contains(baseCurrency) && FiatCodes.Contains(quoteCurrency))
            return "Forex";

        return "Other";
    }
}
