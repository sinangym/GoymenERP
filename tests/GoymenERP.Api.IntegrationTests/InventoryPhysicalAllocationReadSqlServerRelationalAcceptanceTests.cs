using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Physical_allocation_read_is_company_and_sales_line_scoped_and_preserves_released_server_truth()
    {
        await using var run = await Run.StartAsync();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var otherLineId = Guid.NewGuid();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-READ-A", 100m);
        var otherSource = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-READ-OTHER", 100m);
        var foreignSource = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-ALLOC-READ-B", 100m);
        var allocationId = Guid.NewGuid();

        InventoryPhysicalAllocationResultV1 created;
        await using (var write = run.Store(CompanyA))
        {
            created = await write.Store.CreateAsync(
                ReadRequest(allocationId, orderId, lineId, source, "30"), Metadata(run));
            await write.Store.CreateAsync(
                ReadRequest(Guid.NewGuid(), orderId, otherLineId, otherSource, "20"), Metadata(run));
        }
        await using (var write = run.Store(CompanyB))
        {
            await write.Store.CreateAsync(
                ReadRequest(Guid.NewGuid(), orderId, lineId, foreignSource, "10"), Metadata(run));
        }

        var beforeRead = await AllocationEffectCountsAsync(run);
        await using var companyA = run.Context(CompanyA);
        var service = PhysicalAllocationReads(companyA, CompanyA);
        var active = Assert.Single((await service.SearchAsync(new(orderId, lineId))).Items);
        var activeLabelVersion = await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE CompanyId = @company AND Id = @label",
            ("@company", CompanyA), ("@label", source.LabelId));
        Assert.Equal((allocationId, "Active", created.Allocation.Version, activeLabelVersion.ToString()),
            (active.Allocation.AllocationId, active.Allocation.Status, active.Allocation.Version, active.LabelVersion));
        Assert.Equal(beforeRead, await AllocationEffectCountsAsync(run));

        InventoryPhysicalAllocationResultV1 released;
        await using (var write = run.Store(CompanyA))
        {
            released = await write.Store.ReleaseAsync(
                allocationId,
                new ReleasePhysicalAllocationV1(created.Allocation.Version, "Satış satırı tahsisi kaldırıldı."),
                Metadata(run));
        }
        var afterRelease = await AllocationEffectCountsAsync(run);

        var reopened = Assert.Single((await service.SearchAsync(new(orderId, lineId))).Items);
        var releasedLabelVersion = await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE CompanyId = @company AND Id = @label",
            ("@company", CompanyA), ("@label", source.LabelId));
        Assert.Equal((allocationId, "Released", "0", released.Allocation.Version, releasedLabelVersion.ToString()),
            (reopened.Allocation.AllocationId, reopened.Allocation.Status, reopened.Allocation.RemainingKg,
                reopened.Allocation.Version, reopened.LabelVersion));
        Assert.Equal(afterRelease, await AllocationEffectCountsAsync(run));
    }

    [SqlServerFact]
    public async Task Physical_allocation_read_window_is_bounded_and_deterministic_from_real_SQL()
    {
        await using var run = await Run.StartAsync();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var firstSource = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-WINDOW-A", 100m);
        var secondSource = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-WINDOW-B", 100m);
        await using (var write = run.Store(CompanyA))
        {
            await write.Store.CreateAsync(
                ReadRequest(Guid.NewGuid(), orderId, lineId, firstSource, "10"), Metadata(run));
            await write.Store.CreateAsync(
                ReadRequest(Guid.NewGuid(), orderId, lineId, secondSource, "20"), Metadata(run));
        }

        await using var context = run.Context(CompanyA);
        var service = PhysicalAllocationReads(context, CompanyA);
        var beforeRead = await AllocationEffectCountsAsync(run);
        var first = await service.SearchAsync(new(orderId, lineId, 1));
        var repeated = await service.SearchAsync(new(orderId, lineId, 1));

        Assert.True(first.SearchWindowFull);
        Assert.True(repeated.SearchWindowFull);
        Assert.Equal(Assert.Single(first.Items), Assert.Single(repeated.Items));
        Assert.Equal(2, (await service.SearchAsync(new(orderId, lineId, 2))).Items.Count);
        Assert.Equal(beforeRead, await AllocationEffectCountsAsync(run));
    }

    [SqlServerFact]
    public async Task Physical_allocation_read_refuses_a_torn_real_SQL_window_and_corrupt_product_binding()
    {
        await using var run = await Run.StartAsync();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-READ-TORN", 100m);
        var allocationId = Guid.NewGuid();
        await using (var write = run.Store(CompanyA))
        {
            await write.Store.CreateAsync(
                ReadRequest(allocationId, orderId, lineId, source, "10"), Metadata(run));
        }

        var interceptor = new AfterInitialAllocationRead(() => ExecuteAsync(run,
            "UPDATE inventory.Allocations SET Version = Version + 1 WHERE CompanyId = @company AND Id = @id",
            ("@company", CompanyA), ("@id", allocationId)));
        await using (var torn = run.Context(CompanyA, interceptor))
        {
            var conflict = await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() =>
                PhysicalAllocationReads(torn, CompanyA).SearchAsync(new(orderId, lineId)));
            Assert.Equal((409, InventoryPhysicalAllocationReadCodesV1.ReadConflict),
                (conflict.StatusCode, conflict.Code));
        }
        Assert.True(interceptor.Triggered);

        await ExecuteAsync(run,
            "UPDATE inventory.Allocations SET DemandProductId = @product WHERE CompanyId = @company AND Id = @id",
            ("@product", Guid.NewGuid()), ("@company", CompanyA), ("@id", allocationId));
        await using var corrupt = run.Context(CompanyA);
        var unavailable = await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() =>
            PhysicalAllocationReads(corrupt, CompanyA).SearchAsync(new(orderId, lineId)));
        Assert.Equal((503, InventoryPhysicalAllocationReadCodesV1.Unavailable),
            (unavailable.StatusCode, unavailable.Code));
    }

    private static CreatePhysicalSalesAllocationV1 ReadRequest(
        Guid allocationId,
        Guid orderId,
        Guid lineId,
        PhysicalSource source,
        string quantityKg) => new(
            allocationId,
            orderId,
            lineId,
            source.ProductId,
            "7",
            source.LabelId,
            source.LabelVersion.ToString(),
            quantityKg,
            "Onaylı satış satırı için fiziksel kaynak.",
            null);

    private static InventoryPhysicalAllocationReadService PhysicalAllocationReads(
        InventoryDbContext context,
        Guid companyId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        return new InventoryPhysicalAllocationReadService(
            company,
            new PhysicalAllocationReadUser(companyId),
            new PhysicalAllocationReadAuthority(),
            new SqlInventoryPhysicalAllocationReader(context));
    }

    private sealed class PhysicalAllocationReadUser(Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "physical-allocation-reader";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid requestedCompanyId) => requestedCompanyId == companyId;
    }

    private sealed class PhysicalAllocationReadAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(
                permission == ErpPermissions.InventoryPhysicalAllocationWrite,
                true,
                new HashSet<string>(),
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
    }

    private sealed class AfterInitialAllocationRead(Func<Task> action) : DbCommandInterceptor
    {
        public bool Triggered { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!Triggered && command.CommandText.Contains("[inventory].[Allocations]", StringComparison.Ordinal))
            {
                Triggered = true;
                await action();
            }

            return result;
        }
    }
}
