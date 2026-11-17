using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

public sealed partial class ManufacturingDbContext
{
    internal const string WorkCenterQueueIdentityCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [WorkCenterId] <> '00000000-0000-0000-0000-000000000000' AND [Revision] > 0";
    internal const string WorkCenterQueueTextCheck = "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0";
    internal const string WorkCenterQueueSequenceCheck = "[Sequence] > 0";
    internal DbSet<WorkCenterQueueVersionRow> WorkCenterQueueVersions => Set<WorkCenterQueueVersionRow>();
    internal DbSet<WorkCenterQueueEntryRow> WorkCenterQueueEntries => Set<WorkCenterQueueEntryRow>();

    private void ConfigureManualQueues(ModelBuilder modelBuilder)
    {
        var version = modelBuilder.Entity<WorkCenterQueueVersionRow>();
        version.ToTable("WorkCenterQueueVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_WorkCenterQueueVersions_Identity", WorkCenterQueueIdentityCheck);
            table.HasCheckConstraint("CK_WorkCenterQueueVersions_Text", WorkCenterQueueTextCheck);
        });
        version.HasKey(row => new { row.CompanyId, row.WorkCenterId, row.Revision });
        version.Property(row => row.RecordedBy).HasMaxLength(128).IsRequired();
        version.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        version.Property(row => row.Reason).HasMaxLength(256).IsRequired();
        version.HasOne<OrganizationWorkCenterReference>().WithMany().HasForeignKey(row => new { row.CompanyId, row.WorkCenterId })
            .OnDelete(DeleteBehavior.Restrict);
        version.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var entry = modelBuilder.Entity<WorkCenterQueueEntryRow>();
        entry.ToTable("WorkCenterQueueEntries", Schema, table => table.HasCheckConstraint("CK_WorkCenterQueueEntries_Sequence", WorkCenterQueueSequenceCheck));
        entry.HasKey(row => new { row.CompanyId, row.WorkCenterId, row.Revision, row.Sequence });
        entry.HasIndex(row => new { row.CompanyId, row.WorkCenterId, row.Revision, row.PlanId }).IsUnique();
        entry.HasOne<WorkCenterQueueVersionRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.WorkCenterId, row.Revision })
            .OnDelete(DeleteBehavior.Restrict);
        entry.HasOne<PlanRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.PlanId })
            .HasPrincipalKey(row => new { row.CompanyId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        entry.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
        // Both row types are append-only under ApplyWriteGuards; no new mutation admission is added.
    }
}

internal sealed class WorkCenterQueueVersionRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid WorkCenterId { get; set; }
    public long Revision { get; set; }
    public bool IsManual { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

internal sealed class WorkCenterQueueEntryRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid WorkCenterId { get; set; }
    public long Revision { get; set; }
    public int Sequence { get; set; }
    public Guid PlanId { get; set; }
}
