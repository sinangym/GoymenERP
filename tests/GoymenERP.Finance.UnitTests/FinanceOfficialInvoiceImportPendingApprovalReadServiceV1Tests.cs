using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceOfficialInvoiceImportPendingApprovalReadServiceV1Tests
{
    private static readonly Guid Company = Guid.Parse("91000000-0000-0000-0000-000000000001");
    private static readonly Guid Batch = Guid.Parse("92000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Different_checker_lists_and_opens_the_exact_pending_preview_but_uploader_cannot()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        await UploadAsync(db, company);
        var checker = Reader(db, company, "checker", new Authority(PermissionDecision.Allowed, PermissionDecision.Allowed,
            PermissionDecision.Allowed, PermissionDecision.Allowed));

        var page = await checker.ListAsync(Context("checker"), null, default);
        var detail = await checker.GetAsync(Context("checker"), Batch, default);
        var own = await Reader(db, company, "maker", new Authority(PermissionDecision.Allowed, PermissionDecision.Allowed))
            .ListAsync(Context("maker"), null, default);

        var item = Assert.Single(page.Resource!.Items);
        Assert.Equal(ReadOutcome.Found, page.Outcome);
        Assert.Equal(FinanceOfficialInvoiceImportApprovalStatusV1.Pending, item.Status);
        Assert.Equal("1", item.Version);
        Assert.Equal(ReadOutcome.Found, detail.Outcome);
        Assert.Equal(item.BatchChecksum, detail.Resource!.Preview.SourceSha256);
        Assert.Single(detail.Resource.Preview.Batch!.Rows);
        Assert.Empty(own.Resource!.Items);
    }

    [Fact]
    public async Task Revoked_approval_authority_after_read_hides_the_page_and_detail()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        await UploadAsync(db, company);

        var page = await Reader(db, company, "checker",
            new Authority(PermissionDecision.Allowed, PermissionDecision.Denied))
            .ListAsync(Context("checker"), null, default);
        var detail = await Reader(db, company, "checker",
            new Authority(PermissionDecision.Allowed, PermissionDecision.Denied))
            .GetAsync(Context("checker"), Batch, default);

        Assert.Equal(ReadOutcome.Denied, page.Outcome);
        Assert.Null(page.Resource);
        Assert.Equal(ReadOutcome.Denied, detail.Outcome);
        Assert.Null(detail.Resource);
    }

    [Fact]
    public async Task Corrupt_exact_bytes_or_rows_fail_closed()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        db.OfficialInvoiceImportBatches.Add(new FinanceOfficialInvoiceImportBatchRow
        {
            Id = Batch,
            CompanyId = Company,
            BatchVersion = "official-file-v1",
            BatchChecksum = new string('0', 64),
            TemplateVersion = FinanceOfficialInvoiceImportTemplateV1.Version,
            FileBytes = Encoding.UTF8.GetBytes("corrupt"),
            SourceRowsJson = "[]",
            RowCount = 1,
            UploadedBy = "maker",
            UploadedAtUtc = DateTimeOffset.Parse("2026-09-30T12:00:00Z"),
            CorrelationId = "corrupt-seed",
        });
        await db.SaveChangesAsync();
        var reader = Reader(db, company, "checker",
            new Authority(PermissionDecision.Allowed, PermissionDecision.Allowed,
                PermissionDecision.Allowed, PermissionDecision.Allowed));

        var page = await reader.ListAsync(Context("checker"), null, default);
        var detail = await reader.GetAsync(Context("checker"), Batch, default);

        Assert.Equal(ReadOutcome.Unavailable, page.Outcome);
        Assert.Equal(ReadOutcome.Unavailable, detail.Outcome);
    }

    [Fact]
    public async Task Cursor_is_bound_to_the_current_company()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        var other = Guid.NewGuid();
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{other:N}:50"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var result = await Reader(db, company, "checker", new Authority())
            .ListAsync(Context("checker"), raw, default);

        Assert.Equal(ReadOutcome.NotFound, result.Outcome);
    }

    private static async Task UploadAsync(FinanceDbContext db, ICompanyContext company)
    {
        var service = new FinanceOfficialInvoiceImportApprovalServiceV1(db, company, new User("maker", Company),
            new FinanceOfficialInvoiceImportParserV1(), new Authority(Enumerable.Repeat(PermissionDecision.Allowed, 8).ToArray()),
            new ReadAuthority(), new NullAuditCollector(),
            new FinanceOfficialInvoiceImportAuditEnvelopesV1(
                new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Upload, [], "unit-test"),
                new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Approve, [], "unit-test")),
            TimeProvider.System);
        var result = await service.UploadAsync(Context("maker"), new CommandMetadata("upload-key"), Request(), default);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
    }

    private static FinanceOfficialInvoiceImportPendingApprovalReadServiceV1 Reader(
        FinanceDbContext db, ICompanyContext company, string actor, IFinanceOfficialInvoiceImportApprovalAuthorityV1 authority) =>
        new(db, company, new User(actor, Company), new FinanceOfficialInvoiceImportParserV1(), authority);

    private static FinanceOfficialInvoiceImportPreviewRequestV1 Request()
    {
        var csv = string.Join('\n', FinanceOfficialInvoiceImportTemplateV1.Header,
            $"{Guid.NewGuid():D},{Guid.NewGuid():D},{Guid.NewGuid():D},invoice-v1,{Guid.NewGuid():D},{Guid.NewGuid():D},{Guid.NewGuid():D},1250.50,2026-10-31");
        return new(Batch, "official-file-v1", Encoding.UTF8.GetBytes(csv));
    }

    private static FinanceContext Context(string actor) => new(Company, actor, $"pending-{actor}", null);
    private static FinanceDbContext Database(ICompanyContext company) => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"finance-pending-import-{Guid.NewGuid():N}").Options, company);
    private static CompanyContext CompanyContext() { var value = new CompanyContext(); value.Set(Company); return value; }

    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority(params PermissionDecision[] decisions) : IFinanceOfficialInvoiceImportApprovalAuthorityV1
    {
        private readonly Queue<PermissionDecision> values = new(decisions);
        public Task<PermissionDecision> CheckAsync(FinanceContext context,
            FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(values.Count == 0 ? PermissionDecision.Denied : values.Dequeue());
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
