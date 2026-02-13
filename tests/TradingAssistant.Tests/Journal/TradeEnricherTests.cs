using Microsoft.Extensions.Logging.Abstractions;
using TradingAssistant.Api.Models.Journal;
using TradingAssistant.Api.Services.Journal;

namespace TradingAssistant.Tests.Journal;

public class TradeEnricherTests
{
    private readonly TradeEnricher _enricher = new(NullLogger<TradeEnricher>.Instance);

    [Fact]
    public async Task Enrich_BuyTrade_CalculatesPipsCorrectly()
    {
        var entry = new TradeEntry
        {
            Symbol = "EURUSD",
            Direction = "Buy",
            EntryPrice = 1.1000m,
            ExitPrice = 1.1050m,
            PnL = 50m,
            Commission = 3m,
            Swap = 1m,
            OpenTime = DateTime.UtcNow.AddHours(-2),
            CloseTime = DateTime.UtcNow
        };

        await _enricher.EnrichAsync(entry);

        Assert.Equal(50.0m, entry.PnLPips); // (1.1050 - 1.1000) / 0.0001 = 50
    }

    [Fact]
    public async Task Enrich_SellTrade_CalculatesPipsCorrectly()
    {
        var entry = new TradeEntry
        {
            Symbol = "EURUSD",
            Direction = "Sell",
            EntryPrice = 1.1050m,
            ExitPrice = 1.1000m,
            PnL = 50m,
            Commission = 3m,
            Swap = 1m,
            OpenTime = DateTime.UtcNow.AddHours(-2),
            CloseTime = DateTime.UtcNow
        };

        await _enricher.EnrichAsync(entry);

        Assert.Equal(50.0m, entry.PnLPips); // (1.1050 - 1.1000) / 0.0001 = 50
    }

    [Fact]
    public async Task Enrich_JPYPair_UsesTwoPipDecimalPipSize()
    {
        var entry = new TradeEntry
        {
            Symbol = "USDJPY",
            Direction = "Buy",
            EntryPrice = 150.00m,
            ExitPrice = 150.50m,
            PnL = 50m,
            Commission = 3m,
            Swap = 0m,
            OpenTime = DateTime.UtcNow.AddHours(-1),
            CloseTime = DateTime.UtcNow
        };

        await _enricher.EnrichAsync(entry);

        Assert.Equal(50.0m, entry.PnLPips); // (150.50 - 150.00) / 0.01 = 50
    }

    [Fact]
    public async Task Enrich_Gold_UsesPointOnePipSize()
    {
        var entry = new TradeEntry
        {
            Symbol = "XAUUSD",
            Direction = "Buy",
            EntryPrice = 2000.0m,
            ExitPrice = 2005.0m,
            PnL = 500m,
            Commission = 5m,
            Swap = 0m,
            OpenTime = DateTime.UtcNow.AddHours(-3),
            CloseTime = DateTime.UtcNow
        };

        await _enricher.EnrichAsync(entry);

        Assert.Equal(50.0m, entry.PnLPips); // (2005 - 2000) / 0.1 = 50
    }

    [Fact]
    public async Task Enrich_CalculatesNetPnL()
    {
        var entry = new TradeEntry
        {
            Symbol = "EURUSD",
            Direction = "Buy",
            EntryPrice = 1.1000m,
            ExitPrice = 1.1050m,
            PnL = 50m,
            Commission = 7m,
            Swap = 3m,
            OpenTime = DateTime.UtcNow.AddHours(-2),
            CloseTime = DateTime.UtcNow
        };

        await _enricher.EnrichAsync(entry);

        Assert.Equal(40m, entry.NetPnL); // 50 - 7 - 3 = 40
    }

    [Fact]
    public async Task Enrich_CalculatesRiskRewardRatio()
    {
        var entry = new TradeEntry
        {
            Symbol = "EURUSD",
            Direction = "Buy",
            EntryPrice = 1.1000m,
            ExitPrice = 1.1050m,
            StopLoss = 1.0975m,
            TakeProfit = 1.1050m,
            PnL = 50m,
            Commission = 0m,
            Swap = 0m,
            OpenTime = DateTime.UtcNow.AddHours(-2),
            CloseTime = DateTime.UtcNow
        };

        await _enricher.EnrichAsync(entry);

        // Risk = 1.1000 - 1.0975 = 0.0025, Reward = 1.1050 - 1.1000 = 0.0050
        Assert.Equal(2.0m, entry.RiskRewardRatio);
    }

    [Fact]
    public async Task Enrich_CalculatesDuration()
    {
        var openTime = DateTime.UtcNow.AddHours(-5);
        var closeTime = DateTime.UtcNow;

        var entry = new TradeEntry
        {
            Symbol = "EURUSD",
            Direction = "Buy",
            EntryPrice = 1.1000m,
            ExitPrice = 1.1050m,
            PnL = 50m,
            Commission = 0m,
            Swap = 0m,
            OpenTime = openTime,
            CloseTime = closeTime
        };

        await _enricher.EnrichAsync(entry);

        Assert.Equal(closeTime - openTime, entry.Duration);
    }

    [Fact]
    public async Task Enrich_NoStopLoss_SkipsRiskReward()
    {
        var entry = new TradeEntry
        {
            Symbol = "EURUSD",
            Direction = "Buy",
            EntryPrice = 1.1000m,
            ExitPrice = 1.1050m,
            PnL = 50m,
            Commission = 0m,
            Swap = 0m,
            OpenTime = DateTime.UtcNow.AddHours(-2),
            CloseTime = DateTime.UtcNow
        };

        await _enricher.EnrichAsync(entry);

        Assert.Null(entry.RiskRewardRatio);
    }
}
