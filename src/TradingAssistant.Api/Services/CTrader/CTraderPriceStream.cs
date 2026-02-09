using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using TradingAssistant.Api.Hubs;

namespace TradingAssistant.Api.Services.CTrader;

public interface ICTraderPriceStream
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    Task SubscribeAsync(string symbol);
    Task UnsubscribeAsync(string symbol);
    event EventHandler<PriceUpdateEventArgs>? OnPriceUpdate;
}

public class CTraderPriceStream : ICTraderPriceStream
{
    private readonly IHubContext<TradingHub, ITradingHubClient> _hubContext;
    private readonly ILogger<CTraderPriceStream> _logger;
    private readonly ConcurrentDictionary<string, decimal> _lastPrices = new();
    private readonly HashSet<string> _subscribedSymbols = [];

    public event EventHandler<PriceUpdateEventArgs>? OnPriceUpdate;

    public CTraderPriceStream(
        IHubContext<TradingHub, ITradingHubClient> hubContext,
        ILogger<CTraderPriceStream> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Price stream starting...");

        // TODO: Connect to cTrader price stream via gRPC
        // Subscribe to ProtoOASubscribeSpotsReq for each symbol

        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Price stream stopping...");
        _subscribedSymbols.Clear();
        await Task.CompletedTask;
    }

    public async Task SubscribeAsync(string symbol)
    {
        symbol = symbol.ToUpperInvariant();

        if (_subscribedSymbols.Add(symbol))
        {
            _logger.LogDebug("Subscribed to {Symbol}", symbol);

            // TODO: Send ProtoOASubscribeSpotsReq to cTrader
        }

        await Task.CompletedTask;
    }

    public async Task UnsubscribeAsync(string symbol)
    {
        symbol = symbol.ToUpperInvariant();

        if (_subscribedSymbols.Remove(symbol))
        {
            _logger.LogDebug("Unsubscribed from {Symbol}", symbol);

            // TODO: Send ProtoOAUnsubscribeSpotsReq to cTrader
        }

        await Task.CompletedTask;
    }

    private async Task HandlePriceUpdate(string symbol, decimal bid, decimal ask)
    {
        _lastPrices[symbol] = bid;

        var update = new PriceUpdate(symbol, bid, ask, DateTime.UtcNow);

        // Notify SignalR clients
        await _hubContext.Clients.Group($"symbol:{symbol}").ReceivePriceUpdate(update);

        // Raise event for internal consumers (Alert Engine, etc.)
        OnPriceUpdate?.Invoke(this, new PriceUpdateEventArgs(symbol, bid, ask));
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
