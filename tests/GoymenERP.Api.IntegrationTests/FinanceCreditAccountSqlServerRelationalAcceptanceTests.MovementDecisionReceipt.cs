using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    // Actual migrated SQL and the existing SAME scoped Audit writer. Permission/settings remain controlled owner ports.
    [SqlServerFact]
    public async Task Credit_movement_decision_receipt_binds_native_saved_json_and_original_key_after_a_later_opening_correction()
    {
        await using var run = await Run.StartAsync(); var account = await ApprovedMovementAccount(run);
        var input = ActualInput(account) with { Kind = CreditMovementKindV1.OpeningBalance }; Guid original;
        await using (var maker = run.OpenMovement(Maker))
            original = (await maker.Service.ProposeAsync(maker.Context, Key("receipt-opening-prepare"), input, default)).Resource!.Id;
        await using var scope = new MovementDecisionScope(run); var native = await scope.Decide(original, "receipt-opening-decide", true);
        Assert.Equal(CommandOutcome.Succeeded, native.Outcome); var proof = Assert.IsType<FinanceCreditMovementDecisionReceiptV1>(native.DecisionReceipt);
        Assert.Equal((CompanyA, original, Checker, "1", "2", scope.OriginalCorrelation),
            (proof.CompanyId, proof.MovementId, proof.ActorId, proof.ExpectedVersion, proof.ResultVersion, proof.OriginalCorrelationId));
        var stored = await run.ScalarAsync<string>("SELECT ResultJson FROM finance.CreditMovementCommands WHERE CompanyId=@c AND Command=@command AND KeyHash=@key",
            ("@c", CompanyA), ("@command", FinanceCreditMovementServiceV1.DecideCommand), ("@key", FinanceCommandKeys.Hash("receipt-opening-decide")));
        Assert.Equal(stored, JsonSerializer.Serialize(native, FinanceJson.CreateOptions()));
        using (var json = JsonDocument.Parse(stored)) { Assert.Equal(5, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("decisionReceipt", out _)); }
        Guid replacement;
        await using (var maker = run.OpenMovement(Maker)) replacement = (await maker.Service.ProposeAsync(maker.Context, Key("receipt-correction-prepare"), input with
        { ReplacesOpeningMovementId = original, ExpectedOpeningMovementVersion = "2", SourceDocument = input.SourceDocument with { Revision = "2" }, Components = [new("principal", "105.00", null)] }, default)).Resource!.Id;
        Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(replacement, "receipt-correction-decide", true)).Outcome);
        var replay = await scope.Decide(original, "receipt-opening-decide", true);
        Assert.True(replay.IsReplay); Assert.Equal(proof, replay.DecisionReceipt); Assert.Equal("2", replay.Resource!.Version); Assert.Null(replay.Resource.ReversedByMovementId);
        var reader = Assert.IsAssignableFrom<IFinanceCreditMovementDecisionReadV1>(scope.Service);
        var current = await reader.ReadDecisionAsync(new(CompanyA, Checker, scope.OriginalCorrelation, null), original, _ => Task.CompletedTask, default);
        Assert.Equal(ReadOutcome.Found, current.Outcome); Assert.Equal("3", current.Resource!.Movement.Version); Assert.Equal(replacement, current.Resource.Movement.ReversedByMovementId);
        Assert.Equal(proof.SourceFactsDigest, current.Resource.SourceFactsDigest);
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovementCommands WHERE Command=N'finance.credit-movement.decide'"));
        scope.AssertClean();
    }

    [SqlServerFact]
    public async Task Credit_movement_receipt_read_boundaries_roll_back_the_native_decision_audit_outbox_and_key_then_retry_once()
    {
        foreach (var boundary in new[] { 2, 3 }) // post-Save=1; receipt before-query=2 and after-query=3, BEFORE commit.
        foreach (var approve in new[] { true, false })
        {
            await using var run = await Run.StartAsync(); var id = await MovementSafetyPrepare(run); var save = new MovementDecisionSave(id);
            await using var scope = new MovementDecisionScope(run, save: save); var fences = 0; var deny = true;
            scope.Permission.Check = () => save.Saved && ++fences == boundary && deny ? PermissionDecision.Denied : PermissionDecision.Allowed;
            var answer = await scope.Decide(id, "receipt-middle-fence", approve);
            Assert.True(save.Saved); Assert.Equal(boundary, fences); Assert.Equal(CommandOutcome.Denied, answer.Outcome); Assert.Null(answer.DecisionReceipt);
            scope.AssertClean(); await MovementSafetyDurable(run, id, null);
            deny = false; save.Saved = false; var success = await scope.Decide(id, "receipt-middle-fence", approve); var replay = await scope.Decide(id, "receipt-middle-fence", approve);
            Assert.Equal(CommandOutcome.Succeeded, success.Outcome); Assert.NotNull(success.DecisionReceipt); Assert.True(replay.IsReplay); Assert.Equal(success.DecisionReceipt, replay.DecisionReceipt);
            scope.AssertClean(); await MovementSafetyDurable(run, id, approve);
        }
    }

    [SqlServerFact]
    public async Task Credit_movement_final_consumer_normal_completion_after_revocation_or_cancellation_never_releases_human_facts()
    {
        foreach (var cancel in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var id = await MovementSafetyPrepare(run); await using var scope = new MovementDecisionScope(run);
            var reader = Assert.IsAssignableFrom<IFinanceCreditMovementDecisionReadV1>(scope.Service); var allowed = true; var callbacks = 0; using var cancellation = new CancellationTokenSource();
            scope.Permission.Check = () => allowed ? PermissionDecision.Allowed : PermissionDecision.Denied;
            var read = () => reader.ReadDecisionAsync(new(CompanyA, Checker, scope.OriginalCorrelation, null), id, _ =>
            { if (++callbacks == 6) { if (cancel) cancellation.Cancel(); else allowed = false; } return Task.CompletedTask; }, cancellation.Token);
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(read);
            else { var result = await read(); Assert.Equal(ReadOutcome.Denied, result.Outcome); Assert.Null(result.Resource); }
            Assert.Equal(6, callbacks); scope.AssertClean(); await MovementSafetyDurable(run, id, null);
        }
    }
}
