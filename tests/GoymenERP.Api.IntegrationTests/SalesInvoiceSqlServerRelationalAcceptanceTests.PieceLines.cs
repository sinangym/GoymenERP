using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D434 / SAT-D435 (AK-4) on real SQL: a piece invoice line stores its theoretical and actual kg in the one save and reads them
/// back; CK_InvoiceLines_KilogramInformation keeps them both-or-neither and positive; TR_InvoiceLines_Guard keeps them immutable
/// (51224); AddSalesPieceLineKilograms' forward-fix Down refuses (52051) while any kg information exists and rolls back freely while
/// none does. Golden K02 figures (40 adet × 250,00; 493,800 theoretical, 500,160 scale kg). SENTETIK.
/// </summary>
public sealed partial class SalesInvoiceSqlServerRelationalAcceptanceTests
{
    private static readonly Guid PieceUnit = Guid.Parse("5e0cbe45-61cc-541a-834a-366355de36e5");

    [SqlServerFact]
    public async Task SAT_D434_a_piece_invoice_line_keeps_its_kilogram_information_both_or_neither_positive_and_immutable_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var invoiceId = Guid.NewGuid();
        SalesInvoiceStoreResult stored;
        await using (var scope = run.Store(CompanyA))
        {
            stored = await scope.Invoices.StoreInvoiceAsync(run.Envelope("i-piece"), Hash('1'),
                number => PieceInvoice(invoiceId, number, run.Actor, Guid.NewGuid()));
        }

