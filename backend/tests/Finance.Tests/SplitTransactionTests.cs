using Finance.Domain.Categories;
using Finance.Domain.Recurring;
using Finance.Domain.Transactions;
using SharedKernel;

namespace Finance.Tests;

public sealed class SplitTransactionTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid Electricity = Guid.NewGuid();
    private static readonly Guid Gas = Guid.NewGuid();
    private static readonly Guid Water = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 9, 25);

    private static TransactionDraft Bill(decimal amount, params SplitLine[] lines) =>
        new(TransactionType.Expense, Day, amount, "EUR", Account, Splits: lines);

    [Fact]
    public void A_split_expense_has_no_single_category_and_lines_in_order()
    {
        var t = Transaction.Create(Bill(100m, new SplitLine(Electricity, 60m, "  power  ", ExpenseNature.Fixed),
            new SplitLine(Gas, 40m, null, ExpenseNature.Variable)), DataSource.Manual).Value;

        t.IsSplit.ShouldBeTrue();
        t.CategoryId.ShouldBeNull();
        t.Nature.ShouldBeNull(); // no explicit nature: each line keeps its own
        t.Splits.Select(s => (s.Position, s.CategoryId, s.OriginalAmount, s.BaseAmount, s.Nature)).ShouldBe(
        [
            (0, Electricity, 60m, 60m, (ExpenseNature?)ExpenseNature.Fixed),
            (1, Gas, 40m, 40m, (ExpenseNature?)ExpenseNature.Variable),
        ]);
        t.Splits[0].Note.ShouldBe("power");
    }

    [Fact]
    public void An_explicit_nature_applies_to_every_line()
    {
        var t = Transaction.Create(Bill(100m, new SplitLine(Electricity, 60m, Nature: ExpenseNature.Variable),
            new SplitLine(Gas, 40m)) with { Nature = ExpenseNature.Fixed }, DataSource.Manual).Value;

        t.Nature.ShouldBe(ExpenseNature.Fixed);
        t.Splits.ShouldAllBe(s => s.Nature == ExpenseNature.Fixed);
    }

    [Fact]
    public void Lines_without_a_resolved_nature_fall_back_to_variable_and_income_lines_have_none()
    {
        var expense = Transaction.Create(Bill(10m, new SplitLine(Electricity, 4m), new SplitLine(Gas, 6m)), DataSource.Json).Value;
        expense.Splits.ShouldAllBe(s => s.Nature == ExpenseNature.Variable);

        var income = Transaction.Create(new TransactionDraft(TransactionType.Income, Day, 10m, "EUR", Account,
            Nature: ExpenseNature.Fixed, Splits: [new SplitLine(Electricity, 4m), new SplitLine(Gas, 6m)]), DataSource.Manual).Value;
        income.Nature.ShouldBeNull();
        income.Splits.ShouldAllBe(s => s.Nature == null);
    }

    [Fact]
    public void Foreign_currency_lines_add_up_to_the_eur_amount_with_the_remainder_on_the_last_line()
    {
        // 3 × 33.3333 USD at 0.9123: each line rounds to 30.4100 (30.40996…); the total is 91.2300.
        var t = Transaction.Create(new TransactionDraft(TransactionType.Expense, Day, 100m, "USD", Account,
            FxRate: 0.9123m, Splits:
            [
                new SplitLine(Electricity, 33.3333m), new SplitLine(Gas, 33.3333m), new SplitLine(Water, 33.3334m),
            ]), DataSource.Manual).Value;

        t.BaseAmount.ShouldBe(91.23m);
        t.Splits[0].BaseAmount.ShouldBe(30.41m);
        t.Splits[1].BaseAmount.ShouldBe(30.41m);
        t.Splits[2].BaseAmount.ShouldBe(91.23m - 60.82m);
        t.Splits.Sum(s => s.BaseAmount).ShouldBe(t.BaseAmount);
        t.Splits.Sum(s => s.OriginalAmount).ShouldBe(t.OriginalAmount);
    }

    [Fact]
    public void Updating_replaces_the_lines_and_a_category_removes_the_split()
    {
        var t = Transaction.Create(Bill(100m, new SplitLine(Electricity, 60m), new SplitLine(Gas, 40m)), DataSource.Manual).Value;

        t.Update(Bill(120m, new SplitLine(Gas, 20m), new SplitLine(Water, 50m), new SplitLine(Electricity, 50m)))
            .IsSuccess.ShouldBeTrue();
        t.Splits.Select(s => s.CategoryId).ShouldBe([Gas, Water, Electricity]);

        t.Update(new TransactionDraft(TransactionType.Expense, Day, 120m, "EUR", Account, Electricity)).IsSuccess.ShouldBeTrue();
        t.IsSplit.ShouldBeFalse();
        t.CategoryId.ShouldBe(Electricity);
        t.Nature.ShouldBe(ExpenseNature.Variable);
    }

    public static TheoryData<TransactionDraft, string> Invalid => new()
    {
        { Bill(100m, new SplitLine(Electricity, 100m)), "2 to 20" },
        { Bill(100m, new SplitLine(Electricity, 60m), new SplitLine(Gas, 39.99m)), "add up to 99.99" },
        { Bill(100m, new SplitLine(Electricity, 100m), new SplitLine(Gas, 0m)), "greater than zero" },
        { Bill(100m, new SplitLine(Electricity, 50.00001m), new SplitLine(Gas, 49.99999m)), "4 decimals" },
        { Bill(100m, new SplitLine(Electricity, 60m), new SplitLine(Electricity, 40m)), "only once" },
        { Bill(100m, new SplitLine(Electricity, 60m, new string('x', 121)), new SplitLine(Gas, 40m)), "120" },
        { Bill(100m, new SplitLine(Electricity, 60m), new SplitLine(Gas, 40m)) with { CategoryId = Water }, "not both" },
        {
            new TransactionDraft(TransactionType.Transfer, Day, 100m, "EUR", Account, CounterAccountId: Guid.NewGuid(),
                Splits: [new SplitLine(Electricity, 60m), new SplitLine(Gas, 40m)]),
            "Only expenses and income"
        },
        {
            new TransactionDraft(TransactionType.InvestmentContribution, Day, 100m, "EUR", Account, BucketId: Guid.NewGuid(),
                Splits: [new SplitLine(Electricity, 60m), new SplitLine(Gas, 40m)]),
            "Only expenses and income"
        },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Invalid_splits_are_refused(TransactionDraft draft, string message)
    {
        var result = Transaction.Create(draft, DataSource.Manual);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Transaction.Splits");
        result.Error.Description.ShouldContain(message);
    }

    [Theory]
    [InlineData(DataSource.Trading212)]
    [InlineData(DataSource.InteractiveBrokers)]
    [InlineData(DataSource.InterestEstimate)]
    public void Broker_rows_and_estimated_interest_cannot_be_split(DataSource source)
    {
        var result = Transaction.Create(Bill(100m, new SplitLine(Electricity, 60m), new SplitLine(Gas, 40m)), source);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SplitRules.NotAllowedForSource);
    }

    [Fact]
    public void A_recurring_template_carries_its_lines_rescaled_to_the_confirmed_amount()
    {
        var template = RecurringTransaction.Create(new RecurringDefinition("Energy", TransactionType.Expense, 90m, "EUR",
            Account, RecurrenceFrequency.Monthly, Day, Splits: [new SplitLine(Electricity, 60m, "power"), new SplitLine(Gas, 30m)]));

        template.CategoryId.ShouldBeNull();
        template.Nature.ShouldBeNull();
        template.ToDraft(Day).Splits!.Select(l => l.Amount).ShouldBe([60m, 30m]);

        var adjusted = template.ToDraft(Day, 100m);
        adjusted.Amount.ShouldBe(100m);
        adjusted.Splits!.Select(l => (l.CategoryId, l.Amount)).ShouldBe([(Electricity, 66.67m), (Gas, 33.33m)]);
        adjusted.Splits![0].Note.ShouldBe("power");
        Transaction.Create(adjusted, DataSource.Recurring).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Rescaling_keeps_the_exact_total()
    {
        var lines = SplitRules.Rescale([new SplitLine(Electricity, 1m), new SplitLine(Gas, 1m), new SplitLine(Water, 1m)], 3m, 10m);

        lines.Select(l => l.Amount).ShouldBe([3.33m, 3.33m, 3.34m]);
    }
}
