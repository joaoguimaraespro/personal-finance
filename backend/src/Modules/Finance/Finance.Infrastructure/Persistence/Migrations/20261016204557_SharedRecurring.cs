using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SharedRecurring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recurring_transaction_shares",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    percent = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    recurring_transaction_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_transaction_shares", x => x.id);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_shares_accounts_person_id",
                        column: x => x.person_id,
                        principalSchema: "finance",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_shares_recurring_transactions_recurri",
                        column: x => x.recurring_transaction_id,
                        principalSchema: "finance",
                        principalTable: "recurring_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_shares_person_id",
                schema: "finance",
                table: "recurring_transaction_shares",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_shares_recurring_transaction_id",
                schema: "finance",
                table: "recurring_transaction_shares",
                column: "recurring_transaction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_transaction_shares",
                schema: "finance");
        }
    }
}
