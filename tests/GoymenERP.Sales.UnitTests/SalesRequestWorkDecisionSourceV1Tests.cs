using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using static GoymenERP.Sales.UnitTests.SalesWorkInboxTestDoubles;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesRequestWorkDecisionSourceV1Tests
{
    private static readonly Guid Id = Guid.Parse("5a1e0000-0000-4000-8000-000000000007");

    [Theory]
    [InlineData("Submitted", true, true)]
    [InlineData("Approved", false, true)]
    [InlineData("Rejected", false, false)]
    public async Task Exact_selected_read_keeps_queue_membership_separate_from_native_action_support(string state, bool approve, bool reject)
    {
        var detail = Detail(state); var owner = new Reader(detail); var authority = ApproveAuthority();
        var store = new SalesWorkInboxApprovalFakeStore();
        IWorkApprovalDecisionSourceV1 source = new SalesRequestWorkInboxSourceV1(store, new CompanyContext(Company), new User(Maker, Company), authority, owner);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(Context(Maker), Id, default));
        Assert.Equal((Company, detail.Detail.Request.Version, state == "Submitted"), (snapshot.CompanyId, snapshot.SourceVersion, snapshot.Pending));
        Assert.Equal((approve, reject), (snapshot.SalesRequestProof!.ApprovalSupported, snapshot.SalesRequestProof.RejectionSupported));
        Assert.True(SalesRequestDecisionBindingV1.MatchesDecisionSnapshot(snapshot, detail));
        Assert.Null(snapshot.Item.PreparedBy); Assert.Null(snapshot.Item.ExcludedDeciders); Assert.False(snapshot.Item.Excludes(Maker));
        Assert.Equal($"/satis/talepler/{Id:D}", snapshot.Item.Link);
        Assert.Equal(1, owner.Reads); Assert.Empty(store.Calls);
        Assert.All(authority.Asked, name => Assert.Equal(SalesPermissionManifestV1.RequestsApprove, name));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, SalesDraftJson.Options));
        Assert.False(json.RootElement.TryGetProperty("salesRequestProof", out _));
        Assert.DoesNotContain(detail.SourceFactsDigest, json.RootElement.GetRawText());
    }

    [Fact]
    public async Task Existing_pending_list_needs_no_new_owner_and_preserves_its_original_projection()
    {
        var store = new SalesWorkInboxApprovalFakeStore { Requests = [new(Id, Company, "SENTETIK-TLP-7", T0)] };
        var source = new SalesRequestWorkInboxSourceV1(store, new CompanyContext(Company), new User(Approver, Company), ApproveAuthority());
        var list = await source.ListPendingAsync(Context(), 10, default); var item = Assert.Single(list.Items!);
        Assert.True(SalesRequestDecisionBindingV1.MatchesDecisionItem(item, Detail("Submitted")));
        Assert.Single(store.Calls); Assert.Equal(nameof(ISalesWorkInboxApprovalStoreV1.ListSubmittedRequestsAsync), store.Calls[0].Query);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Context(), Id, default));
        Assert.Single(store.Calls); // No selected-id fallback to the pending list.
    }

    [Theory]
    [InlineData("Denied")] [InlineData("NotFound")] [InlineData("Unavailable")]
    [InlineData("null")] [InlineData("company")] [InlineData("id")]
    [InlineData("flags")] [InlineData("digest")] [InlineData("partial-lines")]
    public async Task Selected_source_rejects_unavailable_malformed_or_foreign_owner_answers(string fault)
    {
        var detail = Detail("Submitted");
        if (fault == "company") detail = Reseal(detail with { Detail = detail.Detail with { Request = detail.Detail.Request with { CompanyId = OtherCompany } } });
        if (fault == "id") detail = Reseal(detail with { Detail = detail.Detail with { Request = detail.Detail.Request with { Id = Guid.NewGuid() } } });
        if (fault == "flags") detail = Reseal(detail with { RejectionSupported = false });
        if (fault == "digest") detail = detail with { SourceFactsDigest = new('a', 64) };
        if (fault == "partial-lines") detail = Reseal(detail with { Detail = detail.Detail with { Lines = [] } });
        var owner = new Reader(detail);
        if (Enum.TryParse<SalesRequestDecisionReadStateV1>(fault, out var state)) owner.Answer = new(state, null);
        if (fault == "null") owner.Answer = null!;
        var source = Source(owner);
        if (fault == "NotFound") Assert.Null(await source.ReadDecisionAsync(Context(), Id, default));
        else if (fault == "Denied") await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Context(), Id, default));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Context(), Id, default));
        Assert.Equal(1, owner.Reads);
    }

    [Theory]
    [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("scoped")]
    public async Task Source_denies_before_owner_IO_without_new_general_or_price_read_permissions(string fault)
    {
        var owner = new Reader(Detail("Submitted"));
        var source = new SalesRequestWorkInboxSourceV1(new SalesWorkInboxApprovalFakeStore(), new CompanyContext(Company),
            new User(Approver, Company), new Authority(p => fault != "permission" && p == SalesPermissionManifestV1.RequestsApprove), owner);
        var context = Context();
        if (fault == "company") context = context with { CompanyId = OtherCompany };
        if (fault == "actor") context = context with { UserId = Maker };
        if (fault == "scoped") context = context with { GrantScope = new(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(context, Id, default)); Assert.Equal(0, owner.Reads);
    }

    [Theory]
    [InlineData("permission")] [InlineData("company")] [InlineData("cancel")]
    public async Task Normal_completed_owner_read_does_not_release_a_late_revoked_or_cancelled_snapshot(string fault)
    {
        using var cancelled = new CancellationTokenSource(); var company = new CompanyContext(Company); var granted = true;
        var owner = new Reader(Detail("Submitted")) { AfterRead = () =>
        { if (fault == "permission") granted = false; if (fault == "company") company.Set(OtherCompany); if (fault == "cancel") cancelled.Cancel(); } };
        var source = new SalesRequestWorkInboxSourceV1(new SalesWorkInboxApprovalFakeStore(), company, new User(Approver, Company),
            new Authority(p => granted && p == SalesPermissionManifestV1.RequestsApprove), owner);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadDecisionAsync(Context(), Id, cancelled.Token));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Context(), Id, default));
        Assert.Equal(1, owner.Reads);
    }

    [Theory]
    [InlineData("caption")] [InlineData("reference")] [InlineData("rule")]
    [InlineData("link")] [InlineData("kind")] [InlineData("maker")]
    public async Task Public_owner_binding_reuses_the_exact_legacy_formatter_instead_of_trusting_snapshot_text(string fault)
    {
        var detail = Detail("Approved"); var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await Source(new Reader(detail)).ReadDecisionAsync(Context(), Id, default));
        var item = snapshot.Item;
        item = fault switch
        {
            "caption" => item with { DocumentLabel = "other" },
            "reference" => item with { References = [item.References[0] with { RecordId = Guid.NewGuid() }] },
            "rule" => item with { Rules = [item.Rules[0] with { Actual = "1" }] },
            "link" => item with { Link = "/satis/faturalar" },
            "kind" => item with { Kind = "sales.invoice.v1" },
            _ => item with { PreparedBy = Maker },
        };
        Assert.False(SalesRequestDecisionBindingV1.MatchesDecisionItem(item, detail));
        Assert.False(SalesRequestDecisionBindingV1.MatchesDecisionProof(item, snapshot.SalesRequestProof));
        Assert.False(SalesRequestDecisionBindingV1.MatchesDecisionSnapshot(snapshot with { Item = item }, detail));
    }

    [Fact]
    public async Task Terminal_Int64_max_read_is_canonical_and_cannot_be_relabelled_as_pending()
    {
        var detail = Detail("Rejected"); detail = Reseal(detail with { Detail = detail.Detail with { Request = detail.Detail.Request with { Version = long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture) } } });
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await Source(new Reader(detail)).ReadDecisionAsync(Context(), Id, default));
        Assert.Equal("9223372036854775807", snapshot.SourceVersion); Assert.False(snapshot.Pending);
        Assert.False(snapshot.SalesRequestProof!.ApprovalSupported); Assert.False(snapshot.SalesRequestProof.RejectionSupported);
        Assert.False(SalesRequestDecisionBindingV1.MatchesDecisionSnapshot(snapshot with { Pending = true }, detail));
        Assert.False(SalesRequestDecisionBindingV1.TryVersion("01", out _));
    }

    [Fact]
    public async Task Full_selected_human_digest_cannot_be_replaced_by_another_valid_line_body_with_the_same_header()
    {
        var detail = Detail("Submitted"); var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await Source(new Reader(detail)).ReadDecisionAsync(Context(), Id, default));
        var changed = Reseal(detail with { Detail = detail.Detail with { Lines = [detail.Detail.Lines[0] with { Quantity = "1.250" }] } });
        Assert.True(SalesRequestDecisionBindingV1.MatchesDecisionItem(snapshot.Item, changed)); // The canonical queue formatter carries no quantity.
        Assert.NotEqual(detail.SourceFactsDigest, changed.SourceFactsDigest);
        Assert.False(SalesRequestDecisionBindingV1.MatchesDecisionSnapshot(snapshot, changed));
    }

    private static SalesRequestWorkInboxSourceV1 Source(Reader owner) => new(new SalesWorkInboxApprovalFakeStore(), new CompanyContext(Company), new User(Approver, Company), ApproveAuthority(), owner);
    private static Authority ApproveAuthority() => new(p => p == SalesPermissionManifestV1.RequestsApprove);
    private static SalesRequestDecisionDetailV1 Detail(string state)
    {
        var h = new SalesRequestSummaryView(Id, Company, "SENTETIK-TLP-7", state, state == "Submitted" ? "1" : "2", Guid.NewGuid(), Guid.NewGuid(), new(2026, 11, 28), T0, 1);
        var line = new SalesRequestLineView(Guid.NewGuid(), 1, Guid.NewGuid(), "1", "SNT", "Sentetik ürün", Guid.NewGuid(), "KG", "001.2500", null, "SENTETIK satır");
        return Reseal(new(new(h, null, "SENTETIK talep", [line], state == "Submitted" ? null : new(Maker, T0, state == "Rejected" ? "  SENTETIK ret  " : null)),
            state == "Submitted", state is "Submitted" or "Approved", "", ""));
    }
    private static SalesRequestDecisionDetailV1 Reseal(SalesRequestDecisionDetailV1 detail)
    { var d = detail with { SourceFactsDigest = SalesRequestDecisionDigestsV1.SourceFactsDigest(detail.Detail) }; return d with { PreviewDigest = SalesRequestDecisionDigestsV1.PreviewDigest(d) }; }
    // Controlled port protocol only. Real Audit/attempt provenance and SQL query translation are separate SQL-source cases.
    private sealed class Reader(SalesRequestDecisionDetailV1 detail) : ISalesRequestDecisionReadV1
    {
        internal int Reads; internal Action? AfterRead; internal SalesRequestDecisionReadResultV1 Answer = new(SalesRequestDecisionReadStateV1.Found, detail);
        public async Task<SalesRequestDecisionReadResultV1> ReadRequestDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); Reads++; AfterRead?.Invoke(); await fence(token); return Answer; }
    }
}
