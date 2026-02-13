using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingAssistant.Api.Services.AI;

namespace TradingAssistant.Api.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly IAiAnalysisService _aiService;
    private readonly ILogger<AiController> _logger;

    public AiController(IAiAnalysisService aiService, ILogger<AiController> logger)
    {
        _aiService = aiService;
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
            return Ok(analysis);
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

        var briefing = await _aiService.GenerateDailyBriefingAsync(request.Watchlist);
        return Ok(new BriefingResponse(briefing));
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing news for {Symbol}", symbol);
            return StatusCode(500, new { error = "An error occurred during news analysis." });
        }
    }
}

public record BriefingRequest(List<string>? Watchlist);
public record BriefingResponse(string Briefing);
