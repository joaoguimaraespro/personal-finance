using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GoalLinkedAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                schema: "finance",
                table: "goals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_goals_account_id",
                schema: "finance",
                table: "goals",
                column: "account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_goals_accounts_account_id",
                schema: "finance",
                table: "goals",
                column: "account_id",
                principalSchema: "finance",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_goals_accounts_account_id",
                schema: "finance",
                table: "goals");

            migrationBuilder.DropIndex(
                name: "ix_goals_account_id",
                schema: "finance",
                table: "goals");

            migrationBuilder.DropColumn(
                name: "account_id",
                schema: "finance",
                table: "goals");
        }
    }
}
