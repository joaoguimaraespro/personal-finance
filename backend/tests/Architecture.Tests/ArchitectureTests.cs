using System.Reflection;
using NetArchTest.Rules;

namespace Architecture.Tests;

public sealed class ArchitectureTests
{
    private static readonly Assembly FinanceDomain = typeof(Finance.Domain.Transactions.Transaction).Assembly;
    private static readonly Assembly FinanceApplication = typeof(Finance.Application.FinanceModule).Assembly;
    private static readonly Assembly Reporting = typeof(Reporting.Application.ReportingModule).Assembly;
    private static readonly Assembly Imports = typeof(Imports.Application.ImportsModule).Assembly;
    private static readonly Assembly SharedKernel = typeof(SharedKernel.Result).Assembly;
    private static readonly Assembly InvestmentsDomain = typeof(Investments.Domain.Position).Assembly;
    private static readonly Assembly InvestmentsApplication = typeof(Investments.Application.InvestmentsModule).Assembly;
    private static readonly Assembly IntegrationsApplication = typeof(Integrations.Application.IntegrationsModule).Assembly;
    private static readonly Assembly IntegrationsInfrastructure =
        typeof(Integrations.Infrastructure.IntegrationsInfrastructure).Assembly;

    private static readonly Assembly[] All =
    [
        SharedKernel, FinanceDomain, FinanceApplication, typeof(Finance.Infrastructure.FinanceInfrastructure).Assembly,
        Reporting, Imports, typeof(Program).Assembly, InvestmentsDomain, InvestmentsApplication,
        typeof(Investments.Infrastructure.InvestmentsInfrastructure).Assembly, IntegrationsApplication,
        IntegrationsInfrastructure, typeof(Ai.Application.Gateway.AiGateway).Assembly,
        typeof(Ai.Contracts.AiTools).Assembly,
    ];

    [Fact]
    public void Domain_has_no_framework_or_persistence_dependencies() =>
        Types.InAssembly(FinanceDomain).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql",
                "Finance.Application", "Finance.Infrastructure")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Investments_domain_has_no_framework_dependencies() =>
        Types.InAssembly(InvestmentsDomain).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "System.Net.Http",
                "Investments.Application", "Integrations")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Application_layers_do_not_depend_on_infrastructure() =>
        Types.InAssemblies([FinanceApplication, Reporting, Imports, InvestmentsApplication, IntegrationsApplication])
            .ShouldNot()
            .HaveDependencyOnAny("Npgsql", "Finance.Infrastructure", "Investments.Infrastructure",
                "Integrations.Infrastructure", "Host.Api")
            .GetResult().IsSuccessful.ShouldBeTrue();

    /// <summary>Broker wire formats (T212 JSON, IBKR XML) must not leak beyond the infrastructure layer.</summary>
    [Fact]
    public void Broker_wire_types_stay_internal() =>
        IntegrationsInfrastructure.GetTypes()
            .Where(t => t.Namespace?.EndsWith(".Trading212", StringComparison.Ordinal) == true ||
                        t.Namespace?.EndsWith(".Ibkr", StringComparison.Ordinal) == true)
            .Where(t => t.IsPublic || t.IsNestedPublic)
            .Select(t => t.FullName)
            .ShouldBeEmpty();

    /// <summary>The provider abstraction exposes reads only.</summary>
    [Fact]
    public void Investment_provider_contract_has_only_read_operations() =>
        typeof(Integrations.Application.Contracts.IInvestmentProvider).GetMethods()
            .Where(m => !m.IsSpecialName) // property getters such as Kind
            .Select(m => m.Name)
            .ShouldAllBe(name => name.StartsWith("Get", StringComparison.Ordinal));

    [Fact]
    public void Reporting_never_writes_to_the_ledger()
    {
        // Reports are read-only views: no SaveChanges calls anywhere in the module.
        var writes = Reporting.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.GetMethodBody() is not null)
            .Where(m => CallsSaveChanges(m))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        writes.ShouldBeEmpty();
    }

    /// <summary>
    /// The notification centre only reads: every module source is listed here and none of them (lambdas and async
    /// state machines included) calls SaveChanges. Each source lives in its own module, so the host composes them
    /// without any module reaching into another.
    /// </summary>
    [Fact]
    public void Notification_sources_are_read_only_and_owned_by_their_modules()
    {
        var sources = All.SelectMany(a => a.GetTypes())
            .Where(t => typeof(SharedKernel.Notifications.INotificationSource).IsAssignableFrom(t) && !t.IsInterface)
            .ToList();

        sources.Select(t => $"{t.Assembly.GetName().Name}:{t.Name}").ShouldBe(
        [
            "Finance.Application:FinanceNotificationSource",
            "Reporting.Application:BudgetNotificationSource",
            "Integrations.Application:ConnectionNotificationSource",
            "Ai.Application:AiNotificationSource",
        ], ignoreOrder: true);

        var writes = sources
            .SelectMany(t => t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Append(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.GetMethodBody() is not null && CallsSaveChanges(m))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        writes.ShouldBeEmpty();
    }

    [Fact]
    public void Scanner_detects_writes_where_they_exist()
    {
        // Positive control: the finance module does write, so the IL scanner must find it.
        FinanceApplication.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Any(m => m.GetMethodBody() is not null && CallsSaveChanges(m))
            .ShouldBeTrue();
    }

    /// <summary>
    /// The system reads brokers; it never trades. No operation that could place an order or move money
    /// may exist anywhere in the codebase.
    /// </summary>
    [Fact]
    public void No_trading_or_money_movement_operations_exist()
    {
        string[] forbidden = ["PlaceOrder", "Buy", "Sell", "Withdraw", "Deposit", "CancelOrder", "ModifyOrder",
            "SubmitOrder", "Trade", "Transfer"];
        var offenders = All.SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => forbidden.Any(f => m.Name.StartsWith(f, StringComparison.Ordinal) ||
                                           m.Name.Contains("<" + f, StringComparison.Ordinal)))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void No_raw_sql_is_executed_from_application_code()
    {
        string[] rawSql = ["FromSqlRaw", "FromSqlInterpolated", "ExecuteSqlRaw", "ExecuteSqlInterpolated", "SqlQueryRaw"];
        var offenders = new[] { FinanceApplication, Reporting, Imports, typeof(Program).Assembly, InvestmentsApplication,
                IntegrationsApplication }
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.GetMethodBody() is not null && CallsAny(m, rawSql))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    private static bool CallsSaveChanges(MethodInfo method) => CallsAny(method, ["SaveChanges", "SaveChangesAsync"]);

    private static bool CallsAny(MethodInfo method, string[] names)
    {
        var il = method.GetMethodBody()!.GetILAsByteArray();
        if (il is null)
        {
            return false;
        }

        // Scan call/callvirt tokens and resolve the target method names.
        for (var i = 0; i < il.Length - 4; i++)
        {
            if (il[i] is not (0x28 or 0x6F))
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, i + 1);
            try
            {
                var target = method.Module.ResolveMethod(token, method.DeclaringType?.GetGenericArguments(),
                    method.IsGenericMethod ? method.GetGenericArguments() : null);
                if (target is not null && names.Contains(target.Name))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // Not a method token; keep scanning.
            }
        }

        return false;
    }
}
