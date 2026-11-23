using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceDocumentNumberingTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void All_four_actual_source_tables_keep_nullable_immutable_number_and_format_proof_without_legacy_backfill()
    {
        var company = new CompanyContext(); company.Set(Company);
        using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true").Options, company);
        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(ReceiptDraftRow), typeof(ManualBankPaymentRow), typeof(ExpensePaymentRow), typeof(CashTransferRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.True(entity.FindProperty("DocumentNumber")!.IsNullable);
            Assert.True(entity.FindProperty("DocumentNumberFormatVersion")!.IsNullable);
            Assert.Equal(64, entity.FindProperty("DocumentNumber")!.GetMaxLength());
            Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name)
                .SequenceEqual(new[] { "CompanyId", "DocumentNumber" }) && index.GetFilter() == "[DocumentNumber] IS NOT NULL");
            Assert.Contains(entity.GetCheckConstraints(), x => x.Name!.EndsWith("_DocumentNumber", StringComparison.Ordinal)
                && x.Sql.Contains("[DocumentNumber] IS NULL AND [DocumentNumberFormatVersion] IS NULL", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Recorded_number_migration_has_only_nullable_DDL_and_a_first_guard_while_legacy_rows_remain_untouched()
    {
        var company = new CompanyContext(); company.Set(Company);
        using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true").Options, company);
        Assert.Equal("20261117200743_AddFinanceRecordedDocumentNumbers",
            Assert.Single(db.Database.GetMigrations(), id => id.EndsWith("_AddFinanceRecordedDocumentNumbers", StringComparison.Ordinal)));
        Assert.False(db.Database.HasPendingModelChanges());
        var migration = new AddFinanceRecordedDocumentNumbers();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var columns = migration.UpOperations.OfType<AddColumnOperation>().ToArray();
        Assert.Equal(8, columns.Length);
        Assert.All(columns, column => { Assert.True(column.IsNullable); Assert.Null(column.DefaultValue); Assert.Null(column.DefaultValueSql); });
        Assert.Equal(4, migration.UpOperations.OfType<CreateIndexOperation>().Count());
        Assert.Equal(4, migration.UpOperations.OfType<AddCheckConstraintOperation>().Count());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51911", guard);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        foreach (var table in new[] { "ReceiptDrafts", "ManualBankPayments", "ExpensePayments", "CashTransfers" })
            Assert.Contains("[finance].[" + table + "] WHERE [DocumentNumber] IS NOT NULL OR [DocumentNumberFormatVersion] IS NOT NULL", guard);
    }

    [Fact]
    public async Task Missing_number_composition_is_unavailable_and_a_foreign_number_reply_is_never_accepted()
    {
        var company = new CompanyContext(); company.Set(Company);
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        var context = new FinanceContext(Company, "SENTETIK", "SENTETIK-correlation", null);
        var purpose = SettingsFinanceDocumentPurposeV1.ManualReceiptRecord;
        foreach (var port in new IFinanceDocumentNumberReservationV1?[] { null, new ForeignNumber() })
        {
            var answer = await FinanceTreasuryTransaction.ExecuteAsync(db,
                () => FinanceDocumentNumbering.ReserveAsync(db, port, context, new("SENTETIK-key"),
                    new("finance.manual-receipt.record", [], "Test"), purpose, Guid.NewGuid(), new(2026, 11, 17), default),
                () => Task.FromResult(new FinanceDocumentNumbering.Answer(null, false, "unavailable")), null, default,
                result => result.Number is not null);
            Assert.Null(answer.Number);
            Assert.Equal(SettingsDocumentNumberSeriesProblemCodesV1.Unavailable, answer.Code);
            Assert.Empty(db.ChangeTracker.Entries());
        }
    }

    [Fact]
    public void Number_is_not_a_draft_or_bank_reference_and_existing_legacy_json_omits_the_new_optional_field()
    {
        var row = new ReceiptDraftRow { Id = Guid.NewGuid(), CompanyId = Company, Stage = ReceiptStages.Draft,
            BankReference = "SENTETIK-external-bank-reference" };
        Assert.Null(FinanceDocumentNumbering.View(row));
        var hidden = new AmountView(AmountDisclosure.Redacted, null);
        var detail = new SourceDetail(new SourceSummary(row.Id, Company, row.Id.ToString("D"), "1", SourceStage.Draft,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 11, 17), null, hidden),
            new("SENTETIK-profile", "1"), row.BankReference, null, null, null, null,
            new UnallocatedView(AllocationKnowledge.Unavailable, hidden));
        var legacy = JsonSerializer.Serialize(detail, FinanceJson.CreateOptions());
        Assert.DoesNotContain("\"documentNumber\"", legacy, StringComparison.Ordinal);
        row.DocumentNumber = "MAK-2026-000001"; row.DocumentNumberFormatVersion = "1"; row.Stage = ReceiptStages.Recorded;
        var recorded = detail with { DocumentNumber = FinanceDocumentNumbering.View(row) };
        Assert.Equal(new FinanceDocumentNumberV1(row.DocumentNumber, "1"), recorded.DocumentNumber);
        Assert.Equal("SENTETIK-external-bank-reference", recorded.BankReference);
        var request = new RecordRequest(row.Id, "1");
        Assert.Equal(ReceiptDraftService.RecordPayloadHash(request), ReceiptDraftService.RecordPayloadHash(request with { }));
    }

    [Fact]
    public async Task A_nested_treasury_attempt_is_unavailable_without_removing_the_outer_marker_and_the_context_can_be_reused()
    {
        var company = new CompanyContext(); company.Set(Company);
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        var nestedOperationRan = false;
        var outer = await FinanceTreasuryTransaction.ExecuteAsync(db, async () =>
        {
            Assert.True(FinanceTreasuryTransaction.RequiresFullRollback(db));
            var nested = await FinanceTreasuryTransaction.ExecuteAsync(db,
                () => { nestedOperationRan = true; return Task.FromResult("unexpected"); },
                () => Task.FromResult("unavailable"), null, default);
            Assert.Equal("unavailable", nested);
            Assert.True(FinanceTreasuryTransaction.RequiresFullRollback(db));
            return "outer";
        }, () => Task.FromResult("unavailable"), null, default);
        Assert.Equal("outer", outer);
        Assert.False(nestedOperationRan);
        Assert.False(FinanceTreasuryTransaction.RequiresFullRollback(db));
        var later = await FinanceTreasuryTransaction.ExecuteAsync(db, () => Task.FromResult("later"),
            () => Task.FromResult("unavailable"), null, default);
        Assert.Equal("later", later);
        Assert.False(FinanceTreasuryTransaction.RequiresFullRollback(db));
    }

    private sealed class ForeignNumber : IFinanceDocumentNumberReservationV1
    {
        public Task<SettingsDocumentNumberReservationV1> ReserveAsync(FinanceContext context, CommandMetadata metadata,
            FinanceAuditEnvelope envelope, SettingsFinanceDocumentPurposeV1 purpose, Guid sourceId, DateOnly businessDate,
            CancellationToken cancellationToken) => Task.FromResult(new SettingsDocumentNumberReservationV1(Guid.NewGuid(),
                SettingsDocumentSeriesV1.FinanceReceipt, sourceId, businessDate, "MAK-2026-000001", "1", 1, false));
    }
}
