namespace TradingAssistant.Api.Services.AI;

public interface IAiAnalysisService
{
    Task<MarketAnalysis> AnalyzeMarketAsync(string symbol, string timeframe = "H4");
    Task<string> EnrichAlertAsync(string symbol, decimal price, string alertMessage);
    Task<TradeReview> ReviewTradeAsync(long tradeId);
    Task<string> GenerateDailyBriefingAsync(IEnumerable<string> watchlist);
    Task<NewsSentiment> AnalyzeNewsAsync(string symbol);
}

public class MarketAnalysis
{
    public string Pair { get; set; } = string.Empty;
    public string Bias { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public KeyLevels KeyLevels { get; set; } = new();
    public List<string> RiskEvents { get; set; } = [];
    public string Recommendation { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}

public class KeyLevels
{
    public decimal Support { get; set; }
    public decimal Resistance { get; set; }
}

public class TradeReview
{
    public long TradeId { get; set; }
    public string Assessment { get; set; } = string.Empty;
    public List<string> Strengths { get; set; } = [];
    public List<string> Weaknesses { get; set; } = [];
    public List<string> Improvements { get; set; } = [];
    public int Score { get; set; }
}

public class NewsSentiment
{
    public string Symbol { get; set; } = string.Empty;
    public string OverallSentiment { get; set; } = string.Empty;
    public decimal SentimentScore { get; set; }
    public List<NewsItem> RelevantNews { get; set; } = [];
}

public class NewsItem
{
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Sentiment { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
}
