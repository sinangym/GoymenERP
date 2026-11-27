using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class ManufacturingCostDifferenceV1Tests
{
    [Theory] [InlineData("Prepared", 2L)] [InlineData("Prepared", long.MaxValue)] [InlineData("Posted", 3L)]
    public async Task Cost_work_source_uses_actual_owner_view_version_maker_and_native_links_without_serializing_private_facts(string status, long version)
    {
        var source = DecisionSource(); var p = source.Proposal with { Status = status, Version = version,
            ApprovedBy = status == "Posted" ? Checker : null, JournalId = status == "Posted" ? Guid.NewGuid() : null };
        source = source with { Proposal = p }; var detail = CostWorkDetail(source);
        var company = new CostCompany(); var user = new CostUser(); var authority = new CostAuthority(); await using var db = CostDb(company);
        var owner = new CostWorkOwner(detail); var adapter = new AccountingManufacturingCostDifferenceWorkInboxSourceV1(db, new(company, user, authority), owner);
        var snapshot = (await adapter.ReadDecisionAsync(CostWorkContext(), EventId, default))!;
        Assert.Equal(version.ToString(CultureInfo.InvariantCulture), snapshot.SourceVersion); Assert.Equal(status == "Prepared", snapshot.Pending);
        var proof = Assert.IsType<WorkManufacturingCostDifferenceProofV1>(snapshot.ManufacturingCostDifferenceProof);
        Assert.Equal(Company, proof.CompanyId); Assert.Equal(Maker, proof.PreparedBy); Assert.Equal(p.ManufacturingOrderId, proof.ManufacturingOrderId);
        Assert.True(AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionProof(snapshot.Item, proof));
        Assert.True(AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail));
        Assert.Equal(AccountingWorkInboxFence.Link(AccountingManufacturingCostDifferenceWorkInboxSourceV1.WorkDecisionPath, snapshot.Item.ItemId), snapshot.Item.Link);
        Assert.Equal("600", Assert.Single(snapshot.Item.Rules).Actual); Assert.Equal("TRY", snapshot.Item.Rules[0].Unit);
        Assert.Equal(new[] { "manufacturing-order", "period", "debit-ledger-account", "credit-ledger-account" }, snapshot.Item.References.Select(x => x.Role));
        Assert.Null(snapshot.Item.Scope); Assert.Null(snapshot.Item.ExcludedDeciders); Assert.True(snapshot.Item.Excludes(Maker.ToString("D")));
        var json = JsonSerializer.Serialize(snapshot, Json); Assert.DoesNotContain("manufacturingCostDifferenceProof", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(proof.SourceFactsDigest, json); Assert.DoesNotContain("SENTETIK debit", json); Assert.DoesNotContain("sourceJson", json);
        Assert.Equal(1, owner.Reads); Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(ManufacturingCostDifferenceServiceV1.CostDifferencePreviewDigest(source), ManufacturingCostDifferenceServiceV1.CostDifferencePreviewDigest(p, source.PreparedAtUtc));
    }

    [Theory] [InlineData("company")] [InlineData("id")] [InlineData("preview")] [InlineData("version")] [InlineData("status")]
    [InlineData("source-digest")] [InlineData("plan")] [InlineData("output")] [InlineData("null-view")]
    public async Task Cost_work_source_does_not_replace_malformed_owner_answer_with_a_local_scalar_record(string fault)
    {
        var detail = CostWorkDetail(DecisionSource()); detail = fault switch
        {
            "company" => detail with { Proposal = detail.Proposal with { CompanyId = Guid.NewGuid() } },
            "id" => detail with { Proposal = detail.Proposal with { ProposalId = Guid.NewGuid() } },
            "preview" => detail with { PreviewDigest = new('A', 64) }, "version" => detail with { Proposal = detail.Proposal with { Version = 0 } },
            "status" => detail with { Proposal = detail.Proposal with { Status = "AwaitingPreparation" } },
            "source-digest" => detail with { SourceFactsDigest = "unestablished" }, "plan" => detail with { Proposal = detail.Proposal with { AccountPlan = null } },
            "output" => detail with { Proposal = detail.Proposal with { Outputs = [] } }, _ => detail with { Proposal = null! },
        };
        var company = new CostCompany(); await using var db = CostDb(company);
        var source = new AccountingManufacturingCostDifferenceWorkInboxSourceV1(db, new(company, new CostUser(), new CostAuthority()), new CostWorkOwner(detail));
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(CostWorkContext(), EventId, default));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("cancel")]
    public async Task Cost_work_source_rechecks_native_owner_await_context_and_completed_cancellation(string fault)
    {
        var company = new CostCompany(); var user = new CostUser(); var authority = new CostAuthority(); using var cancel = new CancellationTokenSource();
        var owner = new CostWorkOwner(CostWorkDetail(DecisionSource())) { AfterRead = () =>
        {
            if (fault == "permission") authority.Granted = false; if (fault == "company") company.Set(Guid.NewGuid());
            if (fault == "actor") user.Actor = Maker; if (fault == "member") user.Member = false; if (fault == "cancel") cancel.Cancel();
        } };
        await using var db = CostDb(company); var source = new AccountingManufacturingCostDifferenceWorkInboxSourceV1(db, new(company, user, authority), owner);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadDecisionAsync(CostWorkContext(), EventId, cancel.Token));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(CostWorkContext(), EventId, default));
        Assert.Equal(1, owner.Reads); Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData(AccountingApiStatus.Denied)] [InlineData(AccountingApiStatus.NotFound)] [InlineData(AccountingApiStatus.Unavailable)]
    public async Task Cost_work_source_preserves_owner_refusal_classes_and_requires_registered_purpose_owner(AccountingApiStatus status)
    {
        var company = new CostCompany(); await using var db = CostDb(company); var fence = new AccountingWorkInboxFence(company, new CostUser(), new CostAuthority());
        var source = new AccountingManufacturingCostDifferenceWorkInboxSourceV1(db, fence, new CostWorkOwner(null) { Status = status });
        if (status == AccountingApiStatus.NotFound) Assert.Null(await source.ReadDecisionAsync(CostWorkContext(), EventId, default));
        else if (status == AccountingApiStatus.Denied) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(CostWorkContext(), EventId, default));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(CostWorkContext(), EventId, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AccountingManufacturingCostDifferenceWorkInboxSourceV1(db, fence).ReadDecisionAsync(CostWorkContext(), EventId, default));
    }

    [Theory] [InlineData("maker")] [InlineData("scope")] [InlineData("scoped-context")]
    public async Task Cost_work_source_keeps_maker_and_companywide_scope_refusals_without_extra_owner_access(string fault)
    {
        var sourceFacts = DecisionSource(); if (fault == "maker") sourceFacts = sourceFacts with { Proposal = sourceFacts.Proposal with { PreparedBy = Checker } };
        var owner = new CostWorkOwner(CostWorkDetail(sourceFacts)); var company = new CostCompany(); var authority = new CostAuthority { CompanyWide = fault != "scope" };
        await using var db = CostDb(company); var source = new AccountingManufacturingCostDifferenceWorkInboxSourceV1(db, new(company, new CostUser(), authority), owner);
        var context = CostWorkContext(); if (fault == "scoped-context") context = context with { GrantScope = new(new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>()) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(context, EventId, default));
        Assert.Equal(fault == "maker" ? 1 : 0, owner.Reads); Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData("module")] [InlineData("kind")] [InlineData("id")] [InlineData("caption")] [InlineData("maker")]
    [InlineData("time")] [InlineData("link")] [InlineData("actual")] [InlineData("threshold")] [InlineData("deviation")]
    [InlineData("unit")] [InlineData("ref-id")] [InlineData("ref-role")] [InlineData("ref-label")] [InlineData("ref-link")]
    [InlineData("scope")] [InlineData("excluded")]
    public async Task Cost_work_binding_checks_complete_owner_formatter_against_detail_and_actual_original_receipt(string fault)
    {
        var store = new SyntheticCostDecisionProtocolStore(); var original = await DecisionService(store).ApproveAsync(EventId, 2, "binding");
        var receipt = Assert.IsType<ManufacturingCostDifferenceApprovalReceiptV1>(original.Resource?.ApprovalReceipt);
        var detail = CostWorkDetail(store.Source!); var item = AccountingManufacturingCostDifferenceDecisionBindingV1.FromView(detail.Proposal, detail.PreparedAtUtc)!;
        Assert.True(AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionItem(item, detail));
        Assert.True(AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionItem(item, original.Resource, receipt));
        var rule = item.Rules[0]; var reference = item.References[0]; item = fault switch
        {
            "module" => item with { Module = "finance" }, "kind" => item with { Kind = "accounting.journal-reversal.v1" }, "id" => item with { ItemId = Guid.NewGuid() },
            "caption" => item with { DocumentLabel = "changed" }, "maker" => item with { PreparedBy = Checker.ToString("D") }, "time" => item with { WaitingSinceUtc = item.WaitingSinceUtc.AddSeconds(1) },
            "link" => item with { Link = "/other" }, "actual" => item with { Rules = [rule with { Actual = "601" }] }, "threshold" => item with { Rules = [rule with { Threshold = "1" }] },
            "deviation" => item with { Rules = [rule with { Deviation = "1" }] }, "unit" => item with { Rules = [rule with { Unit = "USD" }] },
            "ref-id" => item with { References = [reference with { RecordId = Guid.NewGuid() }, .. item.References.Skip(1)] },
            "ref-role" => item with { References = [reference with { Role = "other" }, .. item.References.Skip(1)] },
            "ref-label" => item with { References = [reference with { Label = "changed" }, .. item.References.Skip(1)] },
            "ref-link" => item with { References = [reference with { Link = "/other" }, .. item.References.Skip(1)] },
            "scope" => item with { Scope = new(Guid.NewGuid(), null, null) }, _ => item with { ExcludedDeciders = [Checker.ToString("D")] },
        };
        Assert.False(AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionItem(item, detail));
        Assert.False(AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionItem(item, original.Resource, receipt));
        var proof = CostWorkProof(detail);
        // This canonical altered amount cannot be validated from money-free routing scalars. The genuine owner checks above reject it.
        Assert.Equal(fault == "actual", AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionProof(item, proof));
    }

    private static WorkInboxSourceContextV1 CostWorkContext() => new(Company, Checker.ToString("D"), "synthetic-cost", null);
    private static ManufacturingCostDifferenceDecisionDetailV1 CostWorkDetail(ManufacturingCostDifferenceDecisionSourceV1 source) => new(source.Proposal,
        source.PreparedAtUtc!.Value, ManufacturingCostDifferenceServiceV1.CostDifferenceSourceFactsDigest(source), ManufacturingCostDifferenceServiceV1.CostDifferencePreviewDigest(source));
    private static WorkManufacturingCostDifferenceProofV1 CostWorkProof(ManufacturingCostDifferenceDecisionDetailV1 detail) => new(Company, EventId,
        detail.Proposal.ManufacturingOrderId, detail.Proposal.PeriodId!.Value, detail.Proposal.DebitAccountId!.Value, detail.Proposal.CreditAccountId!.Value,
        detail.Proposal.PreparedBy!.Value, detail.PreparedAtUtc, detail.Proposal.Status, detail.SourceFactsDigest, detail.PreviewDigest);
    private sealed class CostWorkOwner(ManufacturingCostDifferenceDecisionDetailV1? detail) : IManufacturingCostDifferenceDecisionReadV1
    {
        public int Reads; public Action? AfterRead; public AccountingApiStatus Status = AccountingApiStatus.Ok;
        public Task<AccountingApiResult<ManufacturingCostDifferenceDecisionDetailV1>> ReadDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { Reads++; AfterRead?.Invoke(); return Task.FromResult(new AccountingApiResult<ManufacturingCostDifferenceDecisionDetailV1>(Status, Status == AccountingApiStatus.Ok ? detail : null, false, null)); }
    }
}
