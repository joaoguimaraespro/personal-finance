using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Investments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CryptoRolling24h : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reference24h_at_utc",
                schema: "investments",
                table: "price_listings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reference24h_price",
                schema: "investments",
                table: "price_listings",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reference24h_at_utc",
                schema: "investments",
                table: "price_listings");

            migrationBuilder.DropColumn(
                name: "reference24h_price",
                schema: "investments",
                table: "price_listings");
        }
    }
}
