using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    // Real migrated native decisions and same-scope actual Audit writer; authority/lender ports remain controlled.
    [SqlServerFact]
    public async Task Bank_limit_original_decision_receipt_survives_a_later_distinct_proposal_and_approved_supersession()
    {
        foreach (var approve in new[] { true, false })
        {
            await using var run = await Run.StartAsync(); var id = await LimitSafetyPrepare(run);
            await using var scope = new LimitDecisionScope(run); var reader = Assert.IsAssignableFrom<IFinanceBankCreditLimitDecisionReadV1>(scope.Service);
            var context = new FinanceContext(CompanyA, Checker, scope.OriginalCorrelation, null);
            var pending = await reader.ReadDecisionAsync(context, id, _ => Task.CompletedTask, default);
            Assert.Equal(ReadOutcome.Found, pending.Outcome); Assert.Equal("1", pending.Resource!.Limit.Version); Assert.Equal(Maker, pending.Resource.ProposedBy);
            var first = await scope.Decide(id, "limit-original-purpose", approve);
            Assert.Equal(CommandOutcome.Succeeded, first.Outcome); var proof = Assert.IsType<FinanceBankCreditLimitDecisionReceiptV1>(first.DecisionReceipt);
            Assert.Equal((CompanyA, id, "1", "2", Checker, scope.OriginalCorrelation),
                (proof.CompanyId, proof.LimitId, proof.ExpectedVersion, proof.ResultVersion, proof.ActorId, proof.OriginalCorrelationId));
            Assert.Equal(pending.Resource.SourceFactsDigest, proof.SourceFactsDigest); Assert.Equal(first.Resource!.DecidedAtUtc, proof.DecidedAtUtc);
            var command = await scope.Db.CreditMovementCommands.AsNoTracking().SingleAsync(x => x.CompanyId == CompanyA && x.Command == FinanceCreditLimitServiceV1.DecideCommand);
            Assert.Equal(command.ResultJson, JsonSerializer.Serialize(first, FinanceJson.CreateOptions())); Assert.Equal(command.PayloadHash, proof.PayloadHash);
            Assert.Equal(FinanceCreditLimitServiceV1.OriginalReceiptDigest(first), proof.OriginalReceiptDigest);
            using var json = JsonDocument.Parse(command.ResultJson); Assert.Equal(5, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("decisionReceipt", out _));
            var nextSource = pending.Resource.Limit.Source with { Amount = "75.000", Reason = "SENTETIK later proposal",
                SourceDocument = pending.Resource.Limit.Source.SourceDocument with { Revision = "2" },
                ExpectedCurrentLimitId = approve ? id : null, ExpectedCurrentLimitVersion = approve ? "2" : null };
            Guid nextId;
            await using (var maker = run.OpenLimit(Maker))
            {
                var next = await maker.Service.ProposeAsync(maker.Context, Key("limit-next-purpose"), nextSource, default);
                Assert.Equal(CommandOutcome.Succeeded, next.Outcome); nextId = next.Resource!.Id; Assert.NotEqual(id, nextId);
            }
            await using (var nextChecker = new LimitDecisionScope(run))
                Assert.Equal(CommandOutcome.Succeeded, (await nextChecker.Decide(nextId, "limit-next-approved", true)).Outcome);
            var current = await reader.ReadDecisionAsync(context, id, _ => Task.CompletedTask, default);
            Assert.Equal(ReadOutcome.Found, current.Outcome); Assert.Equal(proof.SourceFactsDigest, current.Resource!.SourceFactsDigest);
            Assert.Equal(approve ? (Guid?)nextId : null, current.Resource.Limit.SupersededById);
            var replay = await scope.Decide(id, "limit-original-purpose", approve);
            Assert.Equal(CommandOutcome.Succeeded, replay.Outcome); Assert.True(replay.IsReplay); Assert.Equal(proof, replay.DecisionReceipt);
            Assert.Equal(first.Resource, replay.Resource); Assert.Null(replay.Resource!.SupersededById);
            Assert.Equal(scope.OriginalCorrelation, replay.OriginalCorrelationId); scope.AssertClean();
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@c AND EntityType=N'Finance.CreditLimit' AND EntityId=@id AND EventType=N'finance.credit-limit.decide.v1'", ("@c", CompanyA), ("@id", id.ToString("D"))));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovements"));
        }
    }

    [SqlServerFact]
    public async Task Bank_limit_receipt_query_fences_roll_back_native_decision_command_and_audit_then_same_key_executes_once()
    {
        foreach (var approve in new[] { true, false })
        foreach (var boundary in new[] { 2, 3 })
        {
            await using var run = await Run.StartAsync(); var id = await LimitSafetyPrepare(run); var save = new LimitDecisionSave(id);
            await using var scope = new LimitDecisionScope(run, save: save); var calls = 0; var deny = true;
            // Native Save after-fence (1), immediately before original Commands/source join (2), after join (3).
            scope.Permission.Check = () => !save.Saved || !deny || ++calls < boundary ? PermissionDecision.Allowed : PermissionDecision.Denied;
            var answer = await scope.Decide(id, "limit-receipt-refusal", approve);
            Assert.Equal(CommandOutcome.Denied, answer.Outcome); Assert.Null(answer.DecisionReceipt); Assert.True(save.Saved); Assert.Equal(boundary, calls);
            scope.AssertClean(); await LimitSafetyDurable(run, id, null);
            deny = false; save.Saved = false;
            var success = await scope.Decide(id, "limit-receipt-refusal", approve); Assert.Equal(CommandOutcome.Succeeded, success.Outcome); Assert.NotNull(success.DecisionReceipt);
            scope.AssertClean(); await LimitSafetyDurable(run, id, approve);
        }
    }

    [SqlServerFact]
    public async Task Bank_limit_purpose_read_keeps_approve_authority_maker_company_and_every_consumer_boundary_without_new_commands()
    {
        await using var run = await Run.StartAsync(); var id = await LimitSafetyPrepare(run); await using var scope = new LimitDecisionScope(run);
        var reader = Assert.IsAssignableFrom<IFinanceBankCreditLimitDecisionReadV1>(scope.Service);
        var context = new FinanceContext(CompanyA, Checker, scope.OriginalCorrelation, null);
        var commands = await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovementCommands");
        scope.User.Actor = Maker;
        Assert.Equal(ReadOutcome.Denied, (await reader.ReadDecisionAsync(context with { ActorId = Maker }, id, _ => Task.CompletedTask, default)).Outcome);
        scope.User.Actor = Checker;
        Assert.Equal(ReadOutcome.Denied, (await reader.ReadDecisionAsync(context with { CompanyId = CompanyB }, id, _ => Task.CompletedTask, default)).Outcome);
        foreach (var boundary in new[] { 3, 5, 7 })
        {
            var calls = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadDecisionAsync(context, id,
                _ => { if (++calls == boundary) throw new InvalidOperationException("SENTETIK consumer revoked"); return Task.CompletedTask; }, default));
            Assert.Equal(boundary, calls); scope.AssertClean();
        }
        Assert.Equal(commands, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovementCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovementCommands WHERE Command=N'finance.credit-limit.decide'"));
    }
}
