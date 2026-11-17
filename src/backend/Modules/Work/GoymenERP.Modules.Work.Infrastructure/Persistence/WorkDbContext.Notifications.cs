using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

public sealed partial class WorkDbContext
{
    internal const string NotificationIdentityCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecipientUserId] <> '00000000-0000-0000-0000-000000000000' AND [SourceEntityId] <> '00000000-0000-0000-0000-000000000000' AND [Kind] IN (1, 2)";
    internal const string NotificationTargetCheck = "([SalesOrderId] IS NOT NULL AND [SalesOrderLineId] IS NOT NULL AND [ObservedSalesOrderVersion] IS NOT NULL AND [SalesOrderId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [ObservedSalesOrderVersion] >= 1 AND [PurchaseOrderId] IS NULL AND [PurchaseOrderLineId] IS NULL AND [ObservedPurchaseOrderVersion] IS NULL) OR ([Kind] = 2 AND [PurchaseOrderId] IS NOT NULL AND [PurchaseOrderLineId] IS NOT NULL AND [ObservedPurchaseOrderVersion] IS NOT NULL AND [PurchaseOrderId] <> '00000000-0000-0000-0000-000000000000' AND [PurchaseOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [ObservedPurchaseOrderVersion] >= 1 AND [SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL AND [ObservedSalesOrderVersion] IS NULL)";
    internal const string NotificationEvidenceCheck = "LEN([SourceVersion]) > 0 AND LEN([ReasonCode]) > 0 AND LEN([ReasonText]) > 0 AND LEN([CorrelationId]) > 0";
    internal DbSet<WorkNotificationInboxRow> NotificationInbox => Set<WorkNotificationInboxRow>();
    internal DbSet<WorkNotificationRow> Notifications => Set<WorkNotificationRow>();
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
        var item = model.Entity<WorkNotificationRow>();
        item.ToTable("Notifications", Schema, table =>
        {
            table.HasCheckConstraint("CK_WorkNotifications_Identity", NotificationIdentityCheck);
            table.HasCheckConstraint("CK_WorkNotifications_Target", NotificationTargetCheck);
            table.HasCheckConstraint("CK_WorkNotifications_Evidence", NotificationEvidenceCheck);
        });
        item.HasKey(row => new { row.CompanyId, row.Id });
        item.Property(row => row.SourceName).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        item.Property(row => row.SourceVersion).HasMaxLength(128).IsUnicode(false);
        item.Property(row => row.ReasonCode).HasMaxLength(64).IsUnicode(false);
        item.Property(row => row.ReasonText).HasMaxLength(4000);
        item.Property(row => row.CorrelationId).HasMaxLength(128);
        item.Property(row => row.CausationId).HasMaxLength(128);
        item.HasIndex(row => new { row.CompanyId, row.SourceName, row.EventId, row.RecipientUserId, row.SalesOrderId,
            row.SalesOrderLineId, row.PurchaseOrderId, row.PurchaseOrderLineId }).IsUnique().HasFilter(null);
        item.HasIndex(row => new { row.CompanyId, row.RecipientUserId, row.RecordedAtUtc, row.Id });
        item.HasOne<WorkNotificationInboxRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.SourceName, row.EventId }).OnDelete(DeleteBehavior.Restrict);
        item.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
        var passive = model.Entity<WorkNotificationPassivationRow>();
        passive.ToTable("NotificationPassivations", Schema, table => table.HasCheckConstraint("CK_WorkNotificationPassivations_Policy", "[ActiveDays] >= 0 AND LEN([PolicyVersion]) > 0"));
        passive.HasKey(row => new { row.CompanyId, row.NotificationId });
        passive.Property(row => row.PolicyVersion).HasMaxLength(128).IsUnicode(false);
        passive.HasOne<WorkNotificationRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.NotificationId }).OnDelete(DeleteBehavior.Restrict);
        passive.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
    }
}
