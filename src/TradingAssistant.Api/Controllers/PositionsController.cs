using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Trading;
using TradingAssistant.Api.Services;
using TradingAssistant.Api.Services.CTrader;

namespace TradingAssistant.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PositionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICTraderOrderExecutor _orderExecutor;
    private readonly ICTraderSymbolResolver _symbolResolver;
    private readonly ILogger<PositionsController> _logger;

    public PositionsController(
        AppDbContext db,
        ICTraderOrderExecutor orderExecutor,
        ICTraderSymbolResolver symbolResolver,
        ILogger<PositionsController> logger)
    {
        _db = db;
        _orderExecutor = orderExecutor;
        _symbolResolver = symbolResolver;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Position>>> GetOpenPositions()
    {
        return await _db.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .OrderByDescending(p => p.OpenTime)
            .ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Position>> GetPosition(long id)
    {
        var position = await _db.Positions.FindAsync(id);
        if (position is null)
            return NotFound();

        return position;
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<Position>>> GetPositionHistory(
        [FromQuery] string? symbol = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] int limit = 50)
    {
        var query = _db.Positions
            .Where(p => p.Status == PositionStatus.Closed);

        if (!string.IsNullOrEmpty(symbol))
            query = query.Where(p => p.Symbol == symbol.ToUpperInvariant());

        if (from.HasValue)
            query = query.Where(p => p.CloseTime >= from.Value);

        return await query
            .OrderByDescending(p => p.CloseTime)
            .Take(limit)
            .ToListAsync();
    }

    [HttpPost("{id}/close")]
    public async Task<IActionResult> ClosePosition(long id)
    {
        var position = await _db.Positions.FindAsync(id);
        if (position is null)
            return NotFound();

        if (position.Status != PositionStatus.Open)
            return BadRequest("Position is not open");

        var result = await _orderExecutor.ClosePositionAsync(position.CTraderPositionId);
        if (!result.Success)
            return StatusCode(500, result.ErrorMessage);

        _logger.LogInformation("Closed position {PositionId}", id);

        return NoContent();
    }

    [HttpPost("{id}/modify")]
    public async Task<IActionResult> ModifyPosition(long id, ModifyPositionRequest request)
    {
        var position = await _db.Positions.FindAsync(id);
        if (position is null)
            return NotFound();

        if (position.Status != PositionStatus.Open)
            return BadRequest("Position is not open");

        var result = await _orderExecutor.ModifyPositionAsync(
            position.CTraderPositionId,
            request.StopLoss,
            request.TakeProfit);

        if (!result.Success)
            return StatusCode(500, result.ErrorMessage);

        _logger.LogInformation("Modified position {PositionId}: SL={StopLoss}, TP={TakeProfit}",
            id, request.StopLoss, request.TakeProfit);

        return NoContent();
    }

    [HttpGet("account")]
    public async Task<ActionResult<AccountInfo>> GetAccount()
    {
        var account = await _db.Accounts
            .Where(a => a.IsActive)
            .FirstOrDefaultAsync();

        if (account is null)
            return NotFound(new { error = "No active account" });

        var openPositions = await _db.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .ToListAsync();

        var unrealizedPnL = openPositions.Sum(p => p.UnrealizedPnL);
        var equity = account.Equity > 0 ? account.Equity : account.Balance + unrealizedPnL;
        var freeMargin = equity - openPositions.Sum(p => p.Volume * 1000m);

        return new AccountInfo(
            Balance: account.Balance,
            Equity: equity,
            UnrealizedPnL: unrealizedPnL,
            FreeMargin: freeMargin
        );
    }

    [HttpGet("summary")]
    public async Task<ActionResult<PositionSummary>> GetPositionSummary()
    {
        var openPositions = await _db.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .ToListAsync();

        return new PositionSummary(
            TotalPositions: openPositions.Count,
            TotalVolume: openPositions.Sum(p => p.Volume),
            UnrealizedPnL: openPositions.Sum(p => p.UnrealizedPnL),
            BySymbol: openPositions
                .GroupBy(p => p.Symbol)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Volume))
        );
    }
    [HttpGet("symbols")]
    public async Task<ActionResult<IEnumerable<SymbolInfo>>> GetSymbols()
    {
        var raw = await _db.Symbols
            .Where(s => s.IsActive)
            .Select(s => new { s.Name, s.BaseCurrency, s.QuoteCurrency, s.Description, s.MinVolume, s.MaxVolume, s.VolumeStep, s.Digits })
            .ToListAsync();

        var symbols = raw
            .Select(s => new SymbolInfo(
                s.Name, s.MinVolume, s.MaxVolume, s.VolumeStep, s.Digits,
                SymbolCategorizer.Categorize(s.Name, s.BaseCurrency, s.QuoteCurrency),
                string.IsNullOrWhiteSpace(s.Description) ? $"{s.BaseCurrency}/{s.QuoteCurrency}" : s.Description))
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Name)
            .ToList();

        return Ok(symbols);
    }

    [HttpPost("open")]
    public async Task<ActionResult<OrderResult>> OpenPosition(OpenPositionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
            return BadRequest(new { error = "Symbol is required" });

        if (request.Direction is not "Buy" and not "Sell")
            return BadRequest(new { error = "Direction must be 'Buy' or 'Sell'" });

        if (request.Volume <= 0)
            return BadRequest(new { error = "Volume must be greater than 0" });

        if (request.OrderType is not "Market" and not "Limit" and not "Stop")
            return BadRequest(new { error = "OrderType must be 'Market', 'Limit', or 'Stop'" });

        if (request.OrderType is "Limit" or "Stop" && !request.Price.HasValue)
            return BadRequest(new { error = "Price is required for Limit and Stop orders" });

        if (!_symbolResolver.IsInitialized || !_symbolResolver.TryGetSymbolId(request.Symbol, out _))
            return BadRequest(new { error = $"Unknown symbol: {request.Symbol}" });

        OrderResult result;
        try
        {
            result = request.OrderType switch
            {
                "Market" => await _orderExecutor.PlaceMarketOrderAsync(
                    request.Symbol, request.Direction, request.Volume,
                    request.StopLoss, request.TakeProfit),
                "Limit" => await _orderExecutor.PlaceLimitOrderAsync(
                    request.Symbol, request.Direction, request.Volume,
                    request.Price!.Value, request.StopLoss, request.TakeProfit),
                "Stop" => await _orderExecutor.PlaceStopOrderAsync(
                    request.Symbol, request.Direction, request.Volume,
                    request.Price!.Value, request.StopLoss, request.TakeProfit),
                _ => throw new InvalidOperationException($"Unsupported order type: {request.OrderType}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open position for {Symbol}", request.Symbol);
            return StatusCode(500, new { error = ex.Message });
        }

        if (!result.Success)
            return BadRequest(new { error = result.ErrorMessage ?? "Order failed" });

        _logger.LogInformation("Opened {OrderType} {Direction} position for {Symbol}, volume {Volume}",
            request.OrderType, request.Direction, request.Symbol, request.Volume);

        return Ok(result);
    }
}

public record ModifyPositionRequest(decimal? StopLoss, decimal? TakeProfit);
public record PositionSummary(int TotalPositions, decimal TotalVolume, decimal UnrealizedPnL, Dictionary<string, decimal> BySymbol);
public record AccountInfo(decimal Balance, decimal Equity, decimal UnrealizedPnL, decimal FreeMargin);
public record SymbolInfo(string Name, decimal MinVolume, decimal MaxVolume, decimal VolumeStep, int Digits, string Category, string Description);
public record OpenPositionRequest(
    string Symbol,
    string Direction,
    string OrderType,
    decimal Volume,
    decimal? Price,
    decimal? StopLoss,
    decimal? TakeProfit
);
