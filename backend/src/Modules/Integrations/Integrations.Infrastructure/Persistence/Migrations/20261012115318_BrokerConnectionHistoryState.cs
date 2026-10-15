using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integrations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BrokerConnectionHistoryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "history_state",
                schema: "integrations",
                table: "broker_connections",
                type: "text",
                maxLength: 10000000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "history_state",
                schema: "integrations",
                table: "broker_connections");
        }
    }
}
