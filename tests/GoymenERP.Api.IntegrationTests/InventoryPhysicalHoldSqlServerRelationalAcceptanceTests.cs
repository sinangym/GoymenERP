using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Quality_hold_moves_one_whole_real_label_once_replays_and_is_company_isolated_on_real_SQL()
    {
        await using var run = await Run.StartAsync();
        var label = await SeedFreeLabelAsync(run, "SYN-QUALITY-HOLD-ONE", 25.125m);
        var version = await LabelVersionAsync(run, label.Id);
        var holdId = Guid.NewGuid();
        var inspectionId = Guid.NewGuid();
        var maker = HoldMetadata(run, "quality-maker", "hold-place-one");
        var place = new PlaceInventoryPhysicalHoldV1(holdId, label.Id, Version(version), inspectionId,
            "SENTETIK uygunsuzluk incelemesi", null);

        await using (var scope = run.Store(CompanyA))
        {
            var writer = (IInventoryPhysicalHoldWriterV1)scope.Store;
            var first = await writer.PlacePhysicalHoldAsync(place, maker);
            var replay = await writer.PlacePhysicalHoldAsync(place, maker);
            Assert.Equal((InventoryPhysicalHoldTokensV1.Active, "25.125", false),
                (first.Hold.State, first.Hold.QuantityKg, first.Replayed));
            Assert.True(replay.Replayed);
            Assert.Equal(first.Hold, replay.Hold);
        }

        var blockedVersion = await LabelVersionAsync(run, label.Id);
        var dispositionId = Guid.NewGuid();
        var checker = HoldMetadata(run, "quality-checker", "hold-release-one");
        var release = new ReleaseInventoryPhysicalHoldV1(holdId, "1", label.Id, Version(blockedVersion),
            dispositionId, "SENTETIK bağımsız serbest bırakma kararı");
        await using (var scope = run.Store(CompanyA))
        {
            var writer = (IInventoryPhysicalHoldWriterV1)scope.Store;
            var first = await writer.ReleasePhysicalHoldAsync(release, checker);
            var replay = await writer.ReleasePhysicalHoldAsync(release, checker);
            Assert.Equal((InventoryPhysicalHoldTokensV1.Released, "2", false),
                (first.Hold.State, first.Hold.Version, first.Replayed));
            Assert.True(replay.Replayed);
            Assert.Equal(first.Hold, replay.Hold);
        }

        await using (var otherCompany = run.Store(CompanyB))
        {
            var writer = (IInventoryPhysicalHoldWriterV1)otherCompany.Store;
            var error = await Assert.ThrowsAsync<InventoryPhysicalHoldException>(() =>
                writer.PlacePhysicalHoldAsync(place with { HoldId = Guid.NewGuid() },
                    HoldMetadata(run, "company-b-maker", "company-b-place")));
            Assert.Equal(InventoryPhysicalHoldCodesV1.LabelNotFound, error.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.PhysicalHolds WHERE Id = @id", ("@id", holdId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'quality.inspection-hold' AND SourceId = @id",
            ("@id", inspectionId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'quality.disposition' AND SourceId = @id",
            ("@id", dispositionId)));
        Assert.Equal(25.125m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 1", ("@id", label.Id)));
        Assert.Equal(0m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 2", ("@id", label.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @c", ("@c", maker.CorrelationId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @c", ("@c", checker.CorrelationId)));
    }

    [SqlServerFact]
    public async Task Quality_holds_release_only_the_last_typed_hold_fail_closed_for_legacy_block_and_preserve_allocation_on_real_SQL()
    {
        await using var run = await Run.StartAsync();
        var label = await SeedFreeLabelAsync(run, "SYN-QUALITY-HOLD-MULTI", 40m);
        var firstHold = Guid.NewGuid();
        var secondHold = Guid.NewGuid();

        await PlaceAsync(run, new(firstHold, label.Id, Version(await LabelVersionAsync(run, label.Id)), Guid.NewGuid(),
            "SENTETIK birinci kalite nedeni", null), HoldMetadata(run, "maker-a", "multi-place-a"));
        await PlaceAsync(run, new(secondHold, label.Id, Version(await LabelVersionAsync(run, label.Id)), Guid.NewGuid(),
            "SENTETIK ikinci kalite nedeni", null), HoldMetadata(run, "maker-b", "multi-place-b"));

        await ReleaseAsync(run, new(firstHold, "1", label.Id, Version(await LabelVersionAsync(run, label.Id)),
            Guid.NewGuid(), "SENTETIK birinci hold kapanışı"), HoldMetadata(run, "checker-a", "multi-release-a"));
        Assert.Equal(40m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 2", ("@id", label.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.PhysicalHolds WHERE Id = @id AND ReleaseMovementId IS NOT NULL",
            ("@id", firstHold)));

        await ReleaseAsync(run, new(secondHold, "1", label.Id, Version(await LabelVersionAsync(run, label.Id)),
            Guid.NewGuid(), "SENTETIK son hold kapanışı"), HoldMetadata(run, "checker-b", "multi-release-b"));
        Assert.Equal(40m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 1", ("@id", label.Id)));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE EXISTS "
            + "(SELECT 1 FROM inventory.MovementLines l WHERE l.MovementId = inventory.Movements.Id AND l.LabelId = @id)",
            ("@id", label.Id)));

        var legacy = await SeedFreeLabelAsync(run, "SYN-QUALITY-HOLD-LEGACY", 12m);
        var legacyLocation = await run.LocationOfAsync(CompanyA);
        var legacyFree = Position(CompanyA, legacyLocation, legacy, BlockState.Free);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Movements.RecordMovementAsync(Movement(CompanyA, StockMovementKind.Block,
                    Out(legacyFree, 12m), In(legacyFree.WithBlock(BlockState.Blocked), 12m)), Metadata(run));
            var writer = (IInventoryPhysicalHoldWriterV1)scope.Store;
            var legacyVersion = Version(await LabelVersionAsync(run, legacy.Id));
            var error = await Assert.ThrowsAsync<InventoryPhysicalHoldException>(() => writer.PlacePhysicalHoldAsync(
                new(Guid.NewGuid(), legacy.Id, legacyVersion, Guid.NewGuid(),
                    "SENTETIK legacy blok denemesi", null), HoldMetadata(run, "legacy-maker", "legacy-place")));
            Assert.Equal(InventoryPhysicalHoldCodesV1.LegacyBlockedAmbiguous, error.Code);
        }

        var allocated = await SeedFreeLabelAsync(run, "SYN-QUALITY-HOLD-ALLOC", 30m);
        var allocatedHold = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            var allocatedVersion = Version(await LabelVersionAsync(run, allocated.Id));
            await ((IInventoryPhysicalAllocationWriterV1)scope.Store).CreateAsync(
                new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Product, "product-v1", allocated.Id,
                    allocatedVersion, "10", "SENTETIK korunmuş tahsis", null),
                Metadata(run));
            var allocatedCurrentVersion = Version(await LabelVersionAsync(run, allocated.Id));
            await ((IInventoryPhysicalHoldWriterV1)scope.Store).PlacePhysicalHoldAsync(
                new(allocatedHold, allocated.Id, allocatedCurrentVersion, Guid.NewGuid(),
                    "SENTETIK tahsis korunarak kalite kontrolü", null),
                HoldMetadata(run, "allocation-maker", "allocation-place"));
        }
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE LabelId = @id AND Status = 1 AND RemainingKg = 10",
            ("@id", allocated.Id)));
        Assert.Equal(30m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 2", ("@id", allocated.Id)));

        var reader = await GrantStockPositionReaderAsync(run, TestUsers.NoAssignment);
        using (var client = run.Factory.CreateClientFor(reader, CompanyA))
        using (var response = await client.GetAsync(
                   $"{InventoryStockPositionApiV1.PositionsPath}?q=SYN-QUALITY-HOLD-ALLOC&limit=25"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = (await response.Content.ReadFromJsonAsync<InventoryStockPositionPageV1>())!;
            var item = Assert.Single(page.Items);
            Assert.Equal((allocated.Id, "30", "10", "20", "10", "0"),
                (item.LabelId, item.PhysicalKg, item.FreePhysicalKg, item.BlockedPhysicalKg,
                    item.ActiveAllocationKg, item.AvailableKg));
        }

        await ReleaseAsync(run,
            new(allocatedHold, "1", allocated.Id, Version(await LabelVersionAsync(run, allocated.Id)), Guid.NewGuid(),
                "SENTETIK tahsis korunarak kalite serbest bırakması"),
            HoldMetadata(run, "allocation-checker", "allocation-release"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE LabelId = @id AND Status = 1 AND RemainingKg = 10",
            ("@id", allocated.Id)));
        Assert.Equal(30m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 1", ("@id", allocated.Id)));
        using (var client = run.Factory.CreateClientFor(reader, CompanyA))
        using (var response = await client.GetAsync(
                   $"{InventoryStockPositionApiV1.PositionsPath}?q=SYN-QUALITY-HOLD-ALLOC&limit=25"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = (await response.Content.ReadFromJsonAsync<InventoryStockPositionPageV1>())!;
            var item = Assert.Single(page.Items);
            Assert.Equal((allocated.Id, "30", "30", "0", "10", "20"),
                (item.LabelId, item.PhysicalKg, item.FreePhysicalKg, item.BlockedPhysicalKg,
                    item.ActiveAllocationKg, item.AvailableKg));
        }
    }

    [SqlServerFact]
    public async Task Concurrent_first_quality_holds_post_at_most_one_block_movement_on_real_SQL()
    {
        await using var run = await Run.StartAsync();
        var label = await SeedFreeLabelAsync(run, "SYN-QUALITY-HOLD-RACE", 18m);
        var expected = Version(await LabelVersionAsync(run, label.Id));
        using var barrier = new Barrier(2);

        async Task<object> Attempt(string actor)
        {
            try
            {
                await using var scope = run.Store(CompanyA, new BarrierInterceptor(barrier));
                return await ((IInventoryPhysicalHoldWriterV1)scope.Store).PlacePhysicalHoldAsync(
                    new(Guid.NewGuid(), label.Id, expected, Guid.NewGuid(), $"SENTETIK yarış {actor}", null),
                    HoldMetadata(run, actor, $"race-{actor}"));
            }
            catch (InventoryPhysicalHoldException exception)
            {
                return exception;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => Attempt("maker-race-a")), Task.Run(() => Attempt("maker-race-b")));
        Assert.Single(results.OfType<InventoryPhysicalHoldResultV1>());
        Assert.Equal(InventoryPhysicalHoldCodesV1.ConcurrentChange,
            Assert.Single(results.OfType<InventoryPhysicalHoldException>()).Code);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.PhysicalHolds WHERE LabelId = @id", ("@id", label.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'quality.inspection-hold'"));
        Assert.Equal(18m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 2", ("@id", label.Id)));
    }

    [SqlServerFact]
    public async Task Concurrent_last_quality_hold_releases_preserve_one_hold_until_one_release_movement_on_real_SQL()
    {
        await using var run = await Run.StartAsync();
        var label = await SeedFreeLabelAsync(run, "SYN-QUALITY-HOLD-RELEASE-RACE", 22m);
        var holdA = Guid.NewGuid();
        var holdB = Guid.NewGuid();
        await PlaceAsync(run, new(holdA, label.Id, Version(await LabelVersionAsync(run, label.Id)), Guid.NewGuid(),
            "SENTETIK release yarışı A", null), HoldMetadata(run, "maker-release-a", "release-race-place-a"));
        await PlaceAsync(run, new(holdB, label.Id, Version(await LabelVersionAsync(run, label.Id)), Guid.NewGuid(),
            "SENTETIK release yarışı B", null), HoldMetadata(run, "maker-release-b", "release-race-place-b"));

        var expected = Version(await LabelVersionAsync(run, label.Id));
        using var barrier = new Barrier(2);

        async Task<object> Attempt(Guid holdId, string actor)
        {
            try
            {
                await using var scope = run.Store(CompanyA, new BarrierInterceptor(barrier));
                return await ((IInventoryPhysicalHoldWriterV1)scope.Store).ReleasePhysicalHoldAsync(
                    new(holdId, "1", label.Id, expected, Guid.NewGuid(), $"SENTETIK yarış kapanışı {actor}"),
                    HoldMetadata(run, actor, $"release-race-{actor}"));
            }
            catch (InventoryPhysicalHoldException exception)
            {
                return exception;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => Attempt(holdA, "checker-release-a")),
            Task.Run(() => Attempt(holdB, "checker-release-b")));
        Assert.Single(results.OfType<InventoryPhysicalHoldResultV1>());
        Assert.Equal(InventoryPhysicalHoldCodesV1.ConcurrentChange,
            Assert.Single(results.OfType<InventoryPhysicalHoldException>()).Code);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.PhysicalHolds WHERE LabelId = @id AND Status = 1", ("@id", label.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'quality.disposition'"));
        Assert.Equal(22m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 2", ("@id", label.Id)));

        var remaining = await run.ScalarAsync<Guid>(
            "SELECT Id FROM inventory.PhysicalHolds WHERE LabelId = @id AND Status = 1", ("@id", label.Id));
        await ReleaseAsync(run,
            new(remaining, "1", label.Id, Version(await LabelVersionAsync(run, label.Id)), Guid.NewGuid(),
                "SENTETIK yarış sonrası son kapanış"),
            HoldMetadata(run, "checker-release-final", "release-race-final"));

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.PhysicalHolds WHERE LabelId = @id AND Status = 1", ("@id", label.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'quality.disposition'"));
        Assert.Equal(22m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 1", ("@id", label.Id)));
    }

    [SqlServerFact]
    public async Task Physical_hold_migration_applies_rolls_back_and_reapplies_on_real_SQL()
    {
        const string predecessor = "20261002110000_AddInventoryLabelClassification";
        const string physicalHolds = "20261016100000_AddInventoryPhysicalHolds";

        await using (var empty = await Run.StartAsync(migrateInventory: false))
        await using (var context = empty.Context(CompanyA))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(predecessor);
            Assert.Equal(0, await PhysicalHoldTableExistsAsync(empty));
            await migrator.MigrateAsync(physicalHolds);
            Assert.Equal(1, await PhysicalHoldTableExistsAsync(empty));
            await migrator.MigrateAsync(predecessor);
            Assert.Equal(0, await PhysicalHoldTableExistsAsync(empty));
            await migrator.MigrateAsync(physicalHolds);
            Assert.Equal(1, await PhysicalHoldTableExistsAsync(empty));
        }

        await using var populated = await Run.StartAsync();
        var activeLabel = await SeedFreeLabelAsync(populated, "SYN-QUALITY-HOLD-DOWN-ACTIVE", 14m);
        var releasedLabel = await SeedFreeLabelAsync(populated, "SYN-QUALITY-HOLD-DOWN-RELEASED", 16m);
        var activeId = Guid.NewGuid();
        var activeRequest = new PlaceInventoryPhysicalHoldV1(activeId, activeLabel.Id,
            Version(await LabelVersionAsync(populated, activeLabel.Id)), Guid.NewGuid(),
            "SENTETIK downgrade aktif hold", null);
        var activeMetadata = HoldMetadata(populated, "down-active-maker", "down-active-place");
        await PlaceAsync(populated, activeRequest, activeMetadata);

        var releasedId = Guid.NewGuid();
        var releasedRequest = new PlaceInventoryPhysicalHoldV1(releasedId, releasedLabel.Id,
            Version(await LabelVersionAsync(populated, releasedLabel.Id)), Guid.NewGuid(),
            "SENTETIK downgrade bırakılmış hold", null);
        var releasedPlacement = HoldMetadata(populated, "down-released-maker", "down-released-place");
        await PlaceAsync(populated, releasedRequest, releasedPlacement);
        var releaseRequest = new ReleaseInventoryPhysicalHoldV1(releasedId, "1", releasedLabel.Id,
            Version(await LabelVersionAsync(populated, releasedLabel.Id)), Guid.NewGuid(),
            "SENTETIK downgrade öncesi karar");
        var releaseMetadata = HoldMetadata(populated, "down-released-checker", "down-released-release");
        await ReleaseAsync(populated, releaseRequest, releaseMetadata);

        var before = await PhysicalHoldRollbackTruthAsync(populated);
        await using var populatedContext = populated.Context(CompanyA);
        var populatedMigrator = populatedContext.GetService<IMigrator>();
        var refusal = await Assert.ThrowsAsync<SqlException>(() => populatedMigrator.MigrateAsync(predecessor));
        Assert.Equal(51065, refusal.Number);
        Assert.Equal(1, await PhysicalHoldTableExistsAsync(populated));
        Assert.Equal(1, await populated.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.__EFMigrationsHistory WHERE MigrationId = @migration",
            ("@migration", physicalHolds)));
        Assert.Equal(before, await PhysicalHoldRollbackTruthAsync(populated));

        var activeReplay = await PlaceAsync(populated, activeRequest, activeMetadata);
        var releaseReplay = await ReleaseAsync(populated, releaseRequest, releaseMetadata);
        Assert.True(activeReplay.Replayed);
        Assert.True(releaseReplay.Replayed);
        Assert.Equal((activeId, releasedId), (activeReplay.Hold.HoldId, releaseReplay.Hold.HoldId));
    }

    private static Task<int> PhysicalHoldTableExistsAsync(Run run) => run.ScalarAsync<int>(
        "SELECT CASE WHEN OBJECT_ID(N'inventory.PhysicalHolds', N'U') IS NULL THEN 0 ELSE 1 END");

    private static async Task<(int Holds, int Movements, int Commands, int Audits, decimal FreeKg, decimal BlockedKg)>
        PhysicalHoldRollbackTruthAsync(Run run) =>
        (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.PhysicalHolds"),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind IN (N'quality.inspection-hold', N'quality.disposition')"),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.Commands WHERE CommandName IN (N'inventory.physical-hold.place.v1', N'inventory.physical-hold.release.v1')"),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.PhysicalHold'"),
            await run.ScalarAsync<decimal>(
                "SELECT COALESCE(SUM(QuantityKg), 0) FROM inventory.Balances WHERE Block = 1"),
            await run.ScalarAsync<decimal>(
                "SELECT COALESCE(SUM(QuantityKg), 0) FROM inventory.Balances WHERE Block = 2")
        );

    private static async Task<string> GrantStockPositionReaderAsync(Run run, string developmentUser)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var link = await identity.ExternalIdentities.SingleOrDefaultAsync(candidate =>
            candidate.Issuer == BootstrapIdentityOptions.Issuer && candidate.Subject == developmentUser);
        Guid actor;
        if (link is null)
        {
            actor = Guid.NewGuid();
            identity.Users.Add(new UserAccount(actor, "SENTETIK kalite hold stok okuyucu", null));
            identity.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor,
                BootstrapIdentityOptions.Issuer, developmentUser));
        }
        else
        {
            actor = link.UserId;
        }

        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"I041-STOCK-READ-{roleId:N}",
            "SENTETIK I04.1 stok pozisyonu okuma rolü", true);
        role.AddPermission(ErpPermissions.InventoryLabelsRead);
        role.AddPermission(ErpPermissions.InventoryBalancesRead);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, CompanyA,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, CompanyA);
        identity.Roles.Add(role);
        identity.RoleAssignments.Add(assignment);
        await identity.SaveChangesAsync();
        return developmentUser;
    }

    private static async Task<MaterialLabel> SeedFreeLabelAsync(Run run, string code, decimal kilograms)
    {
        var label = NewLabel(CompanyA, code);
        var location = await run.LocationOfAsync(CompanyA);
        await using var scope = run.Store(CompanyA);
        await scope.Labels.SaveLabelAsync(label, Metadata(run));
        await scope.Movements.RecordMovementAsync(Movement(CompanyA, StockMovementKind.Receipt,
            In(Position(CompanyA, location, label, BlockState.Free), kilograms)), Metadata(run));
        return label;
    }

    private static async Task<InventoryPhysicalHoldResultV1> PlaceAsync(
        Run run, PlaceInventoryPhysicalHoldV1 request, InventoryCommandMetadata metadata)
    {
        await using var scope = run.Store(CompanyA);
        return await ((IInventoryPhysicalHoldWriterV1)scope.Store).PlacePhysicalHoldAsync(request, metadata);
    }

    private static async Task<InventoryPhysicalHoldResultV1> ReleaseAsync(
        Run run, ReleaseInventoryPhysicalHoldV1 request, InventoryCommandMetadata metadata)
    {
        await using var scope = run.Store(CompanyA);
        return await ((IInventoryPhysicalHoldWriterV1)scope.Store).ReleasePhysicalHoldAsync(request, metadata);
    }

    private static InventoryCommandMetadata HoldMetadata(Run run, string actor, string key) =>
        Metadata(run) with
        {
            ActorId = actor,
            CorrelationId = $"quality-hold-{key}-{Guid.NewGuid():N}",
            IdempotencyKey = key,
        };

    private static string Version(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
