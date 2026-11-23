using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

internal sealed class LogisticsPlanExcessRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid ExceptionId { get; set; }
    public Guid InstructionId { get; set; }
    public Guid LineId { get; set; }
    public decimal Quantity { get; set; }
    public long Version { get; set; }
    public int Status { get; set; }
    public string Reason { get; set; } = "";
    public string ProposedBy { get; set; } = "";
    public DateTimeOffset ProposedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
}
internal sealed class LogisticsPlanExcessUsageRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid ExceptionId { get; set; }
    public Guid ShareId { get; set; }
    public Guid InstructionId { get; set; }
    public Guid LineId { get; set; }
    public int Kind { get; set; }
    public decimal Quantity { get; set; }
    public string ActorId { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}
internal sealed class LogisticsPlanExcessReservationRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid ExceptionId { get; set; }
    public Guid InstructionId { get; set; }
    public Guid LineId { get; set; }
    public Guid AllocationId { get; set; }
    public Guid LabelId { get; set; }
    public decimal Quantity { get; set; }
    public string OriginalAllocationVersion { get; set; } = "";
    public string OriginalLabelVersion { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}
public sealed partial class LogisticsDbContext
{
    internal DbSet<LogisticsPlanExcessRow> PlanExcessExceptions => Set<LogisticsPlanExcessRow>();
    internal DbSet<LogisticsPlanExcessUsageRow> PlanExcessUsages => Set<LogisticsPlanExcessUsageRow>();
    internal DbSet<LogisticsPlanExcessReservationRow> PlanExcessReservations => Set<LogisticsPlanExcessReservationRow>();
    private void ConfigurePlanExcess(ModelBuilder model)
    {
        var row = model.Entity<LogisticsPlanExcessRow>();
        row.ToTable("PlanExcessExceptions", Schema, table =>
        {
            table.HasTrigger("TR_PlanExcessExceptions_ForwardOnly");
            table.HasCheckConstraint("CK_PlanExcessExceptions_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_PlanExcessExceptions_State", "([Status]=1 AND [Version]=1 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([Status]=2 AND [Version]=2 AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL)");
        });
        row.HasKey(x => new { x.CompanyId, x.ExceptionId });
        row.Property(x => x.Quantity).HasPrecision(18, 3);
        row.Property(x => x.Version).IsConcurrencyToken();
        row.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        row.Property(x => x.ProposedBy).HasMaxLength(128).IsRequired();
        row.Property(x => x.ApprovedBy).HasMaxLength(128);
        row.HasIndex(x => new { x.CompanyId, x.InstructionId, x.LineId, x.ExceptionId });
        row.HasOne<LoadingInstructionLineRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InstructionId, x.LineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.InstructionId, x.LineId }).OnDelete(DeleteBehavior.Restrict);
        row.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        var use = model.Entity<LogisticsPlanExcessUsageRow>();
        use.ToTable("PlanExcessUsages", Schema, table =>
        {
            table.HasTrigger("TR_PlanExcessUsages_Immutable");
            table.HasCheckConstraint("CK_PlanExcessUsages_Shape", "[Quantity]>0 AND [Kind] IN (1,2)");
        });
        use.HasKey(x => new { x.CompanyId, x.ExceptionId, x.ShareId });
        use.Property(x => x.Quantity).HasPrecision(18, 3);
        use.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        use.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        use.HasOne<LogisticsPlanExcessRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ExceptionId, x.InstructionId, x.LineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.ExceptionId, x.InstructionId, x.LineId }).OnDelete(DeleteBehavior.Restrict);
        use.HasOne<LoadingShareRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InstructionId, x.ShareId })
            .HasPrincipalKey(x => new { x.CompanyId, x.InstructionId, x.ShareId }).OnDelete(DeleteBehavior.Restrict);
        use.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        var reservation = model.Entity<LogisticsPlanExcessReservationRow>();
        reservation.ToTable("PlanExcessReservations", Schema, table =>
        {
            table.HasTrigger("TR_PlanExcessReservations_Immutable");
            table.HasCheckConstraint("CK_PlanExcessReservations_Quantity", "[Quantity]>0");
        });
        reservation.HasKey(x => new { x.CompanyId, x.AllocationId });
        reservation.Property(x => x.Quantity).HasPrecision(18, 3);
        reservation.Property(x => x.OriginalAllocationVersion).HasMaxLength(20).IsRequired();
        reservation.Property(x => x.OriginalLabelVersion).HasMaxLength(20).IsRequired();
        reservation.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        reservation.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        reservation.HasOne<LogisticsPlanExcessRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ExceptionId, x.InstructionId, x.LineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.ExceptionId, x.InstructionId, x.LineId }).OnDelete(DeleteBehavior.Restrict);
        reservation.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }
}
