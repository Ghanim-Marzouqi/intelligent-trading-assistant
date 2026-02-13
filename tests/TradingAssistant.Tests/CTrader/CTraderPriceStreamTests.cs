using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingAssistant.Api.Hubs;
using TradingAssistant.Api.Services.CTrader;

namespace TradingAssistant.Tests.CTrader;

public class CTraderPriceStreamTests
{
    private readonly Mock<ICTraderConnectionManager> _connectionManager = new();
    private readonly Mock<ICTraderSymbolResolver> _symbolResolver = new();
    private readonly Mock<IHubContext<TradingHub, ITradingHubClient>> _hubContext = new();
    private readonly CTraderPriceStream _stream;

    public CTraderPriceStreamTests()
    {
        // Set up a no-op hub context (group client that does nothing)
        var mockClients = new Mock<IHubClients<ITradingHubClient>>();
        var mockClient = new Mock<ITradingHubClient>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClient.Object);
        _hubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        _stream = new CTraderPriceStream(
            _connectionManager.Object,
            _symbolResolver.Object,
            _hubContext.Object,
            NullLogger<CTraderPriceStream>.Instance);
    }

    [Fact]
    public void GetCurrentPrice_NoData_ReturnsNull()
    {
        var result = _stream.GetCurrentPrice("EURUSD");

        Assert.Null(result);
    }

    [Fact]
    public void GetPriceHistory_NoData_ReturnsEmpty()
    {
        var result = _stream.GetPriceHistory("EURUSD");

        Assert.Empty(result);
    }

    [Fact]
    public void IsKnownSymbol_KnownSymbol_ReturnsTrue()
    {
        long symbolId = 1;
        _symbolResolver.Setup(r => r.TryGetSymbolId("EURUSD", out symbolId))
            .Returns(true);

        Assert.True(_stream.IsKnownSymbol("EURUSD"));
    }

    [Fact]
    public void IsKnownSymbol_UnknownSymbol_ReturnsFalse()
    {
        long symbolId = 0;
        _symbolResolver.Setup(r => r.TryGetSymbolId("INVALID", out symbolId))
            .Returns(false);

        Assert.False(_stream.IsKnownSymbol("INVALID"));
    }

    [Fact]
    public void IsKnownSymbol_CaseInsensitive()
    {
        long symbolId = 1;
        _symbolResolver.Setup(r => r.TryGetSymbolId("EURUSD", out symbolId))
            .Returns(true);

        Assert.True(_stream.IsKnownSymbol("eurusd"));
    }

    [Fact]
    public async Task HandlePriceUpdate_StoresBidAndAsk()
    {
        // Use reflection to invoke private HandlePriceUpdate since it's called internally
        var method = typeof(CTraderPriceStream).GetMethod("HandlePriceUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(_stream, ["EURUSD", 1.18600m, 1.18610m])!;

        var price = _stream.GetCurrentPrice("EURUSD");
        Assert.NotNull(price);
        Assert.Equal(1.18600m, price.Value.Bid);
        Assert.Equal(1.18610m, price.Value.Ask);
    }

    [Fact]
    public async Task HandlePriceUpdate_AppendsToPriceHistory()
    {
        var method = typeof(CTraderPriceStream).GetMethod("HandlePriceUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(_stream, ["EURUSD", 1.18600m, 1.18610m])!;
        await (Task)method!.Invoke(_stream, ["EURUSD", 1.18620m, 1.18630m])!;
        await (Task)method!.Invoke(_stream, ["EURUSD", 1.18650m, 1.18660m])!;

        var history = _stream.GetPriceHistory("EURUSD");
        Assert.Equal(3, history.Count);
        Assert.Equal(1.18600m, history[0]);
        Assert.Equal(1.18620m, history[1]);
        Assert.Equal(1.18650m, history[2]);
    }

    [Fact]
    public async Task HandlePriceUpdate_CapsHistoryAt100()
    {
        var method = typeof(CTraderPriceStream).GetMethod("HandlePriceUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        for (int i = 0; i < 110; i++)
        {
            await (Task)method!.Invoke(_stream, ["EURUSD", 1.0m + i * 0.0001m, 1.0001m + i * 0.0001m])!;
        }

        var history = _stream.GetPriceHistory("EURUSD");
        Assert.Equal(100, history.Count);
        // First 10 should have been evicted; first in history should be tick #10
        Assert.Equal(1.0m + 10 * 0.0001m, history[0]);
    }

    [Fact]
    public async Task HandlePriceUpdate_RaisesOnPriceUpdateEvent()
    {
        var method = typeof(CTraderPriceStream).GetMethod("HandlePriceUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        PriceUpdateEventArgs? received = null;
        _stream.OnPriceUpdate += (_, e) => received = e;

        await (Task)method!.Invoke(_stream, ["EURUSD", 1.18600m, 1.18610m])!;

        Assert.NotNull(received);
        Assert.Equal("EURUSD", received.Symbol);
        Assert.Equal(1.18600m, received.Bid);
        Assert.Equal(1.18610m, received.Ask);
    }

    [Fact]
    public async Task HandlePriceUpdate_MultipleSymbols_TrackedSeparately()
    {
        var method = typeof(CTraderPriceStream).GetMethod("HandlePriceUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(_stream, ["EURUSD", 1.18600m, 1.18610m])!;
        await (Task)method!.Invoke(_stream, ["USDJPY", 153.050m, 153.060m])!;

        var eurPrice = _stream.GetCurrentPrice("EURUSD");
        var jpyPrice = _stream.GetCurrentPrice("USDJPY");

        Assert.NotNull(eurPrice);
        Assert.NotNull(jpyPrice);
        Assert.Equal(1.18600m, eurPrice.Value.Bid);
        Assert.Equal(153.050m, jpyPrice.Value.Bid);

        var eurHistory = _stream.GetPriceHistory("EURUSD");
        var jpyHistory = _stream.GetPriceHistory("USDJPY");
        Assert.Single(eurHistory);
        Assert.Single(jpyHistory);
    }

    [Fact]
    public async Task GetPriceHistory_ReturnsSnapshot()
    {
        // GetPriceHistory should return a copy, not a live reference
        var method = typeof(CTraderPriceStream).GetMethod("HandlePriceUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(_stream, ["EURUSD", 1.18600m, 1.18610m])!;

        var history1 = _stream.GetPriceHistory("EURUSD");
        Assert.Single(history1);

        await (Task)method!.Invoke(_stream, ["EURUSD", 1.18700m, 1.18710m])!;

        // history1 should still have 1 element (it's a snapshot)
        Assert.Single(history1);
        var history2 = _stream.GetPriceHistory("EURUSD");
        Assert.Equal(2, history2.Count);
    }
}
