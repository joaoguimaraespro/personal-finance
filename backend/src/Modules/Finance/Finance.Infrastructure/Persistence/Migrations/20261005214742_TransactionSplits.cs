using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransactionSplits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recurring_transaction_splits",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    note = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    recurring_transaction_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_transaction_splits", x => x.id);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_splits_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "finance",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_splits_recurring_transactions_recurri",
                        column: x => x.recurring_transaction_id,
                        principalSchema: "finance",
                        principalTable: "recurring_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transaction_splits",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    nature = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    note = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transaction_splits", x => x.id);
                    table.ForeignKey(
                        name: "fk_transaction_splits_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "finance",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transaction_splits_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalSchema: "finance",
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_splits_category_id",
                schema: "finance",
                table: "recurring_transaction_splits",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_splits_recurring_transaction_id",
                schema: "finance",
                table: "recurring_transaction_splits",
                column: "recurring_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_splits_category_id",
                schema: "finance",
                table: "transaction_splits",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_splits_transaction_id",
                schema: "finance",
                table: "transaction_splits",
                column: "transaction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_transaction_splits",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "transaction_splits",
                schema: "finance");
        }
    }
}
