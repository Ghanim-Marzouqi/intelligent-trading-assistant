using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Services.Alerts.Indicators;
using TradingAssistant.Api.Services.CTrader;

namespace TradingAssistant.Api.Services.AI;

public class OpenCodeZenService : IAiAnalysisService
{
    private static readonly JsonSerializerOptions LlmJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly ILogger<OpenCodeZenService> _logger;
    private readonly ICTraderPriceStream _priceStream;
    private readonly bool _isConfigured;

    public OpenCodeZenService(
        HttpClient httpClient,
        IConfiguration config,
        AppDbContext db,
        ILogger<OpenCodeZenService> logger,
        ICTraderPriceStream priceStream)
    {
        _httpClient = httpClient;
        _config = config;
        _db = db;
        _logger = logger;
        _priceStream = priceStream;

        var apiKey = _config["AiProvider:ApiKey"];
        _isConfigured = !string.IsNullOrWhiteSpace(apiKey);

        var baseUrl = _config["AiProvider:BaseUrl"] ?? "https://api.opencode.ai/v1";
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        _httpClient.BaseAddress = new Uri(baseUrl);

        if (_isConfigured)
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }
        else
        {
            _logger.LogWarning("AiProvider:ApiKey is not configured. AI analysis features will be unavailable.");
        }
    }

    private void EnsureConfigured()
    {
        if (!_isConfigured)
            throw new InvalidOperationException(
                "AI provider is not configured. Set the AiProvider:ApiKey configuration value.");
    }

    public async Task<MarketAnalysis> AnalyzeMarketAsync(string symbol, string timeframe = "H4")
    {
        _logger.LogInformation("Analyzing market for {Symbol} on {Timeframe}", symbol, timeframe);

        var ctx = await BuildMarketDataContextAsync(symbol);

        var prompt = $$"""
            {{ctx.Text}}

            Based on the market data above, analyze the current conditions for {{symbol}} on the {{timeframe}} timeframe.
            Reference the actual price levels and indicator values provided. Do NOT fabricate or guess prices.

            Respond ONLY with valid JSON in this exact format (no markdown, no code fences):
            {
                "pair": "{{symbol}}",
                "bias": "bullish|bearish|neutral",
                "confidence": 0.0-1.0,
                "key_levels": { "support": 0, "resistance": 0 },
                "risk_events": ["event1", "event2"],
                "recommendation": "buy|sell|wait|reduce_exposure",
                "reasoning": "detailed explanation referencing the real data"
            }
            """;

        var response = await CallApiAsync(prompt);

        try
        {
            var analysis = JsonSerializer.Deserialize<MarketAnalysis>(response, LlmJsonOptions);

            if (analysis is not null)
            {
                // Override with computed values — never trust the LLM for numeric fields
                analysis.KeyLevels.Support = ctx.Support;
                analysis.KeyLevels.Resistance = ctx.Resistance;
                return analysis;
            }

            return new MarketAnalysis { Pair = symbol };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI response for market analysis");
            return new MarketAnalysis
            {
                Pair = symbol,
                KeyLevels = new KeyLevels { Support = ctx.Support, Resistance = ctx.Resistance },
                Reasoning = response
            };
        }
    }

    public async Task<string> EnrichAlertAsync(string symbol, decimal price, string alertMessage)
    {
        _logger.LogDebug("Enriching alert for {Symbol} at {Price}", symbol, price);

        var ctx = await BuildMarketDataContextAsync(symbol);

        var prompt = $"""
            {ctx.Text}

            An alert was triggered for {symbol} at price {price}.
            Original alert: {alertMessage}

            Using the market data above, provide brief market context (2-3 sentences)
            explaining why this price level is significant and what traders should watch for next.
            Reference actual indicator values in your response.
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

        var tagsStr = string.Join(", ", trade.Tags.Select(t => t.Name));
        var prompt = $$"""
            Review this trade and provide feedback:

            Symbol: {{trade.Symbol}}
            Direction: {{trade.Direction}}
            Entry: {{trade.EntryPrice}}
            Exit: {{trade.ExitPrice}}
            Stop Loss: {{trade.StopLoss}}
            Take Profit: {{trade.TakeProfit}}
            PnL: {{trade.NetPnL}}
            Duration: {{trade.Duration}}
            R:R Ratio: {{trade.RiskRewardRatio}}
            Tags: {{tagsStr}}

            Provide your review in the following JSON format:
            {
                "assessment": "overall assessment",
                "strengths": ["strength1", "strength2"],
                "weaknesses": ["weakness1", "weakness2"],
                "improvements": ["improvement1", "improvement2"],
                "score": 1-10
            }
            """;

        var response = await CallApiAsync(prompt);

        try
        {
            var review = JsonSerializer.Deserialize<TradeReview>(response, LlmJsonOptions);

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

        var prompt = $$"""
            Analyze the current news sentiment for {{symbol}}.

            Provide your analysis in the following JSON format:
            {
                "symbol": "{{symbol}}",
                "overall_sentiment": "bullish|bearish|neutral|mixed",
                "sentiment_score": -1.0,
                "relevant_news": [
                    { "title": "...", "source": "...", "sentiment": "...", "published_at": "..." }
                ]
            }
            """;

        var response = await CallApiAsync(prompt);

        try
        {
            return JsonSerializer.Deserialize<NewsSentiment>(response, LlmJsonOptions) ?? new NewsSentiment { Symbol = symbol };
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

    private record MarketDataContext(string Text, decimal Support, decimal Resistance);

    private async Task<MarketDataContext> BuildMarketDataContextAsync(string symbol)
    {
        const int minTicks = 5;
        const int maxWaitMs = 10_000;
        const int pollIntervalMs = 250;

        // Fail fast for unknown symbols instead of waiting 10s for nothing
        if (!_priceStream.IsKnownSymbol(symbol))
        {
            _logger.LogWarning("Symbol {Symbol} is not recognized by the broker", symbol);
            return new MarketDataContext(
                $"[Symbol {symbol} is not recognized. Check the symbol name — common forex pairs use formats like EURUSD, USDJPY, GBPUSD.]",
                0m, 0m);
        }

        // Auto-subscribe so the price stream has data for this symbol
        var currentPrice = _priceStream.GetCurrentPrice(symbol);
        var wasAlreadySubscribed = currentPrice is not null;

        if (currentPrice is null)
        {
            _logger.LogInformation("No price data for {Symbol}, auto-subscribing and waiting for ticks...", symbol);
            await _priceStream.SubscribeAsync(symbol);
        }

        // Wait for enough ticks to compute meaningful indicators
        var elapsed = 0;
        while (elapsed < maxWaitMs)
        {
            currentPrice = _priceStream.GetCurrentPrice(symbol);
            var tickCount = _priceStream.GetPriceHistory(symbol).Count;
            if (currentPrice is not null && (tickCount >= minTicks || (wasAlreadySubscribed && tickCount >= 1)))
                break;
            await Task.Delay(pollIntervalMs);
            elapsed += pollIntervalMs;
        }

        // Final read after wait
        currentPrice = _priceStream.GetCurrentPrice(symbol);

        if (currentPrice is null)
            return new MarketDataContext(
                $"[No live market data available for {symbol} — the price feed did not respond.]",
                0m, 0m);

        var (bid, ask) = currentPrice.Value;
        var spread = ask - bid;
        var history = _priceStream.GetPriceHistory(symbol);

        _logger.LogInformation("Building market context for {Symbol}: {TickCount} ticks, bid={Bid}, ask={Ask}",
            symbol, history.Count, bid, ask);

        decimal sessionHigh = bid, sessionLow = bid;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== LIVE MARKET DATA for {symbol} ===");
        sb.AppendLine($"Current Bid: {bid}");
        sb.AppendLine($"Current Ask: {ask}");
        sb.AppendLine($"Spread: {spread}");
        sb.AppendLine($"Tick samples: {history.Count}");

        if (history.Count > 0)
        {
            sessionHigh = history.Max();
            sessionLow = history.Min();
            sb.AppendLine($"Session High: {sessionHigh}");
            sb.AppendLine($"Session Low: {sessionLow}");
        }

        decimal support = sessionLow;
        decimal resistance = sessionHigh;

        if (history.Count >= 15) // RSI needs period + 1
        {
            var rsi = new RsiCalculator(14).Calculate(history);
            sb.AppendLine($"RSI(14): {rsi}");
        }

        if (history.Count >= 26) // MACD needs slow period
        {
            var macd = new MacdCalculator(12, 26, 9).Calculate(history);
            sb.AppendLine($"MACD Line: {macd.MacdLine:F6}");
            sb.AppendLine($"MACD Signal: {macd.SignalLine:F6}");
            sb.AppendLine($"MACD Histogram: {macd.Histogram:F6}");
        }

        if (history.Count >= 20) // Bollinger needs period
        {
            var bb = new BollingerCalculator(20, 2m).Calculate(history);
            sb.AppendLine($"Bollinger Upper: {bb.Upper}");
            sb.AppendLine($"Bollinger Middle: {bb.Middle}");
            sb.AppendLine($"Bollinger Lower: {bb.Lower}");
            support = bb.Lower;
            resistance = bb.Upper;
        }

        sb.AppendLine("=== END MARKET DATA ===");
        return new MarketDataContext(sb.ToString(), Math.Round(support, 5), Math.Round(resistance, 5));
    }

    private async Task<string> CallApiAsync(string prompt)
    {
        EnsureConfigured();

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

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("AI API returned {StatusCode}: {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"AI API returned {(int)response.StatusCode}: {errorBody}");
        }

        var responseBody = await response.Content.ReadAsStringAsync();

        try
        {
            var result = JsonSerializer.Deserialize<OpenAiResponse>(responseBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            var message = result?.Choices?.FirstOrDefault()?.Message;
            // Thinking models put output in content, reasoning in reasoning_content
            return !string.IsNullOrEmpty(message?.Content) ? message.Content
                 : !string.IsNullOrEmpty(message?.ReasoningContent) ? message.ReasoningContent
                 : responseBody;
        }
        catch (JsonException)
        {
            // API returned plain text instead of OpenAI-compatible JSON
            _logger.LogDebug("AI response is not OpenAI-compatible JSON, returning raw text");
            return responseBody;
        }
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
    public string? ReasoningContent { get; set; }
}
