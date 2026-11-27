using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class InvoiceLineCogsApprovalSafetyTests
{
    [Fact]
    public async Task Cogs_purpose_read_uses_full_retained_native_proposal_and_price_free_source_without_general_owner_reads()
    {
        var store = new DecisionProtocol(); var source = new Source { Unavailable = true }; var service = Service(store, store, source, new(), new CompanyContextDouble(), new());
        var answer = await service.ReadDecisionAsync(Proposal, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok, answer.Status); var detail = Assert.IsType<InvoiceLineCogsDecisionDetailV1>(answer.Resource);
        Assert.Equal(store.Facts.Prepared, detail.Proposal); Assert.Equal(store.Facts.Source, detail.Source); Assert.Equal(0, detail.CurrentLineVersion);
        Assert.Equal(0, source.Reads); Assert.Equal(0, store.Effects); Assert.Empty(store.Commands);
        Assert.DoesNotContain("commercialProofJson", JsonSerializer.Serialize(detail, Json)); Assert.DoesNotContain("approvalReceipt", JsonSerializer.Serialize(detail, Json));
    }
    [Theory] [InlineData("permission")] [InlineData("scope")] [InlineData("null-scope")] [InlineData("company")]
    [InlineData("actor")] [InlineData("member")] [InlineData("cancel")]
    public async Task Cogs_purpose_final_completed_consumer_loss_never_releases_retained_facts(string fault)
    {
        var store = new DecisionProtocol(); var company = new CompanyContextDouble(); var user = new User(); var authority = new Authority(); using var cts = new CancellationTokenSource();
        var service = Service(store, store, new(), authority, company, user); var callbacks = 0;
        Task Fence(CancellationToken _) { if (++callbacks == 4) Drift(fault, company, user, authority, cts); return Task.CompletedTask; }
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadDecisionAsync(Proposal, Fence, cts.Token));
        else { var answer = await service.ReadDecisionAsync(Proposal, Fence); Assert.Equal(AccountingApiStatus.Denied, answer.Status); Assert.Null(answer.Resource); }
        Assert.Equal(4, callbacks); Assert.Empty(store.Commands); Assert.Equal(0, store.Effects);
    }
    [Fact]
    public async Task Cogs_purpose_missing_and_unconfigured_owner_are_distinct_and_cancelled_entry_never_calls_consumer()
    {
        var store = new DecisionProtocol { Missing = true }; var service = Service(store, store, new(), new(), new CompanyContextDouble(), new());
        Assert.Equal(AccountingApiStatus.NotFound, (await service.ReadDecisionAsync(Proposal, _ => Task.CompletedTask)).Status);
        Assert.Equal(AccountingApiStatus.Invalid, (await service.ReadDecisionAsync(Guid.Empty, _ => Task.CompletedTask)).Status);
        var legacy = Service(new MissingBoundaryStore(), new NeverCommands(), new(), new(), new CompanyContextDouble(), new());
        Assert.Equal(AccountingApiStatus.Unavailable, (await legacy.ReadDecisionAsync(Proposal, _ => Task.CompletedTask)).Status);
        using var cts = new CancellationTokenSource(); cts.Cancel(); var called = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadDecisionAsync(Proposal, _ => { called = true; return Task.CompletedTask; }, cts.Token));
        Assert.False(called); Assert.Empty(store.Commands);
    }
    [Fact]
    public async Task Cogs_read_freezes_collection_facts_before_a_mutating_final_consumer_callback()
    {
        var store = new DecisionProtocol(); var service = Service(store, store, new(), new(), new CompanyContextDouble(), new());
        await Approve(service, "prepare-collection");
        var list = new List<InvoiceLineCogsAttributedCorrectionV1> { new(new('A', 64), Proposal, Invoice, Line) };
        store.Facts = store.Facts with { Proposal = store.Facts.Proposal with { AttributedCorrections = list } };
        var callbacks = 0;
        var answer = await service.ReadDecisionAsync(Proposal, _ => { if (++callbacks == 4) list.Add(new("bad", Guid.Empty, Guid.Empty, Guid.Empty)); return Task.CompletedTask; });
        Assert.Equal(AccountingApiStatus.Ok, answer.Status); Assert.Equal(2, list.Count); Assert.Single(answer.Resource!.Proposal.AttributedCorrections!);
        Assert.NotSame(list, answer.Resource.Proposal.AttributedCorrections); Assert.True(InvoiceLineCogsCorrectionServiceV1.ValidCogsDecisionDetail(answer.Resource));
    }
    [Theory] [InlineData("company")] [InlineData("id")] [InlineData("source")] [InlineData("version")] [InlineData("status")]
    [InlineData("digest")] [InlineData("plan")] [InlineData("approved")] [InlineData("line-version")] [InlineData("null")]
    public async Task Cogs_purpose_malformed_or_foreign_retained_source_is_unavailable(string fault)
    {
        var store = new DecisionProtocol(); var f = store.Facts;
        store.Facts = fault switch
        {
            "company" => f with { Proposal = f.Proposal with { CompanyId = Guid.NewGuid() } },
            "id" => f with { Proposal = f.Proposal with { ProposalId = Guid.NewGuid() } },
            "source" => f with { Source = f.Source with { InvoiceLineId = Guid.NewGuid() } },
            "version" => f with { Proposal = f.Proposal with { Version = 2 } }, "status" => f with { Proposal = f.Proposal with { Status = "Unknown" } },
            "digest" => f with { SourceHash = "bad" }, "plan" => f with { Proposal = f.Proposal with { AccountPlan = null! } },
            "approved" => f with { RevisionBound = true }, "line-version" => f with { CurrentLineVersion = -1 }, _ => f with { Proposal = null! },
        };
        var answer = await Service(store, store, new(), new(), new CompanyContextDouble(), new()).ReadDecisionAsync(Proposal, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Unavailable, answer.Status); Assert.Null(answer.Resource); Assert.Empty(store.Commands);
    }
    [Fact]
    public async Task Cogs_private_original_receipt_survives_later_line_revision_and_unavailable_live_source_without_replacing_native_json()
    {
        var store = new DecisionProtocol(); var source = new Source(); var service = Service(store, store, source, new(), new CompanyContextDouble(), new());
        var first = await Approve(service, "original-proof"); var receipt = Assert.IsType<InvoiceLineCogsApprovalReceiptV1>(first.Resource?.ApprovalReceipt);
        Assert.Equal(1, receipt.ExpectedVersion); Assert.Equal(2, receipt.ResultVersion); Assert.Equal(Checker, receipt.ActorId);
        Assert.Equal(InvoiceLineCogsCorrectionServiceV1.CogsSourceFactsDigest(store.Facts), receipt.SourceFactsDigest);
        Assert.Equal(InvoiceLineCogsCorrectionServiceV1.CogsApprovalPayloadHash(Invoice, Line, Proposal, 1), Assert.Single(store.Commands).RequestHash);
        store.Facts = store.Facts with { CurrentLineVersion = 17 }; source.Unavailable = true; var liveReads = source.Reads;
        var replay = await Approve(service, "original-proof"); var terminal = await service.ReadDecisionAsync(Proposal, _ => Task.CompletedTask);
        Assert.True(replay.IsReplay); Assert.Equal(receipt, replay.Resource?.ApprovalReceipt); Assert.Equal("SENTETIK-cogs-approval", replay.OriginalCorrelationId);
        Assert.Equal(17L, terminal.Resource?.CurrentLineVersion); Assert.Equal(2L, terminal.Resource?.Proposal.Version);
        Assert.Equal(liveReads, source.Reads); Assert.Equal(1, store.Effects); Assert.Single(store.Commands);
        Assert.Equal(8, JsonSerializer.SerializeToElement(first, Json).EnumerateObject().Count()); Assert.Equal(22, JsonSerializer.SerializeToElement(first.Resource, Json).EnumerateObject().Count());
        Assert.DoesNotContain("approvalReceipt", Assert.Single(store.Commands).ResultJson); Assert.DoesNotContain("approvalReceipt", JsonSerializer.Serialize(first, Json));
    }
    [Fact]
    public async Task Cogs_missing_original_provenance_never_forges_receipt_and_same_key_can_reconcile_when_owner_returns()
    {
        var store = new DecisionProtocol { Missing = true }; var service = Service(store, store, new(), new(), new CompanyContextDouble(), new());
        var first = await Approve(service, "missing-proof");
        Assert.Equal(AccountingApiStatus.Ok, first.Status); Assert.Null(first.Resource?.ApprovalReceipt); Assert.Equal(1, store.Effects); Assert.Single(store.Commands);
        // Native success remains native success. A Work consumer must refuse missing proof; no rollback is claimed.
        store.Missing = false; var replay = await Approve(service, "missing-proof");
        Assert.True(replay.IsReplay); Assert.NotNull(replay.Resource?.ApprovalReceipt); Assert.Equal(1, store.Effects); Assert.Single(store.Commands);
    }
    [Theory] [InlineData("extra-root")] [InlineData("extra-view")] [InlineData("private")] [InlineData("actor")] [InlineData("key")]
    [InlineData("payload")] [InlineData("company")] [InlineData("proposal")] [InlineData("facts")] [InlineData("version")] [InlineData("correlation")]
    public async Task Cogs_receipt_binder_rejects_forged_command_or_current_row_substitution(string fault)
    {
        var store = new DecisionProtocol(); var first = await Approve(Service(store, store, new(), new(), new CompanyContextDouble(), new()), "binder");
        var command = Assert.Single(store.Commands); var facts = store.Facts;
        if (fault is "extra-root" or "extra-view" or "private")
        {
            var json = JsonNode.Parse(command.ResultJson)!.AsObject();
            if (fault == "extra-root") json["other"] = true; else json["resource"]!.AsObject()[fault == "private" ? "approvalReceipt" : "other"] = true;
            command = command with { ResultJson = json.ToJsonString() };
        }
        if (fault == "actor") command = command with { ActorId = Maker.ToString("D") };
        if (fault == "key") command = command with { IdempotencyKeyHash = new('B', 64) };
        if (fault == "payload") command = command with { RequestHash = new('B', 64) };
        if (fault == "correlation") command = command with { OriginalCorrelationId = "" };
        if (fault == "company") facts = facts with { Proposal = facts.Proposal with { CompanyId = Guid.NewGuid() } };
        if (fault == "proposal") facts = facts with { Proposal = facts.Proposal with { ProposalId = Guid.NewGuid() } };
        if (fault == "facts") facts = facts with { Prepared = facts.Prepared with { Reason = "other immutable facts" } };
        Assert.Null(InvoiceLineCogsCorrectionServiceV1.BindCogsApprovalReceipt(command, facts, first, Invoice, Line, Proposal, fault == "version" ? 2 : 1, "binder", Checker));
        Assert.Equal(1, store.Effects); Assert.Single(store.Commands);
    }
    [Fact]
    public async Task Cogs_work_snapshot_hides_private_proof_and_refuses_maker_without_general_read_or_writes()
    {
        var store = new DecisionProtocol(); var company = new CompanyContextDouble(); var user = new User(); var authority = new Authority();
        var service = Service(store, store, new(), authority, company, user); await using var db = Db(company);
        var source = new AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1(db, new(company, user, authority), service);
        var context = new WorkInboxSourceContextV1(Company, Checker.ToString("D"), "work-cogs", null);
        var snapshot = (await source.ReadDecisionAsync(context, Proposal, default))!;
        Assert.True(snapshot.Pending); Assert.Equal("1", snapshot.SourceVersion); Assert.NotNull(snapshot.InvoiceLineCogsProof);
        Assert.True(AccountingInvoiceLineCogsDecisionBindingV1.MatchesDecisionProof(snapshot.Item, snapshot.InvoiceLineCogsProof));
        var detail = (await service.ReadDecisionAsync(Proposal, _ => Task.CompletedTask)).Resource;
        Assert.True(AccountingInvoiceLineCogsDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail));
        var json = JsonSerializer.Serialize(snapshot, Json); Assert.DoesNotContain("invoiceLineCogsProof", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENTETIK-account", json); Assert.DoesNotContain(snapshot.InvoiceLineCogsProof!.SourceFactsDigest, json);
        Assert.Equal(AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1.PurposePath + "/" + snapshot.Item.ItemId.ToString("D"), snapshot.Item.Link);
        user.Actor = Maker; await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(context with { UserId = Maker.ToString("D") }, Proposal, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1(db, new(company, new User(), authority)).ReadDecisionAsync(context, Proposal, default));
        Assert.Equal(0, store.Effects); Assert.Empty(store.Commands);
    }
    [Fact]
    public async Task Cogs_source_rejects_a_controlled_valid_found_checker_as_maker_with_authority_refusal()
    {
        var facts = DecisionFacts(); var proposal = facts.Prepared with { PreparedBy = Checker };
        var detail = new InvoiceLineCogsDecisionDetailV1(proposal, facts.Source, 0, new('A', 64), InvoiceLineCogsCorrectionServiceV1.CogsPreviewDigest(proposal, facts.Source, 0));
        var company = new CompanyContextDouble(); await using var db = Db(company);
        var source = new AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1(db, new(company, new User(), new Authority()), new ControlledCogsReader(detail));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(new(Company, Checker.ToString("D"), "controlled-cogs", null), Proposal, default));
        Assert.False(db.ChangeTracker.HasChanges());
    }
    private sealed class ControlledCogsReader(InvoiceLineCogsDecisionDetailV1 detail) : IInvoiceLineCogsDecisionReadV1
    { public Task<AccountingApiResult<InvoiceLineCogsDecisionDetailV1>> ReadDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default) => Task.FromResult(new AccountingApiResult<InvoiceLineCogsDecisionDetailV1>(AccountingApiStatus.Ok, detail, false, null)); }
    [Theory] [InlineData("ref")] [InlineData("caption")] [InlineData("rule")] [InlineData("link")] [InlineData("maker")] [InlineData("scope")]
    public async Task Cogs_binding_keeps_exact_owner_formatter_for_get_and_original_receipt(string fault)
    {
        var store = new DecisionProtocol(); var answer = await Approve(Service(store, store, new(), new(), new CompanyContextDouble(), new()), "binding");
        var receipt = answer.Resource!.ApprovalReceipt; var p = answer.Resource; var item = AccountingInvoiceLineCogsDecisionBindingV1.FromView(p)!;
        var detail = new InvoiceLineCogsDecisionDetailV1(p, store.Facts.Source, 1, InvoiceLineCogsCorrectionServiceV1.CogsSourceFactsDigest(store.Facts), InvoiceLineCogsCorrectionServiceV1.CogsPreviewDigest(p, store.Facts.Source, 1));
        Assert.True(AccountingInvoiceLineCogsDecisionBindingV1.MatchesDecisionItem(item, detail)); Assert.True(AccountingInvoiceLineCogsDecisionBindingV1.MatchesDecisionItem(item, p, receipt));
        item = fault switch
        {
            "ref" => item with { References = [item.References[0] with { RecordId = Guid.NewGuid() }, item.References[1]] },
            "caption" => item with { DocumentLabel = "other" }, "rule" => item with { Rules = [item.Rules[0] with { Actual = "1" }] },
            "link" => item with { Link = "/other" }, "maker" => item with { PreparedBy = Checker.ToString("D") }, _ => item with { Scope = new(Guid.NewGuid(), null, null) },
        };
        Assert.False(AccountingInvoiceLineCogsDecisionBindingV1.MatchesDecisionItem(item, detail)); Assert.False(AccountingInvoiceLineCogsDecisionBindingV1.MatchesDecisionItem(item, p, receipt));
    }
    private static InvoiceLineCogsDecisionSourceV1 DecisionFacts()
    {
        var reference = new FinalizedInvoiceLineReferenceV1(Company, Invoice, Line, Proposal, 7, Proposal, Proposal, Proposal, 1, 1, new(2026, 11, 17), "SENTETIK", Proposal, new('A', 64));
        var prepared = new InvoiceLineCogsCorrectionViewV1(Proposal, Company, Invoice, Line, 7, 1, 0, "Prepared", InvoiceLineCogsClassificationV1.Manufactured,
            new(Proposal, 2, 1, "SENTETIK", new('A', 64), new(Proposal, "620", "SENTETIK-account")), Proposal, new(2026, 11, 17), new(2026, 11, 17), null,
            Maker, DateTimeOffset.Parse("2026-11-17T08:00:00Z"), null, null, "SENTETIK reason", null, null, null);
        return new(prepared, prepared, reference, new('A', 64), 0, false);
    }
    // Explicit synthetic command/read protocol, not relational transaction, Audit or real SQL acceptance.
    private sealed class DecisionProtocol : MissingBoundaryStore, IInvoiceLineCogsCorrectionStoreV1, IInvoiceLineCogsDecisionStoreV1, IAccountingCommandLog
    {
        public InvoiceLineCogsDecisionSourceV1 Facts = DecisionFacts(); public bool Missing; public int Effects; public List<AccountingCommandRecord> Commands { get; } = [];
        public async Task<InvoiceLineCogsDecisionSourceV1?> ReadCogsDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); var result = Missing ? null : Facts; await fence(token); return result; }
        public async Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> RunApprovalAsync(Func<bool, Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>>> attempt, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { try { await fence(token); var answer = await attempt(false); await fence(token); return answer; } catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); } }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default) => Task.FromResult(Commands.SingleOrDefault(x => x.CommandName == command && x.IdempotencyKeyHash == key));
        public Task RecordCommandAsync(AccountingCommandRecord command, CancellationToken token = default) { Commands.Add(command); return Task.CompletedTask; }
        public override async Task<InvoiceLineCogsCorrectionViewV1> ApproveAsync(FinalizedInvoiceLineSourceV1 source, Guid proposal, long expected, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        {
            await fence(token); var approved = Facts.Prepared with { Version = 2, Status = "Approved", ApprovedBy = actor, ApprovedAtUtc = DateTimeOffset.Parse("2026-11-17T09:00:00Z") };
            Facts = Facts with { Proposal = approved, CurrentLineVersion = 1, RevisionBound = true }; Effects++; Commands.Add(metadata.Completion!(approved)); return approved;
        }
    }
}
