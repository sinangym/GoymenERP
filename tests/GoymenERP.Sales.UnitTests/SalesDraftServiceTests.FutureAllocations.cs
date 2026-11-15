using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// F-017 STOK §5.1 (STK-S16) over SYNTHETIC owners: the cancellation preview reads the allocation owner's ACTIVE FUTURE SUPPLY
/// allocations through the kind-aware <see cref="ISalesOrderAllocationReadPortV2"/> beside the physical V1 read. A future
/// allocation is "aktif tahsis" (Present, the cancellation is the key's recorded 409) until SAT-S8 releases it in the
/// cancellation's own transaction; an owner that is not composed, fails or answers a malformed value makes the category
/// Unreadable (fail-closed, 503 unrecorded). The line's kilogram members stay PHYSICAL: a future quantity is in its purchase unit
/// and is never added to them. The header revision of a customer or currency change reads the same categories.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static readonly Guid FuturePurchaseUnit = Guid.Parse("6d6d6d6d-6d6d-46d6-86d6-6d6d6d6d6d6d");

    [Fact]
    public async Task An_active_future_supply_allocation_blocks_the_cancellation_recorded_and_never_adds_to_the_physical_kilograms()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (allocations, _) = Owners(world);
        var future = new FakeFutureAllocations(order => new(Company, order,
            [new SalesOrderLineAllocationV2(line, SalesOrderAllocationKindsV2.FutureSupply, 2, 7.125m, FuturePurchaseUnit)]));
        world.FutureAllocations = future;
        world.Authority.Measured.Clear();

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        Assert.False(preview.Cancellable);
        Assert.Equal(SalesCancellationImpactStatusesV1.Present,
            preview.Sources.Single(source => source.Source == SalesCancellationImpactSourcesV1.ActiveAllocations).Status);
        // §5.1 a: the physical figure is the V1 read only — no future quantity converted or summed into it.
        Assert.Equal((0, "0"), (preview.Lines[0].ActiveAllocations, preview.Lines[0].AllocatedKg));
        Assert.Equal([approved.Draft.Id], future.Asked);
        Assert.Equal([approved.Draft.Id], allocations.Asked);
        Assert.All(world.Authority.Measured, name => Assert.Equal(CancelPermission, name));

        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-future")));
        Assert.Equal((409, SalesDraftProblemCodesV1.CancellationBlocked), Status(blocked));
        Assert.Equal([new SalesDraftFieldError(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.Present)],
            blocked.Errors);
        Assert.Single(world.Store.Refusals);
        Assert.Empty(world.Store.Cancellations);

        // Released IN INVENTORY: the preview is read again and the order cancels under a new key.
        future.Answer = order => new(Company, order, []);
        var again = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);
        Assert.True(again.Cancellable);
        Assert.NotEqual(preview.ImpactToken, again.ImpactToken);
        var cancelled = await world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", again.ImpactToken), Metadata("k-cancel-future-2"));
        Assert.Equal("Cancelled", cancelled.Detail.Draft.State);
    }

    [Fact]
    public async Task A_physical_allocation_still_blocks_when_the_kind_aware_owner_answers_none_and_its_physical_entry_is_shape_only()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (allocations, _) = Owners(world);
        allocations.Answer = order => new(Company, order, [new(line, 1, 0.5m)]);
        world.FutureAllocations = new FakeFutureAllocations(order => new(Company, order,
            [new SalesOrderLineAllocationV2(line, SalesOrderAllocationKindsV2.Physical, 1, 0.5m, null)]));

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        Assert.False(preview.Cancellable);
        Assert.Equal(SalesCancellationImpactStatusesV1.Present,
            preview.Sources.Single(source => source.Source == SalesCancellationImpactSourcesV1.ActiveAllocations).Status);
        Assert.Equal((1, "0.5"), (preview.Lines[0].ActiveAllocations, preview.Lines[0].AllocatedKg));
    }

    [Fact]
    public async Task A_kind_aware_owner_that_is_not_composed_makes_the_allocations_unreadable_and_the_cancellation_503_unrecorded()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var (allocations, _) = Owners(world);
        world.NoFutureAllocations = true;

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        Assert.False(preview.Cancellable);
        Assert.Equal(SalesCancellationImpactStatusesV1.Unreadable,
            preview.Sources.Single(source => source.Source == SalesCancellationImpactSourcesV1.ActiveAllocations).Status);
        // Unreadable shows no number, never zero — even though the physical V1 read answered.
        Assert.Equal([approved.Draft.Id], allocations.Asked);
        Assert.Equal(((int?)null, (string?)null), (preview.Lines[0].ActiveAllocations, preview.Lines[0].AllocatedKg));
        var unavailable = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-no-v2")));
        Assert.Equal((503, SalesDraftProblemCodesV1.CancellationImpactUnavailable), Status(unavailable));
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.Cancellations);
    }

    public static TheoryData<string> MalformedFutureAllocationAnswers => new()
    {
        "throws", "other-company", "other-order", "null-lines", "null-entry", "unknown-line", "unknown-kind", "kind-case",
        "future-without-unit", "future-empty-unit", "future-above-decimals", "physical-with-unit", "physical-above-scale",
        "twice", "negative-count", "negative-quantity", "zero-count-with-quantity",
    };

    [Theory]
    [MemberData(nameof(MalformedFutureAllocationAnswers))]
    public async Task A_malformed_kind_aware_allocation_answer_is_unreadable_shows_no_number_and_refuses_the_cancellation(string fault)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        Owners(world);
        static SalesOrderLineAllocationV2 Future(Guid line, int count, decimal quantity, Guid? unit) =>
            new(line, SalesOrderAllocationKindsV2.FutureSupply, count, quantity, unit);
        Func<Guid, SalesOrderAllocationReadAnswerV2> answer = fault switch
        {
            "throws" => _ => throw new InvalidOperationException("SENTETIK owner outage"),
            "other-company" => order => new(Guid.NewGuid(), order, []),
            "other-order" => _ => new(Company, Guid.NewGuid(), []),
            "null-lines" => order => new(Company, order, null!),
            "null-entry" => order => new(Company, order, [null!]),
            "unknown-line" => order => new(Company, order, [Future(Guid.NewGuid(), 1, 1m, FuturePurchaseUnit)]),
            "unknown-kind" => order => new(Company, order, [new(line, "Planned", 1, 1m, FuturePurchaseUnit)]),
            "kind-case" => order => new(Company, order, [new(line, "futuresupply", 1, 1m, FuturePurchaseUnit)]),
            "future-without-unit" => order => new(Company, order, [Future(line, 1, 1m, null)]),
            "future-empty-unit" => order => new(Company, order, [Future(line, 1, 1m, Guid.Empty)]),
            // Never rounded: more decimals than the purchase line keeps is not the owner's value.
            "future-above-decimals" => order => new(Company, order, [Future(line, 1, 1.0005m, FuturePurchaseUnit)]),
            "physical-with-unit" => order => new(Company, order,
                [new(line, SalesOrderAllocationKindsV2.Physical, 1, 1m, FuturePurchaseUnit)]),
            "physical-above-scale" => order => new(Company, order,
                [new(line, SalesOrderAllocationKindsV2.Physical, 1, 0.0001m, null)]),
            "twice" => order => new(Company, order, [Future(line, 1, 1m, FuturePurchaseUnit), Future(line, 1, 2m, FuturePurchaseUnit)]),
            "negative-count" => order => new(Company, order, [Future(line, -1, 0m, FuturePurchaseUnit)]),
            "negative-quantity" => order => new(Company, order, [Future(line, 1, -1m, FuturePurchaseUnit)]),
            _ => order => new(Company, order, [Future(line, 0, 1m, FuturePurchaseUnit)]),
        };
        world.FutureAllocations = new FakeFutureAllocations(answer);

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        Assert.False(preview.Cancellable);
        Assert.Equal(SalesCancellationImpactStatusesV1.Unreadable,
            preview.Sources.Single(source => source.Source == SalesCancellationImpactSourcesV1.ActiveAllocations).Status);
        Assert.Null(preview.Lines[0].ActiveAllocations);
        Assert.Null(preview.Lines[0].AllocatedKg);
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-malformed")))).Status);
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task Two_purchase_units_of_one_line_and_a_physical_entry_are_well_formed_and_read_as_present()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        Owners(world);
        world.FutureAllocations = new FakeFutureAllocations(order => new(Company, order,
        [
            new SalesOrderLineAllocationV2(line, SalesOrderAllocationKindsV2.FutureSupply, 1, 12.5m, FuturePurchaseUnit),
            new SalesOrderLineAllocationV2(line, SalesOrderAllocationKindsV2.FutureSupply, 1, 3m, KgUnit),
            new SalesOrderLineAllocationV2(line, SalesOrderAllocationKindsV2.Physical, 0, 0m, null),
        ]));

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        Assert.Equal(SalesCancellationImpactStatusesV1.Present,
            preview.Sources.Single(source => source.Source == SalesCancellationImpactSourcesV1.ActiveAllocations).Status);
        Assert.Equal((0, "0"), (preview.Lines[0].ActiveAllocations, preview.Lines[0].AllocatedKg));
    }

    [Fact]
    public async Task A_customer_change_of_an_order_holding_a_future_supply_allocation_is_refused_unrecorded()
    {
        var world = new World();
        Owners(world);
        var (approved, line) = await AcceptedApprovedAsync(world);
        world.FutureAllocations = new FakeFutureAllocations(order => new(Company, order,
            [new SalesOrderLineAllocationV2(line, SalesOrderAllocationKindsV2.FutureSupply, 1, 1m, FuturePurchaseUnit)]));

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Id,
            HeaderRevision("5", account: NewAccount), Metadata("k-future-blocked")));

        Assert.Equal((409, SalesDraftProblemCodesV1.RevisionBlocked), Status(refused));
        Assert.Contains(new SalesDraftFieldError(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesDraftProblemCodesV1.RevisionBlocked),
            refused.Errors);
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.Archived);

        // Without the kind-aware owner the same change cannot be decided: 503, nothing recorded.
        world.FutureAllocations = null;
        world.NoFutureAllocations = true;
        var unavailable = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Id,
            HeaderRevision("5", account: NewAccount), Metadata("k-future-unavailable")));
        Assert.Equal((503, SalesDraftProblemCodesV1.RevisionImpactUnavailable), Status(unavailable));
        Assert.Empty(world.Store.Refusals);
    }

    /// <summary>A SYNTHETIC kind-aware allocation owner (Inventory's role): answers what the case gives it and records the orders asked.</summary>
    private sealed class FakeFutureAllocations(Func<Guid, SalesOrderAllocationReadAnswerV2> answer) : ISalesOrderAllocationReadPortV2
    {
        public Func<Guid, SalesOrderAllocationReadAnswerV2> Answer { get; set; } = answer;

        public List<Guid> Asked { get; } = [];

        public Task<SalesOrderAllocationReadAnswerV2> ReadOrderAllocationsAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            Asked.Add(orderId);
            return Task.FromResult(Answer(orderId));
        }
    }
}
