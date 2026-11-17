using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingDbContext
{
    internal const string SourceVoucherLifecycleStatusCheck =
        "([Status] IN (1,3) AND [JournalId] IS NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR "
        + "([Status] = 2 AND [JournalId] IS NOT NULL AND [JournalId] = [Id] AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NOT NULL "
        + "AND [ApprovedBy] <> [SourceActorId] AND [ApprovedBy] <> '00000000-0000-0000-0000-000000000000' AND [ApprovedAtUtc] IS NOT NULL)";

    internal DbSet<AccountingSourceInvoiceCancellationRow> SourceInvoiceCancellations => Set<AccountingSourceInvoiceCancellationRow>();

    private void ConfigureSourceInvoiceCancellations(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AccountingSourceInvoiceCancellationRow>();
        entity.ToTable("SourceInvoiceCancellations", Schema, table =>
        {
            table.HasCheckConstraint("CK_SourceInvoiceCancellations_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [CancellationId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SourceDocumentId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CancelledBy] <> '00000000-0000-0000-0000-000000000000' AND [FinalizedVersion] > 0");
            table.HasCheckConstraint("CK_SourceInvoiceCancellations_Kind", "[DocumentKind] IN ('sales-invoice','purchase-invoice')");
            table.HasCheckConstraint("CK_SourceInvoiceCancellations_Reason", "LEN([Reason]) > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.CancellationId });
        entity.Property(x => x.CancellationId).ValueGeneratedNever();
        entity.Property(x => x.DocumentKind).HasMaxLength(32).UseCollation(IdentityCollation);
        entity.Property(x => x.Reason).HasMaxLength(1000);
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        entity.Property(x => x.CorrelationId).HasMaxLength(128);
        entity.HasIndex(x => new { x.CompanyId, x.SourceDocumentId }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.DocumentKind, x.InvoiceId, x.FinalizedVersion }).IsUnique();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private static readonly string[] SourceVoucherMutableFields =
    [nameof(AccountingSourceDocumentVoucherDraftRow.Status), nameof(AccountingSourceDocumentVoucherDraftRow.PeriodId),
     nameof(AccountingSourceDocumentVoucherDraftRow.ApprovedBy), nameof(AccountingSourceDocumentVoucherDraftRow.ApprovedAtUtc),
     nameof(AccountingSourceDocumentVoucherDraftRow.JournalId), nameof(AccountingSourceDocumentVoucherDraftRow.UpdatedAtUtc)];

    private static void ValidateSourceVoucherChange(EntityEntry<IAccountingTenantRow> entry, AccountingSourceDocumentVoucherDraftRow row)
    {
        var before = (int)entry.Property(nameof(row.Status)).OriginalValue!;
        var version = (long)entry.Property(nameof(row.Version)).OriginalValue!;
        // A posted voucher's cancellation-reversal proposal advances only its concurrency token
        // and update instant. Its source, amount, original posting and approval are still immutable.
        if (before == 2 && row.Status == 2 && row.Version == checked(version + 1)
            && entry.Properties.Where(property => property.IsModified && !property.Metadata.IsConcurrencyToken)
                .All(property => property.Metadata.Name == nameof(row.UpdatedAtUtc))) return;
        if (before != 1 || row.Status is not (2 or 3) || row.Version != checked(version + 1))
            throw new InvalidOperationException("Kaynak fiş yalnız onay veya iptalle bir kez ilerler.");
        if (row.Status == 2 && (row.JournalId != row.Id || row.PeriodId is null || row.ApprovedBy is null
            || row.ApprovedBy == row.SourceActorId || row.ApprovedAtUtc is null))
            throw new InvalidOperationException("Kaynak fiş onayı gerçek yevmiye ve ayrı onaylayan gerektirir.");
    }
}
