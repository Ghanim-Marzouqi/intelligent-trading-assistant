using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Analytics;
using TradingAssistant.Api.Services.AI;
using TradingAssistant.Api.Services.CTrader;

namespace TradingAssistant.Api.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly IAiAnalysisService _aiService;
    private readonly ICTraderHistoricalData _historicalData;
    private readonly AppDbContext _db;
    private readonly ILogger<AiController> _logger;

    public AiController(
        IAiAnalysisService aiService,
        ICTraderHistoricalData historicalData,
        AppDbContext db,
        ILogger<AiController> logger)
    {
        _aiService = aiService;
        _historicalData = historicalData;
        _db = db;
        _logger = logger;
    }

    [HttpGet("analyze/{symbol}")]
    public async Task<ActionResult<MarketAnalysis>> AnalyzeMarket(
        string symbol,
        [FromQuery] string timeframe = "H4")
    {
        if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 10)
            return BadRequest("Invalid symbol");

        var allowedTimeframes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "M1", "M5", "M15", "M30", "H1", "H4", "D1" };
        if (!allowedTimeframes.Contains(timeframe))
            return BadRequest($"Invalid timeframe. Allowed: {string.Join(", ", allowedTimeframes)}");

        try
        {
            var analysis = await _aiService.AnalyzeMarketAsync(symbol.ToUpperInvariant(), timeframe);
            await SaveSnapshotAsync(analysis, "manual");
            return Ok(analysis);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            return StatusCode(503, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing market for {Symbol}", symbol);
            return StatusCode(500, new { error = "An error occurred during market analysis." });
        }
    }

    [HttpGet("review/{tradeId:long}")]
    public async Task<ActionResult<TradeReview>> ReviewTrade(long tradeId)
    {
        try
        {
            var review = await _aiService.ReviewTradeAsync(tradeId);
            return Ok(review);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            return StatusCode(503, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reviewing trade {TradeId}", tradeId);
            return StatusCode(500, new { error = "An error occurred during trade review." });
        }
    }

    [HttpPost("briefing")]
    public async Task<ActionResult<BriefingResponse>> GenerateBriefing([FromBody] BriefingRequest request)
    {
        if (request.Watchlist is null || request.Watchlist.Count == 0)
            return BadRequest(new { error = "Watchlist is required" });

        try
        {
            var briefing = await _aiService.GenerateDailyBriefingAsync(request.Watchlist);
            return Ok(new BriefingResponse(briefing));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            return StatusCode(503, new { error = ex.Message });
        }
    }

    [HttpGet("news/{symbol}")]
    public async Task<ActionResult<NewsSentiment>> AnalyzeNews(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 10)
            return BadRequest("Invalid symbol");

        try
        {
            var sentiment = await _aiService.AnalyzeNewsAsync(symbol.ToUpperInvariant());
            return Ok(sentiment);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            return StatusCode(503, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing news for {Symbol}", symbol);
            return StatusCode(500, new { error = "An error occurred during news analysis." });
        }
    }
    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<AnalysisSnapshot>>> GetAnalysisHistory(
        [FromQuery] string? symbol = null,
        [FromQuery] int limit = 50)
    {
        var query = _db.AnalysisSnapshots.AsQueryable();

        if (!string.IsNullOrEmpty(symbol))
            query = query.Where(a => a.Symbol == symbol.ToUpperInvariant());

        return await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    private async Task SaveSnapshotAsync(MarketAnalysis analysis, string source)
    {
        try
        {
            var snapshot = new AnalysisSnapshot
            {
                Symbol = analysis.Pair,
                Bias = analysis.Bias,
                Confidence = analysis.Confidence,
                Recommendation = analysis.Recommendation,
                Reasoning = analysis.Reasoning,
                Support = analysis.KeyLevels.Support,
                Resistance = analysis.KeyLevels.Resistance,
                TradeDirection = analysis.Trade?.Direction,
                TradeEntry = analysis.Trade?.Entry,
                TradeStopLoss = analysis.Trade?.StopLoss,
                TradeTakeProfit = analysis.Trade?.TakeProfit,
                TradeLotSize = analysis.Trade?.LotSize,
                TradeRiskReward = analysis.Trade?.RiskRewardRatio,
                MarginRequired = analysis.Trade?.MarginRequired,
                LeverageWarning = analysis.Trade?.LeverageWarning,
                Source = source,
                CreatedAt = DateTime.UtcNow
            };

            _db.AnalysisSnapshots.Add(snapshot);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save analysis snapshot for {Symbol}", analysis.Pair);
        }
    }

    [HttpGet("candles/{symbol}")]
    public async Task<ActionResult<IEnumerable<CandleDto>>> GetCandles(
        string symbol,
        [FromQuery] string timeframe = "H4",
        [FromQuery] int count = 100)
    {
        if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 10)
            return BadRequest("Invalid symbol");

        if (count < 10 || count > 500)
            return BadRequest("Count must be between 10 and 500");

        var period = TrendbarPeriodMapper.Parse(timeframe);

        try
        {
            var candles = await _historicalData.GetCandlesAsync(symbol.ToUpperInvariant(), period, count);
            var dtos = candles.Select(c => new CandleDto(
                new DateTimeOffset(c.Timestamp, TimeSpan.Zero).ToUnixTimeSeconds(),
                c.Open, c.High, c.Low, c.Close, c.Volume));
            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching candles for {Symbol}", symbol);
            return StatusCode(500, new { error = "Failed to fetch candle data." });
        }
    }
}

public record CandleDto(long Time, decimal Open, decimal High, decimal Low, decimal Close, long Volume);
public record BriefingRequest(List<string>? Watchlist);
public record BriefingResponse(string Briefing);
