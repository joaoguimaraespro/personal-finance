using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Investments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BrokerMovementReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "recorded_transaction_id",
                schema: "investments",
                table: "cash_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review",
                schema: "investments",
                table: "cash_movements",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Counted");

            // Deposits and withdrawals of the last few weeks ask where the money came from; older history keeps
            // counting as it did (CashMovement.ReviewWindowDays).
            migrationBuilder.Sql("""
                UPDATE investments.cash_movements SET review = 'Pending'
                WHERE type IN ('Deposit', 'Withdrawal') AND source <> 'Manual'
                  AND occurred_at_utc >= now() - interval '35 days';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recorded_transaction_id",
                schema: "investments",
                table: "cash_movements");

            migrationBuilder.DropColumn(
                name: "review",
                schema: "investments",
                table: "cash_movements");
        }
    }
}
