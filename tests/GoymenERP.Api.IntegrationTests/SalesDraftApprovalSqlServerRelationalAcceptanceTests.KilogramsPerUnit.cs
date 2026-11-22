using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D437 (AK-4) on real SQL: a pinned theoretical kg/unit row is a tenant child of its order line, held by its value check, and
/// protected by the forward-fix Down (52051); an empty store rolls back freely. SENTETIK.
/// </summary>
public sealed partial class SalesDraftApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task SAT_D437_the_piece_kilograms_migrations_Down_passes_with_an_empty_store_and_refuses_while_a_pinned_figure_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddSalesPieceLineKilograms().DownOperations[0]).Sql;
        SalesOrderDraft draft;
        await using (var scope = run.Store(CompanyA))
        {
            draft = (await scope.Store.StoreDraftAsync(run.Envelope("kpu-create"), Hash("kpu-create"),
                number => Draft(Owners.For(CompanyA), number, T0))).Draft;
        }

        // Only kg drafts in the store: the guard passes.
        await run.ExecuteAsync(guard);

        var line = draft.Lines[0];
        const string insert = "INSERT INTO sales.DraftLineKilogramsPerUnit (CompanyId, DraftId, LineId, Revision, [LineNo], ProductId, UnitId, "
            + "KilogramsPerUnit, SourceProof, RecordedAtUtc) VALUES (@company, @draft, @line, 0, 1, @product, @unit, @value, N'{}', SYSDATETIMEOFFSET())";
        // A non-positive figure is refused by the table's own check.
        var zero = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(insert, ("@company", CompanyA), ("@draft", draft.Id),
            ("@line", line.Id), ("@product", line.Product.ProductId), ("@unit", line.OrderUnit.UnitId), ("@value", 0m)));
        Assert.Equal(547, zero.Number);
        await run.ExecuteAsync(insert, ("@company", CompanyA), ("@draft", draft.Id), ("@line", line.Id),
            ("@product", line.Product.ProductId), ("@unit", line.OrderUnit.UnitId), ("@value", 12.345m));

        // Forward fix: the Down guard refuses while a pinned figure exists and leaves it in place.
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(52051, refused.Number);
        Assert.Equal(12.345m, await run.ScalarAsync<decimal>(
            "SELECT KilogramsPerUnit FROM sales.DraftLineKilogramsPerUnit WHERE DraftId = @id", ("@id", draft.Id)));
    }
}
