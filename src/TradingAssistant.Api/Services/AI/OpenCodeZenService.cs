using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;

namespace TradingAssistant.Api.Services.AI;

public class OpenCodeZenService : IAiAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly ILogger<OpenCodeZenService> _logger;

    public OpenCodeZenService(
        HttpClient httpClient,
        IConfiguration config,
        AppDbContext db,
        ILogger<OpenCodeZenService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _db = db;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_config["AiProvider:BaseUrl"] ?? "https://api.opencode.ai/v1");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config["AiProvider:ApiKey"]}");
    }

    public async Task<MarketAnalysis> AnalyzeMarketAsync(string symbol, string timeframe = "H4")
    {
        _logger.LogInformation("Analyzing market for {Symbol} on {Timeframe}", symbol, timeframe);

        var prompt = $"""
            Analyze the current market conditions for {symbol} on the {timeframe} timeframe.

            Provide your analysis in the following JSON format:
            {{
                "pair": "{symbol}",
                "bias": "bullish|bearish|neutral",
                "confidence": 0.0-1.0,
                "key_levels": {{ "support": 0.0, "resistance": 0.0 }},
                "risk_events": ["event1", "event2"],
                "recommendation": "buy|sell|wait|reduce_exposure",
                "reasoning": "detailed explanation"
            }}
            """;

        var response = await CallApiAsync(prompt);

        try
        {
            var analysis = JsonSerializer.Deserialize<MarketAnalysis>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return analysis ?? new MarketAnalysis { Pair = symbol };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI response for market analysis");
            return new MarketAnalysis
            {
                Pair = symbol,
                Reasoning = response
            };
        }
    }

    public async Task<string> EnrichAlertAsync(string symbol, decimal price, string alertMessage)
    {
        _logger.LogDebug("Enriching alert for {Symbol} at {Price}", symbol, price);

        var prompt = $"""
            An alert was triggered for {symbol} at price {price}.
            Original alert: {alertMessage}

            Provide brief market context (2-3 sentences) explaining why this price level
            is significant and what traders should watch for next.
            """;

        return await CallApiAsync(prompt);
    }

    public async Task<TradeReview> ReviewTradeAsync(long tradeId)
    {
        var trade = await _db.TradeEntries
            .Include(t => t.Tags)
            .Include(t => t.Notes)
            .FirstOrDefaultAsync(t => t.Id == tradeId);

        if (trade is null)
            throw new InvalidOperationException($"Trade {tradeId} not found");

        _logger.LogInformation("Reviewing trade {TradeId}", tradeId);

        var prompt = $"""
            Review this trade and provide feedback:

            Symbol: {trade.Symbol}
            Direction: {trade.Direction}
            Entry: {trade.EntryPrice}
            Exit: {trade.ExitPrice}
            Stop Loss: {trade.StopLoss}
            Take Profit: {trade.TakeProfit}
            PnL: {trade.NetPnL}
            Duration: {trade.Duration}
            R:R Ratio: {trade.RiskRewardRatio}
            Tags: {string.Join(", ", trade.Tags.Select(t => t.Name))}

            Provide your review in the following JSON format:
            {{
                "assessment": "overall assessment",
                "strengths": ["strength1", "strength2"],
                "weaknesses": ["weakness1", "weakness2"],
                "improvements": ["improvement1", "improvement2"],
                "score": 1-10
            }}
            """;

        var response = await CallApiAsync(prompt);

        try
        {
            var review = JsonSerializer.Deserialize<TradeReview>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (review is not null)
                review.TradeId = tradeId;

            return review ?? new TradeReview { TradeId = tradeId };
        }
        catch (JsonException)
        {
            return new TradeReview
            {
                TradeId = tradeId,
                Assessment = response
            };
        }
    }

    public async Task<string> GenerateDailyBriefingAsync(IEnumerable<string> watchlist)
    {
        _logger.LogInformation("Generating daily briefing for watchlist");

        var symbols = string.Join(", ", watchlist);

        var prompt = $"""
            Generate a morning market briefing for a forex trader watching these pairs: {symbols}

            Include:
            1. Key market themes for today
            2. Important economic events
            3. Brief technical outlook for each pair (1-2 sentences each)
            4. Risk considerations

            Keep it concise and actionable.
            """;

        return await CallApiAsync(prompt);
    }

    public async Task<NewsSentiment> AnalyzeNewsAsync(string symbol)
    {
        _logger.LogDebug("Analyzing news sentiment for {Symbol}", symbol);

        // TODO: Integrate with news RSS feeds to fetch actual news
        var prompt = $"""
            Analyze the current news sentiment for {symbol}.

            Provide your analysis in the following JSON format:
            {{
                "symbol": "{symbol}",
                "overall_sentiment": "bullish|bearish|neutral|mixed",
                "sentiment_score": -1.0 to 1.0,
                "relevant_news": [
                    {{ "title": "...", "source": "...", "sentiment": "...", "published_at": "..." }}
                ]
            }}
            """;

        var response = await CallApiAsync(prompt);

        try
        {
            return JsonSerializer.Deserialize<NewsSentiment>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new NewsSentiment { Symbol = symbol };
        }
        catch (JsonException)
        {
            return new NewsSentiment
            {
                Symbol = symbol,
                OverallSentiment = "unknown"
            };
        }
    }

    private async Task<string> CallApiAsync(string prompt)
    {
        var model = _config["AiProvider:Model"] ?? "opencode/glm-4.7";
        var maxTokens = _config.GetValue<int>("AiProvider:MaxTokens", 4096);
        var temperature = _config.GetValue<decimal>("AiProvider:Temperature", 0.3m);

        var request = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = "You are a professional forex trading analyst. Provide concise, actionable analysis." },
                new { role = "user", content = prompt }
            },
            max_tokens = maxTokens,
            temperature
        };

        var response = await _httpClient.PostAsJsonAsync("chat/completions", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenAiResponse>();

        return result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
    }
}

internal class OpenAiResponse
{
    public List<Choice>? Choices { get; set; }
}

internal class Choice
{
    public Message? Message { get; set; }
}

internal class Message
{
    public string? Content { get; set; }
}
