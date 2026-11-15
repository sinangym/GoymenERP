using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// F-017 STOK F017-c (§3.5), without a database: the pinned names, the link failure texts, the consumption order and the
/// fail-closed default kilogram conversion. What SQL Server enforces (the link table checks, the single save in the receipt
/// transaction, the Down guard) is proven by <c>InventorySqlServerRelationalAcceptanceTests.FutureSupplyConversion</c>.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryGoodsReceiptFutureSupplyContractTests
{
    private static readonly Guid First = Guid.Parse("8a17c000-0000-4000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("8a17c000-0000-4000-8000-000000000002");
    private static readonly Guid Third = Guid.Parse("8a17c000-0000-4000-8000-000000000003");
    private static readonly Guid Fourth = Guid.Parse("8a17c000-0000-4000-8000-000000000004");
    private static readonly Guid Unknown = Guid.Parse("8a17c000-0000-4000-8000-0000000000ff");
    private static readonly DateTimeOffset Created = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_command_event_reason_and_outcome_values_are_the_pinned_contract_values()
    {
        Assert.Equal(("inventory.future-allocation.convert-on-receipt.v1", "Convert", "inventory_future_allocation_order_invalid"),
            (InventoryGoodsReceiptFutureSupplyV1.CommandName, InventoryGoodsReceiptFutureSupplyV1.ConvertOperation,
                InventoryGoodsReceiptFutureSupplyV1.AllocationOrderInvalid));
        Assert.Equal("acceptance-link-failed", InventoryGoodsReceiptFutureSupplyV1.LinkFailedReleaseReason);
        Assert.Equal(("inventory.future-allocation.converted.v1", 1, "Inventory.FutureSupplyAllocation"),
            (InventoryFutureAllocationConvertedOutboxV1.EventType, InventoryFutureAllocationConvertedOutboxV1.SchemaVersion,
                InventoryFutureAllocationConvertedOutboxV1.AggregateType));
        Assert.Equal(InventoryGoodsReceiptFutureSupplyV1.CommandName, InventoryFutureAllocationConvertedOutboxV1.SourceOperation);
        Assert.Equal((1, 2), ((int)InventoryFutureSupplyConversionOutcomeV1.Converted, (int)InventoryFutureSupplyConversionOutcomeV1.LinkFailed));
    }

    [Fact]
    public void Every_link_failure_has_a_Turkish_release_text_that_fits_the_reason_column_and_an_unknown_one_is_refused()
    {
        string[] failures =
        [
            InventoryFutureSupplyLinkFailuresV1.LabelBlocked,
            InventoryFutureSupplyLinkFailuresV1.UnitConversionUnavailable,
            InventoryFutureSupplyLinkFailuresV1.DemandNotApproved,
            InventoryFutureSupplyLinkFailuresV1.ProductMismatch,
            InventoryFutureSupplyLinkFailuresV1.InsufficientLabelQuantity,
            InventoryFutureSupplyLinkFailuresV1.OwnerNotCompany,
        ];

        var texts = failures.Select(InventoryFutureSupplyLinkFailuresV1.ReleaseText).ToArray();

        Assert.Equal(failures.Length, failures.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(failures.Length, texts.Distinct(StringComparer.Ordinal).Count());
        Assert.All(failures, failure => Assert.InRange(failure.Length, 1, 64));
        Assert.All(texts, text =>
        {
            Assert.StartsWith("Mal kabul bağı kurulamadı: ", text, StringComparison.Ordinal);
            Assert.InRange(text.Length, 1, 1000);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryFutureSupplyLinkFailuresV1.ReleaseText("unknown"));
    }

    [Fact]
    public void Without_a_host_order_the_manual_sequence_comes_first_then_the_due_date_then_the_creation_time()
    {
        InventoryFutureSupplyConsumptionCandidateV1[] candidates =
        [
            new(First, null, null, Created),
            new(Second, null, new DateOnly(2026, 12, 1), Created.AddHours(2)),
            new(Third, 2, null, Created.AddHours(3)),
            new(Fourth, 1, new DateOnly(2027, 1, 1), Created.AddHours(4)),
        ];

        Assert.True(InventoryFutureSupplyConsumptionOrderV1.TryOrder(candidates, null, out var ordered));

        Assert.Equal(new[] { Fourth, Third, Second, First }, ordered.Select(candidate => candidate.AllocationId).ToArray());
    }

    [Fact]
    public void An_earlier_due_date_wins_and_equal_dates_fall_back_to_the_creation_time_and_then_the_id()
    {
        var due = new DateOnly(2026, 12, 1);
        InventoryFutureSupplyConsumptionCandidateV1[] candidates =
        [
            new(Third, null, due, Created),
            new(Second, null, due, Created),
            new(First, null, due, Created.AddMinutes(1)),
            new(Fourth, null, due.AddDays(-1), Created.AddDays(1)),
        ];

        Assert.True(InventoryFutureSupplyConsumptionOrderV1.TryOrder(candidates, Array.Empty<Guid>(), out var ordered));

        Assert.Equal(new[] { Fourth, Second, Third, First }, ordered.Select(candidate => candidate.AllocationId).ToArray());
    }

    [Fact]
    public void A_partial_host_order_goes_first_in_its_own_order_and_the_rest_follow_the_stored_order()
    {
        InventoryFutureSupplyConsumptionCandidateV1[] candidates =
        [
            new(First, 1, null, Created),
            new(Second, 2, null, Created),
            new(Third, 3, null, Created),
            new(Fourth, 4, null, Created),
        ];

        Assert.True(InventoryFutureSupplyConsumptionOrderV1.TryOrder(candidates, new[] { Fourth, Second }, out var ordered));

        Assert.Equal(new[] { Fourth, Second, First, Third }, ordered.Select(candidate => candidate.AllocationId).ToArray());
    }

    [Fact]
    public void A_host_order_that_repeats_an_id_or_names_an_unknown_one_is_refused()
    {
        InventoryFutureSupplyConsumptionCandidateV1[] candidates = [new(First, null, null, Created), new(Second, null, null, Created)];

        Assert.False(InventoryFutureSupplyConsumptionOrderV1.TryOrder(candidates, new[] { First, First }, out var repeated));
        Assert.False(InventoryFutureSupplyConsumptionOrderV1.TryOrder(candidates, new[] { Unknown }, out var unknown));

        Assert.Empty(repeated);
        Assert.Empty(unknown);
    }

    [Fact]
    public async Task The_default_kilogram_conversion_answers_NoSource_and_never_guesses_a_factor()
    {
        IInventoryFutureUnitConverterV1 converter = new InventoryIdentityFutureUnitConverterV1();

        var answer = await converter.ConvertToKilogramsAsync(First, "v1", 12.5m, Second);

        Assert.Equal((InventoryFutureUnitConversionOutcomeV1.NoSource, (decimal?)null), (answer.Outcome, answer.Quantity));
    }
}
