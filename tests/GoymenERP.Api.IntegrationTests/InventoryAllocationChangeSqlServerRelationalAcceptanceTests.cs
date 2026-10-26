using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// INV-AL-1 change over real SQL (Master v3 §25.4 "revize et", AT-063; DEC-STK-003/004/005): a resize keeps the
/// allocation's identity and consumed part, a move releases the source and reserves another label of the same product
/// in the same save; neither writes a stock movement, neither can double-reserve, both keep an append-only history,
/// replay once, refuse stale versions and other companies, and bump the label version (the stock-count fence).
/// SENTETIK data only.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Allocation_resize_keeps_its_identity_frees_or_reserves_the_difference_and_replays_once()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-RESIZE", 100m);
        var create = AllocationRequest(source, "60");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(create, Metadata(run));
        }

        var movements = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");
        var labelVersion = await LabelVersionAsync(run, source.LabelId);
        var down = Metadata(run) with { IdempotencyKey = $"resize-{Guid.NewGuid():N}", CorrelationId = $"resize-{Guid.NewGuid():N}" };
        InventoryPhysicalAllocationChangeResultV1 reduced;
        await using (var scope = run.Store(CompanyA))
        {
            reduced = await scope.Store.ChangeAsync(create.AllocationId,
                ChangeRequest(create, "1", source.LabelId, labelVersion, "25", null), down);
        }

        Assert.Equal((InventoryAllocationChangeKindsV1.Resize, create.AllocationId, "2", "Active", "25", "25"),
            (reduced.Change.Kind, reduced.Change.Allocation.AllocationId, reduced.Change.Allocation.Version,
                reduced.Change.Allocation.Status, reduced.Change.Allocation.AllocatedKg, reduced.Change.Allocation.RemainingKg));
        Assert.Null(reduced.Change.Replaced);
        Assert.Equal(75m, await FreeAfterAllocationsAsync(run, source.LabelId));
        Assert.Equal(labelVersion + 1, await LabelVersionAsync(run, source.LabelId));

        // The same key and content replays with the first correlation; nothing new is written.
        var effects = await AllocationChangeEffectsAsync(run);
        await using (var scope = run.Store(CompanyA))
        {
            var replay = await scope.Store.ChangeAsync(create.AllocationId,
                ChangeRequest(create, "1", source.LabelId, labelVersion, "25", null),
                down with { CorrelationId = $"replay-{Guid.NewGuid():N}" });
            Assert.True(replay.IsReplay);
            Assert.Equal(down.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal("2", replay.Change.Allocation.Version);
        }

        await using (var scope = run.Store(CompanyA))
        {
            var other = await Assert.ThrowsAsync<InventoryAllocationException>(() => scope.Store.ChangeAsync(
                create.AllocationId, ChangeRequest(create, "1", source.LabelId, labelVersion, "24", null), down));
            Assert.Equal((409, InventoryAllocationCodesV1.IdempotencyConflict), (other.Status, other.Code));
        }

        Assert.Equal(effects, await AllocationChangeEffectsAsync(run));

        // An increase is a new reservation part: it must fit the unreserved free kg of the same position.
        var another = AllocationRequest(source, "70") with { ExpectedLabelVersion = (labelVersion + 1).ToString() };
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(another, Metadata(run));
        }

        var current = await LabelVersionAsync(run, source.LabelId);
        await using (var scope = run.Store(CompanyA))
        {
            var over = await Assert.ThrowsAsync<InventoryAllocationException>(() => scope.Store.ChangeAsync(
                create.AllocationId, ChangeRequest(create, "2", source.LabelId, current, "30.001", null), Metadata(run)));
            Assert.Equal((409, InventoryAllocationCodesV1.InsufficientFreeStock), (over.Status, over.Code));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var increased = await scope.Store.ChangeAsync(create.AllocationId,
                ChangeRequest(create, "2", source.LabelId, current, "30", null), Metadata(run));
            Assert.Equal(("3", "30", "30"), (increased.Change.Allocation.Version,
                increased.Change.Allocation.AllocatedKg, increased.Change.Allocation.RemainingKg));
        }

        Assert.Equal(0m, await FreeAfterAllocationsAsync(run, source.LabelId));
        Assert.Equal(movements, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.AllocationChanges WHERE SourceAllocationId = @id AND TargetAllocationId = @id AND Kind = 1",
            ("@id", create.AllocationId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.AllocationChanges WHERE SourceAllocationId = @id AND SourceVersion = 1 "
            + "AND SourceRemainingKg = 60 AND TargetRemainingKg = 25", ("@id", create.AllocationId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockAllocation' AND EntityId = @id "
            + "AND EventType = N'inventory.physical-allocation.resized.v1' AND SourceChannel = N'Web'",
            ("@id", create.AllocationId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Active_quality_hold_refuses_allocation_change_without_history_or_balance_effect_on_real_SQL()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-HOLD-CHANGE", 30m);
        var create = AllocationRequest(source, "30");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(create, Metadata(run));
        }
        await PlaceAsync(run,
            new(Guid.NewGuid(), source.LabelId, Version(await LabelVersionAsync(run, source.LabelId)), Guid.NewGuid(),
                "SENTETIK tahsis değişiklik engeli", null),
            HoldMetadata(run, "allocation-change-hold-maker", "allocation-change-hold-place"));

        var labelVersion = await LabelVersionAsync(run, source.LabelId);
        var before = (
            Effects: await AllocationChangeEffectsAsync(run),
            Movements: await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"),
            Allocation: await run.ScalarAsync<string>(
                "SELECT CONCAT(Version, N'|', Status, N'|', RemainingKg) FROM inventory.Allocations WHERE Id = @id",
                ("@id", create.AllocationId)),
            LabelVersion: labelVersion);

        await using (var scope = run.Store(CompanyA))
        {
            var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() => scope.Store.ChangeAsync(
                create.AllocationId,
                ChangeRequest(create, "1", source.LabelId, labelVersion, "20", null), Metadata(run)));
            Assert.Equal((409, InventoryAllocationCodesV1.AllocationStateConflict),
                (refused.Status, refused.Code));
        }

        var after = (
            Effects: await AllocationChangeEffectsAsync(run),
            Movements: await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"),
            Allocation: await run.ScalarAsync<string>(
                "SELECT CONCAT(Version, N'|', Status, N'|', RemainingKg) FROM inventory.Allocations WHERE Id = @id",
                ("@id", create.AllocationId)),
            LabelVersion: await LabelVersionAsync(run, source.LabelId));
        Assert.Equal(before, after);
    }

    [SqlServerFact]
    public async Task Allocation_move_releases_the_source_and_reserves_the_target_in_one_save_with_history()
    {
        await using var run = await Run.StartAsync();
        var from = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-MOVE-FROM", 100m);
        var to = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-MOVE-TO", 50m);
        var create = AllocationRequest(from, "40");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(create, Metadata(run));
        }

        var movements = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");
        var fromVersion = await LabelVersionAsync(run, from.LabelId);
        var toVersion = await LabelVersionAsync(run, to.LabelId);
        var newId = Guid.NewGuid();

        // Too much for the target: nothing changes on either label.
        await using (var scope = run.Store(CompanyA))
        {
            var over = await Assert.ThrowsAsync<InventoryAllocationException>(() => scope.Store.ChangeAsync(
                create.AllocationId, ChangeRequest(create, "1", to.LabelId, toVersion, "50.001", newId), Metadata(run)));
            Assert.Equal((409, InventoryAllocationCodesV1.InsufficientFreeStock), (over.Status, over.Code));
        }

        Assert.Equal((fromVersion, toVersion), (await LabelVersionAsync(run, from.LabelId), await LabelVersionAsync(run, to.LabelId)));
        Assert.Equal(60m, await FreeAfterAllocationsAsync(run, from.LabelId));

        InventoryPhysicalAllocationChangeResultV1 moved;
        await using (var scope = run.Store(CompanyA))
        {
            moved = await scope.Store.ChangeAsync(create.AllocationId,
                ChangeRequest(create, "1", to.LabelId, toVersion, "35", newId), Metadata(run));
        }

        Assert.Equal(InventoryAllocationChangeKindsV1.Move, moved.Change.Kind);
        Assert.Equal((create.AllocationId, "2", "Released", "0"),
            (moved.Change.Replaced!.AllocationId, moved.Change.Replaced.Version, moved.Change.Replaced.Status,
                moved.Change.Replaced.RemainingKg));
        Assert.Equal((newId, to.LabelId, "1", "Active", "35", create.SalesOrderId, create.SalesOrderLineId),
            (moved.Change.Allocation.AllocationId, moved.Change.Allocation.LabelId, moved.Change.Allocation.Version,
                moved.Change.Allocation.Status, moved.Change.Allocation.RemainingKg, moved.Change.Allocation.DemandId,
                moved.Change.Allocation.DemandLineId));
        Assert.Equal(100m, await FreeAfterAllocationsAsync(run, from.LabelId));
        Assert.Equal(15m, await FreeAfterAllocationsAsync(run, to.LabelId));
        Assert.Equal((fromVersion + 1, toVersion + 1),
            (await LabelVersionAsync(run, from.LabelId), await LabelVersionAsync(run, to.LabelId)));
        Assert.Equal(movements, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE Id = @id AND Status = 3 AND ReleaseReason IS NOT NULL",
            ("@id", create.AllocationId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.AllocationChanges WHERE Kind = 2 AND SourceAllocationId = @from "
            + "AND TargetAllocationId = @to AND SourceRemainingKg = 40 AND TargetRemainingKg = 35",
            ("@from", create.AllocationId), ("@to", newId)));

        // The released source is not active: a second change of it is a state conflict and writes nothing.
        var effects = await AllocationChangeEffectsAsync(run);
        await using (var scope = run.Store(CompanyA))
        {
            var closed = await Assert.ThrowsAsync<InventoryAllocationException>(() => scope.Store.ChangeAsync(
                create.AllocationId, ChangeRequest(create, "2", from.LabelId, fromVersion + 1, "10", null), Metadata(run)));
            Assert.Equal((409, InventoryAllocationCodesV1.AllocationStateConflict), (closed.Status, closed.Code));
        }

        Assert.Equal(effects, await AllocationChangeEffectsAsync(run));

        // Both labels' reads show the history: the source's released row names the move, the target's new row too.
        await using var context = run.Context(CompanyA);
        var reads = PhysicalAllocationReads(context, CompanyA);
        var fromRead = await reads.ReadLabelAsync(from.LabelId);
        var released = Assert.Single(fromRead.Items);
        Assert.Equal(("Released", InventoryAllocationChangeKindsV1.Move, newId),
            (released.Allocation.Status, Assert.Single(released.Changes).Kind, released.Changes[0].TargetAllocationId));
        Assert.NotNull(released.ReleaseReason);
        var toRead = await reads.ReadLabelAsync(to.LabelId);
        var active = Assert.Single(toRead.Items);
        Assert.Equal((newId, create.AllocationId, (toVersion + 1).ToString()),
            (active.Allocation.AllocationId, Assert.Single(active.Changes).SourceAllocationId, toRead.LabelVersion));

        // The Sales order's view (AT-024 / cancellation preview) reads one active allocation holding 35 kg.
        var company = new CompanyContext();
        company.Set(CompanyA);
        var order = await new InventorySalesOrderAllocationReadPort(context, company)
            .ReadOrderAllocationsAsync(create.SalesOrderId);
        var line = Assert.Single(order.Lines);
        Assert.Equal((create.SalesOrderLineId, 1, 35m), (line.OrderLineId, line.ActiveCount, line.ActiveRemainingKg));
    }

    [SqlServerFact]
    public async Task Allocation_change_refuses_stale_foreign_mismatched_and_other_party_targets_without_effect()
    {
        await using var run = await Run.StartAsync();
        var own = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-CHG-OWN", 100m);
        var foreign = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-ALLOC-CHG-FOREIGN", 100m);
        var customerOwned = await SeedOtherPartyFreeSourceAsync(run, "SYN-ALLOC-CHG-CUSTOMER",
            new StockOwner(StockPartyKind.Counterparty, Guid.NewGuid()), new StockCustodian(StockPartyKind.Company, CompanyA));
        var create = AllocationRequest(own, "40");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(create, Metadata(run));
        }

        var foreignCreate = AllocationRequest(foreign, "10");
        await using (var scope = run.Store(CompanyB))
        {
            await scope.Store.CreateAsync(foreignCreate, Metadata(run));
        }

        var version = await LabelVersionAsync(run, own.LabelId);
        var before = await AllocationChangeEffectsAsync(run);
        var labelsBefore = (await LabelVersionAsync(run, own.LabelId), await LabelVersionAsync(run, customerOwned.LabelId));
        var cases = new (Func<InventoryStore, Task> Act, int Status, string Code)[]
        {
            (store => store.ChangeAsync(create.AllocationId,
                ChangeRequest(create, "2", own.LabelId, version, "30", null), Metadata(run)),
                409, InventoryAllocationCodesV1.AllocationVersionConflict),
            (store => store.ChangeAsync(create.AllocationId,
                ChangeRequest(create, "1", own.LabelId, version - 1, "30", null), Metadata(run)),
                409, InventoryAllocationCodesV1.LabelVersionConflict),
            (store => store.ChangeAsync(create.AllocationId,
                ChangeRequest(create, "1", own.LabelId, version, "40", null), Metadata(run)),
                422, InventoryAllocationCodesV1.ChangeUnchanged),
            (store => store.ChangeAsync(create.AllocationId,
                ChangeRequest(create, "1", own.LabelId, version, "30", null) with { SalesOrderLineId = Guid.NewGuid() },
                Metadata(run)),
                422, InventoryAllocationCodesV1.InvalidReference),
            (store => store.ChangeAsync(create.AllocationId,
                ChangeRequest(create, "1", own.LabelId, version, "30", Guid.NewGuid()), Metadata(run)),
                400, InventoryAllocationCodesV1.ValidationFailed),
            (store => store.ChangeAsync(create.AllocationId,
                ChangeRequest(create, "1", customerOwned.LabelId, customerOwned.LabelVersion, "5", Guid.NewGuid()),
                Metadata(run)),
                422, InventoryAllocationCodesV1.OwnerNotCompany),
            // Another company's allocation and another company's label are unknown here (404), never a 403.
            (store => store.ChangeAsync(foreignCreate.AllocationId,
                ChangeRequest(foreignCreate, "1", foreign.LabelId, foreign.LabelVersion, "5", null), Metadata(run)),
                404, InventoryAllocationCodesV1.InvalidReference),
            (store => store.ChangeAsync(create.AllocationId,
                ChangeRequest(create, "1", foreign.LabelId, foreign.LabelVersion, "5", Guid.NewGuid()), Metadata(run)),
                404, InventoryAllocationCodesV1.InvalidReference),
        };

        foreach (var (act, status, code) in cases)
        {
            await using var scope = run.Store(CompanyA);
            var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() => act(scope.Store));
            Assert.Equal((status, code), (refused.Status, refused.Code));
        }

        Assert.Equal(before, await AllocationChangeEffectsAsync(run));
        Assert.Equal(labelsBefore, (await LabelVersionAsync(run, own.LabelId), await LabelVersionAsync(run, customerOwned.LabelId)));
        Assert.Equal(60m, await FreeAfterAllocationsAsync(run, own.LabelId));
    }

    [SqlServerFact]
    public async Task Concurrent_change_and_create_cannot_both_reserve_the_same_free_kilograms()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-CHG-RACE", 100m);
        var create = AllocationRequest(source, "50");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(create, Metadata(run));
        }

        var version = await LabelVersionAsync(run, source.LabelId);
        var barrier = new Barrier(2);

        async Task<object> ActAsync(bool change)
        {
            await using var scope = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return change
                    ? await scope.Store.ChangeAsync(create.AllocationId,
                        ChangeRequest(create, "1", source.LabelId, version, "90", null), Metadata(run))
                    : await scope.Store.CreateAsync(AllocationRequest(source, "40") with
                    {
                        ExpectedLabelVersion = version.ToString(),
                    }, Metadata(run));
            }
            catch (InventoryAllocationException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => ActAsync(true)), Task.Run(() => ActAsync(false)));

        Assert.Single(results, result => result is string code && code == InventoryAllocationCodesV1.ConcurrentChange);
        Assert.True(await FreeAfterAllocationsAsync(run, source.LabelId) >= 0m);
        Assert.Equal(version + 1, await LabelVersionAsync(run, source.LabelId));
    }

    [SqlServerFact]
    public async Task Allocation_change_makes_a_pending_stock_count_stale_like_any_other_label_change()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-CHG-COUNT", 100m);
        var create = AllocationRequest(source, "40");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(create, Metadata(run));
        }

        var version = await LabelVersionAsync(run, source.LabelId);
        var count = new RecordInventoryStockCountV1(Guid.NewGuid(), source.LabelId, version.ToString(), "99",
            At.AddHours(1), "Sentetik sayım farkı.");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockCountAsync(count, Metadata(run), static _ => Task.CompletedTask);
        }

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.ChangeAsync(create.AllocationId,
                ChangeRequest(create, "1", source.LabelId, version, "30", null), Metadata(run));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var stale = await Assert.ThrowsAsync<InventoryStockCountException>(() => scope.Store.DecideStockCountAsync(
                count.CountId, new DecideInventoryStockCountV1(InventoryStockCountDecisionKindV1.Approve, "Sentetik onay."),
                Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") }, static _ => Task.CompletedTask));
            Assert.Equal((409, InventoryStockCountCodesV1.Stale), (stale.Status, stale.Code));
        }

        Assert.Equal(100m, await run.ScalarAsync<decimal>(
            "SELECT SUM(QuantityKg) FROM inventory.Balances WHERE LabelId = @id", ("@id", source.LabelId)));
    }

    [SqlServerFact]
    public async Task Allocation_change_HTTP_is_fenced_by_the_approved_sales_line_and_audited_as_web()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-CHG-HTTP", 100m);
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var allocationId = Guid.NewGuid();
        var actor = await GrantAllocationAsync(run, TestUsers.CompanyA, CompanyA,
            ErpPermissions.InventoryPhysicalAllocationWrite);
        var line = new FixedSalesLine(new SalesOrderLineReferenceAnswerV1(
            SalesOrderLineReferenceOutcomeV1.Approved,
            new SalesOrderLineReferenceV1(CompanyA, orderId, lineId, 1, Guid.NewGuid(), Guid.NewGuid(),
                source.ProductId, "7", Guid.NewGuid(), "10", 0, 4),
            null));
        await using var host = run.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesOrderLineReferencePortV1>();
            services.AddSingleton<ISalesOrderLineReferencePortV1>(line);
        }));

        using var client = ClientFor(host, actor, CompanyA);
        using (var createRequest = AllocationPost(allocationId, orderId, lineId, source, "SYN-ALLOC-CHG-HTTP-create"))
        using (var created = await client.SendAsync(createRequest))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var labelVersion = await LabelVersionAsync(run, source.LabelId);
        HttpRequestMessage ChangePost(string key, string quantity, string expectedVersion = "1") =>
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/physical-allocations/{allocationId:D}/change")
            {
                Content = JsonContent.Create(new ChangeInventoryPhysicalAllocationRequestV1(expectedVersion, orderId, lineId,
                    source.LabelId, labelVersion.ToString(), quantity, "SENTETIK tahsis revize", null)),
                Headers = { { "Idempotency-Key", key } },
            };

        using (var denied = ClientFor(host, TestUsers.CompanyAViewer, CompanyA))
        using (var request = ChangePost("SYN-ALLOC-CHG-HTTP-denied", "20"))
        using (var response = await denied.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (var request = ChangePost("SYN-ALLOC-CHG-HTTP-1", "20"))
        using (var response = await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var change = (await response.Content.ReadFromJsonAsync<InventoryPhysicalAllocationChangeViewV1>())!;
            Assert.Equal(("Resize", allocationId, "2", "20"),
                (change.Kind, change.Allocation.AllocationId, change.Allocation.Version, change.Allocation.RemainingKg));
        }

        using (var request = ChangePost("SYN-ALLOC-CHG-HTTP-1", "20"))
        using (var response = await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        }

        using (var request = ChangePost("SYN-ALLOC-CHG-HTTP-stale", "15"))
        using (var response = await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains(InventoryAllocationCodesV1.AllocationVersionConflict, await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        // The order is cancelled (the Sales owner answers NotApproved): a change is 422, a full release stays available.
        line.Answer = new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.NotApproved, null, "Cancelled");
        using (var request = ChangePost("SYN-ALLOC-CHG-HTTP-cancelled", "15", "2"))
        using (var response = await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }

        using (var label = await client.GetAsync($"/api/v1/inventory/labels/{source.LabelId:D}/physical-allocations"))
        {
            Assert.Equal(HttpStatusCode.OK, label.StatusCode);
            var read = (await label.Content.ReadFromJsonAsync<InventoryLabelPhysicalAllocationsV1>())!;
            var item = Assert.Single(read.Items);
            Assert.Equal(("2", "20", "60", "20"), (item.Allocation.Version, item.Allocation.RemainingKg,
                item.Changes.Single().SourceRemainingKg, item.Changes.Single().TargetRemainingKg));
        }

        using (var unknown = await client.GetAsync($"/api/v1/inventory/labels/{Guid.NewGuid():D}/physical-allocations"))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.AllocationChanges"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'inventory.physical-allocation.resized.v1' AND SourceChannel = N'Web'",
            ("@id", allocationId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Allocation_change_migration_rolls_back_when_empty_and_blocks_after_a_change()
    {
        const string previousMigration = "20260927150554_AddInventoryStockCounts";
        const string currentMigration = "20260928120614_AddInventoryAllocationChanges";
        await using var run = await Run.StartAsync();
        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'inventory') AND name = N'AllocationChanges'"));
            await context.GetService<IMigrator>().MigrateAsync(currentMigration);
            await context.Database.MigrateAsync();
        }

        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-CHG-DOWN", 10m);
        var create = AllocationRequest(source, "5");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(create, Metadata(run));
        }

        var version = await LabelVersionAsync(run, source.LabelId);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.ChangeAsync(create.AllocationId,
                ChangeRequest(create, "1", source.LabelId, version, "4", null), Metadata(run));
        }

        await RemoveSyntheticProductVersionsForHistoricalMigrationAsync(
            run, "SYN-ALLOC-CHG-DOWN", expectedLabels: 1);

        await using (var context = run.Context(CompanyA))
        {
            var rejected = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(previousMigration));
            Assert.Equal(51053, rejected.Number);
            Assert.Equal(currentMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.AllocationChanges"));
    }

    [SqlServerFact]
    public async Task Allocation_change_history_rows_are_constrained_by_SQL()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-CHG-CK", 10m);
        var create = AllocationRequest(source, "5");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(create, Metadata(run));
        }

        // A resize whose source and target differ, or which changes nothing, is refused by CK_AllocationChanges_Kind.
        foreach (var (target, targetKg) in new[] { ("NEWID()", "4"), ("@id", "5") })
        {
            var rejected = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(run,
                $"""
                INSERT INTO inventory.AllocationChanges
                    (Id, CompanyId, Kind, SourceAllocationId, SourceVersion, SourceLabelId, SourceRemainingKg,
                     TargetAllocationId, TargetLabelId, TargetRemainingKg, Reason, ChangedBy, CorrelationId, ChangedAtUtc)
                VALUES (NEWID(), @company, 1, @id, 1, @label, 5, {target}, @label, {targetKg}, N'r', N'a', N'c', SYSUTCDATETIME());
                """, ("@company", CompanyA), ("@id", create.AllocationId), ("@label", source.LabelId)));
            Assert.Equal(547, rejected.Number);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.AllocationChanges"));
    }

    private static ChangePhysicalSalesAllocationV1 ChangeRequest(
        CreatePhysicalSalesAllocationV1 create,
        string expectedAllocationVersion,
        Guid targetLabelId,
        long targetLabelVersion,
        string quantityKg,
        Guid? newAllocationId) => new(
        expectedAllocationVersion,
        create.SalesOrderId,
        create.SalesOrderLineId,
        create.ProductId,
        create.ProductVersion,
        targetLabelId,
        targetLabelVersion.ToString(),
        quantityKg,
        "Sipariş ihtiyacı değişti (SENTETIK).",
        newAllocationId);

    private static async Task<(int Allocations, int Changes, int Commands, int Audits, decimal Active)> AllocationChangeEffectsAsync(
        Run run) =>
        (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.AllocationChanges"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE CommandName LIKE N'inventory.physical-allocation.%'"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockAllocation'"),
            await run.ScalarAsync<decimal>("SELECT COALESCE(SUM(RemainingKg), 0) FROM inventory.Allocations WHERE Status = 1")
        );

    private sealed class FixedSalesLine(SalesOrderLineReferenceAnswerV1 answer) : ISalesOrderLineReferencePortV1
    {
        public SalesOrderLineReferenceAnswerV1 Answer { get; set; } = answer;

        public Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineAsync(
            Guid orderId,
            Guid lineId,
            CancellationToken cancellationToken = default) => Task.FromResult(Answer);

        public Task<SalesApprovedLinePageV1> ListApprovedLinesAsync(
            int pageSize,
            string? cursor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
