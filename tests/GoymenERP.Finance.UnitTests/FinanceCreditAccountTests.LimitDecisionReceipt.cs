using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceCreditAccountTests
{
    private static readonly DateTimeOffset LimitDecisionAt = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);

    [Theory] [InlineData(true)] [InlineData(false)]
    public void Bank_limit_receipt_binds_real_native_five_field_result_without_invented_command_columns(bool approve)
    {
        var (command, row, context, metadata, request, result) = LimitReceiptFixture(approve);
        var proof = FinanceCreditLimitServiceV1.RestoreDecisionReceipt(command, row, context, metadata, request, result);
        Assert.NotNull(proof); Assert.Equal((CompanyA, row.Id, "1", "2", Checker, context.CorrelationId, LimitDecisionAt),
            (proof.CompanyId, proof.LimitId, proof.ExpectedVersion, proof.ResultVersion, proof.ActorId, proof.OriginalCorrelationId, proof.DecidedAtUtc));
        Assert.Equal(command.PayloadHash, proof.PayloadHash); Assert.Equal(FinanceCreditLimitServiceV1.OriginalReceiptDigest(result), proof.OriginalReceiptDigest);
        var wire = JsonSerializer.Serialize(result with { DecisionReceipt = proof }, FinanceJson.CreateOptions()); Assert.Equal(command.ResultJson, wire);
        using var json = JsonDocument.Parse(wire); Assert.Equal(5, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("decisionReceipt", out _));
        Assert.Equal(proof, FinanceCreditLimitServiceV1.RestoreDecisionReceipt(command, row, context with { CorrelationId = "later" }, metadata, request, result with { IsReplay = true }));
        Assert.True(result.Resource!.ProposedAtUtc > proof.DecidedAtUtc); // No new clock chronology rule.
    }

    [Theory]
    [InlineData("company")] [InlineData("limit")] [InlineData("command")] [InlineData("actor")] [InlineData("key")] [InlineData("hash")]
    [InlineData("amount")] [InlineData("source")] [InlineData("maker")] [InlineData("version")] [InlineData("request-version")]
    [InlineData("correlation")] [InlineData("time-offset")] [InlineData("missing-resource")] [InlineData("malformed-json")] [InlineData("extra-json")]
    [InlineData("current-substitute")] [InlineData("wrong-approve")] [InlineData("row-amount")] [InlineData("parent")] [InlineData("source-company")]
    public void Bank_limit_missing_misbound_or_changed_source_never_becomes_a_genuine_original_receipt(string fault)
    {
        var (command, row, context, metadata, request, result) = LimitReceiptFixture(true);
        switch (fault)
        {
            case "company": command.CompanyId = CompanyB; break;
            case "limit": request = request with { LimitId = Guid.NewGuid() }; break;
            case "command": command.Command = FinanceCreditLimitServiceV1.ProposeCommand; break;
            case "actor": command.ActorId = Maker; break;
            case "key": command.KeyHash = "wrong"; break;
            case "hash": command.PayloadHash = "wrong"; break;
            case "amount": row.SourceJson = JsonSerializer.Serialize(result.Resource!.Source with { Amount = "999" }, FinanceJson.CreateOptions()); row.AmountText = "999"; break;
            case "row-amount": row.AmountText = "999"; break;
            case "source": row.SourceJson = JsonSerializer.Serialize(result.Resource!.Source with { Reason = "changed" }, FinanceJson.CreateOptions()); break;
            case "maker": row.ProposedBy = Checker; break;
            case "source-company": row.CompanyId = CompanyB; break;
            case "parent": row.CreditAccountId = Guid.NewGuid(); break;
            case "request-version": request = request with { ExpectedVersion = "2" }; break;
            case "version": result = result with { Resource = result.Resource! with { Version = "3" } }; command.ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()); break;
            case "correlation": result = result with { OriginalCorrelationId = "other" }; command.ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()); break;
            case "time-offset": result = result with { Resource = result.Resource! with { DecidedAtUtc = LimitDecisionAt.ToOffset(TimeSpan.FromHours(1)) } }; command.ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()); break;
            case "missing-resource": result = result with { Resource = null }; command.ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()); break;
            case "malformed-json": command.ResultJson = "{"; break;
            case "extra-json": command.ResultJson = command.ResultJson[..^1] + ",\"extra\":true}"; break;
            case "current-substitute": result = result with { Resource = result.Resource! with { SupersededById = Guid.NewGuid() } }; break;
            case "wrong-approve": request = request with { Approve = false, Reason = "SENTETIK ret" }; break;
        }
        Assert.Null(FinanceCreditLimitServiceV1.RestoreDecisionReceipt(command, row, context, metadata, request, result));
    }

    [Fact]
    public void Bank_limit_facts_digest_binds_full_source_and_maker_but_not_current_supersession_or_decision_projection()
    {
        var (_, row, _, _, _, result) = LimitReceiptFixture(true); var view = result.Resource!;
        var facts = FinanceCreditLimitServiceV1.SourceFactsDigest(view, row.ProposedBy);
        var later = view with { SupersededById = Guid.NewGuid(), Status = CreditAccountStatus.Proposed, Version = "1", IsOwnProposal = true, DecidedAtUtc = null };
        Assert.Equal(facts, FinanceCreditLimitServiceV1.SourceFactsDigest(later, row.ProposedBy));
        Assert.NotEqual(facts, FinanceCreditLimitServiceV1.SourceFactsDigest(view, "other"));
        Assert.NotEqual(facts, FinanceCreditLimitServiceV1.SourceFactsDigest(view with { Source = view.Source with { Amount = "999" } }, row.ProposedBy));
        Assert.NotEqual(FinanceCreditLimitServiceV1.DecisionPreviewDigest(new(view, row.ProposedBy, facts)), FinanceCreditLimitServiceV1.DecisionPreviewDigest(new(later, row.ProposedBy, facts)));
    }

    [Fact]
    public async Task Bank_limit_purpose_reads_one_native_record_under_approve_without_granting_general_credit_workspace()
    {
        await using var run = new Run(); var id = await LimitDecisionPrepare(run); await using var db = run.Memory(CompanyA);
        var permission = new LimitDecisionReadPermission(); var core = LimitSafetyCore(db, new LimitSafetyCompany(CompanyA), new LimitSafetyUser(), permission, null);
        var calls = 0; var answer = await core.ReadDecisionAsync(Ctx(Checker), id, _ => { calls++; return Task.CompletedTask; }, default);
        Assert.Equal(ReadOutcome.Found, answer.Outcome); Assert.Equal(8, calls); Assert.Equal(id, answer.Resource!.Limit.Id); Assert.Equal(Maker, answer.Resource.ProposedBy);
        Assert.Equal("1", answer.Resource.Limit.Version); Assert.Equal("100.00", answer.Resource.Limit.Source.Amount);
        Assert.All(permission.Actions, action => Assert.Equal(FinanceAction.ApproveCredit, action));
        Assert.Equal(ReadOutcome.Denied, (await core.ReadAsync(Ctx(Checker), answer.Resource.Limit.Source.CreditAccountId, default)).Outcome);
        Assert.False(db.ChangeTracker.HasChanges()); Assert.IsAssignableFrom<IFinanceBankCreditLimitDecisionReadV1>(core.WithApprovalSafety());
    }

    [Theory]
    [InlineData("maker")] [InlineData("foreign")] [InlineData("permission")] [InlineData("actor-drift")] [InlineData("company-drift")] [InlineData("membership")] [InlineData("consumer")]
    public async Task Bank_limit_human_read_refuses_maker_foreign_or_lost_fresh_authority_without_owner_writes(string fault)
    {
        await using var run = new Run(); var id = await LimitDecisionPrepare(run); await using var db = run.Memory(CompanyA);
        var company = new LimitSafetyCompany(CompanyA); var user = new LimitSafetyUser(); var permission = new LimitDecisionReadPermission();
        if (fault == "maker") user.Actor = Maker;
        var core = LimitSafetyCore(db, company, user, permission, null); var context = Ctx(fault == "maker" ? Maker : Checker);
        if (fault == "foreign") context = context with { CompanyId = CompanyB };
        var calls = 0;
        Task Consumer(CancellationToken t)
        {
            if (++calls == 3)
            {
                if (fault == "permission") permission.Allowed = false;
                if (fault == "actor-drift") user.Actor = Maker;
                if (fault == "company-drift") company.Set(CompanyB);
                if (fault == "membership") user.Member = false;
                if (fault == "consumer") throw new InvalidOperationException("SENTETIK consumer revoked");
            }
            return Task.CompletedTask;
        }
        if (fault == "consumer") await Assert.ThrowsAsync<InvalidOperationException>(() => core.ReadDecisionAsync(context, id, Consumer, default));
        else { var answer = await core.ReadDecisionAsync(context, id, Consumer, default); Assert.Equal(ReadOutcome.Denied, answer.Outcome); Assert.Null(answer.Resource); }
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Bank_limit_final_source_revision_check_refuses_a_mixed_pending_and_terminal_preview()
    {
        await using var run = new Run(); var id = await LimitDecisionPrepare(run); await using var db = run.Memory(CompanyA);
        var core = LimitSafetyCore(db, new LimitSafetyCompany(CompanyA), new LimitSafetyUser(), new LimitDecisionReadPermission(), null); var calls = 0;
        var answer = await core.ReadDecisionAsync(Ctx(Checker), id, async _ =>
        {
            if (++calls != 6) return;
            var decided = await run.Limit(Checker, owner => owner.DecideAsync(Ctx(Checker), new("limit-concurrent-decision"), new(id, "1", true, null), default));
            Assert.Equal(CommandOutcome.Succeeded, decided.Outcome);
        }, default);
        Assert.Equal(ReadOutcome.Unavailable, answer.Outcome); Assert.Null(answer.Resource); Assert.False(db.ChangeTracker.HasChanges());
    }

    private static async Task<Guid> LimitDecisionPrepare(Run run)
    {
        var account = await ApprovedCredit(run);
        var proposed = await run.Limit(Maker, owner => owner.ProposeAsync(Ctx(Maker), new("limit-purpose-prepare"), Limit(account), default));
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome); return proposed.Resource!.Id;
    }
    private sealed class LimitDecisionReadPermission : IFinancePermissionPort
    {
        public bool Allowed = true; public List<FinanceAction> Actions { get; } = [];
        public Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken t)
        { Actions.Add(a); return Task.FromResult(Allowed && a == FinanceAction.ApproveCredit ? PermissionDecision.Allowed : PermissionDecision.Denied); }
    }
    private static (FinanceCreditMovementCommandRow Command, FinanceCreditLimitRow Row, FinanceContext Context, CommandMetadata Metadata, CreditLimitDecisionV1 Request, CreditLimitResultV1 Result) LimitReceiptFixture(bool approve)
    {
        var id = Guid.NewGuid(); var source = new CreditLimitProposeV1(Guid.NewGuid(), "2", null, null, "00100.00", new("SENTETIK-bank-source", Start, "1"), "SENTETIK original source");
        var row = new FinanceCreditLimitRow { Id = id, CompanyId = CompanyA, CreditAccountId = source.CreditAccountId, LenderPartyId = Guid.NewGuid(),
            LenderCompanyPartyAccountId = Guid.NewGuid(), CreditGroup = 1, CurrencyId = Currency, Version = 2, Revision = 1, Status = approve ? 2 : 3,
            AmountText = source.Amount, SourceJson = JsonSerializer.Serialize(source, FinanceJson.CreateOptions()), ProposedBy = Maker, ProposedAtUtc = LimitDecisionAt.AddHours(1),
            DecidedBy = Checker, DecidedAtUtc = LimitDecisionAt, RejectionReason = approve ? null : "SENTETIK literal ret" };
        var context = Ctx(Checker); var metadata = new CommandMetadata("limit-receipt-key"); var request = new CreditLimitDecisionV1(id, "1", approve, row.RejectionReason);
        var view = new CreditLimitViewV1(id, CompanyA, row.LenderPartyId, row.LenderCompanyPartyAccountId, (CreditAccountGroup)0, Currency, "2",
            approve ? CreditAccountStatus.Approved : CreditAccountStatus.Rejected, source, false, row.ProposedAtUtc, row.DecidedAtUtc, row.RejectionReason, null);
        var result = new CreditLimitResultV1(CommandOutcome.Succeeded, view, false, context.CorrelationId, []);
        var command = new FinanceCreditMovementCommandRow { CompanyId = CompanyA, Command = FinanceCreditLimitServiceV1.DecideCommand,
            KeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey), ActorId = Checker, PayloadHash = FinanceCreditLimitServiceV1.DecisionPayloadHash(request), ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()) };
        return (command, row, context, metadata, request, result);
    }
}
