using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Models.Alerts;
using TradingAssistant.Api.Models.Analytics;
using TradingAssistant.Api.Models.Journal;
using TradingAssistant.Api.Models.Trading;

namespace TradingAssistant.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // Trading schema
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<Symbol> Symbols => Set<Symbol>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<CTraderToken> CTraderTokens => Set<CTraderToken>();

    // Alerts schema
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<AlertTrigger> AlertTriggers => Set<AlertTrigger>();
    public DbSet<AlertCondition> AlertConditions => Set<AlertCondition>();

    // Journal schema
    public DbSet<TradeEntry> TradeEntries => Set<TradeEntry>();
    public DbSet<TradeTag> TradeTags => Set<TradeTag>();
    public DbSet<TradeNote> TradeNotes => Set<TradeNote>();

    // Analytics schema
    public DbSet<DailyStats> DailyStats => Set<DailyStats>();
    public DbSet<PairStats> PairStats => Set<PairStats>();
    public DbSet<EquitySnapshot> EquitySnapshots => Set<EquitySnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure schemas
        modelBuilder.Entity<Position>().ToTable("positions", "trading");
        modelBuilder.Entity<Order>().ToTable("orders", "trading");
        modelBuilder.Entity<Deal>().ToTable("deals", "trading");
        modelBuilder.Entity<Symbol>().ToTable("symbols", "trading");
        modelBuilder.Entity<Account>().ToTable("accounts", "trading");
        modelBuilder.Entity<CTraderToken>().ToTable("ctrader_tokens", "trading");

        modelBuilder.Entity<AlertRule>().ToTable("alert_rules", "alerts");
        modelBuilder.Entity<AlertTrigger>().ToTable("alert_triggers", "alerts");
        modelBuilder.Entity<AlertCondition>().ToTable("alert_conditions", "alerts");

        modelBuilder.Entity<TradeEntry>().ToTable("trade_entries", "journal");
        modelBuilder.Entity<TradeTag>().ToTable("tags", "journal");
        modelBuilder.Entity<TradeNote>().ToTable("notes", "journal");

        modelBuilder.Entity<DailyStats>().ToTable("daily_stats", "analytics");
        modelBuilder.Entity<PairStats>().ToTable("pair_stats", "analytics");
        modelBuilder.Entity<EquitySnapshot>().ToTable("equity_snapshots", "analytics");

        // Configure indexes for frequently queried columns
        modelBuilder.Entity<Position>()
            .HasIndex(p => new { p.Symbol, p.OpenTime });

        modelBuilder.Entity<TradeEntry>()
            .HasIndex(t => new { t.Symbol, t.CloseTime });

        modelBuilder.Entity<AlertRule>()
            .HasIndex(a => new { a.Symbol, a.IsActive });

        modelBuilder.Entity<DailyStats>()
            .HasIndex(d => d.Date);

        modelBuilder.Entity<Symbol>()
            .HasIndex(s => s.CTraderSymbolId)
            .IsUnique()
            .HasFilter("\"CTraderSymbolId\" > 0");
    }
}
