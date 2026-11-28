using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesRequestSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Request_selected_source_uses_guarded_owner_and_keeps_Approved_rejection_separate_from_the_Submitted_queue()
    {
        await using var run = await Run.StartAsync(); var id = await SeedRequest(run); await using var safe = ApprovalScope(run);
        var source = RequestDecisionSource(safe); var context = new WorkInboxSourceContextV1(CompanyA, run.Actor, run.Correlation, null);
        var pending = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context, id, default));
        Assert.True(pending.Pending); Assert.Null(pending.Item.PreparedBy); Assert.False(pending.Item.Excludes(run.Actor));
        Assert.Equal(id, Assert.Single((await source.ListPendingAsync(context, 10, default)).Items!).ItemId);
        var approved = await safe.Service.ApproveRequestAsync(id, new("1"), run.Metadata("source-approve", "SENTETIK-source-original"));
        Assert.True(SalesRequestDecisionBindingV1.MatchesOriginalDecisionReceipt(pending, approved, SalesRequestCommands.ApproveV1, new("1"), "source-approve", run.Actor));
        var afterApproval = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context, id, default));
        Assert.Equal((false, "2", false, true), (afterApproval.Pending, afterApproval.SourceVersion, afterApproval.SalesRequestProof!.ApprovalSupported, afterApproval.SalesRequestProof.RejectionSupported));
        Assert.Empty((await source.ListPendingAsync(context, 10, default)).Items!);
        // SAT-D22 permits the request maker. SAT-D23 still runs its native active-order block inside RejectRequestAsync.
        var rejected = await safe.Service.RejectRequestAsync(id, new("2", "  SENTETIK ret  "), run.Metadata("source-reject"));
        Assert.True(SalesRequestDecisionBindingV1.MatchesOriginalDecisionReceipt(afterApproval, rejected, SalesRequestCommands.RejectV1, new("2", "  SENTETIK ret  "), "source-reject", run.Actor));
        var terminal = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context, id, default));
        Assert.Equal((false, "3", false, false), (terminal.Pending, terminal.SourceVersion, terminal.SalesRequestProof!.ApprovalSupported, terminal.SalesRequestProof.RejectionSupported));
        var original = await safe.Service.ApproveRequestAsync(id, new("1"), run.Metadata("source-approve", "SENTETIK-reconcile"));
        Assert.Equal((true, "Rejected", "3", "Approved", "2", "SENTETIK-source-original"),
            (original.IsReplay, original.Detail.Request.State, original.Detail.Request.Version, original.OriginalDecisionReceipt!.State,
                original.OriginalDecisionReceipt.ResultVersion, original.OriginalCorrelationId));
        Assert.True(SalesRequestDecisionBindingV1.MatchesOriginalDecisionReceipt(terminal, original, SalesRequestCommands.ApproveV1, new("1"), "source-approve", run.Actor));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(terminal, SalesDraftJson.Options));
        Assert.False(json.RootElement.TryGetProperty("salesRequestProof", out _));
        Assert.Equal(2, await DecisionCount(run, id)); Assert.Equal(2, await DecisionAuditCount(run, id));
        Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
    }

    [SqlServerFact]
    public async Task Request_source_binding_rejects_wrong_identity_key_payload_or_observed_response_without_second_read_or_write()
    {
        await using var run = await Run.StartAsync(); var id = await SeedRequest(run); await using var safe = ApprovalScope(run);
        var context = new WorkInboxSourceContextV1(CompanyA, run.Actor, run.Correlation, null);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await RequestDecisionSource(safe).ReadDecisionAsync(context, id, default));
        var response = await safe.Service.ApproveRequestAsync(id, new("1"), run.Metadata("binding-original"));
        var receipt = Assert.IsType<SalesRequestOriginalDecisionReceiptV1>(response.OriginalDecisionReceipt);
        Assert.True(Match(snapshot, response, "1", "binding-original", run.Actor));
        Assert.False(Match(snapshot, response with { OriginalDecisionReceipt = null }, "1", "binding-original", run.Actor));
        Assert.False(Match(snapshot, response, "1", "other-key", run.Actor));
        Assert.False(Match(snapshot, response, "1", "binding-original", "other-actor"));
        Assert.False(Match(snapshot, response, "2", "binding-original", run.Actor));
        Assert.False(Match(snapshot with { CompanyId = CompanyB }, response, "1", "binding-original", run.Actor));
        Assert.False(Match(snapshot with { SalesRequestProof = snapshot.SalesRequestProof! with { PartyId = Guid.NewGuid() } }, response, "1", "binding-original", run.Actor));
        Assert.False(Match(snapshot with { Item = snapshot.Item with { References = [snapshot.Item.References[0] with { RecordId = Guid.NewGuid() }] } }, response, "1", "binding-original", run.Actor));
        Assert.False(Match(snapshot, response with { OriginalDecisionReceipt = receipt with { CurrentResponseDigest = new('a', 64) } }, "1", "binding-original", run.Actor));
        Assert.False(Match(snapshot, response with { Detail = response.Detail with { Note = "different current response" } }, "1", "binding-original", run.Actor));
        Assert.Equal(1, await DecisionCount(run, id)); Assert.Equal(1, await DecisionAuditCount(run, id));
        Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
        static bool Match(WorkApprovalDecisionSnapshotV1 selected, SalesRequestResponse answer, string version, string key, string actor) =>
            SalesRequestDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, answer, SalesRequestCommands.ApproveV1, new(version), key, actor);
        // Match calls above are pure protocol checks; they do not issue an owner-human GET or mutate/repair a receipt.
    }

    private static SalesRequestWorkInboxSourceV1 RequestDecisionSource(RequestApprovalScope safe)
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        return new(new SalesWorkInboxApprovalStoreV1(safe.Db), company, safe.User, safe.Authority, safe.Service);
    }
}
