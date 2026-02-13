using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TradingAssistant.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "trading");

            migrationBuilder.EnsureSchema(
                name: "alerts");

            migrationBuilder.EnsureSchema(
                name: "analytics");

            migrationBuilder.EnsureSchema(
                name: "journal");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CTraderAccountId = table.Column<long>(type: "bigint", nullable: false),
                    AccountNumber = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric", nullable: false),
                    Equity = table.Column<decimal>(type: "numeric", nullable: false),
                    Margin = table.Column<decimal>(type: "numeric", nullable: false),
                    FreeMargin = table.Column<decimal>(type: "numeric", nullable: false),
                    MarginLevel = table.Column<decimal>(type: "numeric", nullable: false),
                    UnrealizedPnL = table.Column<decimal>(type: "numeric", nullable: false),
                    Leverage = table.Column<int>(type: "integer", nullable: false),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "alert_rules",
                schema: "alerts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyTelegram = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyWhatsApp = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyDashboard = table.Column<bool>(type: "boolean", nullable: false),
                    MaxTriggers = table.Column<int>(type: "integer", nullable: true),
                    TriggerCount = table.Column<int>(type: "integer", nullable: false),
                    LastTriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ctrader_tokens",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccessToken = table.Column<string>(type: "text", nullable: false),
                    RefreshToken = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ctrader_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "daily_stats",
                schema: "analytics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalTrades = table.Column<int>(type: "integer", nullable: false),
                    WinningTrades = table.Column<int>(type: "integer", nullable: false),
                    LosingTrades = table.Column<int>(type: "integer", nullable: false),
                    WinRate = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalPnL = table.Column<decimal>(type: "numeric", nullable: false),
                    GrossProfit = table.Column<decimal>(type: "numeric", nullable: false),
                    GrossLoss = table.Column<decimal>(type: "numeric", nullable: false),
                    ProfitFactor = table.Column<decimal>(type: "numeric", nullable: false),
                    AverageWin = table.Column<decimal>(type: "numeric", nullable: false),
                    AverageLoss = table.Column<decimal>(type: "numeric", nullable: false),
                    LargestWin = table.Column<decimal>(type: "numeric", nullable: false),
                    LargestLoss = table.Column<decimal>(type: "numeric", nullable: false),
                    StartingBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    EndingBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_stats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "deals",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CTraderDealId = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    PositionId = table.Column<long>(type: "bigint", nullable: true),
                    OrderId = table.Column<long>(type: "bigint", nullable: true),
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Volume = table.Column<decimal>(type: "numeric", nullable: false),
                    ExecutionPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    Commission = table.Column<decimal>(type: "numeric", nullable: false),
                    Swap = table.Column<decimal>(type: "numeric", nullable: false),
                    PnL = table.Column<decimal>(type: "numeric", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "equity_snapshots",
                schema: "analytics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric", nullable: false),
                    Equity = table.Column<decimal>(type: "numeric", nullable: false),
                    Margin = table.Column<decimal>(type: "numeric", nullable: false),
                    FreeMargin = table.Column<decimal>(type: "numeric", nullable: false),
                    UnrealizedPnL = table.Column<decimal>(type: "numeric", nullable: false),
                    OpenPositions = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equity_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CTraderOrderId = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Volume = table.Column<decimal>(type: "numeric", nullable: false),
                    LimitPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    StopPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    StopLoss = table.Column<decimal>(type: "numeric", nullable: true),
                    TakeProfit = table.Column<decimal>(type: "numeric", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pair_stats",
                schema: "analytics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    TotalTrades = table.Column<int>(type: "integer", nullable: false),
                    WinningTrades = table.Column<int>(type: "integer", nullable: false),
                    WinRate = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalPnL = table.Column<decimal>(type: "numeric", nullable: false),
                    AveragePnL = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalVolume = table.Column<decimal>(type: "numeric", nullable: false),
                    AverageDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    BestTrade = table.Column<decimal>(type: "numeric", nullable: false),
                    WorstTrade = table.Column<decimal>(type: "numeric", nullable: false),
                    FirstTradeAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastTradeAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pair_stats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CTraderPositionId = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Volume = table.Column<decimal>(type: "numeric", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    StopLoss = table.Column<decimal>(type: "numeric", nullable: true),
                    TakeProfit = table.Column<decimal>(type: "numeric", nullable: true),
                    CurrentPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    UnrealizedPnL = table.Column<decimal>(type: "numeric", nullable: false),
                    Swap = table.Column<decimal>(type: "numeric", nullable: false),
                    Commission = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OpenTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CloseTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    RealizedPnL = table.Column<decimal>(type: "numeric", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "symbols",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CTraderSymbolId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    BaseCurrency = table.Column<string>(type: "text", nullable: false),
                    QuoteCurrency = table.Column<string>(type: "text", nullable: false),
                    Digits = table.Column<int>(type: "integer", nullable: false),
                    PipSize = table.Column<decimal>(type: "numeric", nullable: false),
                    ContractSize = table.Column<decimal>(type: "numeric", nullable: false),
                    MinVolume = table.Column<decimal>(type: "numeric", nullable: false),
                    MaxVolume = table.Column<decimal>(type: "numeric", nullable: false),
                    VolumeStep = table.Column<decimal>(type: "numeric", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_symbols", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "trade_entries",
                schema: "journal",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PositionId = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    Volume = table.Column<decimal>(type: "numeric", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    ExitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    StopLoss = table.Column<decimal>(type: "numeric", nullable: true),
                    TakeProfit = table.Column<decimal>(type: "numeric", nullable: true),
                    PnL = table.Column<decimal>(type: "numeric", nullable: false),
                    PnLPips = table.Column<decimal>(type: "numeric", nullable: false),
                    Commission = table.Column<decimal>(type: "numeric", nullable: false),
                    Swap = table.Column<decimal>(type: "numeric", nullable: false),
                    NetPnL = table.Column<decimal>(type: "numeric", nullable: false),
                    RiskRewardRatio = table.Column<decimal>(type: "numeric", nullable: true),
                    RiskPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    OpenTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CloseTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Strategy = table.Column<string>(type: "text", nullable: true),
                    Setup = table.Column<string>(type: "text", nullable: true),
                    Emotion = table.Column<string>(type: "text", nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "alert_conditions",
                schema: "alerts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AlertRuleId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Indicator = table.Column<string>(type: "text", nullable: false),
                    Operator = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: false),
                    SecondaryValue = table.Column<decimal>(type: "numeric", nullable: true),
                    Timeframe = table.Column<string>(type: "text", nullable: true),
                    Period = table.Column<int>(type: "integer", nullable: true),
                    CombineWith = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_conditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alert_conditions_alert_rules_AlertRuleId",
                        column: x => x.AlertRuleId,
                        principalSchema: "alerts",
                        principalTable: "alert_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "alert_triggers",
                schema: "alerts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AlertRuleId = table.Column<long>(type: "bigint", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    TriggerPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    AiEnrichment = table.Column<string>(type: "text", nullable: true),
                    NotifiedTelegram = table.Column<bool>(type: "boolean", nullable: false),
                    NotifiedWhatsApp = table.Column<bool>(type: "boolean", nullable: false),
                    NotifiedDashboard = table.Column<bool>(type: "boolean", nullable: false),
                    TriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_triggers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alert_triggers_alert_rules_AlertRuleId",
                        column: x => x.AlertRuleId,
                        principalSchema: "alerts",
                        principalTable: "alert_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notes",
                schema: "journal",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TradeEntryId = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notes_trade_entries_TradeEntryId",
                        column: x => x.TradeEntryId,
                        principalSchema: "journal",
                        principalTable: "trade_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "journal",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TradeEntryId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tags_trade_entries_TradeEntryId",
                        column: x => x.TradeEntryId,
                        principalSchema: "journal",
                        principalTable: "trade_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_conditions_AlertRuleId",
                schema: "alerts",
                table: "alert_conditions",
                column: "AlertRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_alert_rules_Symbol_IsActive",
                schema: "alerts",
                table: "alert_rules",
                columns: new[] { "Symbol", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_triggers_AlertRuleId",
                schema: "alerts",
                table: "alert_triggers",
                column: "AlertRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_daily_stats_Date",
                schema: "analytics",
                table: "daily_stats",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_notes_TradeEntryId",
                schema: "journal",
                table: "notes",
                column: "TradeEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_positions_Symbol_OpenTime",
                schema: "trading",
                table: "positions",
                columns: new[] { "Symbol", "OpenTime" });

            migrationBuilder.CreateIndex(
                name: "IX_symbols_CTraderSymbolId",
                schema: "trading",
                table: "symbols",
                column: "CTraderSymbolId",
                unique: true,
                filter: "\"CTraderSymbolId\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_tags_TradeEntryId",
                schema: "journal",
                table: "tags",
                column: "TradeEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_trade_entries_Symbol_CloseTime",
                schema: "journal",
                table: "trade_entries",
                columns: new[] { "Symbol", "CloseTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "alert_conditions",
                schema: "alerts");

            migrationBuilder.DropTable(
                name: "alert_triggers",
                schema: "alerts");

            migrationBuilder.DropTable(
                name: "ctrader_tokens",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "daily_stats",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "deals",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "equity_snapshots",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "notes",
                schema: "journal");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "pair_stats",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "positions",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "symbols",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "journal");

            migrationBuilder.DropTable(
                name: "alert_rules",
                schema: "alerts");

            migrationBuilder.DropTable(
                name: "trade_entries",
                schema: "journal");
        }
    }
}
