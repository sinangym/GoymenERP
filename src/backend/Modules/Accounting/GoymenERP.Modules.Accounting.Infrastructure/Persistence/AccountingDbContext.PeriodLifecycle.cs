using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

internal sealed class AccountingPeriodClosureRow : IAccountingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PeriodId { get; set; }
    public long ClosedPeriodVersion { get; set; }
    public Guid TargetPeriodId { get; set; }
    public Guid CarryVoucherId { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public Guid ClosedBy { get; set; }
    public DateTimeOffset ClosedAtUtc { get; set; }
    public Guid? ReopeningId { get; set; }
    public Guid? ReopenedBy { get; set; }
    public DateTimeOffset? ReopenedAtUtc { get; set; }
    public long Version { get; set; }
}

internal sealed class AccountingPeriodReopeningRow : IAccountingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PeriodId { get; set; }
    public Guid ClosureId { get; set; }
    public long ClosedPeriodVersion { get; set; }
    public Guid ProposedBy { get; set; }
    public DateTimeOffset ProposedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public long Version { get; set; }
}

public sealed partial class AccountingDbContext
{
    internal DbSet<AccountingPeriodClosureRow> PeriodClosures => Set<AccountingPeriodClosureRow>();
    internal DbSet<AccountingPeriodReopeningRow> PeriodReopenings => Set<AccountingPeriodReopeningRow>();

    private void ConfigurePeriodLifecycle(ModelBuilder modelBuilder)
    {
        var close = modelBuilder.Entity<AccountingPeriodClosureRow>();
        close.ToTable("PeriodClosures", Schema, table =>
        {
            table.HasCheckConstraint("CK_PeriodClosures_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND "
                + "[CarryVoucherId] <> '00000000-0000-0000-0000-000000000000' AND [PeriodId] <> [TargetPeriodId] "
                + "AND [ClosedBy] <> '00000000-0000-0000-0000-000000000000' AND [ClosedPeriodVersion] > 1 "
                + "AND LEN([SourceHash]) = 64 AND [LineCount] >= 0");
            table.HasCheckConstraint("CK_PeriodClosures_Reopening", "([Version] = 1 AND [ReopeningId] IS NULL "
                + "AND [ReopenedBy] IS NULL AND [ReopenedAtUtc] IS NULL) OR ([Version] = 2 AND [ReopeningId] IS NOT NULL "
                + "AND [ReopeningId] <> '00000000-0000-0000-0000-000000000000' AND [ReopenedBy] IS NOT NULL "
                + "AND [ReopenedBy] <> '00000000-0000-0000-0000-000000000000' AND [ReopenedAtUtc] IS NOT NULL)");
        });
        close.HasKey(x => new { x.CompanyId, x.Id });
        close.Property(x => x.Id).ValueGeneratedNever();
        close.Property(x => x.Version).IsConcurrencyToken();
        close.Property(x => x.SourceHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        close.HasIndex(x => new { x.CompanyId, x.PeriodId, x.ClosedPeriodVersion }).IsUnique();
        close.HasIndex(x => new { x.CompanyId, x.CarryVoucherId }).IsUnique();
        close.HasOne<PeriodRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        close.HasOne<PeriodRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.TargetPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        close.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var reopen = modelBuilder.Entity<AccountingPeriodReopeningRow>();
        reopen.ToTable("PeriodReopenings", Schema, table =>
        {
            table.HasCheckConstraint("CK_PeriodReopenings_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ProposedBy] <> '00000000-0000-0000-0000-000000000000' AND [ClosedPeriodVersion] > 1 AND LEN([Reason]) > 0");
            table.HasCheckConstraint("CK_PeriodReopenings_Approval", "([Version] = 1 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) "
                + "OR ([Version] = 2 AND [ApprovedBy] IS NOT NULL AND [ApprovedBy] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ApprovedBy] <> [ProposedBy] AND [ApprovedAtUtc] IS NOT NULL AND [ApprovedAtUtc] >= [ProposedAtUtc])");
        });
        reopen.HasKey(x => new { x.CompanyId, x.Id });
        reopen.Property(x => x.Id).ValueGeneratedNever();
        reopen.Property(x => x.Version).IsConcurrencyToken();
        reopen.Property(x => x.Reason).HasMaxLength(1000);
        reopen.HasIndex(x => new { x.CompanyId, x.ClosureId }).IsUnique();
        reopen.HasOne<AccountingPeriodClosureRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ClosureId })
            .OnDelete(DeleteBehavior.Restrict);
        reopen.HasOne<PeriodRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        reopen.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private static void ValidatePeriodLifecycleChange(EntityEntry<IAccountingTenantRow> entry)
    {
        if (entry.Entity is not (AccountingPeriodClosureRow or AccountingPeriodReopeningRow)) return;
        if ((long)entry.Property("Version").OriginalValue! != 1 || (long)entry.Property("Version").CurrentValue! != 2)
            throw new InvalidOperationException("Dönem kapanışı veya yeniden açma onayı yalnız bir kez ilerler.");
        if (entry.Entity is AccountingPeriodReopeningRow row && (row.ApprovedBy is null || row.ApprovedAtUtc is null
            || row.ApprovedBy == row.ProposedBy || row.ApprovedAtUtc < row.ProposedAtUtc))
            throw new InvalidOperationException("Dönemi yeniden açmayı hazırlayan kendi önerisini onaylayamaz.");
        if (entry.Entity is AccountingPeriodClosureRow close && (close.ReopeningId is null || close.ReopenedBy is null || close.ReopenedAtUtc is null))
            throw new InvalidOperationException("Kapanışı geri alma kalıcı öneri ve onay izi gerektirir.");
    }
}
