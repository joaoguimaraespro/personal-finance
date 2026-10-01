using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Interest on savings/bank accounts: append-only rate periods (TANB + withholding), the payout frequency on the
    /// account, and one row per account-month tracking the estimate and its reconciliation.
    /// </summary>
    public partial class AccountInterest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "interest_payout",
                schema: "finance",
                table: "accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                // Existing accounts default to monthly payouts (enums are stored as strings).
                defaultValue: "Monthly");

            migrationBuilder.CreateTable(
                name: "account_interest_rates",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    annual_rate_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    withholding_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_interest_rates", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_interest_rates_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "finance",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "interest_months",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    estimated_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    estimated_gross = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    actual_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interest_months", x => x.id);
                    table.ForeignKey(
                        name: "fk_interest_months_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "finance",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_interest_rates_account_id_effective_from",
                schema: "finance",
                table: "account_interest_rates",
                columns: new[] { "account_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interest_months_account_id_year_month",
                schema: "finance",
                table: "interest_months",
                columns: new[] { "account_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interest_months_status",
                schema: "finance",
                table: "interest_months",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_interest_rates",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "interest_months",
                schema: "finance");

            migrationBuilder.DropColumn(
                name: "interest_payout",
                schema: "finance",
                table: "accounts");
        }
    }
}
