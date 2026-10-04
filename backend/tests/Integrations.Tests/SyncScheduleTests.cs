using Integrations.Application.Contracts;
using Integrations.Application.Sync;

namespace Integrations.Tests;

public sealed class SyncScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_failed_ibkr_sync_is_not_retried_on_every_tick()
    {
        var yesterday = Now.AddDays(-1);
        var failedRecently = new LastAttempt(Now.AddMinutes(-30), Failed: true, LockedOut: false);
        var failedLongAgo = new LastAttempt(Now.AddHours(-7), Failed: true, LockedOut: false);

        SyncSchedule.IsDue(BrokerKind.InteractiveBrokers, yesterday, failedRecently, Now).ShouldBeFalse();
        SyncSchedule.IsDue(BrokerKind.InteractiveBrokers, yesterday, failedLongAgo, Now).ShouldBeTrue();
    }

    [Fact]
    public void A_lockout_pauses_scheduled_syncs_for_twelve_hours()
    {
        var locked = new LastAttempt(Now.AddHours(-8), Failed: true, LockedOut: true);

        SyncSchedule.IsDue(BrokerKind.InteractiveBrokers, Now.AddDays(-2), locked, Now).ShouldBeFalse();
        SyncSchedule.IsDue(BrokerKind.InteractiveBrokers, Now.AddDays(-2), locked, Now.AddHours(5)).ShouldBeTrue();
    }

    [Fact]
    public void Successful_syncs_keep_the_normal_cadence()
    {
        var ok = new LastAttempt(Now.AddHours(-5), Failed: false, LockedOut: false);

        SyncSchedule.IsDue(BrokerKind.Trading212, Now.AddHours(-5), ok, Now).ShouldBeTrue();
        SyncSchedule.IsDue(BrokerKind.Trading212, Now.AddHours(-1), ok, Now).ShouldBeFalse();
        SyncSchedule.IsDue(BrokerKind.InteractiveBrokers, Now, ok, Now).ShouldBeFalse(); // already synced today
        SyncSchedule.IsDue(BrokerKind.InteractiveBrokers, null, null, Now).ShouldBeTrue(); // never synced
    }

    [Theory]
    [InlineData("[\"IBKR Flex error 1025: Too many failed attempts\"]", true)]
    [InlineData("[\"Too many failed attempts\"]", true)]
    [InlineData("[\"IBKR Flex busy (1019)\"]", false)]
    [InlineData(null, false)]
    public void Recognises_a_lockout_in_stored_errors(string? errors, bool expected) =>
        SyncSchedule.IsLockout(errors).ShouldBe(expected);
}
