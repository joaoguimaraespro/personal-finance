using System.Globalization;
using Ai.Application.Domain;
using Ai.Application.Gateway;
using Ai.Contracts;
using FluentValidation;
using Finance.Application.Abstractions;
using Finance.Application.Budgets;
using Finance.Application.Goals;
using Finance.Application.Interest;
using Finance.Application.Recurring;
using Finance.Application.Transactions;
using Finance.Domain.Accounts;
using Finance.Domain.Budgets;
using Finance.Domain.Categories;
using Finance.Domain.Recurring;
using Finance.Domain.Transactions;
using Investments.Application;
using Investments.Application.Abstractions;
using Investments.Application.Fx;
using Investments.Application.Manual;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Ai.Application.Tools;

public sealed record WriteContext(AiClient Client, ToolArgs Args, DateOnly Today, DateTimeOffset Now);

/// <param name="Data">What the model receives: the record id, a short summary and nothing else of note.</param>
/// <param name="Before">Audit only: the changed record before (null when created).</param>
/// <param name="After">Audit only: the record after (null when deleted).</param>
public sealed record WriteOutput(object Data, Guid RecordId, object? Before, object? After);

/// <summary>
/// The AI write tools (ADR-0008). Only <see cref="AiGateway"/> calls them, after authentication, the write scope,
/// both rate limits and idempotency. Each changes exactly one record through the application's own commands and
/// validators — the same rules, audit trail and recalculations as the web app — and refuses broker-sourced,
/// investment, estimated-interest and archived records. Every id and text argument is treated as data.
/// </summary>
public sealed class WriteTools(
    IFinanceDb finance,
    IInvestmentsDb investments,
    IAiDb ai,
    InterestAccrualService interest,
    RecurringProposer proposer,
    ManualHoldingService holdings,
    FxRates fx,
    IValidator<TransactionRequest> transactionValidator,
    IValidator<RecurringRequest> recurringValidator,
    IValidator<GoalRequest> goalValidator,
    IValidator<UpdateManualHoldingRequest> holdingValidator,
    IValidator<RewardRequest> rewardValidator)
{
    private const decimal MaxAmount = 100_000_000m;

    public Task<WriteOutput> RunAsync(string tool, WriteContext ctx, CancellationToken ct) => tool switch
    {
        "create_transaction" => CreateTransactionAsync(ctx, ct),
        "update_transaction" => UpdateTransactionAsync(ctx, ct),
        "delete_transaction" => DeleteTransactionAsync(ctx, ct),
        "confirm_expected" => ConfirmExpectedAsync(ctx, ct),
        "skip_expected" => SkipExpectedAsync(ctx, ct),
        "upsert_recurring" => UpsertRecurringAsync(ctx, ct),
        "set_budget_limit" => SetBudgetLimitAsync(ctx, ct),
        "upsert_goal" => UpsertGoalAsync(ctx, ct),
        "add_to_goal" => AddToGoalAsync(ctx, ct),
        "update_crypto_holding" => UpdateCryptoHoldingAsync(ctx, ct),
        "add_crypto_reward" => AddCryptoRewardAsync(ctx, ct),
        "update_asset_value" => UpdateAssetValueAsync(ctx, ct),
        _ => throw new ToolArgumentException("Unknown tool."),
    };

    // ---------------------------------------------------------------- transactions

    private async Task<WriteOutput> CreateTransactionAsync(WriteContext ctx, CancellationToken ct)
    {
        var a = ctx.Args;
        var type = TransactionTypeOf(Required("type", a.OneOf("type", AiTools.WritableTransactionTypes)));
        var date = ToolArgs.Required("date", a.Date("date"));
        var amount = ToolArgs.Required("amount", a.Decimal("amount", 0.01m, MaxAmount, 2));
        var accountId = ToolArgs.Required("account_id", a.Id("account_id"));
        var categoryText = a.Category();
        var toAccountId = a.Id("to_account_id");
        var description = a.Text("description", 120);
        var splitsText = a.Splits(MaxAmount);
        a.IdempotencyKey();

        Guid? categoryId = null;
        string? categoryName = null;
        List<SplitLineRequest>? splits = null;
        if (type == TransactionType.Transfer)
        {
            if (toAccountId is null)
            {
                throw new ToolArgumentException("to_account_id is required for a transfer.");
            }

            if (categoryText is not null || splitsText is not null)
            {
                throw new ToolArgumentException("Transfers have no category and cannot be split.");
            }
        }
        else
        {
            if (toAccountId is not null)
            {
                throw new ToolArgumentException("to_account_id is only for transfers.");
            }

            var categoryType = type == TransactionType.Expense ? CategoryType.Expense : CategoryType.Income;
            if (splitsText is not null)
            {
                if (categoryText is not null)
                {
                    throw new ToolArgumentException("Give either category or splits, not both.");
                }

                splits = await SplitsAsync(splitsText, categoryType, amount, ct);
                categoryName = $"{splits.Count} categories";
            }
            else
            {
                (categoryId, categoryName) = await CategoryAsync(Required("category", categoryText), categoryType, ct);
            }
        }

        var account = await WritableAccountAsync(accountId, "account_id", ct);
        if (toAccountId is { } to)
        {
            await WritableAccountAsync(to, "to_account_id", ct);
        }

        var fxRate = await FxRateAsync(account.Currency, date, ct);
        var request = new TransactionRequest(type, date, amount, account.Currency, accountId, categoryId, null,
            toAccountId, null, null, fxRate, description, null, Splits: splits);
        await CheckAsync(transactionValidator, request, ct);

        var created = Ok(await TransactionCommands.CreateAsync(finance, interest, request.ToDraft(), DataSource.Manual, ct));
        var after = TransactionSnapshot(created, categoryName);
        return new(Result(created.Id, "transaction",
            $"Created {TypeName(type)} of {Money(amount, account.Currency)} on {Day(date)}{(categoryName is null ? "" : $" in {categoryName}")}.",
            new { description = UntrustedText.From(description) }), created.Id, null, after);
    }

    private async Task<WriteOutput> UpdateTransactionAsync(WriteContext ctx, CancellationToken ct)
    {
        var a = ctx.Args;
        var id = ToolArgs.Required("transaction_id", a.Id("transaction_id"));
        var date = a.Date("date");
        var amount = a.Decimal("amount", 0.01m, MaxAmount, 2);
        var categoryText = a.Category();
        var accountId = a.Id("account_id");
        var toAccountId = a.Id("to_account_id");
        var description = a.Text("description", 120);
        var splitsText = a.Splits(MaxAmount);
        a.IdempotencyKey();
        if (date is null && amount is null && categoryText is null && accountId is null && toAccountId is null &&
            description is null && splitsText is null)
        {
            throw new ToolArgumentException("Give at least one field to change.");
        }

        var current = await EditableTransactionAsync(id, ct);
        var before = TransactionSnapshot(current, await CategoryNameAsync(current.CategoryId, ct));

        var categoryId = current.CategoryId;
        var nature = current.Nature;
        if (categoryText is not null)
        {
            if (current.Type == TransactionType.Transfer)
            {
                throw new ToolArgumentException("Transfers have no category.");
            }

            (categoryId, _) = await CategoryAsync(categoryText,
                current.Type == TransactionType.Expense ? CategoryType.Expense : CategoryType.Income, ct);
            nature = categoryId == current.CategoryId ? nature : null; // a new category brings its default nature
        }

        // Split lines: new ones replace the old; a single category removes the split; otherwise they are kept, which
        // only works while the amount stays the same (the lines must add up to it).
        var newAmount = amount ?? current.OriginalAmount;
        List<SplitLineRequest>? splits = null;
        if (splitsText is not null)
        {
            if (current.Type == TransactionType.Transfer)
            {
                throw new ToolArgumentException("Transfers cannot be split.");
            }

            if (categoryText is not null)
            {
                throw new ToolArgumentException("Give either category or splits, not both.");
            }

            splits = await SplitsAsync(splitsText,
                current.Type == TransactionType.Expense ? CategoryType.Expense : CategoryType.Income, newAmount, ct);
            categoryId = null;
        }
        else if (categoryText is null && current.IsSplit)
        {
            if (newAmount != current.OriginalAmount)
            {
                throw new ToolArgumentException(
                    "This transaction is split by category: give splits that add up to the new amount (or one category).");
            }

            splits = current.Splits.OrderBy(sp => sp.Position)
                .Select(sp => new SplitLineRequest(sp.CategoryId, sp.OriginalAmount, sp.Note)).ToList();
        }

        if (toAccountId is not null && current.Type != TransactionType.Transfer)
        {
            throw new ToolArgumentException("to_account_id is only for transfers.");
        }

        var account = await WritableAccountAsync(accountId ?? current.AccountId, "account_id", ct);
        if (toAccountId is { } to)
        {
            await WritableAccountAsync(to, "to_account_id", ct);
        }

        var newDate = date ?? current.OccurredOn;
        var sameRate = account.Currency == current.OriginalCurrency && newDate == current.OccurredOn;
        var fxRate = account.Currency == Currency.Base ? null
            : sameRate ? current.FxRate : await FxRateAsync(account.Currency, newDate, ct);
        var request = new TransactionRequest(current.Type, newDate, newAmount, account.Currency, account.Id, categoryId,
            nature, toAccountId ?? current.CounterAccountId, null, null, fxRate, description ?? current.Description,
            current.Notes, Splits: splits);
        await CheckAsync(transactionValidator, request, ct);

        var draft = request.ToDraft() with { OccurredAtUtc = date is null ? current.OccurredAtUtc : null, TimeZone = current.TimeZone };
        var updated = Ok(await TransactionCommands.UpdateAsync(finance, interest, id, draft, ct));
        var after = TransactionSnapshot(updated, await CategoryNameAsync(updated.CategoryId, ct));
        return new(Result(id, "transaction",
            $"Updated the {TypeName(updated.Type)} of {Day(updated.OccurredOn)}: now {Money(updated.OriginalAmount, updated.OriginalCurrency)}.",
            new { description = UntrustedText.From(updated.Description) }), id, before, after);
    }

    private async Task<WriteOutput> DeleteTransactionAsync(WriteContext ctx, CancellationToken ct)
    {
        var id = ToolArgs.Required("transaction_id", ctx.Args.Id("transaction_id"));
        ctx.Args.IdempotencyKey();
        var current = await EditableTransactionAsync(id, ct);
        var categoryName = await CategoryNameAsync(current.CategoryId, ct);
        var before = TransactionSnapshot(current, categoryName);

        Ok(await TransactionCommands.DeleteAsync(finance, interest, id, ctx.Now, ct));
        var summary = string.Join(" · ", new[]
        {
            TypeName(current.Type), Day(current.OccurredOn), Money(current.OriginalAmount, current.OriginalCurrency),
            categoryName, current.Description,
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var item = AiRecycledItem.Create(AiRecycledItem.TransactionKind, id, ctx.Client, summary, ctx.Now);
        ai.RecycleBin.Add(item);
        await ai.SaveChangesAsync(CancellationToken.None);
        return new(Result(id, "transaction",
            $"Moved the {TypeName(current.Type)} of {Day(current.OccurredOn)} ({Money(current.OriginalAmount, current.OriginalCurrency)}) to the recycle bin. " +
            $"The owner can restore it in the app until {Day(DateOnly.FromDateTime(item.PurgeAfterUtc.UtcDateTime))}; then it is deleted for good."),
            id, before, null);
    }

    // ---------------------------------------------------------------- recurring

    private async Task<WriteOutput> ConfirmExpectedAsync(WriteContext ctx, CancellationToken ct)
    {
        var a = ctx.Args;
        var id = ToolArgs.Required("expected_id", a.Id("expected_id"));
        var amount = a.Decimal("amount", 0.01m, MaxAmount, 2);
        var date = a.Date("date");
        a.IdempotencyKey();

        var (item, template) = await PendingAsync(id, ct);
        await WritableAccountAsync(template.AccountId, "the recurring item's account", ct);
        var on = date ?? item.DueOn;
        var fxRate = await FxRateAsync(template.Currency, on, ct);
        var transaction = Ok(await RecurringCommands.ConfirmAsync(finance, id,
            new ConfirmExpectedRequest(amount, date, null, fxRate, null), ctx.Now, ct));
        return new(Result(transaction.Id, "transaction",
            $"Confirmed the recurring item due {Day(item.DueOn)}: {TypeName(transaction.Type)} of {Money(transaction.OriginalAmount, transaction.OriginalCurrency)} on {Day(transaction.OccurredOn)}.",
            new { name = UntrustedText.From(template.Name), expectedId = id }),
            transaction.Id,
            new { expectedId = id, status = "pending", dueOn = item.DueOn, amount = item.Amount },
            new { expectedId = id, status = "confirmed", transactionId = transaction.Id, date = transaction.OccurredOn, amount = transaction.OriginalAmount });
    }

    private async Task<WriteOutput> SkipExpectedAsync(WriteContext ctx, CancellationToken ct)
    {
        var id = ToolArgs.Required("expected_id", ctx.Args.Id("expected_id"));
        ctx.Args.IdempotencyKey();
        var (item, template) = await PendingAsync(id, ct);
        Ok(await RecurringCommands.SkipAsync(finance, id, ctx.Now, ct));
        return new(Result(id, "expected",
            $"Skipped the recurring item due {Day(item.DueOn)} ({Money(item.Amount, item.Currency)}). No transaction was created.",
            new { name = UntrustedText.From(template.Name) }),
            id, new { status = "pending", dueOn = item.DueOn, amount = item.Amount }, new { status = "skipped" });
    }

    private async Task<WriteOutput> UpsertRecurringAsync(WriteContext ctx, CancellationToken ct)
    {
        var a = ctx.Args;
        var id = a.Id("recurring_id");
        var name = a.Text("name", 80);
        var typeText = a.OneOf("type", "expense", "income");
        var amount = a.Decimal("amount", 0.01m, MaxAmount, 2);
        var frequencyText = a.OneOf("frequency", "daily", "weekly", "monthly", "yearly");
        var accountId = a.Id("account_id");
        var categoryText = a.Category();
        var startOn = a.Date("start_on");
        var dayOfMonth = a.Integer("day_of_month", 1, 31);
        var interval = a.Integer("interval", 1, RecurringTransaction.MaxDailyInterval);
        var endOn = a.Date("end_on");
        a.IdempotencyKey();

        RecurringTransaction? existing = null;
        if (id is { } existingId)
        {
            existing = await finance.RecurringTransactions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == existingId, ct)
                       ?? throw new ToolRefusedException(404, "Recurring item not found.");
            if (existing.Type is not (TransactionType.Expense or TransactionType.Income))
            {
                throw new ToolRefusedException(403, "Only recurring expenses and income can be changed by AI.");
            }
        }

        var type = typeText is null ? existing?.Type : TransactionTypeOf(typeText);
        if (type is null)
        {
            throw new ToolArgumentException("type is required to create a recurring item.");
        }

        Guid? categoryId;
        List<SplitLineRequest>? keptSplits = null;
        if (existing is { Splits.Count: > 0 } && categoryText is null && (type == existing.Type))
        {
            // A split recurring item keeps its lines; they must still add up, so its amount is changed in the app.
            if (amount is not null && amount != existing.Amount)
            {
                throw new ToolArgumentException("This recurring item is split by category; change its amount in the app, or give one category.");
            }

            categoryId = null;
            keptSplits = existing.Splits.OrderBy(sp => sp.Position)
                .Select(sp => new SplitLineRequest(sp.CategoryId, sp.Amount, sp.Note)).ToList();
        }
        else if (categoryText is not null)
        {
            (categoryId, _) = await CategoryAsync(categoryText,
                type == TransactionType.Expense ? CategoryType.Expense : CategoryType.Income, ct);
        }
        else if (existing?.CategoryId is { } kept && existing.Type == type)
        {
            categoryId = kept;
        }
        else
        {
            throw new ToolArgumentException(existing is null
                ? "category is required to create a recurring item."
                : "category is required when the type changes.");
        }

        var account = await WritableAccountAsync(
            accountId ?? existing?.AccountId ?? throw new ToolArgumentException("account_id is required to create a recurring item."),
            "account_id", ct);
        var frequency = frequencyText is null
            ? existing?.Frequency ?? throw new ToolArgumentException("frequency is required to create a recurring item.")
            : Enum.Parse<RecurrenceFrequency>(frequencyText, ignoreCase: true);
        if (frequency == RecurrenceFrequency.Daily && dayOfMonth is not null)
        {
            throw new ToolArgumentException("day_of_month does not apply to daily items: they repeat every interval days from start_on.");
        }

        var nature = existing is not null && existing.Type == type && existing.CategoryId == categoryId ? existing.Nature : null;
        var request = new RecurringRequest(
            name ?? existing?.Name ?? throw new ToolArgumentException("name is required to create a recurring item."),
            type.Value,
            amount ?? existing?.Amount ?? throw new ToolArgumentException("amount is required to create a recurring item."),
            account.Currency, account.Id, frequency, startOn ?? existing?.StartOn ?? ctx.Today,
            interval ?? existing?.Interval ?? 1,
            // A stored day of month means nothing for daily items (and is cleared when switching to daily).
            frequency == RecurrenceFrequency.Daily ? null : dayOfMonth ?? existing?.DayOfMonth, endOn ?? existing?.EndOn,
            categoryId, nature, null, null, existing?.Description, keptSplits);
        await CheckAsync(recurringValidator, request, ct);

        var categoryName = keptSplits is null ? await CategoryNameAsync(categoryId, ct) : $"{keptSplits.Count} categories";
        var saved = existing is null
            ? Ok(await RecurringCommands.CreateAsync(finance, proposer, request.ToDefinition(), ct))
            : Ok(await RecurringCommands.UpdateAsync(finance, proposer, existing.Id, request.ToDefinition(), ct));
        var verb = existing is null ? "Created" : "Updated";
        return new(Result(saved.Id, "recurring",
            $"{verb} the recurring {TypeName(saved.Type)} of {Money(saved.Amount, saved.Currency)} ({ScheduleText(saved)}) in {categoryName}; next due {Day(saved.NextOccurrenceOnOrAfter(ctx.Today) ?? saved.NextDueOn)}.",
            new { name = UntrustedText.From(saved.Name) }),
            saved.Id, existing is null ? null : RecurringSnapshot(existing, await CategoryNameAsync(existing.CategoryId, ct)),
            RecurringSnapshot(saved, categoryName));
    }

    // ---------------------------------------------------------------- budgets and goals

    private async Task<WriteOutput> SetBudgetLimitAsync(WriteContext ctx, CancellationToken ct)
    {
        var a = ctx.Args;
        var (categoryId, categoryName) = await CategoryAsync(Required("category", a.Category()), CategoryType.Expense, ct);
        var amount = ToolArgs.Required("amount", a.Decimal("amount", 0m, 10_000_000m, 2));
        var current = YearMonth.From(ctx.Today);
        var from = a.Month("from_period") ?? current;
        a.IdempotencyKey();
        if (from < current)
        {
            throw new ToolArgumentException("Budgets can be changed from the current month onwards; past months keep theirs.");
        }

        var effective = await BudgetEndpoints.FindEffectiveAsync(finance, from, ct);
        var specs = effective?.Items.Select(i => new BudgetItemSpec(i.Target, i.Mode, i.Value, i.BucketId, i.CategoryId))
            .ToList() ?? [];
        var old = specs.FirstOrDefault(s => s.Target == BudgetTarget.Category && s.CategoryId == categoryId);
        if (old is null && amount == 0)
        {
            throw new ToolRefusedException(409, $"{categoryName} has no budget limit from {from} to remove.");
        }

        specs.RemoveAll(s => s.Target == BudgetTarget.Category && s.CategoryId == categoryId);
        if (amount > 0)
        {
            specs.Add(new BudgetItemSpec(BudgetTarget.Category, BudgetMode.FixedAmount, amount, null, categoryId));
        }

        Ok(await BudgetEndpoints.SaveAsync(finance, from, specs, effective?.Note, ct));
        var budgetId = await finance.Budgets.AsNoTracking().Where(b => b.EffectiveFrom == from.FirstDay)
            .Select(b => b.Id).FirstAsync(ct);
        object? Limit(BudgetItemSpec? s) => s is null ? null
            : s.Mode == BudgetMode.FixedAmount ? new { amountEur = s.Value }
            : new { percentOfIncome = s.Value };
        return new(Result(budgetId, "budget",
            amount == 0
                ? $"Removed the {categoryName} budget limit from {from} onwards."
                : $"Set the {categoryName} budget to {Money(amount, Currency.Base)} a month from {from} onwards."),
            budgetId,
            new { category = categoryName, from = from.ToString(), limit = Limit(old) },
            new { category = categoryName, from = from.ToString(), limit = amount == 0 ? null : new { amountEur = amount } });
    }

    private async Task<WriteOutput> UpsertGoalAsync(WriteContext ctx, CancellationToken ct)
    {
        var a = ctx.Args;
        var id = a.Id("goal_id");
        var name = a.Text("name", 80);
        var target = a.Decimal("target_amount", 0.01m, 1_000_000_000m, 2);
        var targetDate = a.Date("target_date");
        a.IdempotencyKey();

        var existing = id is { } goalId ? await ActiveGoalAsync(goalId, ct) : null;
        if (existing is not null && name is null && target is null && targetDate is null)
        {
            throw new ToolArgumentException("Give at least one field to change.");
        }

        var request = new GoalRequest(
            name ?? existing?.Name ?? throw new ToolArgumentException("name is required to create a goal."),
            target ?? existing?.TargetAmount ?? throw new ToolArgumentException("target_amount is required to create a goal."),
            targetDate ?? existing?.TargetDate, existing?.StartingAmount, existing?.ManualCurrentAmount, existing?.Icon,
            existing?.AccountId);
        await CheckAsync(goalValidator, request, ct);

        var saved = existing is null
            ? await GoalEndpoints.CreateAsync(finance, request, ct)
            : Ok(await GoalEndpoints.UpdateAsync(finance, existing.Id, request, ct));
        return new(Result(saved.Id, "goal",
            $"{(existing is null ? "Created" : "Updated")} the goal: target {Money(saved.TargetAmount, Currency.Base)}{(saved.TargetDate is { } d ? $" by {Day(d)}" : "")}.",
            new { name = UntrustedText.From(saved.Name) }),
            saved.Id,
            existing is null ? null : new { name = existing.Name, targetAmount = existing.TargetAmount, targetDate = existing.TargetDate },
            new { name = saved.Name, targetAmount = saved.TargetAmount, targetDate = saved.TargetDate });
    }

    private async Task<WriteOutput> AddToGoalAsync(WriteContext ctx, CancellationToken ct)
    {
        var id = ToolArgs.Required("goal_id", ctx.Args.Id("goal_id"));
        var amount = ToolArgs.Required("amount", ctx.Args.Decimal("amount", 0.01m, 1_000_000_000m, 2));
        ctx.Args.IdempotencyKey();
        await ActiveGoalAsync(id, ct);

        var goal = await finance.Goals.FirstAsync(g => g.Id == id, ct);
        if (goal.AccountId is not null)
        {
            throw new ToolArgumentException(
                "This goal follows an account's balance; record a savings transaction into that account instead.");
        }

        var before = new { startingAmount = goal.StartingAmount, manualCurrentAmount = goal.ManualCurrentAmount };
        goal.AddToCurrent(amount);
        await finance.SaveChangesAsync(ct);
        var progress = (await GoalEndpoints.ListAsync(finance, ctx.Today, false, ct)).First(g => g.Id == id);
        return new(Result(id, "goal",
            $"Added {Money(amount, Currency.Base)} to the goal: now {Money(progress.CurrentAmount, Currency.Base)} of {Money(progress.TargetAmount, Currency.Base)}.",
            new { name = UntrustedText.From(goal.Name) }),
            id, before, new { startingAmount = goal.StartingAmount, manualCurrentAmount = goal.ManualCurrentAmount });
    }

    // ---------------------------------------------------------------- hand-entered crypto and manual assets

    private async Task<WriteOutput> UpdateCryptoHoldingAsync(WriteContext ctx, CancellationToken ct)
    {
        var a = ctx.Args;
        var id = ToolArgs.Required("holding_id", a.Id("holding_id"));
        var quantity = a.Decimal("quantity", 0.0000000001m, 999_999_999_999m, 10);
        var price = a.Decimal("average_price", 0m, 99_999_999m, 8);
        a.IdempotencyKey();
        if (quantity is null && price is null)
        {
            throw new ToolArgumentException("Give quantity, average_price or both.");
        }

        var (holding, location) = await HoldingAsync(id, ct);
        var request = new UpdateManualHoldingRequest(quantity ?? holding.Quantity, price ?? holding.AveragePrice,
            location.Name, holding.Notes, holding.HeldSince);
        await CheckAsync(holdingValidator, request, ct);
        Ok(await holdings.UpdateAsync(id, request, ct));
        return new(Result(id, "crypto_holding",
            $"Updated the holding: {request.Quantity.ToString(CultureInfo.InvariantCulture)} coins at an average {Money(request.AveragePrice, Currency.Base)}."),
            id, new { quantity = holding.Quantity, averagePrice = holding.AveragePrice },
            new { quantity = request.Quantity, averagePrice = request.AveragePrice });
    }

    private async Task<WriteOutput> AddCryptoRewardAsync(WriteContext ctx, CancellationToken ct)
    {
        var a = ctx.Args;
        var id = ToolArgs.Required("holding_id", a.Id("holding_id"));
        var quantity = ToolArgs.Required("quantity", a.Decimal("quantity", 0.0000000001m, 999_999_999_999m, 10));
        var kind = Enum.Parse<RewardKind>(a.OneOf("kind", "staking", "earn", "airdrop", "other") ?? "staking", ignoreCase: true);
        var receivedOn = a.Date("received_on") ?? ctx.Today;
        a.IdempotencyKey();

        var (holding, _) = await HoldingAsync(id, ct);
        var request = new RewardRequest(quantity, receivedOn, kind, null);
        await CheckAsync(rewardValidator, request, ct);
        var rewardId = Ok(await holdings.AddRewardAsync(id, request, ct));
        return new(Result(rewardId, "crypto_reward",
            $"Added a {kind.ToString().ToLowerInvariant()} reward of {quantity.ToString(CultureInfo.InvariantCulture)} coins received on {Day(receivedOn)}.",
            new { holdingId = id }),
            rewardId, new { holdingId = id, rewardQuantity = holding.RewardQuantity },
            new { holdingId = id, rewardId, quantity, kind = kind.ToString(), receivedOn });
    }

    private async Task<WriteOutput> UpdateAssetValueAsync(WriteContext ctx, CancellationToken ct)
    {
        var a = ctx.Args;
        var id = ToolArgs.Required("asset_id", a.Id("asset_id"));
        var value = ToolArgs.Required("value", a.Decimal("value", 0m, 1_000_000_000_000m, 2));
        var on = a.Date("on") ?? ctx.Today;
        a.IdempotencyKey();
        if (on > ctx.Today.AddDays(1))
        {
            throw new ToolArgumentException("on cannot be in the future.");
        }

        var before = await investments.ManualAssets.AsNoTracking().Include(x => x.Valuations)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        var previous = before?.Valuations.OrderByDescending(v => v.Date).FirstOrDefault();
        var asset = Ok(await InvestmentEndpoints.ValueAssetAsync(investments, id, value, on, refuseArchived: true, ct));
        return new(Result(id, "asset",
            $"Recorded a value of {Money(value, asset.Currency)} on {Day(on)} for the {(asset.IsLiability ? "liability" : "asset")}.",
            new { name = UntrustedText.From(asset.Name) }),
            id, previous is null ? null : new { value = previous.Value, on = previous.Date }, new { value, on });
    }

    // ---------------------------------------------------------------- guards and helpers

    private async Task<Transaction> EditableTransactionAsync(Guid id, CancellationToken ct)
    {
        var t = await finance.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ToolRefusedException(404, "Transaction not found (it may have been deleted).");
        if (AiWritePolicy.RefusalFor(t.Source, t.Type) is { } refusal)
        {
            throw new ToolRefusedException(403, refusal);
        }

        var archived = await finance.Accounts.AsNoTracking()
            .AnyAsync(x => (x.Id == t.AccountId || x.Id == t.CounterAccountId) && x.ArchivedAtUtc != null, ct);
        return archived
            ? throw new ToolRefusedException(403, "This transaction belongs to an archived account; restore the account in the app first.")
            : t;
    }

    /// <summary>An account that accepts hand-entered entries: it exists, is not archived and is not a broker.</summary>
    private async Task<Account> WritableAccountAsync(Guid id, string what, CancellationToken ct)
    {
        var account = await finance.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                      ?? throw new ToolRefusedException(404, $"{what}: account not found.");
        if (account.ArchivedAtUtc is not null)
        {
            throw new ToolRefusedException(403, $"{what}: the account is archived.");
        }

        return account.IsManual
            ? account
            : throw new ToolRefusedException(403, $"{what}: broker accounts and crypto locations are read-only.");
    }

    private async Task<(ExpectedTransaction Item, RecurringTransaction Template)> PendingAsync(Guid id, CancellationToken ct)
    {
        var item = await finance.ExpectedTransactions.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct)
                   ?? throw new ToolRefusedException(404, "Pending recurring item not found.");
        if (item.Status != ExpectedStatus.Pending)
        {
            throw new ToolRefusedException(409, $"This item was already {item.Status.ToString().ToLowerInvariant()}.");
        }

        var template = await finance.RecurringTransactions.AsNoTracking()
            .FirstAsync(r => r.Id == item.RecurringTransactionId, ct);
        return (item, template);
    }

    private async Task<FinancialGoalView> ActiveGoalAsync(Guid id, CancellationToken ct)
    {
        var goal = await finance.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct)
                   ?? throw new ToolRefusedException(404, "Goal not found.");
        return goal.ArchivedAtUtc is not null
            ? throw new ToolRefusedException(403, "This goal is archived; restore it in the app before changing it.")
            : new FinancialGoalView(goal.Id, goal.Name, goal.TargetAmount, goal.TargetDate, goal.StartingAmount,
                goal.ManualCurrentAmount, goal.Icon, goal.AccountId);
    }

    private sealed record FinancialGoalView(Guid Id, string Name, decimal TargetAmount, DateOnly? TargetDate,
        decimal StartingAmount, decimal? ManualCurrentAmount, string? Icon, Guid? AccountId);

    private async Task<(ManualHolding Holding, Account Location)> HoldingAsync(Guid id, CancellationToken ct)
    {
        var holding = await investments.ManualHoldings.AsNoTracking().Include(h => h.Rewards)
                          .FirstOrDefaultAsync(h => h.Id == id, ct)
                      ?? throw new ToolRefusedException(404, "Hand-entered crypto holding not found. Broker positions cannot be changed.");
        var location = await finance.Accounts.AsNoTracking().FirstAsync(x => x.Id == holding.AccountId, ct);
        return location.ArchivedAtUtc is not null
            ? throw new ToolRefusedException(403, "This holding's location is archived; restore it in the app first.")
            : (holding, location);
    }

    private async Task<(Guid Id, string Name)> CategoryAsync(string text, CategoryType type, CancellationToken ct) =>
        await FinanceTools.ResolveCategoryAsync(finance, text, type, activeOnly: true, ct)
        ?? throw new ToolArgumentException($"Unknown {type.ToString().ToLowerInvariant()} category. Use a category key or name.");

    /// <summary>Resolves "category: amount" lines (active categories of the transaction's type); they must add up.</summary>
    private async Task<List<SplitLineRequest>> SplitsAsync(IReadOnlyList<(string Category, decimal Amount)> lines,
        CategoryType type, decimal total, CancellationToken ct)
    {
        var result = new List<SplitLineRequest>();
        foreach (var (category, lineAmount) in lines)
        {
            var (id, _) = await CategoryAsync(category, type, ct);
            if (result.Any(r => r.CategoryId == id))
            {
                throw new ToolArgumentException("Each category can appear only once in splits.");
            }

            result.Add(new SplitLineRequest(id, lineAmount, null));
        }

        var sum = result.Sum(r => r.Amount);
        return sum == total
            ? result
            : throw new ToolArgumentException(
                $"The splits add up to {sum.ToString(CultureInfo.InvariantCulture)} but the amount is {total.ToString(CultureInfo.InvariantCulture)}.");
    }

    private async Task<string?> CategoryNameAsync(Guid? id, CancellationToken ct) => id is null
        ? null
        : await finance.Categories.AsNoTracking().Where(c => c.Id == id).Select(c => c.Name).FirstOrDefaultAsync(ct);

    /// <summary>EUR per unit for a non-EUR account on the entry's date (the same reference rates as the app).</summary>
    private async Task<decimal?> FxRateAsync(string currency, DateOnly on, CancellationToken ct)
    {
        if (currency == Currency.Base)
        {
            return null;
        }

        return await fx.EurPerUnitAsync(currency, on, ct)
               ?? throw new ToolRefusedException(409, $"No {currency} exchange rate is available for {Day(on)}; add this entry in the app.");
    }

    private static async Task CheckAsync<T>(IValidator<T> validator, T request, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(request, ct);
        if (!result.IsValid)
        {
            throw new ToolArgumentException(string.Join(" ", result.Errors.Select(e => e.ErrorMessage).Distinct().Take(3)));
        }
    }

    private static T Ok<T>(Result<T> result) => result.IsSuccess ? result.Value : throw ToolRefusedException.From(result.Error);

    private static void Ok(Result result)
    {
        if (result.IsFailure)
        {
            throw ToolRefusedException.From(result.Error);
        }
    }

    private static string Required(string name, string? value) =>
        value ?? throw new ToolArgumentException($"{name} is required.");

    private static object Result(Guid id, string kind, string summary, object? details = null) =>
        new { id, kind, summary, details };

    private static object TransactionSnapshot(Transaction t, string? category) => new
    {
        type = TypeName(t.Type), date = t.OccurredOn, amount = t.OriginalAmount, currency = t.OriginalCurrency,
        category, accountId = t.AccountId, toAccountId = t.CounterAccountId, description = t.Description,
        splits = t.IsSplit ? t.Splits.OrderBy(sp => sp.Position).Select(sp => new { categoryId = sp.CategoryId, amount = sp.OriginalAmount }).ToList() : null,
    };

    private static string ScheduleText(RecurringTransaction r) => r.Frequency switch
    {
        RecurrenceFrequency.Daily => r.Interval == 1 ? "every day" : $"every {r.Interval} days",
        _ => $"{r.Frequency.ToString().ToLowerInvariant()}{(r.Interval > 1 ? $", every {r.Interval}" : "")}",
    };

    private static object RecurringSnapshot(RecurringTransaction r, string? category) => new
    {
        name = r.Name, type = TypeName(r.Type), amount = r.Amount, currency = r.Currency,
        frequency = r.Frequency.ToString(), interval = r.Interval, dayOfMonth = r.DayOfMonth, startOn = r.StartOn,
        endOn = r.EndOn, category, accountId = r.AccountId,
    };

    private static TransactionType TransactionTypeOf(string type) => type switch
    {
        "expense" => TransactionType.Expense,
        "income" => TransactionType.Income,
        _ => TransactionType.Transfer,
    };

    private static string TypeName(TransactionType type) => type switch
    {
        TransactionType.Expense => "expense",
        TransactionType.Income => "income",
        TransactionType.Transfer => "transfer",
        TransactionType.Savings => "savings",
        _ => "investment",
    };

    private static string Money(decimal amount, string currency) =>
        $"{amount.ToString("0.00######", CultureInfo.InvariantCulture)} {currency}";

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
