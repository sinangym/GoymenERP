using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>SENTETIK SQL evidence only; not a real invoice file, mapping, grant or user UAT.</summary>
public sealed class FinanceOfficialInvoiceImportPendingApprovalSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Batch = Guid.Parse("93000000-0000-0000-0000-000000000003");
    private static readonly Guid SelfBatch = Guid.Parse("93000000-0000-0000-0000-000000000004");
    private const string Correlation = "d05-real-sql-http";

    [SqlServerFact]
    public async Task Public_http_uses_production_di_for_authorized_list_detail_isolation_and_true_empty()
    {
        await using var factory = new SqlServerRelationalApiFactory(
            Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!);
        using var healthClient = factory.CreateClient();
        using var health = await healthClient.GetAsync("/health/live");
        health.EnsureSuccessStatusCode();
        var authorizedActor = await GrantAsync(factory, TestUsers.GroupAdmin, Company);
        await GrantAsync(factory, TestUsers.GroupAdmin, TestCompanyIds.CompanyB);

        var company = new CompanyContext();
        company.Set(Company);
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema)).Options;
        await using var db = new FinanceDbContext(options, company);
        await db.Database.MigrateAsync();
        var authority = new AllowAuthority();
        var maker = Approval(db, company, "SENTETIK-maker", authority);
        var uploaded = await maker.UploadAsync(Context("SENTETIK-maker"), new CommandMetadata("upload-key"),
            Request(Batch), default);
        Assert.Equal(CommandOutcome.Succeeded, uploaded.Outcome);
        var selfMaker = Approval(db, company, authorizedActor, authority);
        var selfUploaded = await selfMaker.UploadAsync(Context(authorizedActor),
            new CommandMetadata("self-upload-key"), Request(SelfBatch), default);
        Assert.Equal(CommandOutcome.Succeeded, selfUploaded.Outcome);

        using var client = WorkClient(factory, TestUsers.GroupAdmin, Company, Correlation);
        using var list = await client.GetAsync(WorkApprovalEndpoints.Path);
        using var detail = await client.GetAsync(
            $"{WorkApprovalEndpoints.Path}/{NativeApprovalKindsV1.FinanceOfficialInvoiceImport}/{Batch:D}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(Correlation, list.Headers.GetValues("X-Correlation-Id").Single());
        using (var page = JsonDocument.Parse(await list.Content.ReadAsStringAsync()))
        {
            var item = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(Batch, item.GetProperty("approvalId").GetGuid());
            Assert.Equal("SENTETIK-maker", item.GetProperty("submittedBy").GetString());
            Assert.Equal(JsonValueKind.Null, page.RootElement.GetProperty("nextCursor").ValueKind);
        }
        using (var body = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
        {
            Assert.Equal(uploaded.Resource!.BatchChecksum,
                body.RootElement.GetProperty("preview").GetProperty("sourceSha256").GetString());
            Assert.Equal(Company,
                body.RootElement.GetProperty("item").GetProperty("companyId").GetGuid());
        }

        using var anonymous = factory.CreateClient();
        using var anonymousResponse = await anonymous.GetAsync(WorkApprovalEndpoints.Path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var noCompany = WorkClient(factory, TestUsers.GroupAdmin, null, "d05-no-company");
        using var noCompanyResponse = await noCompany.GetAsync(WorkApprovalEndpoints.Path);
        Assert.Equal(HttpStatusCode.BadRequest, noCompanyResponse.StatusCode);

        using var badCorrelation = WorkClient(factory, TestUsers.GroupAdmin, Company, "invalid/correlation");
        using var badCorrelationResponse = await badCorrelation.GetAsync(WorkApprovalEndpoints.Path);
        Assert.Equal(HttpStatusCode.BadRequest, badCorrelationResponse.StatusCode);

        using var denied = WorkClient(factory, TestUsers.CompanyAViewer, Company, "d05-denied");
        using var deniedResponse = await denied.GetAsync(WorkApprovalEndpoints.Path);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var foreign = WorkClient(factory, TestUsers.GroupAdmin, TestCompanyIds.CompanyB, "d05-foreign");
        using var foreignList = await foreign.GetAsync(WorkApprovalEndpoints.Path);
        using var foreignDetail = await foreign.GetAsync(
            $"{WorkApprovalEndpoints.Path}/{NativeApprovalKindsV1.FinanceOfficialInvoiceImport}/{Batch:D}");
        Assert.Equal(HttpStatusCode.OK, foreignList.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignDetail.StatusCode);
        using (var foreignPage = JsonDocument.Parse(await foreignList.Content.ReadAsStringAsync()))
        {
            Assert.Empty(foreignPage.RootElement.GetProperty("items").EnumerateArray());
        }

        var checker = Approval(db, company, "SENTETIK-checker", authority);
        var approved = await checker.ApproveAsync(Context("SENTETIK-checker"), new CommandMetadata("approve-key"),
            new FinanceOfficialInvoiceImportApproveRequestV1(Batch, "1"), default);
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        using var afterApproval = await client.GetAsync(WorkApprovalEndpoints.Path);
        Assert.Equal(HttpStatusCode.OK, afterApproval.StatusCode);
        using (var empty = JsonDocument.Parse(await afterApproval.Content.ReadAsStringAsync()))
        {
            Assert.Empty(empty.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, empty.RootElement.GetProperty("nextCursor").ValueKind);
        }

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

    private static FinanceOfficialInvoiceImportPreviewRequestV1 Request(Guid batchId)
    {
        var csv = string.Join('\n', FinanceOfficialInvoiceImportTemplateV1.Header,
            $"{Guid.NewGuid():D},{Guid.NewGuid():D},{Guid.NewGuid():D},invoice-v1,{Guid.NewGuid():D},{Guid.NewGuid():D},{Guid.NewGuid():D},1250.50,2026-10-31");
        return new(batchId, $"official-file-{batchId:N}", Encoding.UTF8.GetBytes(csv));
    }

    private static HttpClient WorkClient(
        SqlServerRelationalApiFactory factory,
        string user,
        Guid? company,
        string correlation)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", user);
        if (company is not null)
        {
            client.DefaultRequestHeaders.Add("X-Company-Id", company.Value.ToString("D"));
        }
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlation);
        return client;
    }

    private static async Task<string> GrantAsync(
        SqlServerRelationalApiFactory factory,
        string developmentUser,
        Guid company)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var actor = (await dbContext.ExternalIdentities.SingleAsync(link =>
            link.Issuer == BootstrapIdentityOptions.Issuer && link.Subject == developmentUser)).UserId;
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(
            roleId,
            $"D05-APPROVE-{roleId:N}",
            "Synthetic D-05 approval inbox test role",
            isSystem: true);
        role.AddPermission(FinanceOfficialInvoiceImportPermissionNamesV1.Approve);
        var assignment = new RoleAssignment(
            Guid.NewGuid(),
            actor,
            roleId,
            company,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            validUntilUtc: null,
            approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, company);
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
        return actor.ToString("D");
    }

    private static FinanceContext Context(string actor) => new(Company, actor, $"sql-{actor}", null);
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
