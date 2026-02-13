using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingAssistant.Api.Models.Trading;
using TradingAssistant.Api.Services.Orders;

namespace TradingAssistant.Tests.Orders;

public class PositionSizerTests
{
    private static IConfiguration CreateConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    [Fact]
    public async Task Calculate_StandardForexPair_ReturnsExpectedLots()
    {
        var db = TestDbContextFactory.Create();
        db.Accounts.Add(new Account
        {
            Id = 1,
            Balance = 10000m,
            Equity = 10000m,
            Currency = "USD",
            IsActive = true,
            AccountNumber = "TEST001"
        });
        await db.SaveChangesAsync();

        var sizer = new PositionSizer(db, CreateConfig(), NullLogger<PositionSizer>.Instance);

        // Risk 1% of $10,000 = $100, SL distance = 50 pips, pip value $10
        // Expected: $100 / (50 * $10) = 0.20 lots
        var result = await sizer.CalculateAsync("EURUSD", 1m, 1.1000m, 1.0950m);

        Assert.Equal(0.20m, result);
    }

    [Fact]
    public async Task Calculate_JPYPair_UsesCorrectPipSize()
    {
        var db = TestDbContextFactory.Create();
        db.Accounts.Add(new Account
        {
            Id = 1,
            Balance = 10000m,
            Equity = 10000m,
            Currency = "USD",
            IsActive = true,
            AccountNumber = "TEST001"
        });
        await db.SaveChangesAsync();

        var sizer = new PositionSizer(db, CreateConfig(), NullLogger<PositionSizer>.Instance);

        // Risk 1% = $100, SL distance = 50 pips (0.50 price), pip value $10
        // Expected: $100 / (50 * $10) = 0.20 lots
        var result = await sizer.CalculateAsync("USDJPY", 1m, 150.00m, 149.50m);

        Assert.Equal(0.20m, result);
    }

    [Fact]
    public async Task Calculate_Gold_UsesCorrectPipSize()
    {
        var db = TestDbContextFactory.Create();
        db.Accounts.Add(new Account
        {
            Id = 1,
            Balance = 10000m,
            Equity = 10000m,
            Currency = "USD",
            IsActive = true,
            AccountNumber = "TEST001"
        });
        await db.SaveChangesAsync();

        var sizer = new PositionSizer(db, CreateConfig(), NullLogger<PositionSizer>.Instance);

        // Risk 1% = $100, SL distance = 50 pips (5.0 price), pip value $10
        // Expected: $100 / (50 * $10) = 0.20 lots
        var result = await sizer.CalculateAsync("XAUUSD", 1m, 2000.0m, 1995.0m);

        Assert.Equal(0.20m, result);
    }

    [Fact]
    public async Task Calculate_RoundsDown_ToLotStep()
    {
        var db = TestDbContextFactory.Create();
        db.Accounts.Add(new Account
        {
            Id = 1,
            Balance = 10000m,
            Equity = 10000m,
            Currency = "USD",
            IsActive = true,
            AccountNumber = "TEST001"
        });
        await db.SaveChangesAsync();

        var sizer = new PositionSizer(db, CreateConfig(), NullLogger<PositionSizer>.Instance);

        // This should result in a non-round lot size that gets floored
        var result = await sizer.CalculateAsync("EURUSD", 1.5m, 1.1000m, 1.0930m);
        
        // Calculate unrounded expectation manually (Risk / (Distance * ValuePerPip...))
        // Assuming CalculateAsync uses standard logic, checking result is floored
        var unrounded = 10000m * 0.015m / (Math.Abs(1.1000m - 1.0930m) * 100000m); // Approx

        // Verify it's a multiple of 0.01 and strictly less-equal unrounded
        Assert.Equal(0, result % 0.01m);
        Assert.True(result <= unrounded);
    }

    [Fact]
    public async Task Calculate_RespectsMinLot()
    {
        var db = TestDbContextFactory.Create();
        db.Accounts.Add(new Account
        {
            Id = 1,
            Balance = 100m, // Very small balance
            Equity = 100m,
            Currency = "USD",
            IsActive = true,
            AccountNumber = "TEST001"
        });
        await db.SaveChangesAsync();

        var sizer = new PositionSizer(db, CreateConfig(), NullLogger<PositionSizer>.Instance);

        var result = await sizer.CalculateAsync("EURUSD", 1m, 1.1000m, 1.0500m);

        Assert.Equal(0.01m, result); // Min lot enforced
    }

    [Fact]
    public async Task Calculate_NoActiveAccount_ThrowsInvalidOperation()
    {
        var db = TestDbContextFactory.Create();
        var sizer = new PositionSizer(db, CreateConfig(), NullLogger<PositionSizer>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sizer.CalculateAsync("EURUSD", 1m, 1.1000m, 1.0950m));
    }

    [Fact]
    public async Task Calculate_ZeroStopLossDistance_ThrowsInvalidOperation()
    {
        var db = TestDbContextFactory.Create();
        db.Accounts.Add(new Account
        {
            Id = 1,
            Balance = 10000m,
            Equity = 10000m,
            Currency = "USD",
            IsActive = true,
            AccountNumber = "TEST001"
        });
        await db.SaveChangesAsync();

        var sizer = new PositionSizer(db, CreateConfig(), NullLogger<PositionSizer>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sizer.CalculateAsync("EURUSD", 1m, 1.1000m, 1.1000m));
    }
}
