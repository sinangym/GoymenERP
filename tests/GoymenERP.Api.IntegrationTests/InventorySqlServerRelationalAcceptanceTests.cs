using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Opening;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// INV-2 against a REAL SQL Server: Inventory's own migration on top of the host's migrated and
/// seeded database, the real approved Settings kilogram profile (GDC = company A) and Audit's real
/// save interceptor. Every identity below is synthetic test data in this test's own database; no
/// product, label or stock is seeded anywhere.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Product = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a61");
    // One synthetic lot per company, stored when the run starts (DEC-PRD-003: every label names a lot of its own company).
    private static readonly Guid LotA = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a62");
    private static readonly Guid LotB = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a63");
    private static readonly DateTimeOffset At = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Receipt_issue_block_and_release_are_durable_replayable_audited_and_reconcile()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, "SYN-INV2-DURABLE");
        var free = Position(CompanyA, location, label, BlockState.Free);
        var blocked = Position(CompanyA, location, label, BlockState.Blocked);

        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Labels.SaveLabelAsync(label, Metadata(run)));
        }

        var receipt = Movement(CompanyA, StockMovementKind.Receipt, In(free, 1000.125m));
        var movements = new[]
        {
            receipt,
            Movement(CompanyA, StockMovementKind.Issue, Out(free, 200m)),
            Movement(CompanyA, StockMovementKind.Block, Out(free, 100m), In(blocked, 100m)),
            Movement(CompanyA, StockMovementKind.Release, Out(blocked, 50m), In(free, 50m)),
        };
        foreach (var movement in movements)
        {
            await using var a = run.Store(CompanyA);
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Movements.RecordMovementAsync(movement, Metadata(run)));
        }

        await using (var a = run.Store(CompanyA))
        {
            var balances = await a.Movements.ReadBalancesAsync(label.Id);
            var stored = await a.Movements.ReadMovementsAsync(label.Id);
            Assert.Equal(750.125m, balances.Single(balance => balance.Position == free).Quantity.Value);
            Assert.Equal(50m, balances.Single(balance => balance.Position == blocked).Quantity.Value);
            Assert.Equal(movements.Select(movement => movement.Id), stored.Select(movement => movement.Id));
            // The stored balances are exactly what the stored movements add up to.
            Assert.Empty(StockLedger.Reconcile(balances, stored));

            // Same id, same content: a replay with no second effect. Same id, other content: refused.
            Assert.Equal(InventoryWriteOutcome.Replayed, await a.Movements.RecordMovementAsync(receipt, Metadata(run)));
            var changed = new StockMovement(receipt.Id, CompanyA, StockMovementKind.Receipt, At, receipt.Source, [In(free, 1m)]);
            Assert.Equal(InventoryStoreCodes.MovementIdConflict,
                (await Assert.ThrowsAsync<InventoryStoreException>(() => a.Movements.RecordMovementAsync(changed, Metadata(run)))).Code);
        }

        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(750.125m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 1", ("@id", label.Id)));
        // One audit event per stored write (the label and four movements), none for the replay.
        Assert.Equal(5, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @c", ("@c", run.Correlation)));
        Assert.Equal(1 + movements.Length, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", label.Id)));
    }

    [SqlServerFact]
    public async Task Each_company_sees_only_its_own_labels_and_a_code_is_unique_per_company_only()
    {
        await using var run = await Run.StartAsync();
        var labelA = NewLabel(CompanyA, "SYN-SHARED-CODE");
        var labelB = NewLabel(CompanyB, "SYN-SHARED-CODE");

        await using (var a = run.Store(CompanyA))
        {
            await a.Labels.SaveLabelAsync(labelA, Metadata(run));
            Assert.Equal(InventoryStoreCodes.LabelCodeTaken, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => a.Labels.SaveLabelAsync(NewLabel(CompanyA, "SYN-SHARED-CODE"), Metadata(run)))).Code);
        }

        await using (var b = run.Store(CompanyB))
        {
            // The same printed code in another company is its own label: no group-wide uniqueness.
            Assert.Equal(InventoryWriteOutcome.Recorded, await b.Labels.SaveLabelAsync(labelB, Metadata(run)));
            // Company A's label does not exist for B, by id or through a movement.
            Assert.Null(await b.Labels.FindLabelAsync(labelA.Id));
            Assert.Empty(await b.Movements.ReadBalancesAsync(labelA.Id));
            // B cannot write a record of company A.
            Assert.Equal(InventoryStoreCodes.CompanyContextMismatch, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => b.Labels.SaveLabelAsync(NewLabel(CompanyA, "SYN-FROM-B"), Metadata(run)))).Code);
            // B has no approved kilogram profile (only GDC does): a movement is refused fail-closed,
            // with the Settings owner's own code, and nothing is stored.
            var locationB = await run.LocationOfAsync(CompanyB);
            var receipt = Movement(CompanyB, StockMovementKind.Receipt, In(Position(CompanyB, locationB, labelB, BlockState.Free), 5m));
            Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<InventoryStoreException>(
                () => b.Movements.RecordMovementAsync(receipt, Metadata(run)))).Code);
        }

        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(labelA.Code, (await a.Labels.FindLabelAsync(labelA.Id))!.Code);
            Assert.Null(await a.Labels.FindLabelAsync(labelB.Id));
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Code = N'SYN-SHARED-CODE'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
    }

    [SqlServerFact]
    public async Task A_source_causes_one_movement_per_kind_and_a_stock_bearing_label_is_not_received_again()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var first = NewLabel(CompanyA, "SYN-SOURCE-1");
        var second = NewLabel(CompanyA, "SYN-SOURCE-2");
        var source = new StockSourceReference("purchase.receipt", Guid.NewGuid());

        await using (var a = run.Store(CompanyA))
        {
            await a.Labels.SaveLabelAsync(first, Metadata(run));
            await a.Labels.SaveLabelAsync(second, Metadata(run));
            await a.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Receipt, source, In(Position(CompanyA, location, first, BlockState.Free), 10m)),
                Metadata(run));

            // The same source again, for another label and with another movement id: refused.
            Assert.Equal(InventoryStoreCodes.DuplicateSource, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => a.Movements.RecordMovementAsync(
                    Movement(CompanyA, StockMovementKind.Receipt, source, In(Position(CompanyA, location, second, BlockState.Free), 10m)),
                    Metadata(run)))).Code);
            // A second receipt of the stock-bearing label, from another source: refused by the rule.
            Assert.Equal(InventoryRuleCodes.LabelAlreadyInStock, (await Assert.ThrowsAsync<InventoryRuleException>(
                () => a.Movements.RecordMovementAsync(
                    Movement(CompanyA, StockMovementKind.Receipt, In(Position(CompanyA, location, first, BlockState.Free), 1m)),
                    Metadata(run)))).Code);
        }

        // The unique index holds even without the store's own check (a racing writer).
        await using (var raw = run.Context(CompanyA))
        {
            raw.Movements.Add(new MovementRow
            {
                Id = Guid.NewGuid(),
                CompanyId = CompanyA,
                Kind = (int)StockMovementKind.Receipt,
                OccurredAtUtc = At,
                SourceKind = source.Kind,
                SourceId = source.Id,
                SourceGrain = (int)StockSourceGrain.Whole,
                CostStatus = 1,
                PayloadHash = new string('0', 64),
                RecordedAtUtc = At,
                RecordedBy = "synthetic-actor",
                CorrelationId = run.Correlation,
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
            Assert.Contains("UX_Movements_Source", error.InnerException!.Message, StringComparison.Ordinal);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(10m, await run.ScalarAsync<decimal>(
            "SELECT SUM(QuantityKg) FROM inventory.Balances WHERE LabelId = @id", ("@id", first.Id)));
    }

    [SqlServerFact]
    public async Task A_source_is_answered_whole_or_line_by_line_never_both()
    {
        // #293 ORG P3-1: a whole and a line movement of one source and kind would count the same
        // quantity twice. The store refuses it with its own code; SQL refuses it without the store.
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var labels = Enumerable.Range(1, 5).Select(number => NewLabel(CompanyA, $"SYN-GRAIN-{number}")).ToArray();
        var whole = new StockSourceReference("purchase.receipt", Guid.NewGuid());
        var lined = Guid.NewGuid();
        var firstLine = new StockSourceReference("purchase.receipt", lined, Guid.NewGuid());
        StockMovement Receive(StockSourceReference source, int label) =>
            Movement(CompanyA, StockMovementKind.Receipt, source, In(Position(CompanyA, location, labels[label], BlockState.Free), 10m));

        await using (var a = run.Store(CompanyA))
        {
            foreach (var label in labels)
            {
                await a.Labels.SaveLabelAsync(label, Metadata(run));
            }

            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Movements.RecordMovementAsync(Receive(whole, 0), Metadata(run)));
            Assert.Equal(InventoryStoreCodes.SourceGrainMismatch, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => a.Movements.RecordMovementAsync(Receive(new StockSourceReference("purchase.receipt", whole.Id, Guid.NewGuid()), 1),
                    Metadata(run)))).Code);

            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Movements.RecordMovementAsync(Receive(firstLine, 1), Metadata(run)));
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Movements.RecordMovementAsync(
                Receive(new StockSourceReference("purchase.receipt", lined, Guid.NewGuid()), 2), Metadata(run)));
            // The same line again (the line index's path), and the whole source after its lines.
            Assert.Equal(InventoryStoreCodes.DuplicateSource, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => a.Movements.RecordMovementAsync(Receive(firstLine, 3), Metadata(run)))).Code);
            Assert.Equal(InventoryStoreCodes.SourceGrainMismatch, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => a.Movements.RecordMovementAsync(Receive(new StockSourceReference("PURCHASE.RECEIPT", lined), 3),
                    Metadata(run)))).Code);
        }

        // Without the store's checks (a racing writer): each path is refused by SQL itself.
        var rawCases = new (StockSourceReference Source, int Grain, string Constraint, int Number)[]
        {
            // A line movement of the source answered whole: its grain row says whole.
            (new StockSourceReference("purchase.receipt", whole.Id, Guid.NewGuid()), 2,
                "FK_Movements_SourceGrains_CompanyId_Kind_SourceKind_SourceId_SourceGrain", 547),
            // A whole movement of the source answered by lines.
            (new StockSourceReference("purchase.receipt", lined), 1,
                "FK_Movements_SourceGrains_CompanyId_Kind_SourceKind_SourceId_SourceGrain", 547),
            // The same source line twice.
            (firstLine, 2, "UX_Movements_SourceLine", 2601),
            // A grain that contradicts the movement's own line (the grain row exists, only the check fails).
            (new StockSourceReference("purchase.receipt", whole.Id, Guid.NewGuid()), 1, "CK_Movements_SourceGrain", 547),
        };
        foreach (var (source, grain, constraint, number) in rawCases)
        {
            await using var raw = run.Context(CompanyA);
            raw.Movements.Add(RawMovement(run, source, grain));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
            var sql = Assert.IsType<SqlException>(error.InnerException);
            Assert.Equal(number, sql.Number);
            Assert.Contains(constraint, sql.Message, StringComparison.Ordinal);
        }

        // A second grain row for a source that has one: the key holds one grain per source and kind.
        await using (var raw = run.Context(CompanyA))
        {
            raw.SourceGrains.Add(new SourceGrainRow
            {
                CompanyId = CompanyA,
                Kind = (int)StockMovementKind.Receipt,
                SourceKind = "Purchase.Receipt",
                SourceId = whole.Id,
                Grain = (int)StockSourceGrain.Line,
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
            Assert.Contains("PK_SourceGrains", error.InnerException!.Message, StringComparison.Ordinal);
        }

        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.SourceGrains"));
        Assert.Equal(30m, await run.ScalarAsync<decimal>("SELECT SUM(QuantityKg) FROM inventory.Balances"));
    }

    [SqlServerFact]
    public async Task A_whole_and_a_line_movement_racing_for_one_source_cannot_both_commit()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var first = NewLabel(CompanyA, "SYN-GRAIN-RACE-1");
        var second = NewLabel(CompanyA, "SYN-GRAIN-RACE-2");
        await using (var a = run.Store(CompanyA))
        {
            await a.Labels.SaveLabelAsync(first, Metadata(run));
            await a.Labels.SaveLabelAsync(second, Metadata(run));
        }

        // Two different labels, so no label version is shared: only the source's grain row is.
        var sourceId = Guid.NewGuid();
        var whole = Movement(CompanyA, StockMovementKind.Receipt, new StockSourceReference("purchase.receipt", sourceId),
            In(Position(CompanyA, location, first, BlockState.Free), 10m));
        var line = Movement(CompanyA, StockMovementKind.Receipt, new StockSourceReference("purchase.receipt", sourceId, Guid.NewGuid()),
            In(Position(CompanyA, location, second, BlockState.Free), 10m));
        var barrier = new Barrier(2);
        async Task<object> WriteAsync(StockMovement movement)
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return await writer.Movements.RecordMovementAsync(movement, Metadata(run));
            }
            catch (InventoryStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => WriteAsync(whole)), Task.Run(() => WriteAsync(line)));

        Assert.Single(results, result => result is InventoryWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == InventoryStoreCodes.ConcurrentChange);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.SourceGrains"));
        Assert.Equal(10m, await run.ScalarAsync<decimal>("SELECT SUM(QuantityKg) FROM inventory.Balances"));

        // The loser, retried, reads the winner's grain and is refused for what it is.
        var loser = results[0] is InventoryWriteOutcome.Recorded ? line : whole;
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryStoreCodes.SourceGrainMismatch, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => a.Movements.RecordMovementAsync(loser, Metadata(run)))).Code);
        }
    }

    [SqlServerFact]
    public async Task A_label_code_has_one_answer_under_the_code_column_collation()
    {
        // #293 SİPARİŞ P3-1: the code column's collation is the one comparison. Today it is SQL
        // Server's default, case-insensitive; this test states that assumption before relying on it.
        await using var run = await Run.StartAsync();
        var collation = await run.ScalarAsync<string>(
            "SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID(N'inventory.Labels') AND name = N'Code'");
        Assert.Contains("_CI_", collation, StringComparison.Ordinal);

        var label = NewLabel(CompanyA, "SYN-CASE-1");
        var sameIdOtherSpelling = new MaterialLabel(label.Id, CompanyA, "syn-case-1", Product, LotA, "product-v1");
        var sameIdOtherCode = new MaterialLabel(label.Id, CompanyA, "SYN-CASE-2", Product, LotA, "product-v1");
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Labels.SaveLabelAsync(label, Metadata(run)));
        }

        // Another spelling of the stored code: the same answer with a new id and with the same id.
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryStoreCodes.LabelCodeTaken, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => a.Labels.SaveLabelAsync(NewLabel(CompanyA, "syn-case-1"), Metadata(run)))).Code);
        }

        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryStoreCodes.LabelCodeTaken, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => a.Labels.SaveLabelAsync(sameIdOtherSpelling, Metadata(run)))).Code);
            // Another code under the same id is still a conflicting identity; the exact label is a replay.
            Assert.Equal(InventoryStoreCodes.LabelConflict, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => a.Labels.SaveLabelAsync(sameIdOtherCode, Metadata(run)))).Code);
            Assert.Equal(InventoryWriteOutcome.Replayed, await a.Labels.SaveLabelAsync(label, Metadata(run)));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Labels"));
        Assert.Equal("SYN-CASE-1", await run.ScalarAsync<string>("SELECT Code FROM inventory.Labels"));
    }

    [SqlServerFact]
    public async Task A_historical_label_without_product_version_replays_and_extends_but_a_new_label_requires_it()
    {
        await using var run = await Run.StartAsync();
        var label = NewLabel(CompanyA, "SYN-LEGACY-PRODUCT-VERSION");
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Labels.SaveLabelAsync(label, Metadata(run)));
        }

        await run.ExecuteAsync("UPDATE inventory.Labels SET ProductVersion = NULL WHERE Id = @id", ("@id", label.Id));
        await using (var a = run.Store(CompanyA))
        {
            var historical = (await a.Labels.FindLabelAsync(label.Id))!;
            Assert.Null(historical.ProductVersion);
            Assert.Equal(InventoryWriteOutcome.Replayed,
                await a.Labels.SaveLabelAsync(historical, Metadata(run)));

            historical.Print(At, "synthetic-operator");
            Assert.Equal(InventoryWriteOutcome.Recorded,
                await a.Labels.SaveLabelAsync(historical, Metadata(run)));

            var versionlessNew = new MaterialLabel(Guid.NewGuid(), CompanyA,
                "SYN-NEW-WITHOUT-PRODUCT-VERSION", Product, LotA);
            Assert.Equal(InventoryStoreCodes.ValueDoesNotFit,
                (await Assert.ThrowsAsync<InventoryStoreException>(() =>
                    a.Labels.SaveLabelAsync(versionlessNew, Metadata(run)))).Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Id = @id AND ProductVersion IS NULL",
            ("@id", label.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.LabelPrints WHERE LabelId = @id", ("@id", label.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Code = N'SYN-NEW-WITHOUT-PRODUCT-VERSION'"));
    }

    [SqlServerFact]
    public async Task A_known_cost_offered_again_at_another_scale_is_a_replay()
    {
        // #293 SİPARİŞ P3-2: 5.00 and 5 are one amount; the stored movement is the same content.
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, "SYN-COST-SCALE");
        var currency = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a63");
        var lines = new[] { In(Position(CompanyA, location, label, BlockState.Free), 10m) };
        var id = Guid.NewGuid();
        var source = new StockSourceReference("purchase.receipt", Guid.NewGuid());
        StockMovement Costed(decimal amount) =>
            new(id, CompanyA, StockMovementKind.Receipt, At, source, lines, CostResult.Known(amount, currency));

        await using (var a = run.Store(CompanyA))
        {
            await a.Labels.SaveLabelAsync(label, Metadata(run));
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Movements.RecordMovementAsync(Costed(5.00m), Metadata(run)));
            Assert.Equal(InventoryWriteOutcome.Replayed, await a.Movements.RecordMovementAsync(Costed(5m), Metadata(run)));
            Assert.Equal(InventoryStoreCodes.MovementIdConflict, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => a.Movements.RecordMovementAsync(Costed(5.01m), Metadata(run)))).Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(10m, await run.ScalarAsync<decimal>("SELECT SUM(QuantityKg) FROM inventory.Balances"));
    }

    [SqlServerFact]
    public async Task Two_writers_that_read_the_same_piece_cannot_both_commit()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, "SYN-RACE");
        await using (var a = run.Store(CompanyA))
        {
            await a.Labels.SaveLabelAsync(label, Metadata(run));
        }

        // Both writers read the label and its (empty) stock, then wait for each other at the save.
        var barrier = new Barrier(2);
        async Task<object> WriteAsync(decimal kilograms)
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return await writer.Movements.RecordMovementAsync(Movement(CompanyA, StockMovementKind.Receipt,
                    In(Position(CompanyA, location, label, BlockState.Free), kilograms)), Metadata(run));
            }
            catch (InventoryStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => WriteAsync(10m)), Task.Run(() => WriteAsync(20m)));

        Assert.Single(results, result => result is InventoryWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == InventoryStoreCodes.ConcurrentChange);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Balances WHERE LabelId = @id", ("@id", label.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockMovement'"));
        await using (var a = run.Store(CompanyA))
        {
            var stored = await a.Movements.ReadMovementsAsync(label.Id);
            Assert.Empty(StockLedger.Reconcile(await a.Movements.ReadBalancesAsync(label.Id), stored));
        }
    }

    [SqlServerFact]
    public async Task A_save_that_fails_in_the_database_leaves_nothing_behind()
    {
        await using var run = await Run.StartAsync();
        var locationA = await run.LocationOfAsync(CompanyA);
        var locationB = await run.LocationOfAsync(CompanyB);
        var label = NewLabel(CompanyA, "SYN-ROLLBACK");
        await using (var a = run.Store(CompanyA))
        {
            await a.Labels.SaveLabelAsync(label, Metadata(run));
        }

        // A location of company B under company A: the composite (company, location) foreign key
        // refuses it (547), and the movement, its balance, the label version and the audit event
        // all roll back together. A location that does not exist at all is refused the same way.
        foreach (var location in new[] { locationB, Guid.NewGuid() })
        {
            await using var a = run.Store(CompanyA);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => a.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Receipt, In(Position(CompanyA, location, label, BlockState.Free), 5m)),
                Metadata(run)));
            Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.MovementLines"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Balances"));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", label.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockMovement'"));

        // The same movement in the right place then succeeds: nothing was half-written.
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Receipt, In(Position(CompanyA, locationA, label, BlockState.Free), 5m)),
                Metadata(run)));
        }
    }

    [SqlServerFact]
    public async Task A_lot_and_its_labels_are_stored_in_one_save_with_their_audit_events_or_not_at_all()
    {
        // DEC-PRD-003 / Business #66 5734060341: lot + labels + audit are one transaction.
        await using var run = await Run.StartAsync();
        var lot = new MaterialLot(Guid.NewGuid(), CompanyA, Product, heatNumber: "SYN-HEAT-1");
        var first = new MaterialLabel(Guid.NewGuid(), CompanyA, "SYN-LOT-ONE-1", Product, lot.Id, "product-v1");
        var second = new MaterialLabel(Guid.NewGuid(), CompanyA, "SYN-LOT-ONE-2", Product, lot.Id, "product-v1");
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Lots.SaveLotAsync(lot, [first, second], Metadata(run)));
            var stored = (await a.Lots.FindLotAsync(lot.Id))!;
            Assert.Equal((CompanyA, Product, (string?)"SYN-HEAT-1", (string?)null),
                (stored.CompanyId, stored.ProductId, stored.HeatNumber, stored.CoilNumber));
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Labels WHERE LotId = @l", ("@l", lot.Id)));
        // One event per stored record, one correlation: the lot and its two labels.
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @c", ("@c", run.Correlation)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.MaterialLot' AND EntityId = @id",
            ("@id", lot.Id.ToString("D"))));

        // A save that fails in the database leaves nothing: the second label's code is taken, so the new lot,
        // its first label and every audit event roll back together.
        var failing = new MaterialLot(Guid.NewGuid(), CompanyA, Product);
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryStoreCodes.LabelCodeTaken, (await Assert.ThrowsAsync<InventoryStoreException>(() => a.Lots.SaveLotAsync(
                failing,
                [
                    new MaterialLabel(Guid.NewGuid(), CompanyA, "SYN-LOT-TWO-1", Product, failing.Id, "product-v1"),
                    new MaterialLabel(Guid.NewGuid(), CompanyA, "syn-lot-one-1", Product, failing.Id, "product-v1"),
                ],
                Metadata(run)))).Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Lots WHERE Id = @l", ("@l", failing.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Labels WHERE LotId = @l", ("@l", failing.Id)));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @c", ("@c", run.Correlation)));
    }

    [SqlServerFact]
    public async Task A_label_without_its_lot_is_refused_by_the_store_and_by_the_schema()
    {
        await using var run = await Run.StartAsync();
        var missingLot = Guid.NewGuid();
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryStoreCodes.LotNotFound, (await Assert.ThrowsAsync<InventoryStoreException>(() => a.Labels.SaveLabelAsync(
                new MaterialLabel(Guid.NewGuid(), CompanyA, "SYN-NO-LOT", Product, missingLot, "product-v1"), Metadata(run)))).Code);
            // A lot is of one product: a label of another product cannot name it.
            Assert.Equal(InventoryStoreCodes.LotProductMismatch, (await Assert.ThrowsAsync<InventoryStoreException>(() => a.Labels.SaveLabelAsync(
                new MaterialLabel(Guid.NewGuid(), CompanyA, "SYN-OTHER-PRODUCT", Guid.NewGuid(), LotA, "product-v1"), Metadata(run)))).Code);
        }

        // Without the store (a writer that skips its check): SQL refuses the orphan itself.
        await using (var raw = run.Context(CompanyA))
        {
            raw.Labels.Add(RawLabel(CompanyA, missingLot));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
            var sql = Assert.IsType<SqlException>(error.InnerException);
            Assert.Equal(547, sql.Number);
            Assert.Contains("FK_Labels_Lots_CompanyId_LotId", sql.Message, StringComparison.Ordinal);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Labels WHERE LotId = @l", ("@l", missingLot)));
    }

    [SqlServerFact]
    public async Task Company_B_can_neither_see_nor_name_company_A_lot()
    {
        await using var run = await Run.StartAsync();
        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Lots.FindLotAsync(LotA));
            Assert.NotNull(await b.Lots.FindLotAsync(LotB));
            // B's own label naming A's lot: the tenant filter hides A's lot, so it does not exist for B.
            Assert.Equal(InventoryStoreCodes.LotNotFound, (await Assert.ThrowsAsync<InventoryStoreException>(() => b.Labels.SaveLabelAsync(
                new MaterialLabel(Guid.NewGuid(), CompanyB, "SYN-B-NAMES-A", Product, LotA, "product-v1"), Metadata(run)))).Code);
            // A's lot offered in B's context is refused before anything is read.
            Assert.Equal(InventoryStoreCodes.CompanyContextMismatch, (await Assert.ThrowsAsync<InventoryStoreException>(() => b.Lots.SaveLotAsync(
                new MaterialLot(LotA, CompanyA, Product), [], Metadata(run)))).Code);
        }

        // #324 P3-1: an id another company holds collides on the key, invisible to B. A retry would collide
        // forever, so the answer is a conflict with no retry advice — the same on every attempt (a fresh
        // store each time, as each request is) — and not concurrent_change.
        var labelOfA = NewLabel(CompanyA, "SYN-A-HOLDS-ITS-ID");
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Labels.SaveLabelAsync(labelOfA, Metadata(run)));
        }

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await using (var b = run.Store(CompanyB))
            {
                Assert.Equal(InventoryStoreCodes.LotConflict, (await Assert.ThrowsAsync<InventoryStoreException>(() => b.Lots.SaveLotAsync(
                    new MaterialLot(LotA, CompanyB, Product), [], Metadata(run)))).Code);
            }

            await using (var b = run.Store(CompanyB))
            {
                Assert.Equal(InventoryStoreCodes.LabelConflict, (await Assert.ThrowsAsync<InventoryStoreException>(() => b.Labels.SaveLabelAsync(
                    new MaterialLabel(labelOfA.Id, CompanyB, "SYN-B-REUSES-A-ID", Product, LotB, "product-v1"), Metadata(run)))).Code);
            }

            // #333 SİPARİŞ P3-1: A's lot id AND A's label id in ONE save. Both keys can collide in the same batch,
            // so the combined message may carry two GUIDs; the id is read from the lot key's own error.
            await using (var b = run.Store(CompanyB))
            {
                Assert.Equal(InventoryStoreCodes.LotConflict, (await Assert.ThrowsAsync<InventoryStoreException>(() => b.Lots.SaveLotAsync(
                    new MaterialLot(LotA, CompanyB, Product),
                    [new MaterialLabel(labelOfA.Id, CompanyB, "SYN-B-REUSES-BOTH", Product, LotA, "product-v1")], Metadata(run)))).Code);
            }
        }

        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(CompanyA, (await a.Lots.FindLotAsync(LotA))!.CompanyId);
            Assert.Equal(labelOfA.Code, (await a.Labels.FindLabelAsync(labelOfA.Id))!.Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Code IN (N'SYN-B-REUSES-A-ID', N'SYN-B-REUSES-BOTH')"));

        // Without the store: the (company, lot) key makes a cross-company link impossible in the schema.
        await using (var raw = run.Context(CompanyB))
        {
            raw.Labels.Add(RawLabel(CompanyB, LotA));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
            var sql = Assert.IsType<SqlException>(error.InnerException);
            Assert.Equal(547, sql.Number);
            Assert.Contains("FK_Labels_Lots_CompanyId_LotId", sql.Message, StringComparison.Ordinal);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE CompanyId = @b AND LotId = @l", ("@b", CompanyB), ("@l", LotA)));
    }

    /// <summary>
    /// #324 P3-2 (Business #66 5734060341: the migration fails visibly, no lot is invented): a label
    /// stored before the lot table existed, naming a lot that does not exist, makes AddMaterialLots fail
    /// when the foreign key is added. The migration's transaction takes the lot table back with it, the
    /// label stays as it was, and the history does not record the migration.
    /// </summary>
    [SqlServerFact]
    public async Task A_label_without_its_lot_makes_the_lot_migration_fail_visibly_and_changes_nothing()
    {
        await using var run = await Run.StartAsync(migrateInventory: false);
        const string BeforeLots = "20260918160000_AddInventorySourceGrain";
        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(BeforeLots);
        }

        var orphan = RawLabel(CompanyA, Guid.NewGuid());
        // This row belongs to the historical schema. Current EF includes columns that did not exist yet.
        await run.ExecuteAsync(
            """
            INSERT INTO inventory.Labels
                (Id, CompanyId, Code, ProductId, LotId, RequiredDocumentsJson, Version, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@id, @company, @code, @product, @lot, @documents, @version, @created, @updated);
            """,
            ("@id", orphan.Id), ("@company", orphan.CompanyId), ("@code", orphan.Code),
            ("@product", orphan.ProductId), ("@lot", orphan.LotId),
            ("@documents", orphan.RequiredDocumentsJson), ("@version", orphan.Version),
            ("@created", orphan.CreatedAtUtc), ("@updated", orphan.UpdatedAtUtc));

        await using (var context = run.Context(CompanyA))
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync());
            Assert.Equal(547, error.Number);
            Assert.Contains("FK_Labels_Lots_CompanyId_LotId", error.Message, StringComparison.Ordinal);
            Assert.Equal(BeforeLots, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'Lots' AND [schema_id] = SCHEMA_ID(N'inventory')"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE [name] = N'FK_Labels_Lots_CompanyId_LotId'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Id = @id AND LotId = @lot AND Version = 1",
            ("@id", orphan.Id), ("@lot", orphan.LotId)));
    }

    [SqlServerFact]
    public async Task Product_version_provenance_blocks_rollback_without_losing_the_label()
    {
        const string previousMigration = "20260930182024_AddInventoryTransfers";
        const string currentMigration = "20261001160000_AddInventoryLabelProductVersion";
        await using var run = await Run.StartAsync();
        var label = NewLabel(CompanyA, "SYN-PRODUCT-VERSION-DOWN");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Labels.SaveLabelAsync(label, Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            var refused = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(previousMigration));
            Assert.Equal(51054, refused.Number);
            Assert.Equal(currentMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM inventory.Labels
            WHERE Id = @id AND CompanyId = @company AND Code = @code AND ProductId = @product
              AND LotId = @lot AND ProductVersion = @productVersion AND Version = 1;
            """, ("@id", label.Id), ("@company", label.CompanyId), ("@code", label.Code),
            ("@product", label.ProductId), ("@lot", label.LotId), ("@productVersion", label.ProductVersion!)));
    }

    /// <summary>
    /// INV-OPEN-1 (T07 opening stock, decision A #66 5738113623 §2): a physical count of two pieces posts,
    /// in one save, the lot, two new labels, one receipt each (sourced by its count line) and their
    /// balances; the book equals the count (Reconcile is empty); the same count again is a replay, another
    /// content under its id a conflict. The real approved GDC kg profile decides the scale.
    /// </summary>
    [SqlServerFact]
    public async Task An_opening_count_posts_one_receipt_per_counted_piece_that_reconciles_and_replays()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var lot = new MaterialLot(Guid.NewGuid(), CompanyA, Product, coilNumber: "SYN-OPEN-COIL");
        var count = new OpeningCount(Guid.NewGuid(), CompanyA, location, new DateOnly(2026, 9, 19), "SYN-SHEET-OPEN-1",
        [
            new(Guid.NewGuid(), lot, new MaterialLabel(Guid.NewGuid(), CompanyA, "SYN-OPEN-1", Product, lot.Id, "product-v1"), Kilograms.Positive(1250.5m)),
            new(Guid.NewGuid(), lot, new MaterialLabel(Guid.NewGuid(), CompanyA, "SYN-OPEN-2", Product, lot.Id, "product-v1"), Kilograms.Positive(980m)),
        ]);

        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Opening.RecordAsync(count, Metadata(run)));
        }

        await using (var a = run.Store(CompanyA))
        {
            foreach (var line in count.Lines)
            {
                var balance = Assert.Single(await a.Movements.ReadBalancesAsync(line.Label.Id));
                Assert.Equal(line.Quantity, balance.Quantity);
                Assert.Equal((count.CompanyId, location, BlockState.Free), (balance.Position.Owner.Id, balance.Position.LocationId, balance.Position.Block));
                var movement = Assert.Single(await a.Movements.ReadMovementsAsync(line.Label.Id));
                Assert.Equal((line.Id, new StockSourceReference(OpeningCount.SourceKind, count.Id, line.Id)), (movement.Id, movement.Source));
                // Book = count at opening.
                Assert.Empty(StockLedger.Reconcile([balance], [movement]));
            }

            var stored = (await a.Opening.ReadCountAsync(count.Id))!;
            Assert.Equal(("SYN-SHEET-OPEN-1", new DateOnly(2026, 9, 19)), (stored.SheetReference, stored.CountedOn));
            Assert.Equal(count.Lines.Select(line => (line.Id, line.Label.Code, line.Quantity)),
                stored.Lines.Select(line => (line.Id, line.Label.Code, line.Quantity)));

            // R3: the same count again has no second effect; another content under its id is refused.
            Assert.Equal(InventoryWriteOutcome.Replayed, await a.Opening.RecordAsync(count, Metadata(run)));
            var changed = new OpeningCount(count.Id, CompanyA, location, count.CountedOn, "SYN-SHEET-OTHER", count.Lines);
            Assert.Equal(InventoryStoreCodes.OpeningConflict, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => a.Opening.RecordAsync(changed, Metadata(run)))).Code);
        }

        Assert.Equal((1, 2, 2), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCounts"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCountLines"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'inventory.opening-count'")));
        // One audit event each: the lot, two labels, two receipts and the count; none for the replay.
        Assert.Equal(6, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @c", ("@c", run.Correlation)));
    }

    [SqlServerFact]
    public async Task A_pre_product_version_opening_replays_without_backfill_or_hash_rewrite()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var lot = new MaterialLot(Guid.NewGuid(), CompanyA, Product);
        var count = new OpeningCount(Guid.NewGuid(), CompanyA, location, new DateOnly(2026, 9, 19),
            "SYN-SHEET-LEGACY-HASH",
            [new(Guid.NewGuid(), lot, new MaterialLabel(Guid.NewGuid(), CompanyA,
                "SYN-OPEN-LEGACY-HASH", Product, lot.Id, "product-v1"), Kilograms.Positive(5m))]);
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Opening.RecordAsync(count, Metadata(run)));
        }

        var legacyHash = InventoryStore.LegacyOpeningHash(count);
        await run.ExecuteAsync("UPDATE inventory.OpeningCounts SET ContentHash = @hash WHERE Id = @id",
            ("@hash", legacyHash), ("@id", count.Id));
        await run.ExecuteAsync("UPDATE inventory.Labels SET ProductVersion = NULL WHERE Id = @id",
            ("@id", count.Lines[0].Label.Id));

        var legacyLabel = new MaterialLabel(count.Lines[0].Label.Id, CompanyA,
            count.Lines[0].Label.Code, Product, lot.Id);
        var legacyCount = new OpeningCount(count.Id, count.CompanyId, count.LocationId, count.CountedOn,
            count.SheetReference,
            [new(count.Lines[0].Id, lot, legacyLabel, count.Lines[0].Quantity)]);

        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryWriteOutcome.Replayed, await a.Opening.RecordAsync(legacyCount, Metadata(run)));
            var changed = new OpeningCount(count.Id, count.CompanyId, count.LocationId, count.CountedOn,
                "SYN-SHEET-LEGACY-HASH-CHANGED", legacyCount.Lines);
            Assert.Equal(InventoryStoreCodes.OpeningConflict,
                (await Assert.ThrowsAsync<InventoryStoreException>(() =>
                    a.Opening.RecordAsync(changed, Metadata(run)))).Code);
        }

        Assert.Equal(legacyHash, await run.ScalarAsync<string>(
            "SELECT ContentHash FROM inventory.OpeningCounts WHERE Id = @id", ("@id", count.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Id = @id AND ProductVersion IS NULL",
            ("@id", count.Lines[0].Label.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'inventory.opening-count' AND SourceId = @id",
            ("@id", count.Id)));
    }

    /// <summary>
    /// Opening is never an adjustment: a piece whose label is already stored is refused and the whole count
    /// writes nothing — with stock <c>inventory_label_already_in_stock</c> (a book/count difference is ST-05
    /// with its second approval), without stock <c>inventory_label_conflict</c> (opening registers new pieces).
    /// </summary>
    [SqlServerFact]
    public async Task An_opening_never_adjusts_a_stored_label_and_a_refused_count_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var stocked = NewLabel(CompanyA, "SYN-OPEN-STOCKED");
        var known = NewLabel(CompanyA, "SYN-OPEN-KNOWN");
        await using (var a = run.Store(CompanyA))
        {
            await a.Labels.SaveLabelAsync(stocked, Metadata(run));
            await a.Labels.SaveLabelAsync(known, Metadata(run));
        }

        await using (var a = run.Store(CompanyA))
        {
            await a.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Receipt, In(Position(CompanyA, location, stocked, BlockState.Free), 10m)), Metadata(run));
        }

        var lotA = new MaterialLot(LotA, CompanyA, Product);
        var fresh = NewLabel(CompanyA, "SYN-OPEN-FRESH");
        foreach (var (existing, expected) in new[] { (stocked, InventoryRuleCodes.LabelAlreadyInStock), (known, InventoryStoreCodes.LabelConflict) })
        {
            var count = new OpeningCount(Guid.NewGuid(), CompanyA, location, new DateOnly(2026, 9, 19), "SYN-SHEET-OPEN-2",
            [
                new(Guid.NewGuid(), lotA, fresh, Kilograms.Positive(5m)),
                new(Guid.NewGuid(), lotA, existing, Kilograms.Positive(5m)),
            ]);
            await using var a = run.Store(CompanyA);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => a.Opening.RecordAsync(count, Metadata(run)));
            Assert.Equal(expected, error switch
            {
                InventoryRuleException rule => rule.Code,
                InventoryStoreException store => store.Code,
                _ => error.GetType().Name,
            });
        }

        await using (var a = run.Store(CompanyA))
        {
            Assert.Null(await a.Labels.FindLabelAsync(fresh.Id));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCounts"));
        Assert.Equal(10m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id", ("@id", stocked.Id)));
    }

    /// <summary>One save: a count whose second piece's code is already taken leaves no lot, label, receipt or count behind.</summary>
    [SqlServerFact]
    public async Task An_opening_count_is_written_whole_or_not_at_all()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        await using (var a = run.Store(CompanyA))
        {
            await a.Labels.SaveLabelAsync(NewLabel(CompanyA, "SYN-OPEN-TAKEN"), Metadata(run));
        }

        var lot = new MaterialLot(Guid.NewGuid(), CompanyA, Product);
        var first = new MaterialLabel(Guid.NewGuid(), CompanyA, "SYN-OPEN-FIRST", Product, lot.Id, "product-v1");
        var count = new OpeningCount(Guid.NewGuid(), CompanyA, location, new DateOnly(2026, 9, 19), "SYN-SHEET-OPEN-3",
        [
            new(Guid.NewGuid(), lot, first, Kilograms.Positive(7m)),
            new(Guid.NewGuid(), lot, new MaterialLabel(Guid.NewGuid(), CompanyA, "SYN-OPEN-TAKEN", Product, lot.Id, "product-v1"), Kilograms.Positive(7m)),
        ]);
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryStoreCodes.LabelCodeTaken, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => a.Opening.RecordAsync(count, Metadata(run)))).Code);
        }

        Assert.Equal((0, 0, 0, 0), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCounts"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Lots WHERE Id = @id", ("@id", lot.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Labels WHERE Id = @id", ("@id", first.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE SourceKind = N'inventory.opening-count'")));
    }

    /// <summary>
    /// Company B can neither record A's count nor open without its own approved kg profile (fail-closed); and
    /// without the store the schema refuses a count at another company's location.
    /// </summary>
    [SqlServerFact]
    public async Task Company_B_cannot_open_into_A_and_opens_nothing_without_its_own_kg_profile()
    {
        await using var run = await Run.StartAsync();
        var locationA = await run.LocationOfAsync(CompanyA);
        var locationB = await run.LocationOfAsync(CompanyB);
        var lotA = new MaterialLot(LotA, CompanyA, Product);
        var lotB = new MaterialLot(LotB, CompanyB, Product);
        var countOfA = new OpeningCount(Guid.NewGuid(), CompanyA, locationA, new DateOnly(2026, 9, 19), "SYN-SHEET-A",
            [new(Guid.NewGuid(), lotA, NewLabel(CompanyA, "SYN-OPEN-A"), Kilograms.Positive(1m))]);
        var countOfB = new OpeningCount(Guid.NewGuid(), CompanyB, locationB, new DateOnly(2026, 9, 19), "SYN-SHEET-B",
            [new(Guid.NewGuid(), lotB, NewLabel(CompanyB, "SYN-OPEN-B"), Kilograms.Positive(1m))]);

        await using (var b = run.Store(CompanyB))
        {
            Assert.Equal(InventoryStoreCodes.CompanyContextMismatch, (await Assert.ThrowsAsync<InventoryStoreException>(
                () => b.Opening.RecordAsync(countOfA, Metadata(run)))).Code);
            // B has no approved kilogram profile (only GDC does): refused with the Settings owner's own code.
            Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<InventoryStoreException>(
                () => b.Opening.RecordAsync(countOfB, Metadata(run)))).Code);
        }

        await using (var raw = run.Context(CompanyB))
        {
            raw.OpeningCounts.Add(new OpeningCountRow
            {
                Id = Guid.NewGuid(), CompanyId = CompanyB, LocationId = locationA, CountedOn = new DateOnly(2026, 9, 19),
                SheetReference = "SYN-RAW", ContentHash = new string('0', 64), RecordedAtUtc = At, RecordedBy = "synthetic-actor",
                CorrelationId = run.Correlation,
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
            var sql = Assert.IsType<SqlException>(error.InnerException);
            Assert.Equal(547, sql.Number);
            Assert.Contains("FK_OpeningCounts_Locations_CompanyId_LocationId", sql.Message, StringComparison.Ordinal);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCounts"));
    }

    [SqlServerFact]
    public async Task The_same_lot_replays_another_content_conflicts_and_a_racing_first_arrival_leaves_one()
    {
        await using var run = await Run.StartAsync();
        var lot = new MaterialLot(Guid.NewGuid(), CompanyA, Product, coilNumber: "SYN-COIL-1");
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Lots.SaveLotAsync(lot, [], Metadata(run)));
            Assert.Equal(InventoryWriteOutcome.Replayed, await a.Lots.SaveLotAsync(
                new MaterialLot(lot.Id, CompanyA, Product, coilNumber: "SYN-COIL-1"), [], Metadata(run)));
            // Q3 (Business #66 5734060341): no path changes a stored lot's heat or coil; another content is a conflict.
            Assert.Equal(InventoryStoreCodes.LotConflict, (await Assert.ThrowsAsync<InventoryStoreException>(() => a.Lots.SaveLotAsync(
                new MaterialLot(lot.Id, CompanyA, Product, heatNumber: "SYN-HEAT-LATER", coilNumber: "SYN-COIL-1"), [],
                Metadata(run)))).Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.MaterialLot' AND EntityId = @id",
            ("@id", lot.Id.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Lots WHERE Id = @id AND HeatNumber IS NULL", ("@id", lot.Id)));

        // Two first arrivals of one new lot pass the lookup and wait for each other at the save: one wins, the
        // other stores nothing, and its retry is the winner's replay.
        var racing = new MaterialLot(Guid.NewGuid(), CompanyA, Product);
        var barrier = new Barrier(2);
        async Task<object> ArriveAsync(string code)
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return await writer.Lots.SaveLotAsync(racing,
                    [new MaterialLabel(Guid.NewGuid(), CompanyA, code, Product, racing.Id, "product-v1")], Metadata(run));
            }
            catch (InventoryStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => ArriveAsync("SYN-RACE-1")), Task.Run(() => ArriveAsync("SYN-RACE-2")));

        Assert.Single(results, result => result is InventoryWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == InventoryStoreCodes.ConcurrentChange);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Lots WHERE Id = @id", ("@id", racing.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Labels WHERE LotId = @id", ("@id", racing.Id)));
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryWriteOutcome.Replayed, await a.Lots.SaveLotAsync(racing, [], Metadata(run)));
        }
    }

    [SqlServerFact]
    public async Task Unknown_heat_and_coil_stay_null_and_a_text_that_does_not_fit_is_refused_not_cut()
    {
        await using var run = await Run.StartAsync();
        var unknown = new MaterialLot(Guid.NewGuid(), CompanyA, Product, heatNumber: "  ", coilNumber: null);
        await using (var a = run.Store(CompanyA))
        {
            await a.Lots.SaveLotAsync(unknown, [], Metadata(run));
            Assert.Equal(InventoryStoreCodes.ValueDoesNotFit, (await Assert.ThrowsAsync<InventoryStoreException>(() => a.Lots.SaveLotAsync(
                new MaterialLot(Guid.NewGuid(), CompanyA, Product, heatNumber: new string('H', MaterialLot.MaximumTraceLength + 1)), [],
                Metadata(run)))).Code);
        }

        // Null, never an empty text or a zero (Ö3: nothing is generated or defaulted).
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Lots WHERE Id = @id AND HeatNumber IS NULL AND CoilNumber IS NULL", ("@id", unknown.Id)));

        // The schema refuses an empty text too, without the domain.
        await using (var raw = run.Context(CompanyA))
        {
            raw.Lots.Add(new LotRow
            {
                Id = Guid.NewGuid(), CompanyId = CompanyA, ProductId = Product, HeatNumber = string.Empty, Version = 1,
                CreatedAtUtc = At, CreatedBy = "synthetic-actor",
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
            var sql = Assert.IsType<SqlException>(error.InnerException);
            Assert.Equal(547, sql.Number);
            Assert.Contains("CK_Lots_Trace", sql.Message, StringComparison.Ordinal);
        }
    }

    private static LabelRow RawLabel(Guid company, Guid lot) => new()
    {
        Id = Guid.NewGuid(), CompanyId = company, Code = $"SYN-RAW-{Guid.NewGuid():N}", ProductId = Product, LotId = lot,
        RequiredDocumentsJson = "[]", Version = 1, CreatedAtUtc = At, UpdatedAtUtc = At,
    };

    private static MaterialLabel NewLabel(Guid company, string code) =>
        new(Guid.NewGuid(), company, code, Product, company == CompanyB ? LotB : LotA, "product-v1");

    private static StockPosition Position(Guid company, Guid location, MaterialLabel label, BlockState block) =>
        new(company, new StockOwner(StockPartyKind.Company, company), new StockCustodian(StockPartyKind.Company, company),
            location, label, block);

    private static StockMovementLine In(StockPosition position, decimal kilograms) =>
        new(position, StockDirection.In, Kilograms.Positive(kilograms));

    private static StockMovementLine Out(StockPosition position, decimal kilograms) =>
        new(position, StockDirection.Out, Kilograms.Positive(kilograms));

    private static StockMovement Movement(Guid company, StockMovementKind kind, params StockMovementLine[] lines) =>
        Movement(company, kind, new StockSourceReference("synthetic.source", Guid.NewGuid()), lines);

    private static StockMovement Movement(Guid company, StockMovementKind kind, StockSourceReference source,
        params StockMovementLine[] lines) =>
        new(Guid.NewGuid(), company, kind, At, source, lines);

    private static MovementRow RawMovement(Run run, StockSourceReference source, int grain) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = CompanyA,
        Kind = (int)StockMovementKind.Receipt,
        OccurredAtUtc = At,
        SourceKind = source.Kind,
        SourceId = source.Id,
        SourceLineId = source.LineId,
        SourceGrain = grain,
        CostStatus = 1,
        PayloadHash = new string('0', 64),
        RecordedAtUtc = At,
        RecordedBy = "synthetic-actor",
        CorrelationId = run.Correlation,
    };

    private static InventoryCommandMetadata Metadata(Run run) => new(
        run.Actor.ToString("D"), "synthetic.inventory.test-write", ["SYNTHETIC-INVENTORY-TEST"], run.Correlation,
        CausationId: null, IdempotencyKey: Guid.NewGuid().ToString("N"), SourceChannel: "Web");

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public Guid Actor { get; } = Guid.NewGuid();
        public string Correlation { get; } = $"inv2-{Guid.NewGuid():N}";

        /// <param name="migrateInventory">False: the host's modules only; the test runs Inventory's migrations itself.</param>
        public static async Task<Run> StartAsync(bool migrateInventory = true)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            // The host migrates and seeds Organization, Audit and the rest; Inventory's own
            // migration runs after it, as its foreign keys need Organization's tables.
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            if (!migrateInventory)
            {
                return run;
            }

            await using (var context = run.Context(CompanyA))
            {
                await context.Database.MigrateAsync();
            }

            // Each company's synthetic lot, under its own correlation so no test's audit count sees it.
            foreach (var (company, lot) in new[] { (CompanyA, LotA), (CompanyB, LotB) })
            {
                await using var scope = run.Store(company);
                await scope.Lots.SaveLotAsync(new MaterialLot(lot, company, Product), [],
                    Metadata(run) with { CorrelationId = $"inv-lot-seed-{Guid.NewGuid():N}" });
            }

            return run;
        }

        public InventoryDbContext Context(Guid company, params IInterceptor[] interceptors)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
                .AddInterceptors(interceptors)
                .Options;
            return new InventoryDbContext(options, companyContext);
        }

        public StoreScope Store(Guid company, params IInterceptor[] extra)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var interceptors = new List<IInterceptor> { new AuditSaveChangesInterceptor(collector, companyContext) };
            interceptors.AddRange(extra);
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
                .AddInterceptors(interceptors)
                .Options;
            var dbContext = new InventoryDbContext(options, companyContext);
            var settings = new ApprovedSettingsPrerequisiteProvider(companyContext, new FixedCurrentUser(Actor, company));
            var store = new InventoryStore(dbContext, companyContext, collector, new InventoryKilogramScale(settings),
                TimeProvider.System);
            return new StoreScope(dbContext, store);
        }

        public async Task<Guid> LocationOfAsync(Guid company) =>
            await ScalarAsync<Guid>("SELECT TOP (1) Id FROM organization.Locations WHERE CompanyId = @c ORDER BY Id",
                ("@c", company));

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class StoreScope(InventoryDbContext dbContext, InventoryStore store) : IAsyncDisposable
    {
        public IInventoryLotStore Lots => store;
        public IInventoryLabelStore Labels => store;
        public IInventoryMovementStore Movements => store;
        public IInventoryOpeningStore Opening => store;
        public IInventoryTransferReaderV1 Transfers => store;
        public InventoryDbContext DbContext => dbContext;
        public InventoryStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>Holds each save until both writers have reached it, so both read the same stock first.</summary>
    private sealed class BarrierInterceptor(Barrier barrier) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixedCurrentUser(Guid userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
