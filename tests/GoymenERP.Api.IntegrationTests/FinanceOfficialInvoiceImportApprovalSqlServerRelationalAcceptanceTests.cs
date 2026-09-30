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

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Real SQL Server persistence evidence for the approved v1 application CSV carrier. Every row and
/// permission in this class is SENTETIK test data; this is not a real invoice file, mapping, grant or UAT.
/// </summary>
public sealed class FinanceOfficialInvoiceImportApprovalSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Batch = Guid.Parse("81000000-0000-0000-0000-000000000001");

    [SqlServerFact]
    public async Task The_uploader_is_refused_then_a_different_authorized_checker_persists_approval_atomically()
    {
        await using var run = await Run.StartAsync();
        await using (var maker = run.Service(Company, "SENTETIK-maker", new AllowAuthority()))
        {
            var upload = await maker.Service.UploadAsync(maker.Context, new CommandMetadata("upload-key"), Request(), default);
            Assert.Equal(CommandOutcome.Succeeded, upload.Outcome);
            Assert.Equal(FinanceOfficialInvoiceImportApprovalStatusV1.Pending, upload.Resource!.Status);

            var selfApproval = await maker.Service.ApproveAsync(maker.Context, new CommandMetadata("self-approve-key"),
                new FinanceOfficialInvoiceImportApproveRequestV1(Batch, "1"), default);
            Assert.Equal(CommandOutcome.ValidationFailed, selfApproval.Outcome);
            Assert.Contains(selfApproval.Issues,
                issue => issue.Field == "approver" && issue.Code == "approver_must_differ_from_uploader");
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportApprovals"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.OfficialInvoiceImportBatch'"));

        await using (var checker = run.Service(Company, "SENTETIK-checker", new AllowAuthority()))
        {
            var approval = await checker.Service.ApproveAsync(checker.Context, new CommandMetadata("approve-key"),
                new FinanceOfficialInvoiceImportApproveRequestV1(Batch, "1"), default);
            Assert.Equal(CommandOutcome.Succeeded, approval.Outcome);
            Assert.Equal("SENTETIK-checker", approval.Resource!.ApprovedBy);
            var source = await checker.Service.GetApprovedAsync(checker.Context,
                new FinanceOpenItemSourceReference(Batch, approval.Resource.BatchVersion, approval.Resource.BatchChecksum), default);
            Assert.Equal(ReadOutcome.Found, source.Outcome);
            Assert.Single(source.Resource!.Rows);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportBatches"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportApprovals"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportCommands"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.OfficialInvoiceImportBatch'"));
        Assert.Equal("SENTETIK-maker", await run.ScalarAsync<string>(
            "SELECT UploadedBy FROM finance.OfficialInvoiceImportBatches WHERE Id = @id", ("@id", Batch)));
        Assert.Equal("SENTETIK-checker", await run.ScalarAsync<string>(
            "SELECT ApprovedBy FROM finance.OfficialInvoiceImportApprovals WHERE BatchId = @id", ("@id", Batch)));
    }

    [SqlServerFact]
    public async Task Concurrent_uploads_and_approvals_keep_one_winner_and_no_loser_side_effects()
    {
        await using var run = await Run.StartAsync();
        using (var uploadBarrier = new Barrier(2))
        await using (var first = run.Service(Company, "SENTETIK-uploader-a", new BarrierAuthority(3, uploadBarrier)))
        await using (var second = run.Service(Company, "SENTETIK-uploader-b", new BarrierAuthority(3, uploadBarrier)))
        {
            var results = await Task.WhenAll(
                first.Service.UploadAsync(first.Context, new CommandMetadata("upload-a"), Request(), default),
                second.Service.UploadAsync(second.Context, new CommandMetadata("upload-b"), Request(), default));
            Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
            Assert.Single(results, result => result.Outcome == CommandOutcome.VersionConflict);
        }

        using (var approvalBarrier = new Barrier(2))
        await using (var first = run.Service(Company, "SENTETIK-checker-a", new BarrierAuthority(3, approvalBarrier)))
        await using (var second = run.Service(Company, "SENTETIK-checker-b", new BarrierAuthority(3, approvalBarrier)))
        {
            var request = new FinanceOfficialInvoiceImportApproveRequestV1(Batch, "1");
            var results = await Task.WhenAll(
                first.Service.ApproveAsync(first.Context, new CommandMetadata("approve-a"), request, default),
                second.Service.ApproveAsync(second.Context, new CommandMetadata("approve-b"), request, default));
            Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
            Assert.Single(results, result => result.Outcome == CommandOutcome.VersionConflict);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportBatches"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportApprovals"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportCommands"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.OfficialInvoiceImportBatch'"));
    }

    [SqlServerFact]
    public async Task A_database_failure_rolls_back_batch_command_and_audit_together()
    {
        await using var run = await Run.StartAsync();
        var unknownCompany = Guid.NewGuid();
        await using (var scope = run.Service(unknownCompany, "SENTETIK-maker", new AllowAuthority()))
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => scope.Service.UploadAsync(
                scope.Context, new CommandMetadata("upload-key"), Request(unknownCompany), default));
            Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialInvoiceImportBatches WHERE CompanyId = @company", ("@company", unknownCompany)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialInvoiceImportCommands WHERE CompanyId = @company", ("@company", unknownCompany)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @company AND EntityType = N'Finance.OfficialInvoiceImportBatch'",
            ("@company", unknownCompany)));
    }

    private static FinanceOfficialInvoiceImportPreviewRequestV1 Request(Guid? company = null)
    {
        var suffix = company?.ToString("N") ?? "known";
        var csv = string.Join('\n', FinanceOfficialInvoiceImportTemplateV1.Header,
            $"{Guid.Parse("82000000-0000-0000-0000-000000000002"):D},{Guid.Parse("83000000-0000-0000-0000-000000000003"):D},{Guid.Parse("84000000-0000-0000-0000-000000000004"):D},invoice-{suffix},{Guid.Parse("85000000-0000-0000-0000-000000000005"):D},{Guid.Parse("86000000-0000-0000-0000-000000000006"):D},{Guid.Parse("87000000-0000-0000-0000-000000000007"):D},1250.50,2026-10-31");
        return new FinanceOfficialInvoiceImportPreviewRequestV1(Batch, "official-file-v1", Encoding.UTF8.GetBytes(csv));
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

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var company = new CompanyContext();
            company.Set(Company);
            await using var context = new FinanceDbContext(Options(factory), company);
            await context.Database.MigrateAsync();
            return run;
        }

        public Scope Service(Guid company, string actor, IFinanceOfficialInvoiceImportApprovalAuthorityV1 authority)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var currentUser = new User(actor, company);
            var collector = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options;
            var db = new FinanceDbContext(options, companyContext);
            var service = new FinanceOfficialInvoiceImportApprovalServiceV1(db, companyContext, currentUser,
                new FinanceOfficialInvoiceImportParserV1(), authority, new ReadAuthority(), collector,
                new FinanceOfficialInvoiceImportAuditEnvelopesV1(
                    new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Upload,
                        ["SENTETIK-FINANCE"], "SqlServerAcceptance"),
                    new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Approve,
                        ["SENTETIK-FINANCE"], "SqlServerAcceptance")),
                TimeProvider.System);
            return new Scope(db, service, new FinanceContext(company, actor, $"fin-import-{Guid.NewGuid():N}", null));
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
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private static DbContextOptions<FinanceDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema)).Options;
    }

    private sealed class Scope(FinanceDbContext db, FinanceOfficialInvoiceImportApprovalServiceV1 service, FinanceContext context)
        : IAsyncDisposable
    {
        public FinanceOfficialInvoiceImportApprovalServiceV1 Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class AllowAuthority : IFinanceOfficialInvoiceImportApprovalAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context,
            FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class BarrierAuthority(int waitOnCall, Barrier barrier) : IFinanceOfficialInvoiceImportApprovalAuthorityV1
    {
        private int calls;
        public Task<PermissionDecision> CheckAsync(FinanceContext context,
            FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid batchId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == waitOnCall) barrier.SignalAndWait(cancellationToken);
            return Task.FromResult(PermissionDecision.Allowed);
        }
    }

    private sealed class ReadAuthority : IFinanceOpenItemSourceAuthority
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(PermissionDecision.Allowed);
    }
}
