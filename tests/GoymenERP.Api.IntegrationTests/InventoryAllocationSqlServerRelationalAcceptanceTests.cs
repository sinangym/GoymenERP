using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Physical_allocation_HTTP_create_replay_and_release_use_one_company_scoped_SQL_lifecycle()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-HTTP", 100m);
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var allocationId = Guid.NewGuid();
        var actor = await GrantAllocationAsync(run, TestUsers.CompanyA, CompanyA,
            ErpPermissions.InventoryPhysicalAllocationWrite);
        var ownerAnswer = new SalesOrderLineReferenceAnswerV1(
            SalesOrderLineReferenceOutcomeV1.Approved,
            new SalesOrderLineReferenceV1(CompanyA, orderId, lineId, 1, Guid.NewGuid(), Guid.NewGuid(),
                source.ProductId, "7", Guid.NewGuid(), "10", 0, 4),
            null);

        await using var host = run.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesOrderLineReferencePortV1>();
            services.AddSingleton<ISalesOrderLineReferencePortV1>(new FixedApprovedSalesLine(ownerAnswer));
        }));

        using (var denied = ClientFor(host, TestUsers.CompanyAViewer, CompanyA))
        using (var request = AllocationPost(allocationId, orderId, lineId, source, "SYN-ALLOC-HTTP-denied"))
        using (var response = await denied.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockAllocation'"));

        using var client = ClientFor(host, actor, CompanyA);
        using var firstRequest = AllocationPost(allocationId, orderId, lineId, source, "SYN-ALLOC-HTTP-create");
        using var first = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = (await first.Content.ReadFromJsonAsync<InventoryPhysicalAllocationViewV1>())!;
        Assert.Equal((allocationId, CompanyA, source.ProductId, "7", "Active", "60"),
            (created.AllocationId, created.CompanyId, created.ProductId, created.DemandProductVersion,
                created.Status, created.RemainingKg));

        using var replayRequest = AllocationPost(allocationId, orderId, lineId, source, "SYN-ALLOC-HTTP-create");
        using var replay = await client.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));

        using var releaseRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/inventory/physical-allocations/{allocationId:D}/release")
        {
            Content = JsonContent.Create(new ReleasePhysicalAllocationV1("1", "SENTETIK release")),
        };
        releaseRequest.Headers.Add("Idempotency-Key", "SYN-ALLOC-HTTP-release");
        using var releasedResponse = await client.SendAsync(releaseRequest);
        Assert.Equal(HttpStatusCode.OK, releasedResponse.StatusCode);
        var released = (await releasedResponse.Content.ReadFromJsonAsync<InventoryPhysicalAllocationViewV1>())!;
        Assert.Equal(("2", "Released", "0"), (released.Version, released.Status, released.RemainingKg));

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE Id = @id AND CompanyId = @company AND Status = 3 AND RemainingKg = 0",
            ("@id", allocationId), ("@company", CompanyA)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockAllocation' AND EntityId = @id",
            ("@id", allocationId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Physical_sales_allocation_reduces_free_capacity_without_a_movement_and_release_restores_it()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-CREATE", 100m);
        var movementCount = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");
        var createMetadata = Metadata(run) with
        {
            IdempotencyKey = $"allocate-{Guid.NewGuid():N}",
            CorrelationId = $"allocate-{Guid.NewGuid():N}",
        };
        var create = AllocationRequest(source, "60");

        InventoryPhysicalAllocationResultV1 first;
        await using (var scope = run.Store(CompanyA))
        {
            first = await scope.Store.CreateAsync(create, createMetadata);
        }

        Assert.False(first.IsReplay);
        Assert.Equal(("1", "Active", "60", "60"),
            (first.Allocation.Version, first.Allocation.Status, first.Allocation.AllocatedKg, first.Allocation.RemainingKg));
        Assert.Equal(movementCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(40m, await FreeAfterAllocationsAsync(run, source.LabelId));

        await using (var replayScope = run.Store(CompanyA))
        {
            var replay = await replayScope.Store.CreateAsync(create, createMetadata with
            {
                CorrelationId = $"replay-{Guid.NewGuid():N}",
            });
            Assert.True(replay.IsReplay);
            Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        }

        await using (var conflictScope = run.Store(CompanyA))
        {
            var conflict = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                conflictScope.Store.CreateAsync(create with { QuantityKg = "59" }, createMetadata));
            Assert.Equal((409, InventoryAllocationCodesV1.IdempotencyConflict), (conflict.Status, conflict.Code));
        }

        var releaseMetadata = Metadata(run) with
        {
            IdempotencyKey = $"release-{Guid.NewGuid():N}",
            CorrelationId = $"release-{Guid.NewGuid():N}",
        };
        await using (var scope = run.Store(CompanyA))
        {
            var released = await scope.Store.ReleaseAsync(create.AllocationId,
                new ReleasePhysicalAllocationV1("1", "Sipariş ihtiyacı kaldırıldı."), releaseMetadata);
            Assert.Equal(("2", "Released", "0"),
                (released.Allocation.Version, released.Allocation.Status, released.Allocation.RemainingKg));
        }

        await using (var replayScope = run.Store(CompanyA))
        {
            var replay = await replayScope.Store.ReleaseAsync(create.AllocationId,
                new ReleasePhysicalAllocationV1("1", "Sipariş ihtiyacı kaldırıldı."), releaseMetadata);
            Assert.True(replay.IsReplay);
        }

        Assert.Equal(100m, await FreeAfterAllocationsAsync(run, source.LabelId));
        Assert.Equal(movementCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", create.AllocationId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockAllocation' AND EntityId = @id",
            ("@id", create.AllocationId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Physical_sales_allocation_refuses_over_allocation_stale_and_foreign_sources_without_effect()
    {
        await using var run = await Run.StartAsync();
        var own = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-BOUND", 100m);
        var foreign = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-ALLOC-FOREIGN", 100m);
        var customerOwned = await SeedOtherPartyFreeSourceAsync(run, "SYN-ALLOC-CUSTOMER",
            new StockOwner(StockPartyKind.Counterparty, Guid.NewGuid()), new StockCustodian(StockPartyKind.Company, CompanyA));
        var fasonHeld = await SeedOtherPartyFreeSourceAsync(run, "SYN-ALLOC-FASON",
            new StockOwner(StockPartyKind.Company, CompanyA), new StockCustodian(StockPartyKind.Counterparty, Guid.NewGuid()));

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(own, "80"), Metadata(run));
        }

        var currentLabelVersion = await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", own.LabelId));
        var before = await AllocationEffectCountsAsync(run);
        await using (var scope = run.Store(CompanyA))
        {
            var insufficient = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(own, "20.001") with
                {
                    AllocationId = Guid.NewGuid(), ExpectedLabelVersion = currentLabelVersion.ToString(),
                }, Metadata(run)));
            Assert.Equal((409, InventoryAllocationCodesV1.InsufficientFreeStock),
                (insufficient.Status, insufficient.Code));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var stale = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(own, "1") with
                {
                    AllocationId = Guid.NewGuid(), ExpectedLabelVersion = own.LabelVersion.ToString(),
                }, Metadata(run)));
            Assert.Equal((409, InventoryAllocationCodesV1.LabelVersionConflict), (stale.Status, stale.Code));
        }

        var activeAllocationId = await run.ScalarAsync<Guid>(
            "SELECT TOP (1) Id FROM inventory.Allocations WHERE LabelId = @id", ("@id", own.LabelId));
        await using (var scope = run.Store(CompanyA))
        {
            var staleRelease = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.ReleaseAsync(activeAllocationId,
                    new ReleasePhysicalAllocationV1("2", "Stale sürüm reddi."), Metadata(run)));
            Assert.Equal((409, InventoryAllocationCodesV1.AllocationVersionConflict),
                (staleRelease.Status, staleRelease.Code));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var hidden = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(foreign, "1") with { AllocationId = Guid.NewGuid() }, Metadata(run)));
            Assert.Equal((404, InventoryAllocationCodesV1.InvalidReference), (hidden.Status, hidden.Code));
        }

        // #479 review P2-2: a Sales order line never reserves a Free position this company does not own AND hold.
        foreach (var other in new[] { customerOwned, fasonHeld })
        {
            await using var scope = run.Store(CompanyA);
            var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(other, "1"), Metadata(run)));
            Assert.Equal((422, InventoryAllocationCodesV1.OwnerNotCompany), (refused.Status, refused.Code));
            Assert.Equal(other.LabelVersion, await run.ScalarAsync<long>(
                "SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", other.LabelId)));
            Assert.Equal(100m, await FreeAfterAllocationsAsync(run, other.LabelId));
        }

        Assert.Equal(before, await AllocationEffectCountsAsync(run));
        Assert.Equal(20m, await FreeAfterAllocationsAsync(run, own.LabelId));
        Assert.Equal(100m, await FreeAfterAllocationsAsync(run, foreign.LabelId));
    }

    [SqlServerFact]
    public async Task Concurrent_physical_allocations_cannot_both_consume_the_same_free_capacity()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-RACE", 100m);
        var barrier = new Barrier(2);

        async Task<object> AllocateAsync()
        {
            await using var scope = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return await scope.Store.CreateAsync(
                    AllocationRequest(source, "60") with { AllocationId = Guid.NewGuid() }, Metadata(run));
            }
            catch (InventoryAllocationException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(AllocateAsync), Task.Run(AllocateAsync));

        Assert.Single(results, result => result is InventoryPhysicalAllocationResultV1);
        Assert.Single(results, result => result is string code && code == InventoryAllocationCodesV1.ConcurrentChange);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE LabelId = @id AND Status = 1", ("@id", source.LabelId)));
        Assert.Equal(40m, await FreeAfterAllocationsAsync(run, source.LabelId));
    }

    [SqlServerFact]
    public async Task Legacy_allocation_survives_physical_lifecycle_up_and_can_roll_back()
    {
        const string previousMigration = "20260924001222_AddInventoryDefiniteShipmentRuntime";
        await using var run = await Run.StartAsync(migrateInventory: false);
        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
        }

        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-LEGACY-UP", 25m);
        var allocationId = Guid.NewGuid();
        await InsertLegacyAllocationAsync(run, allocationId, source.LabelId);

        await using (var context = run.Context(CompanyA))
        {
            await context.Database.MigrateAsync();
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM inventory.Allocations
            WHERE Id = @id
              AND DemandKind IS NULL AND DemandId IS NULL AND DemandLineId IS NULL
              AND DemandProductId IS NULL AND DemandProductVersion IS NULL AND Reason IS NULL
              AND ReservedUntilUtc IS NULL AND ReleasedAtUtc IS NULL AND ReleaseReason IS NULL
              AND Status = 1
            """, ("@id", allocationId)));

        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(previousMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'inventory.Allocations')
              AND [name] IN (N'DemandKind', N'DemandId', N'DemandLineId', N'DemandProductId',
                             N'DemandProductVersion', N'Reason', N'ReservedUntilUtc',
                             N'ReleasedAtUtc', N'ReleaseReason')
            """));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE Id = @id AND Status = 1 AND RemainingKg = 10",
            ("@id", allocationId)));
    }

    [SqlServerFact]
    public async Task Partial_and_hybrid_physical_allocation_rows_are_rejected_by_SQL()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-HYBRID", 50m);

        var missingDemandId = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(run,
            PhysicalAllocationInsertSql(
                "N'sales.order-line'", "NULL", "NEWID()", "b.ProductId", "'1'", "N'approved line'", "NULL",
                "10", "1", "NULL", "NULL"),
            ("@id", Guid.NewGuid()), ("@label", source.LabelId)));
        Assert.Equal(547, missingDemandId.Number);
        Assert.Contains("CK_Allocations_Demand", missingDemandId.Message, StringComparison.Ordinal);

        var legacyReservation = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(run,
            PhysicalAllocationInsertSql(
                "NULL", "NULL", "NULL", "NULL", "NULL", "NULL", "DATEADD(hour, 1, SYSUTCDATETIME())",
                "10", "1", "NULL", "NULL"),
            ("@id", Guid.NewGuid()), ("@label", source.LabelId)));
        Assert.Equal(547, legacyReservation.Number);
        Assert.Contains("CK_Allocations_Demand", legacyReservation.Message, StringComparison.Ordinal);

        var releasedWithoutReason = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(run,
            PhysicalAllocationInsertSql(
                "N'sales.order-line'", "NEWID()", "NEWID()", "b.ProductId", "'1'", "N'approved line'", "NULL",
                "0", "3", "SYSUTCDATETIME()", "NULL"),
            ("@id", Guid.NewGuid()), ("@label", source.LabelId)));
        Assert.Equal(547, releasedWithoutReason.Number);
        Assert.Contains("CK_Allocations_Release", releasedWithoutReason.Message, StringComparison.Ordinal);

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations"));
    }

    [SqlServerFact]
    public async Task Every_physical_lifecycle_fact_blocks_down_before_schema_mutation()
    {
        const string previousMigration = "20260924001222_AddInventoryDefiniteShipmentRuntime";
        const string currentMigration = "20260924013338_AddInventoryPhysicalAllocationLifecycle";
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-DOWN-GUARD", 50m);
        var allocationId = Guid.NewGuid();
        await InsertLegacyAllocationAsync(run, allocationId, source.LabelId);
        await ExecuteAsync(run,
            """
            ALTER TABLE inventory.Allocations NOCHECK CONSTRAINT
                CK_Allocations_Demand, CK_Allocations_Quantity, CK_Allocations_Release;
            """);

        var lifecycleFacts = new[]
        {
            "[DemandKind] = N'sales.order-line'",
            "[DemandId] = NEWID()",
            "[DemandLineId] = NEWID()",
            "[DemandProductId] = NEWID()",
            "[DemandProductVersion] = '1'",
            "[Reason] = N'approved line'",
            "[ReservedUntilUtc] = DATEADD(hour, 1, SYSUTCDATETIME())",
            "[ReleasedAtUtc] = SYSUTCDATETIME()",
            "[ReleaseReason] = N'released'",
            "[Status] = 3",
        };

        foreach (var lifecycleFact in lifecycleFacts)
        {
            await ExecuteAsync(run,
                $"""
                UPDATE inventory.Allocations
                SET DemandKind = NULL, DemandId = NULL, DemandLineId = NULL, DemandProductId = NULL,
                    DemandProductVersion = NULL, Reason = NULL, ReservedUntilUtc = NULL,
                    ReleasedAtUtc = NULL, ReleaseReason = NULL, Status = 1, RemainingKg = 10;
                UPDATE inventory.Allocations SET {lifecycleFact} WHERE Id = @id;
                """, ("@id", allocationId));

            await using var context = run.Context(CompanyA);
            var rejected = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(previousMigration));
            Assert.Equal(51052, rejected.Number);
            Assert.Equal(currentMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
            Assert.Equal(9, await run.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'inventory.Allocations')
                  AND [name] IN (N'DemandKind', N'DemandId', N'DemandLineId', N'DemandProductId',
                                 N'DemandProductVersion', N'Reason', N'ReservedUntilUtc',
                                 N'ReleasedAtUtc', N'ReleaseReason')
                """));
            Assert.Equal(4, await run.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID(N'inventory.Allocations')
                  AND [name] IN (N'CK_Allocations_Demand', N'CK_Allocations_Quantity',
                                 N'CK_Allocations_Release', N'CK_Allocations_State')
                """));
            Assert.Equal(2, await run.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'inventory.Allocations')
                  AND [name] IN (N'IX_Allocations_ActivePosition', N'IX_Allocations_Demand')
                """));
        }

        await ExecuteAsync(run,
            """
            UPDATE inventory.Allocations
            SET DemandKind = NULL, DemandId = NULL, DemandLineId = NULL, DemandProductId = NULL,
                DemandProductVersion = NULL, Reason = NULL, ReservedUntilUtc = NULL,
                ReleasedAtUtc = NULL, ReleaseReason = NULL, Status = 1, RemainingKg = 10;
            ALTER TABLE inventory.Allocations WITH CHECK CHECK CONSTRAINT
                CK_Allocations_Demand, CK_Allocations_Quantity, CK_Allocations_Release;
            """);
    }

    [SqlServerFact]
    public async Task Definite_shipment_flushes_one_atomic_effect_replays_and_host_rollback_removes_every_effect()
    {
        await using var run = await Run.StartAsync();
        var state = await SeedAllocationAsync(run, "SYN-LOG5-SUCCESS");
        var correlation = $"log5-{Guid.NewGuid():N}";
        var metadata = Metadata(run) with
        {
            IdempotencyKey = $"log5-key-{Guid.NewGuid():N}",
            CorrelationId = correlation,
        };
        var request = Request(state, quantityKg: "40");

        InventoryDefiniteShipmentResultV1 first;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            first = await participant.StageAsync(request, metadata);
            Assert.False(first.IsReplay);
            Assert.Equal("2", first.AllocationVersion);
            Assert.Equal("40", first.RemainingAllocatedKg);
            Assert.Equal((state.LabelVersion + 1).ToString(), first.LabelVersion);
            await transaction.CommitAsync();
        }

        Assert.Equal(40m, await run.ScalarAsync<decimal>(
            "SELECT RemainingKg FROM inventory.Allocations WHERE Id = @id", ("@id", state.AllocationId)));
        Assert.Equal(2L, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Allocations WHERE Id = @id", ("@id", state.AllocationId)));
        Assert.Equal(60m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id", ("@id", state.LabelId)));
        Assert.Equal(state.LabelVersion + 1, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", state.LabelId)));

        InventoryDefiniteShipmentResultV1 replay;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            replay = await participant.StageAsync(request, metadata);
            await transaction.CommitAsync();
        }

        Assert.True(replay.IsReplay);
        Assert.Equal(first.OutboxMessageId, replay.OutboxMessageId);
        Assert.Equal(first.MovementId, replay.MovementId);
        Assert.Equal(correlation, replay.OriginalCorrelationId);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", request.MovementId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.OutboxMessages WHERE Id = @id", ("@id", first.OutboxMessageId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = @name AND CorrelationId = @c",
            ("@name", InventoryDefiniteShipmentOutboxV1.SourceOperation), ("@c", correlation)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @c", ("@c", correlation)));

        var rollbackState = await SeedAllocationAsync(run, "SYN-LOG5-ROLLBACK");
        var movementCount = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");
        var outboxCount = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages");
        var commandCount = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands");
        var auditCount = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            await participant.StageAsync(Request(rollbackState, "10"), metadata with
            {
                IdempotencyKey = $"rollback-{Guid.NewGuid():N}", CorrelationId = $"rollback-{Guid.NewGuid():N}",
            });
            await transaction.RollbackAsync();
        }

        Assert.Equal(movementCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(outboxCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages"));
        Assert.Equal(commandCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"));
        Assert.Equal(auditCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        Assert.Equal(1L, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Allocations WHERE Id = @id", ("@id", rollbackState.AllocationId)));
    }

    [SqlServerFact]
    public async Task Definite_shipment_hides_foreign_references_and_same_key_payload_conflicts_without_effect()
    {
        await using var run = await Run.StartAsync();
        var state = await SeedAllocationAsync(run, "SYN-LOG5-CONFLICT");
        var metadata = Metadata(run) with
        {
            IdempotencyKey = $"log5-conflict-{Guid.NewGuid():N}",
            CorrelationId = $"log5-conflict-{Guid.NewGuid():N}",
        };
        var foreignState = await SeedForeignAllocationAsync(run, "SYN-LOG5-FOREIGN");
        var request = Request(state, "5");
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            await participant.StageAsync(request, metadata);
            await transaction.CommitAsync();
        }

        var beforeConflict = await SnapshotShipmentEffectsAsync(run, state, foreignState);
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            var conflict = await Assert.ThrowsAsync<InventoryDefiniteShipmentException>(() =>
                participant.StageAsync(request with { QuantityKg = "4" }, metadata));
            Assert.Equal((409, InventoryDefiniteShipmentCodesV1.IdempotencyConflict), (conflict.Status, conflict.Code));
            await transaction.RollbackAsync();
        }
        Assert.Equal(beforeConflict, await SnapshotShipmentEffectsAsync(run, state, foreignState));

        var beforeForeign = await SnapshotShipmentEffectsAsync(run, state, foreignState);
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            var hidden = await Assert.ThrowsAsync<InventoryDefiniteShipmentException>(() => participant.StageAsync(
                request with
                {
                    AllocationId = foreignState.AllocationId,
                    LabelId = foreignState.LabelId,
                    ExpectedLabelVersion = foreignState.LabelVersion.ToString(),
                }, metadata with
                {
                    IdempotencyKey = $"foreign-{Guid.NewGuid():N}",
                }));
            Assert.Equal((404, InventoryDefiniteShipmentCodesV1.InvalidReference), (hidden.Status, hidden.Code));
            await transaction.RollbackAsync();
        }
        Assert.Equal(beforeForeign, await SnapshotShipmentEffectsAsync(run, state, foreignState));

        // #479 review P2-2: an allocation stored before the Sales ownership gate (or a legacy row) that names a
        // customer-owned position is never consumed as a company shipment; nothing is written.
        var customerOwned = await SeedOtherPartyFreeSourceAsync(run, "SYN-LOG5-CUSTOMER",
            new StockOwner(StockPartyKind.Counterparty, Guid.NewGuid()), new StockCustodian(StockPartyKind.Company, CompanyA),
            allocatedKg: 80m);
        var customerState = new ShipmentState(customerOwned.AllocationId!.Value, customerOwned.LabelId, customerOwned.LabelVersion);
        var beforeCustomer = await SnapshotShipmentEffectsAsync(run, state, customerState);
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            var refused = await Assert.ThrowsAsync<InventoryDefiniteShipmentException>(() => participant.StageAsync(
                Request(customerState, "5"), metadata with { IdempotencyKey = $"customer-{Guid.NewGuid():N}" }));
            Assert.Equal((409, InventoryDefiniteShipmentCodesV1.OwnerNotCompany), (refused.Status, refused.Code));
            await transaction.RollbackAsync();
        }
        Assert.Equal(beforeCustomer, await SnapshotShipmentEffectsAsync(run, state, customerState));
    }

    [SqlServerFact]
    public async Task Physical_allocation_is_company_scoped_constrained_and_optimistically_versioned()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, $"SYN-ALLOC-{Guid.NewGuid():N}");
        var position = Position(CompanyA, location, label, BlockState.Free);

        await using (var store = run.Store(CompanyA))
        {
            await store.Labels.SaveLabelAsync(label, Metadata(run));
            await store.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Receipt, In(position, 100m)), Metadata(run));
        }

        var allocationId = Guid.NewGuid();
        await using (var context = run.Context(CompanyA))
        {
            context.Allocations.Add(new AllocationRow
            {
                Id = allocationId,
                CompanyId = CompanyA,
                OwnerKind = (int)position.Owner.Kind,
                OwnerId = position.Owner.Id,
                CustodianKind = (int)position.Custodian.Kind,
                CustodianId = position.Custodian.Id,
                LocationId = position.LocationId,
                LabelId = position.LabelId,
                Block = (int)position.Block,
                AllocatedKg = 80m,
                RemainingKg = 80m,
                Status = (int)StockAllocationStatus.Active,
                Version = 1,
                CreatedAtUtc = At,
                UpdatedAtUtc = At,
            });
            await context.SaveChangesAsync();
        }

        await using (var otherCompany = run.Context(CompanyB))
        {
            Assert.Empty(await otherCompany.Allocations.AsNoTracking().ToListAsync());
        }

        await using var first = run.Context(CompanyA);
        await using var second = run.Context(CompanyA);
        var firstRow = await first.Allocations.SingleAsync(row => row.Id == allocationId);
        var secondRow = await second.Allocations.SingleAsync(row => row.Id == allocationId);
        firstRow.RemainingKg = 50m;
        firstRow.Version = 2;
        firstRow.UpdatedAtUtc = At.AddMinutes(1);
        await first.SaveChangesAsync();

        secondRow.RemainingKg = 40m;
        secondRow.Version = 2;
        secondRow.UpdatedAtUtc = At.AddMinutes(1);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        Assert.Equal(50m, await run.ScalarAsync<decimal>(
            "SELECT RemainingKg FROM inventory.Allocations WHERE Id = @id", ("@id", allocationId)));
        Assert.Equal(2L, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Allocations WHERE Id = @id", ("@id", allocationId)));

        await using var invalid = run.Context(CompanyA);
        invalid.Allocations.Add(new AllocationRow
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyA,
            OwnerKind = (int)position.Owner.Kind,
            OwnerId = position.Owner.Id,
            CustodianKind = (int)position.Custodian.Kind,
            CustodianId = position.Custodian.Id,
            LocationId = position.LocationId,
            LabelId = position.LabelId,
            Block = (int)BlockState.Blocked,
            AllocatedKg = 1m,
            RemainingKg = 1m,
            Status = (int)StockAllocationStatus.Active,
            Version = 1,
            CreatedAtUtc = At,
            UpdatedAtUtc = At,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations"));
    }

    private static async Task<ShipmentState> SeedAllocationAsync(Run run, string code)
    {
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, code);
        var position = Position(CompanyA, location, label, BlockState.Free);
        var seedMetadata = Metadata(run) with { CorrelationId = $"seed-{Guid.NewGuid():N}" };
        await using (var store = run.Store(CompanyA))
        {
            await store.Labels.SaveLabelAsync(label, seedMetadata);
            await store.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Receipt, In(position, 100m)), seedMetadata);
        }

        var allocationId = Guid.NewGuid();
        await using var context = run.Context(CompanyA);
        context.Allocations.Add(new AllocationRow
        {
            Id = allocationId, CompanyId = CompanyA,
            OwnerKind = (int)position.Owner.Kind, OwnerId = position.Owner.Id,
            CustodianKind = (int)position.Custodian.Kind, CustodianId = position.Custodian.Id,
            LocationId = position.LocationId, LabelId = position.LabelId, Block = (int)position.Block,
            AllocatedKg = 80m, RemainingKg = 80m, Status = (int)StockAllocationStatus.Active, Version = 1,
            CreatedAtUtc = At, UpdatedAtUtc = At,
        });
        await context.SaveChangesAsync();
        var labelVersion = await context.Labels.Where(row => row.Id == label.Id).Select(row => row.Version).SingleAsync();
        return new(allocationId, label.Id, labelVersion);
    }

    private static async Task<PhysicalSource> SeedFreePhysicalSourceAsync(
        Run run,
        Guid company,
        string code,
        decimal quantity)
    {
        var location = await run.LocationOfAsync(company);
        var label = NewLabel(company, code);
        var position = Position(company, location, label, BlockState.Free);
        await using (var scope = run.Store(company))
        {
            var seedMetadata = Metadata(run) with { CorrelationId = $"seed-{Guid.NewGuid():N}" };
            await scope.Lots.SaveLotAsync(
                new MaterialLot(company == CompanyB ? LotB : LotA, company, Product), [], seedMetadata);
            await scope.Labels.SaveLabelAsync(label, seedMetadata);

            if (company == CompanyA)
            {
                await scope.Movements.RecordMovementAsync(
                    Movement(company, StockMovementKind.Receipt, In(position, quantity)), seedMetadata);
            }
        }

        // Only GDC/CompanyA has an approved Settings kilogram profile. The foreign-company
        // isolation fixture therefore stores its pre-existing balance directly, as the definite-
        // shipment foreign fixture below already does; it must not invent a CompanyB profile.
        if (company != CompanyA)
        {
            await using var foreign = run.Context(company);
            foreign.Balances.Add(new BalanceRow
            {
                Id = Guid.NewGuid(), CompanyId = company,
                OwnerKind = (int)position.Owner.Kind, OwnerId = position.Owner.Id,
                CustodianKind = (int)position.Custodian.Kind, CustodianId = position.Custodian.Id,
                LocationId = position.LocationId, LabelId = position.LabelId, ProductId = position.ProductId,
                Block = (int)position.Block, QuantityKg = quantity,
            });
            await foreign.SaveChangesAsync();
        }

        await using var context = run.Context(company);
        var version = await context.Labels.Where(row => row.Id == label.Id).Select(row => row.Version).SingleAsync();
        return new(label.Id, label.ProductId, version);
    }

    /// <summary>
    /// A CompanyA label whose only Free position another party owns or holds, optionally with an active allocation
    /// row stored directly (as a pre-gate or legacy allocation would be). Which flow produces such stock (#479,
    /// #66/5830378162) is not the gate's concern, so the position is stored directly, as the foreign fixture does.
    /// </summary>
    private static async Task<OtherPartySource> SeedOtherPartyFreeSourceAsync(
        Run run,
        string code,
        StockOwner owner,
        StockCustodian custodian,
        decimal? allocatedKg = null)
    {
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, code);
        await using (var scope = run.Store(CompanyA))
        {
            // The run already stores CompanyA's synthetic lot (LotA); only the label is new.
            await scope.Labels.SaveLabelAsync(label, Metadata(run) with { CorrelationId = $"seed-{Guid.NewGuid():N}" });
        }

        Guid? allocationId = allocatedKg is null ? null : Guid.NewGuid();
        await using var context = run.Context(CompanyA);
        context.Balances.Add(new BalanceRow
        {
            Id = Guid.NewGuid(), CompanyId = CompanyA,
            OwnerKind = (int)owner.Kind, OwnerId = owner.Id,
            CustodianKind = (int)custodian.Kind, CustodianId = custodian.Id,
            LocationId = location, LabelId = label.Id, ProductId = label.ProductId,
            Block = (int)BlockState.Free, QuantityKg = 100m,
        });
        if (allocationId is { } id)
        {
            context.Allocations.Add(new AllocationRow
            {
                Id = id, CompanyId = CompanyA,
                OwnerKind = (int)owner.Kind, OwnerId = owner.Id,
                CustodianKind = (int)custodian.Kind, CustodianId = custodian.Id,
                LocationId = location, LabelId = label.Id, Block = (int)BlockState.Free,
                AllocatedKg = allocatedKg!.Value, RemainingKg = allocatedKg.Value,
                Status = (int)StockAllocationStatus.Active, Version = 1, CreatedAtUtc = At, UpdatedAtUtc = At,
            });
        }

        await context.SaveChangesAsync();
        var version = await context.Labels.Where(row => row.Id == label.Id).Select(row => row.Version).SingleAsync();
        return new(label.Id, label.ProductId, version, allocationId);
    }

    private static async Task InsertLegacyAllocationAsync(Run run, Guid allocationId, Guid labelId) =>
        await ExecuteAsync(run,
            """
            INSERT INTO inventory.Allocations
                (Id, CompanyId, OwnerKind, OwnerId, CustodianKind, CustodianId, LocationId, LabelId,
                 Block, AllocatedKg, RemainingKg, Status, Version, CreatedAtUtc, UpdatedAtUtc)
            SELECT @id, b.CompanyId, b.OwnerKind, b.OwnerId, b.CustodianKind, b.CustodianId,
                   b.LocationId, b.LabelId, 1, 10, 10, 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME()
            FROM inventory.Balances b
            WHERE b.LabelId = @label AND b.Block = 1;
            """, ("@id", allocationId), ("@label", labelId));

    private static string PhysicalAllocationInsertSql(
        string demandKind,
        string demandId,
        string demandLineId,
        string demandProductId,
        string demandProductVersion,
        string reason,
        string reservedUntilUtc,
        string remainingKg,
        string status,
        string releasedAtUtc,
        string releaseReason) =>
        $"""
        INSERT INTO inventory.Allocations
            (Id, CompanyId, OwnerKind, OwnerId, CustodianKind, CustodianId, LocationId, LabelId, Block,
             DemandKind, DemandId, DemandLineId, DemandProductId, DemandProductVersion, Reason, ReservedUntilUtc,
             AllocatedKg, RemainingKg, Status, Version, CreatedAtUtc, UpdatedAtUtc, ReleasedAtUtc, ReleaseReason)
        SELECT @id, b.CompanyId, b.OwnerKind, b.OwnerId, b.CustodianKind, b.CustodianId, b.LocationId, b.LabelId, 1,
               {demandKind}, {demandId}, {demandLineId}, {demandProductId}, {demandProductVersion}, {reason}, {reservedUntilUtc},
               10, {remainingKg}, {status}, 1, SYSUTCDATETIME(), SYSUTCDATETIME(), {releasedAtUtc}, {releaseReason}
        FROM inventory.Balances b
        WHERE b.LabelId = @label AND b.Block = 1;
        """;

    private static async Task<int> ExecuteAsync(
        Run run,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    private static CreatePhysicalSalesAllocationV1 AllocationRequest(PhysicalSource source, string quantityKg) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), source.ProductId, "7", source.LabelId,
        source.LabelVersion.ToString(), quantityKg, "Onaylı satış satırı için fiziksel kaynak.", null);

    private static HttpClient ClientFor(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host,
        string developmentUser,
        Guid companyId)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", developmentUser);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Correlation-Id", $"SYN-ALLOC-HTTP-{Guid.NewGuid():N}");
        return client;
    }

    private static HttpRequestMessage AllocationPost(
        Guid allocationId,
        Guid orderId,
        Guid lineId,
        PhysicalSource source,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/physical-allocations")
        {
            Content = JsonContent.Create(new CreateInventoryPhysicalAllocationRequestV1(
                allocationId, orderId, lineId, source.LabelId, source.LabelVersion.ToString(), "60",
                "SENTETIK approved order allocation", null)),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static async Task<string> GrantAllocationAsync(
        Run run,
        string developmentUser,
        Guid companyId,
        params string[] permissions)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
            value => value.Issuer == BootstrapIdentityOptions.Issuer && value.Subject == developmentUser);
        var actorId = link?.UserId ?? Guid.NewGuid();
        if (link is null)
        {
            dbContext.Users.Add(new UserAccount(actorId, "Synthetic Inventory allocation actor", null));
            dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actorId,
                BootstrapIdentityOptions.Issuer, developmentUser));
        }

        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"INV-ALLOC-{roleId:N}",
            "Synthetic Inventory allocation acceptance role", true);
        foreach (var permission in permissions)
        {
            role.AddPermission(permission);
        }

        var assignment = new RoleAssignment(Guid.NewGuid(), actorId, roleId, companyId,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, companyId);
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
        return developmentUser;
    }

    private sealed class FixedApprovedSalesLine(SalesOrderLineReferenceAnswerV1 answer)
        : ISalesOrderLineReferencePortV1
    {
        public Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineAsync(
            Guid orderId,
            Guid lineId,
            CancellationToken cancellationToken = default) => Task.FromResult(answer);

        public Task<SalesApprovedLinePageV1> ListApprovedLinesAsync(
            int pageSize,
            string? cursor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static async Task<decimal> FreeAfterAllocationsAsync(Run run, Guid labelId) =>
        await run.ScalarAsync<decimal>(
            "SELECT (SELECT COALESCE(SUM(QuantityKg), 0) FROM inventory.Balances WHERE LabelId = @id AND Block = 1) "
            + "- (SELECT COALESCE(SUM(RemainingKg), 0) FROM inventory.Allocations WHERE LabelId = @id AND Status = 1)",
            ("@id", labelId));

    private static async Task<(int Allocations, int Commands, int Audits)> AllocationEffectCountsAsync(Run run) =>
        (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE CommandName LIKE N'inventory.physical-allocation.%'"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockAllocation'")
        );

    private static async Task<ShipmentState> SeedForeignAllocationAsync(Run run, string code)
    {
        var location = await run.LocationOfAsync(CompanyB);
        var label = NewLabel(CompanyB, code);
        var position = Position(CompanyB, location, label, BlockState.Free);
        await using (var store = run.Store(CompanyB))
        {
            await store.Labels.SaveLabelAsync(label,
                Metadata(run) with { CorrelationId = $"foreign-seed-{Guid.NewGuid():N}" });
        }

        var allocationId = Guid.NewGuid();
        await using var context = run.Context(CompanyB);
        context.Balances.Add(new BalanceRow
        {
            Id = Guid.NewGuid(), CompanyId = CompanyB,
            OwnerKind = (int)position.Owner.Kind, OwnerId = position.Owner.Id,
            CustodianKind = (int)position.Custodian.Kind, CustodianId = position.Custodian.Id,
            LocationId = position.LocationId, LabelId = position.LabelId, ProductId = position.ProductId,
            Block = (int)position.Block, QuantityKg = 100m,
        });
        context.Allocations.Add(new AllocationRow
        {
            Id = allocationId, CompanyId = CompanyB,
            OwnerKind = (int)position.Owner.Kind, OwnerId = position.Owner.Id,
            CustodianKind = (int)position.Custodian.Kind, CustodianId = position.Custodian.Id,
            LocationId = position.LocationId, LabelId = position.LabelId, Block = (int)position.Block,
            AllocatedKg = 80m, RemainingKg = 80m, Status = (int)StockAllocationStatus.Active, Version = 1,
            CreatedAtUtc = At, UpdatedAtUtc = At,
        });
        await context.SaveChangesAsync();
        var labelVersion = await context.Labels.Where(row => row.Id == label.Id).Select(row => row.Version).SingleAsync();
        return new(allocationId, label.Id, labelVersion);
    }

    private static async Task<ShipmentEffectSnapshot> SnapshotShipmentEffectsAsync(
        Run run,
        ShipmentState own,
        ShipmentState foreign) =>
        new(
            await run.ScalarAsync<decimal>(
                "SELECT RemainingKg FROM inventory.Allocations WHERE Id = @id", ("@id", own.AllocationId)),
            await run.ScalarAsync<long>(
                "SELECT Version FROM inventory.Allocations WHERE Id = @id", ("@id", own.AllocationId)),
            await run.ScalarAsync<int>(
                "SELECT Status FROM inventory.Allocations WHERE Id = @id", ("@id", own.AllocationId)),
            await run.ScalarAsync<decimal>(
                "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id", ("@id", own.LabelId)),
            await run.ScalarAsync<long>(
                "SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", own.LabelId)),
            await run.ScalarAsync<decimal>(
                "SELECT RemainingKg FROM inventory.Allocations WHERE Id = @id", ("@id", foreign.AllocationId)),
            await run.ScalarAsync<long>(
                "SELECT Version FROM inventory.Allocations WHERE Id = @id", ("@id", foreign.AllocationId)),
            await run.ScalarAsync<int>(
                "SELECT Status FROM inventory.Allocations WHERE Id = @id", ("@id", foreign.AllocationId)),
            await run.ScalarAsync<decimal>(
                "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id", ("@id", foreign.LabelId)),
            await run.ScalarAsync<long>(
                "SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", foreign.LabelId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.MovementLines"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.SourceGrains"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));

    private static InventoryDefiniteShipmentRequestV1 Request(ShipmentState state, string quantityKg) => new(
        CompanyA, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), state.AllocationId, "1",
        state.LabelId, state.LabelVersion.ToString(), Guid.NewGuid(), quantityKg, At);

    private static ISettingsPrerequisiteProvider Settings(Run run, Guid company)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(company);
        return new ApprovedSettingsPrerequisiteProvider(companyContext, new FixedCurrentUser(run.Actor, company));
    }

    private sealed record ShipmentState(Guid AllocationId, Guid LabelId, long LabelVersion);

    private sealed record PhysicalSource(Guid LabelId, Guid ProductId, long LabelVersion);

    private sealed record OtherPartySource(Guid LabelId, Guid ProductId, long LabelVersion, Guid? AllocationId)
    {
        public static implicit operator PhysicalSource(OtherPartySource source) =>
            new(source.LabelId, source.ProductId, source.LabelVersion);
    }

    private sealed record ShipmentEffectSnapshot(
        decimal OwnAllocationRemainingKg,
        long OwnAllocationVersion,
        int OwnAllocationStatus,
        decimal OwnBalanceQuantityKg,
        long OwnLabelVersion,
        decimal ForeignAllocationRemainingKg,
        long ForeignAllocationVersion,
        int ForeignAllocationStatus,
        decimal ForeignBalanceQuantityKg,
        long ForeignLabelVersion,
        int MovementCount,
        int MovementLineCount,
        int SourceGrainCount,
        int OutboxCount,
        int CommandCount,
        int AuditCount);
}
