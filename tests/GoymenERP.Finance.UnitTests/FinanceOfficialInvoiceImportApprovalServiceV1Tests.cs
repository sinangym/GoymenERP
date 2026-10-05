using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceOfficialInvoiceImportApprovalServiceV1Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Batch = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid OpenItem = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid Document = Guid.Parse("40000000-0000-0000-0000-000000000004");
    private static readonly Guid Installment = Guid.Parse("50000000-0000-0000-0000-000000000005");
    private static readonly Guid Party = Guid.Parse("60000000-0000-0000-0000-000000000006");
    private static readonly Guid Account = Guid.Parse("70000000-0000-0000-0000-000000000007");
    private static readonly Guid Currency = Guid.Parse("80000000-0000-0000-0000-000000000008");

    public static TheoryData<string?> InvalidBatchVersions => new()
    {
        null,
        string.Empty,
        new string('v', 129),
    };

    [Theory]
    [MemberData(nameof(InvalidBatchVersions))]
    public async Task Invalid_batch_version_is_refused_before_payload_hashing(string? batchVersion)
    {
        var company = CompanyContext();
        await using var db = Database(company);
        var service = Service(db, company, "maker", RepeatingCommandAuthority(PermissionDecision.Allowed), ReadAuthority());

        var result = await service.UploadAsync(Context("maker"), new CommandMetadata("upload-key"),
            Request() with { BatchVersion = batchVersion! }, default);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(result.Issues, issue => issue.Field == "batchVersion"
            && issue.Code == "finance_open_item_batch_version_invalid");
        Assert.Empty(await db.OfficialInvoiceImportBatches.AsNoTracking().ToListAsync());
        Assert.Empty(await db.OfficialInvoiceImportCommands.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Upload_persists_an_immutable_pending_snapshot_but_does_not_publish_it()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        var service = Service(db, company, "maker", RepeatingCommandAuthority(PermissionDecision.Allowed),
            ReadAuthority(PermissionDecision.Allowed, PermissionDecision.Allowed));

        var uploaded = await service.UploadAsync(Context("maker"), new CommandMetadata("upload-key"), Request(), default);
        var hidden = await service.GetApprovedAsync(Context("maker"), Reference(uploaded), default);

        Assert.Equal(CommandOutcome.Succeeded, uploaded.Outcome);
        Assert.Equal(FinanceOfficialInvoiceImportApprovalStatusV1.Pending, uploaded.Resource!.Status);
        Assert.Equal("1", uploaded.Resource.Version);
        Assert.Equal("maker", uploaded.Resource.UploadedBy);
        Assert.Null(uploaded.Resource.ApprovedBy);
        Assert.Equal(ReadOutcome.NotFound, hidden.Outcome);
        var stored = Assert.Single(await db.OfficialInvoiceImportBatches.AsNoTracking().ToListAsync());
        Assert.Equal(Request().Utf8Csv.ToArray(), stored.FileBytes);
        Assert.Equal(uploaded.Resource.BatchChecksum, stored.BatchChecksum);
        Assert.Equal(1, stored.RowCount);
        Assert.Empty(await db.OfficialInvoiceImportApprovals.AsNoTracking().ToListAsync());
        var delivery = Assert.Single(await db.OfficialInvoiceImportWorkDeliveries.AsNoTracking().ToListAsync());
        Assert.Equal(1, delivery.ObservationOrdinal);
        Assert.Equal(Batch, delivery.EvidenceId);
    }

    [Fact]
    public async Task Uploader_cannot_approve_own_batch_and_writes_no_approval()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        var service = Service(db, company, "maker", RepeatingCommandAuthority(PermissionDecision.Allowed), ReadAuthority());
        await service.UploadAsync(Context("maker"), new CommandMetadata("upload-key"), Request(), default);

        var result = await service.ApproveAsync(Context("maker"), new CommandMetadata("approve-key"),
            new FinanceOfficialInvoiceImportApproveRequestV1(Batch, "1"), default);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(result.Issues, issue => issue.Code == "approver_must_differ_from_uploader");
        Assert.Empty(await db.OfficialInvoiceImportApprovals.AsNoTracking().ToListAsync());
        Assert.Single(await db.OfficialInvoiceImportWorkDeliveries.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Different_authorized_checker_publishes_the_exact_batch_and_source_row()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        var maker = Service(db, company, "maker", RepeatingCommandAuthority(PermissionDecision.Allowed), ReadAuthority());
        var pending = await maker.UploadAsync(Context("maker"), new CommandMetadata("upload-key"), Request(), default);
        var checker = Service(db, company, "checker", RepeatingCommandAuthority(PermissionDecision.Allowed),
            ReadAuthority(PermissionDecision.Allowed, PermissionDecision.Allowed));

        var approved = await checker.ApproveAsync(Context("checker"), new CommandMetadata("approve-key"),
            new FinanceOfficialInvoiceImportApproveRequestV1(Batch, "1"), default);
        var source = await checker.GetApprovedAsync(Context("checker"), Reference(pending), default);

        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        Assert.Equal(FinanceOfficialInvoiceImportApprovalStatusV1.Approved, approved.Resource!.Status);
        Assert.Equal("2", approved.Resource.Version);
        Assert.Equal("maker", approved.Resource.UploadedBy);
        Assert.Equal("checker", approved.Resource.ApprovedBy);
        Assert.Equal(ReadOutcome.Found, source.Outcome);
        Assert.Equal(pending.Resource!.BatchChecksum, source.Resource!.BatchChecksum);
        var row = Assert.Single(source.Resource.Rows);
        Assert.Equal(OpenItem, row.OpenItemId);
        Assert.Equal(Document, row.SourceDocumentId);
        Assert.Equal(Installment, row.SourceInstallmentId);
        Assert.Equal("1250.50", row.Amount);
        Assert.Equal([1L, 2L], (await db.OfficialInvoiceImportWorkDeliveries.AsNoTracking()
            .OrderBy(item => item.ObservationOrdinal).Select(item => item.ObservationOrdinal).ToListAsync()).ToArray());
    }

    [Fact]
    public async Task Same_upload_key_and_body_replays_without_a_second_batch()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        var service = Service(db, company, "maker", RepeatingCommandAuthority(PermissionDecision.Allowed), ReadAuthority());
        var metadata = new CommandMetadata("upload-key");

        var first = await service.UploadAsync(Context("maker"), metadata, Request(), default);
        var replay = await service.UploadAsync(Context("maker"), metadata, Request(), default);

        Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
        Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Resource, replay.Resource);
        Assert.Single(await db.OfficialInvoiceImportBatches.AsNoTracking().ToListAsync());
        Assert.Single(await db.OfficialInvoiceImportWorkDeliveries.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Same_key_with_another_body_conflicts_only_after_a_fresh_authority_fence()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        var service = Service(db, company, "maker", RepeatingCommandAuthority(PermissionDecision.Allowed), ReadAuthority());
        await service.UploadAsync(Context("maker"), new CommandMetadata("upload-key"), Request(), default);

        var changed = Request() with { Utf8Csv = Encoding.UTF8.GetBytes("different-body") };
        var result = await service.UploadAsync(Context("maker"), new CommandMetadata("upload-key"), changed, default);

        Assert.Equal(CommandOutcome.IdempotencyConflict, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Single(await db.OfficialInvoiceImportBatches.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Permission_revoked_during_prior_read_hides_the_existing_key_and_conflict()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        var writer = Service(db, company, "maker", RepeatingCommandAuthority(PermissionDecision.Allowed), ReadAuthority());
        await writer.UploadAsync(Context("maker"), new CommandMetadata("upload-key"), Request(), default);
        var revoked = Service(db, company, "maker",
            new CommandAuthority([PermissionDecision.Allowed, PermissionDecision.Denied]), ReadAuthority());

        var result = await revoked.UploadAsync(Context("maker"), new CommandMetadata("upload-key"),
            Request() with { Utf8Csv = Encoding.UTF8.GetBytes("different-body") }, default);

        Assert.Equal(CommandOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Company_context_change_during_prior_read_hides_the_existing_key_and_replay()
    {
        var company = new MutableCompanyContext(Company);
        await using var db = Database(company);
        var writer = Service(db, company, "maker", RepeatingCommandAuthority(PermissionDecision.Allowed), ReadAuthority());
        await writer.UploadAsync(Context("maker"), new CommandMetadata("upload-key"), Request(), default);
        var changing = new CommandAuthority(
            [PermissionDecision.Allowed, PermissionDecision.Allowed],
            call => { if (call == 2) company.Set(Guid.NewGuid()); });
        var reader = Service(db, company, "maker", changing, ReadAuthority());

        var result = await reader.UploadAsync(Context("maker"), new CommandMetadata("upload-key"), Request(), default);

        Assert.Equal(CommandOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Permission_revoked_before_upload_save_refuses_and_writes_nothing()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        var authority = new CommandAuthority(
            [PermissionDecision.Allowed, PermissionDecision.Allowed, PermissionDecision.Denied]);
        var service = Service(db, company, "maker", authority, ReadAuthority());

        var result = await service.UploadAsync(Context("maker"), new CommandMetadata("upload-key"), Request(), default);

        Assert.Equal(CommandOutcome.Denied, result.Outcome);
        Assert.Equal(3, authority.Calls);
        Assert.Empty(await db.OfficialInvoiceImportBatches.AsNoTracking().ToListAsync());
        Assert.Empty(await db.OfficialInvoiceImportCommands.AsNoTracking().ToListAsync());
        Assert.Empty(await db.OfficialInvoiceImportWorkDeliveries.AsNoTracking().ToListAsync());
    }

    private static FinanceOfficialInvoiceImportApprovalServiceV1 Service(
        FinanceDbContext db,
        ICompanyContext company,
        string actor,
        IFinanceOfficialInvoiceImportApprovalAuthorityV1 commandAuthority,
        IFinanceOpenItemSourceAuthority readAuthority) => new(
            db,
            company,
            new User(actor, Company),
            new FinanceOfficialInvoiceImportParserV1(),
            commandAuthority,
            readAuthority,
            new NullAuditCollector(),
            new FinanceOfficialInvoiceImportAuditEnvelopesV1(
                new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Upload, [], "unit-test"),
                new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Approve, [], "unit-test")),
            TimeProvider.System);

    private static FinanceOfficialInvoiceImportPreviewRequestV1 Request()
    {
        var csv = string.Join('\n',
            FinanceOfficialInvoiceImportTemplateV1.Header,
            $"{OpenItem:D},{Document:D},{Installment:D},invoice-v1,{Party:D},{Account:D},{Currency:D},1250.50,2026-10-31");
        return new FinanceOfficialInvoiceImportPreviewRequestV1(Batch, "official-file-v1", Encoding.UTF8.GetBytes(csv));
    }

    private static FinanceOpenItemSourceReference Reference(FinanceOfficialInvoiceImportApprovalResultV1 uploaded) =>
        new(uploaded.Resource!.BatchId, uploaded.Resource.BatchVersion, uploaded.Resource.BatchChecksum);

    private static FinanceContext Context(string actor) => new(Company, actor, $"correlation-{actor}", null);

    private static FinanceDbContext Database(ICompanyContext company) => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"finance-official-import-approval-{Guid.NewGuid():N}")
            .Options,
        company);

    private static CompanyContext CompanyContext()
    {
        var context = new CompanyContext();
        context.Set(Company);
        return context;
    }

    private static CommandAuthority RepeatingCommandAuthority(PermissionDecision decision) =>
        new(Enumerable.Repeat(decision, 32).ToArray());

    private static Authority ReadAuthority(params PermissionDecision[] decisions) => new(decisions);

    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class CommandAuthority : IFinanceOfficialInvoiceImportApprovalAuthorityV1
    {
        private readonly Queue<PermissionDecision> decisions;
        private readonly Action<int>? onCall;

        public CommandAuthority(PermissionDecision[] decisions, Action<int>? onCall = null)
        {
            this.decisions = new Queue<PermissionDecision>(decisions);
            this.onCall = onCall;
        }

        public int Calls { get; private set; }

        public Task<PermissionDecision> CheckAsync(FinanceContext context,
            FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid batchId, CancellationToken cancellationToken)
        {
            Calls++;
            onCall?.Invoke(Calls);
            return Task.FromResult(decisions.Count == 0 ? PermissionDecision.Denied : decisions.Dequeue());
        }
    }

    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }

    private sealed class Authority(params PermissionDecision[] decisions) : IFinanceOpenItemSourceAuthority
    {
        private readonly Queue<PermissionDecision> _decisions = new(decisions);

        public Task<PermissionDecision> CheckAsync(FinanceContext context, Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(_decisions.Count == 0 ? PermissionDecision.Denied : _decisions.Dequeue());
    }

    private sealed class NullAuditCollector : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent) { }
    }
}
