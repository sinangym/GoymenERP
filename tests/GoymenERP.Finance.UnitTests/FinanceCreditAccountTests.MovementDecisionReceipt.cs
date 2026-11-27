using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceCreditAccountTests
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public void Movement_receipt_binds_the_actual_native_five_field_json_and_immutable_original_replay(bool approve)
    {
        var (command, row, context, metadata, request, result) = MovementReceiptFixture(approve);
        var proof = FinanceCreditMovementServiceV1.RestoreDecisionReceipt(command, row, context, metadata, request, result);
        Assert.NotNull(proof); Assert.Equal((CompanyA, row.Id, "1", "2", Checker, context.CorrelationId, row.DecidedAtUtc),
            (proof.CompanyId, proof.MovementId, proof.ExpectedVersion, proof.ResultVersion, proof.ActorId, proof.OriginalCorrelationId, (DateTimeOffset?)proof.DecidedAtUtc));
        var wire = JsonSerializer.Serialize(result with { DecisionReceipt = proof }, FinanceJson.CreateOptions()); Assert.Equal(command.ResultJson, wire);
        using var json = JsonDocument.Parse(wire); Assert.Equal(5, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("decisionReceipt", out _));
        Assert.Equal(proof, FinanceCreditMovementServiceV1.RestoreDecisionReceipt(command, row, context with { CorrelationId = "later" }, metadata, request, result with { IsReplay = true }));
        Assert.True(row.ProposedAtUtc > proof.DecidedAtUtc); // No new chronology policy.
    }

    [Theory]
    [InlineData("company")] [InlineData("movement")] [InlineData("command")] [InlineData("actor")] [InlineData("key")] [InlineData("hash")]
    [InlineData("amount")] [InlineData("profile")] [InlineData("maker")] [InlineData("version")] [InlineData("request-version")]
    [InlineData("correlation")] [InlineData("time-offset")] [InlineData("missing-resource")] [InlineData("malformed-json")] [InlineData("extra-json")]
    [InlineData("current-substitute")] [InlineData("wrong-approve")] [InlineData("bank")] [InlineData("parent")] [InlineData("source-company")]
    [InlineData("source-hash")] [InlineData("decider")] [InlineData("rejection")] [InlineData("legacy-no-profile")]
    public void Movement_missing_misbound_or_changed_source_never_becomes_an_original_receipt(string fault)
    {
        var (command, row, context, metadata, request, result) = MovementReceiptFixture(true);
        switch (fault)
        {
            case "company": command.CompanyId = CompanyB; break;
            case "movement": request = request with { MovementId = Guid.NewGuid() }; break;
            case "command": command.Command = FinanceCreditMovementServiceV1.ProposeCommand; break;
            case "actor": command.ActorId = Maker; break;
            case "key": command.KeyHash = "wrong"; break;
            case "hash": command.PayloadHash = "wrong"; break;
            case "amount": row.SourceJson = JsonSerializer.Serialize(result.Resource!.Source with { Components = [new("principal", "999", null)] }, FinanceJson.CreateOptions()); break;
            case "legacy-no-profile": row.MoneyProfileJson = "null"; break;
            case "profile": row.MoneyProfileJson = JsonSerializer.Serialize(result.Resource!.MoneyProfile with { ProfileVersion = 2 }, FinanceJson.CreateOptions()); break;
            case "maker": row.ProposedBy = Checker; break;
            case "source-company": row.CompanyId = CompanyB; break;
            case "parent": row.CreditAccountId = Guid.NewGuid(); break;
            case "bank": row.BankAccountId = Guid.NewGuid(); break;
            case "source-hash": row.SourceIdentityHash = "wrong"; break;
            case "decider": row.DecidedBy = Maker; break;
            case "rejection": row.RejectionReason = "wrong"; break;
            case "request-version": request = request with { ExpectedVersion = "2" }; break;
            case "version": result = result with { Resource = result.Resource! with { Version = "3" } }; command.ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()); break;
            case "correlation": result = result with { OriginalCorrelationId = "other" }; command.ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()); break;
            case "time-offset": result = result with { Resource = result.Resource! with { DecidedAtUtc = row.DecidedAtUtc!.Value.ToOffset(TimeSpan.FromHours(1)) } }; command.ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()); break;
            case "missing-resource": result = result with { Resource = null }; command.ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()); break;
            case "malformed-json": command.ResultJson = "{"; break;
            case "extra-json": command.ResultJson = command.ResultJson[..^1] + ",\"extra\":true}"; break;
            case "current-substitute": result = result with { Resource = result.Resource! with { ReversedByMovementId = Guid.NewGuid() } }; break;
            case "wrong-approve": request = request with { Approve = false, Reason = "SENTETIK ret" }; break;
        }
        Assert.Null(FinanceCreditMovementServiceV1.RestoreDecisionReceipt(command, row, context, metadata, request, result));
    }

    [Fact]
    public void Original_opening_approval_receipt_stays_version_two_after_a_later_correction_and_digest_excludes_current_projection()
    {
        var (command, row, context, metadata, request, result) = MovementReceiptFixture(true); var view = result.Resource!;
        var original = FinanceCreditMovementServiceV1.RestoreDecisionReceipt(command, row, context, metadata, request, result);
        row.Version = 3; row.ReversedByMovementId = Guid.NewGuid();
        var proof = FinanceCreditMovementServiceV1.RestoreDecisionReceipt(command, row, context with { CorrelationId = "later" }, metadata, request, result with { IsReplay = true });
        Assert.Equal(original, proof); Assert.NotNull(proof); Assert.Equal("2", proof.ResultVersion);
        var later = view with { Version = "3", ReversedByMovementId = row.ReversedByMovementId };
        Assert.Equal(FinanceCreditMovementServiceV1.SourceFactsDigest(view, row.ProposedBy), FinanceCreditMovementServiceV1.SourceFactsDigest(later, row.ProposedBy));
        Assert.NotEqual(FinanceCreditMovementServiceV1.DecisionPreviewDigest(new(view, row.ProposedBy, "Bank A", proof.SourceFactsDigest)),
            FinanceCreditMovementServiceV1.DecisionPreviewDigest(new(later, row.ProposedBy, "Bank B", proof.SourceFactsDigest)));
    }

    [Fact]
    public async Task Movement_decision_read_uses_approve_only_native_facts_without_general_credit_workspace_or_owner_writes()
    {
        await using var run = new Run(); var id = await MovementDecisionPrepare(run); await using var db = run.Memory(CompanyA);
        var permission = new MovementDecisionReadPermission(); var core = MovementSafetyCore(db, new LimitSafetyCompany(CompanyA), new LimitSafetyUser(), permission, null);
        var guarded = Assert.IsAssignableFrom<IFinanceCreditMovementDecisionReadV1>(core.WithApprovalSafety()); var calls = 0;
        var answer = await guarded.ReadDecisionAsync(Ctx(Checker), id, _ => { calls++; return Task.CompletedTask; }, default);
        Assert.Equal(ReadOutcome.Found, answer.Outcome); Assert.Equal(6, calls); Assert.Equal(id, answer.Resource!.Movement.Id); Assert.Equal(Maker, answer.Resource.ProposedBy);
        Assert.Equal("100.00", Assert.Single(answer.Resource.Movement.Source.Components).Amount); Assert.Null(answer.Resource.Movement.Source.Components[0].FunctionalAmount);
        Assert.Equal(ReadOutcome.Denied, (await core.ListAsync(Ctx(Checker), answer.Resource.Movement.Source.CreditAccountId, default)).Outcome);
        Assert.All(permission.Actions.Where(x => x != FinanceAction.ReadCredits), action => Assert.Equal(FinanceAction.ApproveCredit, action));
        Assert.False(db.ChangeTracker.HasChanges()); Assert.Null(db.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("maker")] [InlineData("foreign")] [InlineData("actor")] [InlineData("company")] [InlineData("member")]
    [InlineData("denied")] [InlineData("unavailable")] [InlineData("cancel-first")] [InlineData("cancel-last")] [InlineData("throw")]
    public async Task Movement_read_never_releases_human_facts_after_fresh_consumer_or_owner_authority_loss(string fault)
    {
        await using var run = new Run(); var id = await MovementDecisionPrepare(run); await using var db = run.Memory(CompanyA);
        var company = new LimitSafetyCompany(CompanyA); var user = new LimitSafetyUser(); var permission = new MovementDecisionReadPermission();
        if (fault == "maker") user.Actor = Maker;
        var context = Ctx(fault == "maker" ? Maker : Checker); if (fault == "foreign") context = context with { CompanyId = CompanyB };
        var core = MovementSafetyCore(db, company, user, permission, null); using var cancellation = new CancellationTokenSource(); var calls = 0;
        Task Consumer(CancellationToken token)
        {
            calls++; if (fault == "cancel-first" && calls == 1) cancellation.Cancel();
            if (calls == 6)
            {
                if (fault == "actor") user.Actor = Maker;
                if (fault == "company") company.Set(CompanyB);
                if (fault == "member") user.Member = false;
                if (fault == "denied") permission.Decision = PermissionDecision.Denied;
                if (fault == "unavailable") permission.Decision = PermissionDecision.Unavailable;
                if (fault == "cancel-last") cancellation.Cancel();
                if (fault == "throw") throw new InvalidOperationException("SENTETIK consumer refusal");
            }
            return Task.CompletedTask;
        }
        if (fault.StartsWith("cancel-", StringComparison.Ordinal)) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => core.ReadDecisionAsync(context, id, Consumer, cancellation.Token));
        else if (fault == "throw") await Assert.ThrowsAsync<InvalidOperationException>(() => core.ReadDecisionAsync(context, id, Consumer, default));
        else { var answer = await core.ReadDecisionAsync(context, id, Consumer, default); Assert.Equal(fault == "unavailable" ? ReadOutcome.Unavailable : ReadOutcome.Denied, answer.Outcome); Assert.Null(answer.Resource); }
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData(PermissionDecision.Denied)] [InlineData(PermissionDecision.Unavailable)]
    public async Task Movement_initial_refusal_is_not_overwritten_when_the_second_consumer_restores_authority(PermissionDecision refusal)
    {
        await using var run = new Run(); await using var db = run.Memory(CompanyA); var permission = new MovementDecisionReadPermission { Decision = refusal };
        var core = MovementSafetyCore(db, new LimitSafetyCompany(CompanyA), new LimitSafetyUser(), permission, null); var calls = 0;
        var answer = await core.ReadDecisionAsync(Ctx(Checker), Guid.NewGuid(), _ => { if (++calls == 2) permission.Decision = PermissionDecision.Allowed; return Task.CompletedTask; }, default);
        Assert.Equal(2, calls); Assert.Equal(refusal == PermissionDecision.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, answer.Outcome); Assert.Null(answer.Resource);
    }

    [Fact]
    public async Task Movement_final_exact_source_probe_refuses_mixed_pending_and_decided_facts()
    {
        await using var run = new Run(); var id = await MovementDecisionPrepare(run); await using var db = run.Memory(CompanyA);
        var core = MovementSafetyCore(db, new LimitSafetyCompany(CompanyA), new LimitSafetyUser(), new MovementDecisionReadPermission(), null); var calls = 0;
        var answer = await core.ReadDecisionAsync(Ctx(Checker), id, async _ =>
        {
            if (++calls != 4) return;
            var decision = await run.Movement(Checker, owner => owner.DecideAsync(Ctx(Checker), new("movement-concurrent-decision"), new(id, "1", true, null), default));
            Assert.Equal(CommandOutcome.Succeeded, decision.Outcome);
        }, default);
        Assert.Equal(ReadOutcome.Unavailable, answer.Outcome); Assert.Null(answer.Resource); Assert.False(db.ChangeTracker.HasChanges());
    }

    private static async Task<Guid> MovementDecisionPrepare(Run run)
    {
        var account = await ApprovedCredit(run); var proposed = await run.Movement(Maker, owner => owner.ProposeAsync(Ctx(Maker), new("movement-purpose-prepare"), Movement(account), default));
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome); return proposed.Resource!.Id;
    }
    private sealed class MovementDecisionReadPermission : IFinancePermissionPort
    {
        public PermissionDecision Decision = PermissionDecision.Allowed; public List<FinanceAction> Actions { get; } = [];
        public Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken t)
        { Actions.Add(a); return Task.FromResult(a == FinanceAction.ApproveCredit ? Decision : PermissionDecision.Denied); }
    }
    private static (FinanceCreditMovementCommandRow Command, FinanceCreditMovementRow Row, FinanceContext Context, CommandMetadata Metadata, CreditMovementDecisionV1 Request, CreditMovementResultV1 Result) MovementReceiptFixture(bool approve)
    {
        var id = Guid.NewGuid(); var at = new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
        var source = new CreditMovementProposeV1(Guid.NewGuid(), "2", CreditMovementKindV1.OpeningBalance, new("SENTETIK source", Start, "1"), Start, Start,
            [new("principal", "00100.00", null)], null, null, null, "SENTETIK source reason"); var profile = new CreditPlanToleranceSnapshotV1(Guid.NewGuid(), 1, 2, "0.01");
        var row = new FinanceCreditMovementRow { Id = id, CompanyId = CompanyA, CreditAccountId = source.CreditAccountId, CurrencyId = Currency, Version = 2, Status = approve ? 2 : 3,
            Kind = (int)source.Kind, SourceJson = JsonSerializer.Serialize(source, FinanceJson.CreateOptions()), MoneyProfileJson = JsonSerializer.Serialize(profile, FinanceJson.CreateOptions()),
            SourceIdentityHash = FinanceCreditAccountService.PayloadHash("credit-actual-source", new { source.Kind, source.SourceDocument.Reference, source.SourceDocument.DocumentDate, source.SourceDocument.Revision }),
            ProposedBy = Maker, ProposedAtUtc = at.AddHours(1), DecidedBy = Checker, DecidedAtUtc = at, RejectionReason = approve ? null : "SENTETIK literal ret", OpeningPeriodVersion = "opening-1" };
        var context = Ctx(Checker); var metadata = new CommandMetadata("movement-receipt-key"); var request = new CreditMovementDecisionV1(id, "1", approve, row.RejectionReason);
        var view = new CreditMovementViewV1(id, CompanyA, "2", approve ? CreditMovementStatusV1.Approved : CreditMovementStatusV1.Rejected,
            source, Currency, false, row.ProposedAtUtc, row.DecidedAtUtc, row.RejectionReason, null, row.OpeningPeriodVersion, profile);
        var result = new CreditMovementResultV1(CommandOutcome.Succeeded, view, false, context.CorrelationId, []);
        var command = new FinanceCreditMovementCommandRow { CompanyId = CompanyA, Command = FinanceCreditMovementServiceV1.DecideCommand, KeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            ActorId = Checker, PayloadHash = FinanceCreditMovementServiceV1.DecisionPayloadHash(request), ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()) };
        return (command, row, context, metadata, request, result);
    }
}
