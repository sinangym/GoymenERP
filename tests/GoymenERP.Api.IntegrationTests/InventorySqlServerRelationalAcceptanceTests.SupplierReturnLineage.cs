using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AK-8 (SAT-D433 TI1, SAT-D445) on real SQL Server: the supplier-return lineage reader walks a real merge back to its two goods
/// receipts with kilogram-weighted exact shares, and another company's label is the closed 503 receipt-trace refusal. SENTETIK.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Supplier_return_lineage_reader_resolves_a_real_merge_to_its_receipts_and_refuses_another_company_s_label()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyB);
        var first = await ExactLineageForeignCostedLabelAsync(run, Guid.NewGuid(), location, 600m);
        var second = await ExactLineageForeignCostedLabelAsync(run, Guid.NewGuid(), location, 400m);
        var merged = Guid.NewGuid();
        await using (var context = run.Context(CompanyB))
        {
            var versions = await context.Labels.AsNoTracking().Where(row => row.Id == first.Id || row.Id == second.Id)
                .ToDictionaryAsync(row => row.Id, row => row.Version);
            var request = new TransformInventoryLabelsV1(Guid.NewGuid(), InventoryLabelTransformationKindV1.Merge,
                [new(first.Id, versions[first.Id].ToString(CultureInfo.InvariantCulture)),
                    new(second.Id, versions[second.Id].ToString(CultureInfo.InvariantCulture))],
                [new(merged, $"SYN-AK8-MERGED-{Guid.NewGuid():N}", "1000")], ExactLineageForeignAt.AddMinutes(1),
                "Sentetik AK-8 birlestirme.");
            await using var owner = run.Store(CompanyB);
            await owner.Store.TransformAsync(request, Metadata(run));
        }

        await using (var context = run.Context(CompanyB))
        {
            var acceptances = await context.Movements.AsNoTracking()
                .Where(row => row.Kind == (int)StockMovementKind.Receipt && row.SourceKind == InventoryGoodsReceiptContractV1.SourceKind)
                .Join(context.MovementLines.AsNoTracking(), movement => movement.Id, line => line.MovementId,
                    (movement, line) => new { line.LabelId, movement.SourceId })
                .Where(row => row.LabelId == first.Id || row.LabelId == second.Id)
                .ToDictionaryAsync(row => row.LabelId, row => row.SourceId);
            var company = new CompanyContext();
            company.Set(CompanyB);
            var lineage = await new InventorySupplierReturnLineageReader(context, company).ReadAsync(CompanyB, merged);

            Assert.Equal(2, lineage.Roots.Count);
            var byLabel = lineage.Roots.ToDictionary(root => root.RootLabelId);
            Assert.Equal(new InventoryExactCostRatioV1("3", "5"), byLabel[first.Id].Share);
            Assert.Equal(new InventoryExactCostRatioV1("2", "5"), byLabel[second.Id].Share);
            Assert.All(lineage.Roots, root => Assert.Equal(InventorySupplierReturnLineageRootKindV1.PurchaseReceipt, root.Kind));
            Assert.Equal(acceptances[first.Id], byLabel[first.Id].GoodsAcceptanceId);
            Assert.Equal(acceptances[second.Id], byLabel[second.Id].GoodsAcceptanceId);
        }

        await using (var context = run.Context(CompanyA))
        {
            var company = new CompanyContext();
            company.Set(CompanyA);
            var refused = await Assert.ThrowsAsync<InventorySupplierReturnExitException>(() =>
                new InventorySupplierReturnLineageReader(context, company).ReadAsync(CompanyA, merged));
            Assert.Equal((503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid), (refused.Status, refused.Code));
        }
    }
}
