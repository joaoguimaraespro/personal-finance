using Host.Api.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Integration.Tests;

/// <summary>
/// Database-free checks that periodic jobs follow the wall clock, so a host that sleeps or is switched off for days
/// (a Windows PC, a laptop) catches up right after it comes back instead of waiting out a timer.
/// </summary>
public sealed class WallClockJobTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_job_is_due_on_first_check_after_its_interval_on_a_new_day_or_after_a_clock_change()
    {
        var sixHours = TimeSpan.FromHours(6);

        JobSchedule.IsDue(null, Monday, sixHours).ShouldBeTrue();
        JobSchedule.IsDue(Monday, Monday.AddHours(1), sixHours).ShouldBeFalse();
        JobSchedule.IsDue(Monday, Monday.AddHours(6), sixHours).ShouldBeTrue();
        // 23:30 → 00:10: only 40 minutes, but a new day's snapshot and interest must not wait six hours.
        var lateEvening = new DateTimeOffset(2026, 10, 5, 23, 30, 0, TimeSpan.Zero);
        JobSchedule.IsDue(lateEvening, lateEvening.AddMinutes(40), sixHours).ShouldBeTrue();
        // Host off for a week.
        JobSchedule.IsDue(Monday, Monday.AddDays(7), sixHours).ShouldBeTrue();
        // Clock corrected backwards (e.g. resync after resume): re-anchor rather than stall until it catches up.
        JobSchedule.IsDue(Monday, Monday.AddMinutes(-5), sixHours).ShouldBeTrue();
    }

    [Fact]
    public async Task Runs_shortly_after_start_then_waits_for_its_interval()
    {
        var clock = new ManualClock(Monday);
        using var job = new CountingJob(clock);
        await job.StartAsync(TestContext.Current.CancellationToken);
        await clock.WaitForTimersCreatedAsync(1); // start-up delay

        clock.Advance(TimeSpan.FromMinutes(1));
        await clock.WaitForTimersCreatedAsync(2);
        job.Runs.ShouldBe(1);

        // Five hours of polls: not due yet.
        for (var timers = 3; timers <= 2 + 300; timers++)
        {
            clock.Advance(WallClockJob.DefaultPollInterval);
            await clock.WaitForTimersCreatedAsync(timers);
        }

        job.Runs.ShouldBe(1);

        // Six hours after the first run: due again.
        for (var timers = 303; timers <= 2 + 360; timers++)
        {
            clock.Advance(WallClockJob.DefaultPollInterval);
            await clock.WaitForTimersCreatedAsync(timers);
        }

        job.Runs.ShouldBe(2);

        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Catches_up_within_one_poll_after_the_host_sleeps_for_days()
    {
        var clock = new ManualClock(Monday);
        using var job = new CountingJob(clock);
        await job.StartAsync(TestContext.Current.CancellationToken);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(TimeSpan.FromMinutes(1));
        await clock.WaitForTimersCreatedAsync(2);
        job.Runs.ShouldBe(1);

        // Suspended: the wall clock moves two days on, but no timer counts that time.
        clock.Suspend(TimeSpan.FromDays(2));
        clock.Advance(WallClockJob.DefaultPollInterval);
        await clock.WaitForTimersCreatedAsync(3);
        job.Runs.ShouldBe(2);

        clock.Advance(WallClockJob.DefaultPollInterval);
        await clock.WaitForTimersCreatedAsync(4);
        job.Runs.ShouldBe(2); // and not again until the interval or a new day

        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Broker_syncs_are_checked_soon_after_start_and_often_enough_for_a_host_that_sleeps()
    {
        BrokerSyncService.StartupDelay.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(2));
        BrokerSyncService.PollInterval.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(5));
    }

    private sealed class CountingJob(TimeProvider clock) : WallClockJob(clock, NullLogger.Instance)
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(1);

        protected override TimeSpan Interval => TimeSpan.FromHours(6);

        protected override Task RunOnceAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _runs);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A wall clock plus timers that count running time only. <see cref="Advance"/> moves both;
    /// <see cref="Suspend"/> moves the wall clock alone, like a sleeping machine.
    /// </summary>
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private readonly Lock _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _wall = start;
        private TimeSpan _running;
        private int _created;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _wall;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_gate)
            {
                timer.DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _running + dueTime;
                _timers.Add(timer);
                _created++;
            }

            return timer;
        }

        public void Suspend(TimeSpan duration)
        {
            lock (_gate)
            {
                _wall += duration;
            }
        }

        public void Advance(TimeSpan duration)
        {
            List<ManualTimer> due;
            lock (_gate)
            {
                _wall += duration;
                _running += duration;
                due = _timers.Where(t => t.DueAt <= _running).ToList();
                foreach (var t in due)
                {
                    _timers.Remove(t);
                }
            }

            foreach (var t in due)
            {
                t.Fire();
            }
        }

        public async Task WaitForTimersCreatedAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Volatile.Read(ref _created) < count)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"Expected {count} timers, saw {_created}.");
                }

                await Task.Delay(5);
            }
        }

        private void Remove(ManualTimer timer)
        {
            lock (_gate)
            {
                _timers.Remove(timer);
            }
        }

        private sealed class ManualTimer(ManualClock owner, TimerCallback callback, object? state) : ITimer
        {
            public TimeSpan? DueAt { get; set; }

            public void Fire() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate)
                {
                    DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._running + dueTime;
                    if (!owner._timers.Contains(this))
                    {
                        owner._timers.Add(this);
                    }
                }

                return true;
            }

            public void Dispose() => owner.Remove(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
