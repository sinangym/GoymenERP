using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

public sealed partial class FinanceDbContext
{
    internal DbSet<FinanceCreditLimitRow> CreditLimits => Set<FinanceCreditLimitRow>();
    private void ConfigureCreditLimits(ModelBuilder model)
    {
        var row = model.Entity<FinanceCreditLimitRow>();
        row.ToTable("CreditLimits", Schema, table =>
        {
            table.HasCheckConstraint("CK_CreditLimits_Source", "ISJSON([SourceJson]) = 1 AND [CreditGroup] IN (1,2,3,4) AND [Revision] > 0");
            table.HasCheckConstraint("CK_CreditLimits_Amount", "LEN([AmountText]) > 0 AND [AmountText] NOT LIKE '%[^0-9.]%' AND [AmountText] NOT LIKE '%.%.%' AND [AmountText] NOT LIKE '.%' AND [AmountText] NOT LIKE '%.'");
            table.HasCheckConstraint("CK_CreditLimits_Decision", "([Status] = 1 AND [Version] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] IN (2,3) AND [Version] = 2 AND [DecidedBy] IS NOT NULL AND [DecidedBy] <> [ProposedBy] AND [DecidedAtUtc] IS NOT NULL AND (([Status] = 2 AND [RejectionReason] IS NULL) OR ([Status] = 3 AND LEN([RejectionReason]) > 0)))");
        });
        row.HasKey(x => new { x.CompanyId, x.Id });
        row.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        row.Property(x => x.Version).IsConcurrencyToken();
        row.Property(x => x.AmountText).HasMaxLength(128).IsUnicode(false);
        row.Property(x => x.ProposedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        row.Property(x => x.DecidedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        row.Property(x => x.RejectionReason).HasMaxLength(1000);
        row.HasIndex(x => new { x.CompanyId, x.LenderPartyId, x.LenderCompanyPartyAccountId, x.CreditGroup, x.CurrencyId })
            .IsUnique().HasFilter("[Status] = 1");
        row.HasIndex(x => new { x.CompanyId, x.LenderPartyId, x.LenderCompanyPartyAccountId, x.CreditGroup, x.CurrencyId, x.Revision })
            .IsUnique().HasFilter("[Status] = 2");
        row.HasOne<CreditAccountRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CreditAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        row.HasOne<FinanceCreditLimitRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PreviousLimitId }).OnDelete(DeleteBehavior.Restrict);
    }
    private static bool CreditLimitChangeAllowed(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IFinanceTenantRow> entry, FinanceCreditLimitRow row)
    {
        var before = (int)entry.OriginalValues[nameof(row.Status)]!;
        if (row.Version != (long)entry.OriginalValues[nameof(row.Version)]! + 1) return false;
        var changed = entry.Properties.Where(x => x.IsModified).Select(x => x.Metadata.Name).ToHashSet();
        if (before == 1 && row.Status is 2 or 3)
            return row.DecidedBy is not null && row.DecidedBy != row.ProposedBy && row.DecidedAtUtc is not null
                && (row.Status == 2 ? row.RejectionReason is null : !string.IsNullOrWhiteSpace(row.RejectionReason))
                && changed.IsSubsetOf([nameof(row.Version), nameof(row.Status), nameof(row.DecidedBy), nameof(row.DecidedAtUtc), nameof(row.RejectionReason)]);
        return false;
    }
}
