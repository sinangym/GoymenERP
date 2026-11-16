using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D272 / SAT-D347 / SAT-D350 (STK-S35) and KLN-D83 / KLN-D84 (STK-SC33-a) over real SQL: the company-wide port weights
/// every stocked location of the resolved company by the on-hand kilograms of the company's own goods it holds itself
/// (consignment a counterparty owns and the company's goods held at a fason supplier do not weigh), leaves a location
/// without stock out, answers "hesaplanamadı" for one stocked location it cannot read (unreadable ledger, no ledger row, or
/// a ledger quantity other than the stock), and never sees another company's stock or ledger. The ledger writer stages receipts, issues and transfers (SAT-D260: at the source
/// average) in the caller's context and persists nothing until that caller's single save, moving a row's version once per
/// save. SENTETIK data only.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Average_cost_company_wide_port_weights_on_hand_kg_and_fails_closed_for_a_stocked_location_it_cannot_read()
    {
        await using var run = await Run.StartAsync();
        var first = await run.LocationOfAsync(CompanyA);
        var second = await CompanyWideLocationAsync(run, CompanyA, 1);
        var locationB = await run.LocationOfAsync(CompanyB);
        var product = Guid.NewGuid();
        var counterparty = Guid.NewGuid();

        var none = await CompanyWideReadAsync(run, CompanyA, product);
        Assert.Equal((InventoryAverageCostReadStatusV1.NoSource, null, null, 0m),
            (none.Status, none.AverageCostPerKg, none.UnreadableCode, none.OnHandKg));
        Assert.Empty(none.Sources);

        // Free and blocked company-owned stock both weigh; goods a counterparty owns at the same location do not.
        await SeedCompanyWideStockAsync(run, CompanyA, first, product, CompanyOwner, CompanyA, FreeBlock, 6m);
        await SeedCompanyWideStockAsync(run, CompanyA, first, product, CompanyOwner, CompanyA, BlockedBlock, 4m);
        await SeedCompanyWideStockAsync(run, CompanyA, first, product, CounterpartyOwner, counterparty, FreeBlock, 50m);
        await SeedCompanyWideStockAsync(run, CompanyA, second, product, CompanyOwner, CompanyA, FreeBlock, 20m);

        // Stock the ledger never costed: no figure, the first location in location order is named.
        var uncosted = await CompanyWideReadAsync(run, CompanyA, product);
        Assert.Equal((InventoryAverageCostReadStatusV1.Unreadable, null, "uncosted_stock_on_hand", new[] { first, second }.Min(), 30m),
            (uncosted.Status, uncosted.AverageCostPerKg, uncosted.UnreadableCode, uncosted.UnreadableLocationId, uncosted.OnHandKg));
        Assert.All(uncosted.Sources, source => Assert.Null(source.LedgerVersion));

        await InsertKnownAverageCostAsync(run, CompanyA, first, product, 10m, 10m);
        await InsertKnownAverageCostAsync(run, CompanyA, second, product, 20m, 13m);
        await SeedCompanyWideStockAsync(run, CompanyB, locationB, product, CompanyOwner, CompanyB, FreeBlock, 5m);
        await InsertKnownAverageCostAsync(run, CompanyB, locationB, product, 5m, 99m);

        // (10·10 + 20·13) / 30 = 12; company B's stock and ledger for the same product are invisible.
        var known = await CompanyWideReadAsync(run, CompanyA, product);
        Assert.Equal((InventoryAverageCostReadStatusV1.Value, 12.0000m, null, null, 30m, CompanyA, "company-base-currency"),
            (known.Status, known.AverageCostPerKg, known.UnreadableCode, known.UnreadableLocationId, known.OnHandKg,
                known.CompanyId, known.CostCurrencyBasis));
        Assert.Equal(
            new[] { (first, 10m, (long?)1), (second, 20m, (long?)1) }.OrderBy(source => source.Item1),
            known.Sources.Select(source => (source.LocationId, source.OnHandKg, source.LedgerVersion)));
        var other = await CompanyWideReadAsync(run, CompanyB, product);
        Assert.Equal((InventoryAverageCostReadStatusV1.Value, 99m, 5m), (other.Status, other.AverageCostPerKg, other.OnHandKg));

        // One unreadable stocked location makes the whole answer unreadable: no partial average.
        await run.ExecuteAsync(
            """
            UPDATE inventory.AverageCostLedger
            SET Status = 2, UnreadableReason = 3, QuantityKg = NULL, AverageCostPerKg = NULL, Version = 2
            WHERE CompanyId = @company AND LocationId = @location AND ProductId = @product;
            """, ("@company", CompanyA), ("@location", second), ("@product", product));
        var unreadable = await CompanyWideReadAsync(run, CompanyA, product);
        Assert.Equal((InventoryAverageCostReadStatusV1.Unreadable, null, "foreign_currency_undecided", second, 30m),
            (unreadable.Status, unreadable.AverageCostPerKg, unreadable.UnreadableCode, unreadable.UnreadableLocationId,
                unreadable.OnHandKg));

        // A known figure that covers another quantity than the stock is not blended either.
        await run.ExecuteAsync(
            """
            UPDATE inventory.AverageCostLedger
            SET Status = 1, UnreadableReason = NULL, QuantityKg = 19, AverageCostPerKg = 13, Version = 3
            WHERE CompanyId = @company AND LocationId = @location AND ProductId = @product;
            """, ("@company", CompanyA), ("@location", second), ("@product", product));
        var mismatch = await CompanyWideReadAsync(run, CompanyA, product);
        Assert.Equal((InventoryAverageCostReadStatusV1.Unreadable, "uncosted_stock_on_hand", second),
            (mismatch.Status, mismatch.UnreadableCode, mismatch.UnreadableLocationId));

        // A location whose stock is gone is no cost source: it leaves the weight, whatever its ledger row says.
        await run.ExecuteAsync(
            "UPDATE inventory.Balances SET QuantityKg = 0 WHERE CompanyId = @company AND LocationId = @location AND ProductId = @product",
            ("@company", CompanyA), ("@location", second), ("@product", product));
        var remaining = await CompanyWideReadAsync(run, CompanyA, product);
        Assert.Equal((InventoryAverageCostReadStatusV1.Value, 10m, 10m), (remaining.Status, remaining.AverageCostPerKg, remaining.OnHandKg));
        Assert.Equal([first], remaining.Sources.Select(source => source.LocationId));

        // The read wrote nothing.
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.AverageCostLedger WHERE ProductId = @product", ("@product", product)));
    }

    [SqlServerFact]
    public async Task Average_cost_company_wide_weight_counts_only_company_owned_stock_the_company_holds()
    {
        await using var run = await Run.StartAsync();
        var first = await run.LocationOfAsync(CompanyA);
        var second = await CompanyWideLocationAsync(run, CompanyA, 1);
        var product = Guid.NewGuid();
        var supplier = Guid.NewGuid();
        var consignor = Guid.NewGuid();

        // KLN-D84: the company's own goods it holds weigh; the ledger covers exactly those 10 kg.
        await SeedCompanyWideStockAsync(run, CompanyA, first, product, CompanyOwner, CompanyA, FreeBlock, 10m);
        await InsertKnownAverageCostAsync(run, CompanyA, first, product, 10m, 10m);

        // The company's goods held at a fason supplier do not weigh: counted, they would make the 10 kg ledger a mismatch.
        await SeedCompanyWideHeldStockAsync(
            run, CompanyA, first, product, CompanyOwner, CompanyA, CounterpartyCustodian, supplier, FreeBlock, 7m);

        // A location holding only fason and consignment stock is no cost source: counted, it would be uncosted stock.
        await SeedCompanyWideHeldStockAsync(
            run, CompanyA, second, product, CompanyOwner, CompanyA, CounterpartyCustodian, supplier, FreeBlock, 5m);
        await SeedCompanyWideHeldStockAsync(
            run, CompanyA, second, product, CounterpartyOwner, consignor, CompanyCustodian, CompanyA, FreeBlock, 8m);

        var known = await CompanyWideReadAsync(run, CompanyA, product);
        Assert.Equal((InventoryAverageCostReadStatusV1.Value, 10m, null, 10m),
            (known.Status, known.AverageCostPerKg, known.UnreadableCode, known.OnHandKg));
        Assert.Equal([first], known.Sources.Select(source => source.LocationId));

        // KLN-D83: once the company holds its own uncosted goods at the second location the company-wide figure is
        // "hesaplanamadı" (no partial average over the readable location), naming that location.
        await SeedCompanyWideStockAsync(run, CompanyA, second, product, CompanyOwner, CompanyA, BlockedBlock, 3m);
        var unreadable = await CompanyWideReadAsync(run, CompanyA, product);
        Assert.Equal((InventoryAverageCostReadStatusV1.Unreadable, null, "uncosted_stock_on_hand", second, 13m),
            (unreadable.Status, unreadable.AverageCostPerKg, unreadable.UnreadableCode, unreadable.UnreadableLocationId,
                unreadable.OnHandKg));
    }

    [SqlServerFact]
    public async Task Average_cost_ledger_writer_stages_receipts_issues_and_transfers_in_the_callers_single_save()
    {
        await using var run = await Run.StartAsync();
        var first = await run.LocationOfAsync(CompanyA);
        var second = await CompanyWideLocationAsync(run, CompanyA, 1);
        var product = Guid.NewGuid();

        // Staged but never saved: nothing reaches the ledger.
        await using (var context = run.Context(CompanyA))
        {
            await LedgerWriter(context, CompanyA).StageAsync(first, product, CompanyWidePurchase(10m, 0m, 10m));
        }

        Assert.Equal(0, await LedgerRowCountAsync(run, product));

        // Two receipts in one unit of work: the second sees the first's staged row; one save stores version 1.
        await using (var context = run.Context(CompanyA))
        {
            var writer = LedgerWriter(context, CompanyA);
            await writer.StageAsync(first, product, CompanyWidePurchase(10m, 0m, 10m));
            var after = await writer.StageAsync(first, product, CompanyWidePurchase(10m, 10m, 12m));
            Assert.Equal(AverageCostPosition.Known(20m, 11m), after);
            Assert.Equal(0, await LedgerRowCountAsync(run, product));
            await context.SaveChangesAsync();
        }

        Assert.Equal((20m, 11m, 1L), await LedgerFiguresAsync(run, CompanyA, first, product));

        // An issue keeps the average; a receipt then blends: (15·11 + 25·15) / 40 = 13.5. One save, one version step.
        await using (var context = run.Context(CompanyA))
        {
            var writer = LedgerWriter(context, CompanyA);
            await writer.StageAsync(first, product, new AverageCostMovement(AverageCostMovementKind.Issue, 5m, 20m));
            await writer.StageAsync(first, product, CompanyWidePurchase(25m, 15m, 15m));
            await context.SaveChangesAsync();
        }

        Assert.Equal((40m, 13.5m, 2L), await LedgerFiguresAsync(run, CompanyA, first, product));

        // SAT-D260: the target receives the transfer at the source average.
        await using (var context = run.Context(CompanyA))
        {
            var result = await LedgerWriter(context, CompanyA).StageTransferAsync(first, 40m, second, 0m, product, 15m);
            Assert.Equal((AverageCostPosition.Known(25m, 13.5m), AverageCostPosition.Known(15m, 13.5m)), (result.Source, result.Target));
            await context.SaveChangesAsync();
        }

        Assert.Equal((25m, 13.5m, 3L), await LedgerFiguresAsync(run, CompanyA, first, product));
        Assert.Equal((15m, 13.5m, 1L), await LedgerFiguresAsync(run, CompanyA, second, product));

        // With the stock the ledger costed in place, the company-wide read gives the same figure.
        await SeedCompanyWideStockAsync(run, CompanyA, first, product, CompanyOwner, CompanyA, FreeBlock, 25m);
        await SeedCompanyWideStockAsync(run, CompanyA, second, product, CompanyOwner, CompanyA, FreeBlock, 15m);
        var read = await CompanyWideReadAsync(run, CompanyA, product);
        Assert.Equal((InventoryAverageCostReadStatusV1.Value, 13.5m, 40m), (read.Status, read.AverageCostPerKg, read.OnHandKg));

        // A production receipt makes the position unreadable (SAT-D258); a later receipt leaves it as it is, unrewritten.
        await using (var context = run.Context(CompanyA))
        {
            await LedgerWriter(context, CompanyA).StageAsync(
                second, product, new AverageCostMovement(AverageCostMovementKind.ProductionReceipt, 5m, 15m));
            await context.SaveChangesAsync();
        }

        await using (var context = run.Context(CompanyA))
        {
            var after = await LedgerWriter(context, CompanyA).StageAsync(second, product, CompanyWidePurchase(1m, 20m, 9m));
            Assert.Equal(AverageCostPosition.Unreadable(AverageCostUnreadableReason.ProductionSource), after);
            Assert.All(context.ChangeTracker.Entries<AverageCostLedgerRow>(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            await context.SaveChangesAsync();
        }

        Assert.Equal((2, (int?)6, 2L), await LedgerStateAsync(run, CompanyA, second, product));
        Assert.Equal(2, await LedgerRowCountAsync(run, product));
    }

    private const int CompanyOwner = 1;
    private const int CounterpartyOwner = 2;
    private const int FreeBlock = 1;
    private const int BlockedBlock = 2;
    private const int CompanyCustodian = 1;
    private const int CounterpartyCustodian = 2;

    private static InventoryAverageCostLedgerWriter LedgerWriter(InventoryDbContext context, Guid company)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(company);
        return new InventoryAverageCostLedgerWriter(context, companyContext, TimeProvider.System);
    }

    private static AverageCostMovement CompanyWidePurchase(decimal quantityKg, decimal stockBeforeKg, decimal unitCost) =>
        new(AverageCostMovementKind.PurchaseReceipt, quantityKg, stockBeforeKg, unitCost);

    private static async Task<InventoryCompanyWideAverageCostReadAnswerV1> CompanyWideReadAsync(Run run, Guid company, Guid product)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(company);
        await using var context = run.Context(company);
        return await new InventoryCompanyWideAverageCostReadPort(context, companyContext).ReadCompanyWideAsync(product);
    }

    private static async Task<Guid> CompanyWideLocationAsync(Run run, Guid company, int offset) =>
        await run.ScalarAsync<Guid>(
            "SELECT Id FROM organization.Locations WHERE CompanyId = @company ORDER BY Id OFFSET @offset ROWS FETCH NEXT 1 ROWS ONLY",
            ("@company", company), ("@offset", offset));

    /// <summary>One lot, label and balance of the product the company holds itself at the location (synthetic stock).</summary>
    private static Task SeedCompanyWideStockAsync(
        Run run, Guid company, Guid location, Guid product, int ownerKind, Guid owner, int block, decimal quantityKg) =>
        SeedCompanyWideHeldStockAsync(
            run, company, location, product, ownerKind, owner, CompanyCustodian, company, block, quantityKg);

    /// <summary>One lot, label and balance of the product at the location, written straight to SQL (synthetic stock).</summary>
    private static Task SeedCompanyWideHeldStockAsync(
        Run run, Guid company, Guid location, Guid product, int ownerKind, Guid owner, int custodianKind, Guid custodian,
        int block, decimal quantityKg) =>
        run.ExecuteAsync(
            """
            DECLARE @lot uniqueidentifier = NEWID(), @label uniqueidentifier = NEWID();
            INSERT INTO inventory.Lots (Id, CompanyId, ProductId, HeatNumber, CoilNumber, Version, CreatedAtUtc, CreatedBy)
            VALUES (@lot, @company, @product, NULL, NULL, 1, @at, N'synthetic-actor');

            INSERT INTO inventory.Labels
                (Id, CompanyId, Code, ProductId, LotId, RequiredDocumentsJson, Version, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@label, @company, CONCAT(N'SYN-CWAC-', REPLACE(CONVERT(nvarchar(36), @label), N'-', N'')), @product, @lot, N'[]', 1, @at, @at);

            INSERT INTO inventory.Balances
                (Id, CompanyId, OwnerKind, OwnerId, CustodianKind, CustodianId, LocationId, LabelId, ProductId, Block, QuantityKg)
            VALUES (NEWID(), @company, @ownerKind, @owner, @custodianKind, @custodian, @location, @label, @product, @block, @quantity);
            """,
            ("@company", company), ("@location", location), ("@product", product), ("@ownerKind", ownerKind),
            ("@owner", owner), ("@custodianKind", custodianKind), ("@custodian", custodian), ("@block", block),
            ("@quantity", quantityKg), ("@at", At));

    private static Task<int> LedgerRowCountAsync(Run run, Guid product) =>
        run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.AverageCostLedger WHERE ProductId = @product", ("@product", product));

    private static async Task<(decimal QuantityKg, decimal AverageCostPerKg, long Version)> LedgerFiguresAsync(
        Run run, Guid company, Guid location, Guid product)
    {
        await using var context = run.Context(company);
        var row = await context.AverageCostLedger.AsNoTracking()
            .SingleAsync(row => row.LocationId == location && row.ProductId == product);
        Assert.Equal((int)AverageCostStatus.Known, row.Status);
        return (row.QuantityKg!.Value, row.AverageCostPerKg!.Value, row.Version);
    }

    private static async Task<(int Status, int? UnreadableReason, long Version)> LedgerStateAsync(
        Run run, Guid company, Guid location, Guid product)
    {
        await using var context = run.Context(company);
        var row = await context.AverageCostLedger.AsNoTracking()
            .SingleAsync(row => row.LocationId == location && row.ProductId == product);
        return (row.Status, row.UnreadableReason, row.Version);
    }
}
