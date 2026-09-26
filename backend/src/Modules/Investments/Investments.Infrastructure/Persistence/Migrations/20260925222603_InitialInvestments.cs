using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Investments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInvestments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "investments");

            migrationBuilder.CreateTable(
                name: "cash_balances",
                schema: "investments",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    synced_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_balances", x => new { x.account_id, x.currency });
                });

            migrationBuilder.CreateTable(
                name: "cash_movements",
                schema: "investments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_movements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fx_rates",
                schema: "investments",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    quote = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(19,10)", precision: 19, scale: 10, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fx_rates", x => new { x.date, x.quote });
                });

            migrationBuilder.CreateTable(
                name: "manual_assets",
                schema: "investments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manual_assets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "market_prices",
                schema: "investments",
                columns: table => new
                {
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    close = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_prices", x => new { x.security_id, x.date });
                });

            migrationBuilder.CreateTable(
                name: "net_worth_snapshots",
                schema: "investments",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    assets_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    liabilities_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    breakdown = table.Column<string>(type: "jsonb", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_net_worth_snapshots", x => x.date);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_snapshots",
                schema: "investments",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    market_value_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    cash_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    net_flow_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolio_snapshots", x => new { x.account_id, x.date });
                });

            migrationBuilder.CreateTable(
                name: "securities",
                schema: "investments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    symbol = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    exchange = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    asset_class = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    asset_class_override = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_securities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "target_allocations",
                schema: "investments",
                columns: table => new
                {
                    asset_class = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    percent = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_target_allocations", x => x.asset_class);
                });

            migrationBuilder.CreateTable(
                name: "asset_valuation",
                schema: "investments",
                columns: table => new
                {
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_valuation", x => new { x.asset_id, x.date });
                    table.ForeignKey(
                        name: "fk_asset_valuation_manual_assets_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "investments",
                        principalTable: "manual_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "broker_symbols",
                schema: "investments",
                columns: table => new
                {
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    symbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    security_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_symbols", x => new { x.source, x.symbol });
                    table.ForeignKey(
                        name: "fk_broker_symbols_securities_security_id",
                        column: x => x.security_id,
                        principalSchema: "investments",
                        principalTable: "securities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dividends",
                schema: "investments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    withholding_tax = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    net_base_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    withholding_derived = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dividends", x => x.id);
                    table.ForeignKey(
                        name: "fk_dividends_securities_security_id",
                        column: x => x.security_id,
                        principalSchema: "investments",
                        principalTable: "securities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                schema: "investments",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    average_price = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: false),
                    last_price = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: false),
                    price_as_of_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    synced_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => new { x.account_id, x.security_id });
                    table.ForeignKey(
                        name: "fk_positions_securities_security_id",
                        column: x => x.security_id,
                        principalSchema: "investments",
                        principalTable: "securities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trades",
                schema: "investments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    price = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    fees = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    taxes = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    costs_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    realized_pnl_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    executed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trades", x => x.id);
                    table.ForeignKey(
                        name: "fk_trades_securities_security_id",
                        column: x => x.security_id,
                        principalSchema: "investments",
                        principalTable: "securities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_broker_symbols_security_id",
                schema: "investments",
                table: "broker_symbols",
                column: "security_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_movements_account_id_occurred_at_utc",
                schema: "investments",
                table: "cash_movements",
                columns: new[] { "account_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_movements_source_external_id",
                schema: "investments",
                table: "cash_movements",
                columns: new[] { "source", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dividends_paid_on",
                schema: "investments",
                table: "dividends",
                column: "paid_on");

            migrationBuilder.CreateIndex(
                name: "ix_dividends_security_id",
                schema: "investments",
                table: "dividends",
                column: "security_id");

            migrationBuilder.CreateIndex(
                name: "ix_dividends_source_external_id",
                schema: "investments",
                table: "dividends",
                columns: new[] { "source", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_positions_security_id",
                schema: "investments",
                table: "positions",
                column: "security_id");

            migrationBuilder.CreateIndex(
                name: "ix_securities_isin",
                schema: "investments",
                table: "securities",
                column: "isin",
                unique: true,
                filter: "isin IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_trades_account_id_executed_at_utc",
                schema: "investments",
                table: "trades",
                columns: new[] { "account_id", "executed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_trades_security_id",
                schema: "investments",
                table: "trades",
                column: "security_id");

            migrationBuilder.CreateIndex(
                name: "ix_trades_source_external_id",
                schema: "investments",
                table: "trades",
                columns: new[] { "source", "external_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_valuation",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "broker_symbols",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "cash_balances",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "cash_movements",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "dividends",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "fx_rates",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "market_prices",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "net_worth_snapshots",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "portfolio_snapshots",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "positions",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "target_allocations",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "trades",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "manual_assets",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "securities",
                schema: "investments");
        }
    }
}
