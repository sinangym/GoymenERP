using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceOpeningBalanceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Opening_four_decisions_attach_only_genuine_original_command_receipts_and_keep_native_five_field_wire()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in Enumerable.Range(0, 4))
        {
            var id = await OpeningSafetyPrepare(run, operation);
            await using var scope = new OpeningSafetyScope(run);
            var key = "opening-proof-" + id;
            var first = await scope.Decide(id, operation, key);
            Assert.Equal(CommandOutcome.Succeeded, first.Outcome); Assert.False(first.IsReplay);
            var proof = Assert.IsType<OpeningBalanceDecisionReceiptV1>(first.DecisionReceipt);
            Assert.Equal(CompanyA, proof.CompanyId); Assert.Equal(id, proof.OpeningBalanceId);
            Assert.Equal(OpeningSafetyCommand(operation), proof.CommandName); Assert.Equal(Manager, proof.ActorId);
            Assert.Equal("1", proof.ExpectedVersion); Assert.Equal("2", proof.ResultVersion);
            Assert.Equal(first.OriginalCorrelationId, proof.OriginalCorrelationId);
            Assert.Equal(FinanceOpeningBalanceService.OriginalReceiptDigest(first), proof.OriginalReceiptDigest);
            Assert.Equal(first.Resource!.Reversal?.Id, proof.ReversalId);
            Assert.Equal(operation >= 2, proof.ReversalFactsDigest is not null);
            var originalJson = await run.ScalarAsync<string>("SELECT ResultJson FROM finance.OpeningBalanceCommands WHERE CompanyId=@company AND OpeningBalanceId=@id AND CommandName=@command",
                ("@company", CompanyA), ("@id", id), ("@command", OpeningSafetyCommand(operation)));
            Assert.Equal(originalJson, JsonSerializer.Serialize(first, FinanceJson.CreateOptions()));
            using (var wire = JsonDocument.Parse(originalJson))
            { Assert.Equal(5, wire.RootElement.EnumerateObject().Count()); Assert.False(wire.RootElement.TryGetProperty("decisionReceipt", out _)); }
            // A later real reversal changes the live opening to Reversed/3, not the first Approved/2 receipt.
            if (operation == 0)
            {
                await OpeningSafetyProposeReversal(run, id); await using var later = new OpeningSafetyScope(run);
                Assert.Equal(CommandOutcome.Succeeded, (await later.Decide(id, 2, "opening-proof-later-" + id)).Outcome);
            }
            scope.Correlation = "SENTETIK-later-request";
            var replay = await scope.Decide(id, operation, key);
            Assert.Equal(CommandOutcome.Succeeded, replay.Outcome); Assert.True(replay.IsReplay);
            Assert.Equal(proof, replay.DecisionReceipt); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(JsonSerializer.Serialize(first.Resource, FinanceJson.CreateOptions()), JsonSerializer.Serialize(replay.Resource, FinanceJson.CreateOptions()));
            Assert.Equal(originalJson, await run.ScalarAsync<string>("SELECT ResultJson FROM finance.OpeningBalanceCommands WHERE CompanyId=@company AND OpeningBalanceId=@id AND CommandName=@command",
                ("@company", CompanyA), ("@id", id), ("@command", OpeningSafetyCommand(operation))));
            scope.AssertClean();
        }
    }

    [SqlServerFact]
    public async Task Opening_bound_rejected_reversal_A_never_decides_new_B_with_the_same_version_but_original_A_key_replays_A()
    {
        await using var run = await Run.StartAsync(); var id = await OpeningSafetyPrepare(run, 3);
        await using var scope = new OpeningSafetyScope(run);
        var reader = Assert.IsAssignableFrom<IFinanceOpeningBalanceDecisionReadV1>(scope.Service);
        var decisions = Assert.IsAssignableFrom<IFinanceOpeningBalanceReversalDecisionV1>(scope.Service);
        var a = await OpeningPendingReversalId(run, id); var key = "opening-bound-A-" + id;
        var context = new FinanceContext(CompanyA, Manager, scope.Correlation, "SENTETIK-bound-cause");
        var rejected = await decisions.RejectReversalDecisionAsync(context, new(key), a, new(id, "1", "SENTETIK ret"), default);
        Assert.Equal(CommandOutcome.Succeeded, rejected.Outcome); Assert.Equal(a, rejected.Resource!.Reversal!.Id);
        Assert.NotNull(rejected.DecisionReceipt); Assert.Equal(a, rejected.DecisionReceipt.ReversalId);
        await OpeningSafetyProposeReversal(run, id); var b = await OpeningPendingReversalId(run, id); Assert.NotEqual(a, b);
        var aRead = await reader.ReadReversalDecisionAsync(context, a, _ => Task.CompletedTask, default);
        Assert.Equal(ReadOutcome.Found, aRead.Outcome); Assert.Equal(a, aRead.Resource!.OpeningBalance.Reversal!.Id);
        Assert.Equal(OpeningBalanceReversalStatus.Rejected, aRead.Resource.OpeningBalance.Reversal.Status);
        Assert.Equal(a, aRead.Resource.DecisionProof!.ReversalId); Assert.Equal("2", aRead.Resource.DecisionProof.Version);
        foreach (var approve in new[] { false, true })
        {
            var staleKey = "opening-bound-stale-" + approve + id;
            var refusal = approve
                ? await decisions.ApproveReversalDecisionAsync(context, new(staleKey), a, new(id, "1"), default)
                : await decisions.RejectReversalDecisionAsync(context, new(staleKey), a, new(id, "1", "SENTETIK ret"), default);
            Assert.Equal(CommandOutcome.VersionConflict, refusal.Outcome); Assert.Null(refusal.Resource); Assert.Null(refusal.DecisionReceipt);
            Assert.Equal("opening_balance_reversal_changed", Assert.Single(refusal.Issues).Code);
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpeningBalanceCommands WHERE CompanyId=@company AND OpeningBalanceId=@id AND IdempotencyKeyHash=@key",
                ("@company", CompanyA), ("@id", id), ("@key", FinanceCommandKeys.Hash(staleKey))));
            scope.AssertClean();
        }
        // The native public body/hash is unchanged; the Work-only bound ID must also reject reusing A's key under B.
        var wrongTargetReplay = await decisions.RejectReversalDecisionAsync(context, new(key), b, new(id, "1", "SENTETIK ret"), default);
        Assert.Equal(CommandOutcome.Unavailable, wrongTargetReplay.Outcome); Assert.Null(wrongTargetReplay.Resource); Assert.Null(wrongTargetReplay.DecisionReceipt);
        var replay = await decisions.RejectReversalDecisionAsync(context with { CorrelationId = "SENTETIK-old-A-replay" }, new(key), a, new(id, "1", "SENTETIK ret"), default);
        Assert.Equal(CommandOutcome.Succeeded, replay.Outcome); Assert.True(replay.IsReplay);
        Assert.Equal(rejected.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.Equal(rejected.DecisionReceipt, replay.DecisionReceipt);
        Assert.Equal(a, replay.Resource!.Reversal!.Id); Assert.Equal(OpeningBalanceReversalStatus.Rejected, replay.Resource.Reversal.Status);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM finance.OpeningBalanceReversals WHERE CompanyId=@company AND Id=@id", ("@company", CompanyA), ("@id", b)));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM finance.OpeningBalanceReversals WHERE CompanyId=@company AND Id=@id", ("@company", CompanyA), ("@id", b)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpeningBalanceMovements WHERE CompanyId=@company AND OpeningBalanceId=@id", ("@company", CompanyA), ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@company AND EntityId=@id AND EventType='finance.opening-balance.reversal-rejected.v1'",
            ("@company", CompanyA), ("@id", id.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@company AND EntityId=@id AND EventType='finance.opening-balance.reversal-approved.v1'",
            ("@company", CompanyA), ("@id", id.ToString("D"))));
        scope.AssertClean();
    }

    [SqlServerFact]
    public async Task Opening_receipt_before_after_commands_and_after_source_facts_revocation_roll_back_native_effect_attempt_and_actual_audit()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in Enumerable.Range(0, 4))
        foreach (var revokeAt in new[] { 2, 3, 4 })
        {
            var id = await OpeningSafetyPrepare(run, operation); var probe = new OpeningSafetySaveProbe(id, operation);
            await using var scope = new OpeningSafetyScope(run, probe: probe); var calls = 0; var deny = true;
            scope.Authority.BeforeRead = () => !probe.Saved || ++calls != revokeAt || !deny ? PermissionDecision.Allowed : PermissionDecision.Denied;
            var key = "opening-proof-fence-" + revokeAt + id;
            var refused = await scope.Decide(id, operation, key);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome); Assert.Null(refused.Resource); Assert.Null(refused.DecisionReceipt);
            Assert.True(probe.Saved); Assert.Equal(revokeAt, calls); scope.AssertClean();
            await OpeningSafetyDurable(run, id, operation, decided: false);
            deny = false; probe.Saved = false;
            var succeeded = await scope.Decide(id, operation, key);
            Assert.Equal(CommandOutcome.Succeeded, succeeded.Outcome); Assert.False(succeeded.IsReplay); Assert.NotNull(succeeded.DecisionReceipt);
            var replay = await scope.Decide(id, operation, key);
            Assert.True(replay.IsReplay); Assert.Equal(succeeded.DecisionReceipt, replay.DecisionReceipt);
            Assert.Equal(succeeded.OriginalCorrelationId, replay.OriginalCorrelationId);
            scope.AssertClean(); await OpeningSafetyDurable(run, id, operation, decided: true);
        }
    }

    private static Task<Guid> OpeningPendingReversalId(Run run, Guid id) => run.ScalarAsync<Guid>(
        "SELECT Id FROM finance.OpeningBalanceReversals WHERE CompanyId=@company AND OpeningBalanceId=@id AND Status=1",
        ("@company", CompanyA), ("@id", id));
}
