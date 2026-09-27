using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ST-05 / AT-037 (DEC-STK-007) over real SQL: a count with its reason changes no stock; its difference is final
/// only after another actor approves it (fail-closed maker≠checker while the critical-difference policy is
/// undecided, INV-R20-R21-01 Q2); the approval posts exactly one CountAdjustment movement, which is the balance
/// ST-01, the label detail and a new allocation see next; replays have no second effect; history is never deleted.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Stock_count_difference_is_pending_until_another_actor_approves_and_ST01_and_allocation_see_the_counted_kg()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-SHORT", 100m);
        var counter = Metadata(run) with { IdempotencyKey = $"count-{Guid.NewGuid():N}", CorrelationId = $"count-{Guid.NewGuid():N}" };
        var request = new RecordInventoryStockCountV1(Guid.NewGuid(), source.LabelId, source.LabelVersion.ToString(), "97.5",
            At.AddHours(2), "Sentetik sayım: rulo tartımı eksik çıktı.");

        await using (var scope = run.Store(CompanyA))
        {
            var recorded = await scope.Store.RecordStockCountAsync(request, counter);
            Assert.False(recorded.IsReplay);
            Assert.Equal((InventoryStockCountStatusesV1.PendingApproval, "100", "97.5", "-2.5", source.LabelVersion.ToString()),
                (recorded.Count.Status, recorded.Count.BookKg, recorded.Count.CountedKg, recorded.Count.DifferenceKg,
                    recorded.Count.LabelVersion));
        }

        // A count is evidence, not an effect: no movement, the same balance and label version.
        Assert.Equal(100m, await LabelKgAsync(run, source.LabelId));
        Assert.Equal(source.LabelVersion, await LabelVersionAsync(run, source.LabelId));
        Assert.Equal(0, await CountAdjustmentsAsync(run));

        await using (var scope = run.Store(CompanyA))
        {
            var sameKey = await scope.Store.RecordStockCountAsync(request, counter with { CorrelationId = $"retry-{Guid.NewGuid():N}" });
            Assert.True(sameKey.IsReplay);
            Assert.Equal(counter.CorrelationId, sameKey.OriginalCorrelationId);
            var newKey = await scope.Store.RecordStockCountAsync(request, counter with { IdempotencyKey = $"other-{Guid.NewGuid():N}" });
            Assert.True(newKey.IsReplay);
            var reopened = (await scope.Store.ReadStockCountsAsync(source.LabelId))!;
            var only = Assert.Single(reopened.Items);
            Assert.Equal((request.CountId, InventoryStockCountStatusesV1.PendingApproval, request.Reason),
                (only.CountId, only.Status, only.Reason));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var self = await Assert.ThrowsAsync<InventoryStockCountException>(() => scope.Store.DecideStockCountAsync(
                request.CountId, new(InventoryStockCountDecisionKindV1.Approve, "Kendi sayımım"),
                counter with { IdempotencyKey = $"self-{Guid.NewGuid():N}" }));
            Assert.Equal((409, InventoryStockCountCodesV1.SelfApproval), (self.Status, self.Code));
        }

        Assert.Equal(100m, await LabelKgAsync(run, source.LabelId));
        var approver = Metadata(run) with
        {
            ActorId = Guid.NewGuid().ToString("D"),
            IdempotencyKey = $"approve-{Guid.NewGuid():N}",
            CorrelationId = $"approve-{Guid.NewGuid():N}",
        };
        var approval = new DecideInventoryStockCountV1(InventoryStockCountDecisionKindV1.Approve, "Tartım fişiyle doğrulandı.");
        Guid movementId;
        await using (var scope = run.Store(CompanyA))
        {
            var approved = await scope.Store.DecideStockCountAsync(request.CountId, approval, approver);
            Assert.False(approved.IsReplay);
            Assert.Equal(InventoryStockCountStatusesV1.Approved, approved.Count.Status);
            movementId = approved.Count.Decision!.AdjustmentMovementId!.Value;
        }

        Assert.Equal(97.5m, await LabelKgAsync(run, source.LabelId));
        Assert.Equal(source.LabelVersion + 1, await LabelVersionAsync(run, source.LabelId));
        Assert.Equal(1, await CountAdjustmentsAsync(run));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements m JOIN inventory.MovementLines l ON l.MovementId = m.Id "
            + "WHERE m.Id = @id AND m.Kind = 6 AND m.SourceKind = N'inventory.stock-count' AND m.SourceId = @count "
            + "AND l.Direction = 2 AND l.QuantityKg = 2.5 AND l.LabelId = @label",
            ("@id", movementId), ("@count", request.CountId), ("@label", source.LabelId)));

        await using (var scope = run.Store(CompanyA))
        {
            var sameKey = await scope.Store.DecideStockCountAsync(request.CountId, approval,
                approver with { CorrelationId = $"retry-{Guid.NewGuid():N}" });
            Assert.True(sameKey.IsReplay);
            Assert.Equal(approver.CorrelationId, sameKey.OriginalCorrelationId);
            var newKey = await scope.Store.DecideStockCountAsync(request.CountId, approval,
                approver with { IdempotencyKey = $"approve-again-{Guid.NewGuid():N}" });
            Assert.True(newKey.IsReplay);
            Assert.Equal(movementId, newKey.Count.Decision!.AdjustmentMovementId);
            var other = await Assert.ThrowsAsync<InventoryStockCountException>(() => scope.Store.DecideStockCountAsync(
                request.CountId, new(InventoryStockCountDecisionKindV1.Reject, "Vazgeçildi"),
                approver with { IdempotencyKey = $"reject-{Guid.NewGuid():N}" }));
            Assert.Equal((409, InventoryStockCountCodesV1.AlreadyDecided), (other.Status, other.Code));
        }

        // No double effect, and the history is intact.
        Assert.Equal(97.5m, await LabelKgAsync(run, source.LabelId));
        Assert.Equal(1, await CountAdjustmentsAsync(run));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockCounts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockCountDecisions"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockCount' AND SourceChannel = N'Web'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockMovement' AND EntityId = @id",
            ("@id", movementId.ToString("D"))));

        // Reopen: the count shows its decision and ST-01, the label detail and allocation all read the counted kg.
        await using (var scope = run.Store(CompanyA))
        {
            var reopened = Assert.Single((await scope.Store.ReadStockCountsAsync(source.LabelId))!.Items);
            Assert.Equal((InventoryStockCountStatusesV1.Approved, "Tartım fişiyle doğrulandı.", (Guid?)movementId),
                (reopened.Status, reopened.Decision!.Reason, reopened.Decision.AdjustmentMovementId));

            var detail = await LabelDetails(scope.DbContext, CompanyA, scope.Store).GetAsync(source.LabelId);
            Assert.Equal("97.5", detail.Balances.PhysicalKg);
            Assert.Contains(detail.MovementTimeline.Entries, entry => entry.MovementId == movementId
                && entry.Kind == nameof(StockMovementKind.CountAdjustment));
        }

        await using (var context = run.Context(CompanyA))
        {
            var position = Assert.Single((await StockPositions(context, CompanyA).SearchAsync(new("SYN-COUNT-SHORT", 25))).Items);
            Assert.Equal(("97.5", "97.5", (source.LabelVersion + 1).ToString()),
                (position.PhysicalKg, position.AvailableKg, position.LabelVersion));
        }

        var adjusted = source with { LabelVersion = source.LabelVersion + 1 };
        await using (var scope = run.Store(CompanyA))
        {
            var tooMuch = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(adjusted, "97.501"), Metadata(run)));
            Assert.Equal(409, tooMuch.Status);
        }

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(adjusted, "97.5"), Metadata(run));
        }
    }

    [SqlServerFact]
    public async Task Stock_count_refuses_stale_foreign_positionless_self_and_over_allocated_counts_without_an_effect()
    {
        await using var run = await Run.StartAsync();
        var own = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-OWN", 100m);
        var foreign = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-COUNT-FOREIGN", 100m);
        var approver = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };

        await using (var scope = run.Store(CompanyA))
        {
            await AssertCountRefused(scope.Store.RecordStockCountAsync(Count(own.LabelId, own.LabelVersion + 1, "90"), Metadata(run)),
                409, InventoryStockCountCodesV1.LabelVersionConflict);
            await AssertCountRefused(scope.Store.RecordStockCountAsync(Count(foreign.LabelId, foreign.LabelVersion, "90"), Metadata(run)),
                404, InventoryStockCountCodesV1.LabelNotFound);
            await AssertCountRefused(scope.Store.RecordStockCountAsync(Count(own.LabelId, own.LabelVersion, "90.0001"), Metadata(run)),
                400, InventoryStockCountCodesV1.ValidationFailed);
            await AssertCountRefused(scope.Store.RecordStockCountAsync(
                    Count(own.LabelId, own.LabelVersion, "90") with { Reason = "  " }, Metadata(run)),
                400, InventoryStockCountCodesV1.ValidationFailed);
            Assert.Null(await scope.Store.ReadStockCountsAsync(foreign.LabelId));
        }

        // A label without stock has no book to count against.
        var empty = NewLabel(CompanyA, "SYN-COUNT-EMPTY");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Labels.SaveLabelAsync(empty, Metadata(run));
        }

        await using (var scope = run.Store(CompanyA))
        {
            await AssertCountRefused(scope.Store.RecordStockCountAsync(Count(empty.Id, 1, "5"), Metadata(run)),
                409, InventoryStockCountCodesV1.PositionConflict);
        }

        // One key, other content.
        var key = Metadata(run) with { IdempotencyKey = $"count-key-{Guid.NewGuid():N}" };
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockCountAsync(Count(own.LabelId, own.LabelVersion, "100"), key);
            await AssertCountRefused(scope.Store.RecordStockCountAsync(Count(own.LabelId, own.LabelVersion, "99"), key),
                409, InventoryStockCountCodesV1.IdempotencyConflict);
            var zero = Assert.Single((await scope.Store.ReadStockCountsAsync(own.LabelId))!.Items);
            Assert.Equal((InventoryStockCountStatusesV1.NoDifference, "0"), (zero.Status, zero.DifferenceKg));
            await AssertCountRefused(scope.Store.DecideStockCountAsync(zero.CountId,
                    new(InventoryStockCountDecisionKindV1.Approve, "Fark yok"), approver),
                409, InventoryStockCountCodesV1.NotPending);
            await AssertCountRefused(scope.Store.DecideStockCountAsync(Guid.NewGuid(),
                    new(InventoryStockCountDecisionKindV1.Approve, "Yok"), approver),
                404, InventoryStockCountCodesV1.CountNotFound);
        }

        // Stale: the label changed after the count, so the book the counter saw is gone. Rejecting still closes it.
        var stale = Count(own.LabelId, own.LabelVersion, "95");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockCountAsync(stale, Metadata(run));
        }

        await TouchLabelAsync(run, own.LabelId);
        await using (var scope = run.Store(CompanyA))
        {
            await AssertCountRefused(scope.Store.DecideStockCountAsync(stale.CountId,
                    new(InventoryStockCountDecisionKindV1.Approve, "Onay"), approver with { IdempotencyKey = Guid.NewGuid().ToString("N") }),
                409, InventoryStockCountCodesV1.Stale);
            var rejected = await scope.Store.DecideStockCountAsync(stale.CountId,
                new(InventoryStockCountDecisionKindV1.Reject, "Sayım sonrası hareket oldu; yeniden sayılacak."),
                approver with { IdempotencyKey = Guid.NewGuid().ToString("N") });
            Assert.Equal((InventoryStockCountStatusesV1.Rejected, (Guid?)null),
                (rejected.Count.Status, rejected.Count.Decision!.AdjustmentMovementId));
        }

        // A shortage below the active allocations is refused; the reservation is never silently broken.
        var version = await LabelVersionAsync(run, own.LabelId);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(own with { LabelVersion = version }, "50"), Metadata(run));
        }

        version = await LabelVersionAsync(run, own.LabelId);
        var belowAllocation = Count(own.LabelId, version, "40");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockCountAsync(belowAllocation, Metadata(run));
            await AssertCountRefused(scope.Store.DecideStockCountAsync(belowAllocation.CountId,
                    new(InventoryStockCountDecisionKindV1.Approve, "Onay"), approver with { IdempotencyKey = Guid.NewGuid().ToString("N") }),
                409, InventoryStockCountCodesV1.AllocationConflict);
        }

        Assert.Equal(100m, await LabelKgAsync(run, own.LabelId));
        Assert.Equal(0, await CountAdjustmentsAsync(run));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockCounts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockCountDecisions WHERE Decision = 2"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockCountDecisions WHERE Decision = 1"));
    }

    [SqlServerFact]
    public async Task Stock_count_migration_rolls_back_when_empty_and_blocks_after_a_count()
    {
        const string previousMigration = "20260924193000_AddInventoryManufacturingReturn";
        const string currentMigration = "20260927150554_AddInventoryStockCounts";
        await using var run = await Run.StartAsync();
        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'inventory') AND name LIKE N'StockCount%'"));
            await context.GetService<IMigrator>().MigrateAsync(currentMigration);
        }

        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-DOWN", 10m);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockCountAsync(Count(source.LabelId, source.LabelVersion, "9"), Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            var rejected = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(previousMigration));
            Assert.Equal(51053, rejected.Number);
            Assert.Equal(currentMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockCounts"));
    }

    private static RecordInventoryStockCountV1 Count(Guid labelId, long version, string countedKg) =>
        new(Guid.NewGuid(), labelId, version.ToString(), countedKg, At.AddHours(1), "Sentetik sayım farkı.");

    private static async Task AssertCountRefused(Task task, int status, string code)
    {
        var exception = await Assert.ThrowsAsync<InventoryStockCountException>(() => task);
        Assert.Equal((status, code), (exception.Status, exception.Code));
    }

    private static async Task<decimal> LabelKgAsync(Run run, Guid labelId) => await run.ScalarAsync<decimal>(
        "SELECT COALESCE(SUM(QuantityKg), 0) FROM inventory.Balances WHERE LabelId = @id", ("@id", labelId));

    private static async Task<long> LabelVersionAsync(Run run, Guid labelId) => await run.ScalarAsync<long>(
        "SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", labelId));

    private static async Task<int> CountAdjustmentsAsync(Run run) =>
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 6");
}
