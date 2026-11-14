using GoymenERP.Modules.Inventory.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-22a actual SQL: dispatch moves stock only against the exact current effective Logistics outgoing-document binding,
/// stores the proved document version, and records a terminal outgoing-document refusal once per key without a stock
/// effect. The migration Down refuses (THROW 51272) once a proved dispatch exists.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Transfer_dispatch_stores_the_proved_document_version_and_refuses_a_reordered_binding_without_stock()
    {
        await using var run = await Run.StartAsync();
        var first = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-PROOF-1", 3m);
        var second = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-PROOF-2", 4m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var firstLine = Guid.NewGuid();
        var secondLine = Guid.NewGuid();
        var plan = new PlanInventoryTransferV1(Guid.NewGuid(), sourceLocation, "Company", CompanyA,
            destinationLocation, "Company", CompanyA, At,
            [
                new(firstLine, first.LabelId, first.LabelVersion.ToString(), "3"),
                new(secondLine, second.LabelId, second.LabelVersion.ToString(), "4"),
            ]);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(plan, Metadata(run), Authorized);
        }

        var request = TransferDispatch(documentVersion: "3");
        await using (var scope = run.Store(CompanyA))
        {
            var reordered = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.DispatchAsync(
                plan.TransferId, request, EffectiveOutgoing(plan.TransferId, request, secondLine, firstLine),
                Metadata(run), Authorized));
            Assert.Equal((503, InventoryTransferCodesV1.Unavailable), (reordered.Status, reordered.Code));
        }

        Assert.Equal((3m, 4m, 0), (await TransferLabelKgAsync(run, first.LabelId),
            await TransferLabelKgAsync(run, second.LabelId),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferDispatches WHERE TransferId = @id",
                ("@id", plan.TransferId))));

        await using (var scope = run.Store(CompanyA))
        {
            var dispatched = await scope.Store.DispatchAsync(plan.TransferId, request,
                EffectiveOutgoing(plan, request), Metadata(run), Authorized);
            Assert.Equal(("2", "InTransit"), (dispatched.Transfer.Version, dispatched.Transfer.Status));
        }

        Assert.Equal((0m, 0m), (await TransferLabelKgAsync(run, first.LabelId),
            await TransferLabelKgAsync(run, second.LabelId)));
        Assert.Equal((3, InventoryTransferDocumentKindsV1.LogisticsOutgoingDocument, request.OutgoingDocumentId), (
            await run.ScalarAsync<int>("SELECT DocumentVersion FROM inventory.TransferDispatches WHERE TransferId = @id",
                ("@id", plan.TransferId)),
            await run.ScalarAsync<string>("SELECT DocumentKind FROM inventory.TransferDispatches WHERE TransferId = @id",
                ("@id", plan.TransferId)),
            await run.ScalarAsync<Guid>("SELECT DocumentId FROM inventory.TransferDispatches WHERE TransferId = @id",
                ("@id", plan.TransferId))));
        var rejected = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE inventory.TransferDispatches SET DocumentVersion = 0 WHERE TransferId = @id; SELECT @@ROWCOUNT",
            ("@id", plan.TransferId)));
        Assert.Contains("CK_TransferDispatches_DocumentVersion", rejected.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task Transfer_dispatch_refusal_is_recorded_once_with_audit_and_replays_without_a_stock_effect()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-REFUSAL", 5m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var plan = TransferPlan(source, sourceLocation, destinationLocation, "5");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(plan, Metadata(run), Authorized);
        }

        var request = TransferDispatch();
        var refusalMetadata = Metadata(run) with
        {
            IdempotencyKey = $"transfer-dispatch-refusal-{Guid.NewGuid():N}",
            CorrelationId = $"transfer-dispatch-refusal-{Guid.NewGuid():N}",
        };
        await using (var scope = run.Store(CompanyA))
        {
            Assert.Null(await scope.Store.RecordDispatchRefusalAsync(plan.TransferId, request,
                new InventoryTransferException(409, InventoryTransferCodesV1.OutgoingDocumentNotFinal),
                refusalMetadata, Authorized));
        }

        var retry = refusalMetadata with { CorrelationId = $"retry-{Guid.NewGuid():N}" };
        await using (var scope = run.Store(CompanyA))
        {
            var replayed = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.ReplayDispatchAsync(
                plan.TransferId, request, retry, Authorized));
            Assert.Equal((409, InventoryTransferCodesV1.OutgoingDocumentNotFinal, true, refusalMetadata.CorrelationId),
                (replayed.Status, replayed.Code, replayed.IsReplay, replayed.OriginalCorrelationId));

            // A later effective proof does not override the key's recorded answer.
            var stillRefused = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.DispatchAsync(
                plan.TransferId, request, EffectiveOutgoing(plan, request), retry, Authorized));
            Assert.Equal((409, InventoryTransferCodesV1.OutgoingDocumentNotFinal, true),
                (stillRefused.Status, stillRefused.Code, stillRefused.IsReplay));
        }

        Assert.Equal((5m, 0, 0, 1L), (await TransferLabelKgAsync(run, source.LabelId),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferDispatches WHERE TransferId = @id",
                ("@id", plan.TransferId)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'inventory.transfer' AND SourceId = @id",
                ("@id", plan.TransferId)),
            await run.ScalarAsync<long>("SELECT Version FROM inventory.Transfers WHERE Id = @id",
                ("@id", plan.TransferId))));
        Assert.Equal((2, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id",
                ("@id", plan.TransferId)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.Transfer' AND EntityId = @id "
                + "AND EventType = N'inventory.transfer.dispatch-refused.v1' AND CorrelationId = @correlation",
                ("@id", plan.TransferId.ToString("D")), ("@correlation", refusalMetadata.CorrelationId))));

        await using (var scope = run.Store(CompanyA))
        {
            var unrecordable = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                scope.Store.RecordDispatchRefusalAsync(plan.TransferId, request,
                    new InventoryTransferException(422, InventoryTransferCodesV1.OutgoingDocumentNotFound),
                    Metadata(run), Authorized));
            Assert.Equal("refusal", unrecordable.ParamName);
        }

        var fresh = TransferDispatch();
        await using (var scope = run.Store(CompanyA))
        {
            var dispatched = await scope.Store.DispatchAsync(plan.TransferId, fresh, EffectiveOutgoing(plan, fresh),
                Metadata(run), Authorized);
            Assert.Equal(("2", "InTransit"), (dispatched.Transfer.Version, dispatched.Transfer.Status));
        }

        Assert.Equal(0m, await TransferLabelKgAsync(run, source.LabelId));
    }

    [SqlServerFact]
    public async Task Transfer_dispatch_document_version_migration_rolls_back_when_unproved_and_blocks_after_a_proved_dispatch()
    {
        const string previousMigration = "20261111091157_RelaxInventoryAverageCostReasonToZeroCostReceipt";
        const string currentMigration = "20261111231419_AddInventoryTransferDispatchDocumentVersion";
        await using var run = await Run.StartAsync();
        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'inventory.TransferDispatches') "
                + "AND name = N'DocumentVersion'"));
            await context.GetService<IMigrator>().MigrateAsync(currentMigration);
            await context.Database.MigrateAsync();
        }

        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-PROOF-DOWN", 2m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var plan = TransferPlan(source, sourceLocation, destinationLocation, "2");
        var request = TransferDispatch();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(plan, Metadata(run), Authorized);
            await scope.Store.DispatchAsync(plan.TransferId, request, EffectiveOutgoing(plan, request), Metadata(run),
                Authorized);
        }

        await using (var context = run.Context(CompanyA))
        {
            var rejected = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(previousMigration));
            Assert.Equal(51272, rejected.Number);
            Assert.Equal(currentMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.TransferDispatches WHERE DocumentVersion = 1 AND TransferId = @id",
            ("@id", plan.TransferId)));
    }
}
