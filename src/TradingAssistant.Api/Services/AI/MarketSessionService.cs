namespace TradingAssistant.Api.Services.AI;

public static class MarketSessionService
{
    private record Session(string Name, int OpenHourUtc, int CloseHourUtc);

    private static readonly Session[] ForexSessions =
    [
        new("Sydney", 22, 7),
        new("Tokyo", 0, 9),
        new("London", 8, 17),
        new("New York", 13, 22)
    ];

    public static MarketSessionInfo GetSessionInfo(string category)
    {
        var now = DateTime.UtcNow;

        if (string.Equals(category, "Crypto", StringComparison.OrdinalIgnoreCase))
        {
            return new MarketSessionInfo
            {
                IsMarketOpen = true,
                ActiveSessions = ["24/7"],
                PrimarySession = "Crypto (Always Open)",
                TradingAdvice = "Crypto markets trade around the clock. Liquidity varies — highest during US/EU overlap."
            };
        }

        // Weekend check: Fri 22:00 UTC → Sun 22:00 UTC
        if (IsWeekend(now))
        {
            var nextOpen = GetNextSundayOpen(now);
            return new MarketSessionInfo
            {
                IsMarketOpen = false,
                ActiveSessions = [],
                PrimarySession = "Market Closed",
                TradingAdvice = "Markets are closed for the weekend. Opens Sunday 22:00 UTC.",
                NextOpen = nextOpen
            };
        }

        var activeSessions = new List<string>();
        var hour = now.Hour;

        foreach (var session in ForexSessions)
        {
            if (IsInSession(hour, session.OpenHourUtc, session.CloseHourUtc))
                activeSessions.Add(session.Name);
        }

        var primarySession = GetPrimarySession(activeSessions);
        var tradingAdvice = GetTradingAdvice(activeSessions, category);

        return new MarketSessionInfo
        {
            IsMarketOpen = activeSessions.Count > 0,
            ActiveSessions = activeSessions,
            PrimarySession = primarySession,
            TradingAdvice = tradingAdvice
        };
    }

    private static bool IsInSession(int hour, int open, int close)
    {
        if (open < close)
            return hour >= open && hour < close;
        // Wraps midnight (e.g. Sydney 22-07)
        return hour >= open || hour < close;
    }

    private static bool IsWeekend(DateTime utcNow)
    {
        return utcNow.DayOfWeek switch
        {
            DayOfWeek.Saturday => true,
            DayOfWeek.Sunday => utcNow.Hour < 22,
            DayOfWeek.Friday => utcNow.Hour >= 22,
            _ => false
        };
    }

    private static DateTime GetNextSundayOpen(DateTime utcNow)
    {
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)utcNow.DayOfWeek + 7) % 7;
        if (daysUntilSunday == 0 && utcNow.Hour >= 22)
            daysUntilSunday = 7;
        var sunday = utcNow.Date.AddDays(daysUntilSunday);
        return sunday.AddHours(22);
    }

    private static string GetPrimarySession(List<string> activeSessions)
    {
        if (activeSessions.Count == 0)
            return "No Active Session";

        if (activeSessions.Contains("London") && activeSessions.Contains("New York"))
            return "London/New York Overlap";

        if (activeSessions.Contains("Tokyo") && activeSessions.Contains("London"))
            return "Tokyo/London Overlap";

        if (activeSessions.Contains("Sydney") && activeSessions.Contains("Tokyo"))
            return "Sydney/Tokyo Overlap";

        return string.Join(", ", activeSessions);
    }

    private static string GetTradingAdvice(List<string> activeSessions, string category)
    {
        if (activeSessions.Count == 0)
            return "No major session active. Expect lower liquidity and wider spreads.";

        var hasLondon = activeSessions.Contains("London");
        var hasNewYork = activeSessions.Contains("New York");
        var hasTokyo = activeSessions.Contains("Tokyo");

        if (hasLondon && hasNewYork)
        {
            return category switch
            {
                "Forex" => "Peak liquidity window. Excellent for EUR/USD, GBP/USD, and major pairs.",
                "Metals" => "High volatility for gold and silver during London/NY overlap.",
                "Energies" => "Strong volume for oil — NYMEX and ICE both active.",
                "Indices" => "US and European indices both trading. High volume expected.",
                _ => "London/New York overlap — highest liquidity of the day."
            };
        }

        if (hasLondon)
            return "London session active. Good liquidity for EUR, GBP, and CHF pairs.";

        if (hasNewYork)
            return "New York session active. Best for USD pairs and commodities.";

        if (hasTokyo)
            return "Tokyo session active. Focus on JPY, AUD, and NZD pairs.";

        return "Sydney session — lower liquidity. Consider wider stops.";
    }
}
