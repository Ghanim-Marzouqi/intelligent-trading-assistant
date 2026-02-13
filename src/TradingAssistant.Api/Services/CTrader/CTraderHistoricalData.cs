using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using OpenAPI.Net;
using OpenAPI.Net.Helpers;

namespace TradingAssistant.Api.Services.CTrader;

public interface ICTraderHistoricalData
{
    Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string symbol,
        ProtoOATrendbarPeriod period = ProtoOATrendbarPeriod.H1,
        int count = 50,
        CancellationToken ct = default);
}

public record Candle(
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    DateTime Timestamp);

public class CTraderHistoricalData : ICTraderHistoricalData
{
    private readonly ICTraderConnectionManager _connectionManager;
    private readonly ICTraderSymbolResolver _symbolResolver;
    private readonly ILogger<CTraderHistoricalData> _logger;

    // Trendbar prices are always encoded with 5-digit precision
    // (same as spot events, confirmed from cTrader Open API docs)
    private const int TrendbarPriceDigits = 5;

    public CTraderHistoricalData(
        ICTraderConnectionManager connectionManager,
        ICTraderSymbolResolver symbolResolver,
        ILogger<CTraderHistoricalData> logger)
    {
        _connectionManager = connectionManager;
        _symbolResolver = symbolResolver;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string symbol,
        ProtoOATrendbarPeriod period = ProtoOATrendbarPeriod.H1,
        int count = 50,
        CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();

        if (!_symbolResolver.TryGetSymbolId(symbol, out var symbolId))
        {
            _logger.LogWarning("Cannot fetch candles for {Symbol}: unknown symbol", symbol);
            return [];
        }

        var client = await _connectionManager.GetClientAsync(ct);
        var accountId = _connectionManager.AccountId;

        // Compute the time range based on the period and count requested.
        // We use the SDK helper to get the maximum allowed time span for this period.
        var maxTimeSpan = TrendbarsMaximumTime.GetMaximumTime(period);
        var toTimestamp = DateTimeOffset.UtcNow;
        var fromTimestamp = toTimestamp - maxTimeSpan;

        var req = new ProtoOAGetTrendbarsReq
        {
            CtidTraderAccountId = accountId,
            SymbolId = symbolId,
            Period = period,
            FromTimestamp = fromTimestamp.ToUnixTimeMilliseconds(),
            ToTimestamp = toTimestamp.ToUnixTimeMilliseconds(),
            Count = (uint)count
        };

        _logger.LogDebug(
            "Requesting {Count} {Period} candles for {Symbol} (ID={SymbolId})",
            count, period, symbol, symbolId);

        await client.SendMessage(req, ProtoOAPayloadType.ProtoOaGetTrendbarsReq);

        var response = await client.OfType<ProtoOAGetTrendbarsRes>()
            .Where(r => r.SymbolId == symbolId)
            .FirstAsync()
            .ToTask(ct);

        var candles = new List<Candle>(response.Trendbar.Count);

        foreach (var bar in response.Trendbar)
        {
            var low = (decimal)bar.Low / 100_000m;
            var open = low + (decimal)bar.DeltaOpen / 100_000m;
            var high = low + (decimal)bar.DeltaHigh / 100_000m;
            var close = low + (decimal)bar.DeltaClose / 100_000m;
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(bar.UtcTimestampInMinutes * 60L).UtcDateTime;

            candles.Add(new Candle(open, high, low, close, bar.Volume, timestamp));
        }

        _logger.LogInformation(
            "Received {Count} {Period} candles for {Symbol}",
            candles.Count, period, symbol);

        return candles;
    }
}

/// <summary>
/// Maps user-friendly timeframe strings (H1, H4, D1, etc.) to ProtoOATrendbarPeriod values.
/// </summary>
public static class TrendbarPeriodMapper
{
    public static ProtoOATrendbarPeriod Parse(string timeframe)
    {
        return timeframe.ToUpperInvariant() switch
        {
            "M1" => ProtoOATrendbarPeriod.M1,
            "M5" => ProtoOATrendbarPeriod.M5,
            "M15" => ProtoOATrendbarPeriod.M15,
            "M30" => ProtoOATrendbarPeriod.M30,
            "H1" => ProtoOATrendbarPeriod.H1,
            "H4" => ProtoOATrendbarPeriod.H4,
            "H12" => ProtoOATrendbarPeriod.H12,
            "D1" or "DAILY" => ProtoOATrendbarPeriod.D1,
            "W1" or "WEEKLY" => ProtoOATrendbarPeriod.W1,
            "MN1" or "MONTHLY" => ProtoOATrendbarPeriod.Mn1,
            _ => ProtoOATrendbarPeriod.H1
        };
    }
}
