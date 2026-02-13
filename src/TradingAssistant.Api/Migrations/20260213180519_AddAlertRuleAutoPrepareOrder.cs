using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingAssistant.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertRuleAutoPrepareOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoPrepareOrder",
                schema: "alerts",
                table: "alert_rules",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoPrepareOrder",
                schema: "alerts",
                table: "alert_rules");
        }
    }
}
