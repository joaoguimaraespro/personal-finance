extern alias mcp;

using System.Reflection;
using Ai.Application.Gateway;
using Ai.Application.Tools;
using Ai.Contracts;
using ModelContextProtocol.Server;
using NetArchTest.Rules;
using FinanceMcpTools = mcp::Host.Mcp.FinanceMcpTools;

namespace Architecture.Tests;

/// <summary>
/// The AI surface is small and typed, reads are read-only, and writes exist only as the reviewed, opt-in,
/// single-record tools of ADR-0008 — all enforced at build time.
/// </summary>
public sealed class AiBoundaryTests
{
    private static readonly Assembly Mcp = typeof(FinanceMcpTools).Assembly;
    private static readonly Assembly AiApplication = typeof(AiGateway).Assembly;

    /// <summary>The explicit allow-list of write scopes. Adding one is a deliberate edit here and in ADR-0008.</summary>
    private static readonly string[] AllowedWriteScopes =
        ["transactions.write", "recurring.write", "planning.write", "holdings.write"];

    [Fact]
    public void Mcp_host_has_no_path_to_the_database()
    {
        var references = Mcp.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        references.ShouldNotContain(r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        references.ShouldNotContain(r => r.StartsWith("Npgsql", StringComparison.Ordinal));
        references.ShouldNotContain(r => r.StartsWith("Finance.", StringComparison.Ordinal));
        references.ShouldNotContain(r => r.StartsWith("Investments.", StringComparison.Ordinal));
        references.ShouldNotContain(r => r.StartsWith("Integrations.", StringComparison.Ordinal));
        references.ShouldNotContain("Ai.Application");
    }

    /// <summary>
    /// Every MCP tool mirrors its catalogue entry: name, argument names and annotations. Read tools are read-only,
    /// non-destructive and idempotent; write tools say they are not read-only, so MCP clients can ask first.
    /// </summary>
    [Fact]
    public void Mcp_tools_mirror_the_catalogue_with_matching_annotations()
    {
        var methods = typeof(FinanceMcpTools).GetMethods()
            .Select(m => (Method: m, Attribute: m.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(x => x.Attribute is not null)
            .ToList();

        methods.Select(x => x.Attribute!.Name).ShouldBe(AiTools.All.Select(t => t.Name), ignoreOrder: true);
        foreach (var (method, attribute) in methods)
        {
            var definition = AiTools.Find(attribute!.Name!)!;
            attribute.ReadOnly.ShouldBe(!definition.Write, definition.Name);
            attribute.Destructive.ShouldBe(definition.Destructive, definition.Name);
            attribute.Idempotent.ShouldBe(definition.Idempotent, definition.Name);
            attribute.OpenWorld.ShouldBeFalse(definition.Name);
            method.GetParameters().Where(p => p.ParameterType != typeof(CancellationToken)).Select(p => p.Name)
                .ShouldBe(definition.Parameters.Select(p => p.Name), ignoreOrder: true, definition.Name);
        }
    }

    [Fact]
    public void Catalogue_contains_no_sql_shell_file_trading_or_bulk_tools()
    {
        string[] forbidden = ["sql", "query", "database", "shell", "exec", "file", "order", "trade", "withdraw",
            "deposit", "bulk", "batch", "many", "all_", "import", "export", "purge", "restore", "client", "scope",
            "token", "account_identifier", "broker"];

        AiTools.All.Select(t => t.Name)
            .Where(name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ShouldBeEmpty();
        AiTools.All.SelectMany(t => t.Parameters).Select(p => p.Type).Distinct()
            .ShouldBeSubsetOf(["string", "integer", "number"]); // no arrays or objects: one record per call
    }

    [Fact]
    public void Read_tools_are_gets_with_read_scopes()
    {
        string[] writeVerbs = ["create", "update", "delete", "write", "set_", "add_", "upsert", "confirm", "skip", "remove"];
        var reads = AiTools.All.Where(t => !t.Write).ToList();

        reads.ShouldAllBe(t => t.Name.StartsWith("get_", StringComparison.Ordinal));
        reads.ShouldAllBe(t => t.Scope.EndsWith(".read", StringComparison.Ordinal));
        reads.ShouldAllBe(t => !t.Destructive && t.Idempotent);
        reads.Where(t => writeVerbs.Any(v => t.Name.Contains(v, StringComparison.OrdinalIgnoreCase))).ShouldBeEmpty();
        AiTools.Reads.ShouldBe(reads);
    }

    /// <summary>
    /// Write tools are allowed only with a write scope from the explicit allow-list; write scopes are sensitive and
    /// used by nothing else; deletes and skips are flagged destructive; deletes take exactly one id.
    /// </summary>
    [Fact]
    public void Write_tools_need_an_allow_listed_write_scope()
    {
        var writes = AiTools.All.Where(t => t.Write).ToList();

        writes.ShouldAllBe(t => AllowedWriteScopes.Contains(t.Scope));
        AiScopes.WriteScopes.ShouldBe(AllowedWriteScopes, ignoreOrder: true);
        AiScopes.All.Where(s => s.Write).Select(s => s.Name).ShouldBe(AllowedWriteScopes, ignoreOrder: true);
        AiScopes.All.Where(s => s.Write).ShouldAllBe(s => s.Sensitive);
        AiTools.All.Where(t => AllowedWriteScopes.Contains(t.Scope)).ShouldAllBe(t => t.Write);
        AiTools.Writes.ShouldBe(writes);

        string[] verbs = ["create_", "update_", "delete_", "confirm_", "skip_", "upsert_", "set_", "add_"];
        writes.ShouldAllBe(t => verbs.Any(v => t.Name.StartsWith(v, StringComparison.Ordinal)));
        writes.Where(t => t.Name.StartsWith("delete_", StringComparison.Ordinal) || t.Name.StartsWith("skip_", StringComparison.Ordinal))
            .ShouldAllBe(t => t.Destructive);
        writes.Where(t => t.Destructive).ShouldAllBe(t =>
            t.Parameters.Count(p => p.Required) == 1 && t.Parameters.All(p => p.Name.EndsWith("_id") || p.Name == "idempotency_key"));
        writes.ShouldAllBe(t => t.Parameters.Any(p => p.Name == "idempotency_key" && !p.Required));
    }

    [Fact]
    public void Read_tools_need_a_known_non_sensitive_scope_and_sensitive_read_scopes_only_extend_answers()
    {
        AiTools.All.ShouldAllBe(t => AiScopes.Names.Contains(t.Scope));
        var sensitive = AiScopes.All.Where(s => s.Sensitive).Select(s => s.Name).ToHashSet();
        AiTools.Reads.ShouldAllBe(t => !sensitive.Contains(t.Scope));
        sensitive.ShouldBe(["accounts.identifiers.read", "raw.transactions.read", "personal.notes.read",
            .. AllowedWriteScopes], ignoreOrder: true);
    }

    /// <summary>
    /// Write tools run only through the gateway (authentication, scopes, limits, idempotency, audit): nothing else in
    /// the AI module may hold them.
    /// </summary>
    [Fact]
    public void Write_tools_are_reachable_only_through_the_gateway()
    {
        var holders = Types.InAssembly(AiApplication).That().HaveDependencyOn(typeof(WriteTools).FullName).GetTypes()
            .Select(t => TopLevel(t).Name)
            .Distinct()
            .ToList();

        holders.ShouldBeSubsetOf([nameof(WriteTools), nameof(AiGateway), nameof(Ai.Application.AiEndpoints)]);
        holders.ShouldContain(nameof(AiGateway));
    }

    /// <summary>
    /// Broker data belongs to the read-only integrations (ADR-0005): the write tools have no dependency on broker
    /// positions, trades, dividends, cash, snapshots, the sync writer or the integrations.
    /// </summary>
    [Fact]
    public void Write_tools_never_touch_broker_sourced_entities()
    {
        var result = Types.InAssembly(AiApplication).That().HaveName(nameof(WriteTools)).ShouldNot()
            .HaveDependencyOnAny("Investments.Domain.Position", "Investments.Domain.Trade", "Investments.Domain.Dividend",
                "Investments.Domain.CashMovement", "Investments.Domain.CashBalance", "Investments.Domain.PortfolioSnapshot",
                "Investments.Domain.BrokerSymbol", "Investments.Application.Sync", "Investments.Application.Portfolio",
                "Integrations")
            .GetResult();

        (result.FailingTypeNames ?? []).ShouldBeEmpty();

        // Positive control: the read tools do use the portfolio, so the rule can see such a dependency.
        Types.InAssembly(AiApplication).That().HaveName(nameof(FinanceTools)).ShouldNot()
            .HaveDependencyOnAny("Investments.Application.Portfolio").GetResult().IsSuccessful.ShouldBeFalse();
    }

    /// <summary>
    /// The exact surface, tool by tool. Adding a tool or moving one to another scope changes what existing clients
    /// can see or do, so it must be a deliberate edit here (and in docs/ai.md), never a side effect.
    /// </summary>
    [Fact]
    public void Catalogue_is_exactly_the_reviewed_tool_and_scope_list()
    {
        AiTools.All.Select(t => $"{t.Name} -> {t.Scope}").ShouldBe(
        [
            "get_financial_overview -> overview.read",
            "get_year_breakdown -> overview.read",
            "get_monthly_summary -> overview.read",
            "get_expense_summary -> expenses.summary.read",
            "get_expense_transactions -> expenses.transactions.read",
            "get_transactions -> transactions.read",
            "get_income_summary -> income.summary.read",
            "get_net_worth -> networth.read",
            "get_net_worth_history -> networth.read",
            "get_accounts -> accounts.balances.read",
            "get_recurring -> recurring.read",
            "get_portfolio_summary -> portfolio.summary.read",
            "get_allocation -> portfolio.summary.read",
            "get_positions -> portfolio.positions.read",
            "get_portfolio_performance -> portfolio.performance.read",
            "get_dividend_summary -> dividends.read",
            "get_budget_status -> budget.read",
            "get_goals -> goals.read",
            "create_transaction -> transactions.write",
            "update_transaction -> transactions.write",
            "delete_transaction -> transactions.write",
            "confirm_expected -> recurring.write",
            "skip_expected -> recurring.write",
            "upsert_recurring -> recurring.write",
            "set_budget_limit -> planning.write",
            "upsert_goal -> planning.write",
            "add_to_goal -> planning.write",
            "update_crypto_holding -> holdings.write",
            "add_crypto_reward -> holdings.write",
            "update_asset_value -> holdings.write",
        ], ignoreOrder: true);
    }

    private static Type TopLevel(Type t)
    {
        while (t.DeclaringType is not null)
        {
            t = t.DeclaringType;
        }

        return t;
    }
}
