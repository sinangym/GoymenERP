using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingDbContext
{
    internal const string FinanceJournalSourceIdentityCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'";
    internal const string FinanceJournalSourcePayloadCheck = "ISJSON([SourceJson]) = 1 AND LEN([SourceHash]) = 64";
    internal const string FinanceJournalSourceKindCheck = "[SourceKind] IN (N'finance.expense-payment.recorded.v1', N'finance.cash-transfer.recorded.v1', "
        + "N'finance.cash-exchange.recorded.v1', N'finance.cash-exchange.book-value-approved.v1') "
        + "AND LEN([SourceVersion]) > 0 AND LEN([OwnerProfileId]) > 0 AND LEN([OwnerProfileVersion]) > 0 AND LEN([AmountText]) > 0";
    internal DbSet<FinanceJournalSourceRow> FinanceJournalSources => Set<FinanceJournalSourceRow>();
    private void ConfigureFinanceJournalSources(ModelBuilder model)
    {
        var row = model.Entity<FinanceJournalSourceRow>();
        row.ToTable("FinanceJournalSources", Schema, table =>
        {
            table.HasCheckConstraint("CK_FinanceJournalSources_Identity", FinanceJournalSourceIdentityCheck);
            table.HasCheckConstraint("CK_FinanceJournalSources_Payload", FinanceJournalSourcePayloadCheck);
            table.HasCheckConstraint("CK_FinanceJournalSources_Source", FinanceJournalSourceKindCheck);
        });
        row.HasKey(x => new { x.CompanyId, x.Id });
        row.HasIndex(x => new { x.CompanyId, x.SourceId, x.SourceKind, x.SourceVersion }).IsUnique();
        row.Property(x => x.SourceKind).HasMaxLength(128).UseCollation(IdentityCollation);
        row.Property(x => x.SourceVersion).HasMaxLength(64).UseCollation(IdentityCollation);
        row.Property(x => x.AmountText).HasMaxLength(128).IsUnicode(false);
        row.Property(x => x.OwnerProfileId).HasMaxLength(128).UseCollation(IdentityCollation);
        row.Property(x => x.OwnerProfileVersion).HasMaxLength(128).UseCollation(IdentityCollation);
        row.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        row.Property(x => x.OccurredOn).HasColumnType("date");
        row.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        row.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}
