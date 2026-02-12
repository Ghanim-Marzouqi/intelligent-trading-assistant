using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Alerts;
using TradingAssistant.Api.Services.CTrader;
using TradingAssistant.Api.Services.Notifications;

namespace TradingAssistant.Api.Services.Alerts;

public class AlertEngine : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICTraderPriceStream _priceStream;
    private readonly INotificationService _notificationService;
    private readonly ILogger<AlertEngine> _logger;
    private readonly ConcurrentDictionary<string, List<AlertRule>> _rulesBySymbol = new();

    public AlertEngine(
        IServiceProvider serviceProvider,
        ICTraderPriceStream priceStream,
        INotificationService notificationService,
        ILogger<AlertEngine> logger)
    {
        _serviceProvider = serviceProvider;
        _priceStream = priceStream;
        _notificationService = notificationService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Alert Engine starting...");

        // Retry initial rule load with exponential backoff
        var attemptCount = 0;
        const int maxDelaySeconds = 60;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await LoadActiveRulesAsync();
                break;
            }
            catch (Exception ex)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attemptCount), maxDelaySeconds));
                attemptCount++;
                _logger.LogError(ex, "Failed to load alert rules, retrying in {Delay}...", delay);
                await Task.Delay(delay, stoppingToken);
            }
        }

        _priceStream.OnPriceUpdate += async (sender, args) =>
        {
            try
            {
                await OnPriceUpdateAsync(args.Symbol, args.Bid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing price update for {Symbol}", args.Symbol);
            }
        };

        // Periodically reload rules to pick up changes
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            try
            {
                await LoadActiveRulesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload alert rules, will retry next cycle");
            }
        }
    }

    private async Task LoadActiveRulesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rules = await db.AlertRules
            .Where(r => r.IsActive)
            .Include(r => r.Conditions)
            .ToListAsync();

        _rulesBySymbol.Clear();

        foreach (var rule in rules)
        {
            if (!_rulesBySymbol.ContainsKey(rule.Symbol))
                _rulesBySymbol[rule.Symbol] = [];

            _rulesBySymbol[rule.Symbol].Add(rule);

            await _priceStream.SubscribeAsync(rule.Symbol);
        }

        _logger.LogDebug("Loaded {Count} active alert rules", rules.Count);
    }

    private async Task OnPriceUpdateAsync(string symbol, decimal price)
    {
        if (!_rulesBySymbol.TryGetValue(symbol, out var rules))
            return;

        foreach (var rule in rules.ToList())
        {
            if (await EvaluateRuleAsync(rule, price))
            {
                await TriggerAlertAsync(rule, price);
            }
        }
    }

    private async Task<bool> EvaluateRuleAsync(AlertRule rule, decimal price)
    {
        foreach (var condition in rule.Conditions)
        {
            var result = EvaluateCondition(condition, price);

            if (condition.CombineWith == LogicalOperator.Or && result)
                return true;

            if (condition.CombineWith == LogicalOperator.And && !result)
                return false;

            if (condition.CombineWith is null)
                return result;
        }

        return false;
    }

    private bool EvaluateCondition(AlertCondition condition, decimal price)
    {
        return condition.Operator switch
        {
            ComparisonOperator.GreaterThan => price > condition.Value,
            ComparisonOperator.LessThan => price < condition.Value,
            ComparisonOperator.GreaterOrEqual => price >= condition.Value,
            ComparisonOperator.LessOrEqual => price <= condition.Value,
            // TODO: Implement crossover detection (requires previous price)
            _ => false
        };
    }

    private async Task TriggerAlertAsync(AlertRule rule, decimal triggerPrice)
    {
        try
        {
            _logger.LogInformation("Alert triggered: {AlertName} for {Symbol} at {Price}",
                rule.Name, rule.Symbol, triggerPrice);

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var trigger = new AlertTrigger
            {
                AlertRuleId = rule.Id,
                Symbol = rule.Symbol,
                TriggerPrice = triggerPrice,
                Message = $"{rule.Name}: {rule.Symbol} reached {triggerPrice}",
                TriggeredAt = DateTime.UtcNow
            };

            db.AlertTriggers.Add(trigger);

            rule.TriggerCount++;
            rule.LastTriggeredAt = DateTime.UtcNow;

            // Deactivate if max triggers reached
            if (rule.MaxTriggers.HasValue && rule.TriggerCount >= rule.MaxTriggers.Value)
            {
                rule.IsActive = false;
                _logger.LogInformation("Alert {AlertName} deactivated after {Count} triggers",
                    rule.Name, rule.TriggerCount);
            }

            await db.SaveChangesAsync();

            // Send notifications
            await _notificationService.SendAlertAsync(trigger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger alert {AlertName} for {Symbol} at {Price}",
                rule.Name, rule.Symbol, triggerPrice);
        }
    }

    public void AddRule(AlertRule rule)
    {
        if (!_rulesBySymbol.ContainsKey(rule.Symbol))
            _rulesBySymbol[rule.Symbol] = [];

        _rulesBySymbol[rule.Symbol].Add(rule);
    }

    public void RemoveRule(long ruleId)
    {
        foreach (var symbol in _rulesBySymbol.Keys)
        {
            _rulesBySymbol[symbol].RemoveAll(r => r.Id == ruleId);
        }
    }
}
