using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceOfficialSalesInvoiceServiceV1Tests
{
    [Fact]
    public async Task Import_first_difference_is_shown_in_detail_and_on_manual_approval_and_audited_once()
    {
        var fixture = Fixture.Create();
        var maker = fixture.Service("maker", new Documents());
        Assert.Equal(CommandOutcome.Succeeded, (await maker.RecordAsync(Context("maker"),
            new CommandMetadata("dated-manual"), Request(), default)).Outcome);
        var batchId = Guid.NewGuid(); var documentId = Guid.NewGuid();
        var csv = FinanceOfficialSalesInvoiceImportTemplateV3.Header + "\n" + string.Join(',', Guid.NewGuid(), documentId,
            Guid.NewGuid(), "1", Customer, Guid.NewGuid(), Currency, "1250.50", "2026-12-01", Request().OfficialNo,
            Ettn, "1042.08", "208.42", "0", "2026-11-16", "1300") + "\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var parsed = FinanceOfficialInvoiceImportParserV1.Parse(Context("maker"), new(batchId, "1", bytes));
        Assert.True(parsed.Preview.IsValid);
        fixture.Db.OfficialInvoiceImportBatches.Add(new() { Id = batchId, CompanyId = Company, BatchVersion = "1",
            BatchChecksum = parsed.Preview.SourceSha256, TemplateVersion = parsed.Preview.TemplateVersion, FileBytes = bytes,
            SourceRowsJson = JsonSerializer.Serialize(parsed.Preview.Batch!.Rows, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            RowCount = 1, UploadedBy = "maker", UploadedAtUtc = DateTimeOffset.UtcNow, CorrelationId = "SENTETIK" });
        fixture.Db.OfficialInvoiceImportApprovals.Add(new() { Id = Guid.NewGuid(), CompanyId = Company,
            BatchId = batchId, UploadedBy = "maker", ApprovedBy = "checker", ApprovedAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = "SENTETIK" });
        foreach (var (kind, value) in new[] { (FinanceOfficialInvoiceIdentityKinds.OfficialNo, Request().OfficialNo),
                     (FinanceOfficialInvoiceIdentityKinds.Ettn, Ettn) })
            fixture.Db.OfficialInvoiceIdentityClaims.Add(new() { Id = Guid.NewGuid(), CompanyId = Company,
                IdentityKind = kind, IdentityValue = value, Generation = 1, HolderBatchId = batchId,
                HolderSourceDocumentId = documentId, ClaimedBy = "checker", ClaimedAtUtc = DateTimeOffset.UtcNow, CorrelationId = "SENTETIK" });
        await fixture.Db.SaveChangesAsync(); fixture.Db.ChangeTracker.Clear();
        var detail = await maker.GetAsync(Context("maker"), Invoice, default);
        var pendingDifference = Assert.Single(detail.Resource!.SourceDifferences!);
        Assert.Equal("approved-import", pendingDifference.Winner);
        Assert.Equal(new[] { "payableFunctionalAmount" }, pendingDifference.Fields);
        var checker = fixture.Service("checker", new Documents());
        var approved = await checker.ApproveAsync(Context("checker"), new CommandMetadata("source-warning"), Approve(), default);
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        Assert.Equal("manual", Assert.Single(approved.Resource!.SourceDifferences!).Winner);
        Assert.Single(fixture.Audit.Events, item => item.EventType == FinanceOpenItemSourceAdmissionService.SourceDifferenceEventType);
        var replay = await checker.ApproveAsync(Context("checker"), new CommandMetadata("source-warning"), Approve(), default);
        Assert.True(replay.IsReplay);
        Assert.Single(fixture.Audit.Events, item => item.EventType == FinanceOpenItemSourceAdmissionService.SourceDifferenceEventType);
    }

    [Fact]
    public void Source_date_migration_is_nullable_for_unknown_history_and_refuses_down_after_real_dates()
    {
        var migration = new GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations.AddOfficialSalesInvoiceSourceDate();
        var column = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        Assert.Equal("InvoiceDate", column.Name);
        Assert.True(column.IsNullable);
        Assert.Null(column.DefaultValue);
        Assert.Null(column.DefaultValueSql);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51965", guard.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
    }

    [Fact]
    public async Task New_official_record_requires_source_invoice_date_and_never_uses_the_recording_clock()
    {
        var fixture = Fixture.Create();
        var documents = new Documents();
        var service = fixture.Service("maker", documents);
        var absent = await service.RecordAsync(Context("maker"), new CommandMetadata("missing-date"),
            Request() with { InvoiceDate = null }, default);
        Assert.Equal(CommandOutcome.ValidationFailed, absent.Outcome);
        Assert.Contains(absent.Issues, issue => issue.Field == "invoiceDate" && issue.Code == "invoice_date_required");
        Assert.Equal(0, documents.Calls);
        Assert.Empty(await fixture.Db.OfficialSalesInvoices.ToListAsync());
        var sourceDate = new DateOnly(2026, 9, 12);
        var saved = await service.RecordAsync(Context("maker"), new CommandMetadata("source-date"),
            Request() with { InvoiceDate = sourceDate }, default);
        Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
        Assert.Equal(sourceDate, saved.Resource!.InvoiceDate);
        var stored = await fixture.Db.OfficialSalesInvoices.SingleAsync();
        Assert.Equal(sourceDate, stored.InvoiceDate);
        stored.InvoiceDate = sourceDate.AddDays(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.SaveChangesAsync());
    }
}
