using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TradingAssistant.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analysis_snapshots",
                schema: "analytics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    Bias = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric", nullable: false),
                    Recommendation = table.Column<string>(type: "text", nullable: false),
                    Reasoning = table.Column<string>(type: "text", nullable: false),
                    Support = table.Column<decimal>(type: "numeric", nullable: false),
                    Resistance = table.Column<decimal>(type: "numeric", nullable: false),
                    TradeDirection = table.Column<string>(type: "text", nullable: true),
                    TradeEntry = table.Column<decimal>(type: "numeric", nullable: true),
                    TradeStopLoss = table.Column<decimal>(type: "numeric", nullable: true),
                    TradeTakeProfit = table.Column<decimal>(type: "numeric", nullable: true),
                    TradeLotSize = table.Column<decimal>(type: "numeric", nullable: true),
                    TradeRiskReward = table.Column<decimal>(type: "numeric", nullable: true),
                    MarginRequired = table.Column<decimal>(type: "numeric", nullable: true),
                    LeverageWarning = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_snapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_analysis_snapshots_Symbol_CreatedAt",
                schema: "analytics",
                table: "analysis_snapshots",
                columns: new[] { "Symbol", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analysis_snapshots",
                schema: "analytics");
        }
    }
}
