using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Alerts;
using TradingAssistant.Api.Services.CTrader;

namespace TradingAssistant.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AlertsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICTraderPriceStream _priceStream;
    private readonly ILogger<AlertsController> _logger;

    public AlertsController(AppDbContext db, ICTraderPriceStream priceStream, ILogger<AlertsController> logger)
    {
        _db = db;
        _priceStream = priceStream;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AlertRule>>> GetAlerts(
        [FromQuery] bool? activeOnly = null,
        [FromQuery] string? symbol = null)
    {
        var query = _db.AlertRules.AsQueryable();

        if (activeOnly == true)
            query = query.Where(a => a.IsActive);

        if (!string.IsNullOrEmpty(symbol))
            query = query.Where(a => a.Symbol == symbol.ToUpperInvariant());

        return await query
            .Include(a => a.Conditions)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AlertRule>> GetAlert(long id)
    {
        var alert = await _db.AlertRules
            .Include(a => a.Conditions)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (alert is null)
            return NotFound();

        return alert;
    }

    [HttpPost]
    public async Task<ActionResult<AlertRule>> CreateAlert(CreateAlertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
            return BadRequest(new { error = "Symbol is required." });

        if (!_priceStream.IsKnownSymbol(request.Symbol))
            return BadRequest(new { error = $"Unknown trading symbol: {request.Symbol}" });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });

        if (request.Name.Length > 100)
            return BadRequest(new { error = "Name must be 100 characters or fewer." });

        if (request.MaxTriggers.HasValue && request.MaxTriggers.Value <= 0)
            return BadRequest(new { error = "MaxTriggers must be greater than 0." });

        if (request.Conditions is null or { Count: 0 })
            return BadRequest(new { error = "At least one condition must be provided." });

        var alert = new AlertRule
        {
            Symbol = request.Symbol.ToUpperInvariant(),
            Name = request.Name,
            Description = request.Description,
            Type = request.Type ?? AlertType.Price,
            AutoPrepareOrder = request.AutoPrepareOrder ?? false,
            AiEnrichEnabled = request.AiEnrichEnabled ?? true,
            NotifyTelegram = request.NotifyTelegram ?? true,
            NotifyDashboard = request.NotifyDashboard ?? true,
            MaxTriggers = request.MaxTriggers,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        if (request.Conditions is { Count: > 0 })
        {
            foreach (var c in request.Conditions)
            {
                alert.Conditions.Add(new AlertCondition
                {
                    Type = c.Type,
                    Operator = c.Operator,
                    Value = c.Value,
                    CombineWith = c.CombineWith,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        _db.AlertRules.Add(alert);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created alert {AlertId} for {Symbol}", alert.Id, alert.Symbol);

        return CreatedAtAction(nameof(GetAlert), new { id = alert.Id }, alert);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAlert(long id, UpdateAlertRequest request)
    {
        var alert = await _db.AlertRules.FindAsync(id);
        if (alert is null)
            return NotFound();

        alert.Name = request.Name ?? alert.Name;
        alert.Description = request.Description ?? alert.Description;
        alert.IsActive = request.IsActive ?? alert.IsActive;
        alert.AutoPrepareOrder = request.AutoPrepareOrder ?? alert.AutoPrepareOrder;
        alert.AiEnrichEnabled = request.AiEnrichEnabled ?? alert.AiEnrichEnabled;
        alert.NotifyTelegram = request.NotifyTelegram ?? alert.NotifyTelegram;
        alert.NotifyDashboard = request.NotifyDashboard ?? alert.NotifyDashboard;
        alert.MaxTriggers = request.MaxTriggers ?? alert.MaxTriggers;
        alert.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAlert(long id)
    {
        var alert = await _db.AlertRules.FindAsync(id);
        if (alert is null)
            return NotFound();

        _db.AlertRules.Remove(alert);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted alert {AlertId}", id);

        return NoContent();
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<AlertTrigger>>> GetAlertHistory(
        [FromQuery] int limit = 50,
        [FromQuery] DateTime? since = null)
    {
        limit = Math.Clamp(limit, 1, 500);

        var query = _db.AlertTriggers.AsQueryable();

        if (since.HasValue)
            query = query.Where(t => t.TriggeredAt >= since.Value);

        return await query
            .OrderByDescending(t => t.TriggeredAt)
            .Take(limit)
            .ToListAsync();
    }
}

public record CreateAlertRequest(
    string Symbol,
    string Name,
    string? Description,
    AlertType? Type,
    bool? AutoPrepareOrder,
    bool? AiEnrichEnabled,
    bool? NotifyTelegram,
    bool? NotifyDashboard,
    int? MaxTriggers,
    List<CreateAlertConditionDto>? Conditions);

public record CreateAlertConditionDto(
    ConditionType Type,
    ComparisonOperator Operator,
    decimal Value,
    LogicalOperator? CombineWith);

public record UpdateAlertRequest(
    string? Name,
    string? Description,
    bool? IsActive,
    bool? AutoPrepareOrder,
    bool? AiEnrichEnabled,
    bool? NotifyTelegram,
    bool? NotifyDashboard,
    int? MaxTriggers);