        Assert.Equal(SalesDraftWriteOutcome.Recorded, stored.Outcome);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceLines WHERE InvoiceId = @id AND UnitId = @unit AND Quantity = 40 "
            + "AND TheoreticalKg = 493.800 AND ActualKg = 500.160 AND NetAmount = 10000.00", ("@id", invoiceId), ("@unit", PieceUnit)));
        await using (var scope = run.Store(CompanyA))
        {
            var reloaded = await scope.Invoices.FindInvoiceAsync(invoiceId);
            var share = Assert.Single(reloaded!.Lines).Share;
            Assert.Equal((40m, 493.8m, 500.16m), (share.Quantity, share.TheoreticalKilograms, share.ActualKilograms));
            var replay = await scope.Invoices.FindInvoiceReplayAsync(run.Envelope("i-piece", "SENTETIK-again"), Hash('1'));
            Assert.Equal((493.8m, 500.16m), (replay!.Invoice.Lines[0].Share.TheoreticalKilograms, replay.Invoice.Lines[0].Share.ActualKilograms));
        }

        // CK_InvoiceLines_KilogramInformation runs before the AFTER trigger: half the information or a non-positive figure is refused.
        // (Each half is IS NOT NULL-guarded: "x > 0 AND NULL > 0" is UNKNOWN, which a CHECK lets through.)
        foreach (var sql in new[]
        {
            "UPDATE sales.InvoiceLines SET ActualKg = NULL WHERE InvoiceId = @id",
            "UPDATE sales.InvoiceLines SET TheoreticalKg = NULL WHERE InvoiceId = @id",
            "UPDATE sales.InvoiceLines SET ActualKg = 0 WHERE InvoiceId = @id",
            "UPDATE sales.InvoiceLines SET TheoreticalKg = -1 WHERE InvoiceId = @id",
        })
        {
            var check = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(sql, ("@id", invoiceId)));
            Assert.Equal(547, check.Number);
            Assert.Contains("CK_InvoiceLines_KilogramInformation", check.Message, StringComparison.Ordinal);
        }

        // TR_InvoiceLines_Guard: the kg information is as immutable as the rest of the line, cleared or changed.
        foreach (var sql in new[]
        {
            "UPDATE sales.InvoiceLines SET TheoreticalKg = 493.801 WHERE InvoiceId = @id",
            "UPDATE sales.InvoiceLines SET ActualKg = 748.900 WHERE InvoiceId = @id",
            "UPDATE sales.InvoiceLines SET TheoreticalKg = NULL, ActualKg = NULL WHERE InvoiceId = @id",
        })
        {
            var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(sql, ("@id", invoiceId)));
            Assert.Equal(51224, guard.Number);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceLines WHERE InvoiceId = @id AND TheoreticalKg = 493.800 AND ActualKg = 500.160",
            ("@id", invoiceId)));

        // A kg line carries neither: giving it half (or all) of the information is refused by the check and the guard alike.
        var kgInvoice = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.StoreInvoiceAsync(run.Envelope("i-kg"), Hash('2'),
                number => Invoice(kgInvoice, number, run.Actor, Guid.NewGuid(), Guid.NewGuid()));
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceLines WHERE InvoiceId = @id AND TheoreticalKg IS NULL AND ActualKg IS NULL", ("@id", kgInvoice)));
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE sales.InvoiceLines SET TheoreticalKg = 1 WHERE InvoiceId = @id", ("@id", kgInvoice)))).Number);
        Assert.Equal(51224, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE sales.InvoiceLines SET TheoreticalKg = 1, ActualKg = 1 WHERE InvoiceId = @id", ("@id", kgInvoice)))).Number);
    }

    [SqlServerFact]
    public async Task SAT_D434_the_piece_kilograms_migrations_Down_rolls_back_without_kg_information_and_refuses_52051_while_a_piece_line_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddSalesPieceLineKilograms().DownOperations[0]).Sql;
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.StoreInvoiceAsync(run.Envelope("i-down-kg"), Hash('3'),
                number => Invoice(Guid.NewGuid(), number, run.Actor, Guid.NewGuid(), Guid.NewGuid()));
        }

        // Only kg lines (no kg information) and no pinned figure: the guard passes and the migration rolls back and forward again.
        await run.ExecuteAsync(guard);
        string[] applied;
        await using (var scope = run.Store(CompanyA))
        {
            applied = [.. await scope.Context.Database.GetAppliedMigrationsAsync()];
            var index = Array.FindIndex(applied, id => id.EndsWith("_AddSalesPieceLineKilograms", StringComparison.Ordinal));
            Assert.Equal(applied.Length - 1, index);
            await scope.Context.GetService<IMigrator>().MigrateAsync(applied[index - 1]);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'sales.InvoiceLines') AND name IN (N'TheoreticalKg', N'ActualKg')"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = N'DraftLineKilogramsPerUnit'"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'sales.TR_InvoiceLines_Guard') AND definition LIKE N'%TheoreticalKg%'"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceLines"));

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Context.Database.MigrateAsync();
            Assert.Equal(applied, await scope.Context.Database.GetAppliedMigrationsAsync());
        }

        // A piece line with kg information: the guard refuses and the rollback leaves the schema and the figures in place.
        var pieceInvoice = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.StoreInvoiceAsync(run.Envelope("i-down-piece"), Hash('4'),
                number => PieceInvoice(pieceInvoice, number, run.Actor, Guid.NewGuid()));
        }

        Assert.Equal(52051, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        await using (var scope = run.Store(CompanyA))
        {
            var rollback = await Assert.ThrowsAsync<SqlException>(() =>
                scope.Context.GetService<IMigrator>().MigrateAsync(applied[^2]));
            Assert.Equal(52051, rollback.Number);
            Assert.Equal(applied, await scope.Context.Database.GetAppliedMigrationsAsync());
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceLines WHERE InvoiceId = @id AND TheoreticalKg = 493.800 AND ActualKg = 500.160",
            ("@id", pieceInvoice)));
    }

    /// <summary>Golden K02: one piece line, 40 adet × 250,00 TL, theoretical 493,800 kg and scale 500,160 kg (information only).</summary>
    private static SalesInvoice PieceInvoice(Guid id, string number, string actor, Guid shipment)
    {
        var tax = new SalesLineTaxSnapshot(Product, Day, "Normal", "Steel", "Resolved", null, "Record", Guid.NewGuid(), 1, "Resolved", 20m,
            "NotRequired", null, null, "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);
        var share = new SalesInvoiceShareInput(Guid.NewGuid(), shipment, Guid.NewGuid(), "IRS-2026-000001", 1, Day, Guid.NewGuid(),
            Guid.NewGuid(), 0, Product, PieceUnit, 40m, "40", new CommercialDraftFields("250.00", PieceUnit, null), tax, 493.8m, 500.16m);
        var terms = SalesDueTerms.Create([new SalesDueInstalment(1, SalesDueBasis.OrderDate, 30, "100")]);
        return SalesInvoice.Create(id, CompanyA, number, new SalesInvoiceHeader(Guid.NewGuid(), Guid.NewGuid(), Try, Guid.NewGuid(), "1", 2, Day, null),
            [share], SalesDueDateCalculator.Compute(terms, Day, Day).DueDates, terms, actor, DateTimeOffset.UtcNow);
    }
}
