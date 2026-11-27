using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceRecordSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Native_receipt_reversal_original_command_receipt_binds_retained_recorded_facts_and_never_changes_public_wire()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var (source, proposal) = await ReceiptDecisionSafetyPrepare(run); await using var scope = new ReceiptDecisionSafetyScope(run);
            var read = Assert.IsAssignableFrom<IReceiptReversalDecisionReadV1>(scope.Service);
            var context = new FinanceContext(CompanyA, ReceiptChecker, scope.OriginalCorrelation, null);
            var preview = await read.ReadDecisionAsync(context, proposal, _ => Task.CompletedTask, default);
            Assert.Equal(ReadOutcome.Found, preview.Outcome); Assert.Equal(source, preview.Resource!.OriginalReceipt.SourceId);
            Assert.Equal(run.Actor, preview.Resource.OriginalReceipt.RecordedBy);
            Assert.Equal(await run.ScalarAsync<Guid>("SELECT Id FROM finance.MoneyEvents WHERE CompanyId=@c AND SourceId=@s AND Kind=1", ("@c", CompanyA), ("@s", source)), preview.Resource.OriginalReceipt.OriginalEventId);
            var digest = ReversalProposalService.RecordedFactsDigest(preview.Resource); var key = "receipt-proof-" + proposal;
            var first = await scope.Decide(proposal, approve, key); Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
            var proof = Assert.IsType<ReceiptReversalDecisionReceiptV1>(first.DecisionReceipt);
            Assert.Equal((CompanyA, proposal, source, "1", "2", ReceiptChecker, scope.OriginalCorrelation, digest),
                (proof.CompanyId, proof.ProposalId, proof.OriginalSourceId, proof.ExpectedVersion, proof.ResultVersion, proof.ActorId, proof.OriginalCorrelationId, proof.RecordedFactsDigest));
            var stored = await run.ScalarAsync<string>("SELECT ResultJson FROM finance.Commands WHERE CompanyId=@c AND ReversalProposalId=@p AND CommandName=@command",
                ("@c", CompanyA), ("@p", proposal), ("@command", proof.CommandName));
            Assert.Equal(stored, JsonSerializer.Serialize(first, FinanceJson.CreateOptions()));
            using var json = JsonDocument.Parse(stored); Assert.Equal(5, json.RootElement.EnumerateObject().Count());
            Assert.False(json.RootElement.TryGetProperty("decisionReceipt", out _));
            if (!approve)
            {
                await using var maker = run.ReversalService(CompanyA, run.Actor);
                var later = await maker.Service.ProposeAsync(maker.Context, Key("receipt-proof-later-" + source), new(source, "2", OccurredOn.AddDays(3), "SENTETIK later"), default);
                Assert.Equal(CommandOutcome.Succeeded, later.Outcome); Assert.NotEqual(proposal, later.Resource!.Id);
            }
            scope.Correlation = "SENTETIK-later-retry";
            var replay = await scope.Decide(proposal, approve, key); Assert.True(replay.IsReplay);
            Assert.Equal(first.Resource, replay.Resource); Assert.Equal(proof, replay.DecisionReceipt);
            var terminal = await read.ReadDecisionAsync(context, proposal, _ => Task.CompletedTask, default);
            Assert.Equal(ReadOutcome.Found, terminal.Outcome); Assert.Equal("2", terminal.Resource!.Proposal.Version);
            Assert.Equal(digest, ReversalProposalService.RecordedFactsDigest(terminal.Resource));
            Assert.NotEqual(ReversalProposalService.DecisionPreviewDigest(preview.Resource), ReversalProposalService.DecisionPreviewDigest(terminal.Resource));
            scope.AssertClean(); await ReceiptDecisionSafetyDurable(run, source, proposal, 2,
                approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
        }
    }

    [SqlServerFact]
    public async Task Native_receipt_reversal_each_receipt_read_fence_aborts_all_native_effects_and_same_key_later_executes_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var receiptFence in new[] { 2, 3, 4 })
        {
            var (source, proposal) = await ReceiptDecisionSafetyPrepare(run); var save = new ReceiptDecisionSafetySave(proposal);
            await using var scope = new ReceiptDecisionSafetyScope(run, save: save); var savedFences = 0; var deny = true;
            // After native Save (1): before Commands (2), after Commands/before retained facts (3), after facts (4).
            scope.Permissions.Check = () => !save.Saved || !deny || ++savedFences < receiptFence
                ? PermissionDecision.Allowed : PermissionDecision.Denied;
            var key = "receipt-fence-" + proposal;
            var refused = await scope.Decide(proposal, approve, key);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome); Assert.Null(refused.DecisionReceipt);
            Assert.Equal(receiptFence, savedFences); Assert.True(save.Saved); scope.AssertClean();
            await ReceiptDecisionSafetyDurable(run, source, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
            deny = false; save.Saved = false;
            var succeeded = await scope.Decide(proposal, approve, key);
            Assert.Equal(CommandOutcome.Succeeded, succeeded.Outcome); Assert.NotNull(succeeded.DecisionReceipt); scope.AssertClean();
            await ReceiptDecisionSafetyDurable(run, source, proposal, 2,
                approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
        }
    }

    [SqlServerFact]
    public async Task Native_receipt_reversal_human_read_rechecks_company_actor_permission_and_consumers_without_owner_writes()
    {
        await using var run = await Run.StartAsync(); var (_, proposal) = await ReceiptDecisionSafetyPrepare(run);
        await using var scope = new ReceiptDecisionSafetyScope(run); var reader = Assert.IsAssignableFrom<IReceiptReversalDecisionReadV1>(scope.Service);
        var context = new FinanceContext(CompanyA, ReceiptChecker, "receipt-preview", null);
        var commands = await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands");
        foreach (var phase in new[] { "permission", "actor", "company", "membership" })
        {
            var calls = 0;
            var result = await reader.ReadDecisionAsync(context, proposal, _ =>
            {
                if (++calls == 6)
                {
                    if (phase == "permission") scope.Permissions.Check = () => PermissionDecision.Denied;
                    if (phase == "actor") scope.User.Actor = "SENTETIK-other";
                    if (phase == "company") scope.Company.Set(CompanyB);
                    if (phase == "membership") scope.User.Member = false;
                }
                return Task.CompletedTask;
            }, default);
            Assert.Equal(ReadOutcome.Denied, result.Outcome); Assert.Null(result.Resource);
            scope.User.Actor = ReceiptChecker; scope.Company.Set(CompanyA); scope.User.Member = true; scope.Permissions.Check = null;
        }
        scope.User.Actor = run.Actor;
        Assert.Equal(ReadOutcome.Denied, (await reader.ReadDecisionAsync(context with { ActorId = run.Actor }, proposal, _ => Task.CompletedTask, default)).Outcome);
        scope.User.Actor = ReceiptChecker;
        Assert.Equal(ReadOutcome.Denied, (await reader.ReadDecisionAsync(context with { CompanyId = CompanyB }, proposal, _ => Task.CompletedTask, default)).Outcome);
        Assert.Equal(commands, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        scope.AssertClean();
    }
}
