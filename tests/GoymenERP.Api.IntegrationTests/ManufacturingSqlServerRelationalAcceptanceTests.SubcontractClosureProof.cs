using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    // Reuses the existing native Sales→Manufacturing→customer-owned Inventory→Logistics fixture.
    // Synthetic authority/customer/risk verdicts remain fixture inputs. This is owner-read evidence, not native human close/Finance acceptance.
    [SqlServerFact]
    public async Task Subcontract_closure_owner_reads_preserve_own_order_shares_customer_ownership_and_missing_proof_on_real_sql()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var first = await SeedSubcontractDeliveryHostAsync(run, "SYN-CLOSURE-FIRST");
        var second = await SeedSubcontractDeliveryHostAsync(run, "SYN-CLOSURE-SECOND", first.Party, first.Account);
        await using var scope = SubcontractHostScope.Create(run, first.Party, first.Account);
        var one = await scope.CommandAsync(first); var two = await scope.CommandAsync(second);
        var created = await scope.Host.CreateAsync(one with { Lines = [one.Lines[0], two.Lines[0]] },
            new("sub-close-create", "SENTETIK-sub-close-create", null, At));
        await using var inventoryDb = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).Options, scope.Company);
        var inventory = new InventorySubcontractOrderDeliveryProofReadPortV1(inventoryDb, scope.Company);
        Task Fence(CancellationToken token) { token.ThrowIfCancellationRequested(); Assert.Equal(CompanyA, scope.Company.CompanyId); return Task.CompletedTask; }
        var pending = await inventory.ReadAsync(CompanyA, first.SalesOrder, [], Fence);
        Assert.True(pending.Available); Assert.True(pending.HasOpenAllocations);

        await PrepareS3LoadedInstructionAsync(scope, created, run.Actor.ToString("D"));
        var current = await scope.Host.ReadAsync(created.InstructionId);
        var shipmentIds = current.Lines.ToDictionary(x => x.Source.SalesOrderId, _ => Guid.NewGuid());
        await scope.Host.ShipAsync(created.InstructionId, new(current.Version, At.AddHours(2), current.Lines.Select(line =>
            new SubcontractDeliveryShipmentLineV1(line.LineId, shipmentIds[line.Source.SalesOrderId], Guid.NewGuid(), line.AllocationVersion!,
                line.LabelVersion!, line.Source.SalesOrderId, line.Source.SalesOrderLineId, line.Source.ManufacturingOrderId,
                line.Source.OutputFactId, line.Source.LabelId)).ToArray()), new("sub-close-ship", "SENTETIK-sub-close-ship", null, At));
        var logistics = new LogisticsSubcontractOrderDeliveryReadPortV1(scope.Context, scope.Company, scope.Store,
            new LogisticsSubcontractDeliveryNetWeightReadPort(scope.Store, scope.Company));
        var order = await logistics.ReadAsync(CompanyA, first.SalesOrder, Fence);
        Assert.True(order.Available);
        var instruction = Assert.Single(order.Instructions);
        Assert.Equal(first.SalesOrder, Assert.Single(instruction.Lines).Source.SalesOrderId);
        Assert.Equal(shipmentIds[first.SalesOrder], Assert.Single(instruction.Shipments).ShipmentId);
        Assert.Equal(2, instruction.Weighing!.Lines.Count); // Shared immutable denominator remains intact.
        var physical = await inventory.ReadAsync(CompanyA, first.SalesOrder, [shipmentIds[first.SalesOrder]], Fence);
        Assert.True(physical.Available); Assert.False(physical.HasOpenAllocations);
        var proof = Assert.Single(physical.Shipments);
        Assert.Equal(80m, proof.PhysicalKg);
        Assert.Equal(first.Party, proof.Source.CustomerPartyId);
        Assert.Equal(first.Output.LabelId, proof.Source.LabelId);
        Assert.Equal(first.ServiceProduct, proof.Source.SalesServiceProductId);
        Assert.NotEqual(proof.Source.ProductId, proof.Source.SalesServiceProductId);
        Assert.False((await inventory.ReadAsync(CompanyA, first.SalesOrder, [shipmentIds[second.SalesOrder]], Fence)).Available);
        Assert.False((await inventory.ReadAsync(CompanyA, first.SalesOrder, [Guid.NewGuid()], Fence)).Available);
        Assert.False((await inventory.ReadAsync(CompanyB, first.SalesOrder, [shipmentIds[first.SalesOrder]], Fence)).Available);
        Assert.False((await logistics.ReadAsync(CompanyB, first.SalesOrder, Fence)).Available);
    }
}
