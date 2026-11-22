using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

public sealed partial class WorkDbContext
{
    internal DbSet<WorkNotificationInboxRow> NotificationInbox => Set<WorkNotificationInboxRow>();
    internal DbSet<WorkNotificationPassivationRow> NotificationPassivations => Set<WorkNotificationPassivationRow>();
    private void ConfigureNotifications(ModelBuilder model)
    {
        var inbox = model.Entity<WorkNotificationInboxRow>();
        inbox.ToTable("NotificationInbox", Schema, table =>
        {
            table.HasCheckConstraint("CK_WorkNotificationInbox_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND [SchemaVersion] >= 1 AND LEN([SourceName]) > 0 AND LEN([EventType]) > 0");
            table.HasCheckConstraint("CK_WorkNotificationInbox_Hash", string.Format(PayloadHashCheck, "PayloadSha256"));
        });
        inbox.HasKey(row => new { row.CompanyId, row.SourceName, row.EventId });
        inbox.Property(row => row.SourceName).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        inbox.Property(row => row.EventType).HasMaxLength(160).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        inbox.Property(row => row.PayloadSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength();
        inbox.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
        inbox.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        var passive = model.Entity<WorkNotificationPassivationRow>();
        passive.ToTable("NotificationPassivations", Schema, table => table.HasCheckConstraint("CK_WorkNotificationPassivations_Policy", "[ActiveDays] >= 0 AND LEN([PolicyVersion]) > 0"));
        passive.HasKey(row => new { row.CompanyId, row.NotificationId });
        passive.Property(row => row.PolicyVersion).HasMaxLength(128).IsUnicode(false);
        // D9 (WP2): passivation now points at the single notification store.
        passive.HasOne<WorkInboxNotificationRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.NotificationId }).OnDelete(DeleteBehavior.Restrict);
        passive.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
    }
}
