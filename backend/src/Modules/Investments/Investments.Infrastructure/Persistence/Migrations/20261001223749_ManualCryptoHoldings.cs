using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Investments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManualCryptoHoldings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "price",
                schema: "investments",
                table: "trades",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,8)",
                oldPrecision: 19,
                oldScale: 8);

            migrationBuilder.AlterColumn<decimal>(
                name: "last_price",
                schema: "investments",
                table: "positions",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,8)",
                oldPrecision: 19,
                oldScale: 8);

            migrationBuilder.AlterColumn<decimal>(
                name: "average_price",
                schema: "investments",
                table: "positions",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,8)",
                oldPrecision: 19,
                oldScale: 8);

            migrationBuilder.AlterColumn<decimal>(
                name: "close",
                schema: "investments",
                table: "market_prices",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,8)",
                oldPrecision: 19,
                oldScale: 8);

            migrationBuilder.CreateTable(
                name: "manual_holdings",
                schema: "investments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    average_price = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    held_since = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manual_holdings", x => x.id);
                    table.ForeignKey(
                        name: "fk_manual_holdings_securities_security_id",
                        column: x => x.security_id,
                        principalSchema: "investments",
                        principalTable: "securities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "holding_rewards",
                schema: "investments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    holding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holding_rewards", x => x.id);
                    table.ForeignKey(
                        name: "fk_holding_rewards_manual_holdings_holding_id",
                        column: x => x.holding_id,
                        principalSchema: "investments",
                        principalTable: "manual_holdings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_holding_rewards_holding_id",
                schema: "investments",
                table: "holding_rewards",
                column: "holding_id");

            migrationBuilder.CreateIndex(
                name: "ix_manual_holdings_account_id_security_id",
                schema: "investments",
                table: "manual_holdings",
                columns: new[] { "account_id", "security_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_manual_holdings_security_id",
                schema: "investments",
                table: "manual_holdings",
                column: "security_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "holding_rewards",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "manual_holdings",
                schema: "investments");

            migrationBuilder.AlterColumn<decimal>(
                name: "price",
                schema: "investments",
                table: "trades",
                type: "numeric(19,8)",
                precision: 19,
                scale: 8,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "last_price",
                schema: "investments",
                table: "positions",
                type: "numeric(19,8)",
                precision: 19,
                scale: 8,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "average_price",
                schema: "investments",
                table: "positions",
                type: "numeric(19,8)",
                precision: 19,
                scale: 8,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "close",
                schema: "investments",
                table: "market_prices",
                type: "numeric(19,8)",
                precision: 19,
                scale: 8,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);
        }
    }
}
