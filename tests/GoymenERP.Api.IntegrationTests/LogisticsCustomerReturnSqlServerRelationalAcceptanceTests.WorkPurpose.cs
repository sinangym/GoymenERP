using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsCustomerReturnSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Customer_return_purpose_reads_native_request_and_rejects_with_original_receipt_replay_and_human_only_facts()
    {
        await using var run = await Run.StartAsync(); var delivery = await run.CreateDeliveredSourceAsync(100);
        var execution = new WorkDecisionExecutionFenceV1(); IErpAuthorityRevalidationService? nativeAuthority = null;
        await using var frame = run.DecisionFrame(decorate: inner => nativeAuthority = new WorkDecisionAuthorityV1(inner, execution));
        var id = Guid.NewGuid(); await frame.Service.RequestAsync(delivery.InstructionId, delivery.LineId,
            new(id, delivery.DeliveryId, "6", "5", "SYNTHETIC human-only return reason", T0), ReturnMetadata("purpose-request"));
        frame.User.Actor = "other-decider"; var source = new LogisticsCustomerReturnWorkInboxSourceV1(frame.Db,
            new LogisticsWorkInboxFence(frame.Company, frame.User, nativeAuthority!));
        var correlation = new CorrelationContext(); correlation.Set("return-safety-correlation", null, "Web", null, null);
        var host = new WorkDecisionHostV1(frame.Company, frame.User, correlation, nativeAuthority!, [source], new ReturnPurposeDelegations(),
            execution, null!, null!, logisticsCustomerReturns: frame.Service);
        var preview = await host.PreviewAsync(source.Kind, id, default);
        Assert.True(preview.Pending); Assert.True(preview.ApprovalSupported); Assert.False(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
        Assert.Equal("SYNTHETIC human-only return reason", preview.LogisticsCustomerReturn!.Reason);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(15, JsonSerializer.SerializeToElement(preview, options).GetProperty("logisticsCustomerReturn").EnumerateObject().Count());
        var snapshot = await source.ReadDecisionAsync(new(Company, frame.User.Actor, "SYNTHETIC", null), id, default);
        Assert.DoesNotContain("SYNTHETIC human-only return reason", JsonSerializer.Serialize(snapshot, options));
        var answer = await host.DecideAsync(source.Kind, id, new("1", false, null), "purpose-reject", default);
        Assert.Equal(("Rejected", false), (answer.State, answer.Replayed));
        var terminal = await host.PreviewAsync(source.Kind, id, default); Assert.False(terminal.Pending); Assert.Equal("2", terminal.SourceVersion);
        var replay = await host.DecideAsync(source.Kind, id, new("1", false, null), "purpose-reject", default);
        Assert.Equal(answer with { Replayed = true }, replay);
        Assert.Equal((1, 2), await run.CountsAsync()); Assert.Equal(2, await run.DecisionAuditCount());
    }

    [SqlServerFact]
    public async Task Customer_return_purpose_native_post_save_delegation_loss_rolls_back_real_rejection_receipt_and_audit()
    {
        await using var run = await Run.StartAsync(); var delivery = await run.CreateDeliveredSourceAsync(100);
        var execution = new WorkDecisionExecutionFenceV1(); var delegations = new ReturnPurposeDelegations();
        IErpAuthorityRevalidationService? nativeAuthority = null; var probe = new ReturnDecisionProbe();
        await using var frame = run.DecisionFrame(probe, decorate: inner => nativeAuthority = new WorkDecisionAuthorityV1(inner, execution));
        var id = Guid.NewGuid(); await frame.Service.RequestAsync(delivery.InstructionId, delivery.LineId,
            new(id, delivery.DeliveryId, "6", "5", "SYNTHETIC return", T0), ReturnMetadata("delegated-request"));
        frame.User.Actor = "other-decider"; var source = new LogisticsCustomerReturnWorkInboxSourceV1(frame.Db,
            new LogisticsWorkInboxFence(frame.Company, frame.User, nativeAuthority!));
        var correlation = new CorrelationContext(); correlation.Set("return-safety-correlation", null, "Web", null, null);
        var host = new WorkDecisionHostV1(frame.Company, frame.User, correlation, nativeAuthority!, [source], delegations,
            execution, null!, null!, logisticsCustomerReturns: frame.Service);
        var before = await run.CountsAsync(); var audits = await run.DecisionAuditCount();
        probe.AfterSaved = () => delegations.Live = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(source.Kind, id,
            new("1", false, null, Guid.NewGuid()), "delegated-reject", default))).Status);
        Assert.Equal(1, probe.SavedDecisions); Assert.Equal(before, await run.CountsAsync()); Assert.Equal(audits, await run.DecisionAuditCount());
        Assert.Empty(frame.Db.ChangeTracker.Entries()); Assert.False(frame.Writer.HasPendingDrafts);
    }

    [SqlServerFact]
    public async Task Customer_return_minimum_supplied_occurrence_date_survives_real_native_request_Work_rejection_and_replay()
    {
        await using var run = await Run.StartAsync(); var delivery = await run.CreateDeliveredSourceAsync(100);
        var execution = new WorkDecisionExecutionFenceV1(); IErpAuthorityRevalidationService? nativeAuthority = null;
        await using var frame = run.DecisionFrame(decorate: inner => nativeAuthority = new WorkDecisionAuthorityV1(inner, execution));
        var id = Guid.NewGuid();
        var requested = await frame.Service.RequestAsync(delivery.InstructionId, delivery.LineId,
            new(id, delivery.DeliveryId, "6", "5", "SYNTHETIC minimum supplied date", DateTimeOffset.MinValue), ReturnMetadata("minimum-date-request"));
        Assert.Equal(DateTimeOffset.MinValue, requested.Return.OccurredAtUtc);
        Assert.Equal("Pending", requested.Return.Status);
        frame.User.Actor = "other-decider";
        var source = new LogisticsCustomerReturnWorkInboxSourceV1(frame.Db, new LogisticsWorkInboxFence(frame.Company, frame.User, nativeAuthority!));
        var correlation = new CorrelationContext(); correlation.Set("return-safety-correlation", null, "Web", null, null);
        var host = new WorkDecisionHostV1(frame.Company, frame.User, correlation, nativeAuthority!, [source], new ReturnPurposeDelegations(),
            execution, null!, null!, logisticsCustomerReturns: frame.Service);
        Assert.Equal(DateTimeOffset.MinValue, (await host.PreviewAsync(source.Kind, id, default)).LogisticsCustomerReturn!.OccurredAtUtc);
        var rejected = await host.DecideAsync(source.Kind, id, new("1", false, null), "minimum-date-reject", default);
        Assert.Equal("Rejected", rejected.State); Assert.False(rejected.Replayed);
        var terminal = await host.PreviewAsync(source.Kind, id, default);
        Assert.False(terminal.Pending); Assert.Equal(DateTimeOffset.MinValue, terminal.LogisticsCustomerReturn!.OccurredAtUtc);
        Assert.Equal(rejected with { Replayed = true }, await host.DecideAsync(source.Kind, id, new("1", false, null), "minimum-date-reject", default));
        Assert.Equal((1, 2), await run.CountsAsync()); Assert.Equal(2, await run.DecisionAuditCount());
    }

    // Controlled live WP3 seam, not the actual delegation reader/database acceptance. The native
    // command, Work authority decorator, source, SQL receipt and both runtime fences are real.
    private sealed class ReturnPurposeDelegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, Company, item.Kind, LogisticsPermissionManifestV1.CustomerReturnsDecision,
                Guid.NewGuid(), Guid.NewGuid(), T0, DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), T0, "SYNTHETIC", null, null, null) : null);
    }
}
