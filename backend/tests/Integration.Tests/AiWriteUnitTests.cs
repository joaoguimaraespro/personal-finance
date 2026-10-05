using System.Text.Json;
using Ai.Application.Gateway;
using Ai.Application.Tools;
using Finance.Domain.Goals;
using Finance.Domain.Transactions;
using SharedKernel;

namespace Integration.Tests;

/// <summary>Database-free checks of the write path's building blocks: argument parsing, the write budget, policy.</summary>
public sealed class AiWriteUnitTests
{
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ToolArgs Args(object args, params string[] declared) =>
        new(JsonSerializer.SerializeToElement(args), declared);

    [Fact]
    public void Amounts_are_bounded_numbers_with_limited_decimals()
    {
        Args(new { amount = 12.34m }, "amount").Decimal("amount", 0.01m, 100m, 2).ShouldBe(12.34m);
        Args(new { amount = "12.5" }, "amount").Decimal("amount", 0.01m, 100m, 2).ShouldBe(12.5m);
        Args(new { }, "amount").Decimal("amount", 0.01m, 100m, 2).ShouldBeNull();
        Should.Throw<ToolArgumentException>(() => Args(new { amount = 1.234m }, "amount").Decimal("amount", 0m, 100m, 2));
        Should.Throw<ToolArgumentException>(() => Args(new { amount = 0m }, "amount").Decimal("amount", 0.01m, 100m, 2));
        Should.Throw<ToolArgumentException>(() => Args(new { amount = 101m }, "amount").Decimal("amount", 0m, 100m, 2));
        Should.Throw<ToolArgumentException>(() => Args(new { amount = "1e3" }, "amount").Decimal("amount", 0m, 10_000m, 2));
        Should.Throw<ToolArgumentException>(() => Args(new { amount = true }, "amount").Decimal("amount", 0m, 100m, 2));
    }

    [Fact]
    public void Text_is_length_capped_and_refuses_invisible_or_control_characters()
    {
        Args(new { description = "  Lunch at Zé's  " }, "description").Text("description", 120).ShouldBe("Lunch at Zé's");
        Should.Throw<ToolArgumentException>(() => Args(new { description = new string('a', 121) }, "description").Text("description", 120));
        Should.Throw<ToolArgumentException>(() => Args(new { description = "   " }, "description").Text("description", 120));
        foreach (var hidden in new[] { "a\u0000b", "a\nb", "a​b", "a‮b", "a⁦b", "a﻿b" })
        {
            Should.Throw<ToolArgumentException>(() => Args(new { description = hidden }, "description").Text("description", 120));
        }

        Should.Throw<ToolArgumentException>(() => Args(new { description = 42 }, "description").Text("description", 120));
    }

    [Fact]
    public void Ids_months_and_idempotency_keys_are_strict()
    {
        var id = Guid.NewGuid();
        Args(new { transaction_id = id.ToString() }, "transaction_id").Id("transaction_id").ShouldBe(id);
        Should.Throw<ToolArgumentException>(() => Args(new { transaction_id = "1; drop table" }, "transaction_id").Id("transaction_id"));
        Should.Throw<ToolArgumentException>(() => Args(new { transaction_id = Guid.Empty.ToString() }, "transaction_id").Id("transaction_id"));
        Args(new { from_period = "2047-05" }, "from_period").Month("from_period").ShouldBe(new YearMonth(2047, 5));
        Should.Throw<ToolArgumentException>(() => Args(new { from_period = "May" }, "from_period").Month("from_period"));
        Args(new { idempotency_key = "retry_0001-ABC" }, "idempotency_key").IdempotencyKey().ShouldBe("retry_0001-ABC");
        Should.Throw<ToolArgumentException>(() => Args(new { idempotency_key = "short" }, "idempotency_key").IdempotencyKey());
        Should.Throw<ToolArgumentException>(() => Args(new { idempotency_key = "has spaces in it" }, "idempotency_key").IdempotencyKey());
        Should.Throw<ToolArgumentException>(() => Args(new { idempotency_key = new string('k', 65) }, "idempotency_key").IdempotencyKey());

        var args = Args(new { amount = 5m, idempotency_key = "retry_0001-ABC" }, "amount", "idempotency_key");
        args.Decimal("amount", 0m, 10m, 2);
        args.IdempotencyKey();
        args.UsedWithoutKey.Keys.ShouldBe(["amount"]);
    }

    [Fact]
    public void The_write_budget_is_per_client_over_a_rolling_hour()
    {
        var clock = new ManualClock(new DateTimeOffset(2041, 4, 10, 12, 0, 0, TimeSpan.Zero));
        var limiter = new AiWriteLimiter(clock);
        var client = Guid.NewGuid();

        Enumerable.Range(0, 20).Select(_ => limiter.TryAcquire(client, 20)).ToList().ShouldAllBe(ok => ok);
        limiter.TryAcquire(client, 20).ShouldBeFalse();
        limiter.TryAcquire(Guid.NewGuid(), 20).ShouldBeTrue(); // other clients have their own budget

        clock.Now = clock.Now.AddMinutes(30);
        limiter.TryAcquire(client, 20).ShouldBeFalse();
        clock.Now = clock.Now.AddMinutes(29);
        limiter.TryAcquire(client, 20).ShouldBeFalse();
        clock.Now = clock.Now.AddMinutes(1); // an hour after the first writes, their slots free up
        Enumerable.Range(0, 20).Select(_ => limiter.TryAcquire(client, 20)).ToList().ShouldAllBe(ok => ok);
        limiter.TryAcquire(client, 20).ShouldBeFalse();
    }

    [Fact]
    public void Only_hand_entered_expenses_income_and_transfers_are_writable()
    {
        AiWritePolicy.IsEditable(DataSource.Manual, TransactionType.Expense).ShouldBeTrue();
        AiWritePolicy.IsEditable(DataSource.Recurring, TransactionType.Income).ShouldBeTrue();
        AiWritePolicy.IsEditable(DataSource.Csv, TransactionType.Transfer).ShouldBeTrue();
        foreach (var source in new[] { DataSource.Trading212, DataSource.InteractiveBrokers, DataSource.Demo,
                     DataSource.MarketData, DataSource.InterestEstimate })
        {
            AiWritePolicy.IsEditable(source, TransactionType.Expense).ShouldBeFalse(source.ToString());
            AiWritePolicy.RefusalFor(source, TransactionType.Expense).ShouldNotBeNull();
        }

        foreach (var type in new[] { TransactionType.Savings, TransactionType.InvestmentContribution, TransactionType.InvestmentSale })
        {
            AiWritePolicy.IsEditable(DataSource.Manual, type).ShouldBeFalse(type.ToString());
            AiWritePolicy.RefusalFor(DataSource.Manual, type).ShouldNotBeNull();
        }

        AiWritePolicy.RefusalFor(DataSource.Manual, TransactionType.Expense).ShouldBeNull();
    }

    [Fact]
    public void Adding_to_a_goal_raises_the_tracked_amount()
    {
        var byHand = FinancialGoal.Create("Car", 5000m, null, 0m, manualCurrentAmount: 1000m, null);
        byHand.AddToCurrent(250m);
        byHand.ManualCurrentAmount.ShouldBe(1250m);
        byHand.CurrentAmount(400m).ShouldBe(1250m);

        var linked = FinancialGoal.Create("Trip", 2000m, null, 100m, null, null);
        linked.AddToCurrent(50m);
        linked.StartingAmount.ShouldBe(150m);
        linked.CurrentAmount(30m).ShouldBe(180m);
    }
}
