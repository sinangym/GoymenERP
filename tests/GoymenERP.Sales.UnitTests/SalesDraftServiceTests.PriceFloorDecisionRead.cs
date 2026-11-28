using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Selected_floor_read_uses_only_approval_and_independent_price_disclosure(bool withheld)
    {
        var (world, id) = await FloorPurposeWorld();
        world.Authority.Denied.Add(ReadPermission);
        if (withheld) world.Authority.Denied.Add(PriceReadPermission);
        world.Authority.Measured.Clear();
        var answer = await world.Service().ReadPriceFloorDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(SalesPriceFloorDecisionReadStateV1.Found, answer.State);
        var detail = Assert.IsType<SalesPriceFloorDecisionDetailV1>(answer.Detail);
        Assert.True(detail.ApprovalSupported); Assert.Single(detail.Lines);
        Assert.Equal(withheld ? SalesPriceDisclosure.Withheld : SalesPriceDisclosure.Visible, detail.PriceDisclosure);
        if (withheld) Assert.Null(detail.Lines[0].Pricing);
        else Assert.Equal("11", Assert.IsType<SalesDraftLinePricingV1>(detail.Lines[0].Pricing).NetUnitPrice);
        Assert.NotNull(detail.Lines[0].FloorException);
        Assert.All(world.Authority.Measured, p => Assert.Contains(p, new[] { SchedulePermission, PriceReadPermission }));
        Assert.Equal(SalesPriceFloorDecisionDigestsV1.PreviewDigest(detail), detail.PreviewDigest);
        Assert.Equal(1, world.Store.FloorPurposeReads);
    }

    [Theory]
    [InlineData("approval")] [InlineData("price")] [InlineData("actor")] [InlineData("company")]
    [InlineData("cancel")]
    public async Task Selected_floor_final_normal_completed_consumer_cannot_release_revoked_facts(string fault)
    {
        var (world, id) = await FloorPurposeWorld(); var callbacks = 0;
        using var cancellation = new CancellationTokenSource();
        Task Fence(CancellationToken _)
        {
            if (++callbacks == 5)
            {
                if (fault == "approval") world.Authority.Denied.Add(SchedulePermission);
                if (fault == "price") world.Authority.Denied.Add(PriceReadPermission);
                if (fault == "actor") world.User.Id = "other";
                if (fault == "company") world.User.Company = Guid.NewGuid();
                if (fault == "cancel") cancellation.Cancel();
            }
            return Task.CompletedTask;
        }
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Service().ReadPriceFloorDecisionAsync(id, Fence, cancellation.Token));
        else
        {
            var answer = await world.Service().ReadPriceFloorDecisionAsync(id, Fence);
            Assert.Equal(fault == "price" ? SalesPriceFloorDecisionReadStateV1.Found : SalesPriceFloorDecisionReadStateV1.Denied, answer.State);
            if (fault == "price") { Assert.Equal(SalesPriceDisclosure.Withheld, answer.Detail!.PriceDisclosure); Assert.All(answer.Detail.Lines, x => Assert.Null(x.Pricing)); }
            else Assert.Null(answer.Detail);
        }
        Assert.Equal(5, callbacks);
    }

    [Theory]
    [InlineData("last-requester")] [InlineData("wrong-company")] [InlineData("wrong-id")]
    [InlineData("duplicate-id")] [InlineData("duplicate-line")]
    [InlineData("501-lines")] [InlineData("missing-floor")]
    public async Task Selected_floor_full_set_and_all_requester_SoD_are_fail_closed(string fault)
    {
        var (world, id) = await FloorPurposeWorld(); var source = await world.Store.ReadPriceFloorDecisionSourceAsync(id, _ => Task.CompletedTask);
        Assert.NotNull(source); var first = Assert.Single(source.Lines); var second = first with { LineId = Guid.NewGuid(), LineNo = 2 };
        if (fault == "last-requester") second = second with { Commercial = first.Commercial with
        { FloorException = first.Commercial.FloorException! with { RequestedBy = world.User.Id } } };
        world.Store.FloorPurposeOverride = fault switch
        {
            "wrong-company" => source with { CompanyId = Guid.NewGuid() },
            "wrong-id" => source with { DraftId = Guid.NewGuid() },
            "duplicate-id" => source with { Lines = [first, second with { LineId = first.LineId }] },
            "duplicate-line" => source with { Lines = [first, second with { LineNo = first.LineNo }] },
            "501-lines" => source with { Lines = Enumerable.Range(1, 501).Select(n => first with { LineId = Guid.NewGuid(), LineNo = n }).ToArray() },
            "missing-floor" => source with { Lines = [first with { Commercial = first.Commercial with { FloorException = null } }] },
            _ => source with { Lines = [first, second] },
        };
        var answer = await world.Service().ReadPriceFloorDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(fault == "last-requester" ? SalesPriceFloorDecisionReadStateV1.Denied : SalesPriceFloorDecisionReadStateV1.Unavailable, answer.State);
        Assert.Null(answer.Detail);
    }

    [Fact]
    public async Task Selected_floor_exact_native_500_line_boundary_is_complete_and_not_truncated()
    {
        var (world, id) = await FloorPurposeWorld(); var source = await world.Store.ReadPriceFloorDecisionSourceAsync(id, _ => Task.CompletedTask);
        Assert.NotNull(source); var first = Assert.Single(source.Lines);
        world.Store.FloorPurposeOverride = source with { Lines = Enumerable.Range(1, SalesOrderDraft.MaximumLines)
            .Select(n => first with { LineId = Guid.NewGuid(), LineNo = n }).ToArray() };
        var answer = await world.Service().ReadPriceFloorDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(SalesPriceFloorDecisionReadStateV1.Found, answer.State); Assert.NotNull(answer.Detail);
        Assert.Equal(SalesOrderDraft.MaximumLines, answer.Detail.Lines.Count); Assert.Equal(SalesOrderDraft.MaximumLines, answer.Detail.Lines[^1].LineNo);
    }

    [Theory]
    [InlineData("unauthenticated")] [InlineData("permission")] [InlineData("membership")] [InlineData("scoped")]
    public async Task Selected_floor_admission_denial_reads_no_commercial_owner_facts(string fault)
    {
        var (world, id) = await FloorPurposeWorld();
        if (fault == "unauthenticated") world.User.Authenticated = false;
        if (fault == "permission") world.Authority.Denied.Add(SchedulePermission);
        if (fault == "membership") world.User.Company = Guid.NewGuid();
        if (fault == "scoped") world.Authority.CompanyWide = false;
        var answer = await world.Service().ReadPriceFloorDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(SalesPriceFloorDecisionReadStateV1.Denied, answer.State); Assert.Null(answer.Detail);
        Assert.Equal(0, world.Store.FloorPurposeReads);
    }

    [Fact]
    public async Task Selected_floor_missing_and_initial_cancellation_do_not_fabricate_a_preview()
    {
        var world = new World(); world.Bindings = world.Bindings with { PriceFloorExceptionApprovePermission = SchedulePermission };
        var missing = await world.Service().ReadPriceFloorDecisionAsync(Guid.NewGuid(), _ => Task.CompletedTask);
        Assert.Equal(SalesPriceFloorDecisionReadStateV1.NotFound, missing.State); Assert.Null(missing.Detail);
        var before = world.Authority.Calls; using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Service().ReadPriceFloorDecisionAsync(Guid.NewGuid(), _ => throw new InvalidOperationException(), cancel.Token));
        Assert.Equal(before, world.Authority.Calls); Assert.Equal(1, world.Store.FloorPurposeReads);
    }

    [Fact]
    public async Task Selected_floor_detaches_external_lines_before_validation_and_uses_that_copy_after_the_final_consumer()
    {
        var (world, id) = await FloorPurposeWorld(); var original = await world.Store.ReadPriceFloorDecisionSourceAsync(id, _ => Task.CompletedTask);
        Assert.NotNull(original); var first = Assert.Single(original.Lines); var mutable = original.Lines.ToList();
        world.Store.FloorPurposeOverride = original with { Lines = mutable }; var callbacks = 0;
        var answer = await world.Service().ReadPriceFloorDecisionAsync(id, _ =>
        {
            if (++callbacks == 5) { mutable.Clear(); mutable.Add(first with { Quantity = "999", Commercial = first.Commercial with
                { FloorException = first.Commercial.FloorException! with { RequestedBy = world.User.Id } } }); }
            return Task.CompletedTask;
        });
        Assert.Equal(SalesPriceFloorDecisionReadStateV1.Found, answer.State); var detail = Assert.IsType<SalesPriceFloorDecisionDetailV1>(answer.Detail);
        Assert.Equal(first.Quantity, Assert.Single(detail.Lines).Quantity); Assert.Equal(first, Assert.Single(detail.Source!.Lines));
        Assert.Equal(SalesPriceFloorDecisionDigestsV1.SourceFactsDigest(detail.Source), detail.SourceFactsDigest);
        var guarded = Assert.IsAssignableFrom<IList<SalesPriceFloorDecisionSourceLineV1>>(detail.Source.Lines);
        Assert.Throws<NotSupportedException>(() => guarded[0] = mutable[0]);
        Assert.NotEqual(world.User.Id, detail.Lines[0].FloorException!.RequestedBy); Assert.Equal(5, callbacks);
    }

    private static async Task<(World World, Guid Id)> FloorPurposeWorld(bool creatorApproves = false)
    {
        var world = new World { PriceReferences = new FakePriceReferences { Price = 12.50m, Floor = 12m } }.AllowDiscountsUpTo(10m);
        world.Bindings = world.Bindings with { PriceFloorExceptionApprovePermission = SchedulePermission };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("SENTETIK-floor-purpose-create"))).Detail;
        if (creatorApproves) world.User.Id = "SENTETIK-different-commercial-requester";
        await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(created.Lines[0].LineId, "11", KgUnit, null, new SalesDraftPriceReferenceRequestV1(PriceList, 1))),
            Metadata("SENTETIK-floor-purpose-price"));
        world.User.Id = creatorApproves ? Actor : "SENTETIK-independent-floor-approver";
        return (world, created.Draft.Id);
    }

    // Explicit synthetic selected-read protocol. It proves owner fences/withholding; it is not a SQL/transaction test.
    private sealed partial class FakeStore : ISalesPriceFloorDecisionReadStoreV1
    {
        internal int FloorPurposeReads; internal SalesPriceFloorDecisionSourceV1? FloorPurposeOverride;
        public async Task<SalesPriceFloorDecisionSourceV1?> ReadPriceFloorDecisionSourceAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken ct = default)
        {
            await fence(ct); FloorPurposeReads++;
            var draft = Drafts.SingleOrDefault(x => x.Id == id);
            var source = FloorPurposeOverride ?? (draft is null ? null : new SalesPriceFloorDecisionSourceV1(draft.CompanyId, draft.Id,
                draft.DocumentNo, draft.Version, draft.Revision, draft.State.ToString(), draft.ApprovalState.ToString(), draft.Header.PartyId,
                draft.Header.CompanyAccountId, draft.Header.CurrencyId, draft.Header.BusinessDate,
                draft.Lines.Select(x => new SalesPriceFloorDecisionSourceLineV1(x.Id, x.LineNo, x.Product.ProductId, x.Product.ProductVersion,
                    x.Product.Code, x.Product.Name, x.OrderUnit.UnitId, x.QuantityText, x.Commercial)).ToArray()));
            await fence(ct); return source;
        }
        internal SalesDraftReplayResult FloorPurposeStoredResult(string key) => _attempts[(SalesDraftCommands.ApprovePriceFloorExceptionsV1, key)].Result!;
    }
}
