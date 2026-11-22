using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Quantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Purchasing.Domain.Quantities.PurchasingQuantityRefusals>;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AK-3 (SAT-D437, ALS-D119) on real SQL Server: the theoretical kg/piece snapshot is only accepted on a piece general line with
/// its source version, and AddPurchasePieceKilogramFacts' Down refuses (52041) while a snapshot exists. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid PieceUnit = Guid.Parse("5e0cbe45-61cc-541a-834a-366355de36e5");

    [SqlServerFact]
    public async Task The_piece_kilogram_migrations_Down_passes_with_only_kilogram_lines_and_refuses_while_a_snapshot_exists()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        string guardSql = Assert.IsType<SqlOperation>(new AddPurchasePieceKilogramFacts().DownOperations[0]).Sql;
        Guid kgOrderId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder kg = Order(owners, null, null, 5m);
            kgOrderId = (await a.Store.StoreOrderAsync(run.Envelope("k-piece-kg"), Hash("piece-kg"), owners.Account, Day, () => kg, VersionsOf(kg)))
                .Order.Order.Id;
        }

        await run.ExecuteAsync(guardSql);

        // The check keeps a snapshot off a kilogram line and refuses a figure without its source version.
        var onKg = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseOrderLines SET TheoreticalKilogramsPerUnit = 12.345, TheoreticalKilogramsSourceVersion = N'SENTETIK' "
            + "WHERE OrderId = @id", ("@id", kgOrderId)));
        Assert.Equal(547, onKg.Number);
        Assert.Contains("CK_PurchaseOrderLines_TheoreticalKilograms", onKg.Message, StringComparison.Ordinal);

        Guid pieceOrderId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder piece = PurchaseOrder.Create(Guid.NewGuid(), owners.Company, owners.Party, T0);
            piece.AddLine(piece.Version, Guid.NewGuid(), Product, PieceUnit, Quantity.Of(100m, QuantityUnit.Piece), null, T0);
            pieceOrderId = (await a.Store.StoreOrderAsync(run.Envelope("k-piece"), Hash("piece"), owners.Account, Day, () => piece,
                VersionsOf(piece))).Order.Order.Id;
        }

        var partial = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseOrderLines SET TheoreticalKilogramsPerUnit = 12.345 WHERE OrderId = @id", ("@id", pieceOrderId)));
        Assert.Equal(547, partial.Number);

        await run.ExecuteAsync(
            "UPDATE purchasing.PurchaseOrderLines SET TheoreticalKilogramsPerUnit = 12.345, TheoreticalKilogramsSourceVersion = N'SENTETIK' "
            + "WHERE OrderId = @id", ("@id", pieceOrderId));
        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guardSql));
        Assert.Equal(52041, guard.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseOrderLines WHERE TheoreticalKilogramsPerUnit IS NOT NULL"));
    }
}
