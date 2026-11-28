using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Floor_version_source_uses_genuine_original_receipt_when_the_approver_is_todays_commercial_requester()
    {
        var f = await FloorReceiptFixture(canonicalVersion: true); var receipt = Assert.IsType<SalesPriceFloorOriginalReceiptV1>(FloorRestore(f));
        // The same approver legitimately prepares a later commercial snapshot: native NEW approval now denies them.
        await f.World.Service().ReviseCommercialAsync(f.Response.Detail.Draft.Id, Revision(f.Response.Detail.Draft.Version,
            new SalesDraftLineCommercialV1(f.Response.Detail.Lines[0].LineId, "10.9", KgUnit, null,
                new SalesDraftPriceReferenceRequestV1(PriceList, 1))), Metadata("SENTETIK-self-later-commercial"));
        f.World.Store.FloorOriginalOverride = new(SalesPriceFloorDecisionReadStateV1.Found, receipt);
        var current = await f.World.Service().ReadPriceFloorDecisionAsync(receipt.DraftId, _ => Task.CompletedTask);
        Assert.Equal(SalesPriceFloorDecisionReadStateV1.Denied, current.State);
        var before = f.World.Store.FloorPurposeReads; var source = FloorPurposeSource(f.World); var context = FloorPurposeContext(f.World);
        var selected = await source.ReadDecisionVersionAsync(context, receipt.DraftId, f.Request.ExpectedVersion, default);
        Assert.NotNull(selected); Assert.False(selected.Pending); Assert.True(selected.SalesPriceFloorProof!.Historical);
        Assert.Equal(before, f.World.Store.FloorPurposeReads); Assert.False(selected.Item.Excludes(context.UserId));
        Assert.Equal(receipt.ExpectedVersion.ToString(CultureInfo.InvariantCulture), selected.SourceVersion);
        Assert.All(selected.Item.Rules, rule => { Assert.Null(rule.Threshold); Assert.Null(rule.Actual); Assert.Null(rule.Deviation); Assert.Null(rule.Unit); });
        var replay = await f.World.Service().ApprovePriceFloorExceptionsAsync(receipt.DraftId, f.Request,
            Metadata("SENTETIK-floor-receipt", correlation: "retry-after-own-reprice"));
        var provenReplay = replay with { OriginalFloorApprovalReceipt = Assert.IsType<SalesPriceFloorOriginalReceiptV1>(FloorRestore(f, replay)) };
        Assert.True(SalesPriceFloorDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, provenReplay, f.Request, "SENTETIK-floor-receipt", context.UserId));
        Assert.Single(f.World.Store.Transitions, x => x.Command == SalesDraftCommands.ApprovePriceFloorExceptionsV1);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(selected, SalesDraftJson.Options));
        Assert.False(json.RootElement.TryGetProperty("salesPriceFloorProof", out _));
    }

    [Theory]
    [InlineData("key")] [InlineData("actor")] [InlineData("payload")] [InlineData("version")]
    [InlineData("caption")] [InlineData("reference")] [InlineData("requester")]
    [InlineData("rule")] [InlineData("digest")]
    public async Task Floor_historical_binding_requires_actual_POST_identity_and_exact_original_source_facts(string fault)
    {
        var f = await FloorReceiptFixture(canonicalVersion: true); var receipt = Assert.IsType<SalesPriceFloorOriginalReceiptV1>(FloorRestore(f));
        var proof = SalesPriceFloorDecisionBindingV1.Proof(receipt); var item = SalesPriceFloorDecisionBindingV1.Item(receipt)!;
        var request = f.Request; var key = "SENTETIK-floor-receipt"; var actor = f.Envelope.ActorId;
        if (fault == "key") key = "another-key";
        if (fault == "actor") actor = "another-actor";
        if (fault == "payload") request = request with { Lines = request.Lines.Select(x => x with { Reason = "another reason" }).ToArray() };
        if (fault == "version") request = request with { ExpectedVersion = (f.Expected + 1).ToString(CultureInfo.InvariantCulture) };
        if (fault == "caption") item = item with { DocumentLabel = "another document" };
        if (fault == "reference") item = item with { References = item.References.Select((x, i) => i == 0 ? x with { RecordId = Guid.NewGuid() } : x).ToArray() };
        if (fault == "requester") proof = proof with { Lines = proof.Lines.Select(x => x with { RequestedBy = "another-requester" }).ToArray() };
        if (fault == "rule") item = item with { Rules = [new(SalesPriceFloorExceptionWorkInboxSourceV1.BelowFloorRule, "1", "0", "1", "TRY")] };
        if (fault == "digest") proof = proof with { SourceFactsDigest = new('a', 64) };
        var snapshot = new WorkApprovalDecisionSnapshotV1(Company, item, f.Request.ExpectedVersion, false) { SalesPriceFloorProof = proof };
        Assert.False(SalesPriceFloorDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot,
            f.Response with { OriginalFloorApprovalReceipt = receipt }, request, key, actor));
        // A post-native proof refusal does not pretend the already-executed native decision rolled back.
        Assert.Single(f.World.Store.Transitions, x => x.Command == SalesDraftCommands.ApprovePriceFloorExceptionsV1);
    }

    [Theory]
    [InlineData("legacy")] [InlineData("ambiguous")] [InlineData("foreign")]
    [InlineData("actor")] [InlineData("version")] [InlineData("digest")]
    public async Task Floor_history_missing_or_malformed_provenance_never_falls_back_to_a_later_current_maker(string fault)
    {
        var f = await FloorReceiptFixture(canonicalVersion: true); var receipt = Assert.IsType<SalesPriceFloorOriginalReceiptV1>(FloorRestore(f));
        var result = new SalesPriceFloorOriginalVersionReadResultV1(SalesPriceFloorDecisionReadStateV1.Found, receipt);
        if (fault == "legacy") result = new(SalesPriceFloorDecisionReadStateV1.NotFound, null) { CurrentVersion = f.Expected + 1 };
        if (fault == "ambiguous") result = new(SalesPriceFloorDecisionReadStateV1.Unavailable, null);
        if (fault == "foreign") result = result with { Receipt = receipt with { CompanyId = Guid.NewGuid() } };
        if (fault == "actor") result = result with { Receipt = receipt with { ActorId = "other" } };
        if (fault == "version") result = result with { Receipt = receipt with { ExpectedVersion = f.Expected + 1 } };
        if (fault == "digest") result = result with { Receipt = receipt with { OriginalDecisionDigest = new('b', 64) } };
        f.World.Store.FloorOriginalOverride = result; var before = f.World.Store.FloorPurposeReads;
        await Assert.ThrowsAsync<InvalidOperationException>(() => FloorPurposeSource(f.World).ReadDecisionVersionAsync(FloorPurposeContext(f.World),
            receipt.DraftId, f.Request.ExpectedVersion, default));
        Assert.Equal(before, f.World.Store.FloorPurposeReads);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Floor_history_final_completed_consumer_revocation_or_cancellation_releases_no_receipt(bool cancel)
    {
        var f = await FloorReceiptFixture(canonicalVersion: true); var receipt = Assert.IsType<SalesPriceFloorOriginalReceiptV1>(FloorRestore(f));
        f.World.Store.FloorOriginalOverride = new(SalesPriceFloorDecisionReadStateV1.Found, receipt);
        using var cancellation = new CancellationTokenSource(); var calls = 0;
        Task Fence(CancellationToken _)
        { if (++calls == 4) { if (cancel) cancellation.Cancel(); else f.World.Authority.Denied.Add(SchedulePermission); } return Task.CompletedTask; }
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.World.Service().ReadPriceFloorOriginalVersionAsync(receipt.DraftId, f.Request.ExpectedVersion, Fence, cancellation.Token));
        else { var result = await f.World.Service().ReadPriceFloorOriginalVersionAsync(receipt.DraftId, f.Request.ExpectedVersion, Fence); Assert.Equal(SalesPriceFloorDecisionReadStateV1.Denied, result.State); Assert.Null(result.Receipt); }
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task Floor_current_source_binds_full_owner_rules_and_refs_but_scalar_proof_alone_does_not_certify_amounts()
    {
        var (world, id) = await FloorPurposeWorld();
        var detail = (await world.Service().ReadPriceFloorDecisionAsync(id, _ => Task.CompletedTask)).Detail!;
        var item = SalesPriceFloorDecisionBindingV1.Item(detail)!; var proof = SalesPriceFloorDecisionBindingV1.Proof(detail);
        var snapshot = new WorkApprovalDecisionSnapshotV1(Company, item, detail.Version, proof.Pending) { SalesPriceFloorProof = proof };
        Assert.True(SalesPriceFloorDecisionBindingV1.MatchesDecisionSnapshot(snapshot, detail));
        var changed = item with { Rules = item.Rules.Select(x => x with { Actual = "999" }).ToArray() };
        Assert.True(SalesPriceFloorDecisionBindingV1.MatchesDecisionProof(changed, proof)); // structural only, deliberately not monetary certification
        Assert.False(SalesPriceFloorDecisionBindingV1.MatchesDecisionItem(changed, detail));
        Assert.False(SalesPriceFloorDecisionBindingV1.MatchesDecisionSnapshot(snapshot, detail with { DocumentNo = "other" }));
        var source = FloorPurposeSource(world); var current = await source.ReadDecisionVersionAsync(FloorPurposeContext(world), id, detail.Version, default);
        Assert.NotNull(current); Assert.True(current.Pending); Assert.False(current.SalesPriceFloorProof!.Historical);
        Assert.Equal(SalesWorkInboxItemsV1.OrderLink(id), current.Item.Link);
    }

    [Fact]
    public void Floor_pending_list_preserves_metadata_and_excludes_every_native_requester_without_duplicate_actors()
    {
        var reference = SalesWorkInboxTestDoubles.Reference("100", "95");
        var first = new GoymenERP.Modules.Sales.Domain.Drafts.CommercialDraftFields("90.5", SalesWorkInboxTestDoubles.PriceUnit, null, reference,
            FloorException: new("requester-A", SalesWorkInboxTestDoubles.T0, reference.PriceListId, reference.VersionNo, reference.PriceUnitId, "90.5", "90.5", "95"));
        var other = first with { FloorException = first.FloorException! with { RequestedBy = "requester-B", RequestedAtUtc = SalesWorkInboxTestDoubles.T0.AddMinutes(1) } };
        var draft = SalesWorkInboxTestDoubles.Draft(1, [SalesWorkInboxTestDoubles.Line(1, first), SalesWorkInboxTestDoubles.Line(2, other), SalesWorkInboxTestDoubles.Line(3, other)]);
        var result = SalesPriceFloorExceptionWorkInboxSourceV1.Project(SalesWorkInboxTestDoubles.Company, [draft], 10, true);
        var item = Assert.Single(result.Items); Assert.Equal("requester-A", item.PreparedBy);
        Assert.Equal("requester-B", Assert.Single(item.ExcludedDeciders!)); Assert.True(item.Excludes("requester-A")); Assert.True(item.Excludes("requester-B"));
        Assert.False(item.Excludes("approver-C")); Assert.Equal(3, item.Rules.Count); Assert.False(result.HasMore);
    }

    [Fact]
    public async Task Floor_source_final_price_disclosure_reduces_rules_and_context_drift_releases_nothing()
    {
        foreach (var drift in new[] { false, true })
        {
            var (world, id) = await FloorPurposeWorld(); var prices = 0;
            world.Authority.OnRead = (permission, _) =>
            {
                if (permission != PriceReadPermission) return;
                prices++;
                if (!drift && prices == 3) world.Authority.Denied.Add(PriceReadPermission);
                if (drift && prices == 4) world.User.Id = "other";
            };
            var source = FloorPurposeSource(world); var context = FloorPurposeContext(world);
            if (drift) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(context, id, default));
            else { var result = await source.ReadDecisionAsync(context, id, default); Assert.NotNull(result); Assert.False(result.SalesPriceFloorProof!.PriceDisclosed); Assert.All(result.Item.Rules, x => Assert.Null(x.Actual)); }
            Assert.Equal(4, prices);
        }
    }

    private static WorkInboxSourceContextV1 FloorPurposeContext(World world) => new(Company, world.User.Id, "SENTETIK-floor-work", null);
    private static SalesPriceFloorExceptionWorkInboxSourceV1 FloorPurposeSource(World world)
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(new SalesWorkInboxTestDoubles.Store { Failure = new InvalidOperationException("No list fallback") }, company,
            world.User, world.Authority, world.Service());
    }

    private sealed partial class FakeStore
    {
        internal SalesPriceFloorOriginalVersionReadResultV1? FloorOriginalOverride;
        public async Task<SalesPriceFloorOriginalVersionReadResultV1> ReadPriceFloorOriginalVersionAsync(Guid id, long version, string actor,
            Func<CancellationToken, Task> fence, CancellationToken ct = default)
        {
            // Synthetic selected-read seam only; actual immutable joins/transactions are exercised by the SQL source.
            await fence(ct);
            var result = FloorOriginalOverride ?? new(SalesPriceFloorDecisionReadStateV1.NotFound, null)
            { CurrentVersion = Drafts.SingleOrDefault(x => x.Id == id)?.Version };
            await fence(ct); return result;
        }
    }
}
