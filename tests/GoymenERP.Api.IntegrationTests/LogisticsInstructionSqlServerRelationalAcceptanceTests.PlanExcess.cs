using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Approved_plan_excess_uses_are_separate_from_plan_and_immutable_original_approval_replay()
    {
        await using var run = await Run.StartAsync(); var owners = Owners.For(CompanyA);
        var original = InstructionForOrderLine(owners, Guid.NewGuid(), 100m); var line = Assert.Single(original.Lines);
        await using var frame = PlanExcessFrame(run, CompanyA); var store = frame.Store; var db = frame.Db;
        await store.StoreInstructionAsync(run.Envelope("px-create"), Hash("px-create"), Day, () => original);
        await store.MutateInstructionAsync(run.Envelope("px-issue"), Hash("px-issue"), LogisticsCommands.IssueV1, original.Id, (value, now) => value.Issue(2, now), Issued);
        var exceptionId = Guid.NewGuid(); var propose = run.Envelope("px-propose") with { PermissionCode = LogisticsPermissionManifestV1.LoadsRecord };
        var approve = run.Envelope("px-approve") with { PermissionCode = LogisticsPlanExcessV1.ApprovePermission };
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var pending = await store.ProposePlanExcessAsync(propose, Hash("px-propose"), original.Id, line.Id, new(exceptionId, "3", "20", "SENTETIK fazla yükleme"), default);
            Assert.Equal(("Pending", "100", "20", "1"), (pending.Exception.Status, pending.Exception.Instructed, pending.Exception.Quantity, pending.Exception.Version));
            await tx.CommitAsync();
        }
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var approved = await store.ApprovePlanExcessAsync(approve, Hash("px-approve"), exceptionId, new("1", "3"), default);
            Assert.Equal((run.Actor, run.Actor, "Approved", "3"), (approved.Exception.ProposedBy, approved.Exception.ApprovedBy, approved.Exception.Status, approved.Exception.InstructionVersion));
            await tx.CommitAsync();
        }
        await store.MutateWithPlanExcessAsync(run.Envelope("px-load"), Hash("px-load"), LogisticsCommands.RecordLoadV1, original.Id,
            (value, now) => value.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(120), MeasurementKind.Measured, now), Loaded, null, null,
            new Dictionary<Guid, IReadOnlyList<LogisticsPlanExcessUseV1>?> { [line.Id] = [new(exceptionId, "2", "20")] }, default);
        await store.MutateWithPlanExcessAsync(run.Envelope("px-ship"), Hash("px-ship"), LogisticsCommands.RecordShipmentV1, original.Id,
            (value, now) => value.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(120), now), Shipped, null, null,
            new Dictionary<Guid, IReadOnlyList<LogisticsPlanExcessUseV1>?> { [line.Id] = [new(exceptionId, "2", "20")] }, default);
        var restored = (await store.FindInstructionAsync(original.Id))!.Instruction;
        Assert.Equal((100m, 120m, 120m), (restored.Lines[0].InstructedQuantity.Value, restored.Lines[0].LoadedQuantity.Value, restored.Lines[0].ShippedQuantity.Value));
        Assert.Equal(2, await db.PlanExcessUsages.CountAsync()); Assert.Equal(1, await db.PlanExcessExceptions.CountAsync());
        var replay = await store.FindPlanExcessReplayAsync(approve with { CorrelationId = "SENTETIK-retry" }, Hash("px-approve"), LogisticsPlanExcessV1.ApproveCommand, default);
        Assert.True(replay!.IsReplay); Assert.Equal(("3", "0", "0"), (replay.Exception.InstructionVersion, replay.Exception.LoadedUsed, replay.Exception.ShippedUsed));
        var planned = await new LogisticsSalesOrderPlannedQuantityReadPort(db, frame.Company).ReadOpenInstructionQuantitiesAsync(CompanyA, [owners.Order]);
        Assert.Empty(planned.Lines); // 120 does not become a negative plan or consume another instruction's remainder.
        await using var other = PlanExcessFrame(run, CompanyB);
        Assert.Empty((await other.Store.ListPlanExcessAsync(null, null, default)).Items);
    }
    [SqlServerFact]
    public async Task Unapproved_or_wrong_exception_excess_load_writes_no_share_usage_attempt_or_audit()
    {
        await using var run = await Run.StartAsync(); var owners = Owners.For(CompanyA);
        var instruction = InstructionForOrderLine(owners, Guid.NewGuid(), 100m); var line = instruction.Lines[0];
        await using var frame = PlanExcessFrame(run, CompanyA);
        await frame.Store.StoreInstructionAsync(run.Envelope("px-deny-create"), Hash("px-deny-create"), Day, () => instruction);
        await frame.Store.MutateInstructionAsync(run.Envelope("px-deny-issue"), Hash("px-deny-issue"), LogisticsCommands.IssueV1, instruction.Id, (value, now) => value.Issue(2, now), Issued);
        var before = await run.CountsAsync(instruction.Id);
        await Assert.ThrowsAsync<GoymenERP.Modules.Logistics.Domain.LogisticsRuleException>(() => frame.Store.MutateWithPlanExcessAsync(
            run.Envelope("px-deny-load"), Hash("px-deny-load"), LogisticsCommands.RecordLoadV1, instruction.Id,
            (value, now) => value.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(120), MeasurementKind.Measured, now), Loaded, null, null,
            new Dictionary<Guid, IReadOnlyList<LogisticsPlanExcessUseV1>?> { [line.Id] = [new(Guid.NewGuid(), "2", "20")] }, default));
        Assert.Equal(before, await run.CountsAsync(instruction.Id)); Assert.Equal(0, await frame.Db.PlanExcessUsages.CountAsync());
    }
    [SqlServerFact]
    public async Task Plan_excess_approval_rollback_preserves_pending_fact_plan_cap_and_original_key_for_retry()
    {
        await using var run = await Run.StartAsync(); var owners = Owners.For(CompanyA); var original = InstructionForOrderLine(owners, Guid.NewGuid(), 100m);
        await using var frame = PlanExcessFrame(run, CompanyA); var store = frame.Store; var db = frame.Db;
        await store.StoreInstructionAsync(run.Envelope("px-rb-create"), Hash("px-rb-create"), Day, () => original);
        await store.MutateInstructionAsync(run.Envelope("px-rb-issue"), Hash("px-rb-issue"), LogisticsCommands.IssueV1, original.Id, (value, now) => value.Issue(2, now), Issued);
        var id = Guid.NewGuid(); var envelope = run.Envelope("px-rb-approve") with { PermissionCode = LogisticsPlanExcessV1.ApprovePermission };
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await store.ProposePlanExcessAsync(run.Envelope("px-rb-propose") with { PermissionCode = LogisticsPermissionManifestV1.LoadsRecord }, Hash("px-rb-propose"), original.Id, original.Lines[0].Id, new(id, "3", "20", "SENTETIK"), default);
            await tx.CommitAsync();
        }
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await store.ApprovePlanExcessAsync(envelope, Hash("px-rb-approve"), id, new("1", "3"), default);
            await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Equal(1, (await db.PlanExcessExceptions.SingleAsync()).Status);
        Assert.Equal(0m, await db.Lines.Select(row => row.ApprovedPlanExcess).SingleAsync());
        Assert.Null(await store.FindPlanExcessReplayAsync(envelope, Hash("px-rb-approve"), LogisticsPlanExcessV1.ApproveCommand, default));
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await store.ApprovePlanExcessAsync(envelope, Hash("px-rb-approve"), id, new("1", "3"), default);
            await tx.CommitAsync();
        }
        Assert.Equal(20m, await db.Lines.Select(row => row.ApprovedPlanExcess).SingleAsync());
    }
    [SqlServerFact]
    public async Task Competing_plan_excess_loads_cannot_consume_one_approved_cap_twice()
    {
        await using var run = await Run.StartAsync(); var owners = Owners.For(CompanyA); var instruction = InstructionForOrderLine(owners, Guid.NewGuid(), 100m);
        var line = instruction.Lines[0]; var exceptionId = Guid.NewGuid();
        await using (var seed = PlanExcessFrame(run, CompanyA))
        {
            await seed.Store.StoreInstructionAsync(run.Envelope("px-race-create"), Hash("px-race-create"), Day, () => instruction);
            await seed.Store.MutateInstructionAsync(run.Envelope("px-race-issue"), Hash("px-race-issue"), LogisticsCommands.IssueV1,
                instruction.Id, (value, now) => value.Issue(2, now), Issued);
            await seed.Store.MutateInstructionAsync(run.Envelope("px-race-base"), Hash("px-race-base"), LogisticsCommands.RecordLoadV1,
                instruction.Id, (value, now) => value.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(100), MeasurementKind.Measured, now), Loaded);
            await using var tx = await seed.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await seed.Store.ProposePlanExcessAsync(run.Envelope("px-race-propose") with { PermissionCode = LogisticsPermissionManifestV1.LoadsRecord },
                Hash("px-race-propose"), instruction.Id, line.Id, new(exceptionId, "4", "20", "SENTETIK"), default);
            await seed.Store.ApprovePlanExcessAsync(run.Envelope("px-race-approve") with { PermissionCode = LogisticsPlanExcessV1.ApprovePermission },
                Hash("px-race-approve"), exceptionId, new("1", "4"), default); await tx.CommitAsync();
        }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> TryLoad(string key)
        {
            await start.Task; await using var frame = PlanExcessFrame(run, CompanyA);
            try
            {
                await frame.Store.MutateWithPlanExcessAsync(run.Envelope(key), Hash(key), LogisticsCommands.RecordLoadV1, instruction.Id,
                    (value, now) => value.RecordLoad(4, Guid.NewGuid(), line.Id, Kg(20), MeasurementKind.Measured, now), Loaded, null, null,
                    new Dictionary<Guid, IReadOnlyList<LogisticsPlanExcessUseV1>?> { [line.Id] = [new(exceptionId, "2", "20")] }, default);
                return true;
            }
            catch (GoymenERP.Modules.Logistics.Domain.LogisticsVersionConflictException) { return false; }
            catch (LogisticsStoreException error) when (error.Code == LogisticsStoreCodes.ConcurrentChange) { return false; }
            catch (Microsoft.Data.SqlClient.SqlException error) when (error.Number == 1205) { return false; }
            catch (LogisticsInstructionServiceException error) when (error.Status == 409) { return false; }
        }
        var first = TryLoad("px-race-a"); var second = TryLoad("px-race-b"); start.SetResult();
        var results = await Task.WhenAll(first, second); Assert.Single(results, value => value);
        await using var check = PlanExcessFrame(run, CompanyA);
        Assert.Equal(120m, (await check.Store.FindInstructionAsync(instruction.Id))!.Instruction.Lines[0].LoadedQuantity.Value);
        Assert.Equal(20m, await check.Db.PlanExcessUsages.Where(use => use.ExceptionId == exceptionId).SumAsync(use => use.Quantity));
        Assert.Equal(2, await check.Db.Attempts.CountAsync(attempt => attempt.CommandName == LogisticsCommands.RecordLoadV1 && attempt.InstructionId == instruction.Id));
    }

    private sealed record PlanExcessStoreFrame(LogisticsDbContext Db, LogisticsStore Store, CompanyContext Company) : IAsyncDisposable
    { public ValueTask DisposeAsync() => Db.DisposeAsync(); }
    private static PlanExcessStoreFrame PlanExcessFrame(Run run, Guid companyId)
    {
        var company = new CompanyContext(); company.Set(companyId); var audit = new AuditEventCollector();
        var db = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
        return new(db, new LogisticsStore(db, company, audit, TimeProvider.System), company);
    }
}
