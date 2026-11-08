using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D17 / SAT-D168 / SAT-D215 against a REAL SQL Server and the REAL host composition: a recorded line closure is stored in
/// one save through the real Sales store, rebuilt by the store's restore (counted by the version floor), refused a second time
/// for the same line, held by the identity check and the draft / line foreign keys, and the migration refuses to roll back over a
/// recorded closure (THROW 51228, forward fix). SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesOrderLineClosureSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    // A business day on which GDC's (company A) approved SAT-D5 tolerance (1 / 2 / 3 %) is in force (SAT-D14).
    private static readonly DateOnly Day = new(2026, 10, 26);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7a01");
    private static readonly Guid MoneyProfile = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7a02");
    private static readonly Guid KgProfile = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7a03");
    private static readonly Guid Product = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7a04");
    private const string Shipper = "SENTETIK-shipper";

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit LineClosed =
        new("sales.order-draft.line-closed.v1", "CloseLine", "SENTETIK satır kapatıldı");

    [SqlServerFact]
    public async Task The_line_closure_migrations_Down_passes_with_no_closure_and_refuses_while_a_closure_exists()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        var guard = Assert.IsType<SqlOperation>(new AddDraftLineClosures().DownOperations[0]).Sql;
        // No closure: the guard passes (an empty table rolls back freely).
        await run.ScalarAsync<int>(guard + " SELECT 1");

        // One closure through the real Sales store: the domain's input as the command builds it (90 of 100 kg shipped, 10 % short).
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.CloseLineAsync(run.SalesEnvelope("k-close", "SENTETIK-closer"), Hash("k-close"),
                SalesDraftCommands.CloseLineV1, draft.Id,
                (d, input, now) => d.CloseLine(4, "SENTETIK-closer", input, now), LineClosed,
                (_, _) => Task.FromResult(new LineClosureInput(line.Id, 0, "100", 90m, DeliveryToleranceBand.Approval, [Shipper])));
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1"));
        Assert.Equal(51228, error.Number);
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var closuresIndex = Array.FindIndex(applied, id => id.EndsWith("_AddDraftLineClosures", StringComparison.Ordinal));
        Assert.True(closuresIndex > 0);
        // Whatever migrations follow the closure one are rolled back first (they hold no data here), so the guard under test is the next Down.
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(applied[closuresIndex]);
        Assert.Equal(applied[..(closuresIndex + 1)], await context.Database.GetAppliedMigrationsAsync());
        var rollback = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[closuresIndex - 1]));
        Assert.Equal(51228, rollback.Number);
        Assert.Equal(applied[..(closuresIndex + 1)], await context.Database.GetAppliedMigrationsAsync());
        // Forward again: the later migrations are re-applied and the closure row survived the refused Down.
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id)));
        Assert.Equal((5L, "100", "90", "10"), (
            await run.ScalarAsync<long>("SELECT [DraftVersion] FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [Ordered] FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [ShippedAtClosure] FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [CancelledRemainder] FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id))));

        // The rebuilt order carries its closure (the store's restore through the domain, counted by the version floor).
        await using (var reader = run.SalesStore(CompanyA))
        {
            var reread = await reader.Store.FindDraftAsync(draft.Id);
            var closure = Assert.Single(reread!.LineClosures);
            Assert.Equal((5L, "SENTETIK-closer", true), (reread.Version, closure.ClosedBy, reread.IsLineClosed(line.Id)));
        }

        // The database refuses a second closure of the same line, one under another company, one at a version below the floor and
        // one at the version the first closure already took.
        const string insert = "INSERT INTO [sales].[DraftLineClosures] ([CompanyId],[DraftId],[LineId],[LineNo],[BasisRevision],[Ordered],"
            + "[ShippedAtClosure],[CancelledRemainder],[Band],[ClosedBy],[ClosedAtUtc],[DraftVersion],[CorrelationId]) "
            + "VALUES (@company, @id, @line, 1, 0, N'100', N'90', N'10', 2, N'x', SYSDATETIMEOFFSET(), @version, N'x'); SELECT 1";
        Assert.Equal(2627, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(insert,
            ("@company", CompanyA), ("@id", draft.Id), ("@line", line.Id), ("@version", 6L)))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(insert,
            ("@company", CompanyB), ("@id", draft.Id), ("@line", line.Id), ("@version", 6L)))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(insert,
            ("@company", CompanyA), ("@id", draft.Id), ("@line", Guid.NewGuid()), ("@version", 4L)))).Number);
    }

    [SqlServerFact]
    public async Task A_line_closure_is_stored_in_one_save_with_its_attempt_and_audit_and_the_same_key_replays_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        var attemptsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]");
        var envelope = run.SalesEnvelope("k-close", "SENTETIK-closer");
        var closures = 0;

        await using (var scope = run.SalesStore(CompanyA))
        {
            var recorded = await scope.Store.CloseLineAsync(envelope, Hash("k-close"), SalesDraftCommands.CloseLineV1, draft.Id,
                (d, input, now) => d.CloseLine(4, "SENTETIK-closer", input, now), LineClosed,
                (_, _) => Task.FromResult(new LineClosureInput(line.Id, 0, "100", 90m, DeliveryToleranceBand.Approval, [Shipper])));
            Assert.Equal((SalesDraftWriteOutcome.Recorded, 5L), (recorded!.Outcome, recorded.Draft.Version));
        }

        // One save: the closure row, header version + 1, the attempt and the audit event (with the shipper) are all there.
        Assert.Equal((1, 5L), (await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        Assert.Equal(attemptsBefore + 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @command AND [Outcome] = 1 AND [DraftId] = @id",
            ("@command", SalesDraftCommands.CloseLineV1), ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.line-closed.v1' AND [EntityId] = @id "
            + "AND [EntityVersionBefore] = 4 AND [EntityVersionAfter] = 5 AND [AfterJson] LIKE N'%' + @shipper + N'%'",
            ("@id", draft.Id.ToString("D")), ("@shipper", Shipper)));

        // The same key and content replays the stored answer and writes nothing again.
        await using (var scope = run.SalesStore(CompanyA))
        {
            var replay = await scope.Store.CloseLineAsync(envelope, Hash("k-close"), SalesDraftCommands.CloseLineV1, draft.Id,
                (_, _, _) => throw new InvalidOperationException("A replay does not mutate."), LineClosed,
                (_, _) => throw new InvalidOperationException("A replay asks no owner."));
            closures = replay!.Draft.LineClosures.Count;
            Assert.Equal(SalesDraftWriteOutcome.Replayed, replay.Outcome);
        }

        Assert.Equal((1, 1, attemptsBefore + 1), (closures,
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]")));
    }

    [SqlServerFact]
    public async Task A_recorded_line_closure_refusal_is_stored_under_the_key_and_replays_without_closing_anything_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var envelope = run.SalesEnvelope("k-refuse", "SENTETIK-closer");
        var refusal = new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.LineClosureSelfClosure,
            [new SalesDraftFieldError("shipped", SalesDraftProblemCodesV1.LineClosureSelfClosure)]);

        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.RecordRefusalAsync(envelope, Hash("k-refuse"), refusal, CancellationToken.None, SalesDraftCommands.CloseLineV1, draft.Id);
            var replay = await scope.Store.FindReplayAsync(envelope, Hash("k-refuse"), CancellationToken.None, SalesDraftCommands.CloseLineV1);
            Assert.Null(replay!.Draft);
            Assert.Equal((409, SalesDraftProblemCodesV1.LineClosureSelfClosure), (replay.Refusal!.Status, replay.Refusal.Code));
        }

        Assert.Equal((0, 4L, 1), (await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @command AND [Outcome] = 2",
                ("@command", SalesDraftCommands.CloseLineV1))));

        // The same key under different content is the payload mismatch, never a second answer.
        await using var other = run.SalesStore(CompanyA);
        var line = Assert.Single(draft.Lines);
        var mismatch = await Assert.ThrowsAsync<SalesDraftStoreException>(() => other.Store.CloseLineAsync(envelope, Hash("k-other"), SalesDraftCommands.CloseLineV1,
            draft.Id, (d, input, now) => d.CloseLine(4, "SENTETIK-closer", input, now), LineClosed,
            (_, _) => Task.FromResult(new LineClosureInput(line.Id, 0, "100", 90m, DeliveryToleranceBand.Approval, [Shipper]))));
        Assert.Equal(SalesDraftStoreCodes.SameKeyPayloadMismatch, mismatch.Code);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id)));
    }

    [SqlServerFact]
    public async Task Another_company_cannot_close_the_line_and_its_closure_is_invisible_to_it_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);

        // Company B's store does not find company A's order: the command answers null (the service's 404) and writes nothing.
        await using (var other = run.SalesStore(CompanyB))
        {
            var foreign = await other.Store.CloseLineAsync(run.SalesEnvelope("k-foreign", "SENTETIK-closer"), Hash("k-foreign"),
                SalesDraftCommands.CloseLineV1, draft.Id, (d, input, now) => d.CloseLine(4, "SENTETIK-closer", input, now), LineClosed,
                (_, _) => Task.FromResult(new LineClosureInput(line.Id, 0, "100", 90m, DeliveryToleranceBand.Approval, [Shipper])));
            Assert.Null(foreign);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLineClosures]"));

        await using (var owner = run.SalesStore(CompanyA))
        {
            await owner.Store.CloseLineAsync(run.SalesEnvelope("k-own", "SENTETIK-closer"), Hash("k-own"), SalesDraftCommands.CloseLineV1,
                draft.Id, (d, input, now) => d.CloseLine(4, "SENTETIK-closer", input, now), LineClosed,
                (_, _) => Task.FromResult(new LineClosureInput(line.Id, 0, "100", 90m, DeliveryToleranceBand.Approval, [Shipper])));
        }

        // The closure carries company A; company B reads the order as absent, so it sees no closure either.
        Assert.Equal(CompanyA, await run.ScalarAsync<Guid>("SELECT [CompanyId] FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id)));
        await using var reader = run.SalesStore(CompanyB);
        Assert.Null(await reader.Store.FindDraftAsync(draft.Id));
    }

    [SqlServerFact]
    public async Task The_line_port_refuses_a_closed_line_as_closed_and_keeps_answering_the_order_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.CloseLineAsync(run.SalesEnvelope("k-close", "SENTETIK-closer"), Hash("k-close"), SalesDraftCommands.CloseLineV1,
                draft.Id, (d, input, now) => d.CloseLine(4, "SENTETIK-closer", input, now), LineClosed,
                (_, _) => Task.FromResult(new LineClosureInput(line.Id, 0, "100", 90m, DeliveryToleranceBand.Approval, [Shipper])));
        }

        var company = new CompanyContext();
        company.Set(CompanyA);
        await using var read = run.SalesStore(CompanyA);
        var port = new SalesOrderLineReferencePort(read.Store, company);
        var answer = await port.GetApprovedLineAsync(draft.Id, line.Id);

        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "Closed"), (answer.Outcome, answer.ApprovalState));
    }

    [SqlServerFact]
    public async Task A_force_closure_after_a_line_closure_is_stored_with_the_closure_triple_and_the_order_loads_again_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.CloseLineAsync(run.SalesEnvelope("k-close", "SENTETIK-closer"), Hash("k-close"), SalesDraftCommands.CloseLineV1,
                draft.Id, (d, input, now) => d.CloseLine(4, "SENTETIK-closer", input, now), LineClosed,
                (_, _) => Task.FromResult(new LineClosureInput(line.Id, 0, "100", 90m, DeliveryToleranceBand.Approval, [Shipper])));
            // The owner now names 95 (shipped moved after the closure): the closed line is still recorded as 100 / 90 / 10.
            await scope.Store.ForceCloseDraftAsync(run.SalesEnvelope("k-force", "SENTETIK-force-closer"), Hash("k-force"), SalesDraftCommands.ForceCloseV1,
                draft.Id, (d, plan, now) => d.ForceClose(5, "SENTETIK-force-closer", "SENTETIK neden", plan.Impact, plan.Lines, now),
                new SalesDraftStateAudit("sales.order-draft.force-closed.v1", "ForceClose", "SENTETIK zorla kapatıldı"),
                (_, _) => Task.FromResult(new SalesForceClosurePlan(new ForceClosureImpactSnapshot(new string('e', 64),
                    [new(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.None, false)]),
                    [new ForceClosureLineInput(line.Id, 95m)])));
        }

        // The save-load round trip: the store rebuilds the closure first, then the force-closure, and the order loads.
        await using var reader = run.SalesStore(CompanyA);
        var reloaded = (await reader.Store.FindDraftAsync(draft.Id))!;
        var snapshot = Assert.Single(reloaded.ForceClosure!.Lines);
        Assert.Equal((SalesDraftState.Closed, 6L, "100", "90", "10"),
            (reloaded.State, reloaded.Version, snapshot.Ordered, snapshot.Shipped, snapshot.ClosedRemainder));
        Assert.Single(reloaded.LineClosures);
    }

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line draft (the given kg quantity) from SYNTHETIC owner snapshots in the approved kg unit: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo, string quantity)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30);
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3)), T0);
        return draft;
    }

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;
        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = "SENTETIK-preparer-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"sales-sat-d17-{Guid.NewGuid():N}";

        /// <summary>The real host composed over a fresh database (health is probed so the migrations have run).</summary>
        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>A stored draft of the company on the business day, submitted and approved (version 4), through the real Sales store.</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix, string quantity)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo, quantity))).Draft;
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"), Hash($"{keyPrefix}-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public SalesDbContext SalesContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

        public SalesScope SalesStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

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
}
