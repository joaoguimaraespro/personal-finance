using FluentValidation;
using Finance.Application.Accounts;
using Finance.Application.Budgets;
using Finance.Application.Buckets;
using Finance.Application.Categories;
using Finance.Application.Goals;
using Finance.Application.Recurring;
using Finance.Application.Transactions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Application;

public static class FinanceModule
{
    public static IServiceCollection AddFinanceApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<TransactionRequestValidator>(includeInternalTypes: true);
        services.AddScoped<RecurringProposer>();
        return services;
    }

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapAccounts();
        api.MapCategories();
        api.MapBuckets();
        api.MapTransactions();
        api.MapRecurring();
        api.MapBudgets();
        api.MapGoals();
        return api;
    }
}
