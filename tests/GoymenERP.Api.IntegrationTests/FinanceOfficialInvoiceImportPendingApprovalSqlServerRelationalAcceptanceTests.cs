using System.Text;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>SENTETIK SQL evidence only; not a real invoice file, mapping, grant or user UAT.</summary>
public sealed class FinanceOfficialInvoiceImportPendingApprovalSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Batch = Guid.Parse("93000000-0000-0000-0000-000000000003");

    [SqlServerFact]
    public async Task A_different_checker_reads_pending_detail_approves_and_then_reopens_the_approved_source()
    {
        await using var factory = new SqlServerRelationalApiFactory(
            Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!);
        using var client = factory.CreateClient();
        using var health = await client.GetAsync("/health/live");
        health.EnsureSuccessStatusCode();

        var company = new CompanyContext();
        company.Set(Company);
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema)).Options;
        await using var db = new FinanceDbContext(options, company);
        await db.Database.MigrateAsync();
        var authority = new AllowAuthority();
        var maker = Approval(db, company, "SENTETIK-maker", authority);
        var uploaded = await maker.UploadAsync(Context("SENTETIK-maker"), new CommandMetadata("upload-key"), Request(), default);
        Assert.Equal(CommandOutcome.Succeeded, uploaded.Outcome);

        var checkerUser = new User("SENTETIK-checker", Company);
        var pending = new FinanceOfficialInvoiceImportPendingApprovalReadServiceV1(db, company,
            checkerUser, new FinanceOfficialInvoiceImportParserV1(), authority);
        var work = new NativeApprovalReadQueryV1(
            company,
            checkerUser,
            new Correlation("sql-SENTETIK-checker"),
            new FinanceInvoiceImportPendingApprovalAdapterV1(pending));
        var page = await work.ListAsync(null, default);
        var detail = await work.GetAsync(NativeApprovalKindsV1.FinanceOfficialInvoiceImport, Batch, default);
        Assert.Single(page.Resource!.Items);
        Assert.Equal(uploaded.Resource!.BatchChecksum, detail.Resource!.Preview.SourceSha256);
        Assert.Equal((NativeApprovalKindsV1.FinanceOfficialInvoiceImport, Batch, Company, "SENTETIK-maker"),
            (detail.Resource.Item.Kind, detail.Resource.Item.ApprovalId,
                detail.Resource.Item.CompanyId, detail.Resource.Item.SubmittedBy));

        var checker = Approval(db, company, "SENTETIK-checker", authority);
        var approved = await checker.ApproveAsync(Context("SENTETIK-checker"), new CommandMetadata("approve-key"),
            new FinanceOfficialInvoiceImportApproveRequestV1(Batch, "1"), default);
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        Assert.Empty((await work.ListAsync(null, default)).Resource!.Items);

        var source = await checker.GetApprovedAsync(Context("SENTETIK-checker"),
            new FinanceOpenItemSourceReference(Batch, approved.Resource!.BatchVersion, approved.Resource.BatchChecksum), default);
        Assert.Equal(ReadOutcome.Found, source.Outcome);
        Assert.Single(source.Resource!.Rows);
    }

    private static FinanceOfficialInvoiceImportApprovalServiceV1 Approval(
        FinanceDbContext db, ICompanyContext company, string actor, IFinanceOfficialInvoiceImportApprovalAuthorityV1 authority) =>
        new(db, company, new User(actor, Company), new FinanceOfficialInvoiceImportParserV1(), authority,
            new ReadAuthority(), new NullAuditCollector(),
            new FinanceOfficialInvoiceImportAuditEnvelopesV1(
                new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Upload, ["SENTETIK-FINANCE"], "SQL"),
                new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Approve, ["SENTETIK-FINANCE"], "SQL")),
            TimeProvider.System);

    private static FinanceOfficialInvoiceImportPreviewRequestV1 Request()
    {
        var csv = string.Join('\n', FinanceOfficialInvoiceImportTemplateV1.Header,
            $"{Guid.NewGuid():D},{Guid.NewGuid():D},{Guid.NewGuid():D},invoice-v1,{Guid.NewGuid():D},{Guid.NewGuid():D},{Guid.NewGuid():D},1250.50,2026-10-31");
        return new(Batch, "official-file-v1", Encoding.UTF8.GetBytes(csv));
    }

    private static FinanceContext Context(string actor) => new(Company, actor, $"sql-{actor}", null);
    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId { get; } = correlationId;
        public string? CausationId => null;
        public string SourceChannel => "SQL";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(
            string candidateCorrelationId,
            string? causationId,
            string sourceChannel,
            string? clientIpAddress,
            string? userAgent) =>
            throw new InvalidOperationException("The SQL acceptance correlation context is immutable.");
    }
    private sealed class AllowAuthority : IFinanceOfficialInvoiceImportApprovalAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context,
            FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(PermissionDecision.Allowed);
    }
    private sealed class ReadAuthority : IFinanceOpenItemSourceAuthority
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(PermissionDecision.Allowed);
    }
    private sealed class NullAuditCollector : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent) { }
    }
}
