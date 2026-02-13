using Microsoft.Extensions.Logging.Abstractions;
using TradingAssistant.Api.Models.Journal;
using TradingAssistant.Api.Services.Journal;

namespace TradingAssistant.Tests.Journal;

public class AnalyticsAggregatorTests
{
    [Fact]
    public async Task UpdateDailyStats_NewDay_CreatesStats()
    {
        var db = TestDbContextFactory.Create();
        var aggregator = new AnalyticsAggregator(db, NullLogger<AnalyticsAggregator>.Instance);

        var trade = new TradeEntry
        {
            AccountId = 1,
            Symbol = "EURUSD",
            Direction = "Buy",
            Volume = 1.0m,
            EntryPrice = 1.1000m,
            ExitPrice = 1.1050m,
            PnL = 50m,
            NetPnL = 47m,
            CloseTime = DateTime.UtcNow,
            OpenTime = DateTime.UtcNow.AddHours(-2)
        };
        db.TradeEntries.Add(trade);
        await db.SaveChangesAsync();

        await aggregator.UpdateDailyStatsAsync(trade);

        var stats = db.DailyStats.FirstOrDefault();
        Assert.NotNull(stats);
        Assert.Equal(1, stats.TotalTrades);
        Assert.Equal(1, stats.WinningTrades);
        Assert.Equal(0, stats.LosingTrades);
        Assert.Equal(100m, stats.WinRate);
        Assert.Equal(47m, stats.TotalPnL);
    }

    [Fact]
    public async Task UpdateDailyStats_ExistingDay_UpdatesStats()
    {
        var db = TestDbContextFactory.Create();
        var aggregator = new AnalyticsAggregator(db, NullLogger<AnalyticsAggregator>.Instance);

        // First trade (winning)
        var trade1 = new TradeEntry
        {
            AccountId = 1,
            Symbol = "EURUSD",
            Direction = "Buy",
            Volume = 1.0m,
            PnL = 50m,
            NetPnL = 50m,
            CloseTime = DateTime.UtcNow,
            OpenTime = DateTime.UtcNow.AddHours(-2)
        };
        db.TradeEntries.Add(trade1);
        await db.SaveChangesAsync();
        await aggregator.UpdateDailyStatsAsync(trade1);

        // Second trade (losing)
        var trade2 = new TradeEntry
        {
            AccountId = 1,
            Symbol = "GBPUSD",
            Direction = "Sell",
            Volume = 0.5m,
            PnL = -30m,
            NetPnL = -30m,
            CloseTime = DateTime.UtcNow,
            OpenTime = DateTime.UtcNow.AddHours(-1)
        };
        db.TradeEntries.Add(trade2);
        await db.SaveChangesAsync();
        await aggregator.UpdateDailyStatsAsync(trade2);

        var stats = db.DailyStats.FirstOrDefault();
        Assert.NotNull(stats);
        Assert.Equal(2, stats.TotalTrades);
        Assert.Equal(1, stats.WinningTrades);
        Assert.Equal(1, stats.LosingTrades);
        Assert.Equal(50m, stats.WinRate);
        Assert.Equal(20m, stats.TotalPnL); // 50 - 30
    }

    [Fact]
    public async Task UpdatePairStats_CreatesStatsForSymbol()
    {
        var db = TestDbContextFactory.Create();
        var aggregator = new AnalyticsAggregator(db, NullLogger<AnalyticsAggregator>.Instance);

        var trade = new TradeEntry
        {
            AccountId = 1,
            Symbol = "EURUSD",
            Direction = "Buy",
            Volume = 1.0m,
            PnL = 50m,
            NetPnL = 47m,
            CloseTime = DateTime.UtcNow,
            OpenTime = DateTime.UtcNow.AddHours(-2)
        };
        db.TradeEntries.Add(trade);
        await db.SaveChangesAsync();

        await aggregator.UpdatePairStatsAsync(trade);

        var stats = db.PairStats.FirstOrDefault(s => s.Symbol == "EURUSD");
        Assert.NotNull(stats);
        Assert.Equal(1, stats.TotalTrades);
        Assert.Equal(1, stats.WinningTrades);
        Assert.Equal(47m, stats.TotalPnL);
        Assert.Equal(1.0m, stats.TotalVolume);
    }

    [Fact]
    public async Task RecalculateAll_AggregatesMultipleTrades()
    {
        var db = TestDbContextFactory.Create();
        var aggregator = new AnalyticsAggregator(db, NullLogger<AnalyticsAggregator>.Instance);

        db.TradeEntries.AddRange(
            new TradeEntry
            {
                AccountId = 1,
                Symbol = "EURUSD",
                Direction = "Buy",
                Volume = 1.0m,
                PnL = 50m,
                NetPnL = 50m,
                OpenTime = DateTime.UtcNow.AddHours(-5),
                CloseTime = DateTime.UtcNow.AddHours(-3)
            },
            new TradeEntry
            {
                AccountId = 1,
                Symbol = "EURUSD",
                Direction = "Sell",
                Volume = 0.5m,
                PnL = -20m,
                NetPnL = -20m,
                OpenTime = DateTime.UtcNow.AddHours(-3),
                CloseTime = DateTime.UtcNow.AddHours(-1)
            }
        );
        await db.SaveChangesAsync();

        await aggregator.RecalculateAllAsync(DateTime.UtcNow.AddDays(-1));

        var dailyStats = db.DailyStats.ToList();
        Assert.Single(dailyStats);
        Assert.Equal(2, dailyStats[0].TotalTrades);

        var pairStats = db.PairStats.ToList();
        Assert.Single(pairStats);
        Assert.Equal("EURUSD", pairStats[0].Symbol);
        Assert.Equal(30m, pairStats[0].TotalPnL); // 50 - 20
    }

    [Fact]
    public async Task UpdateDailyStats_CalculatesProfitFactor()
    {
        var db = TestDbContextFactory.Create();
        var aggregator = new AnalyticsAggregator(db, NullLogger<AnalyticsAggregator>.Instance);

        var trade1 = new TradeEntry
            {
                AccountId = 1, Symbol = "EURUSD", Direction = "Buy",
                PnL = 100m, NetPnL = 100m, Volume = 1000m,
                OpenTime = DateTime.UtcNow.AddHours(-5),
                CloseTime = DateTime.UtcNow.AddHours(-3)
            };
        var trade2 = new TradeEntry
            {
                AccountId = 1, Symbol = "GBPUSD", Direction = "Sell",
                PnL = -50m, NetPnL = -50m, Volume = 1000m,
                OpenTime = DateTime.UtcNow.AddHours(-3),
                CloseTime = DateTime.UtcNow.AddHours(-1)
            };

        db.TradeEntries.AddRange(trade1, trade2);
        await db.SaveChangesAsync();

        await aggregator.UpdateDailyStatsAsync(trade1);
        await aggregator.UpdateDailyStatsAsync(trade2);

        var stats = db.DailyStats.First();
        Assert.Equal(2m, stats.ProfitFactor); // GrossProfit 100 / GrossLoss 50 = 2
        Assert.Equal(100m, stats.GrossProfit);
        Assert.Equal(50m, stats.GrossLoss);
    }
}
