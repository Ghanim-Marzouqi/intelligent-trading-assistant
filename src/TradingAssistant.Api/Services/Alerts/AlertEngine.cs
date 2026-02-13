using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Hubs;
using TradingAssistant.Api.Models.Alerts;
using TradingAssistant.Api.Services.Alerts.Conditions;
using TradingAssistant.Api.Services.CTrader;
using TradingAssistant.Api.Services.AI;
using TradingAssistant.Api.Services.Notifications;
using TradingAssistant.Api.Services.Orders;

namespace TradingAssistant.Api.Services.Alerts;

public class AlertEngine : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICTraderPriceStream _priceStream;
    private readonly INotificationService _notificationService;
    private readonly ILogger<AlertEngine> _logger;
    private readonly ConcurrentDictionary<string, List<AlertRule>> _rulesBySymbol = new();
    private readonly ConcurrentDictionary<string, decimal> _previousPrices = new();
    private readonly ConcurrentDictionary<string, List<decimal>> _priceHistory = new();

    private const int MaxPriceHistory = 100;

    private readonly PriceCondition _priceCondition = new();
    private readonly IndicatorCondition _indicatorCondition = new();

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
        // Track price history
        var history = _priceHistory.GetOrAdd(symbol, _ => new List<decimal>());
        lock (history)
        {
            history.Add(price);
            if (history.Count > MaxPriceHistory)
                history.RemoveAt(0);
        }

        // Get previous price for crossover detection
        var hasPrevious = _previousPrices.TryGetValue(symbol, out var previousPrice);

        if (!_rulesBySymbol.TryGetValue(symbol, out var rules))
        {
            _previousPrices[symbol] = price;
            return;
        }

        foreach (var rule in rules.ToList())
        {
            if (await EvaluateRuleAsync(rule, price, hasPrevious ? previousPrice : null, history))
            {
                await TriggerAlertAsync(rule, price);
            }
        }

        _previousPrices[symbol] = price;
    }

    private async Task<bool> EvaluateRuleAsync(AlertRule rule, decimal price, decimal? previousPrice,
        List<decimal> priceHistory)
    {
        foreach (var condition in rule.Conditions)
        {
            bool result;
            IReadOnlyList<decimal> historySnapshot;
            lock (priceHistory)
            {
                historySnapshot = priceHistory.ToList();
            }

            result = EvaluateCondition(condition, price, previousPrice, historySnapshot);

            if (condition.CombineWith == LogicalOperator.Or && result)
                return true;

            if (condition.CombineWith == LogicalOperator.And && !result)
                return false;

            if (condition.CombineWith is null)
                return result;
        }

        return false;
    }

    private bool EvaluateCondition(AlertCondition condition, decimal price, decimal? previousPrice,
        IReadOnlyList<decimal> priceHistory)
    {
        return condition.Type switch
        {
            ConditionType.PriceLevel or ConditionType.PriceChange
                => _priceCondition.Evaluate(condition, price, previousPrice),

            ConditionType.IndicatorValue or ConditionType.IndicatorCrossover
                => _indicatorCondition.EvaluateWithHistory(condition, priceHistory, price, previousPrice),

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

            // Fire-and-forget AI enrichment (only when enabled on the rule)
            if (rule.AiEnrichEnabled)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var enrichScope = _serviceProvider.CreateScope();
                        var aiService = enrichScope.ServiceProvider.GetRequiredService<IAiAnalysisService>();
                        var enrichDb = enrichScope.ServiceProvider.GetRequiredService<AppDbContext>();

                        var enrichment = await aiService.EnrichAlertAsync(rule.Symbol, triggerPrice, trigger.Message);

                        var savedTrigger = await enrichDb.AlertTriggers.FindAsync(trigger.Id);
                        if (savedTrigger is not null)
                        {
                            savedTrigger.AiEnrichment = enrichment;
                            await enrichDb.SaveChangesAsync();
                        }

                        // Push enriched follow-up to SignalR clients
                        var hubContext = enrichScope.ServiceProvider
                            .GetRequiredService<IHubContext<TradingHub, ITradingHubClient>>();
                        await hubContext.Clients.All.ReceiveAlert(new AlertNotification(
                            trigger.Id, trigger.Symbol, trigger.Message,
                            "info", trigger.TriggeredAt, enrichment));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "AI enrichment failed for alert {AlertName}", rule.Name);
                    }
                });
            }

            // Auto-prepare order when enabled on the rule
            if (rule.AutoPrepareOrder)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var orderScope = _serviceProvider.CreateScope();
                        var aiService = orderScope.ServiceProvider.GetRequiredService<IAiAnalysisService>();
                        var orderManager = orderScope.ServiceProvider.GetRequiredService<IOrderManager>();

                        var analysis = await aiService.AnalyzeMarketAsync(rule.Symbol);

                        if (analysis.Trade is not null && analysis.Recommendation is "buy" or "sell")
                        {
                            var request = new OrderRequest
                            {
                                Symbol = rule.Symbol,
                                Direction = analysis.Trade.Direction.Equals("buy", StringComparison.OrdinalIgnoreCase)
                                    ? "Buy" : "Sell",
                                EntryPrice = analysis.Trade.Entry,
                                StopLoss = analysis.Trade.StopLoss,
                                TakeProfit = analysis.Trade.TakeProfit,
                                RiskPercent = analysis.Trade.RiskPercent
                            };

                            await orderManager.PrepareOrderAsync(request);

                            _logger.LogInformation(
                                "Auto-prepared order from alert {AlertName}: {Symbol} {Direction}",
                                rule.Name, rule.Symbol, request.Direction);
                        }
                        else
                        {
                            _logger.LogInformation(
                                "Alert {AlertName} fired with AutoPrepareOrder but AI did not suggest a trade for {Symbol}",
                                rule.Name, rule.Symbol);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Auto-prepare order failed for alert {AlertName}", rule.Name);
                    }
                });
            }
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
