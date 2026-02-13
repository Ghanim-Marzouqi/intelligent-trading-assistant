using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Services.Alerts.Indicators;
using TradingAssistant.Api.Services.CTrader;
using TradingAssistant.Api.Services.Orders;

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
    private readonly ICTraderHistoricalData _historicalData;
    private readonly IPositionSizer _positionSizer;
    private readonly bool _isConfigured;

    public OpenCodeZenService(
        HttpClient httpClient,
        IConfiguration config,
        AppDbContext db,
        ILogger<OpenCodeZenService> logger,
        ICTraderPriceStream priceStream,
        ICTraderHistoricalData historicalData,
        IPositionSizer positionSizer)
    {
        _httpClient = httpClient;
        _config = config;
        _db = db;
        _logger = logger;
        _priceStream = priceStream;
        _historicalData = historicalData;
        _positionSizer = positionSizer;

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

            DECISION GUIDELINES — use these to determine your recommendation:
            - "buy": RSI < 30 (oversold), price near/below Bollinger Lower, or MACD histogram turning positive
            - "sell": RSI > 70 (overbought), price near/above Bollinger Upper, or MACD histogram turning negative
            - "reduce_exposure": multiple conflicting signals with high volatility (wide Bollinger bands, extreme RSI)
            - "wait": ONLY when indicators are genuinely mixed/neutral (RSI 40-60, price near Bollinger Middle, flat MACD)

            Be decisive. If the data shows a clear directional signal, recommend "buy" or "sell" with appropriate confidence.
            Do NOT default to "wait" unless the indicators truly show no directional bias.

            TRADE SUGGESTION: If your recommendation is "buy" or "sell", you MUST also provide a "trade" object with concrete price levels.
            - Place stop_loss beyond the nearest support (for buy) or resistance (for sell) with a small buffer.
            - Place take_profit targeting at least a 2:1 reward-to-risk ratio. Prefer 2.5:1 or 3:1 when market structure allows.
            - If recommendation is "wait" or "reduce_exposure", set "trade" to null.

            Respond ONLY with valid JSON in this exact format (no markdown, no code fences):
            {
                "pair": "{{symbol}}",
                "bias": "bullish|bearish|neutral",
                "confidence": 0.0-1.0,
                "key_levels": { "support": 0, "resistance": 0 },
                "risk_events": ["event1", "event2"],
                "recommendation": "buy|sell|wait|reduce_exposure",
                "reasoning": "detailed explanation referencing the real data and why this recommendation was chosen",
                "trade": {
                    "order_type": "market|limit|stop",
                    "direction": "buy|sell",
                    "entry": <price>,
                    "stop_loss": <price>,
                    "take_profit": <price>,
                    "rationale": "brief explanation of why these specific levels were chosen"
                }
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

                // Post-process trade suggestion
                await PostProcessTradeAsync(analysis, symbol);

                // Attach market session info
                AttachSessionInfo(analysis, symbol);

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

    private async Task PostProcessTradeAsync(MarketAnalysis analysis, string symbol)
    {
        if (analysis.Trade is null)
            return;

        try
        {
            var trade = analysis.Trade;

            // Validate SL is on correct side of entry
            if (trade.Direction.Equals("buy", StringComparison.OrdinalIgnoreCase) && trade.StopLoss >= trade.Entry)
            {
                _logger.LogWarning("Invalid trade: buy SL ({SL}) >= entry ({Entry}), clearing trade", trade.StopLoss, trade.Entry);
                analysis.Trade = null;
                return;
            }
            if (trade.Direction.Equals("sell", StringComparison.OrdinalIgnoreCase) && trade.StopLoss <= trade.Entry)
            {
                _logger.LogWarning("Invalid trade: sell SL ({SL}) <= entry ({Entry}), clearing trade", trade.StopLoss, trade.Entry);
                analysis.Trade = null;
                return;
            }

            var slDistance = Math.Abs(trade.Entry - trade.StopLoss);
            if (slDistance == 0)
            {
                analysis.Trade = null;
                return;
            }

            var tpDistance = Math.Abs(trade.TakeProfit - trade.Entry);
            var rr = tpDistance / slDistance;

            // Enforce configurable minimum R:R
            var minRR = _config.GetValue<decimal>("Risk:MinRiskRewardRatio", 1.5m);
            if (rr < minRR)
            {
                _logger.LogInformation(
                    "Adjusting {Symbol} R:R from {OriginalRR}:1 to {MinRR}:1 (below minimum)",
                    symbol, Math.Round(rr, 2), minRR);

                var adjustedTpDistance = slDistance * minRR;
                trade.TakeProfit = trade.Direction.Equals("buy", StringComparison.OrdinalIgnoreCase)
                    ? trade.Entry + adjustedTpDistance
                    : trade.Entry - adjustedTpDistance;
                tpDistance = adjustedTpDistance;
                rr = minRR;
            }

            trade.RiskRewardRatio = Math.Round(rr, 2);
            var riskPercent = _config.GetValue<decimal>("Risk:DefaultRiskPercent", 1.0m);
            trade.RiskPercent = riskPercent;

            // Calculate pip size
            var pipSize = symbol.Contains("JPY") ? 0.01m
                : (symbol.Contains("XAU") || symbol.Contains("GOLD")) ? 0.1m
                : 0.0001m;

            trade.PipsAtRisk = Math.Round(slDistance / pipSize, 1);
            trade.PipsToTarget = Math.Round(tpDistance / pipSize, 1);

            // Get account balance for risk calculation
            var account = await _db.Accounts.FirstOrDefaultAsync(a => a.IsActive);
            if (account is not null)
            {
                trade.RiskAmount = Math.Round(account.Balance * (riskPercent / 100m), 2);
                trade.PotentialReward = Math.Round(trade.RiskAmount * trade.RiskRewardRatio, 2);
            }

            // Calculate lot size via position sizer
            trade.LotSize = await _positionSizer.CalculateAsync(symbol, riskPercent, trade.Entry, trade.StopLoss);

            // Check margin requirements
            var margin = await _positionSizer.CalculateMarginAsync(symbol, trade.LotSize, trade.Entry);
            trade.MarginRequired = margin.Required;

            if (!margin.Sufficient)
            {
                // Calculate minimum leverage needed: marginRequired * leverage / freeMargin
                var minLeverage = margin.FreeMargin > 0
                    ? (int)Math.Ceiling((double)(margin.Required * margin.Leverage / margin.FreeMargin))
                    : 0;

                trade.LeverageWarning = $"Insufficient margin: ${margin.Required} required, ${margin.FreeMargin} available at {margin.Leverage}:1 leverage. " +
                    (minLeverage > 0 ? $"Increase leverage to at least {minLeverage}:1." : "Increase account balance or leverage.");

                _logger.LogWarning(
                    "Margin insufficient for {Symbol}: {Required} required, {Free} available at {Leverage}:1",
                    symbol, margin.Required, margin.FreeMargin, margin.Leverage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Trade post-processing failed for {Symbol}, clearing trade suggestion", symbol);
            analysis.Trade = null;
        }
    }

    private void AttachSessionInfo(MarketAnalysis analysis, string symbol)
    {
        try
        {
            var sym = _db.Symbols.FirstOrDefault(s => s.Name == symbol && s.IsActive);
            var category = sym is not null
                ? SymbolCategorizer.Categorize(sym.Name, sym.BaseCurrency, sym.QuoteCurrency)
                : "Other";
            analysis.MarketSession = MarketSessionService.GetSessionInfo(category);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to attach session info for {Symbol}", symbol);
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
            .FirstOrDefaultAsync(t => t.PositionId == tradeId);

        if (trade is null)
            throw new InvalidOperationException($"Trade {tradeId} not found");

        _logger.LogInformation("Reviewing trade {TradeId}", tradeId);

        var tagsStr = trade.Tags.Count > 0
            ? string.Join(", ", trade.Tags.Select(t => t.Name))
            : "None";
        var notesStr = trade.Notes.Count > 0
            ? string.Join(" | ", trade.Notes.Select(n => n.Content))
            : "None";

        var priceMovement = trade.ExitPrice - trade.EntryPrice;
        var priceMovementPct = trade.EntryPrice != 0
            ? Math.Round(priceMovement / trade.EntryPrice * 100, 4)
            : 0m;

        var durationStr = trade.Duration.TotalHours >= 24
            ? $"{trade.Duration.Days}d {trade.Duration.Hours}h {trade.Duration.Minutes}m"
            : trade.Duration.TotalHours >= 1
                ? $"{(int)trade.Duration.TotalHours}h {trade.Duration.Minutes}m"
                : $"{trade.Duration.Minutes}m {trade.Duration.Seconds}s";

        var prompt = $$"""
            Review this closed forex trade and provide constructive feedback.
            Focus on execution quality, timing, risk management, and what can be learned.

            === TRADE DATA ===
            Symbol: {{trade.Symbol}}
            Direction: {{trade.Direction}}
            Volume: {{trade.Volume}} lots
            Entry Price: {{trade.EntryPrice}}
            Exit Price: {{trade.ExitPrice}}
            Price Movement: {{priceMovement:+0.#####;-0.#####}} ({{priceMovementPct:+0.####;-0.####}}%)
            PnL (pips): {{trade.PnLPips:+0.#;-0.#}} pips
            Gross PnL: ${{trade.PnL:+0.00;-0.00}}
            Commission: ${{trade.Commission:0.00}}
            Swap: ${{trade.Swap:0.00}}
            Net PnL: ${{trade.NetPnL:+0.00;-0.00}}
            Stop Loss: {{(trade.StopLoss.HasValue ? trade.StopLoss.Value.ToString() : "Not set")}}
            Take Profit: {{(trade.TakeProfit.HasValue ? trade.TakeProfit.Value.ToString() : "Not set")}}
            R:R Ratio: {{(trade.RiskRewardRatio.HasValue ? $"{trade.RiskRewardRatio.Value:0.00}:1" : "N/A (no SL/TP)")}}
            Duration: {{durationStr}}
            Open Time: {{trade.OpenTime:yyyy-MM-dd HH:mm:ss}} UTC
            Close Time: {{trade.CloseTime:yyyy-MM-dd HH:mm:ss}} UTC
            Strategy: {{(string.IsNullOrEmpty(trade.Strategy) ? "Not specified" : trade.Strategy)}}
            Setup: {{(string.IsNullOrEmpty(trade.Setup) ? "Not specified" : trade.Setup)}}
            Tags: {{tagsStr}}
            Trader Notes: {{notesStr}}
            === END TRADE DATA ===

            REVIEW GUIDELINES:
            - Evaluate the trade based on what IS present, not just what's missing.
            - If the trade was profitable, acknowledge the successful execution.
            - Consider the pip movement and whether the entry/exit timing was good.
            - If SL/TP were not set, note it as an area for improvement but don't let it dominate the review.
            - Score reflects overall execution: 1-3 = poor, 4-5 = below average, 6-7 = acceptable, 8-9 = good, 10 = excellent.

            Respond ONLY with valid JSON in this exact format (no markdown, no code fences):
            {
                "assessment": "1-2 sentence overall assessment",
                "strengths": ["strength1", "strength2"],
                "weaknesses": ["weakness1", "weakness2"],
                "improvements": ["actionable improvement1", "actionable improvement2"],
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
        // Fail fast for unknown symbols
        if (!_priceStream.IsKnownSymbol(symbol))
        {
            _logger.LogWarning("Symbol {Symbol} is not recognized by the broker", symbol);
            return new MarketDataContext(
                $"[Symbol {symbol} is not recognized. Check the symbol name — common forex pairs use formats like EURUSD, USDJPY, GBPUSD.]",
                0m, 0m);
        }

        // Ensure we have a live price subscription
        var currentPrice = _priceStream.GetCurrentPrice(symbol);
        if (currentPrice is null)
        {
            _logger.LogInformation("No price data for {Symbol}, auto-subscribing...", symbol);
            await _priceStream.SubscribeAsync(symbol);

            // Wait briefly for at least one tick
            for (var i = 0; i < 20 && currentPrice is null; i++)
            {
                await Task.Delay(250);
                currentPrice = _priceStream.GetCurrentPrice(symbol);
            }
        }

        if (currentPrice is null)
            return new MarketDataContext(
                $"[No live market data available for {symbol} — the price feed did not respond.]",
                0m, 0m);

        var (bid, ask) = currentPrice.Value;
        var spread = ask - bid;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== LIVE MARKET DATA for {symbol} ===");
        sb.AppendLine($"Current Bid: {bid}");
        sb.AppendLine($"Current Ask: {ask}");
        sb.AppendLine($"Spread: {spread}");

        decimal support = bid, resistance = bid;

        // Try to fetch OHLC candles for proper indicator computation
        IReadOnlyList<Candle> candles = [];
        try
        {
            candles = await _historicalData.GetCandlesAsync(symbol, ProtoOATrendbarPeriod.H1, 50);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch OHLC candles for {Symbol}, falling back to tick data", symbol);
        }

        if (candles.Count > 0)
        {
            // Use OHLC candle close prices for indicator computation
            var closes = candles.Select(c => c.Close).ToList();
            var sessionHigh = candles.Max(c => c.High);
            var sessionLow = candles.Min(c => c.Low);
            support = sessionLow;
            resistance = sessionHigh;

            sb.AppendLine($"Data source: {candles.Count} H1 candles");
            sb.AppendLine($"Session High: {sessionHigh}");
            sb.AppendLine($"Session Low: {sessionLow}");

            // Show recent OHLC bars
            sb.AppendLine("Recent candles (newest first):");
            foreach (var c in candles.TakeLast(5).Reverse())
                sb.AppendLine($"  {c.Timestamp:yyyy-MM-dd HH:mm} O={c.Open} H={c.High} L={c.Low} C={c.Close} V={c.Volume}");

            if (closes.Count >= 15)
            {
                var rsi = new RsiCalculator(14).Calculate(closes);
                sb.AppendLine($"RSI(14): {rsi:F2}");
            }

            if (closes.Count >= 26)
            {
                var macd = new MacdCalculator(12, 26, 9).Calculate(closes);
                sb.AppendLine($"MACD Line: {macd.MacdLine:F6}");
                sb.AppendLine($"MACD Signal: {macd.SignalLine:F6}");
                sb.AppendLine($"MACD Histogram: {macd.Histogram:F6}");
            }

            if (closes.Count >= 20)
            {
                var bb = new BollingerCalculator(20, 2m).Calculate(closes);
                sb.AppendLine($"Bollinger Upper: {bb.Upper}");
                sb.AppendLine($"Bollinger Middle: {bb.Middle}");
                sb.AppendLine($"Bollinger Lower: {bb.Lower}");
                support = bb.Lower;
                resistance = bb.Upper;
            }
        }
        else
        {
            // Fallback to tick data when candles aren't available
            var history = _priceStream.GetPriceHistory(symbol);
            sb.AppendLine($"Data source: {history.Count} tick samples (candles unavailable)");

            if (history.Count > 0)
            {
                var sessionHigh = history.Max();
                var sessionLow = history.Min();
                support = sessionLow;
                resistance = sessionHigh;
                sb.AppendLine($"Session High: {sessionHigh}");
                sb.AppendLine($"Session Low: {sessionLow}");
            }

            if (history.Count >= 15)
            {
                var rsi = new RsiCalculator(14).Calculate(history);
                sb.AppendLine($"RSI(14): {rsi:F2}");
            }

            if (history.Count >= 26)
            {
                var macd = new MacdCalculator(12, 26, 9).Calculate(history);
                sb.AppendLine($"MACD Line: {macd.MacdLine:F6}");
                sb.AppendLine($"MACD Signal: {macd.SignalLine:F6}");
                sb.AppendLine($"MACD Histogram: {macd.Histogram:F6}");
            }

            if (history.Count >= 20)
            {
                var bb = new BollingerCalculator(20, 2m).Calculate(history);
                sb.AppendLine($"Bollinger Upper: {bb.Upper}");
                sb.AppendLine($"Bollinger Middle: {bb.Middle}");
                sb.AppendLine($"Bollinger Lower: {bb.Lower}");
                support = bb.Lower;
                resistance = bb.Upper;
            }
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
