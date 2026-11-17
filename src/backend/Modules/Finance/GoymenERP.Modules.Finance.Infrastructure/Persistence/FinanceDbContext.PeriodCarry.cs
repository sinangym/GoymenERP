using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

internal sealed class FinancePeriodCarryVoucherRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ClosureId { get; set; }
    public Guid SourcePeriodId { get; set; }
    public long ClosedPeriodVersion { get; set; }
    public DateOnly SourceEndsOn { get; set; }
    public Guid TargetPeriodId { get; set; }
    public DateOnly TargetStartsOn { get; set; }
    public string SourceJson { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public string LinesJson { get; set; } = string.Empty;
    public Guid RecordedBy { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal sealed class FinancePeriodCarryReversalRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ClosureId { get; set; }
    public Guid VoucherId { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
}

public sealed partial class FinanceDbContext
{
    internal DbSet<FinancePeriodCarryVoucherRow> PeriodCarryVouchers => Set<FinancePeriodCarryVoucherRow>();
    internal DbSet<FinancePeriodCarryReversalRow> PeriodCarryReversals => Set<FinancePeriodCarryReversalRow>();

    private void ConfigurePeriodCarry(ModelBuilder model)
    {
        var voucher = model.Entity<FinancePeriodCarryVoucherRow>();
        voucher.ToTable("PeriodCarryVouchers", Schema, table =>
        {
            table.HasCheckConstraint("CK_PeriodCarryVouchers_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ClosureId] <> '00000000-0000-0000-0000-000000000000' AND [SourcePeriodId] <> [TargetPeriodId] "
                + "AND [RecordedBy] <> '00000000-0000-0000-0000-000000000000' AND [ClosedPeriodVersion] > 1 "
                + "AND [TargetStartsOn] > [SourceEndsOn]");
            table.HasCheckConstraint("CK_PeriodCarryVouchers_Source", "ISJSON([SourceJson]) = 1 AND ISJSON([LinesJson]) = 1 AND LEN([SourceHash]) = 64");
        });
        voucher.HasKey(x => new { x.CompanyId, x.Id });
        voucher.Property(x => x.Id).ValueGeneratedNever();
        voucher.Property(x => x.SourceHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        voucher.Property(x => x.SourceEndsOn).HasColumnType("date");
        voucher.Property(x => x.TargetStartsOn).HasColumnType("date");
        voucher.HasIndex(x => new { x.CompanyId, x.ClosureId }).IsUnique();
        voucher.HasIndex(x => new { x.CompanyId, x.SourcePeriodId, x.ClosedPeriodVersion }).IsUnique();
        voucher.HasIndex(x => new { x.CompanyId, x.TargetStartsOn });
        voucher.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        voucher.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var reversal = model.Entity<FinancePeriodCarryReversalRow>();
        reversal.ToTable("PeriodCarryReversals", Schema, table =>
        {
            table.HasCheckConstraint("CK_PeriodCarryReversals_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [RecordedBy] <> '00000000-0000-0000-0000-000000000000' AND LEN([Reason]) > 0 AND LEN([SourceHash]) = 64");
        });
        reversal.HasKey(x => new { x.CompanyId, x.Id });
        reversal.Property(x => x.Id).ValueGeneratedNever();
        reversal.Property(x => x.Reason).HasMaxLength(1000);
        reversal.Property(x => x.SourceHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        reversal.HasIndex(x => new { x.CompanyId, x.VoucherId }).IsUnique();
        reversal.HasIndex(x => new { x.CompanyId, x.ClosureId }).IsUnique();
        reversal.HasOne<FinancePeriodCarryVoucherRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.VoucherId }).OnDelete(DeleteBehavior.Restrict);
        reversal.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }
}
