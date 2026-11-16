using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_legacy_cancelled_plan_without_allocation_provenance_cannot_start_a_new_allocation_generation()
    {
        await using var run = await Run.StartAsync();
        var (plan, orderId) = await run.ApprovedOrderAsync(CompanyA);
        await using (var store = run.Store(CompanyA))
            await store.Corrections.CancelOrderAsync(orderId, 1, run.Actor, At.AddHours(1), "SENTETIK legacy plan", Metadata(run));
        await using var context = run.Context(CompanyA);
        var company = new CompanyContext(); company.Set(CompanyA);
        var storeOwner = new ManufacturingStore(context, company, new AuditEventCollector(), TimeProvider.System);
        var host = new ManufacturingPlanAllocationHost(context, storeOwner, company, ManufacturingSettings(run), TimeProvider.System);
        var called = false;
        await Assert.ThrowsAsync<ManufacturingInventoryRealizationPortException>(() => host.ApproveAndAllocateAsync(plan.Id,
            new Dictionary<Guid, long>(), _ => { called = true; return Task.FromResult(true); }, Metadata(run), CancellationToken.None));
        Assert.False(called);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanReapprovals WHERE PlanId = @id", ("@id", plan.Id)));
    }

    [SqlServerFact]
    public async Task The_order_cancellation_Down_passes_with_open_orders_and_refuses_without_losing_a_cancelled_order()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddManufacturingOrderCancellation().DownOperations[0]).Sql;
        Assert.Equal(1, await run.ScalarAsync<int>(guard + "; SELECT 1"));
        var (plan, orderId) = await run.ApprovedOrderAsync(CompanyA);
        Assert.Equal(1, await run.ScalarAsync<int>(guard + "; SELECT 1"));
        await using (var store = run.Store(CompanyA))
        {
            await store.Corrections.CancelOrderAsync(orderId, 1, run.Actor, At.AddHours(1),
                "SENTETIK cancellation Down guard", Metadata(run));
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(51870, refused.Number);
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT Status FROM manufacturing.Orders WHERE Id = @id", ("@id", orderId)));
        await using var fresh = run.Store(CompanyA);
        var cancelled = (await fresh.Orders.FindOrderAsync(orderId))!;
        Assert.Equal("SENTETIK cancellation Down guard", cancelled.CancellationReason);
        var released = (await fresh.Plans.FindPlanAsync(plan.Id))!;
        Assert.Null(released.OrderId);
        Assert.True(released.RequiresReapproval);
        Assert.Equal(2L, cancelled.Version);
    }
    [SqlServerFact]
    public async Task A_cancelled_orders_plan_revision_roundtrips_new_labels_without_rewriting_the_cancelled_order_or_first_approval()
    {
        await using var run = await Run.StartAsync();
        var (original, orderId) = await run.ApprovedOrderAsync(CompanyA);
        await using (var store = run.Store(CompanyA))
        {
            await store.Corrections.CancelOrderAsync(orderId, 1, run.Actor, At.AddHours(1), "SENTETIK yeniden hesap", Metadata(run));
            var released = (await store.Plans.FindPlanAsync(original.Id))!;
            await store.Revisions.RevisePlanContentAsync(released.Id, released.Version, released.WorkCenterId,
                released.Process, [new PlannedInput(RollB, Kg(800m), 19)], released.Target,
                released.CutToLengthSpec, released.LineSpeedMetresPerMinute, "SENTETIK yeni etiket", Metadata(run),
                processSpec: released.ProcessSpec);
        }
        await using var fresh = run.Store(CompanyA);
        var revised = (await fresh.Plans.FindPlanAsync(original.Id))!;
        var cancelled = (await fresh.Orders.FindOrderAsync(orderId))!;
        Assert.True(revised.RequiresReapproval);
        Assert.Equal(4L, revised.Version);
        Assert.Equal(RollB, Assert.Single(revised.Inputs).ParentRollId);
        Assert.Equal(new[] { RollA, RollB }, cancelled.PlannedInputs.Select(input => input.ParentRollId));
        Assert.Equal((original.ApprovedByUserId, original.ApprovedAtUtc), (revised.ApprovedByUserId, revised.ApprovedAtUtc));
        var history = (await fresh.Revisions.ListPlanRevisionsAsync(original.Id))!;
        Assert.Equal(new long[] { 1, 4 }, history.Select(revision => revision.PlanVersion));
        Assert.Equal("SENTETIK yeni etiket", history[1].Reason);
    }

}
