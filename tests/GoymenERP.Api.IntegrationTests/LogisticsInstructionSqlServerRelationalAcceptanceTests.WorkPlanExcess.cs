using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Work_plan_excess_keeps_the_original_native_approval_after_real_load_shipment_and_instruction_version_changes()
    {
        await using var run = await Run.StartAsync(); await using var frame = OwnedPlanExcessFrame(run);
        var (instruction, exception) = await SeedOwnedPlanException(run, frame);
        var host = PlanExcessWorkHost(frame, () => true, "SENTETIK-work-original");
        var preview = await host.PreviewAsync(WorkDecisionHostV1.LogisticsPlanExcess, exception, default);
        Assert.Equal(("1", "3", false, false), (preview.SourceVersion, preview.LogisticsPlanExcess!.InstructionVersion, preview.RejectionSupported, preview.ApprovalReasonRequired));
        var body = new WorkDecisionRequestV1(preview.SourceVersion, true, null,
            LogisticsPlanExcess: new(preview.LogisticsPlanExcess.InstructionVersion));
        var approved = await host.DecideAsync(WorkDecisionHostV1.LogisticsPlanExcess, exception, body, "px-work-approve", default);
        Assert.Equal(("Approved", false, "SENTETIK-work-original"), (approved.State, approved.Replayed, approved.OriginalCorrelationId));
        var line = await frame.Db.Lines.AsNoTracking().SingleAsync(value => value.InstructionId == instruction);
        await frame.Store.MutateWithPlanExcessAsync(run.Envelope("px-work-load"), Hash("px-work-load"), LogisticsCommands.RecordLoadV1, instruction,
            (value, now) => value.RecordLoad(3, Guid.NewGuid(), line.LineId, Kg(120), MeasurementKind.Measured, now), Loaded, null, null,
            new Dictionary<Guid, IReadOnlyList<LogisticsPlanExcessUseV1>?> { [line.LineId] = [new(exception, "2", "20")] }, default);
        await frame.Store.MutateWithPlanExcessAsync(run.Envelope("px-work-ship"), Hash("px-work-ship"), LogisticsCommands.RecordShipmentV1, instruction,
            (value, now) => value.MarkShipped(4, Guid.NewGuid(), line.LineId, Kg(120), now), Shipped, null, null,
            new Dictionary<Guid, IReadOnlyList<LogisticsPlanExcessUseV1>?> { [line.LineId] = [new(exception, "2", "20")] }, default);
        var afterUsage = await run.CountsAsync(instruction); Assert.Equal(2, await frame.Db.PlanExcessUsages.CountAsync());
        var retry = PlanExcessWorkHost(frame, () => true, "SENTETIK-work-retry");
        var current = await retry.PreviewAsync(WorkDecisionHostV1.LogisticsPlanExcess, exception, default);
        Assert.False(current.Pending); Assert.Equal("5", current.LogisticsPlanExcess!.InstructionVersion);
        var replay = await retry.DecideAsync(WorkDecisionHostV1.LogisticsPlanExcess, exception, body, "px-work-approve", default);
        Assert.Equal(approved with { Replayed = true }, replay); Assert.Equal(afterUsage, await run.CountsAsync(instruction));
        Assert.Equal(409, (await Assert.ThrowsAsync<WorkDecisionException>(() => retry.DecideAsync(WorkDecisionHostV1.LogisticsPlanExcess,
            exception, body with { LogisticsPlanExcess = new(current.LogisticsPlanExcess.InstructionVersion) }, "px-work-approve", default))).Status);
        Assert.Equal(afterUsage, await run.CountsAsync(instruction));
    }

    [SqlServerFact]
    public async Task Work_plan_excess_late_delegation_loss_rolls_back_real_capacity_attempt_and_audit_before_retry()
    {
        await using var run = await Run.StartAsync(); var probe = new PlanApprovalProbe(); await using var frame = OwnedPlanExcessFrame(run, probe);
        var (instruction, exception) = await SeedOwnedPlanException(run, frame); var before = await run.CountsAsync(instruction); var revoked = true;
        var host = PlanExcessWorkHost(frame, () => !revoked || !probe.Saved, "SENTETIK-work-late");
        var body = new WorkDecisionRequestV1("1", true, null, Guid.NewGuid(), LogisticsPlanExcess: new("3"));
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(WorkDecisionHostV1.LogisticsPlanExcess,
            exception, body, "px-work-late", default))).Status);
        Assert.True(probe.Saved); Assert.Equal(before, await run.CountsAsync(instruction));
        Assert.False(frame.Writer.HasPendingDrafts); Assert.Empty(frame.Db.ChangeTracker.Entries()); Assert.Null(frame.Db.Database.CurrentTransaction);
        Assert.Equal(0m, await frame.Db.Lines.Select(value => value.ApprovedPlanExcess).SingleAsync());
        revoked = false;
        var approved = await host.DecideAsync(WorkDecisionHostV1.LogisticsPlanExcess, exception, body, "px-work-late", default);
        Assert.Equal(("Approved", false), (approved.State, approved.Replayed));
        Assert.Equal(20m, await frame.Db.Lines.Select(value => value.ApprovedPlanExcess).SingleAsync());
    }

    [SqlServerFact]
    public async Task Work_plan_excess_foreign_company_maker_and_current_authority_refusals_do_not_write()
    {
        await using var run = await Run.StartAsync(); await using var frame = OwnedPlanExcessFrame(run);
        var (instruction, exception) = await SeedOwnedPlanException(run, frame); var before = await run.CountsAsync(instruction);
        var user = new PlanExcessWorkUser(); var allowed = true;
        var host = PlanExcessWorkHost(frame, () => true, "SENTETIK-work-refused", user, () => allowed);
        frame.Company.Set(CompanyB);
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(WorkDecisionHostV1.LogisticsPlanExcess, exception, default))).Status);
        frame.Company.Set(CompanyA); user.UserId = run.Actor;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(WorkDecisionHostV1.LogisticsPlanExcess, exception, default))).Status);
        user.UserId = PlanExcessApprover; allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(WorkDecisionHostV1.LogisticsPlanExcess,
            exception, new("1", true, null, LogisticsPlanExcess: new("3")), "px-work-denied", default))).Status);
        Assert.Equal(before, await run.CountsAsync(instruction));
    }

    private static WorkDecisionHostV1 PlanExcessWorkHost(PlanApprovalFrame frame, Func<bool> delegationAllowed, string correlationId,
        PlanExcessWorkUser? user = null, Func<bool>? permissionAllowed = null)
    {
        user ??= new(); var correlation = new CorrelationContext(); correlation.Set(correlationId, null, "Web", null, null);
        var fence = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(new PlanApprovalAuthority(permissionAllowed ?? (() => true)), fence);
        var source = new LogisticsPlanExcessWorkInboxSourceV1(frame.Db, new LogisticsWorkInboxFence(frame.Company, user, authority));
        var service = new LogisticsInstructionService(frame.Company, user, authority, correlation,
            null!, null!, null!, frame.Store, new(), TimeProvider.System, frame.Runner);
        return new(frame.Company, user, correlation, authority, [source], new PlanExcessWorkDelegation(delegationAllowed), fence, null!, null!, logisticsPlanExcess: service);
    }
    private sealed class PlanExcessWorkUser : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId { get; set; } = PlanExcessApprover;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([CompanyA, CompanyB]); public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class PlanExcessWorkDelegation(Func<bool> allowed) : IWorkDelegationReaderV1
    {
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) =>
            Task.FromResult<WorkDelegationViewV1?>(allowed() ? new(id, CompanyA, item.Kind, LogisticsPlanExcessV1.ApprovePermission,
                Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), DateTimeOffset.UtcNow, "SENTETIK", null, null, null) : null);
    }
}
