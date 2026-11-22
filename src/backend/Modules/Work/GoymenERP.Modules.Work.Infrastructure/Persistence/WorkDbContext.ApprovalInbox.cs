using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

public sealed partial class WorkDbContext
{
    internal const string ApprovalInboxEventIdentityCheck =
        "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [ItemId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Kind]) > 0 AND [EventType] IN (1, 2, 3, 4)";
    internal const string ApprovalInboxEventShapeCheck =
        "([EventType] IN (1, 4) AND [ClientEventId] IS NULL) OR ([EventType] IN (2, 3) AND [ClientEventId] IS NOT NULL "
        + "AND [ClientEventId] <> '00000000-0000-0000-0000-000000000000')";
    internal const string ApprovalInboxEventTimeCheck =
        "([EventType] <> 1 OR [OccurredAtUtc] = [CycleStartedAtUtc]) AND [RecordedAtUtc] >= [CycleStartedAtUtc]";

    internal DbSet<WorkApprovalInboxEventRow> ApprovalInboxEvents => Set<WorkApprovalInboxEventRow>();

    private void ConfigureApprovalInbox(ModelBuilder model)
    {
        var entity = model.Entity<WorkApprovalInboxEventRow>();
        entity.ToTable("ApprovalInboxEvents", Schema, table =>
        {
            table.HasCheckConstraint("CK_WorkApprovalInboxEvents_Identity", ApprovalInboxEventIdentityCheck);
            table.HasCheckConstraint("CK_WorkApprovalInboxEvents_Shape", ApprovalInboxEventShapeCheck);
            table.HasCheckConstraint("CK_WorkApprovalInboxEvents_Time", ApprovalInboxEventTimeCheck);
        });
        entity.HasKey(row => new { row.CompanyId, row.Id });
        entity.Property(row => row.Kind).HasMaxLength(160).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        // One arrival and one observed resolution per pending cycle; a racing reader's duplicate is refused here.
        entity.HasIndex(row => new { row.CompanyId, row.Kind, row.ItemId, row.CycleStartedAtUtc, row.EventType })
            .IsUnique().HasFilter("[EventType] IN (1, 4)").HasDatabaseName("UX_WorkApprovalInboxEvents_Cycle");
        entity.HasIndex(row => new { row.CompanyId, row.ClientEventId })
            .IsUnique().HasFilter("[ClientEventId] IS NOT NULL").HasDatabaseName("UX_WorkApprovalInboxEvents_ClientEvent");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
    }
}
