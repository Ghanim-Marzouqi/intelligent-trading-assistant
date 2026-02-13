using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Alerts;

namespace TradingAssistant.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AlertsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AlertsController> _logger;

    public AlertsController(AppDbContext db, ILogger<AlertsController> logger)
    {
        _db = db;
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

        return await query.OrderByDescending(a => a.CreatedAt).ToListAsync();
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
        var alert = new AlertRule
        {
            Symbol = request.Symbol.ToUpperInvariant(),
            Name = request.Name,
            Description = request.Description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

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
        var query = _db.AlertTriggers.AsQueryable();

        if (since.HasValue)
            query = query.Where(t => t.TriggeredAt >= since.Value);

        return await query
            .OrderByDescending(t => t.TriggeredAt)
            .Take(limit)
            .ToListAsync();
    }
}

public record CreateAlertRequest(string Symbol, string Name, string? Description);
public record UpdateAlertRequest(string? Name, string? Description, bool? IsActive);
