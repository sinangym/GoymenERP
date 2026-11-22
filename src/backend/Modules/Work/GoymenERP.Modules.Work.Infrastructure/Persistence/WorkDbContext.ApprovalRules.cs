using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

/// <summary>WP1: the versioned approval rule table (kind → approver permission, duration class, escalation permission).</summary>
public sealed partial class WorkDbContext
{
    internal const string ApprovalRuleVersionCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [Id] <> " + EmptyGuid + " AND [RecordedByUserId] <> " + EmptyGuid
        + " AND LEN([Kind]) > 0 AND [Version] >= 1 AND [DurationClass] IN (1, 2) AND LEN([ApproverPermission]) > 0"
        + " AND ([EscalationPermission] IS NULL OR LEN([EscalationPermission]) > 0)"
        + " AND LEN([Reason]) > 0 AND LEN([CorrelationId]) > 0";

    internal DbSet<WorkApprovalRuleVersionRow> ApprovalRuleVersions => Set<WorkApprovalRuleVersionRow>();

    private void ConfigureApprovalRules(ModelBuilder model)
    {
        var entity = model.Entity<WorkApprovalRuleVersionRow>();
        entity.ToTable("ApprovalRuleVersions", Schema,
            table => table.HasCheckConstraint("CK_WorkApprovalRuleVersions_Identity", ApprovalRuleVersionCheck));
        entity.HasKey(row => new { row.CompanyId, row.Id });
        entity.Property(row => row.Kind).HasMaxLength(160).IsUnicode(false).UseCollation("Latin1_General_100_BIN2").IsRequired();
        entity.Property(row => row.ApproverPermission).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        entity.Property(row => row.EscalationPermission).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        entity.Property(row => row.Reason).HasMaxLength(500).IsRequired();
        entity.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(row => row.CausationId).HasMaxLength(128);
        // One version number per kind: two racing changes from the same version cannot both land.
        entity.HasIndex(row => new { row.CompanyId, row.Kind, row.Version }).IsUnique()
            .HasDatabaseName("UX_WorkApprovalRuleVersions_KindVersion");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
    }
}
