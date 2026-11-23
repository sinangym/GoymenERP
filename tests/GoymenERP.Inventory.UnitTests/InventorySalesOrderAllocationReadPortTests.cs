using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// DEC-SAL-005, the Inventory side without a database: the module registers its answer to the Sales-declared active-allocation
/// read (so Sales never reaches Inventory tables), and the port refuses to read without a server-resolved company before any
/// connection is opened. What it counts on real rows — only ACTIVE allocations of the order's lines, company-filtered — is
/// proven on SQL Server (<c>SalesOrderCancellationSqlServerRelationalAcceptanceTests</c>). F-017 STOK §5.1 (STK-S16): the same
/// for the kind-aware V2 read, registered beside V1 (V1 unchanged); its real-row answer is proven in
/// <c>InventorySqlServerRelationalAcceptanceTests.FutureSupply</c>. SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventorySalesOrderAllocationReadPortTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void The_inventory_module_registers_the_sales_allocation_read_over_its_own_store_as_a_scoped_service()
    {
        var services = new ServiceCollection().AddInventoryModule(new Settings("Server=unused.invalid;Database=unused;"));

        var registration = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISalesOrderAllocationReadPortV1));
        Assert.Equal((ServiceLifetime.Scoped, typeof(InventorySalesOrderAllocationReadPort)),
            (registration.Lifetime, registration.ImplementationType));
    }

    [Fact]
    public async Task Without_a_resolved_company_the_port_refuses_before_any_connection_and_never_reads_unscoped()
    {
        await using var context = Context(resolved: false);
        var port = new InventorySalesOrderAllocationReadPort(context, new CompanyContext());

        // The connection string points nowhere: reaching the database would be a SqlException, not this refusal.
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ReadOrderAllocationsAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task An_empty_order_id_answers_nothing_for_the_context_company_without_a_query()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        await using var context = Context(resolved: true);
        var port = new InventorySalesOrderAllocationReadPort(context, company);

        var answer = await port.ReadOrderAllocationsAsync(Guid.Empty);

        Assert.Equal((CompanyA, Guid.Empty, 0), (answer.CompanyId, answer.OrderId, answer.Lines.Count));
    }

    [Fact]
    public void The_inventory_module_registers_the_kind_aware_sales_allocation_read_beside_the_unchanged_v1()
    {
        var services = new ServiceCollection().AddInventoryModule(new Settings("Server=unused.invalid;Database=unused;"));

        var v2 = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISalesOrderAllocationReadPortV2));
        Assert.Equal((ServiceLifetime.Scoped, typeof(InventorySalesOrderAllocationReadPortV2)), (v2.Lifetime, v2.ImplementationType));
        var v1 = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISalesOrderAllocationReadPortV1));
        Assert.Equal(typeof(InventorySalesOrderAllocationReadPort), v1.ImplementationType);
    }

    [Fact]
    public async Task Without_a_resolved_company_the_kind_aware_port_refuses_before_any_connection_and_never_reads_unscoped()
    {
        await using var context = Context(resolved: false);
        var port = new InventorySalesOrderAllocationReadPortV2(context, new CompanyContext());

        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ReadOrderAllocationsAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task An_empty_order_id_answers_no_allocation_of_either_kind_for_the_context_company_without_a_query()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        await using var context = Context(resolved: true);
        var port = new InventorySalesOrderAllocationReadPortV2(context, company);

        var answer = await port.ReadOrderAllocationsAsync(Guid.Empty);

        Assert.Equal((CompanyA, Guid.Empty, 0), (answer.CompanyId, answer.OrderId, answer.Lines.Count));
    }

    [Fact]
    public void The_future_supply_decimals_sales_checks_are_the_decimals_inventory_holds_a_future_quantity_with()
    {
        // Sales refuses (never rounds) a future quantity with more decimals than this; the owner's value object is the source.
        Assert.Equal(FutureSupplyQuantity.MaximumDecimals, SalesOrderAllocationKindsV2.FutureSupplyQuantityMaximumDecimals);
    }

    /// <summary>The two host settings the module reads (no configuration package in this test project).</summary>
    private sealed class Settings(string connectionString) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "DB_CONNECTION_STRING" ? connectionString : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }

    private static InventoryDbContext Context(bool resolved)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        return new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}
