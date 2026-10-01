using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Investments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "estimated_holdings",
                schema: "investments",
                table: "portfolio_snapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "price_listings",
                schema: "investments",
                columns: table => new
                {
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    symbol = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    covered_from = table.Column<DateOnly>(type: "date", nullable: true),
                    covered_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_listings", x => x.security_id);
                    table.ForeignKey(
                        name: "fk_price_listings_securities_security_id",
                        column: x => x.security_id,
                        principalSchema: "investments",
                        principalTable: "securities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_listings",
                schema: "investments");

            migrationBuilder.DropColumn(
                name: "estimated_holdings",
                schema: "investments",
                table: "portfolio_snapshots");
        }
    }
}
