using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

public sealed partial class WorkDbContext
{
    internal const string DelegationCheck = "[CompanyId] <> " + EmptyGuid + " AND [Id] <> " + EmptyGuid
        + " AND [DelegatorUserId] <> " + EmptyGuid + " AND [DelegateUserId] <> " + EmptyGuid
        + " AND [DelegatorUserId] <> [DelegateUserId] AND [RecordedByUserId] <> " + EmptyGuid
        + " AND LEN([Kind]) > 0 AND LEN([ApproverPermission]) > 0 AND LEN([Reason]) > 0"
        + " AND [StartsAtUtc] >= [RecordedAtUtc] AND [EndsAtUtc] > [StartsAtUtc]"
        + " AND (([Version] = 1 AND [RevokedByUserId] IS NULL AND [RevokedAtUtc] IS NULL AND [RevocationReason] IS NULL)"
        + " OR ([Version] = 2 AND [RevokedByUserId] IS NOT NULL AND [RevokedByUserId] <> " + EmptyGuid
        + " AND [RevokedAtUtc] IS NOT NULL AND [RevokedAtUtc] >= [RecordedAtUtc] AND [RevocationReason] IS NOT NULL AND LEN([RevocationReason]) > 0))";

    internal DbSet<WorkDelegationRow> Delegations => Set<WorkDelegationRow>();

    private void ConfigureDelegations(ModelBuilder model)
    {
        var row = model.Entity<WorkDelegationRow>();
        row.ToTable("ApprovalDelegations", Schema, table => table.HasCheckConstraint("CK_WorkApprovalDelegations_Content", DelegationCheck));
        row.HasKey(item => new { item.CompanyId, item.Id });
        row.Property(item => item.Kind).HasMaxLength(160).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        row.Property(item => item.ApproverPermission).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        row.Property(item => item.Reason).HasMaxLength(500);
        row.Property(item => item.RevocationReason).HasMaxLength(500);
        row.Property(item => item.Version).IsConcurrencyToken();
        row.Property(item => item.RowVersion).IsRowVersion();
        row.HasIndex(item => new { item.CompanyId, item.ApproverPermission, item.StartsAtUtc, item.EndsAtUtc });
        row.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
        row.HasQueryFilter(item => companyContext.IsResolved && item.CompanyId == companyContext.CompanyId);
    }

    private static void GuardDelegationWrite(EntityEntry<IWorkTenantRow> entry)
    {
        if (entry.Entity is not WorkDelegationRow row || entry.State != EntityState.Modified) return;
        string[] immutable = [nameof(row.Id), nameof(row.Kind), nameof(row.ApproverPermission), nameof(row.DelegatorUserId),
            nameof(row.DelegateUserId), nameof(row.StartsAtUtc), nameof(row.EndsAtUtc), nameof(row.RecordedByUserId),
            nameof(row.RecordedAtUtc), nameof(row.Reason)];
        if (immutable.Any(name => entry.Property(name).IsModified) || entry.Property(nameof(row.Version)).OriginalValue is not 1L
            || row.Version != 2 || row.RevokedByUserId is null || row.RevokedAtUtc is null || string.IsNullOrWhiteSpace(row.RevocationReason))
            throw new InvalidOperationException("A delegation can only be revoked once; its original scope and period are immutable.");
    }
}
