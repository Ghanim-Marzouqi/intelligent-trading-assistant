using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Trading;
using TradingAssistant.Api.Services;
using TradingAssistant.Api.Services.CTrader;
using TradingAssistant.Api.Services.Orders;

namespace TradingAssistant.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PositionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICTraderOrderExecutor _orderExecutor;
    private readonly ICTraderSymbolResolver _symbolResolver;
    private readonly IRiskGuard _riskGuard;
    private readonly ILogger<PositionsController> _logger;

    public PositionsController(
        AppDbContext db,
        ICTraderOrderExecutor orderExecutor,
        ICTraderSymbolResolver symbolResolver,
        IRiskGuard riskGuard,
        ILogger<PositionsController> logger)
    {
        _db = db;
        _orderExecutor = orderExecutor;
        _symbolResolver = symbolResolver;
        _riskGuard = riskGuard;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PositionDto>>> GetOpenPositions()
    {
        var positions = await _db.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .OrderByDescending(p => p.OpenTime)
            .ToListAsync();

        return positions.Select(ToDto).ToList();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PositionDto>> GetPosition(long id)
    {
        var position = await _db.Positions.FindAsync(id);
        if (position is null)
            return NotFound();

        return ToDto(position);
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<PositionDto>>> GetPositionHistory(
        [FromQuery] string? symbol = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 500);

        var query = _db.Positions
            .Where(p => p.Status == PositionStatus.Closed);

        if (!string.IsNullOrEmpty(symbol))
            query = query.Where(p => p.Symbol == symbol.ToUpperInvariant());

        if (from.HasValue)
            query = query.Where(p => p.CloseTime >= from.Value);

        var positions = await query
            .OrderByDescending(p => p.CloseTime)
            .Take(limit)
            .ToListAsync();

        return positions.Select(ToDto).ToList();
    }

    [HttpPost("{id}/close")]
    [EnableRateLimiting("trading")]
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
    [EnableRateLimiting("trading")]
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
        var leverage = account.Leverage > 0 ? account.Leverage : 1;
        var usedMargin = openPositions.Sum(p => p.Volume * 100_000m * p.EntryPrice / leverage);
        var freeMargin = equity - usedMargin;

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

        // Convert from cTrader volume units (100,000 = 1 lot) to lots for the UI
        var symbols = raw
            .Select(s => new SymbolInfo(
                s.Name,
                s.MinVolume / 100_000m,
                s.MaxVolume / 100_000m,
                s.VolumeStep / 100_000m,
                s.Digits,
                SymbolCategorizer.Categorize(s.Name, s.BaseCurrency, s.QuoteCurrency),
                string.IsNullOrWhiteSpace(s.Description) ? $"{s.BaseCurrency}/{s.QuoteCurrency}" : s.Description))
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Name)
            .ToList();

        return Ok(symbols);
    }

    [HttpPost("open")]
    [EnableRateLimiting("trading")]
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

        // Validate volume against broker symbol constraints (DB stores cTrader units, convert to lots)
        var symbolInfo = await _db.Symbols.FirstOrDefaultAsync(s => s.Name == request.Symbol && s.IsActive);
        if (symbolInfo != null)
        {
            var minLot = symbolInfo.MinVolume / 100_000m;
            var maxLot = symbolInfo.MaxVolume / 100_000m;
            var volumeStep = symbolInfo.VolumeStep / 100_000m;

            if (request.Volume < minLot)
                return BadRequest(new { error = $"Volume {request.Volume} below minimum {minLot} lots" });
            if (request.Volume > maxLot)
                return BadRequest(new { error = $"Volume {request.Volume} exceeds maximum {maxLot} lots" });

            if (volumeStep > 0)
            {
                var remainder = Math.Round((request.Volume - minLot) % volumeStep, 8);
                if (remainder != 0 && Math.Abs(remainder) > 0.000001m)
                    return BadRequest(new { error = $"Volume must be in steps of {volumeStep} lots" });
            }
        }

        // Validate account has positive balance
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.IsActive);
        if (account is null)
            return BadRequest(new { error = "No active trading account" });
        if (account.Balance <= 0)
            return BadRequest(new { error = "Insufficient account balance" });

        // Validate against risk guards
        var riskResult = await _riskGuard.ValidateAsync(request.Symbol, request.Volume, request.Direction);
        if (!riskResult.IsValid)
            return BadRequest(new { error = riskResult.Reason });

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

    private static PositionDto ToDto(Position p)
    {
        // Notional = lots * contract size (100,000) * entry price
        var notionalUsd = p.Volume * 100_000m * p.EntryPrice;
        return new PositionDto(
            p.Id, p.CTraderPositionId, p.AccountId, p.Symbol,
            p.Direction.ToString(), p.Volume, notionalUsd,
            p.EntryPrice, p.StopLoss, p.TakeProfit,
            p.CurrentPrice, p.UnrealizedPnL, p.Swap, p.Commission,
            p.Status.ToString(), p.OpenTime, p.CloseTime, p.ClosePrice,
            p.RealizedPnL);
    }
}

public record PositionDto(
    long Id, long CTraderPositionId, long AccountId, string Symbol,
    string Direction, decimal Volume, decimal NotionalUsd,
    decimal EntryPrice, decimal? StopLoss, decimal? TakeProfit,
    decimal CurrentPrice, decimal UnrealizedPnL, decimal Swap, decimal Commission,
    string Status, DateTime OpenTime, DateTime? CloseTime, decimal? ClosePrice,
    decimal? RealizedPnL);
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
