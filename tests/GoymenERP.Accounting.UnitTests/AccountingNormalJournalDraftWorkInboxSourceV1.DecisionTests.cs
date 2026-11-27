using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Kit = GoymenERP.Accounting.UnitTests.AccountingWorkInboxTestKit;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingNormalJournalDraftDecisionSourceTests
{
    [Theory] [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Exact_owner_pending_and_terminal_reads_reuse_native_caption_rules_references_and_keep_the_native_link(bool terminal, bool credit)
    {
        await using var kit = new Kit(); var owner = new Owner { Detail = Detail(terminal, credit) }; var fence = kit.Fence();
        var source = new AccountingNormalJournalDraftWorkInboxSourceV1(kit.Db, fence, owner);
        var result = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(Kit.Context(), owner.Detail.Draft.DraftId, default));
        Assert.Equal(!terminal, result.Pending); Assert.Equal(terminal ? "8" : "7", result.SourceVersion);
        Assert.Equal(owner.Detail.Draft.DraftId, result.NormalJournalDraftProof!.DraftId); Assert.Equal(Kit.Maker, result.NormalJournalDraftProof.PreparedBy);
        Assert.Equal(owner.Detail.SourceFactsDigest, result.NormalJournalDraftProof.SourceFactsDigest);
        Assert.Equal(credit ? "credit-movement-proposal" : "source-record", result.Item.References[0].Role);
        Assert.Equal(credit ? 2 : 1, result.Item.Rules.Count); Assert.EndsWith(owner.Detail.Draft.DraftId.ToString("D"), result.Item.Link);
        Assert.StartsWith(AccountingNormalJournalDraftWorkInboxSourceV1.ScreenPath, result.Item.Link);
        Assert.True(AccountingNormalJournalDraftDecisionBindingV1.MatchesDecisionItem(result.Item, owner.Detail));
        Assert.DoesNotContain("normalJournalDraftProof", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isCreditSource", JsonSerializer.Serialize(owner.Detail), StringComparison.OrdinalIgnoreCase);
        Assert.False(kit.Db.ChangeTracker.HasChanges()); Assert.Equal(1, owner.Reads);
    }

    [Theory] [InlineData("caption")][InlineData("maker")][InlineData("actual")][InlineData("threshold")][InlineData("unit")]
    [InlineData("reference-id")][InlineData("reference-label")][InlineData("credit-role")][InlineData("membership")][InlineData("missing-membership")]
    public async Task Source_owned_formatter_binds_every_rule_and_reference_to_actual_owner_membership(string fault)
    {
        await using var kit = new Kit(); var owner = new Owner { Detail = Detail() }; var fence = kit.Fence();
        var source = new AccountingNormalJournalDraftWorkInboxSourceV1(kit.Db, fence, owner);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(Kit.Context(), owner.Detail.Draft.DraftId, default));
        var item = snapshot.Item; var detail = owner.Detail;
        item = fault switch
        {
            "caption" => item with { DocumentLabel = "wrong" }, "maker" => item with { PreparedBy = Kit.CallerText },
            "actual" => item with { Rules = [item.Rules[0] with { Actual = "1" }] }, "threshold" => item with { Rules = [item.Rules[0] with { Threshold = "1" }] },
            "unit" => item with { Rules = [item.Rules[0] with { Unit = "kg" }] },
            "reference-id" => item with { References = item.References.Select((x, i) => i == 0 ? x with { RecordId = Guid.NewGuid() } : x).ToArray() },
            "reference-label" => item with { References = item.References.Select((x, i) => i == 0 ? x with { Label = "wrong" } : x).ToArray() },
            "credit-role" => item with { References = item.References.Select((x, i) => i == 0 ? x with { Role = "credit-movement-proposal" } : x).ToArray() }, _ => item,
        };
        if (fault == "membership") detail = detail with { IsCreditSource = true };
        if (fault == "missing-membership") detail = detail with { IsCreditSource = null };
        Assert.False(AccountingNormalJournalDraftDecisionBindingV1.MatchesDecisionItem(item, detail));
    }

    [Theory] [InlineData(AccountingApiStatus.Denied)][InlineData(AccountingApiStatus.NotFound)][InlineData(AccountingApiStatus.Unavailable)]
    public async Task Owner_refusals_never_fall_back_to_a_direct_header_query(AccountingApiStatus status)
    {
        await using var kit = new Kit(); var owner = new Owner { Status = status }; var fence = kit.Fence();
        var source = new AccountingNormalJournalDraftWorkInboxSourceV1(kit.Db, fence, owner);
        if (status == AccountingApiStatus.Denied) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(), Guid.NewGuid(), default));
        else if (status == AccountingApiStatus.NotFound) Assert.Null(await source.ReadDecisionAsync(Kit.Context(), Guid.NewGuid(), default));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Kit.Context(), Guid.NewGuid(), default));
        Assert.Equal(1, owner.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData("company")][InlineData("id")][InlineData("preview")][InlineData("membership")][InlineData("maker")]
    public async Task Malformed_or_own_maker_owner_detail_is_not_an_available_snapshot(string fault)
    {
        await using var kit = new Kit(); var owner = new Owner { Detail = Detail() }; var id = owner.Detail.Draft.DraftId;
        var view = owner.Detail.Draft;
        view = fault switch { "company" => view with { CompanyId = Kit.OtherCompany }, "id" => view with { DraftId = Guid.NewGuid() },
            "maker" => view with { PreparedByUserId = Kit.Caller }, _ => view };
        owner.Detail = owner.Detail with { Draft = view, PreviewDigest = NormalJournalDraftServiceV1.NormalPreviewDigest(view), IsCreditSource = fault == "membership" ? null : false };
        if (fault == "preview") owner.Detail = owner.Detail with { PreviewDigest = "wrong" };
        var fence = kit.Fence(); var source = new AccountingNormalJournalDraftWorkInboxSourceV1(kit.Db, fence, owner);
        if (fault == "maker") await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(), id, default));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Kit.Context(), id, default));
    }

    [Fact]
    public async Task Missing_owner_and_scoped_or_wrong_actor_context_fail_closed_before_reading_any_accounting_rows()
    {
        await using var kit = new Kit(); var owner = new Owner { Detail = Detail() }; var fence = kit.Fence();
        var source = new AccountingNormalJournalDraftWorkInboxSourceV1(kit.Db, fence, owner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(user: "different"), owner.Detail.Draft.DraftId, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context() with { GrantScope = new(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) }, owner.Detail.Draft.DraftId, default));
        var unsupported = new AccountingNormalJournalDraftWorkInboxSourceV1(kit.Db, fence);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unsupported.ReadDecisionAsync(Kit.Context(), owner.Detail.Draft.DraftId, default));
        Assert.Equal(0, owner.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    private static NormalJournalDraftDecisionDetailV1 Detail(bool terminal = false, bool credit = false)
    {
        var id = Guid.NewGuid(); var at = Kit.Start.ToUniversalTime();
        var view = new NormalJournalDraftViewV1(id, Kit.Company, terminal ? 8 : 7, terminal ? "Approved" : "Draft", Kit.Maker, at,
            terminal ? Kit.Caller : null, terminal ? at.AddMinutes(1) : null, terminal ? Guid.NewGuid() : null, terminal ? id : null,
            Guid.NewGuid(), "SENTETIK-purpose", "rule-1", "plan-1", new string('A', 64), Guid.NewGuid(), new(2026, 11, 20), new(2026, 11, 20), null,
            [new(1, "debit", "Debit", Guid.NewGuid(), "10"), new(2, "credit", "Credit", Guid.NewGuid(), "10")]);
        return new(view, new string('A', 64), NormalJournalDraftServiceV1.NormalPreviewDigest(view)) { IsCreditSource = credit };
    }
    private sealed class Owner : INormalJournalDraftDecisionReadV1
    {
        public NormalJournalDraftDecisionDetailV1 Detail = null!; public AccountingApiStatus Status = AccountingApiStatus.Ok; public int Reads;
        public async Task<AccountingApiResult<NormalJournalDraftDecisionDetailV1>> ReadDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token)
        { await fence(token); Reads++; await fence(token); return new(Status, Status == AccountingApiStatus.Ok ? Detail : null, false, null); }
    }
}
