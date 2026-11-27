using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Dispatch_Work_purpose_uses_real_owner_source_service_writer_and_retained_original_receipt_after_later_native_changes()
    {
        await using var run = await Run.StartAsync(); var ids = await DraftedInstructionAsync(run, Owners.For(CompanyA));
        await using var scope = DispatchApprovalScope(run);
        const string checker = "SENTETIK-purpose-checker";
        var user = new WorkInventoryTransferLossDecisionHostTests.LossUser(CompanyA, checker);
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-dispatch-purpose", null, "Web", null, null);
        var execution = new WorkDecisionExecutionFenceV1();
        var authority = new WorkDecisionAuthorityV1(new WorkInventoryTransferLossDecisionHostTests.LossAuthority(), execution);
        var source = new LogisticsDispatchNoteWorkInboxSourceV1(scope.Context, new LogisticsWorkInboxFence(scope.Company, user, authority));
        var native = new LogisticsInstructionService(scope.Company, user, authority, correlation, null!, null!, null!, scope.Store, new(), TimeProvider.System);
        var host = new WorkDecisionHostV1(scope.Company, user, correlation, authority, [source], new DispatchPurposeNoDelegations(), execution,
            null!, null!, logisticsInstructions: native);
        var preview = await host.PreviewAsync(source.Kind, ids.Note, default);
        Assert.True(preview.Pending); Assert.False(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        Assert.Equal(ids.Id, preview.LogisticsDispatchNote!.InstructionId); Assert.NotEmpty(preview.LogisticsDispatchNote.Lines);
        Assert.Single(preview.References); Assert.Single(preview.Rules);
        var request = new WorkDecisionRequestV1(preview.SourceVersion, true, null);
        var first = await host.DecideAsync(source.Kind, ids.Note, request, "dispatch-work-original", default);
        Assert.Equal(("Approved", false), (first.State, first.Replayed));
        var version = long.Parse(preview.SourceVersion, System.Globalization.CultureInfo.InvariantCulture);
        await scope.Store.MutateInstructionAsync(run.Envelope("dispatch-work-e-number"), Hash("work-e-number"), LogisticsCommands.SetEDispatchNumberV1, ids.Id,
            (item, now) => item.SetEDispatchNumber(version + 1, ids.Note, "GYM2026000000001", run.Actor, now), EDispatchRecorded);
        await scope.Store.MutateInstructionAsync(run.Envelope("dispatch-work-cancel"), Hash("work-cancel"), LogisticsCommands.CancelDispatchNoteV1, ids.Id,
            (item, now) => item.CancelDispatchNote(version + 2, ids.Note, run.Actor, "SENTETIK correction", now), Cancelled);
        var changed = await host.PreviewAsync(source.Kind, ids.Note, default);
        Assert.False(changed.Pending); Assert.Equal("Cancelled", changed.LogisticsDispatchNote!.Status);
        Assert.Equal((version + 3).ToString(System.Globalization.CultureInfo.InvariantCulture), changed.SourceVersion);
        var replay = await host.DecideAsync(source.Kind, ids.Note, request, "dispatch-work-original", default);
        Assert.Equal(("Approved", true, first.OriginalCorrelationId), (replay.State, replay.Replayed, replay.OriginalCorrelationId));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE InstructionId=@id AND CommandName=@command", ("@id", ids.Id), ("@command", LogisticsCommands.ApproveDispatchNoteV1)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'logistics.loading-instruction.dispatch-note-approved.v1'", ("@id", ids.Id.ToString("D"))));
    }
    private sealed class DispatchPurposeNoDelegations : IWorkDelegationReaderV1
    {
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) =>
            throw new InvalidOperationException("No delegation was supplied; must not invent one.");
    }
}
