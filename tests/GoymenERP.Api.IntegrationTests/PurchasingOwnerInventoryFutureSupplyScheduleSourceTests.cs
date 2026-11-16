using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class PurchasingOwnerInventoryFutureSupplyScheduleSourceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Order = Guid.Parse("8d050000-0000-4000-8000-000000000001");
    private static readonly Guid Line = Guid.Parse("8d050000-0000-4000-8000-000000000002");
    private static readonly Guid Unit = Guid.Parse("8d050000-0000-4000-8000-000000000003");

    [Fact]
    public async Task The_latest_remaining_delivery_date_is_used_without_redistributing_receipts_or_sorting_acceptance_by_date()
    {
        var owner = new Owner(Found([
            Slice(1, new(2026, 12, 30), "10", "0"), // Owner already consumed the first slice, although its date is later.
            Slice(2, new(2026, 11, 20), "10", "5"),
            Slice(3, new(2026, 12, 10), "10", "10")]));
        var result = await new PurchasingOwnerInventoryFutureSupplyScheduleSource(owner, Context()).GetLineScheduleAsync(Order, Line);
        Assert.Equal((true, Order, Line, new DateOnly(2026, 12, 10)),
            (result.IsReadable, result.PurchaseOrderId, result.PurchaseOrderLineId, result.ExpectedDeliveryDate));
        Assert.Equal([(Order, Line)], owner.Asked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Only_an_explicit_found_empty_or_fully_consumed_schedule_is_readable_without_a_date(bool empty)
    {
        var result = await new PurchasingOwnerInventoryFutureSupplyScheduleSource(new Owner(Found(empty ? [] :
            [Slice(1, new(2026, 12, 1), "1", "0")])), Context()).GetLineScheduleAsync(Order, Line);
        Assert.True(result.IsReadable);
        Assert.Null(result.ExpectedDeliveryDate);
    }

    [Theory]
    [InlineData(PurchaseFutureSupplyScheduleOutcomeV1.NotFound)]
    [InlineData(PurchaseFutureSupplyScheduleOutcomeV1.Unreadable)]
    public async Task A_missing_or_unreadable_owner_answer_never_becomes_an_undated_success(PurchaseFutureSupplyScheduleOutcomeV1 outcome)
    {
        var result = await new PurchasingOwnerInventoryFutureSupplyScheduleSource(new Owner(Found([]) with { Outcome = outcome }), Context())
            .GetLineScheduleAsync(Order, Line);
        Assert.False(result.IsReadable);
    }

    [Fact]
    public async Task Malformed_foreign_and_changed_company_answers_fail_closed()
    {
        var slice = Slice(1, new(2026, 12, 1), "10", "5");
        PurchaseFutureSupplyScheduleV1[] malformed = [
            Found([slice]) with { CompanyId = TestCompanyIds.CompanyB },
            Found([slice]) with { OrderId = Guid.NewGuid() },
            Found([slice]) with { LineId = Guid.NewGuid() },
            Found([slice]) with { ObservedOrderVersion = 0 },
            Found([slice]) with { UnitId = Guid.Empty },
            Found([slice, slice]),
            Found([slice with { RemainingText = "11" }]),
            Found([slice with { RemainingText = "1,5" }]),
            Found([slice with { QuantityText = "0" }]),
            Found([slice with { SliceNo = 0 }]),
            Found([slice with { DeliveryDate = default }]),
        ];
        foreach (var answer in malformed)
            Assert.False((await new PurchasingOwnerInventoryFutureSupplyScheduleSource(new Owner(answer), Context())
                .GetLineScheduleAsync(Order, Line)).IsReadable);
        var context = Context();
        var switched = new Owner(Found([slice]), () => context.Set(TestCompanyIds.CompanyB));
        Assert.False((await new PurchasingOwnerInventoryFutureSupplyScheduleSource(switched, context)
            .GetLineScheduleAsync(Order, Line)).IsReadable);
    }

    [Fact]
    public async Task Technical_failures_are_unreadable_while_caller_cancellation_is_preserved()
    {
        var source = new PurchasingOwnerInventoryFutureSupplyScheduleSource(new Owner(Found([]),
            () => throw new InvalidOperationException("SENTETIK owner failure")), Context());
        Assert.False((await source.GetLineScheduleAsync(Order, Line)).IsReadable);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetLineScheduleAsync(Order, Line, cancellation.Token));
    }

    private static CompanyContext Context() { var company = new CompanyContext(); company.Set(Company); return company; }
    private static PurchaseFutureSupplyScheduleV1 Found(IReadOnlyList<PurchaseFutureSupplyScheduleSliceV1> slices) =>
        new(PurchaseFutureSupplyScheduleOutcomeV1.Found, Company, Order, Line, Unit, 3, slices);
    private static PurchaseFutureSupplyScheduleSliceV1 Slice(int sequence, DateOnly day, string quantity, string remaining) =>
        new(Guid.NewGuid(), sequence, day, quantity, remaining);
    private sealed class Owner(PurchaseFutureSupplyScheduleV1 answer, Action? duringRead = null) : IPurchaseFutureSupplyScheduleReadPortV1
    {
        public List<(Guid, Guid)> Asked { get; } = [];
        public Task<PurchaseFutureSupplyScheduleV1> GetLineScheduleAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default)
        { Asked.Add((orderId, lineId)); duringRead?.Invoke(); return Task.FromResult(answer); }
    }
}
