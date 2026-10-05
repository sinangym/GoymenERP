using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class FinanceOfficialInvoiceImportWorkDeliverySqlServerRelationalAcceptanceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Batch = Guid.Parse("91000000-0000-0000-0000-000000000001");

    [SqlServerFact]
    public async Task Upload_and_approval_deliveries_are_atomic_coherent_and_replay_stable()
    {
        await using var run = await Run.StartAsync();
        Guid uploadDelivery;
        await using (var maker = run.Service("SENTETIK-maker"))
        {
            var first = await maker.Service.UploadAsync(maker.Context, new CommandMetadata("upload-key"), Request(), default);
            var replay = await maker.Service.UploadAsync(maker.Context, new CommandMetadata("upload-key"), Request(), default);
            Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
            Assert.True(replay.IsReplay);
            uploadDelivery = await run.ScalarAsync<Guid>(
                "SELECT DeliveryId FROM finance.OfficialInvoiceImportWorkDeliveries WHERE BatchId = @id AND ObservationOrdinal = 1",
                ("@id", Batch));
            var observed = await maker.Service.GetAsync(maker.Context, Batch, 1, uploadDelivery, default);
            Assert.Equal(ReadOutcome.Found, observed.Outcome);
            Assert.Equal(FinanceOfficialInvoiceImportWorkStateV1.NeedsAction, observed.Resource!.DesiredState);
            Assert.Equal(Batch, observed.Resource.EvidenceId);
        }

        await using (var checker = run.Service("SENTETIK-checker"))
        {
            var request = new FinanceOfficialInvoiceImportApproveRequestV1(Batch, "1");
            var first = await checker.Service.ApproveAsync(checker.Context, new CommandMetadata("approve-key"), request, default);
            var replay = await checker.Service.ApproveAsync(checker.Context, new CommandMetadata("approve-key"), request, default);
            Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
            Assert.True(replay.IsReplay);
            var delivery = await run.ScalarAsync<Guid>(
                "SELECT DeliveryId FROM finance.OfficialInvoiceImportWorkDeliveries WHERE BatchId = @id AND ObservationOrdinal = 2",
                ("@id", Batch));
            var observed = await checker.Service.GetAsync(checker.Context, Batch, 2, delivery, default);
            Assert.Equal(ReadOutcome.Found, observed.Outcome);
            Assert.Equal(FinanceOfficialInvoiceImportWorkStateV1.Satisfied, observed.Resource!.DesiredState);
            Assert.NotEqual(Batch, observed.Resource.EvidenceId);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportCommands"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportWorkDeliveries"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.OfficialInvoiceImportBatch'"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialInvoiceImportWorkDeliveries WHERE DATALENGTH(PayloadSha256) <> 64 OR PayloadSha256 COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9a-f]%'"));
    }

    [SqlServerFact]
    public async Task Work_delivery_migration_refuses_occupied_down_and_preserves_the_fact()
    {
        await using var run = await Run.StartAsync();
        await using (var maker = run.Service("SENTETIK-maker"))
        {
            Assert.Equal(CommandOutcome.Succeeded,
                (await maker.Service.UploadAsync(maker.Context, new CommandMetadata("guard-key"), Request(), default)).Outcome);
        }

        await using var context = run.Context();
        var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>()
            .MigrateAsync("20260930185113_AddOfficialInvoiceImportApprovalV1"));

        Assert.Equal(51051, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportWorkDeliveries"));
        Assert.EndsWith("_AddOfficialInvoiceImportWorkDeliveryV1",
            (await context.Database.GetAppliedMigrationsAsync()).Last(), StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task Work_delivery_migration_rolls_back_and_reapplies_when_empty()
    {
        await using var run = await Run.StartAsync();
        await using var context = run.Context();
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync("20260930185113_AddOfficialInvoiceImportApprovalV1");
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') AND name = N'OfficialInvoiceImportWorkDeliveries'"));
        await context.Database.MigrateAsync();
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') AND name = N'OfficialInvoiceImportWorkDeliveries'"));
    }

    private static FinanceOfficialInvoiceImportPreviewRequestV1 Request()
    {
        var csv = string.Join('\n', FinanceOfficialInvoiceImportTemplateV1.Header,
            $"{Guid.Parse("92000000-0000-0000-0000-000000000002"):D},{Guid.Parse("93000000-0000-0000-0000-000000000003"):D},{Guid.Parse("94000000-0000-0000-0000-000000000004"):D},invoice-work,{Guid.Parse("95000000-0000-0000-0000-000000000005"):D},{Guid.Parse("96000000-0000-0000-0000-000000000006"):D},{Guid.Parse("97000000-0000-0000-0000-000000000007"):D},1250.50,2026-10-31");
        return new(Batch, "official-file-v1", Encoding.UTF8.GetBytes(csv));
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;
        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        private SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context();
            await context.Database.MigrateAsync();
            return run;
        }

        public FinanceDbContext Context()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new FinanceDbContext(Options(), company);
        }

        public Scope Service(string actor)
        {
            var company = new CompanyContext();
            company.Set(Company);
            var collector = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<FinanceDbContext>(Options())
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options;
            var db = new FinanceDbContext(options, company);
            var service = new FinanceOfficialInvoiceImportApprovalServiceV1(db, company, new User(actor),
                new FinanceOfficialInvoiceImportParserV1(), new Authority(), new ReadAuthority(), collector,
                new FinanceOfficialInvoiceImportAuditEnvelopesV1(
                    new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Upload, [], "Integration"),
                    new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Approve, [], "Integration")),
                TimeProvider.System);
            return new Scope(db, service, new FinanceContext(Company, actor, $"work-{Guid.NewGuid():N}", null));
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Factory.DatabaseName}]; END;";
            await command.ExecuteNonQueryAsync();
        }

        private DbContextOptions<FinanceDbContext> Options() => new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema)).Options;
    }

    private sealed class Scope(FinanceDbContext db, FinanceOfficialInvoiceImportApprovalServiceV1 service,
        FinanceContext context) : IAsyncDisposable
    {
        public FinanceOfficialInvoiceImportApprovalServiceV1 Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class User(string actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority : IFinanceOfficialInvoiceImportApprovalAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context,
            FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class ReadAuthority : IFinanceOpenItemSourceAuthority
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, Guid batchId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }
}
