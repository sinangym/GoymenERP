using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    // Depends on the native WeighingSafety increment's actual writer-bound KilogramStore fixture.
    // Native source/store/domain calculator and immutable receipt are real; permission/delegation is a controlled fixture.
    [SqlServerFact]
    public async Task Work_weighing_native_approval_and_old_key_replay_survive_later_native_note_cancellation_without_new_audit()
    {
        await using var run = await Run.StartAsync(); var note = await KilogramNoteAsync(run);
        await using var scope = KilogramStore(run, CompanyA);
        var source = (await scope.Store.ReadSourceAsync(note))!; var record = WeighingPurposePendingRecord(run, source);
        await scope.Store.StoreAsync(run.Envelope("weigh-work-record"), Hash("weigh-work-record"), record, source, _ => Task.CompletedTask);
        var user = new PlanExcessWorkUser(); var host = WeighingPurposeHost(scope, user, () => true);
        var preview = await host.PreviewAsync(WorkDecisionHostV1.LogisticsWeighingDifference, record.RecordId, default);
        Assert.True(preview.Pending); Assert.Equal("1", preview.SourceVersion); Assert.False(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        var body = new WorkDecisionRequestV1("1", true, null); var key = "weigh-work-native-original";
        var approved = await host.DecideAsync(WorkDecisionHostV1.LogisticsWeighingDifference, record.RecordId, body, key, default);
        Assert.Equal(("Approved", false), (approved.State, approved.Replayed));
        await using (var instruction = run.Store(CompanyA))
            await instruction.Store.MutateInstructionAsync(run.Envelope("weigh-work-later-note"), Hash("weigh-work-later-note"), LogisticsCommands.CancelDispatchNoteV1,
                source.InstructionId, (item, now) => item.CancelDispatchNote(item.Version, note, run.Actor, "SENTETIK later cancellation", now),
                new LogisticsMutationAudit("logistics.loading-instruction.dispatch-note-cancelled.v1", "CancelDispatchNote", "SENTETIK later cancellation"));
        Assert.False((await scope.Store.ReadSourceAsync(note))!.IsDraft);
        var terminal = await host.PreviewAsync(WorkDecisionHostV1.LogisticsWeighingDifference, record.RecordId, default);
        Assert.False(terminal.Pending); Assert.Equal("2", terminal.SourceVersion);
        Assert.Equal(approved with { Replayed = true }, await host.DecideAsync(WorkDecisionHostV1.LogisticsWeighingDifference, record.RecordId, body, key, default));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.WeighingDifferenceAttempts WHERE RecordId=@id", ("@id", record.RecordId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Logistics.WeighingDifference' AND EntityId=@id", ("@id", record.RecordId.ToString("D"))));
        Assert.False(scope.Writer.HasPendingDrafts);
    }

    [SqlServerFact]
    public async Task Work_weighing_delegation_revoked_after_actual_owner_save_rolls_back_native_approval_receipt_and_audit()
    {
        await using var run = await Run.StartAsync(); var note = await KilogramNoteAsync(run);
        await using var scope = KilogramStore(run, CompanyA);
        var source = (await scope.Store.ReadSourceAsync(note))!; var record = WeighingPurposePendingRecord(run, source);
        await scope.Store.StoreAsync(run.Envelope("weigh-work-revoke-record"), Hash("weigh-work-revoke-record"), record, source, _ => Task.CompletedTask);
        var saved = false;
        bool Allowed()
        {
            if (scope.Context.ChangeTracker.Entries<WeighingDifferenceRecordRow>().Any(row => row.Entity.RecordId == record.RecordId
                && row.Entity.Status == 3 && row.State == EntityState.Unchanged)) saved = true;
            return !saved;
        }
        var user = new PlanExcessWorkUser(); var host = WeighingPurposeHost(scope, user, Allowed);
        var body = new WorkDecisionRequestV1("1", true, null, Guid.NewGuid()); var key = "weigh-work-revoke-approve";
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(WorkDecisionHostV1.LogisticsWeighingDifference,
            record.RecordId, body, key, default))).Status);
        Assert.True(saved); Assert.Empty(scope.Context.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
        Assert.Null(scope.Context.Database.CurrentTransaction);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.WeighingDifferenceAttempts WHERE RecordId=@id", ("@id", record.RecordId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Logistics.WeighingDifference' AND EntityId=@id", ("@id", record.RecordId.ToString("D"))));
        var retry = WeighingPurposeHost(scope, user, () => true);
        Assert.Equal("Approved", (await retry.DecideAsync(WorkDecisionHostV1.LogisticsWeighingDifference, record.RecordId, body, key, default)).State);
    }

    [SqlServerFact]
    public async Task Work_weighing_company_recorder_and_current_permission_refusals_never_create_an_approval_attempt()
    {
        await using var run = await Run.StartAsync(); var note = await KilogramNoteAsync(run);
        await using var scope = KilogramStore(run, CompanyA);
        var source = (await scope.Store.ReadSourceAsync(note))!; var record = WeighingPurposePendingRecord(run, source);
        await scope.Store.StoreAsync(run.Envelope("weigh-work-refuse-record"), Hash("weigh-work-refuse-record"), record, source, _ => Task.CompletedTask);
        var user = new PlanExcessWorkUser(); var allowed = true; var host = WeighingPurposeHost(scope, user, () => true, () => allowed);
        scope.Company.Set(CompanyB);
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(WorkDecisionHostV1.LogisticsWeighingDifference, record.RecordId, default))).Status);
        scope.Company.Set(CompanyA); user.UserId = record.RecordedBy;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(WorkDecisionHostV1.LogisticsWeighingDifference, record.RecordId, default))).Status);
        user.UserId = PlanExcessApprover; allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(WorkDecisionHostV1.LogisticsWeighingDifference,
            record.RecordId, new("1", true, null), "weigh-work-denied", default))).Status);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.WeighingDifferenceAttempts WHERE RecordId=@id", ("@id", record.RecordId)));
    }

    // The existing generic kilogram fixture is fully covered by free stock and is Recorded, not approvable.
    // Keep the same native calculator/snapshot construction, with an uncovered .001kg so approval is legally required.
    private static WeighingDifferenceRecord WeighingPurposePendingRecord(Run run, WeighingDifferenceSourceV1 source)
    {
        var product = source.Lines[0].ProductId;
        var evaluation = WeighingDifferenceCalculator.Evaluate(source.Lines.Select(line => new WeighingDifferenceNoteLine(
            line.LineNo, line.ShipmentId, line.ProductId, Kg(line.Quantity))).ToArray(),
            [new(product, Kg(5.001m), Kg(0m))], WeighingDifferencePolicy.Unreadable);
        var allocation = DispatchKilogramSnapshot.Theoretical(source.Lines.Select(line => new DispatchKilogramSourceLine(
            line.LineNo, line.ShipmentId, line.ProductId, line.Quantity)).ToArray(), [new(product, 5.001m)]);
        var record = WeighingDifferenceRecord.Create(CompanyA, Guid.NewGuid(), source.DispatchNoteId, source.InstructionId, evaluation,
            source.EnteredBy.Append(run.Actor), source.EnteredByComplete, run.Actor, DateTimeOffset.UtcNow, allocation);
        Assert.Equal(WeighingDifferenceRecordStatus.PendingApproval, record.Status);
        return record;
    }

    private static WorkDecisionHostV1 WeighingPurposeHost(KilogramStoreScope scope, PlanExcessWorkUser user,
        Func<bool> delegationAllowed, Func<bool>? permissionAllowed = null)
    {
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-weigh-work", null, "Web", null, null);
        var execution = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(new PlanApprovalAuthority(permissionAllowed ?? (() => true)), execution);
        var ownerSource = new LogisticsWeighingDifferenceWorkInboxSourceV1(scope.Context, new LogisticsWorkInboxFence(scope.Company, user, authority));
        var native = new LogisticsWeighingDifferenceService(new LogisticsWeighingDifferenceScope(scope.Company, user, authority), scope.Store,
            new(LogisticsPermissionManifestV1.WeighingDifferencesRecord, LogisticsPermissionManifestV1.WeighingDifferencesApprove, LogisticsPermissionManifestV1.WeighingDifferencesRead),
            new UnavailableLogisticsWeighingDifferencePolicyReadPort(), new UnavailableLogisticsWeighingDifferenceStockReadPort(), correlation, TimeProvider.System);
        return new(scope.Company, user, correlation, authority, [ownerSource], new WeighingWorkDelegation(delegationAllowed), execution,
            null!, null!, logisticsWeighingDifferences: native);
    }
    private sealed class WeighingWorkDelegation(Func<bool> allowed) : IWorkDelegationReaderV1
    {
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) =>
            Task.FromResult<WorkDelegationViewV1?>(allowed() ? new(id, CompanyA, item.Kind, LogisticsPermissionManifestV1.WeighingDifferencesApprove,
                Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(),
                DateTimeOffset.UtcNow, "SENTETIK delegation", null, null, null) : null);
    }
}
