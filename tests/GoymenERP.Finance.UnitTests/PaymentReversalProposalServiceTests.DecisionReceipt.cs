using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class PaymentReversalProposalServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decision_receipt_uses_exact_original_result_key_actor_and_raw_request_hash_without_changing_five_field_wire(bool approve)
    {
        var (row, result, context, metadata, reason) = ReceiptFixture(approve);
        var proof = PaymentReversalProposalService.RestoreDecisionReceipt(row, context, metadata, result.Resource!.Id,
            "1", reason, row.CommandName, result, new string('a', 64));
        Assert.NotNull(proof); Assert.Equal("2", proof.ResultVersion); Assert.Equal(context.ActorId, proof.ActorId);
        var wire = JsonSerializer.SerializeToElement(result with { DecisionReceipt = proof }, FinanceJson.CreateOptions());
        Assert.Equal(5, wire.EnumerateObject().Count()); Assert.False(wire.TryGetProperty("decisionReceipt", out _));
        Assert.Equal(row.ResultJson, JsonSerializer.Serialize(result with { DecisionReceipt = proof }, FinanceJson.CreateOptions()));
        var replay = PaymentReversalProposalService.RestoreDecisionReceipt(row, context with { CorrelationId = "new-read" }, metadata,
            result.Resource!.Id, "1", reason, row.CommandName, result with { IsReplay = true }, new string('a', 64));
        Assert.Equal(proof, replay); Assert.Equal("original-correlation", replay!.OriginalCorrelationId);
        if (!approve)
        {
            Assert.Equal("ret", result.Resource!.DecisionReason); Assert.Equal(" ret ", reason);
            Assert.NotEqual(row.PayloadHash, PaymentReversalProposalService.RejectPayloadHash(new(result.Resource!.Id, "1", "ret")));
            Assert.Null(PaymentReversalProposalService.RestoreDecisionReceipt(row, context, metadata, result.Resource!.Id,
                "1", "ret", row.CommandName, result, new string('a', 64)));
        }
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("key")]
    [InlineData("payload")]
    [InlineData("payment")]
    [InlineData("correlation")]
    [InlineData("timestamp")]
    [InlineData("foreign-result")]
    [InlineData("terminal-version")]
    [InlineData("unknown-member")]
    [InlineData("malformed-json")]
    [InlineData("missing-resource")]
    [InlineData("failure")]
    [InlineData("maker")]
    public void Missing_or_malformed_original_provenance_never_becomes_a_Work_receipt(string fault)
    {
        var (row, result, context, metadata, reason) = ReceiptFixture(true);
        switch (fault)
        {
            case "company": row.CompanyId = Guid.NewGuid(); break;
            case "actor": row.ActorId = "another-checker"; break;
            case "key": row.IdempotencyKeyHash = new string('b', 64); break;
            case "payload": row.PayloadHash = new string('b', 64); break;
            case "payment": row.ManualBankPaymentId = Guid.NewGuid(); break;
            case "correlation": row.CorrelationId = "different"; break;
            case "timestamp": row.RecordedAtUtc = At.AddMinutes(1); break;
            case "foreign-result": result = result with { Resource = result.Resource! with { Id = Guid.NewGuid() } }; break;
            case "terminal-version": row.ResultJson = JsonSerializer.Serialize(result = result with { Resource = result.Resource! with { Version = "3" } }, FinanceJson.CreateOptions()); break;
            case "unknown-member": row.ResultJson = row.ResultJson[..^1] + ",\"unknown\":true}"; break;
            case "malformed-json": row.ResultJson = "{"; break;
            case "missing-resource": row.ResultJson = JsonSerializer.Serialize(result = result with { Resource = null }, FinanceJson.CreateOptions()); break;
            case "failure": row.ResultJson = JsonSerializer.Serialize(result = result with { Outcome = CommandOutcome.VersionConflict }, FinanceJson.CreateOptions()); break;
            case "maker": row.ResultJson = JsonSerializer.Serialize(result = result with { Resource = result.Resource! with { ProposedBy = context.ActorId } }, FinanceJson.CreateOptions()); break;
        }
        Assert.Null(PaymentReversalProposalService.RestoreDecisionReceipt(row, context, metadata,
            // Always the actual original request's proposal, even when the answer was replaced.
            Guid.Parse("33333333-3333-4333-8333-333333333333"), "1", reason, PaymentReversalProposalService.ApproveCommand,
            result, new string('a', 64)));
    }

    [Fact]
    public async Task Purpose_preview_joins_only_company_bound_immutable_payment_facts_and_never_needs_general_payment_read()
    {
        await using var run = await Run.StartAsync(); var id = await run.ProposeAsync("maker"); await using var db = run.Context();
        var company = new DecisionCompany(Company); var user = new DecisionUser(); var permissions = new DecisionEntryPermissions();
        permissions.ForAction = action => action == FinanceAction.ApprovePaymentReversal ? PermissionDecision.Allowed : PermissionDecision.Denied;
        var core = DecisionCore(db, company, user, permissions, null); var context = new FinanceContext(Company, "checker", "preview", null);
        var calls = 0;
        var read = await core.ReadDecisionPreviewAsync(context, id, _ => { calls++; return Task.CompletedTask; }, default);
        Assert.Equal(ReadOutcome.Found, read.Outcome); var view = Assert.IsType<PaymentReversalDecisionPreviewV1>(read.Resource);
        Assert.Equal(4, calls); Assert.Equal(run.PaymentId, view.Proposal.PaymentId); Assert.Equal(run.EventId, view.OriginalEventId);
        Assert.Equal("1250.50", view.Amount); Assert.Equal("recorder", view.PaymentRecordedBy); Assert.Equal(Company, view.CompanyId);
        Assert.False(db.ChangeTracker.HasChanges()); Assert.Empty(await db.OutgoingMoneyEventReversals.ToArrayAsync());
        user.Actor = "maker";
        Assert.Equal(ReadOutcome.Denied, (await core.ReadDecisionPreviewAsync(context with { ActorId = "maker" }, id, _ => Task.CompletedTask, default)).Outcome);
        user.Actor = "checker"; permissions.ForAction = _ => PermissionDecision.Denied;
        Assert.Equal(ReadOutcome.Denied, (await core.ReadDecisionPreviewAsync(context, id, _ => Task.CompletedTask, default)).Outcome);
        permissions.ForAction = _ => PermissionDecision.Allowed; calls = 0;
        var refused = await core.ReadDecisionPreviewAsync(context, id, _ => { if (++calls == 3) company.Set(Guid.NewGuid()); return Task.CompletedTask; }, default);
        Assert.Equal(ReadOutcome.Denied, refused.Outcome); Assert.Null(refused.Resource); Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("membership")]
    [InlineData("grant")]
    [InlineData("consumer")]
    public async Task Purpose_preview_never_releases_a_result_after_the_post_query_authority_or_consumer_fence_changes(string fault)
    {
        await using var run = await Run.StartAsync(); var id = await run.ProposeAsync("maker"); await using var db = run.Context();
        var user = new PreviewChangingUser(); var permission = new DecisionEntryPermissions();
        var core = DecisionCore(db, new DecisionCompany(Company), user, permission, null);
        var calls = 0; var context = new FinanceContext(Company, "checker", "release", null);
        async Task Fence(CancellationToken token)
        {
            await Task.CompletedTask;
            if (++calls != 3) return;
            if (fault == "actor") user.Actor = "another";
            if (fault == "membership") user.Member = false;
            if (fault == "grant") permission.ForAction = _ => PermissionDecision.Denied;
            if (fault == "consumer") throw new InvalidOperationException("SENTETIK consumer delegation revoked");
        }
        if (fault == "consumer") await Assert.ThrowsAsync<InvalidOperationException>(() => core.ReadDecisionPreviewAsync(context, id, Fence, default));
        else { var answer = await core.ReadDecisionPreviewAsync(context, id, Fence, default); Assert.Equal(ReadOutcome.Denied, answer.Outcome); Assert.Null(answer.Resource); }
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("actual")]
    [InlineData("threshold")]
    [InlineData("unit")]
    [InlineData("deviation")]
    [InlineData("reference")]
    [InlineData("maker")]
    public async Task Owner_matcher_binds_the_entire_native_rule_and_reference_shape_to_the_human_payment_facts(string fault)
    {
        await using var run = await Run.StartAsync(); var id = await run.ProposeAsync("maker"); await using var db = run.Context();
        var core = DecisionCore(db, new DecisionCompany(Company), new DecisionUser(), new DecisionEntryPermissions(), null);
        var read = await core.ReadDecisionPreviewAsync(new(Company, "checker", "rule", null), id, _ => Task.CompletedTask, default);
        var view = read.Resource!; var proposal = view.Proposal;
        // With no document number, native Item captions use the proposal; payment references use the payment.
        var caption = FinanceWorkInboxRead.Label(view.PaymentDocumentNumber, proposal.Id);
        var paymentLabel = FinanceWorkInboxRead.Label(view.PaymentDocumentNumber, proposal.PaymentId);
        var link = FinanceWorkInboxFence.Link(FinanceManualBankPaymentReversalWorkInboxSourceV1.ScreenPath, proposal.PaymentId);
        var item = new WorkInboxItemV1("finance", FinanceManualBankPaymentReversalWorkInboxSourceV1.KindName, id, caption,
            proposal.ProposedAtUtc, link, proposal.ProposedBy,
            [new(FinanceManualBankPaymentReversalWorkInboxSourceV1.ApprovalRequiredRule, null, FinanceWorkInboxRead.Amount(view.Amount), null, null)],
            [FinanceWorkInboxRead.Reference("manual-bank-payment", proposal.PaymentId, paymentLabel, link),
                FinanceWorkInboxRead.Reference("party", view.PartyId), FinanceWorkInboxRead.Reference("bank-account", view.TreasuryAccountId, view.TreasuryAccountLabel),
                FinanceWorkInboxRead.Reference("currency", view.CurrencyId)]);
        Assert.True(PaymentReversalProposalService.MatchesDecisionItem(item, view));
        var rule = item.Rules[0];
        var altered = fault switch
        {
            "actual" => item with { Rules = [rule with { Actual = "1250.51" }] },
            "threshold" => item with { Rules = [rule with { Threshold = "1" }] },
            "unit" => item with { Rules = [rule with { Unit = "TRY" }] },
            "deviation" => item with { Rules = [rule with { Deviation = "1" }] },
            "reference" => item with { References = [item.References[0] with { RecordId = Guid.NewGuid() }, .. item.References.Skip(1)] },
            _ => item with { PreparedBy = "another-maker" },
        };
        Assert.False(PaymentReversalProposalService.MatchesDecisionItem(altered, view));
    }

    [Fact]
    public async Task Decision_source_preserves_native_Inbox_facts_and_keeps_scalar_binding_out_of_serialized_snapshot()
    {
        await using var run = await Run.StartAsync(); var id = await run.ProposeAsync("maker"); await using var db = run.Context();
        var company = new DecisionCompany(Company); var user = new DecisionUser(); var authority = new PreviewSourceAuthority();
        var core = DecisionCore(db, company, user, new DecisionEntryPermissions(), null);
        var source = new FinanceManualBankPaymentReversalWorkInboxSourceV1(db, new(company, user, authority), core.WithApprovalSafety());
        var context = new WorkInboxSourceContextV1(Company, "checker", "snapshot", null);
        var queue = await source.ListPendingAsync(context, 10, default);
        var pending = await source.ReadDecisionAsync(context, id, default);
        Assert.NotNull(pending); Assert.True(pending.Pending); Assert.Equal("1", pending.SourceVersion);
        var preview = (await core.ReadDecisionPreviewAsync(new(Company, "checker", "human", null), id, _ => Task.CompletedTask, default)).Resource!;
        Assert.Equal(PaymentReversalProposalService.DecisionPreviewDigest(preview), pending.ManualBankPaymentReversalProof!.PreviewDigest);
        var originalItem = Assert.Single(queue.Items!);
        Assert.True(PaymentReversalProposalService.MatchesDecisionItem(originalItem, preview));
        Assert.True(PaymentReversalProposalService.MatchesDecisionItem(pending.Item, preview));
        var wire = JsonSerializer.Serialize(pending, FinanceJson.CreateOptions());
        Assert.DoesNotContain("manualBankPaymentReversalProof", wire); Assert.DoesNotContain("recordedFactsDigest", wire);
        Assert.DoesNotContain("previewDigest", wire); Assert.DoesNotContain("originalEventId", wire);
        // Actual pure native reject fixture: this is not guarded SQL commit evidence.
        await using (var checker = run.Scope("checker"))
            Assert.Equal(CommandOutcome.Succeeded, (await checker.Service.RejectAsync(checker.Context, Key("old-reject"), new(id, "1", " ret "), default)).Outcome);
        var replacement = await run.ProposeAsync("new-maker", "new-proposal"); Assert.NotEqual(id, replacement);
        var terminal = await source.ReadDecisionAsync(context, id, default);
        Assert.NotNull(terminal); Assert.False(terminal.Pending); Assert.Equal("2", terminal.SourceVersion);
        Assert.Equal(id, terminal.Item.ItemId); Assert.Equal("maker", terminal.Item.PreparedBy);
        Assert.Equal("Rejected", terminal.ManualBankPaymentReversalProof!.Status);
        Assert.Equal(pending.ManualBankPaymentReversalProof!.RecordedFactsDigest, terminal.ManualBankPaymentReversalProof!.RecordedFactsDigest);
        authority.Scope = GoymenERP.BuildingBlocks.Security.PermissionScope.Denied;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(context, id, default));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Decision_source_without_the_registered_guarded_owner_fails_closed_instead_of_reading_raw_payment_tables_as_a_new_engine()
    {
        await using var run = await Run.StartAsync(); var id = await run.ProposeAsync("maker"); await using var db = run.Context();
        var source = new FinanceManualBankPaymentReversalWorkInboxSourceV1(db,
            new(new DecisionCompany(Company), new DecisionUser(), new PreviewSourceAuthority()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(new(Company, "checker", "uncomposed", null), id, default));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    private sealed class PreviewSourceAuthority : GoymenERP.BuildingBlocks.Security.IErpAuthorityRevalidationService
    {
        public GoymenERP.BuildingBlocks.Security.PermissionScope Scope = new(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        public Task<GoymenERP.BuildingBlocks.Security.PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default) => Task.FromResult(Scope);
    }

    private static (FinanceCommandRow Row, PaymentReversalProposalCommandResult Result, FinanceContext Context, CommandMetadata Metadata, string? Reason) ReceiptFixture(bool approve)
    {
        var id = Guid.Parse("33333333-3333-4333-8333-333333333333"); var payment = Guid.NewGuid();
        var context = new FinanceContext(Company, "checker", "original-correlation", null); var key = Key("original-key");
        var view = new PaymentReversalProposalView(id, payment, ReversalOn, "Hatalı ödeme", approve ? ReversalProposalStatus.Approved : ReversalProposalStatus.Rejected,
            "maker", At.AddHours(-1), "checker", At, approve ? null : "ret", approve ? Guid.NewGuid() : null, "2");
        var result = new PaymentReversalProposalCommandResult(CommandOutcome.Succeeded, view, false, context.CorrelationId, []);
        var command = approve ? PaymentReversalProposalService.ApproveCommand : PaymentReversalProposalService.RejectCommand;
        string? reason = approve ? null : " ret ";
        var payload = approve ? PaymentReversalProposalService.ApprovePayloadHash(new(id, "1")) : PaymentReversalProposalService.RejectPayloadHash(new(id, "1", reason!));
        return (new FinanceCommandRow { CompanyId = Company, ManualBankPaymentId = payment, CommandName = command,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(key.IdempotencyKey), PayloadHash = payload, ActorId = context.ActorId,
            CorrelationId = context.CorrelationId, RecordedAtUtc = At, ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()) }, result, context, key, reason);
    }
    private sealed class PreviewChangingUser : GoymenERP.BuildingBlocks.Security.ICurrentUser
    {
        public string Actor = "checker"; public bool Member = true;
        public bool IsAuthenticated => true; public string? UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id);
    }
}
