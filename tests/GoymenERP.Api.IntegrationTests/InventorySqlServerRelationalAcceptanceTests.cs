using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// INV-2 against a REAL SQL Server: Inventory's own migration on top of the host's migrated and
/// seeded database, the real approved Settings kilogram profile (GDC = company A) and Audit's real
/// save interceptor. Every identity below is synthetic test data in this test's own database; no
/// product, label or stock is seeded anywhere.
/// </summary>
public sealed class InventorySqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Product = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a61");
    private static readonly Guid Lot = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a62");
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

    private static MaterialLabel NewLabel(Guid company, string code) => new(Guid.NewGuid(), company, code, Product, Lot);

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

        public static async Task<Run> StartAsync()
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
            await using var context = run.Context(CompanyA);
            await context.Database.MigrateAsync();
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
        public IInventoryLabelStore Labels => store;
        public IInventoryMovementStore Movements => store;

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
