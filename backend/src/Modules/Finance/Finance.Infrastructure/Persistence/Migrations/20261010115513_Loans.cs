using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Loans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loans",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    first_payment_on = table.Column<DateOnly>(type: "date", nullable: false),
                    term_months = table.Column<int>(type: "integer", nullable: false),
                    rate_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision_months = table.Column<int>(type: "integer", nullable: true),
                    index_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    spread_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loans", x => x.id);
                    table.ForeignKey(
                        name: "fk_loans_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "finance",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loan_prepayments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_prepayments", x => x.id);
                    table.ForeignKey(
                        name: "fk_loan_prepayments_loans_loan_id",
                        column: x => x.loan_id,
                        principalSchema: "finance",
                        principalTable: "loans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loan_rates",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    annual_rate_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    index_rate_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_rates", x => x.id);
                    table.ForeignKey(
                        name: "fk_loan_rates_loans_loan_id",
                        column: x => x.loan_id,
                        principalSchema: "finance",
                        principalTable: "loans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_loan_prepayments_loan_id",
                schema: "finance",
                table: "loan_prepayments",
                column: "loan_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_rates_loan_id_effective_from",
                schema: "finance",
                table: "loan_rates",
                columns: new[] { "loan_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loans_account_id",
                schema: "finance",
                table: "loans",
                column: "account_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loan_prepayments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "loan_rates",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "loans",
                schema: "finance");
        }
    }
}
