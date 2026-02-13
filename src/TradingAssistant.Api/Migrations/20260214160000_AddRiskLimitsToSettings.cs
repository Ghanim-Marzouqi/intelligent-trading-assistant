using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingAssistant.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskLimitsToSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxOpenPositions",
                schema: "analytics",
                table: "analysis_settings",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxTotalVolume",
                schema: "analytics",
                table: "analysis_settings",
                type: "numeric",
                nullable: false,
                defaultValue: 10m);

            migrationBuilder.AddColumn<int>(
                name: "MaxPositionsPerSymbol",
                schema: "analytics",
                table: "analysis_settings",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDailyLossPercent",
                schema: "analytics",
                table: "analysis_settings",
                type: "numeric",
                nullable: false,
                defaultValue: 5m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxOpenPositions",
                schema: "analytics",
                table: "analysis_settings");

            migrationBuilder.DropColumn(
                name: "MaxTotalVolume",
                schema: "analytics",
                table: "analysis_settings");

            migrationBuilder.DropColumn(
                name: "MaxPositionsPerSymbol",
                schema: "analytics",
                table: "analysis_settings");

            migrationBuilder.DropColumn(
                name: "MaxDailyLossPercent",
                schema: "analytics",
                table: "analysis_settings");
        }
    }
}
