using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Selected_discount_read_uses_only_approval_and_independent_price_disclosure(bool withheld)
    {
        var (world, id) = await DiscountPurposeWorld();
        world.Authority.Denied.Add(ReadPermission);
        if (withheld) world.Authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead);
        world.Authority.Measured.Clear();
        var answer = await world.Service().ReadDiscountDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(SalesDiscountDecisionReadStateV1.Found, answer.State);
        var detail = Assert.IsType<SalesDiscountDecisionDetailV1>(answer.Detail);
        Assert.True(detail.ApprovalSupported); Assert.Single(detail.Lines);
        Assert.Equal(withheld ? SalesPriceDisclosure.Withheld : SalesPriceDisclosure.Visible, detail.PriceDisclosure);
        if (withheld) Assert.Null(detail.Lines[0].Pricing);
        else Assert.Equal("11", Assert.IsType<SalesDraftLinePricingV1>(detail.Lines[0].Pricing).NetUnitPrice);
        Assert.NotNull(detail.Lines[0].DiscountCheck);
        Assert.All(world.Authority.Measured, p => Assert.Contains(p, new[] { SalesPermissionManifestV1.OrdersDiscountExceptionApprove, SalesPermissionManifestV1.OrdersPriceRead }));
        Assert.Equal(SalesDiscountDecisionDigestsV1.PreviewDigest(detail), detail.PreviewDigest);
        Assert.Equal(1, world.Store.DiscountPurposeReads);
    }

    [Theory]
    [InlineData("approval")] [InlineData("price")] [InlineData("actor")] [InlineData("company")]
    [InlineData("cancel")]
    public async Task Selected_discount_final_normal_completed_consumer_cannot_release_revoked_facts(string fault)
    {
        var (world, id) = await DiscountPurposeWorld(); var callbacks = 0;
        using var cancellation = new CancellationTokenSource();
        Task Fence(CancellationToken _)
        {
            if (++callbacks == 5)
            {
                if (fault == "approval") world.Authority.Denied.Add(SalesPermissionManifestV1.OrdersDiscountExceptionApprove);
                if (fault == "price") world.Authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead);
                if (fault == "actor") world.User.Id = "other";
                if (fault == "company") world.User.Company = Guid.NewGuid();
                if (fault == "cancel") cancellation.Cancel();
            }
            return Task.CompletedTask;
        }
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Service().ReadDiscountDecisionAsync(id, Fence, cancellation.Token));
        else
        {
            var answer = await world.Service().ReadDiscountDecisionAsync(id, Fence);
            Assert.Equal(fault == "price" ? SalesDiscountDecisionReadStateV1.Found : SalesDiscountDecisionReadStateV1.Denied, answer.State);
            if (fault == "price") { Assert.Equal(SalesPriceDisclosure.Withheld, answer.Detail!.PriceDisclosure); Assert.All(answer.Detail.Lines, x => Assert.Null(x.Pricing)); }
            else Assert.Null(answer.Detail);
        }
        Assert.Equal(5, callbacks);
    }

    [Theory]
    [InlineData("last-requester")] [InlineData("wrong-company")] [InlineData("wrong-id")]
    [InlineData("duplicate-id")] [InlineData("duplicate-line")]
    [InlineData("501-lines")] [InlineData("missing-discount")]
    public async Task Selected_discount_full_set_and_all_requester_SoD_are_fail_closed(string fault)
    {
        var (world, id) = await DiscountPurposeWorld(); var source = await world.Store.ReadDiscountDecisionSourceAsync(id, _ => Task.CompletedTask);
        Assert.NotNull(source); var first = Assert.Single(source.Lines); var second = first with { LineId = Guid.NewGuid(), LineNo = 2 };
        if (fault == "last-requester") second = second with { Commercial = first.Commercial with
        { DiscountCheck = first.Commercial.DiscountCheck! with { CheckedBy = world.User.Id } } };
        world.Store.DiscountPurposeOverride = fault switch
        {
            "wrong-company" => source with { CompanyId = Guid.NewGuid() },
            "wrong-id" => source with { DraftId = Guid.NewGuid() },
            "duplicate-id" => source with { Lines = [first, second with { LineId = first.LineId }] },
            "duplicate-line" => source with { Lines = [first, second with { LineNo = first.LineNo }] },
            "501-lines" => source with { Lines = Enumerable.Range(1, 501).Select(n => first with { LineId = Guid.NewGuid(), LineNo = n }).ToArray() },
            "missing-discount" => source with { Lines = [first with { Commercial = first.Commercial with { DiscountCheck = null } }] },
            _ => source with { Lines = [first, second] },
        };
        var answer = await world.Service().ReadDiscountDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(fault == "last-requester" ? SalesDiscountDecisionReadStateV1.Denied : SalesDiscountDecisionReadStateV1.Unavailable, answer.State);
        Assert.Null(answer.Detail);
    }

    [Fact]
    public async Task Selected_discount_exact_native_500_line_boundary_is_complete_and_not_truncated()
    {
        var (world, id) = await DiscountPurposeWorld(); var source = await world.Store.ReadDiscountDecisionSourceAsync(id, _ => Task.CompletedTask);
        Assert.NotNull(source); var first = Assert.Single(source.Lines);
        world.Store.DiscountPurposeOverride = source with { Lines = Enumerable.Range(1, SalesOrderDraft.MaximumLines)
            .Select(n => first with { LineId = Guid.NewGuid(), LineNo = n }).ToArray() };
        var answer = await world.Service().ReadDiscountDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(SalesDiscountDecisionReadStateV1.Found, answer.State); Assert.NotNull(answer.Detail);
        Assert.Equal(SalesOrderDraft.MaximumLines, answer.Detail.Lines.Count); Assert.Equal(SalesOrderDraft.MaximumLines, answer.Detail.Lines[^1].LineNo);
    }

    [Theory]
    [InlineData("unauthenticated")] [InlineData("permission")] [InlineData("membership")] [InlineData("scoped")]
    public async Task Selected_discount_admission_denial_reads_no_commercial_owner_facts(string fault)
    {
        var (world, id) = await DiscountPurposeWorld();
        if (fault == "unauthenticated") world.User.Authenticated = false;
        if (fault == "permission") world.Authority.Denied.Add(SalesPermissionManifestV1.OrdersDiscountExceptionApprove);
        if (fault == "membership") world.User.Company = Guid.NewGuid();
        if (fault == "scoped") world.Authority.CompanyWide = false;
        var answer = await world.Service().ReadDiscountDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(SalesDiscountDecisionReadStateV1.Denied, answer.State); Assert.Null(answer.Detail);
        Assert.Equal(0, world.Store.DiscountPurposeReads);
    }

    [Fact]
    public async Task Selected_discount_missing_and_initial_cancellation_do_not_fabricate_a_preview()
    {
        var world = new World(); world.Bindings = world.Bindings with { DiscountExceptionApprovePermission = SalesPermissionManifestV1.OrdersDiscountExceptionApprove, PriceReadPermission = SalesPermissionManifestV1.OrdersPriceRead };
        var missing = await world.Service().ReadDiscountDecisionAsync(Guid.NewGuid(), _ => Task.CompletedTask);
        Assert.Equal(SalesDiscountDecisionReadStateV1.NotFound, missing.State); Assert.Null(missing.Detail);
        var before = world.Authority.Calls; using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Service().ReadDiscountDecisionAsync(Guid.NewGuid(), _ => throw new InvalidOperationException(), cancel.Token));
        Assert.Equal(before, world.Authority.Calls); Assert.Equal(1, world.Store.DiscountPurposeReads);
    }

    [Fact]
    public async Task Selected_discount_detaches_external_lines_before_validation_and_uses_that_copy_after_the_final_consumer()
    {
        var (world, id) = await DiscountPurposeWorld(); var original = await world.Store.ReadDiscountDecisionSourceAsync(id, _ => Task.CompletedTask);
        Assert.NotNull(original); var first = Assert.Single(original.Lines); var mutable = original.Lines.ToList();
        world.Store.DiscountPurposeOverride = original with { Lines = mutable }; var callbacks = 0;
        var answer = await world.Service().ReadDiscountDecisionAsync(id, _ =>
        {
            if (++callbacks == 5) { mutable.Clear(); mutable.Add(first with { Quantity = "999", Commercial = first.Commercial with
                { DiscountCheck = first.Commercial.DiscountCheck! with { CheckedBy = world.User.Id } } }); }
            return Task.CompletedTask;
        });
        Assert.Equal(SalesDiscountDecisionReadStateV1.Found, answer.State); var detail = Assert.IsType<SalesDiscountDecisionDetailV1>(answer.Detail);
        Assert.Equal(first.Quantity, Assert.Single(detail.Lines).Quantity); Assert.Equal(first, Assert.Single(detail.Source!.Lines));
        Assert.Equal(SalesDiscountDecisionDigestsV1.SourceFactsDigest(detail.Source), detail.SourceFactsDigest);
        var guarded = Assert.IsAssignableFrom<IList<SalesDiscountDecisionSourceLineV1>>(detail.Source.Lines);
        Assert.Throws<NotSupportedException>(() => guarded[0] = mutable[0]);
        Assert.NotEqual(world.User.Id, detail.Lines[0].DiscountCheck!.CheckedBy); Assert.Equal(5, callbacks);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Selected_discount_native_list_presence_alone_decides_the_self_approval_exception(bool listed)
    {
        var (world, id) = await DiscountPurposeWorld(listed: listed); world.User.Id = Actor;
        var answer = await world.Service().ReadDiscountDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(listed ? SalesDiscountDecisionReadStateV1.Denied : SalesDiscountDecisionReadStateV1.Found, answer.State);
        if (listed) Assert.Null(answer.Detail);
        else { Assert.NotNull(answer.Detail); Assert.True(answer.Detail.ApprovalSupported); Assert.Equal(Actor, answer.Detail.Lines[0].DiscountCheck!.CheckedBy); }
    }

    [Fact]
    public async Task Selected_discount_last_approval_await_cannot_retain_a_removed_native_price_read()
    {
        var (world, id) = await DiscountPurposeWorld(); var prices = 0; var revoked = false;
        world.Authority.OnRead = (permission, _) =>
        {
            if (permission == SalesPermissionManifestV1.OrdersPriceRead) prices++;
            if (permission == SalesPermissionManifestV1.OrdersDiscountExceptionApprove && prices == 2)
            { world.Authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead); revoked = true; }
        };
        var answer = await world.Service().ReadDiscountDecisionAsync(id, _ => Task.CompletedTask);
        Assert.True(revoked); Assert.Equal(3, prices); Assert.Equal(SalesDiscountDecisionReadStateV1.Found, answer.State);
        Assert.Equal(SalesPriceDisclosure.Withheld, answer.Detail!.PriceDisclosure);
        Assert.All(answer.Detail.Lines, line => { Assert.Null(line.Pricing); Assert.Null(line.DiscountCheck!.ListUnitPrice); });
    }

    private static async Task<(World World, Guid Id)> DiscountPurposeWorld(bool creatorApproves = false, bool listed = true)
    {
        var world = new World { PriceReferences = new FakePriceReferences { Price = 12.50m } }.AllowDiscountsUpTo(10m);
        world.Bindings = world.Bindings with { DiscountExceptionApprovePermission = SalesPermissionManifestV1.OrdersDiscountExceptionApprove, PriceReadPermission = SalesPermissionManifestV1.OrdersPriceRead };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("SENTETIK-discount-purpose-create"))).Detail;
        if (creatorApproves) world.User.Id = "SENTETIK-different-commercial-requester";
        await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(created.Lines[0].LineId, "11", KgUnit, null, listed ? new SalesDraftPriceReferenceRequestV1(PriceList, 1) : null)),
            Metadata("SENTETIK-discount-purpose-price"));
        world.User.Id = creatorApproves ? Actor : "SENTETIK-independent-discount-approver";
        return (world, created.Draft.Id);
    }

    // Explicit synthetic selected-read protocol. It proves owner fences/withholding; it is not a SQL/transaction test.
    private sealed partial class FakeStore : ISalesDiscountDecisionReadStoreV1
    {
        internal int DiscountPurposeReads; internal SalesDiscountDecisionSourceV1? DiscountPurposeOverride;
        public async Task<SalesDiscountDecisionSourceV1?> ReadDiscountDecisionSourceAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken ct = default)
        {
            await fence(ct); DiscountPurposeReads++;
            var draft = Drafts.SingleOrDefault(x => x.Id == id);
            var source = DiscountPurposeOverride ?? (draft is null ? null : new SalesDiscountDecisionSourceV1(draft.CompanyId, draft.Id,
                draft.DocumentNo, draft.Version, draft.Revision, draft.State.ToString(), draft.ApprovalState.ToString(), draft.Header.PartyId,
                draft.Header.CompanyAccountId, draft.Header.CurrencyId, draft.Header.BusinessDate, draft.IsSubcontractService,
                draft.Lines.Select(x => new SalesDiscountDecisionSourceLineV1(x.Id, x.LineNo, x.Product.ProductId, x.Product.ProductVersion,
                    x.Product.Code, x.Product.Name, x.OrderUnit.UnitId, x.QuantityText, x.Commercial)).ToArray()));
            await fence(ct); return source;
        }
        internal SalesDraftReplayResult DiscountPurposeStoredResult(string key) => _attempts[(SalesDraftCommands.ApproveDiscountExceptionsV1, key)].Result!;
    }
}
