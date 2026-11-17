using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F2 (SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1 §F2, SAT-D309/D312, LOJ-D9/D12) against a REAL SQL Server and the REAL host
/// composition: a subcontract service invoice draft billing the delivery weighing's net kg is recorded through the real Sales store
/// in one save (invoice, service line, attempt, audit); the same key replays it; a second invoice of the same weighing for the same
/// order under another key loses on UX_SubcontractServiceInvoices_Weighing (one record, nothing of the loser stays); the database
/// refuses a non-draft state and a source without a weighing revision; another company never finds it. The migration refuses to roll
/// back over a recorded invoice (THROW 51732, forward fix). No other module's table is written. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class SalesSubcontractServiceInvoiceSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 11, 15);
    private static readonly Guid Currency = Guid.Parse("7e3f4051-6c7b-4d8e-9fa0-1b2c3d4e5f11");
    private static readonly Guid Profile = Guid.Parse("7e3f4051-6c7b-4d8e-9fa0-1b2c3d4e5f12");
    private static readonly Guid Kg = Guid.Parse("7e3f4051-6c7b-4d8e-9fa0-1b2c3d4e5f13");
    private static readonly Guid Product = Guid.Parse("7e3f4051-6c7b-4d8e-9fa0-1b2c3d4e5f14");
    private static readonly Guid Order = Guid.Parse("7e3f4051-6c7b-4d8e-9fa0-1b2c3d4e5f15");
    private static readonly Guid OrderLine = Guid.Parse("7e3f4051-6c7b-4d8e-9fa0-1b2c3d4e5f16");
    private static readonly Guid Instruction = Guid.Parse("7e3f4051-6c7b-4d8e-9fa0-1b2c3d4e5f17");
    private static readonly Guid Weighing = Guid.Parse("7e3f4051-6c7b-4d8e-9fa0-1b2c3d4e5f18");
    private const string Preparer = "SENTETIK-subcontract-invoice-preparer";
    private const string NetKg = "1234.560";

    [SqlServerFact]
    public async Task A_subcontract_service_invoice_is_recorded_once_per_weighing_and_order_in_one_save_and_another_company_never_finds_it()
    {
        await using var run = await Run.StartAsync();
        var stored = await run.StoreAsync("k-create", Weighing);
        Assert.Equal(SalesDraftWriteOutcome.Recorded, stored.Outcome);
        var id = stored.Invoice.Id;
        Assert.StartsWith(SalesStore.SubcontractServiceInvoiceSeries + "-", stored.Invoice.DocumentNo, StringComparison.Ordinal);

        // One save: the invoice (draft, version 1, the weighing's verbatim net kg), its service line, its attempt and its audit.
        Assert.Equal((1, 1, 1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[SubcontractServiceInvoices] WHERE [Id] = @id AND [State] = 1 "
                + "AND [Version] = 1 AND [NetKgText] = @kg AND [WeighingId] = @weighing", ("@id", id), ("@kg", NetKg), ("@weighing", Weighing)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[SubcontractServiceInvoiceLines] WHERE [InvoiceId] = @id "
                + "AND [Quantity] = 1234.560 AND [NetAmount] = 3086.40 AND [VatAmount] = 617.28", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[SubcontractServiceInvoiceAttempts] WHERE [InvoiceId] = @id "
                + "AND [ActorId] = @actor", ("@id", id), ("@actor", Preparer)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Sales.SubcontractServiceInvoice' "
                + "AND [EventType] = N'sales.subcontract-service-invoice.created.v1' AND [ActorId] = @actor", ("@actor", Preparer))));

        // The same key replays the stored invoice and writes nothing again.
        var replay = await run.StoreAsync("k-create", Weighing);
        Assert.Equal((SalesDraftWriteOutcome.Replayed, id), (replay.Outcome, replay.Invoice.Id));

        // LOJ-D12: another key for the same weighing and order loses on the unique index; only the first invoice stays.
        var lost = await Assert.ThrowsAsync<SalesDraftStoreException>(() => run.StoreAsync("k-second", Weighing));
        Assert.Equal(SalesDraftStoreCodes.ConcurrentChange, lost.Code);
        Assert.Equal((1, 1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[SubcontractServiceInvoices]"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[SubcontractServiceInvoiceLines]"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[SubcontractServiceInvoiceAttempts]")));

        // Lifecycle transitions require their version advance; the original source revision constraint remains.
        Assert.Equal(51997, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[SubcontractServiceInvoices] SET [State] = 2 WHERE [Id] = @id; SELECT 1", ("@id", id)))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[SubcontractServiceInvoices] SET [WeighingRevision] = 0 WHERE [Id] = @id; SELECT 1", ("@id", id)))).Number);

        await using (var scope = run.SalesStore(CompanyA))
        {
            var found = await scope.Store.FindSubcontractServiceInvoiceByWeighingAsync(Order, Instruction, Weighing);
            Assert.Equal((id, NetKg), (found!.Id, found.Source.NetKgText));
        }

        // Another company neither finds the invoice by id nor by its weighing key.
        await using (var other = run.SalesStore(CompanyB))
        {
            Assert.Null(await other.Store.FindSubcontractServiceInvoiceAsync(id));
            Assert.Null(await other.Store.FindSubcontractServiceInvoiceByWeighingAsync(Order, Instruction, Weighing));
        }
    }

    [SqlServerFact]
    public async Task The_subcontract_service_invoice_migrations_Down_passes_with_no_invoice_and_refuses_51732_while_one_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddSubcontractServiceInvoices().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + " SELECT 1");

        await run.StoreAsync("k-create", Weighing);

        Assert.Equal(51732, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1"))).Number);
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, id => id.EndsWith("_AddSubcontractServiceInvoices", StringComparison.Ordinal));
        Assert.True(index > 0);
        var migrator = context.GetService<IMigrator>();
        // SC34 history is forward-fix as well; rolling through it cannot discard the newer lifecycle evidence.
        var rollback = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[index - 1]));
        Assert.Equal(51994, rollback.Number);
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[SubcontractServiceInvoices]"));
    }

    private static string Hash(char fill) => new(fill, 64);

    private static SalesLineTaxSnapshot Tax() =>
        new(Product, Day, "Normal", "Service", "Resolved", null, "Record", Guid.Parse("7e3f4051-6c7b-4d8e-9fa0-1b2c3d4e5f19"), 1,
            "Resolved", 20m, "NotRequired", null, null, "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);

    private static SalesSubcontractServiceInvoice Invoice(string documentNo, Guid weighing, DateTimeOffset now) =>
        SalesSubcontractServiceInvoice.Create(Guid.NewGuid(), CompanyA, documentNo,
            new SalesSubcontractServiceInvoiceHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Profile, "3", 2, Day),
            new SalesSubcontractServiceInvoiceSource(Order, Instruction, weighing, 1, 1, NetKg),
            [new SalesSubcontractServiceLineInput(Guid.NewGuid(), OrderLine, 1, Product, Kg,
                decimal.Parse(NetKg, CultureInfo.InvariantCulture), NetKg, new CommercialDraftFields("2.50", Kg, null), Tax())],
            decimal.Parse(NetKg, CultureInfo.InvariantCulture), Preparer, now);

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public SalesStore Store => store;
        public void SetCommandTimeout(int seconds) => dbContext.Database.SetCommandTimeout(seconds);
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
        public string Correlation { get; } = $"sales-f2-{Guid.NewGuid():N}";

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

        /// <summary>A subcontract service invoice of company A for the weighing, through the real store (same content per call).</summary>
        public async Task<SalesSubcontractServiceInvoiceStoreResult> StoreAsync(string key, Guid weighing)
        {
            await using var scope = SalesStore(CompanyA);
            return await scope.Store.StoreSubcontractServiceInvoiceAsync(
                Envelope(key, Preparer, SalesPermissionManifestV1.InvoicesWrite), Hash('c'),
                number => Invoice(number, weighing, TimeProvider.System.GetUtcNow()));
        }

        public SalesDraftWriteEnvelope Envelope(string key, string actor, string permission) =>
            new(actor, permission, ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public CompanyContext Company(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return companyContext;
        }

        public SalesDbContext SalesContext(Guid company) =>
            new(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, Company(company));

        public SalesScope SalesStore(Guid company)
        {
            var companyContext = Company(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            // These facts isolate the real Sales SQL store. Logistics owner integration is covered separately.
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System,
                new SyntheticCommitWeighingRead()));
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

    private sealed class SyntheticCommitWeighingRead : ISalesSubcontractWeighingCommitReadPortV1
    {
        public Task<SalesSubcontractDeliveryWeighingReadV1> ReadAsync(System.Data.Common.DbConnection connection,
            System.Data.Common.DbTransaction transaction, Guid companyId, Guid instructionId, Guid weighingId,
            CancellationToken cancellationToken = default) => Task.FromResult(new SalesSubcontractDeliveryWeighingReadV1(
                SalesSubcontractDeliveryWeighingStatusV1.Available, companyId, instructionId, weighingId,
                new(NetKg, 1, 1, [new(Order, OrderLine, Product, Kg, NetKg)])));
    }
}
