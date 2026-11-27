using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsCustomerReturnSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Piece_return_weighing_is_append_once_and_original_request_and_weighing_receipts_survive_rejection_at_the_next_version()
    {
        await using var run = await Run.StartAsync(); var source = await run.CreateDeliveredSourceAsync(2, piece: true);
        await using var frame = run.DecisionFrame(); var id = Guid.NewGuid();
        var request = new RequestCustomerReturnV1(id, source.DeliveryId, "6", "1", "SYNTHETIC Piece return", T0);
        var requested = await frame.Service.RequestAsync(source.InstructionId, source.LineId, request, ReturnMetadata("piece-request"));
        Assert.Null(requested.Return.Weighing); Assert.Equal("1", requested.Return.Version);
        frame.User.Actor = "checker";
        Assert.Equal(503, (await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() =>
            frame.Service.ApproveAsync(id, new("1", Guid.NewGuid()), ReturnMetadata("missing-weight")))).Status);
        Assert.Equal((1, 1), await run.CountsAsync());
        frame.User.Actor = "operator";
        var weighing = new RecordCustomerReturnWeighingV1("1", Guid.NewGuid(), "GrossFirst", "1013.125", "1000");
        var weighed = await frame.Service.RecordWeighingAsync(id, weighing, ReturnMetadata("piece-weighing"));
        Assert.Equal(("Pending", "2", "13.125"), (weighed.Return.Status, weighed.Return.Version, weighed.Return.Weighing!.NetKg));
        Assert.Equal("operator", weighed.Return.Weighing.RecordedBy);
        frame.User.Actor = "checker";
        Assert.Equal(409, (await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() =>
            frame.Service.ApproveAsync(id, new("1", Guid.NewGuid()), ReturnMetadata("stale-before-weighing")))).Status);
        var rejected = await frame.Service.RejectAsync(id, new("2"), ReturnMetadata("piece-reject"));
        Assert.Equal(("Rejected", "3"), (rejected.Return.Status, rejected.Return.Version));
        Assert.Equal(weighed.Return.Weighing, rejected.Return.Weighing);
        frame.User.Actor = "operator";
        var before = await run.CountsAsync(); var audits = await run.DecisionAuditCount();
        var replay = await frame.Service.RecordWeighingAsync(id, weighing, ReturnMetadata("piece-weighing"));
        Assert.True(replay.IsReplay); Assert.Equal(weighed.Return, replay.Return); Assert.Equal("Pending", replay.Return.Status);
        frame.User.Actor = requested.Return.RequestedBy;
        var original = await frame.Service.RequestAsync(source.InstructionId, source.LineId, request, ReturnMetadata("piece-request"));
        Assert.True(original.IsReplay); Assert.Equal(requested.Return, original.Return); Assert.Null(original.Return.Weighing);
        Assert.Equal(before, await run.CountsAsync()); Assert.Equal(audits, await run.DecisionAuditCount());
    }

    [SqlServerFact]
    public async Task Piece_return_post_save_weighing_authority_loss_rolls_back_parent_CAS_receipt_and_same_collector_audit()
    {
        await using var run = await Run.StartAsync(); var source = await run.CreateDeliveredSourceAsync(2, piece: true);
        var probe = new ReturnDecisionProbe(); await using var frame = run.DecisionFrame(probe); var id = Guid.NewGuid();
        await frame.Service.RequestAsync(source.InstructionId, source.LineId,
            new(id, source.DeliveryId, "6", "1", "SYNTHETIC Piece return", T0), ReturnMetadata("rollback-request"));
        var before = await run.CountsAsync(); var audits = await run.DecisionAuditCount();
        probe.AfterSaved = () => frame.Authority.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => frame.Service.RecordWeighingAsync(id,
            new("1", Guid.NewGuid(), "GrossFirst", "1013.125", "1000"), ReturnMetadata("rollback-weighing")))).Status);
        Assert.Equal(1, probe.SavedDecisions); Assert.Equal(before, await run.CountsAsync()); Assert.Equal(audits, await run.DecisionAuditCount());
        Assert.Empty(frame.Db.ChangeTracker.Entries()); Assert.False(frame.Writer.HasPendingDrafts);
        frame.Authority.Allowed = true;
        Assert.Equal("1", (await frame.Service.GetAsync(id))!.Version); Assert.Null((await frame.Service.GetAsync(id))!.Weighing);
    }
}
