using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingAssistant.Api.Models.Journal;
using TradingAssistant.Api.Models.Trading;
using TradingAssistant.Api.Services.Orders;

namespace TradingAssistant.Tests.Orders;

public class RiskGuardTests
{
    private static IConfiguration CreateConfig(
        decimal maxTotalVolume = 10m,
        int maxPerSymbol = 3,
        decimal maxDailyLoss = 5m,
        decimal maxCorrelated = 3m)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Risk:MaxTotalVolume"] = maxTotalVolume.ToString(),
                ["Risk:MaxPositionsPerSymbol"] = maxPerSymbol.ToString(),
                ["Risk:MaxDailyLossPercent"] = maxDailyLoss.ToString(),
                ["Risk:MaxCorrelatedVolume"] = maxCorrelated.ToString(),
            })
            .Build();
    }

    [Fact]
    public async Task Validate_NoPositions_ReturnsValid()
    {
        var db = TestDbContextFactory.Create();
        var guard = new RiskGuard(db, CreateConfig(), NullLogger<RiskGuard>.Instance);

        var result = await guard.ValidateAsync("EURUSD", 1.0m, "Buy");

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_ExceedsMaxTotalVolume_ReturnsInvalid()
    {
        var db = TestDbContextFactory.Create();
        db.Positions.Add(new Position
        {
            Symbol = "EURUSD",
            Volume = 9.5m,
            Status = PositionStatus.Open,
            Direction = TradeDirection.Buy,
            AccountId = 1
        });
        await db.SaveChangesAsync();

        var guard = new RiskGuard(db, CreateConfig(maxTotalVolume: 10m), NullLogger<RiskGuard>.Instance);

        var result = await guard.ValidateAsync("GBPUSD", 1.0m, "Buy");

        Assert.False(result.IsValid);
        Assert.Contains("Max total volume exceeded", result.Reason);
    }

    [Fact]
    public async Task Validate_ExceedsMaxPerSymbol_ReturnsInvalid()
    {
        var db = TestDbContextFactory.Create();
        for (int i = 0; i < 3; i++)
        {
            db.Positions.Add(new Position
            {
                Symbol = "EURUSD",
                Volume = 0.5m,
                Status = PositionStatus.Open,
                Direction = TradeDirection.Buy,
                AccountId = 1
            });
        }
        await db.SaveChangesAsync();

        var guard = new RiskGuard(db, CreateConfig(maxPerSymbol: 3), NullLogger<RiskGuard>.Instance);

        var result = await guard.ValidateAsync("EURUSD", 0.5m, "Buy");

        Assert.False(result.IsValid);
        Assert.Contains("Max positions for EURUSD reached", result.Reason);
    }

    [Fact]
    public async Task Validate_DailyLossLimitReached_ReturnsInvalid()
    {
        var db = TestDbContextFactory.Create();

        db.Accounts.Add(new Account
        {
            Id = 1,
            Balance = 10000m,
            Equity = 9400m,
            Currency = "USD",
            IsActive = true,
            AccountNumber = "TEST001"
        });

        // Realized losses today that exceed 5% of balance
        db.TradeEntries.Add(new TradeEntry
        {
            Symbol = "EURUSD",
            Direction = "Buy",
            NetPnL = -600m,
            CloseTime = DateTime.UtcNow,
            AccountId = 1
        });

        await db.SaveChangesAsync();

        var guard = new RiskGuard(db, CreateConfig(maxDailyLoss: 5m), NullLogger<RiskGuard>.Instance);

        var result = await guard.ValidateAsync("GBPUSD", 0.5m, "Buy");

        Assert.False(result.IsValid);
        Assert.Contains("Daily loss limit reached", result.Reason);
    }

    [Fact]
    public async Task Validate_CorrelatedExposureTooHigh_ReturnsInvalid()
    {
        var db = TestDbContextFactory.Create();

        // Add correlated positions (EURUSD and GBPUSD are in the same USD weakness group)
        db.Positions.Add(new Position
        {
            Symbol = "EURUSD",
            Volume = 2.5m,
            Status = PositionStatus.Open,
            Direction = TradeDirection.Buy,
            AccountId = 1
        });
        await db.SaveChangesAsync();

        var guard = new RiskGuard(db, CreateConfig(maxCorrelated: 3m), NullLogger<RiskGuard>.Instance);

        // Adding 1.0 lot GBPUSD Buy would put correlated volume at 3.5 > 3.0
        var result = await guard.ValidateAsync("GBPUSD", 1.0m, "Buy");

        Assert.False(result.IsValid);
        Assert.Contains("Correlated exposure too high", result.Reason);
    }

    [Fact]
    public async Task Validate_ClosedPositions_NotCounted()
    {
        var db = TestDbContextFactory.Create();
        db.Positions.Add(new Position
        {
            Symbol = "EURUSD",
            Volume = 9.5m,
            Status = PositionStatus.Closed, // Closed - should not count
            Direction = TradeDirection.Buy,
            AccountId = 1
        });
        await db.SaveChangesAsync();

        var guard = new RiskGuard(db, CreateConfig(maxTotalVolume: 10m), NullLogger<RiskGuard>.Instance);

        var result = await guard.ValidateAsync("EURUSD", 1.0m, "Buy");

        Assert.True(result.IsValid);
    }
}
