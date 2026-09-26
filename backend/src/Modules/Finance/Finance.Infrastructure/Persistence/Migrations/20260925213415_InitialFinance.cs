using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    opening_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    opening_balance_on = table.Column<DateOnly>(type: "date", nullable: false),
                    institution = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    identifier = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "allocation_buckets",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    group = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation_buckets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "budgets",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budgets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    default_nature = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    icon = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_categories_categories_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "finance",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goals",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    target_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    target_date = table.Column<DateOnly>(type: "date", nullable: true),
                    starting_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    manual_current_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    icon = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    achieved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "imports",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payload = table.Column<string>(type: "jsonb", maxLength: 200, nullable: false),
                    created = table.Column<int>(type: "integer", nullable: false),
                    skipped = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    committed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rolled_back_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transaction_audit",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    changes = table.Column<string>(type: "jsonb", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transaction_audit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "allocation_checks",
                schema: "finance",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    bucket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation_checks", x => new { x.year, x.month, x.bucket_id });
                    table.ForeignKey(
                        name: "fk_allocation_checks_allocation_buckets_bucket_id",
                        column: x => x.bucket_id,
                        principalSchema: "finance",
                        principalTable: "allocation_buckets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "budget_item",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    budget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    bucket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_budget_item_allocation_buckets_bucket_id",
                        column: x => x.bucket_id,
                        principalSchema: "finance",
                        principalTable: "allocation_buckets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_budget_item_budgets_budget_id",
                        column: x => x.budget_id,
                        principalSchema: "finance",
                        principalTable: "budgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_budget_item_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "finance",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recurring_transactions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counter_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nature = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    bucket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    frequency = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    interval = table.Column<int>(type: "integer", nullable: false),
                    day_of_month = table.Column<int>(type: "integer", nullable: true),
                    start_on = table.Column<DateOnly>(type: "date", nullable: false),
                    end_on = table.Column<DateOnly>(type: "date", nullable: true),
                    next_due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_recurring_transactions_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "finance",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transactions_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "finance",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counter_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nature = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    bucket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    original_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    fx_rate = table.Column<decimal>(type: "numeric(19,10)", precision: 19, scale: 10, nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    base_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    import_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    expected_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_transactions_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "finance",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transactions_accounts_counter_account_id",
                        column: x => x.counter_account_id,
                        principalSchema: "finance",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transactions_allocation_buckets_bucket_id",
                        column: x => x.bucket_id,
                        principalSchema: "finance",
                        principalTable: "allocation_buckets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transactions_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "finance",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transactions_goals_goal_id",
                        column: x => x.goal_id,
                        principalSchema: "finance",
                        principalTable: "goals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "expected_transactions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recurring_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expected_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_expected_transactions_recurring_transactions_recurring_tran",
                        column: x => x.recurring_transaction_id,
                        principalSchema: "finance",
                        principalTable: "recurring_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_allocation_buckets_key",
                schema: "finance",
                table: "allocation_buckets",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_allocation_checks_bucket_id",
                schema: "finance",
                table: "allocation_checks",
                column: "bucket_id");

            migrationBuilder.CreateIndex(
                name: "ix_budget_item_bucket_id",
                schema: "finance",
                table: "budget_item",
                column: "bucket_id");

            migrationBuilder.CreateIndex(
                name: "ix_budget_item_budget_id",
                schema: "finance",
                table: "budget_item",
                column: "budget_id");

            migrationBuilder.CreateIndex(
                name: "ix_budget_item_category_id",
                schema: "finance",
                table: "budget_item",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_budgets_effective_from",
                schema: "finance",
                table: "budgets",
                column: "effective_from",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_categories_key",
                schema: "finance",
                table: "categories",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_id",
                schema: "finance",
                table: "categories",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_expected_transactions_recurring_transaction_id_due_on",
                schema: "finance",
                table: "expected_transactions",
                columns: new[] { "recurring_transaction_id", "due_on" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expected_transactions_status",
                schema: "finance",
                table: "expected_transactions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_imports_created_at_utc",
                schema: "finance",
                table: "imports",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_account_id",
                schema: "finance",
                table: "recurring_transactions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_category_id",
                schema: "finance",
                table: "recurring_transactions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_audit_transaction_id",
                schema: "finance",
                table: "transaction_audit",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_account_id",
                schema: "finance",
                table: "transactions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_bucket_id",
                schema: "finance",
                table: "transactions",
                column: "bucket_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_category_id",
                schema: "finance",
                table: "transactions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_counter_account_id",
                schema: "finance",
                table: "transactions",
                column: "counter_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_goal_id",
                schema: "finance",
                table: "transactions",
                column: "goal_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_import_id",
                schema: "finance",
                table: "transactions",
                column: "import_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_occurred_on",
                schema: "finance",
                table: "transactions",
                column: "occurred_on");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_source_external_id",
                schema: "finance",
                table: "transactions",
                columns: new[] { "source", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_type_occurred_on",
                schema: "finance",
                table: "transactions",
                columns: new[] { "type", "occurred_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allocation_checks",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "budget_item",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "expected_transactions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "imports",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "transaction_audit",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "budgets",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "recurring_transactions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "allocation_buckets",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "goals",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "finance");
        }
    }
}
