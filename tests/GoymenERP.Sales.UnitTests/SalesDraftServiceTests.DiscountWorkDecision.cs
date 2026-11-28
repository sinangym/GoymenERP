using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Discount_version_source_uses_genuine_original_receipt_when_the_approver_is_todays_commercial_requester()
    {
        var f = await DiscountReceiptFixture(canonicalVersion: true); var receipt = Assert.IsType<SalesDiscountOriginalReceiptV1>(DiscountRestore(f));
        // The same approver legitimately prepares a later commercial snapshot: native NEW approval now denies them.
        await f.World.Service().ReviseCommercialAsync(f.Response.Detail.Draft.Id, Revision(f.Response.Detail.Draft.Version,
            new SalesDraftLineCommercialV1(f.Response.Detail.Lines[0].LineId, "10.9", KgUnit, null,
                new SalesDraftPriceReferenceRequestV1(PriceList, 1))), Metadata("SENTETIK-self-later-commercial"));
        f.World.Store.DiscountOriginalOverride = new(SalesDiscountDecisionReadStateV1.Found, receipt);
        var current = await f.World.Service().ReadDiscountDecisionAsync(receipt.DraftId, _ => Task.CompletedTask);
        Assert.Equal(SalesDiscountDecisionReadStateV1.Denied, current.State);
        var before = f.World.Store.DiscountPurposeReads; var source = DiscountPurposeSource(f.World); var context = DiscountPurposeContext(f.World);
        var selected = await source.ReadDecisionVersionAsync(context, receipt.DraftId, f.Request.ExpectedVersion, default);
        Assert.NotNull(selected); Assert.False(selected.Pending); Assert.True(selected.SalesDiscountProof!.Historical);
        Assert.Equal(before, f.World.Store.DiscountPurposeReads); Assert.False(selected.Item.Excludes(context.UserId));
        Assert.Equal(receipt.ExpectedVersion.ToString(CultureInfo.InvariantCulture), selected.SourceVersion);
        Assert.All(selected.Item.Rules, rule => { Assert.Equal("10", rule.Threshold); Assert.Null(rule.Actual); Assert.Null(rule.Deviation); Assert.Equal("%", rule.Unit); });
        var replay = await f.World.Service().ApproveDiscountExceptionsAsync(receipt.DraftId, f.Request,
            Metadata("SENTETIK-discount-receipt", correlation: "retry-after-own-reprice"));
        var provenReplay = replay with { OriginalDiscountApprovalReceipt = Assert.IsType<SalesDiscountOriginalReceiptV1>(DiscountRestore(f, replay)) };
        Assert.True(SalesDiscountDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, provenReplay, f.Request, "SENTETIK-discount-receipt", context.UserId));
        Assert.Single(f.World.Store.Transitions, x => x.Command == SalesDraftCommands.ApproveDiscountExceptionsV1);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(selected, SalesDraftJson.Options));
        Assert.False(json.RootElement.TryGetProperty("salesDiscountProof", out _));
    }

    [Theory]
    [InlineData("key")] [InlineData("actor")] [InlineData("payload")] [InlineData("version")]
    [InlineData("caption")] [InlineData("reference")] [InlineData("requester")]
    [InlineData("rule")] [InlineData("digest")]
    public async Task Discount_historical_binding_requires_actual_POST_identity_and_exact_original_source_facts(string fault)
    {
        var f = await DiscountReceiptFixture(canonicalVersion: true); var receipt = Assert.IsType<SalesDiscountOriginalReceiptV1>(DiscountRestore(f));
        var proof = SalesDiscountDecisionBindingV1.Proof(receipt); var item = SalesDiscountDecisionBindingV1.Item(receipt)!;
        var request = f.Request; var key = "SENTETIK-discount-receipt"; var actor = f.Envelope.ActorId;
        if (fault == "key") key = "another-key";
        if (fault == "actor") actor = "another-actor";
        if (fault == "payload") request = request with { Lines = request.Lines.Select(x => x with { Reason = "another reason" }).ToArray() };
        if (fault == "version") request = request with { ExpectedVersion = (f.Expected + 1).ToString(CultureInfo.InvariantCulture) };
        if (fault == "caption") item = item with { DocumentLabel = "another document" };
        if (fault == "reference") item = item with { References = item.References.Select((x, i) => i == 0 ? x with { RecordId = Guid.NewGuid() } : x).ToArray() };
        if (fault == "requester") proof = proof with { Lines = proof.Lines.Select(x => x with { CheckedBy = "another-requester" }).ToArray() };
        if (fault == "rule") item = item with { Rules = [new(SalesDiscountExceptionWorkInboxSourceV1.OverLimitRule, "1", "0", "1", "TRY")] };
        if (fault == "digest") proof = proof with { SourceFactsDigest = new('a', 64) };
        var snapshot = new WorkApprovalDecisionSnapshotV1(Company, item, f.Request.ExpectedVersion, false) { SalesDiscountProof = proof };
        Assert.False(SalesDiscountDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot,
            f.Response with { OriginalDiscountApprovalReceipt = receipt }, request, key, actor));
        // A post-native proof refusal does not pretend the already-executed native decision rolled back.
        Assert.Single(f.World.Store.Transitions, x => x.Command == SalesDraftCommands.ApproveDiscountExceptionsV1);
    }

    [Theory]
    [InlineData("legacy")] [InlineData("ambiguous")] [InlineData("foreign")]
    [InlineData("actor")] [InlineData("version")] [InlineData("digest")]
    public async Task Discount_history_missing_or_malformed_provenance_never_falls_back_to_a_later_current_maker(string fault)
    {
        var f = await DiscountReceiptFixture(canonicalVersion: true); var receipt = Assert.IsType<SalesDiscountOriginalReceiptV1>(DiscountRestore(f));
        var result = new SalesDiscountOriginalVersionReadResultV1(SalesDiscountDecisionReadStateV1.Found, receipt);
        if (fault == "legacy") result = new(SalesDiscountDecisionReadStateV1.NotFound, null) { CurrentVersion = f.Expected + 1 };
        if (fault == "ambiguous") result = new(SalesDiscountDecisionReadStateV1.Unavailable, null);
        if (fault == "foreign") result = result with { Receipt = receipt with { CompanyId = Guid.NewGuid() } };
        if (fault == "actor") result = result with { Receipt = receipt with { ActorId = "other" } };
        if (fault == "version") result = result with { Receipt = receipt with { ExpectedVersion = f.Expected + 1 } };
        if (fault == "digest") result = result with { Receipt = receipt with { OriginalDecisionDigest = new('b', 64) } };
        f.World.Store.DiscountOriginalOverride = result; var before = f.World.Store.DiscountPurposeReads;
        await Assert.ThrowsAsync<InvalidOperationException>(() => DiscountPurposeSource(f.World).ReadDecisionVersionAsync(DiscountPurposeContext(f.World),
            receipt.DraftId, f.Request.ExpectedVersion, default));
        Assert.Equal(before, f.World.Store.DiscountPurposeReads);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Discount_history_final_completed_consumer_revocation_or_cancellation_releases_no_receipt(bool cancel)
    {
        var f = await DiscountReceiptFixture(canonicalVersion: true); var receipt = Assert.IsType<SalesDiscountOriginalReceiptV1>(DiscountRestore(f));
        f.World.Store.DiscountOriginalOverride = new(SalesDiscountDecisionReadStateV1.Found, receipt);
        using var cancellation = new CancellationTokenSource(); var calls = 0;
        Task Fence(CancellationToken _)
        { if (++calls == 4) { if (cancel) cancellation.Cancel(); else f.World.Authority.Denied.Add(SalesPermissionManifestV1.OrdersDiscountExceptionApprove); } return Task.CompletedTask; }
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.World.Service().ReadDiscountOriginalVersionAsync(receipt.DraftId, f.Request.ExpectedVersion, Fence, cancellation.Token));
        else { var result = await f.World.Service().ReadDiscountOriginalVersionAsync(receipt.DraftId, f.Request.ExpectedVersion, Fence); Assert.Equal(SalesDiscountDecisionReadStateV1.Denied, result.State); Assert.Null(result.Receipt); }
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task Discount_current_source_binds_full_owner_rules_and_refs_but_scalar_proof_alone_does_not_certify_amounts()
    {
        var (world, id) = await DiscountPurposeWorld();
        var detail = (await world.Service().ReadDiscountDecisionAsync(id, _ => Task.CompletedTask)).Detail!;
        var item = SalesDiscountDecisionBindingV1.Item(detail)!; var proof = SalesDiscountDecisionBindingV1.Proof(detail);
        var snapshot = new WorkApprovalDecisionSnapshotV1(Company, item, detail.Version, proof.Pending) { SalesDiscountProof = proof };
        Assert.True(SalesDiscountDecisionBindingV1.MatchesDecisionSnapshot(snapshot, detail));
        var changed = item with { Rules = item.Rules.Select(x => x with { Actual = "999" }).ToArray() };
        Assert.True(SalesDiscountDecisionBindingV1.MatchesDecisionProof(changed, proof)); // structural only, deliberately not monetary certification
        Assert.False(SalesDiscountDecisionBindingV1.MatchesDecisionItem(changed, detail));
        Assert.False(SalesDiscountDecisionBindingV1.MatchesDecisionSnapshot(snapshot, detail with { DocumentNo = "other" }));
        var source = DiscountPurposeSource(world); var current = await source.ReadDecisionVersionAsync(DiscountPurposeContext(world), id, detail.Version, default);
        Assert.NotNull(current); Assert.True(current.Pending); Assert.False(current.SalesDiscountProof!.Historical);
        Assert.Equal(SalesWorkInboxItemsV1.OrderLink(id), current.Item.Link);
    }

    [Fact]
    public void Discount_pending_list_excludes_all_listed_checkers_but_preserves_unlisted_self_approval()
    {
        var reference = SalesWorkInboxTestDoubles.Reference("100", null);
        var raw = new GoymenERP.Modules.Sales.Domain.Drafts.CommercialDraftFields("90", SalesWorkInboxTestDoubles.PriceUnit, null, reference);
        var first = SalesOrderDraft.WithDiscountLimitCheck(raw, SalesDiscountLimit.Defined(5m, DiscountGroupA, 1), "checker-A", SalesWorkInboxTestDoubles.T0);
        var second = SalesOrderDraft.WithDiscountLimitCheck(raw, SalesDiscountLimit.Undefined, "checker-B", SalesWorkInboxTestDoubles.T0.AddMinutes(1));
        var unlisted = SalesOrderDraft.WithDiscountLimitCheck(raw with { Reference = null }, SalesDiscountLimit.Undefined, "checker-C", SalesWorkInboxTestDoubles.T0.AddMinutes(-1));
        var draft = SalesWorkInboxTestDoubles.Draft(1, [SalesWorkInboxTestDoubles.Line(1, first), SalesWorkInboxTestDoubles.Line(2, second), SalesWorkInboxTestDoubles.Line(3, unlisted)]);
        var result = SalesDiscountExceptionWorkInboxSourceV1.Project(SalesWorkInboxTestDoubles.Company, [draft], 10, true);
        var item = Assert.Single(result.Items); Assert.Equal("checker-A", item.PreparedBy); Assert.Equal("checker-B", Assert.Single(item.ExcludedDeciders!));
        Assert.True(item.Excludes("checker-A")); Assert.True(item.Excludes("checker-B")); Assert.False(item.Excludes("checker-C"));
        Assert.Equal(SalesWorkInboxTestDoubles.T0.AddMinutes(-1), item.WaitingSinceUtc); Assert.Equal(3, item.Rules.Count);
        var onlyUnlisted = draft with { Lines = [SalesWorkInboxTestDoubles.Line(1, unlisted)] };
        var own = Assert.Single(SalesDiscountExceptionWorkInboxSourceV1.Project(SalesWorkInboxTestDoubles.Company, [onlyUnlisted], 10, false).Items);
        Assert.Null(own.PreparedBy); Assert.Null(own.ExcludedDeciders); Assert.False(own.Excludes("checker-C"));
    }

    [Fact]
    public async Task Discount_source_final_price_disclosure_reduces_rules_and_context_drift_releases_nothing()
    {
        foreach (var drift in new[] { false, true })
        {
            var (world, id) = await DiscountPurposeWorld(); var prices = 0;
            world.Authority.OnRead = (permission, _) =>
            {
                if (permission != SalesPermissionManifestV1.OrdersPriceRead) return;
                prices++;
                if (!drift && prices == 3) world.Authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead);
                if (drift && prices == 4) world.User.Id = "other";
            };
            var source = DiscountPurposeSource(world); var context = DiscountPurposeContext(world);
            if (drift) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(context, id, default));
            else { var result = await source.ReadDecisionAsync(context, id, default); Assert.NotNull(result); Assert.False(result.SalesDiscountProof!.PriceDisclosed); Assert.All(result.Item.Rules, x => Assert.Null(x.Actual)); }
            Assert.Equal(4, prices);
        }
    }

    [Fact]
    public async Task Discount_selected_source_without_the_guarded_owner_never_falls_back_to_the_pending_list()
    {
        var (world, id) = await DiscountPurposeWorld(); var company = new CompanyContext(); company.Set(Company);
        var oldConstructor = new SalesDiscountExceptionWorkInboxSourceV1(new SalesWorkInboxTestDoubles.Store
            { Failure = new InvalidOperationException("No pending fallback") }, company, world.User, world.Authority);
        await Assert.ThrowsAsync<InvalidOperationException>(() => oldConstructor.ReadDecisionAsync(DiscountPurposeContext(world), id, default));
        Assert.Equal(0, world.Store.DiscountPurposeReads);
    }

    private static WorkInboxSourceContextV1 DiscountPurposeContext(World world) => new(Company, world.User.Id, "SENTETIK-discount-work", null);
    private static SalesDiscountExceptionWorkInboxSourceV1 DiscountPurposeSource(World world)
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(new SalesWorkInboxTestDoubles.Store { Failure = new InvalidOperationException("No list fallback") }, company,
            world.User, world.Authority, world.Service());
    }

    private sealed partial class FakeStore
    {
        internal SalesDiscountOriginalVersionReadResultV1? DiscountOriginalOverride;
        public async Task<SalesDiscountOriginalVersionReadResultV1> ReadDiscountOriginalVersionAsync(Guid id, long version, string actor,
            Func<CancellationToken, Task> fence, CancellationToken ct = default)
        {
            // Synthetic selected-read seam only; actual immutable joins/transactions are exercised by the SQL source.
            await fence(ct);
            var result = DiscountOriginalOverride ?? new(SalesDiscountDecisionReadStateV1.NotFound, null)
            { CurrentVersion = Drafts.SingleOrDefault(x => x.Id == id)?.Version };
            await fence(ct); return result;
        }
    }
}
