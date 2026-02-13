using System.Collections.Concurrent;

namespace TradingAssistant.Api.Services.Orders;

public interface IApprovalTokenStore
{
    void Store(string token, PreparedOrder order);
    bool TryGet(string token, out PreparedOrder? order);
    bool TryRemove(string token, out PreparedOrder? order);
    IReadOnlyList<PreparedOrder> GetPending();
}

public class ApprovalTokenStore : IApprovalTokenStore
{
    private readonly ConcurrentDictionary<string, PreparedOrder> _pending = new();

    public void Store(string token, PreparedOrder order)
    {
        _pending[token] = order;
    }

    public bool TryGet(string token, out PreparedOrder? order)
    {
        return _pending.TryGetValue(token, out order);
    }

    public bool TryRemove(string token, out PreparedOrder? order)
    {
        return _pending.TryRemove(token, out order);
    }

    public IReadOnlyList<PreparedOrder> GetPending()
    {
        // Remove expired orders and return active ones
        var now = DateTime.UtcNow;
        var expired = _pending.Where(kv => now > kv.Value.ExpiresAt).Select(kv => kv.Key).ToList();
        foreach (var key in expired)
            _pending.TryRemove(key, out _);

        return _pending.Values.OrderByDescending(o => o.PreparedAt).ToList();
    }
}
