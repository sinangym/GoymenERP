using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    /// <summary>
    /// STK-S33 (KLN-D44, KLN-D41): the trace and its search read the resolved company's own labels and movements on real SQL,
    /// see no other company's label and create, change or version nothing.
    /// </summary>
    [SqlServerFact]
    public async Task Label_trace_reads_only_the_resolved_companys_chain_without_an_effect()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, "SYN-STK33-TRACE");
        var position = Position(CompanyA, location, label, BlockState.Free);
        var acceptanceId = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            var seedMetadata = Metadata(run) with { CorrelationId = $"seed-{Guid.NewGuid():N}" };
            await scope.Lots.SaveLotAsync(new MaterialLot(LotA, CompanyA, Product), [], seedMetadata);
            await scope.Labels.SaveLabelAsync(label, seedMetadata);
            await scope.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Receipt,
                    new StockSourceReference(InventoryGoodsReceiptContractV1.SourceKind, acceptanceId), In(position, 42.5m)),
                seedMetadata);
        }

        var unrelated = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-STK33-OTHER", 10m);
        var foreignLabel = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-STK33-FOREIGN", 5m);
        const string Footprint =
            "SELECT CONCAT((SELECT COUNT(*) FROM inventory.Labels), ':', (SELECT SUM(Version) FROM inventory.Labels), ':', "
            + "(SELECT COUNT(*) FROM inventory.Movements), ':', (SELECT COUNT(*) FROM inventory.MovementLines))";
        var before = await run.ScalarAsync<string>(Footprint);

        await using (var context = run.Context(CompanyA))
        {
            var trace = await LabelTrace(context, CompanyA).ReadAsync(label.Id, InventoryLabelTraceDirectionV1.Forward);

            Assert.Equal((label.Id, "SYN-STK33-TRACE", InventoryLabelTraceApiV1.Forward),
                (trace.LabelId, trace.LabelCode, trace.Direction));
            Assert.Equal(0, Assert.Single(trace.Labels).Depth);
            Assert.Empty(trace.Links);
            var receipt = Assert.Single(trace.Events);
            Assert.Equal(
                (InventoryLabelTraceStepsV1.SupplierReceipt, "Receipt", "In", "42.5", InventoryGoodsReceiptContractV1.SourceKind,
                    acceptanceId),
                (receipt.Step, receipt.MovementKind, receipt.Direction, receipt.QuantityKg, receipt.SourceKind, receipt.SourceId));
            Assert.Equal(
                (InventoryLabelTraceStatusesV1.Answered, InventoryLabelTraceStatusesV1.NotAnswered,
                    InventoryLabelTraceStatusesV1.Answered),
                (trace.ProductionStatus, trace.PartyStatus, trace.PartyNameStatus));
            Assert.False(trace.WindowFull);

            var search = await LabelTrace(context, CompanyA).SearchAsync("SYN-STK33");
            Assert.Equal([unrelated.LabelId, label.Id], search.Items.Select(item => item.LabelId));
            Assert.All(search.Items, item => Assert.Equal(InventoryLabelTraceMatchesV1.LabelCode, item.MatchedBy));
            Assert.Equal(InventoryLabelTraceStatusesV1.NotAnswered, search.SupplierSearchStatus);
        }

        await using (var foreign = run.Context(CompanyB))
        {
            var refused = await Assert.ThrowsAsync<InventoryLabelTraceException>(
                () => LabelTrace(foreign, CompanyB).ReadAsync(label.Id, InventoryLabelTraceDirectionV1.Backward));
            Assert.Equal((404, InventoryLabelTraceCodesV1.NotFound), (refused.StatusCode, refused.Code));

            var search = await LabelTrace(foreign, CompanyB).SearchAsync("SYN-STK33");
            Assert.Equal(foreignLabel.LabelId, Assert.Single(search.Items).LabelId);
        }

        Assert.Equal(before, await run.ScalarAsync<string>(Footprint));
    }

    private static InventoryLabelTraceService LabelTrace(InventoryDbContext context, Guid companyId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        return new InventoryLabelTraceService(
            company,
            new LabelDetailUser(companyId),
            new LabelDetailAuthority(),
            new LabelDetailGenealogyReader(context),
            new SqlInventoryLabelTraceReader(context),
            production: null,
            parties: null,
            partyNames: null,
            supplierSearch: null);
    }
}
