using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingAssistant.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertRuleAiEnrichEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AiEnrichEnabled",
                schema: "alerts",
                table: "alert_rules",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiEnrichEnabled",
                schema: "alerts",
                table: "alert_rules");
        }
    }
}
