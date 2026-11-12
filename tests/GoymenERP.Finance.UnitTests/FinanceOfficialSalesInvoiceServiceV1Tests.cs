using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceOfficialSalesInvoiceServiceV1Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-0000000000a1");
    private static readonly Guid ForeignCompany = Guid.Parse("10000000-0000-0000-0000-0000000000b2");
    private static readonly Guid Invoice = Guid.Parse("20000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Customer = Guid.Parse("30000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Currency = Guid.Parse("40000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Pdf = Guid.Parse("50000000-0000-0000-0000-0000000000a1");
    private const string Ettn = "6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b";
    private static readonly string Sha = new('a', 64);
    private static readonly string OtherSha = new('b', 64);

    public static TheoryData<string, string, string> InvalidRequests => new()
    {
        { "invoiceId", "identity_required", "invoiceId" },
        { "officialNo", "required", "officialNo-empty" },
        { "officialNo", "invalid", "officialNo-padded" },
        { "officialNo", "invalid", "officialNo-long" },
        { "ettn", "required", "ettn-empty" },
        { "ettn", "invalid", "ettn-not-guid" },
        { "ettn", "invalid", "ettn-empty-guid" },
        { "customerPartyId", "required", "customerPartyId" },
        { "currencyId", "required", "currencyId" },
        { "payableAmount", "required", "payableAmount-empty" },
        { "payableAmount", "invalid", "payableAmount-zero" },
        { "payableAmount", "invalid", "payableAmount-negative" },
        { "payableAmount", "invalid", "payableAmount-does-not-fit-decimal" },
        { "payableFunctionalAmount", "invalid", "payableFunctionalAmount-text" },
        { "taxBaseFunctionalAmount", "required", "taxBaseFunctionalAmount-missing" },
        { "taxBaseFunctionalAmount", "invalid", "taxBaseFunctionalAmount-negative" },
        { "vatFunctionalAmount", "required", "vatFunctionalAmount-empty" },
        { "withholdingFunctionalAmount", "invalid", "withholdingFunctionalAmount-negative" },
        { "pdfDocumentId", "required", "pdfDocumentId" },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Record_refuses_a_missing_or_invalid_field_and_writes_nothing(string field, string code,
        string variant)
    {
        var fixture = Fixture.Create();
        var documents = new Documents();
        var service = fixture.Service("maker", documents);

        var result = await service.RecordAsync(Context("maker"), new CommandMetadata("record-key"),
            Invalid(variant), default);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Contains(result.Issues, issue => issue.Field == field && issue.Code == code);
        Assert.Equal(0, documents.Calls);
        Assert.Empty(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.OfficialSalesInvoiceCommands.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Record_stores_a_pending_record_with_the_pinned_pdf_version_and_hash()
    {
        var fixture = Fixture.Create();
        var documents = new Documents();
        var service = fixture.Service("maker", documents);

        var result = await service.RecordAsync(Context("maker"), new CommandMetadata("record-key"),
            Request() with { Ettn = Ettn.ToUpperInvariant() }, default);

        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        Assert.False(result.IsReplay);
        var view = result.Resource!;
        Assert.Equal(FinanceOfficialSalesInvoiceStatusV1.PendingApproval, view.Status);
        Assert.Equal("1", view.Version);
        Assert.Equal(Ettn, view.Ettn);
        Assert.Equal(1, view.PdfDocumentVersionNo);
        Assert.Equal(Sha, view.PdfSha256);
        Assert.Equal("maker", view.RecordedBy);
        Assert.Null(view.ApprovedBy);
        Assert.Equal([(Pdf, Customer, (int?)null)], documents.Requests);
        var stored = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Equal(Company, stored.CompanyId);
        Assert.Equal("1250.50", stored.PayableAmountText);
        Assert.Equal("1042.08", stored.TaxBaseFunctionalAmountText);
        Assert.Equal("1042.08", view.TaxBaseFunctionalAmount);
        Assert.Single(await fixture.Db.OfficialSalesInvoiceCommands.AsNoTracking().ToListAsync());
        Assert.Single(fixture.Audit.Events, e => e.EventType == "finance.official-sales-invoice.recorded.v1");
    }

    [Fact]
    public async Task Recorder_cannot_approve_own_record_and_the_record_stays_pending()
    {
        var fixture = Fixture.Create();
        await Record(fixture);

        var result = await fixture.Service("maker", new Documents())
            .ApproveAsync(Context("maker"), new CommandMetadata("approve-key"), Approve(), default);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(result.Issues, issue => issue.Field == "approver"
            && issue.Code == "approver_must_differ_from_recorder");
        var stored = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Equal((int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval, stored.Status);
        Assert.Null(stored.ApprovedBy);
        Assert.Single(await fixture.Db.OfficialSalesInvoiceCommands.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_different_actor_approves_once_and_a_second_approval_is_a_version_conflict()
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        var documents = new Documents();

        var approved = await fixture.Service("checker", documents)
            .ApproveAsync(Context("checker"), new CommandMetadata("approve-key"), Approve(), default);
        fixture.Db.ChangeTracker.Clear();
        var again = await fixture.Service("third", new Documents())
            .ApproveAsync(Context("third"), new CommandMetadata("approve-key-2"), Approve(), default);

        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        Assert.Equal(FinanceOfficialSalesInvoiceStatusV1.Approved, approved.Resource!.Status);
        Assert.Equal("2", approved.Resource.Version);
        Assert.Equal("checker", approved.Resource.ApprovedBy);
        Assert.NotNull(approved.Resource.ApprovedAtUtc);
        Assert.Equal([(Pdf, Customer, (int?)1)], documents.Requests);
        Assert.Equal(CommandOutcome.VersionConflict, again.Outcome);
        Assert.Contains(again.Issues, issue => issue.Field == "expectedVersion" && issue.Code == "version_conflict");
        Assert.Single(fixture.Audit.Events, e => e.EventType == "finance.official-sales-invoice.approved.v1");
    }

    [Fact]
    public async Task Stale_expected_version_is_a_version_conflict()
    {
        var fixture = Fixture.Create();
        await Record(fixture);

        var result = await fixture.Service("checker", new Documents()).ApproveAsync(Context("checker"),
            new CommandMetadata("approve-key"), Approve() with { ExpectedVersion = "2" }, default);

        Assert.Equal(CommandOutcome.VersionConflict, result.Outcome);
        var stored = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Equal((int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval, stored.Status);
    }

    [Fact]
    public async Task Approving_an_unknown_record_is_refused()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Service("checker", new Documents())
            .ApproveAsync(Context("checker"), new CommandMetadata("approve-key"), Approve(), default);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(result.Issues, issue => issue.Field == "invoiceId" && issue.Code == "invoice_not_found");
    }

    [Fact]
    public async Task Same_key_replays_the_stored_result_and_a_different_body_or_actor_conflicts()
    {
        var fixture = Fixture.Create();
        var first = await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var documents = new Documents();

        var replay = await fixture.Service("maker", documents)
            .RecordAsync(Context("maker"), new CommandMetadata("record-key"), Request(), default);
        var changed = await fixture.Service("maker", documents).RecordAsync(Context("maker"),
            new CommandMetadata("record-key"), Request() with { PayableAmount = "1250.51" }, default);
        var otherActor = await fixture.Service("other", documents)
            .RecordAsync(Context("other"), new CommandMetadata("record-key"), Request(), default);

        Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Resource, replay.Resource);
        Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(CommandOutcome.IdempotencyConflict, changed.Outcome);
        Assert.Equal(CommandOutcome.IdempotencyConflict, otherActor.Outcome);
        Assert.Equal(0, documents.Calls);
        Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Single(await fixture.Db.OfficialSalesInvoiceCommands.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Approval_replay_returns_the_stored_result_without_a_second_write()
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        var approved = await fixture.Service("checker", new Documents())
            .ApproveAsync(Context("checker"), new CommandMetadata("approve-key"), Approve(), default);
        fixture.Db.ChangeTracker.Clear();

        var replay = await fixture.Service("checker", new Documents())
            .ApproveAsync(Context("checker"), new CommandMetadata("approve-key"), Approve(), default);

        Assert.True(replay.IsReplay);
        Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
        Assert.Equal(approved.Resource, replay.Resource);
        Assert.Equal(2, await fixture.Db.OfficialSalesInvoiceCommands.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Duplicate_invoice_id_official_number_and_ettn_are_refused()
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var service = fixture.Service("maker", new Documents());

        var sameId = await service.RecordAsync(Context("maker"), new CommandMetadata("key-id"),
            Request() with { OfficialNo = "GYM2026000000002", Ettn = Guid.NewGuid().ToString("D") }, default);
        var sameNo = await service.RecordAsync(Context("maker"), new CommandMetadata("key-no"),
            Request() with { InvoiceId = Guid.NewGuid(), Ettn = Guid.NewGuid().ToString("D") }, default);
        var sameEttn = await service.RecordAsync(Context("maker"), new CommandMetadata("key-ettn"),
            Request() with { InvoiceId = Guid.NewGuid(), OfficialNo = "GYM2026000000003" }, default);

        Assert.Equal(CommandOutcome.IdempotencyConflict, sameId.Outcome);
        Assert.Contains(sameId.Issues, issue => issue.Code == "invoice_already_exists");
        Assert.Equal(CommandOutcome.ValidationFailed, sameNo.Outcome);
        Assert.Contains(sameNo.Issues, issue => issue.Code == "official_no_already_recorded");
        Assert.Equal(CommandOutcome.ValidationFailed, sameEttn.Outcome);
        Assert.Contains(sameEttn.Issues, issue => issue.Code == "ettn_already_recorded");
        Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
    }

    public static TheoryData<FinanceOfficialSalesInvoicePdfOutcomeV1, CommandOutcome, string> PdfRefusals => new()
    {
        { FinanceOfficialSalesInvoicePdfOutcomeV1.NotFound, CommandOutcome.ValidationFailed, "document_not_found" },
        { FinanceOfficialSalesInvoicePdfOutcomeV1.NotPdf, CommandOutcome.ValidationFailed, "document_not_pdf" },
        { FinanceOfficialSalesInvoicePdfOutcomeV1.OwnerMismatch, CommandOutcome.ValidationFailed,
            "document_owner_mismatch" },
        { FinanceOfficialSalesInvoicePdfOutcomeV1.NotDownloadable, CommandOutcome.ValidationFailed,
            "document_not_clean" },
        { FinanceOfficialSalesInvoicePdfOutcomeV1.Denied, CommandOutcome.Denied, "document_access_denied" },
        { FinanceOfficialSalesInvoicePdfOutcomeV1.Unavailable, CommandOutcome.Unavailable, "document_unavailable" },
    };

    [Theory]
    [MemberData(nameof(PdfRefusals))]
    public async Task Record_fails_closed_when_the_pdf_is_not_accepted(FinanceOfficialSalesInvoicePdfOutcomeV1 pdf,
        CommandOutcome outcome, string code)
    {
        var fixture = Fixture.Create();
        var service = fixture.Service("maker", new Documents(new FinanceOfficialSalesInvoicePdfCheckV1(pdf, null, null)));

        var result = await service.RecordAsync(Context("maker"), new CommandMetadata("record-key"), Request(), default);

        Assert.Equal(outcome, result.Outcome);
        Assert.Contains(result.Issues, issue => issue.Field == "pdfDocumentId" && issue.Code == code);
        Assert.Empty(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.OfficialSalesInvoiceCommands.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(0, "aaaa")]
    [InlineData(1, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Record_refuses_an_accepted_answer_without_a_valid_version_or_hash(int version, string sha)
    {
        var fixture = Fixture.Create();
        var service = fixture.Service("maker", new Documents(
            new FinanceOfficialSalesInvoicePdfCheckV1(FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted, version, sha)));

        var result = await service.RecordAsync(Context("maker"), new CommandMetadata("record-key"), Request(), default);

        Assert.Equal(CommandOutcome.Unavailable, result.Outcome);
        Assert.Contains(result.Issues, issue => issue.Code == "document_unavailable");
    }

    [Fact]
    public async Task Approve_refuses_when_the_pinned_pdf_hash_changed()
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var changed = new Documents(
            new FinanceOfficialSalesInvoicePdfCheckV1(FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted, 1, OtherSha));

        var result = await fixture.Service("checker", changed)
            .ApproveAsync(Context("checker"), new CommandMetadata("approve-key"), Approve(), default);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(result.Issues, issue => issue.Field == "pdfDocumentId" && issue.Code == "document_changed");
        var stored = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Equal((int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval, stored.Status);
        Assert.Single(await fixture.Db.OfficialSalesInvoiceCommands.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, CommandOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, CommandOutcome.Unavailable)]
    public async Task Record_and_approve_are_refused_without_fresh_authority(PermissionDecision decision,
        CommandOutcome outcome)
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var documents = new Documents();

        var recorded = await fixture.Service("maker", documents, new Authority(decision)).RecordAsync(
            Context("maker"), new CommandMetadata("key-2"),
            Request() with { InvoiceId = Guid.NewGuid(), OfficialNo = "X2", Ettn = Guid.NewGuid().ToString("D") },
            default);
        var approved = await fixture.Service("checker", documents, new Authority(decision))
            .ApproveAsync(Context("checker"), new CommandMetadata("approve-key"), Approve(), default);

        Assert.Equal(outcome, recorded.Outcome);
        Assert.Equal(outcome, approved.Outcome);
        Assert.Equal(0, documents.Calls);
        Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        if (decision == PermissionDecision.Unavailable)
        {
            Assert.Contains(recorded.Issues, issue => issue.Code == "finance_permission_mapping_unavailable");
        }
    }

    [Fact]
    public async Task Authority_revoked_before_save_writes_nothing()
    {
        var fixture = Fixture.Create();
        var authority = new Authority(PermissionDecision.Allowed, PermissionDecision.Allowed,
            PermissionDecision.Allowed, PermissionDecision.Denied);

        var result = await fixture.Service("maker", new Documents(), authority)
            .RecordAsync(Context("maker"), new CommandMetadata("record-key"), Request(), default);

        Assert.Equal(CommandOutcome.Denied, result.Outcome);
        Assert.Empty(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.OfficialSalesInvoiceCommands.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Audit.Events);
    }

    [Fact]
    public async Task Another_company_cannot_read_or_approve_the_record()
    {
        var databaseName = $"finance-official-sales-{Guid.NewGuid():N}";
        var fixture = Fixture.Create(Company, databaseName);
        await Record(fixture);
        var foreign = Fixture.Create(ForeignCompany, databaseName);

        var read = await foreign.Service("checker", new Documents())
            .GetAsync(Context("checker", ForeignCompany), Invoice, default);
        var list = await foreign.Service("checker", new Documents())
            .ListAsync(Context("checker", ForeignCompany), false, null, default);
        var approve = await foreign.Service("checker", new Documents())
            .ApproveAsync(Context("checker", ForeignCompany), new CommandMetadata("approve-key"), Approve(), default);
        var crossContext = await fixture.Service("checker", new Documents())
            .GetAsync(Context("checker", ForeignCompany), Invoice, default);

        Assert.Equal(ReadOutcome.NotFound, read.Outcome);
        Assert.Equal(ReadOutcome.Found, list.Outcome);
        Assert.Empty(list.Resource!.Items);
        Assert.Equal(CommandOutcome.ValidationFailed, approve.Outcome);
        Assert.Contains(approve.Issues, issue => issue.Code == "invoice_not_found");
        Assert.Equal(ReadOutcome.Denied, crossContext.Outcome);
    }

    [Fact]
    public async Task Get_and_list_return_the_record_and_the_pending_filter_hides_approved_records()
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        var second = Guid.Parse("20000000-0000-0000-0000-0000000000a2");
        await fixture.Service("maker", new Documents()).RecordAsync(Context("maker"), new CommandMetadata("key-2"),
            Request() with { InvoiceId = second, OfficialNo = "GYM2026000000002", Ettn = Guid.NewGuid().ToString("D") },
            default);
        await fixture.Service("checker", new Documents())
            .ApproveAsync(Context("checker"), new CommandMetadata("approve-key"), Approve(), default);
        fixture.Db.ChangeTracker.Clear();
        var service = fixture.Service("checker", new Documents());

        var detail = await service.GetAsync(Context("checker"), Invoice, default);
        var all = await service.ListAsync(Context("checker"), false, null, default);
        var pending = await service.ListAsync(Context("checker"), true, null, default);
        var badCursor = await service.ListAsync(Context("checker"), true, "not-a-cursor", default);
        var empty = await service.GetAsync(Context("checker"), Guid.Empty, default);

        Assert.Equal(ReadOutcome.Found, detail.Outcome);
        Assert.Equal(FinanceOfficialSalesInvoiceStatusV1.Approved, detail.Resource!.Status);
        Assert.Equal(2, all.Resource!.Items.Count);
        Assert.Null(all.Resource.NextCursor);
        Assert.Equal(second, Assert.Single(pending.Resource!.Items).InvoiceId);
        Assert.Equal(ReadOutcome.NotFound, badCursor.Outcome);
        Assert.Equal(ReadOutcome.NotFound, empty.Outcome);
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, ReadOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, ReadOutcome.Unavailable)]
    public async Task Reads_are_refused_without_fresh_read_authority(PermissionDecision decision, ReadOutcome outcome)
    {
        var fixture = Fixture.Create();
        var service = fixture.Service("reader", new Documents(), new Authority(decision));

        var detail = await service.GetAsync(Context("reader"), Invoice, default);
        var list = await service.ListAsync(Context("reader"), true, null, default);

        Assert.Equal(outcome, detail.Outcome);
        Assert.Equal(outcome, list.Outcome);
    }

    [Fact]
    public void The_model_carries_the_maker_checker_constraint_and_the_unique_official_identities()
    {
        var fixture = Fixture.Create();
        var entity = fixture.Db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(FinanceOfficialSalesInvoiceRow))!;

        var approval = Assert.Single(entity.GetCheckConstraints(), c => c.Name == "CK_OfficialSalesInvoices_Approval");
        Assert.Equal(FinanceDbContext.OfficialSalesInvoiceApprovalCheck, approval.Sql);
        Assert.Contains("[ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [RecordedBy]", approval.Sql,
            StringComparison.Ordinal);
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.GetDatabaseName()
            == "UX_OfficialSalesInvoices_OfficialNo");
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.GetDatabaseName()
            == "UX_OfficialSalesInvoices_Ettn");
        Assert.Equal("finance", entity.GetSchema());
    }

    [Fact]
    public async Task An_approved_record_cannot_be_changed_or_deleted_by_the_write_guard()
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        await fixture.Service("checker", new Documents())
            .ApproveAsync(Context("checker"), new CommandMetadata("approve-key"), Approve(), default);
        fixture.Db.ChangeTracker.Clear();

        var row = await fixture.Db.OfficialSalesInvoices.SingleAsync();
        row.PayableAmountText = "1.00";
        Assert.Throws<InvalidOperationException>(() => fixture.Db.SaveChanges());
        fixture.Db.ChangeTracker.Clear();

        fixture.Db.OfficialSalesInvoices.Remove(await fixture.Db.OfficialSalesInvoices.SingleAsync());
        Assert.Throws<InvalidOperationException>(() => fixture.Db.SaveChanges());
        fixture.Db.ChangeTracker.Clear();

        var command = await fixture.Db.OfficialSalesInvoiceCommands.FirstAsync();
        command.ResultJson = "{}";
        Assert.Throws<InvalidOperationException>(() => fixture.Db.SaveChanges());
    }

    [Fact]
    public void The_migration_is_pure_ddl_up_and_its_rollback_refuses_first_while_a_record_is_stored()
    {
        var migration = new AddOfficialSalesInvoices();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51051", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[OfficialSalesInvoices]", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[OfficialSalesInvoiceCommands]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }

    private static async Task<FinanceOfficialSalesInvoiceResultV1> Record(Fixture fixture)
    {
        var result = await fixture.Service("maker", new Documents())
            .RecordAsync(Context("maker"), new CommandMetadata("record-key"), Request(), default);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        return result;
    }

    private static FinanceOfficialSalesInvoiceRecordRequestV1 Request() => new(
        Invoice, "GYM2026000000001", Ettn, Customer, Currency, "1250.50", "1250.50", "1042.08", "208.42", "0", Pdf);

    private static FinanceOfficialSalesInvoiceApproveRequestV1 Approve() => new(Invoice, "1");

    private static FinanceOfficialSalesInvoiceRecordRequestV1 Invalid(string variant) => variant switch
    {
        "invoiceId" => Request() with { InvoiceId = Guid.Empty },
        "officialNo-empty" => Request() with { OfficialNo = " " },
        "officialNo-padded" => Request() with { OfficialNo = " GYM1" },
        "officialNo-long" => Request() with { OfficialNo = new string('N', 65) },
        "ettn-empty" => Request() with { Ettn = string.Empty },
        "ettn-not-guid" => Request() with { Ettn = new string('x', 36) },
        "ettn-empty-guid" => Request() with { Ettn = Guid.Empty.ToString("D") },
        "customerPartyId" => Request() with { CustomerPartyId = Guid.Empty },
        "currencyId" => Request() with { CurrencyId = Guid.Empty },
        "payableAmount-empty" => Request() with { PayableAmount = string.Empty },
        "payableAmount-zero" => Request() with { PayableAmount = "0" },
        "payableAmount-negative" => Request() with { PayableAmount = "-1" },
        "payableAmount-does-not-fit-decimal" => Request() with { PayableAmount = "1." + new string('1', 40) },
        "payableFunctionalAmount-text" => Request() with { PayableFunctionalAmount = "bir" },
        "taxBaseFunctionalAmount-missing" => Request() with { TaxBaseFunctionalAmount = null! },
        "taxBaseFunctionalAmount-negative" => Request() with { TaxBaseFunctionalAmount = "-0.01" },
        "vatFunctionalAmount-empty" => Request() with { VatFunctionalAmount = null! },
        "withholdingFunctionalAmount-negative" => Request() with { WithholdingFunctionalAmount = "-0.01" },
        "pdfDocumentId" => Request() with { PdfDocumentId = Guid.Empty },
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    private static FinanceContext Context(string actor, Guid? company = null) =>
        new(company ?? Company, actor, $"correlation-{actor}", null);

    private sealed record Fixture(FinanceDbContext Db, Guid CompanyId, AuditCollector Audit)
    {
        public static Fixture Create(Guid? company = null, string? databaseName = null)
        {
            var companyId = company ?? Company;
            var context = new CompanyContext();
            context.Set(companyId);
            var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName ?? $"finance-official-sales-{Guid.NewGuid():N}").Options, context);
            return new Fixture(db, companyId, new AuditCollector());
        }

        public FinanceOfficialSalesInvoiceServiceV1 Service(string actor, Documents documents,
            IFinanceOfficialSalesInvoiceAuthorityV1? authority = null)
        {
            var context = new CompanyContext();
            context.Set(CompanyId);
            return new FinanceOfficialSalesInvoiceServiceV1(Db, context, new User(actor, CompanyId),
                authority ?? new Authority(), documents, Audit,
                new FinanceOfficialSalesInvoiceAuditEnvelopesV1(
                    new FinanceAuditEnvelope(FinanceOfficialSalesInvoicePermissionNamesV1.Record, [], "unit-test"),
                    new FinanceAuditEnvelope(FinanceOfficialSalesInvoicePermissionNamesV1.Approve, [], "unit-test")),
                TimeProvider.System);
        }
    }

    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    /// <summary>Answers the queued decisions in order, then repeats the last one (Allowed when none given).</summary>
    private sealed class Authority(params PermissionDecision[] decisions) : IFinanceOfficialSalesInvoiceAuthorityV1
    {
        private readonly Queue<PermissionDecision> queue = new(decisions);
        private PermissionDecision last = decisions.Length == 0 ? PermissionDecision.Allowed : decisions[^1];

        public Task<PermissionDecision> CheckAsync(FinanceContext context,
            FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId, CancellationToken cancellationToken)
        {
            if (queue.Count > 0) last = queue.Dequeue();
            return Task.FromResult(last);
        }
    }

    private sealed class Documents(FinanceOfficialSalesInvoicePdfCheckV1? answer = null)
        : IFinanceOfficialSalesInvoiceDocumentPortV1
    {
        public List<(Guid Document, Guid Customer, int? Version)> Requests { get; } = [];
        public int Calls => Requests.Count;

        public Task<FinanceOfficialSalesInvoicePdfCheckV1> CheckAsync(FinanceContext context, Guid documentId,
            Guid customerPartyId, int? versionNo, CancellationToken cancellationToken)
        {
            Requests.Add((documentId, customerPartyId, versionNo));
            return Task.FromResult(answer
                ?? new FinanceOfficialSalesInvoicePdfCheckV1(FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted, 1, Sha));
        }
    }

    private sealed class AuditCollector : IAuditEventCollector
    {
        public List<AuditEventDraft> Events { get; } = [];
        public void Enqueue(AuditEventDraft auditEvent) => Events.Add(auditEvent);
    }
}
