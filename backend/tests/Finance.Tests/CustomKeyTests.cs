using Finance.Domain.Allocation;
using Finance.Domain.Categories;

namespace Finance.Tests;

/// <summary>Custom buckets and categories get unique keys even when created in a burst (e.g. a JSON import).</summary>
public sealed class CustomKeyTests
{
    [Fact]
    public void Custom_buckets_created_in_a_burst_have_distinct_keys() =>
        Enumerable.Range(0, 2_000).Select(i => AllocationBucket.CreateCustom($"B{i}", BucketGroup.Investment).Key)
            .Distinct().Count().ShouldBe(2_000);

    [Fact]
    public void Custom_categories_created_in_a_burst_have_distinct_keys() =>
        Enumerable.Range(0, 2_000).Select(i => Category.CreateCustom($"C{i}", CategoryType.Expense, null, null, null, null).Key)
            .Distinct().Count().ShouldBe(2_000);
}
