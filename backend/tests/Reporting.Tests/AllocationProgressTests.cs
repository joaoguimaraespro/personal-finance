using System.Globalization;
using Finance.Domain.Allocation;
using Reporting.Application;

namespace Reporting.Tests;

/// <summary>A bucket's allocation status from what was actually set aside against its target.</summary>
public sealed class AllocationProgressTests
{
    [Theory]
    [InlineData(null, "0", null)]
    [InlineData("0", "50", null)]
    [InlineData("300", "0", AllocationStatus.Todo)]
    [InlineData("300", "0.01", AllocationStatus.Partial)]
    [InlineData("300", "299.99", AllocationStatus.Partial)]
    [InlineData("300", "300", AllocationStatus.Done)]
    [InlineData("300", "450", AllocationStatus.Done)]
    public void Status_follows_actual_against_target(string? target, string actual, AllocationStatus? expected) =>
        AllocationProgress.Of(target is null ? null : decimal.Parse(target, CultureInfo.InvariantCulture),
            decimal.Parse(actual, CultureInfo.InvariantCulture)).ShouldBe(expected);
}
