using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D384 over real SQL: only the active last label is counted. A label a split consumed answers the converted
/// warning ("dönüştürüldü → yeni etiket") without a count row, command or audit, while its output label is counted
/// and its difference is the only one recorded.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Stock_count_of_a_label_consumed_by_a_split_answers_converted_and_only_the_output_label_is_counted()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-CONVERTED", 100m);
        var outputA = Guid.NewGuid();
        var outputB = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.TransformAsync(new TransformInventoryLabelsV1(
                    Guid.NewGuid(),
                    InventoryLabelTransformationKindV1.Split,
                    [new(source.LabelId, source.LabelVersion.ToString())],
                    [new(outputA, "SYN-COUNT-CONVERTED-A", "40"), new(outputB, "SYN-COUNT-CONVERTED-B", "60")],
                    At,
                    "Sentetik sayim donusum kabul testi."),
                Metadata(run) with { IdempotencyKey = $"count-converted-split-{Guid.NewGuid():N}" });
        }

        var countsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockCounts");
        var consumedVersion = await LabelVersionAsync(run, source.LabelId);
        await using (var scope = run.Store(CompanyA))
        {
            await AssertCountRefused(scope.Store.RecordStockCountAsync(Count(source.LabelId, consumedVersion, "5"),
                    Metadata(run), static _ => Task.CompletedTask),
                409, InventoryStockCountCodesV1.LabelConverted);
        }

        // The consumed label never enters a count difference: nothing was written for it.
        Assert.Equal(countsBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockCounts"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.StockCounts WHERE LabelId = @id", ("@id", source.LabelId)));

        // The output label is the active last label and is counted against its own book.
        var outputVersion = await LabelVersionAsync(run, outputA);
        await using (var scope = run.Store(CompanyA))
        {
            var recorded = await scope.Store.RecordStockCountAsync(Count(outputA, outputVersion, "39"), Metadata(run),
                static _ => Task.CompletedTask);
            Assert.Equal((InventoryStockCountStatusesV1.PendingApproval, "40", "39", "-1"),
                (recorded.Count.Status, recorded.Count.BookKg, recorded.Count.CountedKg, recorded.Count.DifferenceKg));
        }

        // A label without stock that no transformation consumed keeps the position conflict.
        var empty = NewLabel(CompanyA, "SYN-COUNT-CONVERTED-EMPTY");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Labels.SaveLabelAsync(empty, Metadata(run));
        }

        await using (var scope = run.Store(CompanyA))
        {
            await AssertCountRefused(scope.Store.RecordStockCountAsync(Count(empty.Id, 1, "5"), Metadata(run),
                    static _ => Task.CompletedTask),
                409, InventoryStockCountCodesV1.PositionConflict);
        }
    }
}
