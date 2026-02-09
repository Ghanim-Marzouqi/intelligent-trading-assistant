using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Alerts;

namespace TradingAssistant.Api.Services.Alerts;

public interface IAlertRuleRepository
{
    Task<IEnumerable<AlertRule>> GetActiveRulesAsync(string? symbol = null);
    Task<AlertRule?> GetByIdAsync(long id);
    Task<AlertRule> CreateAsync(AlertRule rule);
    Task UpdateAsync(AlertRule rule);
    Task DeleteAsync(long id);
}

public class AlertRuleRepository : IAlertRuleRepository
{
    private readonly AppDbContext _db;

    public AlertRuleRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<AlertRule>> GetActiveRulesAsync(string? symbol = null)
    {
        var query = _db.AlertRules
            .Where(r => r.IsActive)
            .Include(r => r.Conditions);

        if (!string.IsNullOrEmpty(symbol))
            query = query.Where(r => r.Symbol == symbol.ToUpperInvariant());

        return await query.ToListAsync();
    }

    public async Task<AlertRule?> GetByIdAsync(long id)
    {
        return await _db.AlertRules
            .Include(r => r.Conditions)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<AlertRule> CreateAsync(AlertRule rule)
    {
        rule.CreatedAt = DateTime.UtcNow;
        _db.AlertRules.Add(rule);
        await _db.SaveChangesAsync();
        return rule;
    }

    public async Task UpdateAsync(AlertRule rule)
    {
        rule.UpdatedAt = DateTime.UtcNow;
        _db.AlertRules.Update(rule);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var rule = await _db.AlertRules.FindAsync(id);
        if (rule is not null)
        {
            _db.AlertRules.Remove(rule);
            await _db.SaveChangesAsync();
        }
    }
}
