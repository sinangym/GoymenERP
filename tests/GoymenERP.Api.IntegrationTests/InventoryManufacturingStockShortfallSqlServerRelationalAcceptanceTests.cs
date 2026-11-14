using System.Data.Common;
using System.Globalization;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D318 (owner decision of 2026-11-13): no operation takes physical stock below zero, and the refusal says which
/// operation and where. On real SQL Server the input issue's physical-stock refusal names the refused issue, its label
/// with the product, the stored place of the label's stock and the kilograms its check saw, with the usable kilograms
/// the issue could have taken (the figure SINAN-CEVAP (8) question 5 discloses); the status and code stay the same and
/// nothing is written.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Manufacturing_input_issue_shortfall_names_the_issue_label_place_and_checked_kg_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();

        // Free stock smaller than the issue, with no reservation on it.
        var small = await SeedManufacturingParentAsync(run, "SYN-MFG-SHORTFALL-FREE", 10m);
        var overFree = IssueRequest(Guid.NewGuid(), small.Label.Id, "2", "10.001");
        AssertInputIssueShortfall(await IssueShortfallRefusalAsync(run, overFree, "shortfall-free"),
            overFree, small.Label, small.LocationId, availableKg: 10m, freePhysicalKg: 10m,
            reservedKg: 0m);
        await AssertNothingIssuedAsync(run, overFree, labelVersion: 2L);

        // A Sales reservation of 15 kg leaves 5 kg of a 20 kg Free label to the issue; every figure is reported.
        var reserved = await SeedManufacturingParentAsync(run, "SYN-MFG-SHORTFALL-RESERVED", 20m);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(
                new PhysicalSource(reserved.Label.Id, reserved.Label.ProductId, 2), "15"), Metadata(run));
        }

        var overReserved = IssueRequest(Guid.NewGuid(), reserved.Label.Id, "3", "5.001");
        AssertInputIssueShortfall(await IssueShortfallRefusalAsync(run, overReserved, "shortfall-reserved"),
            overReserved, reserved.Label, reserved.LocationId, availableKg: 5m,
            freePhysicalKg: 20m, reservedKg: 15m);
        await AssertNothingIssuedAsync(run, overReserved, labelVersion: 3L);

        // All of the label's stock is blocked: no Free kg, and the place is where its blocked stock lies.
        var blocked = await SeedManufacturingParentAsync(run, "SYN-MFG-SHORTFALL-BLOCKED", 10m);
        await BlockManufacturingParentAsync(run, blocked, 10m);
        var blockedVersion = await LabelVersionAsync(run, blocked.Label.Id);
        var overBlocked = IssueRequest(Guid.NewGuid(), blocked.Label.Id,
            blockedVersion.ToString(CultureInfo.InvariantCulture), "1");
        AssertInputIssueShortfall(await IssueShortfallRefusalAsync(run, overBlocked, "shortfall-blocked"),
            overBlocked, blocked.Label, blocked.LocationId, availableKg: 0m, freePhysicalKg: 0m,
            reservedKg: null);
        await AssertNothingIssuedAsync(run, overBlocked, blockedVersion);

        // A label already issued to zero holds no stock anywhere, so the refusal names no place.
        var emptied = await SeedManufacturingParentAsync(run, "SYN-MFG-SHORTFALL-EMPTY", 10m);
        var emptyingIssue = IssueRequest(Guid.NewGuid(), emptied.Label.Id, "2", "10");
        Assert.Equal("3", (await CommitIssueAsync(run, emptyingIssue,
            ManufacturingMetadata(run, "shortfall-emptying"))).ParentLabelVersion);
        var overEmpty = IssueRequest(Guid.NewGuid(), emptied.Label.Id, "3", "0.001");
        AssertInputIssueShortfall(await IssueShortfallRefusalAsync(run, overEmpty, "shortfall-empty"),
            overEmpty, emptied.Label, locationId: null, availableKg: 0m, freePhysicalKg: 0m,
            reservedKg: null);
        await AssertNothingIssuedAsync(run, overEmpty, labelVersion: 3L);
    }

    [SqlServerFact]
    public async Task Manufacturing_allocated_input_issue_shortfall_counts_only_other_demands_as_reserved_and_consumes_nothing()
    {
        await using var run = await Run.StartAsync();
        var parent = await SeedManufacturingParentAsync(run, "SYN-MFG-SHORTFALL-PLAN", 100m);
        var planId = Guid.NewGuid();
        var allocated = await CommitPlanAllocationAsync(run,
            PlanRequest(planId, CompanyLine(parent.Label.Id, "2", "60")),
            ManufacturingMetadata(run, "shortfall-plan-allocate"));
        var allocationId = allocated.Allocations.Single(item => item.LabelId == parent.Label.Id).AllocationId;
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(
                new PhysicalSource(parent.Label.Id, parent.Label.ProductId, 3), "40"), Metadata(run));
        }

        // A quality block takes 10 kg out of Free stock: 90 kg Free carry 60 kg for the plan and 40 kg for Sales.
        await BlockManufacturingParentAsync(run, parent, 10m);
        var version = await LabelVersionAsync(run, parent.Label.Id);
        var labelVersion = version.ToString(CultureInfo.InvariantCulture);

        // The plan's own 60 kg count as available to its issue; only Sales' 40 kg are reserved against it, so 50 kg
        // of the 90 kg Free are usable.
        var overAllocated = IssueRequest(Guid.NewGuid(), parent.Label.Id, labelVersion, "55")
            with { AllocationId = allocationId, PlanId = planId };
        AssertInputIssueShortfall(await IssueShortfallRefusalAsync(run, overAllocated, "shortfall-plan-over"),
            overAllocated, parent.Label, parent.LocationId, availableKg: 50m,
            freePhysicalKg: 90m, reservedKg: 40m);
        await AssertNothingIssuedAsync(run, overAllocated, version);

        // Without the allocation every active reservation of the position counts, here more than its Free kg: none
        // is usable.
        var unallocated = IssueRequest(Guid.NewGuid(), parent.Label.Id, labelVersion, "1");
        AssertInputIssueShortfall(await IssueShortfallRefusalAsync(run, unallocated, "shortfall-plan-unallocated"),
            unallocated, parent.Label, parent.LocationId, availableKg: 0m,
            freePhysicalKg: 90m, reservedKg: 100m);
        await AssertNothingIssuedAsync(run, unallocated, version);

        Assert.Equal((1, 60m), (
            await run.ScalarAsync<int>("SELECT Status FROM inventory.Allocations WHERE Id = @id", ("@id", allocationId)),
            await run.ScalarAsync<decimal>("SELECT RemainingKg FROM inventory.Allocations WHERE Id = @id",
                ("@id", allocationId))));
        Assert.Equal(90m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @label AND Block = 1",
            ("@label", parent.Label.Id)));
    }

    private static async Task<InventoryManufacturingRealizationException> IssueShortfallRefusalAsync(Run run,
        InventoryManufacturingInputIssueRequestV1 request, string operation)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, CompanyA, ManufacturingSettings(run));
        var error = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
            participant.StageInputIssueAsync(request, ManufacturingMetadata(run, operation)));
        await transaction.RollbackAsync();
        return error;
    }

    private static void AssertInputIssueShortfall(InventoryManufacturingRealizationException refusal,
        InventoryManufacturingInputIssueRequestV1 request, MaterialLabel label, Guid? locationId,
        decimal availableKg, decimal freePhysicalKg, decimal? reservedKg)
    {
        Assert.Equal((409, InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient),
            (refusal.Status, refusal.Code));
        var shortfall = refusal.StockShortfall;
        Assert.NotNull(shortfall);
        Assert.Equal(StockMovementKind.Issue, shortfall.MovementKind);
        Assert.Equal(InventoryManufacturingRealizationContractV1.InputIssueSourceKind, shortfall.SourceKind);
        Assert.Equal((request.ManufacturingOrderId, (Guid?)request.IssueId),
            (shortfall.SourceId, shortfall.SourceLineId));
        Assert.Equal((label.Id, label.ProductId, locationId),
            (shortfall.LabelId, shortfall.ProductId, shortfall.LocationId));
        Assert.Equal(decimal.Parse(request.QuantityKg, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
            shortfall.RequestedKg.Value);
        Assert.Equal((availableKg, freePhysicalKg, reservedKg),
            (shortfall.AvailableKg.Value, shortfall.FreePhysicalKg.Value, shortfall.ReservedKg?.Value));
    }

    /// <summary>The refusal is not recorded (a later slice): no movement, no command and an unchanged label.</summary>
    private static async Task AssertNothingIssuedAsync(Run run, InventoryManufacturingInputIssueRequestV1 request,
        long labelVersion)
    {
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 2 AND SourceId = @order",
            ("@order", request.ManufacturingOrderId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", request.IssueId)));
        Assert.Equal(labelVersion, await LabelVersionAsync(run, request.ParentLabelId));
    }

    private static async Task BlockManufacturingParentAsync(Run run, ManufacturingParentSeed parent, decimal kilograms)
    {
        var free = Position(CompanyA, parent.LocationId, parent.Label, BlockState.Free);
        await using var scope = run.Store(CompanyA);
        Assert.Equal(InventoryWriteOutcome.Recorded, await scope.Movements.RecordMovementAsync(new StockMovement(
            Guid.NewGuid(), CompanyA, StockMovementKind.Block, ManufacturingAt,
            new StockSourceReference("synthetic.manufacturing-block", Guid.NewGuid()),
            [Out(free, kilograms), In(free.WithBlock(BlockState.Blocked), kilograms)]), Metadata(run)));
    }
}
