using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using System.Net;
using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task Native_plan_approval_final_fence_refusal_after_business_save_rolls_back_the_real_owned_transaction() =>
        AssertAllocationFenceRollbackAsync(afterAllocation: false);

    [SqlServerFact]
    public Task Native_plan_approval_final_fence_refusal_after_real_Inventory_and_allocation_proof_saves_rolls_back_both_owners() =>
        AssertAllocationFenceRollbackAsync(afterAllocation: true);

    private static async Task AssertAllocationFenceRollbackAsync(bool afterAllocation)
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        using var creator = run.ClientFor(user, CompanyA);
        var roll = await run.StockedLabelAsync($"SYN-MFG-FRESH-ALLOC-{Guid.NewGuid():N}");
        var request = new SavePlanRequestV1(Guid.NewGuid(), await run.WorkCenterOfAsync(CompanyA),
            [new PlannedInputRequestV1(roll.Id, "600.125", roll.Version)], null);
        using (var created = await creator.PostWithKeyAsync($"{Base}/plans", request)) Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await run.EnterTerminAsync(request.PlanId);
        var approver = await run.ApproverAsync(CompanyA);
        var probe = new AllocationFenceSqlProbe();
        await using var guarded = run.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IManufacturingPlanAllocationPortV1>();
            services.AddScoped<IManufacturingPlanAllocationPortV1>(provider => new ControlledAllocationFencePort(
                ActivatorUtilities.CreateInstance<ManufacturingPlanAllocationHost>(provider),
                provider.GetRequiredService<ManufacturingDbContext>(), afterAllocation, probe));
        }));
        using var client = guarded.CreateClient();
        foreach (var header in approver.DefaultRequestHeaders)
            client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        // The real native authority callback still runs. Only its later owner-transaction refusal is controlled;
        // this is not a database race against the Identity delegation tables or Work/UI end-to-end acceptance.
        using (var denied = await client.PostWithKeyAsync($"{Base}/plans/{request.PlanId}/approval", new ApprovePlanRequestV1(1), "fresh-alloc"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.True(probe.BusinessSaved);
        Assert.Equal(afterAllocation, probe.AllocationSaved);
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id=@p", ("@p", request.PlanId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId=@p", ("@p", request.PlanId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanInputAllocations WHERE PlanId=@p", ("@p", request.PlanId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands WHERE CompanyId=@c AND CommandName=@n",
            ("@c", CompanyA), ("@n", ManufacturingCommandNames.ApprovePlan)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'manufacturing.plan.approved.v1' AND EntityId=@p",
            ("@p", request.PlanId.ToString("D"))));
        Assert.Equal(long.Parse(roll.Version, System.Globalization.CultureInfo.InvariantCulture),
            await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id=@p", ("@p", roll.Id)));
        // The same native key can now succeed exactly once: the denied save left neither a receipt nor audit ghost.
        using (var retry = await approver.PostWithKeyAsync($"{Base}/plans/{request.PlanId}/approval", new ApprovePlanRequestV1(1), "fresh-alloc"))
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId=@p", ("@p", request.PlanId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'manufacturing.plan.approved.v1' AND EntityId=@p",
            ("@p", request.PlanId.ToString("D"))));
    }

    private sealed class AllocationFenceSqlProbe
    {
        public bool BusinessSaved { get; set; }
        public bool AllocationSaved { get; set; }
    }
    private sealed class ControlledAllocationFencePort(ManufacturingPlanAllocationHost host, ManufacturingDbContext database,
        bool afterAllocation, AllocationFenceSqlProbe probe) : IManufacturingPlanAllocationPortV1
    {
        public Task<T> ApproveAndAllocateAsync<T>(Guid id, IReadOnlyDictionary<Guid, long> versions,
            Func<CancellationToken, Task<T>> approve, ManufacturingCommandMetadata metadata,
            Func<CancellationToken, Task> freshDecisionFence, CancellationToken token) =>
            host.ApproveAndAllocateAsync(id, versions, approve, metadata, async current =>
            {
                await freshDecisionFence(current);
                Assert.NotNull(database.Database.CurrentTransaction);
                probe.BusinessSaved = await database.Plans.AsNoTracking().AnyAsync(row => row.Id == id && row.Status == (int)PlanStatus.Approved, current);
                probe.AllocationSaved = await database.PlanInputAllocations.AsNoTracking().AnyAsync(row => row.PlanId == id, current);
                if (afterAllocation ? probe.AllocationSaved : probe.BusinessSaved)
                    throw new ManufacturingStoreException(ManufacturingStoreCodes.AuthorityDenied, "Controlled final owner fence refusal.");
            }, token);
        public Task<T> WriteAndReleaseAsync<T>(Guid id, ManufacturingPlanAllocationReleaseReasonV1 reason,
            Func<CancellationToken, Task<T>> write, ManufacturingCommandMetadata metadata, CancellationToken token) =>
            host.WriteAndReleaseAsync(id, reason, write, metadata, token);
    }
}

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Native_plan_approval_unfenced_or_unclean_entry_refuses_before_callbacks_and_preserves_caller_state()
    {
        await using var run = await Run.StartAsync();
        var plan = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var seed = run.Store(CompanyA)) await seed.Plans.SavePlanAsync(plan, Metadata(run));
        foreach (var boundary in new[] { "old-overload", "missing-writer", "borrowed", "dirty", "audit", "ambient" })
        {
            await using var services = run.Factory.Services.CreateAsyncScope();
            var company = services.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
            var database = services.ServiceProvider.GetRequiredService<ManufacturingDbContext>();
            var owner = services.ServiceProvider.GetRequiredService<ManufacturingStore>();
            var writer = services.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var collector = services.ServiceProvider.GetRequiredService<AuditEventCollector>();
            var host = new ManufacturingPlanAllocationHost(database, owner, company, ManufacturingSettings(run), TimeProvider.System,
                decisionAuditWriter: boundary == "missing-writer" ? null : writer);
            await using var borrowed = boundary == "borrowed" ? await database.Database.BeginTransactionAsync() : null;
            var row = boundary == "dirty" ? await database.Plans.SingleAsync(x => x.Id == plan.Id) : null;
            if (row is not null) row.UpdatedAtUtc = At.AddMinutes(10);
            if (boundary == "audit") collector.Enqueue(RejectionCallerAudit(run));
            using var ambient = boundary == "ambient"
                ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            var ambientBefore = System.Transactions.Transaction.Current;
            var called = false;
            Task<bool> Invoke() => boundary == "old-overload"
                ? host.ApproveAndAllocateAsync(plan.Id, new Dictionary<Guid, long>(),
                    _ => { called = true; return Task.FromResult(true); }, Metadata(run), CancellationToken.None)
                : host.ApproveAndAllocateAsync(plan.Id, new Dictionary<Guid, long>(),
                    _ => { called = true; return Task.FromResult(true); }, Metadata(run),
                    _ => { called = true; return Task.CompletedTask; }, CancellationToken.None);
            if (boundary is "old-overload" or "missing-writer")
            {
                var refusal = await Assert.ThrowsAsync<ManufacturingInventoryRealizationPortException>(Invoke);
                Assert.Equal(ManufacturingApiStatus.Unavailable, refusal.Status);
            }
            else await Assert.ThrowsAsync<InvalidOperationException>(Invoke);
            Assert.False(called);
            if (borrowed is not null) Assert.Same(borrowed, database.Database.CurrentTransaction);
            if (row is not null) Assert.Equal(EntityState.Modified, database.Entry(row).State);
            if (boundary == "audit") { Assert.True(writer.HasPendingDrafts); writer.DiscardPending(); }
            if (boundary == "ambient") Assert.Same(ambientBefore, System.Transactions.Transaction.Current);
        }
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id=@p", ("@p", plan.Id)));
    }
}
