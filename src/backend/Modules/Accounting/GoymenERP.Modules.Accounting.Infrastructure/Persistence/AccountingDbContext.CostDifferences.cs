using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingDbContext
{
    internal DbSet<ManufacturingCostDifferenceRow> ManufacturingCostDifferences => Set<ManufacturingCostDifferenceRow>();

    internal const string ManufacturingCostDifferenceStateCheck =
        "([Status] IN (1,4) AND [PreparedBy] IS NULL AND [PreparedAtUtc] IS NULL AND [PeriodId] IS NULL "
        + "AND [AccountingDate] IS NULL AND [TransactionDate] IS NULL AND [CurrencyId] IS NULL "
        + "AND [DebitAccountId] IS NULL AND [CreditAccountId] IS NULL AND [AccountPlanVersion] IS NULL "
        + "AND [AccountPlanSha256] IS NULL AND [AccountPlanProofJson] IS NULL AND [Reason] IS NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [JournalId] IS NULL) "
        + "OR ([Status] IN (2,3) AND [PreparedBy] IS NOT NULL AND [PreparedBy] <> '00000000-0000-0000-0000-000000000000' AND [PreparedAtUtc] IS NOT NULL "
        + "AND [PeriodId] IS NOT NULL AND [AccountingDate] IS NOT NULL AND [TransactionDate] IS NOT NULL "
        + "AND [CurrencyId] IS NOT NULL AND [DebitAccountId] IS NOT NULL AND [CreditAccountId] IS NOT NULL "
        + "AND [DebitAccountId] <> [CreditAccountId] AND [PeriodId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [DebitAccountId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [CreditAccountId] <> '00000000-0000-0000-0000-000000000000' AND [AccountPlanVersion] IS NOT NULL AND LEN([AccountPlanVersion]) > 0 "
        + "AND [AccountPlanSha256] IS NOT NULL AND LEN([AccountPlanSha256]) = 64 AND [AccountPlanProofJson] IS NOT NULL AND ISJSON([AccountPlanProofJson]) = 1 AND [Reason] IS NOT NULL AND LEN([Reason]) > 0 "
        + "AND (([Status] = 2 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [JournalId] IS NULL) "
        + "OR ([Status] = 3 AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [JournalId] IS NOT NULL "
        + "AND [ApprovedBy] <> [PreparedBy] AND [ApprovedBy] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [JournalId] <> '00000000-0000-0000-0000-000000000000')))";

    private static readonly string[] CostDifferenceMutableFields =
    [ nameof(ManufacturingCostDifferenceRow.Status), nameof(ManufacturingCostDifferenceRow.PeriodId),
      nameof(ManufacturingCostDifferenceRow.AccountingDate), nameof(ManufacturingCostDifferenceRow.TransactionDate),
      nameof(ManufacturingCostDifferenceRow.CurrencyId), nameof(ManufacturingCostDifferenceRow.DebitAccountId),
      nameof(ManufacturingCostDifferenceRow.CreditAccountId), nameof(ManufacturingCostDifferenceRow.AccountPlanVersion),
      nameof(ManufacturingCostDifferenceRow.AccountPlanSha256), nameof(ManufacturingCostDifferenceRow.AccountPlanProofJson), nameof(ManufacturingCostDifferenceRow.PreparedBy),
      nameof(ManufacturingCostDifferenceRow.PreparedAtUtc), nameof(ManufacturingCostDifferenceRow.ApprovedBy),
      nameof(ManufacturingCostDifferenceRow.ApprovedAtUtc), nameof(ManufacturingCostDifferenceRow.JournalId),
      nameof(ManufacturingCostDifferenceRow.Reason) ];

    private void ConfigureManufacturingCostDifferences(ModelBuilder model)
    {
        var entity = model.Entity<ManufacturingCostDifferenceRow>();
        entity.ToTable("ManufacturingCostDifferences", Schema, table =>
        {
            table.HasCheckConstraint("CK_ManufacturingCostDifferences_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ManufacturingOrderId] <> '00000000-0000-0000-0000-000000000000' AND [GenerationId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_ManufacturingCostDifferences_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_ManufacturingCostDifferences_Source", "ISJSON([SourceJson]) = 1 AND LEN([SourceHash]) = 64");
            table.HasCheckConstraint("CK_ManufacturingCostDifferences_State", ManufacturingCostDifferenceStateCheck);
        });
        entity.HasKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.GenerationVersion).HasMaxLength(20);
        entity.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        entity.Property(x => x.SourceJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.SourceCorrelationId).HasMaxLength(128);
        entity.Property(x => x.SignedTlDifference).HasMaxLength(64);
        entity.Property(x => x.AccountingDate).HasColumnType("date");
        entity.Property(x => x.TransactionDate).HasColumnType("date");
        entity.Property(x => x.AccountPlanVersion).HasMaxLength(128);
        entity.Property(x => x.AccountPlanSha256).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        entity.Property(x => x.AccountPlanProofJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.Reason).HasMaxLength(1000);
        entity.HasIndex(x => new { x.CompanyId, x.GenerationId, x.GenerationVersion }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.Status, x.SourceOccurredAtUtc });
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PeriodRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<JournalRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.JournalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ValidateCostDifferenceChange(EntityEntry<IAccountingTenantRow> entry, ManufacturingCostDifferenceRow row)
    {
        var previous = (int)entry.OriginalValues[nameof(ManufacturingCostDifferenceRow.Status)]!;
        var version = (long)entry.OriginalValues[nameof(ManufacturingCostDifferenceRow.Version)]!;
        if (row.Version != checked(version + 1) || previous is not (1 or 2)
            || (row.Status != 2 && !(previous == 2 && row.Status == 3))
            || row.PreparedBy is null || row.PreparedAtUtc is null
            || row.Status == 3 && (row.ApprovedBy is null || row.ApprovedBy == row.PreparedBy || row.JournalId is null))
            throw new InvalidOperationException("Maliyet farkı yalnız hazırlık ve farklı kişinin onayıyla ilerler.");
        if (row.Status == 3 && CostDifferenceMutableFields.Where(name => name is not (nameof(ManufacturingCostDifferenceRow.Status)
            or nameof(ManufacturingCostDifferenceRow.ApprovedBy) or nameof(ManufacturingCostDifferenceRow.ApprovedAtUtc)
            or nameof(ManufacturingCostDifferenceRow.JournalId))).Any(name => entry.Property(name).IsModified))
            throw new InvalidOperationException("Onay sırasında hazırlanmış maliyet farkı değiştirilemez.");
    }
}
