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

    /// <summary>
    /// Fallback: infer category from symbol name when BaseCurrency/QuoteCurrency are unavailable.
    /// Checks if the symbol name starts with a known code prefix.
    /// </summary>
    public static string InferFromName(string symbolName)
    {
        var upper = symbolName.ToUpperInvariant();

        foreach (var code in CryptoCodes)
            if (upper.StartsWith(code, StringComparison.Ordinal))
                return "Crypto";

        foreach (var code in MetalCodes)
            if (upper.StartsWith(code, StringComparison.Ordinal))
                return "Metals";

        if (upper.Contains("OIL") || upper.Contains("BRENT") || upper.Contains("WTI") || upper.Contains("NATGAS"))
            return "Energies";

        foreach (var code in EnergyCodes)
            if (upper.StartsWith(code, StringComparison.Ordinal))
                return "Energies";

        if (IndexPattern.IsMatch(upper))
            return "Indices";

        // Check if it looks like a forex pair (6 chars, both halves are fiat codes)
        if (upper.Length == 6)
        {
            var first = upper[..3];
            var second = upper[3..];
            if (FiatCodes.Contains(first) && FiatCodes.Contains(second))
                return "Forex";
        }

        return "Other";
    }
}
