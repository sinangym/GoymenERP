using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using InventoryKg = GoymenERP.Modules.Inventory.Domain.Quantities.Kilograms;
using InventoryPieces = GoymenERP.Modules.Inventory.Domain.Quantities.PieceCount;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Counted_input_and_return_keep_both_balances_replay_and_roll_back_missing_counts()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var location = await InventoryHostLocationOfAsync(run, CompanyA);
        var label = InventoryHostLabel(CompanyA, "SYN-MFG-COUNTED-INPUT");
        await using (var stock = InventoryHostStore(run, CompanyA))
        {
            await stock.Labels.SaveLabelAsync(label, InventoryHostMetadata(run));
            await stock.Movements.RecordMovementAsync(InventoryHostMovement(CompanyA, StockMovementKind.Receipt,
                new StockSourceReference("synthetic.manufacturing-counted-input", Guid.NewGuid()),
                new StockMovementLine(InventoryHostPosition(CompanyA, location, label, BlockState.Free),
                    StockDirection.In, InventoryKg.Positive(100m), InventoryPieces.Positive(10))), InventoryHostMetadata(run));
        }

        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        await using var scope = ManufacturingHostScope.Create(run, CompanyA, authority);
        var issued = await SeedManufacturingOrderAsync(run, scope.Store, label.Id, 2, 60m);
        var missingIssue = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
            scope.Host.IssueAsync(issued, ManufacturingHostMetadata(run, "missing-input-count")));
        Assert.Equal((400, InventoryManufacturingRealizationCodesV1.InvalidQuantity),
            (missingIssue.Status, missingIssue.Code));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id",
            ("@id", issued.IssueId)));

        issued = issued with { PieceCount = 6 };
        var issueResult = await scope.Host.IssueAsync(issued, ManufacturingHostMetadata(run, "counted-input"));
        Assert.False(issueResult.IsReplay);
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT PieceCount FROM inventory.Balances WHERE LabelId = @id AND QuantityKg = 40", ("@id", label.Id)));
        Assert.True((await scope.Host.IssueAsync(issued, ManufacturingHostMetadata(run, "counted-input-replay"))).IsReplay);
        var changedIssue = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
            scope.Host.IssueAsync(issued with { PieceCount = 5 }, ManufacturingHostMetadata(run, "changed-input-count")));
        Assert.Equal((409, InventoryManufacturingRealizationCodesV1.IdempotencyConflict),
            (changedIssue.Status, changedIssue.Code));

        await using var returns = ReturnOutputHostScope.Create(run, authority, new ManufacturingOutputAdmission());
        var returned = new ManufacturingInventoryInputReturnCommand(issued.OrderId, issueResult.OrderVersion,
            Guid.NewGuid(), label.Id, issueResult.ParentLabelVersion, Guid.NewGuid(), new Kilograms(20m),
            issued.OccurredAtUtc.AddMinutes(1), issued.MovementId);
        var missingReturn = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
            returns.Host.ReturnAsync(returned, ManufacturingHostMetadata(run, "missing-return-count")));
        Assert.Equal((400, InventoryManufacturingRealizationCodesV1.InvalidQuantity),
            (missingReturn.Status, missingReturn.Code));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id",
            ("@id", returned.ReturnId)));

        returned = returned with { PieceCount = 2 };
        Assert.False((await returns.Host.ReturnAsync(returned, ManufacturingHostMetadata(run, "counted-return"))).IsReplay);
        Assert.True((await returns.Host.ReturnAsync(returned, ManufacturingHostMetadata(run, "counted-return-replay"))).IsReplay);
        Assert.Equal(6, await run.ScalarAsync<int>(
            "SELECT PieceCount FROM inventory.Balances WHERE LabelId = @id AND QuantityKg = 60", ("@id", label.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.MovementLines WHERE MovementId = @id AND PieceCount = 6",
            ("@id", issued.MovementId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.MovementLines WHERE MovementId = @id AND PieceCount = 2",
            ("@id", returned.MovementId)));
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Orders WHERE Id = @id",
            ("@id", issued.OrderId)));
    }
}
