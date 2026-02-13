using System.Collections.Concurrent;
using System.Reactive.Linq;
using Microsoft.AspNetCore.SignalR;
using OpenAPI.Net;
using TradingAssistant.Api.Hubs;

namespace TradingAssistant.Api.Services.CTrader;

public interface ICTraderPriceStream
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    Task SubscribeAsync(string symbol);
    Task UnsubscribeAsync(string symbol);
    event EventHandler<PriceUpdateEventArgs>? OnPriceUpdate;
    (decimal Bid, decimal Ask)? GetCurrentPrice(string symbol);
    IReadOnlyList<decimal> GetPriceHistory(string symbol);
}

public class CTraderPriceStream : ICTraderPriceStream
{
    private readonly ICTraderConnectionManager _connectionManager;
    private readonly ICTraderSymbolResolver _symbolResolver;
    private readonly IHubContext<TradingHub, ITradingHubClient> _hubContext;
    private readonly ILogger<CTraderPriceStream> _logger;
    private readonly ConcurrentDictionary<string, decimal> _lastPrices = new();
    private readonly ConcurrentDictionary<string, decimal> _lastAsks = new();
    private readonly ConcurrentDictionary<string, List<decimal>> _priceHistory = new();
    private readonly HashSet<string> _subscribedSymbols = [];
    private const int MaxPriceHistory = 100;

    private IDisposable? _spotSubscription;

    public event EventHandler<PriceUpdateEventArgs>? OnPriceUpdate;

    public CTraderPriceStream(
        ICTraderConnectionManager connectionManager,
        ICTraderSymbolResolver symbolResolver,
        IHubContext<TradingHub, ITradingHubClient> hubContext,
        ILogger<CTraderPriceStream> logger)
    {
        _connectionManager = connectionManager;
        _symbolResolver = symbolResolver;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Price stream starting...");

        var client = await _connectionManager.GetClientAsync(cancellationToken);

        _spotSubscription = client.OfType<ProtoOASpotEvent>().Subscribe(
            async spotEvent =>
            {
                try
                {
                    await HandleSpotEventAsync(spotEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling spot event for symbol {SymbolId}", spotEvent.SymbolId);
                }
            },
            error => _logger.LogError(error, "Price stream error"));

        _logger.LogInformation("Price stream started — listening for spot events");
    }

    public Task StopAsync()
    {
        _logger.LogInformation("Price stream stopping...");
        _spotSubscription?.Dispose();
        _spotSubscription = null;
        _subscribedSymbols.Clear();
        return Task.CompletedTask;
    }

    public async Task SubscribeAsync(string symbol)
    {
        symbol = symbol.ToUpperInvariant();

        if (!_subscribedSymbols.Add(symbol))
            return;

        if (!_symbolResolver.TryGetSymbolId(symbol, out var symbolId))
        {
            _logger.LogWarning("Cannot subscribe to {Symbol}: unknown symbol", symbol);
            _subscribedSymbols.Remove(symbol);
            return;
        }

        var client = await _connectionManager.GetClientAsync();
        var accountId = _connectionManager.AccountId;

        var req = new ProtoOASubscribeSpotsReq
        {
            CtidTraderAccountId = accountId
        };
        req.SymbolId.Add(symbolId);

        await client.SendMessage(req, ProtoOAPayloadType.ProtoOaSubscribeSpotsReq);

        _logger.LogDebug("Subscribed to spot prices for {Symbol} (ID={SymbolId})", symbol, symbolId);
    }

    public async Task UnsubscribeAsync(string symbol)
    {
        symbol = symbol.ToUpperInvariant();

        if (!_subscribedSymbols.Remove(symbol))
            return;

        if (!_symbolResolver.TryGetSymbolId(symbol, out var symbolId))
            return;

        var client = await _connectionManager.GetClientAsync();
        var accountId = _connectionManager.AccountId;

        var req = new ProtoOAUnsubscribeSpotsReq
        {
            CtidTraderAccountId = accountId
        };
        req.SymbolId.Add(symbolId);

        await client.SendMessage(req, ProtoOAPayloadType.ProtoOaUnsubscribeSpotsReq);

        _logger.LogDebug("Unsubscribed from spot prices for {Symbol}", symbol);
    }

    private async Task HandleSpotEventAsync(ProtoOASpotEvent spotEvent)
    {
        var symbolName = _symbolResolver.GetSymbolName(spotEvent.SymbolId);
        var digits = _symbolResolver.GetDigits(spotEvent.SymbolId);

        var bid = spotEvent.HasBid
            ? CTraderConversions.PriceToDecimal(spotEvent.Bid, digits)
            : _lastPrices.GetValueOrDefault(symbolName);
        var ask = spotEvent.HasAsk
            ? CTraderConversions.PriceToDecimal(spotEvent.Ask, digits)
            : bid;

        if (bid == 0) return;

        await HandlePriceUpdate(symbolName, bid, ask);
    }

    private async Task HandlePriceUpdate(string symbol, decimal bid, decimal ask)
    {
        _lastPrices[symbol] = bid;
        _lastAsks[symbol] = ask;

        var history = _priceHistory.GetOrAdd(symbol, _ => new List<decimal>());
        lock (history)
        {
            history.Add(bid);
            if (history.Count > MaxPriceHistory)
                history.RemoveAt(0);
        }

        var update = new PriceUpdate(symbol, bid, ask, DateTime.UtcNow);

        // Notify SignalR clients
        await _hubContext.Clients.Group($"symbol:{symbol}").ReceivePriceUpdate(update);

        // Raise event for internal consumers (Alert Engine, etc.)
        OnPriceUpdate?.Invoke(this, new PriceUpdateEventArgs(symbol, bid, ask));
    }

    public (decimal Bid, decimal Ask)? GetCurrentPrice(string symbol)
    {
        symbol = symbol.ToUpperInvariant();
        if (_lastPrices.TryGetValue(symbol, out var bid) && _lastAsks.TryGetValue(symbol, out var ask))
            return (bid, ask);
        return null;
    }

    public IReadOnlyList<decimal> GetPriceHistory(string symbol)
    {
        symbol = symbol.ToUpperInvariant();
        if (_priceHistory.TryGetValue(symbol, out var history))
        {
            lock (history)
            {
                return history.ToList();
            }
        }
        return [];
    }
}

public class PriceUpdateEventArgs : EventArgs
{
    public string Symbol { get; }
    public decimal Bid { get; }
    public decimal Ask { get; }
    public DateTime Timestamp { get; }

    public PriceUpdateEventArgs(string symbol, decimal bid, decimal ask)
    {
        Symbol = symbol;
        Bid = bid;
        Ask = ask;
        Timestamp = DateTime.UtcNow;
    }
}
