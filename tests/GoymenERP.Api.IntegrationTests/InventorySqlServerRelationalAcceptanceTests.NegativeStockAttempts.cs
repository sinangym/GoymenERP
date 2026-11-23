using GoymenERP.Api.InventoryRuntime;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real SQL owner transactions and Audit. Authority loss/failure injection is explicitly synthetic. NOT_RUN.</summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Physical_refusal_survives_its_owner_transaction_rollback_once_without_a_movement_or_command_terminal()
    {
        await using var run = await Run.StartAsync();
        var failed = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-ROLLBACK");
        await using var reads = run.Context(CompanyA);
        var service = NegativeAttemptService(run, reads);
        await service.RecordAfterRollbackAsync(failed.Refusal);
        await service.RecordAfterRollbackAsync(failed.Refusal);
        var page = await service.SearchAsync(new(OpenOnly: true));
        var item = Assert.Single(page.Items);
        Assert.Equal((failed.Movement.Id, failed.LabelId, "120", "100", "20"),
            (item.MovementId, item.LabelId, item.RequestedKg, item.AvailableKg, item.MissingKg));
        Assert.Null(item.Resolution);
        Assert.Equal(100m, await LabelKgAsync(run, failed.LabelId));
        Assert.False(await reads.Movements.AnyAsync(row => row.Id == failed.Movement.Id));
        Assert.False(await reads.Commands.AnyAsync(row => row.TargetId == failed.Movement.Id));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'inventory.negative-stock-attempt.recorded.v1'"));
        Assert.Equal(1, await reads.NegativeStockAttempts.CountAsync());
    }

    [SqlServerFact]
    public async Task Audit_failure_rolls_back_fresh_attempt_record_while_the_original_physical_refusal_stays_rolled_back()
    {
        await using var run = await Run.StartAsync();
        var failed = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-AUDIT-FAULT");
        await using var reads = run.Context(CompanyA);
        var factory = new NegativeAuditFaultScopeFactory(run.Factory.Services.GetRequiredService<IServiceScopeFactory>());
        var service = NegativeAttemptService(run, reads, factory: factory);
        var failure = await Assert.ThrowsAsync<InventoryNegativeStockAttemptException>(() => service.RecordAfterRollbackAsync(failed.Refusal));
        Assert.Equal(503, failure.StatusCode);
        Assert.Equal(0, await reads.NegativeStockAttempts.CountAsync());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'inventory.negative-stock-attempt.recorded.v1'"));
        Assert.Equal(100m, await LabelKgAsync(run, failed.LabelId));
        Assert.False(await reads.Movements.AnyAsync(row => row.Id == failed.Movement.Id));
    }

    [SqlServerFact]
    public async Task Fresh_authority_loss_after_real_attempt_and_audit_writes_rolls_back_both()
    {
        await using var run = await Run.StartAsync();
        var failed = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-PERMISSION-LOSS");
        await using var reads = run.Context(CompanyA);
        var fence = new NegativeAttemptAuthority { DenyAtCall = 3 };
        var service = NegativeAttemptService(run, reads, authority: fence);
        var failure = await Assert.ThrowsAsync<InventoryNegativeStockAttemptException>(() => service.RecordAfterRollbackAsync(failed.Refusal));
        Assert.Equal(403, failure.StatusCode);
        Assert.Equal(3, fence.Calls);
        Assert.Equal(0, await reads.NegativeStockAttempts.CountAsync());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'inventory.negative-stock-attempt.recorded.v1'"));
    }

    [SqlServerFact]
    public async Task Reservation_only_insufficiency_never_creates_a_negative_physical_attempt()
    {
        await using var run = await Run.StartAsync();
        await using var reads = run.Context(CompanyA);
        var service = NegativeAttemptService(run, reads);
        var reservation = InventoryManufacturingRealizationException.Refused(409,
            InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient,
            new InventoryManufacturingStockShortfallV1(StockMovementKind.Issue, "manufacturing.input-issue",
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Product, await run.LocationOfAsync(CompanyA),
                Kilograms.Positive(30m), Kilograms.Of(100m), Kilograms.Of(80m)));
        await service.RecordAfterRollbackAsync(reservation);
        Assert.Equal(0, await reads.NegativeStockAttempts.CountAsync());
        Assert.Equal(0, await reads.NegativeStockAttemptResolutions.CountAsync());
    }

    [SqlServerFact]
    public async Task Approved_count_resolves_all_open_labels_of_the_same_product_and_location_atomically_and_allows_the_original_exit_retry()
    {
        await using var run = await Run.StartAsync();
        var first = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-COUNT-ONE");
        var second = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-COUNT-TWO");
        await using var reads = run.Context(CompanyA);
        var service = NegativeAttemptService(run, reads);
        await service.RecordAfterRollbackAsync(first.Refusal);
        await service.RecordAfterRollbackAsync(second.Refusal);
        var countId = Guid.NewGuid();
        await using (var count = run.Store(CompanyA))
            await count.Store.RecordStockCountAsync(new(countId, first.LabelId, "2", "150", At.AddHours(2),
                "Sentetik onaylı yeniden tartım."), Metadata(run), static _ => Task.CompletedTask);
        var approver = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
        var approval = new DecideInventoryStockCountV1(InventoryStockCountDecisionKindV1.Approve, "Sayım farkı doğrulandı.");
        // A final authority fault after SaveChanges must undo BOTH the count adjustment and every resolution.
        await using (var count = run.Store(CompanyA))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => count.Store.DecideStockCountAsync(countId,
                approval, approver, _ =>
                {
                    if (count.DbContext.ChangeTracker.Entries<StockCountDecisionRow>()
                        .Any(entry => entry.State == EntityState.Unchanged)) throw new UnauthorizedAccessException("synthetic final fence");
                    return Task.CompletedTask;
                }));
        }
        Assert.Equal(2, (await service.SearchAsync(new(OpenOnly: true))).Items.Count);
        Assert.Equal(100m, await LabelKgAsync(run, first.LabelId));
        await using (var count = run.Store(CompanyA))
            await count.Store.DecideStockCountAsync(countId, approval, approver, static _ => Task.CompletedTask);
        Assert.Empty((await service.SearchAsync(new(OpenOnly: true))).Items);
        var history = await service.SearchAsync(new());
        Assert.Equal(2, history.Items.Count);
        Assert.All(history.Items, item => Assert.Equal(countId, item.Resolution!.StockCountId));
        Assert.Equal(2, await reads.NegativeStockAttempts.CountAsync());
        // The attempt ledger is not a terminal refusal for the physical id/key. New proved stock permits a retry.
        await using (var physical = run.Store(CompanyA))
            Assert.Equal(InventoryWriteOutcome.Recorded,
                await physical.Movements.RecordMovementAsync(first.Movement, first.Metadata));
        Assert.Equal(30m, await LabelKgAsync(run, first.LabelId));
        Assert.Equal(2, await reads.NegativeStockAttempts.CountAsync());
        var later = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-AFTER-COUNT");
        await service.RecordAfterRollbackAsync(later.Refusal);
        var openLater = Assert.Single((await service.SearchAsync(new(OpenOnly: true))).Items);
        Assert.Equal(later.LabelId, openLater.LabelId);
        Assert.Null(openLater.Resolution);
    }

    [SqlServerFact]
    public async Task Rejected_and_no_difference_counts_resolve_nothing_and_another_company_cannot_read_attempts()
    {
        await using var run = await Run.StartAsync();
        var failed = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-REJECT");
        await using var reads = run.Context(CompanyA);
        var service = NegativeAttemptService(run, reads);
        await service.RecordAfterRollbackAsync(failed.Refusal);
        var approver = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
        await using (var count = run.Store(CompanyA))
        {
            var equal = new RecordInventoryStockCountV1(Guid.NewGuid(), failed.LabelId, "2", "100", At.AddHours(1), "Farksız sayım.");
            await count.Store.RecordStockCountAsync(equal, Metadata(run), static _ => Task.CompletedTask);
            var difference = new RecordInventoryStockCountV1(Guid.NewGuid(), failed.LabelId, "2", "105", At.AddHours(2), "Ret edilen sayım.");
            await count.Store.RecordStockCountAsync(difference, Metadata(run), static _ => Task.CompletedTask);
            await count.Store.DecideStockCountAsync(difference.CountId,
                new(InventoryStockCountDecisionKindV1.Reject, "Kanıt yetersiz."), approver, static _ => Task.CompletedTask);
        }
        Assert.Single((await service.SearchAsync(new(OpenOnly: true))).Items);
        var company = new CompanyContext(); company.Set(CompanyB);
        await using var otherReads = run.Context(CompanyB);
        var other = new InventoryNegativeStockAttempts(company, new FixedCurrentUser(run.Actor, CompanyB),
            new NegativeAttemptAuthority(), run.Factory.Services.GetRequiredService<IServiceScopeFactory>(), otherReads);
        Assert.Empty((await other.SearchAsync(new())).Items);
    }

    [SqlServerFact]
    public async Task Concurrent_same_key_SQL_insert_lock_race_keeps_one_immutable_attempt_and_a_retry_reuses_it()
    {
        await using var run = await Run.StartAsync();
        var failed = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-INSERT-RACE");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task Gate(int call)
        {
            if (call != 1) return; // Both enter before the real SQL guard; no barrier waits while holding that lock.
            if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        async Task<bool> RecordAsync()
        {
            await using var reads = run.Context(CompanyA);
            var service = NegativeAttemptService(run, reads, authority: new NegativeAttemptAuthority { OnCall = Gate });
            try { await service.RecordAfterRollbackAsync(failed.Refusal); return true; }
            catch (InventoryNegativeStockAttemptException exception) when (exception.StatusCode == 503) { return false; }
        }
        var outcomes = await Task.WhenAll(RecordAsync(), RecordAsync()).WaitAsync(TimeSpan.FromSeconds(45));
        Assert.Contains(true, outcomes); // SQL may choose a deadlock victim; the failed recording is never called success.
        await using var final = run.Context(CompanyA);
        await NegativeAttemptService(run, final).RecordAfterRollbackAsync(failed.Refusal);
        Assert.Equal(1, await final.NegativeStockAttempts.CountAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'inventory.negative-stock-attempt.recorded.v1'"));
        Assert.False(await final.Movements.AnyAsync(row => row.Id == failed.Movement.Id));
    }

    [SqlServerFact]
    public async Task Approved_matching_count_in_the_rollback_to_insert_gap_resolves_the_original_refusal_but_not_a_later_one()
    {
        await using var run = await Run.StartAsync();
        var failed = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-GAP-REFUSAL");
        Assert.Equal(0L, InventoryNegativeStockAttemptEvidenceException.FindEvidence(failed.Refusal)!.ObservedCountBoundaryVersion);
        // A different label of the SAME actual product/location is a valid SAT-D339 corrective count.
        var counted = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-NEG-GAP-COUNT", 100m);
        var countId = Guid.NewGuid();
        await using (var count = run.Store(CompanyA))
        {
            await count.Store.RecordStockCountAsync(new(countId, counted.LabelId, "2", "150", At.AddHours(2),
                "Aynı ürün ve depoda ikinci kişi sayımı."), Metadata(run), static _ => Task.CompletedTask);
            await count.Store.DecideStockCountAsync(countId,
                new(InventoryStockCountDecisionKindV1.Approve, "Sayım farkı doğrulandı."),
                Metadata(run) with { ActorId = "synthetic-count-checker" }, static _ => Task.CompletedTask);
        }
        await using var reads = run.Context(CompanyA);
        Assert.Empty(await reads.NegativeStockAttempts.ToArrayAsync()); // Approval happened before the independent insert.
        var service = NegativeAttemptService(run, reads);
        await service.RecordAfterRollbackAsync(failed.Refusal);
        var item = Assert.Single((await service.SearchAsync(new())).Items);
        Assert.Equal(countId, item.Resolution!.StockCountId);
        Assert.Equal(0L, (await reads.NegativeStockAttempts.SingleAsync()).ObservedCountBoundaryVersion);
        Assert.Equal(1L, (await reads.NegativeStockAttemptResolutions.SingleAsync()).CountBoundaryVersion);
        Assert.False(await reads.Movements.AnyAsync(row => row.Id == failed.Movement.Id));
        Assert.Equal(100m, await LabelKgAsync(run, failed.LabelId)); // Resolution never makes the refused physical movement succeed.
        var after = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-GAP-AFTER-COUNT");
        Assert.Equal(1L, InventoryNegativeStockAttemptEvidenceException.FindEvidence(after.Refusal)!.ObservedCountBoundaryVersion);
        await service.RecordAfterRollbackAsync(after.Refusal);
        var open = Assert.Single((await service.SearchAsync(new(OpenOnly: true))).Items);
        Assert.Equal(after.Movement.Id, open.MovementId);
        Assert.Null(open.Resolution); // The older count is not a corrective count for a newer physical failure.
    }

    [SqlServerFact]
    public async Task Rolled_back_approved_count_never_supplies_a_later_boundary_for_a_delayed_refusal_insert()
    {
        await using var run = await Run.StartAsync();
        var failed = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-GAP-COUNT-ROLLBACK");
        var countId = Guid.NewGuid();
        await using (var count = run.Store(CompanyA))
        {
            await count.Store.RecordStockCountAsync(new(countId, failed.LabelId, "2", "150", At.AddHours(2),
                "İkinci kişi sayımı."), Metadata(run), static _ => Task.CompletedTask);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => count.Store.DecideStockCountAsync(countId,
                new(InventoryStockCountDecisionKindV1.Approve, "Sayım farkı doğrulandı."),
                Metadata(run) with { ActorId = "synthetic-count-checker" }, _ =>
                {
                    if (count.DbContext.ChangeTracker.Entries<StockCountDecisionRow>()
                        .Any(entry => entry.State == EntityState.Unchanged))
                        throw new UnauthorizedAccessException("synthetic final authority loss");
                    return Task.CompletedTask;
                }));
        }
        await using var reads = run.Context(CompanyA);
        Assert.Empty(await reads.NegativeStockCountBoundaries.ToArrayAsync());
        await NegativeAttemptService(run, reads).RecordAfterRollbackAsync(failed.Refusal);
        Assert.Null(Assert.Single((await NegativeAttemptService(run, reads).SearchAsync(new(OpenOnly: true))).Items).Resolution);
        Assert.Empty(await reads.NegativeStockAttemptResolutions.ToArrayAsync());
        Assert.Equal(100m, await LabelKgAsync(run, failed.LabelId));
    }

    [SqlServerFact]
    public async Task Physical_validation_keeps_the_SQL_count_guard_locked_until_rollback_then_the_delayed_attempt_uses_the_real_count()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-NEG-GUARD-REFUSAL", 100m);
        var counted = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-NEG-GUARD-COUNT", 100m);
        var countId = Guid.NewGuid();
        await using (var count = run.Store(CompanyA))
            await count.Store.RecordStockCountAsync(new(countId, counted.LabelId, "2", "150", At.AddHours(2),
                "Aynı ürün ve depo sayımı."), Metadata(run), static _ => Task.CompletedTask);
        InventoryRuleException refusal;
        await using (var physical = run.Store(CompanyA))
        {
            var label = (await physical.Labels.FindLabelAsync(source.LabelId))!;
            var movement = Movement(CompanyA, StockMovementKind.Issue,
                Out(Position(CompanyA, await run.LocationOfAsync(CompanyA), label, BlockState.Free), 120m));
            await using var transaction = await physical.DbContext.Database.BeginTransactionAsync();
            refusal = await Assert.ThrowsAsync<InventoryRuleException>(() =>
                physical.Movements.RecordMovementAsync(movement, Metadata(run)));
            // A separate actual SQL command cannot advance the same mutex while validation's TX is still alive.
            var blocked = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
                "SET LOCK_TIMEOUT 200; UPDATE inventory.NegativeStockValidationGuards SET Version = Version + 1 WHERE CompanyId = @c; SELECT 1;",
                ("@c", CompanyA)));
            Assert.Equal(1222, blocked.Number);
            await transaction.RollbackAsync();
        }
        await using (var count = run.Store(CompanyA))
            await count.Store.DecideStockCountAsync(countId,
                new(InventoryStockCountDecisionKindV1.Approve, "Sayım farkı doğrulandı."),
                Metadata(run) with { ActorId = "synthetic-count-checker" }, static _ => Task.CompletedTask);
        await using var reads = run.Context(CompanyA);
        var service = NegativeAttemptService(run, reads);
        await service.RecordAfterRollbackAsync(refusal);
        Assert.Equal(countId, Assert.Single((await service.SearchAsync(new())).Items).Resolution!.StockCountId);
        Assert.Equal(100m, await LabelKgAsync(run, source.LabelId));
    }

    [SqlServerFact]
    public async Task A_reused_owner_context_refreshes_unchanged_physical_rows_after_another_transaction_approves_a_count()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-NEG-CACHED-COUNT", 100m);
        await using var reused = run.Store(CompanyA);
        var trackedLabel = await reused.DbContext.Labels.SingleAsync(row => row.Id == source.LabelId);
        var trackedBalance = await reused.DbContext.Balances.SingleAsync(row => row.LabelId == source.LabelId);
        var label = (await reused.Labels.FindLabelAsync(source.LabelId))!;
        Assert.Equal(100m, trackedBalance.QuantityKg);
        var countId = Guid.NewGuid();
        await using (var count = run.Store(CompanyA))
        {
            await count.Store.RecordStockCountAsync(new(countId, source.LabelId, "2", "150", At.AddHours(2),
                "Başka transaction ikinci kişi sayımı."), Metadata(run), static _ => Task.CompletedTask);
            await count.Store.DecideStockCountAsync(countId,
                new(InventoryStockCountDecisionKindV1.Approve, "Sayım farkı doğrulandı."),
                Metadata(run) with { ActorId = "synthetic-count-checker" }, static _ => Task.CompletedTask);
        }
        var movement = Movement(CompanyA, StockMovementKind.Issue,
            Out(Position(CompanyA, await run.LocationOfAsync(CompanyA), label, BlockState.Free), 120m));
        Assert.Equal(InventoryWriteOutcome.Recorded, await reused.Movements.RecordMovementAsync(movement, Metadata(run)));
        Assert.Equal(30m, trackedBalance.QuantityKg); // The former tracked 100kg did not produce a false physical refusal.
        Assert.Equal(4L, trackedLabel.Version);
        Assert.Equal(30m, await LabelKgAsync(run, source.LabelId));
        Assert.Empty(await reused.DbContext.NegativeStockAttempts.ToArrayAsync());
    }

    [SqlServerFact]
    public async Task Borrowed_validation_guard_never_flushes_the_callers_staged_physical_work_or_owns_its_transaction()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-NEG-GUARD-BORROW", 100m);
        await using var physical = run.Store(CompanyA);
        var tracked = await physical.DbContext.Balances.SingleAsync(row => row.LabelId == source.LabelId);
        tracked.QuantityKg = 80m; // Deliberately staged, not a saved physical effect.
        await using var transaction = await physical.DbContext.Database.BeginTransactionAsync();
        await physical.Store.LockPhysicalValidationAsync(CancellationToken.None);
        Assert.Same(transaction, physical.DbContext.Database.CurrentTransaction);
        Assert.Equal(EntityState.Modified, physical.DbContext.Entry(tracked).State);
        Assert.Equal(80m, tracked.QuantityKg);
        // Read this TX directly, bypassing EF identity-map values; the technical guard saved none of its owner's work.
        Assert.Equal(100m, await physical.DbContext.Balances.AsNoTracking().Where(row => row.Id == tracked.Id)
            .Select(row => row.QuantityKg).SingleAsync());
        await transaction.RollbackAsync();
        Assert.Equal(100m, await LabelKgAsync(run, source.LabelId));
    }

    [SqlServerFact]
    public async Task Original_physical_kg_message_is_persisted_and_replayed_by_actual_Purchasing_and_Logistics_owners()
    {
        await using var run = await Run.StartAsync();
        var failed = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-TERMINAL-MESSAGE");
        var detail = InventoryPhysicalStockRefusalProblem.Apply(409, failed.Refusal, new Dictionary<string, object?>());
        Assert.Equal("Sistemde 100 kg var, 120 kg istendi.", detail);
        var hash = new string('a', 64);
        var purchasingEnvelope = new PurchasingWriteEnvelope(run.Actor.ToString("D"), ErpPermissions.PurchasingGoodsReceiptsRecord,
            ["SYNTHETIC"], run.Correlation, null, Guid.NewGuid().ToString("N"), "Web");
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await scope.ServiceProvider.GetRequiredService<PurchasingStore>().RecordRefusalAsync(purchasingEnvelope, hash,
                PurchaseReceiptReversalApiV1.CommandName,
                new(409, "inventory_goods_receipt_reversal_stock_not_available", [], detail));
        }
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var replay = await scope.ServiceProvider.GetRequiredService<PurchasingStore>().FindReplayAsync(
                purchasingEnvelope with { CorrelationId = "synthetic-retry" }, hash, PurchaseReceiptReversalApiV1.CommandName);
            Assert.Equal(detail, replay!.Refusal!.Detail);
            Assert.Equal(purchasingEnvelope.CorrelationId, replay.OriginalCorrelationId);
        }
        var logisticsEnvelope = new LogisticsWriteEnvelope(run.Actor.ToString("D"), ErpPermissions.LogisticsShipmentsRecord,
            ["SYNTHETIC"], run.Correlation, null, Guid.NewGuid().ToString("N"), "Web");
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await scope.ServiceProvider.GetRequiredService<LogisticsStore>().RecordRefusalAsync(logisticsEnvelope, hash,
                LogisticsCommands.RecordShipmentV1, new(409, "inventory_physical_stock_insufficient", [], detail));
        }
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var replay = await scope.ServiceProvider.GetRequiredService<LogisticsStore>().FindReplayAsync(
                logisticsEnvelope with { CorrelationId = "synthetic-retry" }, hash, LogisticsCommands.RecordShipmentV1);
            Assert.Equal(detail, replay!.Refusal!.Detail);
            Assert.Equal(logisticsEnvelope.CorrelationId, replay.OriginalCorrelationId);
        }
    }

    [SqlServerFact]
    public async Task Negative_attempt_history_blocks_down_migration_before_any_history_table_is_lost()
    {
        await using var run = await Run.StartAsync();
        var failed = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-DOWN-GUARD");
        await using var database = run.Context(CompanyA);
        await NegativeAttemptService(run, database).RecordAfterRollbackAsync(failed.Refusal);
        // Execute this migration's actual first Down operation. A newer forward guard must not hide 51986.
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryNegativeStockAttempts();
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]).Sql;
        var failure = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1;"));
        Assert.Equal(51986, failure.Number);
        Assert.Equal(1, await database.NegativeStockAttempts.CountAsync());
        Assert.True(await database.Database.CanConnectAsync());
    }

    [SqlServerFact]
    public async Task Count_boundary_downgrade_passes_empty_then_refuses_a_real_validation_mutex_before_any_history_is_lost()
    {
        await using var run = await Run.StartAsync();
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryNegativeStockCountBoundaries();
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + " SELECT 1;");
        var failed = await NegativeAttemptEvidenceAsync(run, "SYN-NEG-BOUNDARY-DOWN");
        await using var reads = run.Context(CompanyA);
        await NegativeAttemptService(run, reads).RecordAfterRollbackAsync(failed.Refusal);
        var refusal = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1;"));
        Assert.Equal(51988, refusal.Number);
        Assert.Equal(1, await reads.NegativeStockAttempts.CountAsync());
        Assert.Equal(0L, (await reads.NegativeStockAttempts.SingleAsync()).ObservedCountBoundaryVersion);
        Assert.True(await reads.NegativeStockValidationGuards.AnyAsync());
    }

    private sealed record NegativePhysicalFailure(InventoryRuleException Refusal, StockMovement Movement,
        InventoryCommandMetadata Metadata, Guid LabelId);
    private static async Task<NegativePhysicalFailure> NegativeAttemptEvidenceAsync(Run run, string labelCode)
    {
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, labelCode, 100m);
        await using var physical = run.Store(CompanyA);
        var label = (await physical.Labels.FindLabelAsync(source.LabelId))!;
        var movement = Movement(CompanyA, StockMovementKind.Issue,
            Out(Position(CompanyA, await run.LocationOfAsync(CompanyA), label, BlockState.Free), 120m));
        var metadata = Metadata(run);
        await using var transaction = await physical.DbContext.Database.BeginTransactionAsync();
        var failure = await Assert.ThrowsAsync<InventoryRuleException>(() => physical.Movements.RecordMovementAsync(movement, metadata));
        Assert.NotNull(InventoryNegativeStockAttemptEvidenceException.FindEvidence(failure));
        await transaction.RollbackAsync();
        return new(failure, movement, metadata, source.LabelId);
    }
    private static InventoryNegativeStockAttempts NegativeAttemptService(Run run, InventoryDbContext reads,
        NegativeAttemptAuthority? authority = null, IServiceScopeFactory? factory = null)
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        return new(company, new FixedCurrentUser(run.Actor, CompanyA), authority ?? new NegativeAttemptAuthority(),
            factory ?? run.Factory.Services.GetRequiredService<IServiceScopeFactory>(), reads);
    }
    private sealed class NegativeAttemptAuthority : IErpAuthorityRevalidationService
    {
        public int Calls { get; private set; }
        public int? DenyAtCall { get; init; }
        public Func<int, Task>? OnCall { get; init; }
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (OnCall is not null) await OnCall(Calls);
            return DenyAtCall is { } call && Calls >= call ? PermissionScope.Denied
                : new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }
    }
    private sealed class NegativeAuditFaultScopeFactory(IServiceScopeFactory actual) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new FaultScope(actual.CreateScope());
        private sealed class FaultScope(IServiceScope actualScope) : IServiceScope, IAsyncDisposable
        {
            public IServiceProvider ServiceProvider { get; } = new FaultProvider(actualScope.ServiceProvider);
            public void Dispose() => actualScope.Dispose();
            public ValueTask DisposeAsync() => actualScope is IAsyncDisposable asyncScope ? asyncScope.DisposeAsync()
                : DisposeSynchronously();
            private ValueTask DisposeSynchronously() { Dispose(); return ValueTask.CompletedTask; }
        }
        private sealed class FaultProvider(IServiceProvider actualProvider) : IServiceProvider
        {
            public object? GetService(Type serviceType) => serviceType == typeof(IAuditTransactionWriterV1)
                ? new FaultWriter(actualProvider.GetRequiredService<IAuditTransactionWriterV1>()) : actualProvider.GetService(serviceType);
        }
        private sealed class FaultWriter(IAuditTransactionWriterV1 inner) : IAuditTransactionWriterV1
        {
            public bool HasPendingDrafts => inner.HasPendingDrafts;
            public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("synthetic Audit owner failure");
            public void DiscardPending() => inner.DiscardPending();
        }
    }
}
