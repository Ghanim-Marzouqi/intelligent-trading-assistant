using TradingAssistant.Api.Services.CTrader;
using TradingAssistant.Api.Services.Notifications;

namespace TradingAssistant.Api.Services.Orders;

public interface IOrderManager
{
    Task<PreparedOrder> PrepareOrderAsync(OrderRequest request);
    Task<OrderResult> ExecuteOrderAsync(PreparedOrder order);
    Task<bool> ApproveOrderAsync(string approvalToken);
    Task RejectOrderAsync(string approvalToken);
}

public class OrderManager : IOrderManager
{
    private readonly ICTraderOrderExecutor _executor;
    private readonly IPositionSizer _positionSizer;
    private readonly IRiskGuard _riskGuard;
    private readonly INotificationService _notifications;
    private readonly ILogger<OrderManager> _logger;
    private readonly Dictionary<string, PreparedOrder> _pendingApprovals = new();

    public OrderManager(
        ICTraderOrderExecutor executor,
        IPositionSizer positionSizer,
        IRiskGuard riskGuard,
        INotificationService notifications,
        ILogger<OrderManager> logger)
    {
        _executor = executor;
        _positionSizer = positionSizer;
        _riskGuard = riskGuard;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<PreparedOrder> PrepareOrderAsync(OrderRequest request)
    {
        _logger.LogInformation("Preparing order: {Symbol} {Direction}", request.Symbol, request.Direction);

        // Calculate position size based on risk parameters
        var volume = await _positionSizer.CalculateAsync(
            request.Symbol,
            request.RiskPercent,
            request.EntryPrice,
            request.StopLoss);

        // Validate against risk guards
        var riskCheck = await _riskGuard.ValidateAsync(request.Symbol, volume, request.Direction);
        if (!riskCheck.IsValid)
        {
            throw new InvalidOperationException($"Risk guard rejected: {riskCheck.Reason}");
        }

        var order = new PreparedOrder
        {
            Symbol = request.Symbol,
            Direction = request.Direction,
            Volume = volume,
            EntryPrice = request.EntryPrice,
            StopLoss = request.StopLoss,
            TakeProfit = request.TakeProfit,
            RiskPercent = request.RiskPercent,
            ApprovalToken = Guid.NewGuid().ToString("N"),
            PreparedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };

        _pendingApprovals[order.ApprovalToken] = order;

        // Send approval request to Telegram
        await _notifications.SendOrderApprovalRequestAsync(order);

        return order;
    }

    public async Task<OrderResult> ExecuteOrderAsync(PreparedOrder order)
    {
        _logger.LogInformation("Executing order: {Symbol} {Direction} {Volume}",
            order.Symbol, order.Direction, order.Volume);

        return await _executor.PlaceMarketOrderAsync(
            order.Symbol,
            order.Direction,
            order.Volume,
            order.StopLoss,
            order.TakeProfit);
    }

    public async Task<bool> ApproveOrderAsync(string approvalToken)
    {
        if (!_pendingApprovals.TryGetValue(approvalToken, out var order))
        {
            _logger.LogWarning("Approval token not found: {Token}", approvalToken);
            return false;
        }

        if (DateTime.UtcNow > order.ExpiresAt)
        {
            _logger.LogWarning("Order approval expired: {Token}", approvalToken);
            _pendingApprovals.Remove(approvalToken);
            return false;
        }

        _pendingApprovals.Remove(approvalToken);

        var result = await ExecuteOrderAsync(order);

        if (result.Success)
        {
            await _notifications.SendOrderExecutedAsync(order, result);
            _logger.LogInformation("Order executed successfully: {OrderId}", result.OrderId);
        }
        else
        {
            await _notifications.SendOrderFailedAsync(order, result);
            _logger.LogError("Order execution failed: {Error}", result.ErrorMessage);
        }

        return result.Success;
    }

    public Task RejectOrderAsync(string approvalToken)
    {
        if (_pendingApprovals.Remove(approvalToken))
        {
            _logger.LogInformation("Order rejected: {Token}", approvalToken);
        }

        return Task.CompletedTask;
    }
}

public class OrderRequest
{
    public string Symbol { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal RiskPercent { get; set; } = 1m;
}

public class PreparedOrder
{
    public string Symbol { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal Volume { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal RiskPercent { get; set; }
    public string ApprovalToken { get; set; } = string.Empty;
    public DateTime PreparedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
