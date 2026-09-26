using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ai.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AiClientInternal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "internal",
                schema: "ai",
                table: "clients",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "internal",
                schema: "ai",
                table: "clients");
        }
    }
}
