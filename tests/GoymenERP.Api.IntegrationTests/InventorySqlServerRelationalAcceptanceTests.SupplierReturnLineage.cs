using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
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

    /// <summary>
    /// AK-8b (SAT-D433, SAT-D440, K3-B2/TI3) on real SQL Server: a label merged from two goods receipts is returned in ONE
    /// command — one exit movement per receipt, each sourced from its own receipt, one command record — and a request that moves
    /// kilograms between the receipts stages nothing. A second attempt under the key is a replay; each receipt's open quantity
    /// then reads what its own exit took.
    /// </summary>
    [SqlServerFact]
    public async Task An_atomic_lineage_return_of_a_merged_label_stages_one_exit_per_receipt_under_one_command_and_another_distribution_stages_nothing()
    {
        await using var run = await Run.StartAsync();
        var first = await ReceiveForSupplierReturnAsync(run, BlockState.Free);
        // The domain merges only labels of one lot: the second receipt arrives into the first one's lot.
        var second = await ReceiveForSupplierReturnAsync(run, BlockState.Free, first.LotId);
        var merged = Guid.NewGuid();
        await using (var context = run.Context(CompanyA))
        {
            var versions = await context.Labels.AsNoTracking().Where(row => row.Id == first.LabelId || row.Id == second.LabelId)
                .ToDictionaryAsync(row => row.Id, row => row.Version);
            await using var owner = run.Store(CompanyA);
            await owner.Store.TransformAsync(new TransformInventoryLabelsV1(Guid.NewGuid(), InventoryLabelTransformationKindV1.Merge,
                [new(first.LabelId, versions[first.LabelId].ToString(CultureInfo.InvariantCulture)),
                    new(second.LabelId, versions[second.LabelId].ToString(CultureInfo.InvariantCulture))],
                [new(merged, $"SYN-AK8B-MERGED-{Guid.NewGuid():N}", "8")], GoodsReceiptTime.AddMinutes(30),
                "Sentetik AK-8b birlestirme."), Metadata(run));
        }

        var metadata = SupplierReturnMetadata(run);
        var returnId = Guid.NewGuid();
        Guid[] lines = [Guid.NewGuid(), Guid.NewGuid()], movements = [Guid.NewGuid(), Guid.NewGuid()];
        InventorySupplierReturnLineageExitRequestV1 Request(string firstKg, string secondKg) => new(CompanyA, returnId, merged,
            BlockState.Free, "3", null, GoodsReceiptTime.AddHours(1),
            [new(first.AcceptanceId, lines[0], movements[0], firstKg, null, "41.5", null),
                new(second.AcceptanceId, lines[1], movements[1], secondKg, null, "41.5", null)]);

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventorySupplierReturnExitParticipant.EnlistAsync(
                connection, transaction, CompanyA, GoodsReceiptSettings(run));
            var refused = await Assert.ThrowsAsync<InventorySupplierReturnExitException>(() =>
                participant.StageLineageAsync(Request("2", "1"), metadata));
            Assert.Equal((409, InventorySupplierReturnExitCodesV1.LineageMismatch), (refused.Status, refused.Code));
            await transaction.RollbackAsync();
        }

        Assert.Equal(8m, await LabelBlockKgAsync(run, merged, BlockState.Free));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = @kind", ("@kind", (int)StockMovementKind.SupplierReturnExit)));

        InventorySupplierReturnLineageExitResultV1 staged;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventorySupplierReturnExitParticipant.EnlistAsync(
                connection, transaction, CompanyA, GoodsReceiptSettings(run));
            staged = await participant.StageLineageAsync(Request("1.5", "1.5"), metadata);
            await transaction.CommitAsync();
        }

        Assert.False(staged.IsReplay);
        Assert.Equal([(first.AcceptanceId, first.MovementId, movements[0], "1.5"), (second.AcceptanceId, second.MovementId, movements[1], "1.5")],
            staged.Parts.Select(part => (part.GoodsAcceptanceId, part.OriginalReceiptMovementId, part.InventoryMovementId, part.QuantityKg)));
        Assert.All(staged.Parts, part => Assert.Equal(merged, part.LabelId));
        Assert.Equal(5m, await LabelBlockKgAsync(run, merged, BlockState.Free));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = @kind AND SourceId IN (@first, @second)",
            ("@kind", (int)StockMovementKind.SupplierReturnExit), ("@first", first.MovementId), ("@second", second.MovementId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = @name AND TargetId = @id",
            ("@name", InventorySupplierReturnExitContractV1.LineageCommandName), ("@id", returnId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = @name",
            ("@name", InventorySupplierReturnExitContractV1.CommandName)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.MovementCostStamps WHERE MovementId IN (@a, @b) AND EvidenceJson LIKE '%supplierReturnLineageSelection%'",
            ("@a", movements[0]), ("@b", movements[1])));

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventorySupplierReturnExitParticipant.EnlistAsync(
                connection, transaction, CompanyA, GoodsReceiptSettings(run));
            var replay = await participant.StageLineageAsync(Request("1.5", "1.5"), metadata with { CorrelationId = "lineage-return-retry" });
            await transaction.CommitAsync();
            Assert.True(replay.IsReplay);
            Assert.Equal(metadata.CorrelationId, replay.OriginalCorrelationId);
        }

        Assert.Equal(5m, await LabelBlockKgAsync(run, merged, BlockState.Free));
        await using (var context = run.Context(CompanyA))
        {
            var company = new CompanyContext();
            company.Set(CompanyA);
            var plan = await new InventorySupplierReturnLineageReader(context, company).PlanAsync(CompanyA, merged, "1", null);
            Assert.Equal(("5", false), (plan.FreeKg, plan.TracksPieces));
            Assert.Equal([("4", "2.5", "0.5", true), ("4", "2.5", "0.5", true)],
                plan.Receipts.Select(receipt => (receipt.ReceivedKg, receipt.OpenKg, receipt.DistributedKg!, receipt.PartsReadable)));
            Assert.All(plan.Receipts, receipt => Assert.Equal(new InventoryExactCostRatioV1("5", "2"), Normalised(receipt.PendingRemainingKg!)));
        }
    }

    /// <summary>
    /// SAT-D456 / SAT-D461 (golden EK1 E06, E08, E10) on real SQL Server: a quantity return measured on the billed document amount
    /// is refused on any kuruş difference — against the readable moving average and, when it cannot be read, against the approved
    /// manual cost — and stages nothing. An unreadable average without an approved manual cost is the SAT-D393 boundary.
    /// </summary>
    [SqlServerFact]
    public async Task A_quantity_supplier_return_measured_on_the_billed_amount_is_refused_on_a_kurus_difference_with_a_readable_or_an_unreadable_average()
    {
        await using var run = await Run.StartAsync();
        var (first, merged) = await MergedQuantityReturnLabelAsync(run);

        // Unreadable average: nothing without the approved manual cost; with it, 3 × 20.7 = 62.10 against 61.50 billed is a difference.
        await SeedQuantityReturnAverageAsync(run, merged, null);
        Assert.Equal((409, InventorySupplierReturnExitCodesV1.QuantityCostUnreadable),
            await QuantityReturnRefusalAsync(run, QuantityReturnRequest(first, merged, null, "61.5", null)));
        Assert.Equal((409, InventorySupplierReturnExitCodesV1.QuantityCostUnreadable),
            await QuantityReturnRefusalAsync(run, QuantityReturnRequest(first, merged, "20.7", "61.5", null)));
        Assert.Equal((409, InventorySupplierReturnExitCodesV1.CostDifferenceUnposted),
            await QuantityReturnRefusalAsync(run, QuantityReturnRequest(first, merged, "20.7", "61.5", true)));

        // Readable average 21.25: 3 × 21.25 = 63.75 against 63 billed is a difference; a manual cost or an approval has no place.
        await SeedQuantityReturnAverageAsync(run, merged, 21.25m);
        Assert.Equal((409, InventorySupplierReturnExitCodesV1.CostDifferenceUnposted),
            await QuantityReturnRefusalAsync(run, QuantityReturnRequest(first, merged, null, "63", null)));
        Assert.Equal((409, InventorySupplierReturnExitCodesV1.CostDifferenceUnposted),
            await QuantityReturnRefusalAsync(run, QuantityReturnRequest(first, merged, "21.25", "63.75", true)));

        Assert.Equal(8m, await LabelBlockKgAsync(run, merged, BlockState.Free));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = @kind", ("@kind", (int)StockMovementKind.SupplierReturnExit)));
    }

    /// <summary>
    /// SAT-D456 / SAT-D461 (golden EK1 E13b shape) on real SQL Server: with a readable moving average and no kuruş difference the
    /// quantity return leaves the foreign label at the average Inventory itself read, and answers that average.
    /// </summary>
    [SqlServerFact]
    public async Task A_quantity_supplier_return_without_a_kurus_difference_leaves_the_label_at_the_moving_average_inventory_read()
    {
        await using var run = await Run.StartAsync();
        var (first, merged) = await MergedQuantityReturnLabelAsync(run);
        await SeedQuantityReturnAverageAsync(run, merged, 21.25m);
        var request = QuantityReturnRequest(first, merged, null, "63.75", null);

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventorySupplierReturnExitParticipant.EnlistAsync(
                connection, transaction, CompanyA, GoodsReceiptSettings(run));
            var staged = await participant.StageAsync(request, SupplierReturnMetadata(run));
            await transaction.CommitAsync();
            Assert.False(staged.IsReplay);
            Assert.Equal((merged, "21.25"), (staged.LabelId, staged.OriginalUnitCostTryPerKg));
        }

        Assert.Equal(5m, await LabelBlockKgAsync(run, merged, BlockState.Free));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = @kind AND Id = @id AND SourceId = @receipt",
            ("@kind", (int)StockMovementKind.SupplierReturnExit), ("@id", request.InventoryMovementId), ("@receipt", first.MovementId)));
    }

    private static async Task<(InventoryGoodsReceiptRequestV1 First, Guid Merged)> MergedQuantityReturnLabelAsync(Run run)
    {
        var first = await ReceiveForSupplierReturnAsync(run, BlockState.Free);
        // The domain merges only labels of one lot: the second receipt arrives into the first one's lot.
        var second = await ReceiveForSupplierReturnAsync(run, BlockState.Free, first.LotId);
        var merged = Guid.NewGuid();
        await using var context = run.Context(CompanyA);
        var versions = await context.Labels.AsNoTracking().Where(row => row.Id == first.LabelId || row.Id == second.LabelId)
            .ToDictionaryAsync(row => row.Id, row => row.Version);
        await using var owner = run.Store(CompanyA);
        await owner.Store.TransformAsync(new TransformInventoryLabelsV1(Guid.NewGuid(), InventoryLabelTransformationKindV1.Merge,
            [new(first.LabelId, versions[first.LabelId].ToString(CultureInfo.InvariantCulture)),
                new(second.LabelId, versions[second.LabelId].ToString(CultureInfo.InvariantCulture))],
            [new(merged, $"SYN-AK8B-QTY-{Guid.NewGuid():N}", "8")], GoodsReceiptTime.AddMinutes(30),
            "Sentetik AK-8b miktar iadesi birlestirme."), Metadata(run));
        return (first, merged);
    }

    private static InventorySupplierReturnExitRequestV1 QuantityReturnRequest(InventoryGoodsReceiptRequestV1 receipt, Guid merged,
        string? manualCost, string billedAmount, bool? approved) => new(CompanyA, Guid.NewGuid(), Guid.NewGuid(), receipt.AcceptanceId,
        Guid.NewGuid(), BlockState.Free, "3", manualCost, GoodsReceiptTime.AddHours(1), SourceLabelId: merged,
        LineageMode: InventorySupplierReturnLineageModeV1.Quantity, ApprovedCostException: approved, SupplierReturnAmountTry: billedAmount);

    private static async Task<(int Status, string Code)> QuantityReturnRefusalAsync(Run run, InventorySupplierReturnExitRequestV1 request)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var participant = await InventorySupplierReturnExitParticipant.EnlistAsync(
            connection, transaction, CompanyA, GoodsReceiptSettings(run));
        var refused = await Assert.ThrowsAsync<InventorySupplierReturnExitException>(() =>
            participant.StageAsync(request, SupplierReturnMetadata(run)));
        await transaction.RollbackAsync();
        return (refused.Status, refused.Code);
    }

    /// <summary>
    /// Sets the label's location/product moving-average row: a known average over exactly the company's costed stock there, or
    /// (null) an unreadable position. Synthetic test state; the ledger writer itself is covered by the average-cost facts.
    /// </summary>
    private static async Task SeedQuantityReturnAverageAsync(Run run, Guid labelId, decimal? average)
    {
        var location = await run.LocationOfAsync(CompanyA);
        var product = await run.ScalarAsync<Guid>("SELECT ProductId FROM inventory.Labels WHERE Id = @id", ("@id", labelId));
        await run.ExecuteAsync(
            "DELETE FROM inventory.AverageCostLedger WHERE CompanyId = @company AND LocationId = @location AND ProductId = @product;",
            ("@company", CompanyA), ("@location", location), ("@product", product));
        if (average is not { } known)
        {
            await run.ExecuteAsync(
                """
                INSERT INTO inventory.AverageCostLedger
                    (Id, CompanyId, LocationId, ProductId, Status, UnreadableReason, QuantityKg, AverageCostPerKg, Version, UpdatedAtUtc)
                VALUES (NEWID(), @company, @location, @product, 2, 3, NULL, NULL, 1, SYSUTCDATETIME());
                """, ("@company", CompanyA), ("@location", location), ("@product", product));
            return;
        }

        await run.ExecuteAsync(
            """
            INSERT INTO inventory.AverageCostLedger
                (Id, CompanyId, LocationId, ProductId, Status, UnreadableReason, QuantityKg, AverageCostPerKg, Version, UpdatedAtUtc)
            SELECT NEWID(), @company, @location, @product, 1, NULL, SUM(QuantityKg), @average, 1, SYSUTCDATETIME()
            FROM inventory.Balances
            WHERE CompanyId = @company AND ProductId = @product AND LocationId = @location AND OwnerKind = 1 AND OwnerId = @company
                AND CustodianKind = 1 AND CustodianId = @company AND QuantityKg > 0;
            """, ("@company", CompanyA), ("@location", location), ("@product", product), ("@average", known));
    }

    private static InventoryExactCostRatioV1 Normalised(InventoryExactCostRatioV1 ratio)
    {
        var numerator = System.Numerics.BigInteger.Parse(ratio.Numerator, CultureInfo.InvariantCulture);
        var denominator = System.Numerics.BigInteger.Parse(ratio.Denominator, CultureInfo.InvariantCulture);
        var divisor = System.Numerics.BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new((numerator / divisor).ToString(CultureInfo.InvariantCulture), (denominator / divisor).ToString(CultureInfo.InvariantCulture));
    }
}
