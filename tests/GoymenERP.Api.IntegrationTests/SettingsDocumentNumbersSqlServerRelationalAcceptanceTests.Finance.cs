using System.Data;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Data.SqlClient;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsCompanyPolicySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Finance_series_Down_allows_the_existing_purchase_series_and_refuses_51910_while_a_finance_format_exists()
    {
        await using var run = await HostRun.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddFinanceDocumentNumberSeries().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsDocumentNumberSeriesPermissionsV1.Manage);
        using var editor = run.Client(subject, CompanyA);
        const string path = "/api/v1/settings/company-policies/document-number-series/versions";
        await run.CommandAsync(editor, path, "SENTETIK-old-purchase-format",
            new RecordSettingsDocumentNumberFormatV1(SettingsDocumentSeriesV1.PurchaseOrder, "0", "ALI", 6, "SENTETIK mevcut seri"));
        await run.ExecuteAsync(guard);
        await run.CommandAsync(editor, path, "SENTETIK-new-receipt-format",
            new RecordSettingsDocumentNumberFormatV1(SettingsDocumentSeriesV1.FinanceReceipt, "0", "MAK", 6, "SENTETIK açık seri"));
        Assert.Equal(51910, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM settings.DocumentNumberFormats WHERE Series=5"));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM settings.DocumentNumberFormats WHERE Series=4"));
    }

    [SqlServerFact]
    public async Task Five_actual_finance_purposes_share_two_explicit_annual_series_under_their_own_record_permission()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsDocumentNumberSeriesPermissionsV1.Manage);
        using var editor = run.Client(subject, CompanyA);
        foreach (var (series, prefix) in new[] { (SettingsDocumentSeriesV1.FinanceReceipt, "MAK"), (SettingsDocumentSeriesV1.FinanceVoucher, "DEK") })
            await run.CommandAsync(editor, "/api/v1/settings/company-policies/document-number-series/versions", "SENTETIK-format-" + prefix,
                new RecordSettingsDocumentNumberFormatV1(series, "0", prefix, 6, "SENTETIK açık format"));
        var purposes = new[]
        {
            (SettingsFinanceDocumentPurposeV1.ManualReceiptRecord, ErpPermissions.FinanceManualReceiptRecord, "MAK-2026-000001"),
            (SettingsFinanceDocumentPurposeV1.CustomerInstrumentCollect, ErpPermissions.FinanceCustomerInstrumentCollect, "MAK-2026-000002"),
            (SettingsFinanceDocumentPurposeV1.ManualBankPaymentRecord, ErpPermissions.FinanceManualBankPaymentRecord, "DEK-2026-000001"),
            (SettingsFinanceDocumentPurposeV1.ExpensePaymentRecord, ErpPermissions.FinanceExpensePaymentRecord, "DEK-2026-000002"),
            (SettingsFinanceDocumentPurposeV1.CashTransferCommit, ErpPermissions.FinanceCashTransfersCommit, "DEK-2026-000003"),
        };
        foreach (var (purpose, permission, expected) in purposes)
        {
            var source = Guid.NewGuid();
            await using var port = run.Numbers(CompanyA, new NumberAuthority { OnlyPermission = permission });
            await using var tx = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var result = await port.Value.ReserveFinanceNumberAsync(tx.GetDbTransaction(), CompanyA, purpose, source,
                new(2026, 11, 17), NumberMetadata("SENTETIK-same-opaque-key"));
            Assert.Equal(expected, result.Number);
            var replay = await port.Value.ReserveFinanceNumberAsync(tx.GetDbTransaction(), CompanyA, purpose, source,
                new(2026, 11, 17), NumberMetadata("SENTETIK-same-opaque-key"));
            Assert.True(replay.Replayed); Assert.Equal(result.Number, replay.Number);
            await tx.CommitAsync();
        }
        Assert.Equal(5, await run.CountAsync("SELECT COUNT(*) FROM settings.DocumentNumberReservations"));
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM settings.DocumentNumberCounters"));
        await using (var port = run.Numbers(CompanyA, new NumberAuthority { OnlyPermission = ErpPermissions.FinanceCustomerInstrumentCollect }))
        {
            await using var tx = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => port.Value.ReserveFinanceNumberAsync(tx.GetDbTransaction(),
                CompanyA, SettingsFinanceDocumentPurposeV1.ManualReceiptRecord, Guid.NewGuid(), new(2027, 1, 1), NumberMetadata("SENTETIK-wrong-record-grant")));
            await tx.RollbackAsync();
        }
    }

    [SqlServerFact]
    public async Task Finance_number_reservation_has_no_implicit_format_and_a_caller_rollback_preserves_its_annual_counter_and_audit()
    {
        await using var run = await HostRun.StartAsync();
        await using (var port = run.Numbers(CompanyA))
        {
            await using var tx = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var error = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => port.Value.ReserveFinanceNumberAsync(
                tx.GetDbTransaction(), CompanyA, SettingsFinanceDocumentPurposeV1.CashTransferCommit, Guid.NewGuid(),
                new(2026, 11, 17), NumberMetadata("SENTETIK-no-format")));
            Assert.Equal(SettingsDocumentNumberSeriesProblemCodesV1.FormatMissing, error.Code);
        }
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM settings.DocumentNumberFormats"));
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsDocumentNumberSeriesPermissionsV1.Manage);
        using var editor = run.Client(subject, CompanyA);
        await run.CommandAsync(editor, "/api/v1/settings/company-policies/document-number-series/versions", "SENTETIK-voucher-format",
            new RecordSettingsDocumentNumberFormatV1(SettingsDocumentSeriesV1.FinanceVoucher, "0", "DEK", 6, "SENTETIK açık format"));
        await using (var port = run.Numbers(CompanyA))
        {
            await using var tx = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await port.Value.ReserveFinanceNumberAsync(tx.GetDbTransaction(), CompanyA, SettingsFinanceDocumentPurposeV1.CashTransferCommit,
                Guid.NewGuid(), new(2026, 11, 17), NumberMetadata("SENTETIK-rollback-number"));
            await tx.RollbackAsync();
        }
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM settings.DocumentNumberReservations"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM settings.DocumentNumberCounters"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'settings.document-number.reserved.v1'"));
        await using (var port = run.Numbers(CompanyA))
        {
            await using var tx = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var first = await port.Value.ReserveFinanceNumberAsync(tx.GetDbTransaction(), CompanyA, SettingsFinanceDocumentPurposeV1.CashTransferCommit,
                Guid.NewGuid(), new(2026, 11, 17), NumberMetadata("SENTETIK-first-number"));
            var annual = await port.Value.ReserveFinanceNumberAsync(tx.GetDbTransaction(), CompanyA, SettingsFinanceDocumentPurposeV1.CashTransferCommit,
                Guid.NewGuid(), new(2027, 1, 1), NumberMetadata("SENTETIK-next-year"));
            Assert.Equal(("DEK-2026-000001", "DEK-2027-000001"), (first.Number, annual.Number));
            await tx.CommitAsync();
        }
    }
}
