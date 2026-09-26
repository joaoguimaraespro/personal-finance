extern alias mcp;

using System.Reflection;
using Ai.Contracts;
using ModelContextProtocol.Server;
using FinanceMcpTools = mcp::Host.Mcp.FinanceMcpTools;

namespace Architecture.Tests;

/// <summary>The AI surface is small, typed and read-only — and it is enforced at build time.</summary>
public sealed class AiBoundaryTests
{
    private static readonly Assembly Mcp = typeof(FinanceMcpTools).Assembly;

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

    [Fact]
    public void Mcp_tools_mirror_the_catalogue_and_are_all_read_only()
    {
        var tools = typeof(FinanceMcpTools).GetMethods()
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)
            .ToList();

        tools.Select(t => t!.Name).ShouldBe(AiTools.All.Select(t => t.Name), ignoreOrder: true);
        tools.ShouldAllBe(t => t!.ReadOnly && !t.Destructive && t.Idempotent && !t.OpenWorld);
    }

    [Fact]
    public void Catalogue_contains_no_sql_shell_file_or_write_tools()
    {
        string[] forbidden = ["sql", "query", "database", "shell", "exec", "file", "create", "update", "delete", "write",
            "order", "trade", "transfer", "withdraw", "deposit", "set_", "add_"];

        AiTools.All.Select(t => t.Name)
            .Where(name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ShouldBeEmpty();
        AiTools.All.ShouldAllBe(t => t.Name.StartsWith("get_", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_tool_needs_a_known_non_sensitive_scope_and_sensitive_scopes_only_extend_answers()
    {
        AiTools.All.ShouldAllBe(t => AiScopes.Names.Contains(t.Scope));
        var sensitive = AiScopes.All.Where(s => s.Sensitive).Select(s => s.Name).ToHashSet();
        AiTools.All.ShouldAllBe(t => !sensitive.Contains(t.Scope));
        sensitive.ShouldBe(["accounts.identifiers.read", "raw.transactions.read", "personal.notes.read"], ignoreOrder: true);
    }
}
