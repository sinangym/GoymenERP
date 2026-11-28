using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.WorkInbox;

namespace GoymenERP.Organization.UnitTests;

public sealed partial class OrganizationCompanyTaxIdChangeWorkInboxSourceV1Tests
{
    // Controlled owner protocol tests; the SQL partial separately uses the real guarded owner.
    [Theory]
    [InlineData("pending", false, false)] [InlineData("pending", true, false)] [InlineData("pending", false, true)]
    [InlineData("approved", true, false)] [InlineData("rejected", true, false)]
    public async Task Purpose_snapshot_preserves_native_formatter_and_current_company_version(string status, bool stale, bool first)
    {
        var detail = Detail(status, stale, first); var owner = new DecisionOwner(detail);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await DecisionSource(owner).ReadDecisionAsync(Caller(), detail.Request.Id, default));
        Assert.Equal(detail.CompanyVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), snapshot.SourceVersion);
        Assert.Equal(status == "pending", snapshot.Pending); Assert.Equal(ProposedVkn, snapshot.Item.DocumentLabel);
        Assert.Equal($"{OrganizationCompanyTaxIdDecisionBindingV1.PurposePath}/{snapshot.Item.ItemId:D}", snapshot.Item.Link);
        Assert.Equal("/ayarlar/organizasyon/sirket-vergi-no", OrganizationCompanyTaxIdChangeWorkInboxSourceV1.ScreenPath);
        Assert.Equal(stale && status == "pending", snapshot.Item.Rules.Any(x => x.RuleCode == OrganizationCompanyTaxIdChangeWorkInboxSourceV1.StaleRule));
        Assert.Equal(first, snapshot.Item.Rules.Any(x => x.RuleCode == OrganizationCompanyTaxIdChangeWorkInboxSourceV1.FirstVknRule));
        Assert.True(OrganizationCompanyTaxIdDecisionBindingV1.MatchesDecisionProof(snapshot.Item, snapshot.OrganizationCompanyTaxIdProof));
        Assert.True(OrganizationCompanyTaxIdDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail));
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("organizationCompanyTaxIdProof", json); Assert.DoesNotContain(detail.SourceFactsDigest, json);
        Assert.DoesNotContain(detail.PreviewDigest, json); Assert.DoesNotContain(detail.Request.Reason, json);
        Assert.All(asked, code => Assert.Equal(OrganizationCompanyTaxIdPermissionsV1.Approve, code));
    }

    [Theory]
    [InlineData("company")] [InlineData("id")] [InlineData("source")] [InlineData("preview")] [InlineData("reason")]
    [InlineData("is-current")] [InlineData("status")] [InlineData("company-version")]
    public async Task Purpose_refuses_malformed_or_mismatched_full_owner_facts(string fault)
    {
        var original = Detail(); var detail = fault switch
        {
            "company" => original with { CompanyId = Guid.NewGuid() },
            "id" => original with { Request = original.Request with { Id = Guid.NewGuid() } },
            "source" => original with { SourceFactsDigest = new('A', 64) },
            "preview" => original with { PreviewDigest = new('B', 64) },
            "reason" => original with { Request = original.Request with { Reason = "changed" } },
            "is-current" => original with { IsCurrent = false },
            "status" => original with { Request = original.Request with { Status = "unknown" } },
            _ => original with { CompanyVersion = 0 },
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => DecisionSource(new DecisionOwner(detail)).ReadDecisionAsync(Caller(), original.Request.Id, default));
    }

    [Theory]
    [InlineData("missing")] [InlineData("denied")] [InlineData("not-found")] [InlineData("unavailable")] [InlineData("maker")]
    public async Task Purpose_owner_status_and_maker_boundaries_are_distinct(string fault)
    {
        var detail = Detail();
        if (fault == "maker")
        {
            detail = detail with { Request = detail.Request with { RequestedBy = Caller().UserId } };
            detail = detail with { SourceFactsDigest = OrganizationCompanyTaxIdDecisionFactsV1.SourceFactsDigest(detail.CompanyId, detail.Request, detail.CompanyVersionAtRequest),
                PreviewDigest = OrganizationCompanyTaxIdDecisionFactsV1.PreviewDigest(detail) };
        }
        var owner = new DecisionOwner(detail);
        owner.Answer = fault switch
        {
            "denied" => new(OrganizationCompanyTaxIdDecisionReadStatusV1.Denied, null),
            "not-found" => new(OrganizationCompanyTaxIdDecisionReadStatusV1.NotFound, null),
            "unavailable" => new(OrganizationCompanyTaxIdDecisionReadStatusV1.Unavailable, null),
            _ => owner.Answer,
        };
        var caller = Caller();
        if (fault == "not-found") Assert.Null(await DecisionSource(owner).ReadDecisionAsync(caller, detail.Request.Id, default));
        else if (fault is "denied" or "maker") await Assert.ThrowsAsync<UnauthorizedAccessException>(() => DecisionSource(owner).ReadDecisionAsync(caller, detail.Request.Id, default));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => DecisionSource(fault == "missing" ? null : owner).ReadDecisionAsync(caller, detail.Request.Id, default));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Late_completed_owner_callback_cannot_release_cached_snapshot(bool cancel)
    {
        var detail = Detail(); using var cts = new CancellationTokenSource();
        var owner = new DecisionOwner(detail) { BeforeReturn = () => { if (cancel) cts.Cancel(); else granted = false; } };
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DecisionSource(owner).ReadDecisionAsync(Caller(), detail.Request.Id, cts.Token));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => DecisionSource(owner).ReadDecisionAsync(Caller(), detail.Request.Id, default));
    }

    [Theory]
    [InlineData("none")] [InlineData("proposed")] [InlineData("first")] [InlineData("missing-proposed")] [InlineData("missing-first")]
    [InlineData("digest")] [InlineData("maker")] [InlineData("caption")] [InlineData("threshold")] [InlineData("references")]
    public void Original_receipt_binds_primary_caption_and_first_rule_without_inventing_old_current_company_state(string fault)
    {
        var d = Detail("approved", true); var p = Proof(d);
        var item = OrganizationCompanyTaxIdDecisionBindingV1.Item(d.Request.Id, d.Request.ProposedVkn, d.Request.RequestedBy, d.Request.RequestedAtUtc, false, false, false)!;
        var r = new OrganizationCompanyTaxIdDecisionReceiptV1(CompanyA, d.Request.Id, 7, 7, 2, "approve", null, d.Request.RequestedBy,
            d.Request.RequestedAtUtc, "checker", Start, "organization.company.tax-id.change-decision.v1", new('A',64), new('B',64), "checker", "original",
            Start, d.SourceFactsDigest, new('C',64)) { ProposedVkn = d.Request.ProposedVkn, FirstVkn = false };
        if (fault == "proposed") r = r with { ProposedVkn = "9000000033" };
        if (fault == "first") r = r with { FirstVkn = true };
        if (fault == "missing-proposed") r = r with { ProposedVkn = null };
        if (fault == "missing-first") r = r with { FirstVkn = null };
        if (fault == "digest") r = r with { SourceFactsDigest = new('D',64) };
        if (fault == "maker") r = r with { RequestedBy = "other" };
        if (fault == "caption") item = item with { DocumentLabel = "other" };
        if (fault == "threshold") item = item with { Rules = [item.Rules[0] with { Threshold = "1" }] };
        if (fault == "references") item = item with { References = [new("company", "other", CompanyA, null)] };
        Assert.Equal(fault == "none", OrganizationCompanyTaxIdDecisionBindingV1.MatchesOriginalDecisionItem(item, p, r));
        var json = JsonSerializer.Serialize(r, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("proposedVkn", json); Assert.DoesNotContain("firstVkn", json);
        Assert.Equal(7, r.ExpectedCompanyVersion); Assert.Equal(long.MaxValue, p.CurrentCompanyVersion);
    }

    private OrganizationCompanyTaxIdChangeWorkInboxSourceV1 DecisionSource(IOrganizationCompanyTaxIdDecisionReadV1? owner) =>
        new(Context(), new(Resolved(), new User(), new Authority(this)), owner);
    private static OrganizationCompanyTaxIdDecisionDetailV1 Detail(string status = "pending", bool stale = false, bool first = false)
    {
        var view = new CompanyTaxIdChangeView(Guid.Parse("69000000-0000-4000-8000-000000000003"), first ? null : CurrentVkn,
            ProposedVkn, "SENTETIK-only-human-reason", status, "SENTETIK-maker", Start,
            status == "pending" ? null : "checker", status == "pending" ? null : Start, null);
        var d = new OrganizationCompanyTaxIdDecisionDetailV1(CompanyA, stale ? long.MaxValue : 7, first ? null : CurrentVkn, view, 7, !stale, "", "");
        return d with { SourceFactsDigest = OrganizationCompanyTaxIdDecisionFactsV1.SourceFactsDigest(CompanyA, view, 7),
            PreviewDigest = OrganizationCompanyTaxIdDecisionFactsV1.PreviewDigest(d) };
    }
    private static WorkOrganizationCompanyTaxIdProofV1 Proof(OrganizationCompanyTaxIdDecisionDetailV1 d) => new(d.CompanyId, d.Request.Id,
        d.CompanyVersion, d.CompanyVersionAtRequest, d.Request.RequestedBy, d.Request.RequestedAtUtc, d.Request.Status, d.IsCurrent,
        d.Request.CurrentVkn is null, d.Request.ProposedVkn, d.SourceFactsDigest, d.PreviewDigest);
    private sealed class DecisionOwner(OrganizationCompanyTaxIdDecisionDetailV1 detail) : IOrganizationCompanyTaxIdDecisionReadV1
    {
        internal OrganizationCompanyTaxIdDecisionReadResultV1 Answer = new(OrganizationCompanyTaxIdDecisionReadStatusV1.Found, detail);
        internal Action? BeforeReturn;
        public async Task<OrganizationCompanyTaxIdDecisionReadResultV1> ReadDecisionAsync(Guid requestId, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
        { await freshConsumerFence(cancellationToken); BeforeReturn?.Invoke(); return Answer; }
        public Task<OrganizationCompanyTaxIdDecisionReceiptV1?> ReadDecisionReceiptAsync(Guid requestId, DecideCompanyTaxIdChange command, string key,
            CommandExecutionResult original, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Source read must never execute or read a command receipt.");
    }
}
