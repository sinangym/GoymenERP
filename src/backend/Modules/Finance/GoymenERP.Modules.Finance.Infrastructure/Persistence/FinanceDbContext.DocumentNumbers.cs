using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

internal interface IFinanceNumberedSource : IFinanceTenantRow
{
    string? DocumentNumber { get; set; }
    string? DocumentNumberFormatVersion { get; set; }
}

public sealed partial class FinanceDbContext
{
    private static void ConfigureDocumentNumbers(ModelBuilder model)
    {
        Configure<ReceiptDraftRow>("ReceiptDrafts", "[Stage] = 1");
        Configure<ManualBankPaymentRow>("ManualBankPayments", "[Status] = 2");
        Configure<ExpensePaymentRow>("ExpensePayments", "[Status] = 2");
        Configure<CashTransferRow>("CashTransfers", "[TransferConfirmed] = 1");

        void Configure<T>(string table, string recorded) where T : class, IFinanceNumberedSource
        {
            var row = model.Entity<T>();
            row.Property(x => x.DocumentNumber).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
            row.Property(x => x.DocumentNumberFormatVersion).HasMaxLength(32).IsUnicode(false);
            row.HasIndex(x => new { x.CompanyId, x.DocumentNumber }).IsUnique().HasFilter("[DocumentNumber] IS NOT NULL");
            row.ToTable(table, Schema, t => t.HasCheckConstraint($"CK_{table}_DocumentNumber",
                "([DocumentNumber] IS NULL AND [DocumentNumberFormatVersion] IS NULL) OR " +
                $"({recorded} AND [DocumentNumber] IS NOT NULL AND LEN([DocumentNumber]) BETWEEN 1 AND 64 " +
                "AND [DocumentNumberFormatVersion] IS NOT NULL AND TRY_CONVERT(bigint,[DocumentNumberFormatVersion]) IS NOT NULL " +
                "AND TRY_CONVERT(bigint,[DocumentNumberFormatVersion]) > 0 " +
                "AND [DocumentNumberFormatVersion] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%' " +
                "AND LEFT([DocumentNumberFormatVersion],1) BETWEEN '1' AND '9')"));
        }
    }

    private void GuardDocumentNumbers()
    {
        foreach (var entry in ChangeTracker.Entries<IFinanceNumberedSource>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            var row = entry.Entity;
            if ((row.DocumentNumber is null) != (row.DocumentNumberFormatVersion is null))
                throw new InvalidOperationException("Belge numarası ve biçim sürümü birlikte kaydedilir.");
            if (row.DocumentNumber is not null && (row.DocumentNumber.Length is < 1 or > 64
                || !long.TryParse(row.DocumentNumberFormatVersion, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var format) || format < 1
                || row.DocumentNumberFormatVersion != format.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || (row switch
                {
                    ReceiptDraftRow receipt => receipt.Stage != ReceiptStages.Recorded,
                    ManualBankPaymentRow payment => payment.Status != ManualBankPaymentStatuses.Recorded,
                    ExpensePaymentRow expense => expense.Status != ExpensePaymentStatuses.Recorded,
                    CashTransferRow transfer => !transfer.TransferConfirmed,
                    _ => true,
                }))) throw new InvalidOperationException("Yalnız gerçek kesin kayıt belge numarası taşır.");
            if (entry.State == EntityState.Modified
                && (entry.OriginalValues[nameof(IFinanceNumberedSource.DocumentNumber)] is not null
                    || entry.OriginalValues[nameof(IFinanceNumberedSource.DocumentNumberFormatVersion)] is not null)
                && (entry.Property(nameof(IFinanceNumberedSource.DocumentNumber)).IsModified
                    || entry.Property(nameof(IFinanceNumberedSource.DocumentNumberFormatVersion)).IsModified))
                throw new InvalidOperationException("Kayıtlı finans belge numarası değiştirilemez.");
        }
    }
}
