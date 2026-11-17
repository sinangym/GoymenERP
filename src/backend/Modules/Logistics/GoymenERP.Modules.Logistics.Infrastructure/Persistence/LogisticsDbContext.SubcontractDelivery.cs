using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed partial class LogisticsDbContext
{
    internal DbSet<VehicleWeighingCorrectionRow> WeighingCorrections => Set<VehicleWeighingCorrectionRow>();

    internal const string WeighingCorrectionWeightsCheck = "[Revision] >= 2 AND [SharesBefore] >= 1 "
        + "AND [FirstWeighingKg] > 0 AND [SecondWeighingKg] > 0 AND [NetKg] > 0 AND [PreviousNetKg] > 0 AND [LoadedKgAtWeighing] >= 0";
    internal const string WeighingCorrectionNetCheck =
        "([Direction] = 1 AND [SecondWeighingKg] > [FirstWeighingKg] AND [NetKg] = [SecondWeighingKg] - [FirstWeighingKg]) "
        + "OR ([Direction] = 2 AND [FirstWeighingKg] > [SecondWeighingKg] AND [NetKg] = [FirstWeighingKg] - [SecondWeighingKg])";

    private void ConfigureWeighingCorrections(ModelBuilder modelBuilder)
    {
        var correction = modelBuilder.Entity<VehicleWeighingCorrectionRow>();
        correction.ToTable("VehicleWeighingCorrections", Schema, table =>
        {
            table.HasCheckConstraint("CK_VehicleWeighingCorrections_Identity", $"[CorrectionId] <> {EmptyGuid} AND LEN([CorrectedBy]) > 0 AND LEN([Reason]) > 0");
            table.HasCheckConstraint("CK_VehicleWeighingCorrections_Weights", WeighingCorrectionWeightsCheck);
            table.HasCheckConstraint("CK_VehicleWeighingCorrections_Net", WeighingCorrectionNetCheck);
            table.HasCheckConstraint("CK_VehicleWeighingCorrections_Difference", "[DifferenceKg] = [NetKg] - [LoadedKgAtWeighing]");
        });
        correction.HasKey(row => new { row.CompanyId, row.InstructionId, row.Revision });
        correction.HasIndex(row => new { row.CompanyId, row.CorrectionId }).IsUnique();
        foreach (var weight in new[] { nameof(VehicleWeighingCorrectionRow.FirstWeighingKg), nameof(VehicleWeighingCorrectionRow.SecondWeighingKg),
                     nameof(VehicleWeighingCorrectionRow.NetKg), nameof(VehicleWeighingCorrectionRow.PreviousNetKg),
                     nameof(VehicleWeighingCorrectionRow.LoadedKgAtWeighing), nameof(VehicleWeighingCorrectionRow.DifferenceKg) })
            correction.Property<decimal>(weight).HasPrecision(18, 3);
        correction.Property(row => row.CorrectedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        correction.Property(row => row.Reason).HasMaxLength(500).IsRequired();
        correction.HasOne<VehicleWeighingRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.InstructionId })
            .OnDelete(DeleteBehavior.Restrict);
        correction.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
    }
}

/// <summary>LOJ-D13: immutable correction facts; the original weighing and every earlier correction remain unchanged.</summary>
internal sealed class VehicleWeighingCorrectionRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InstructionId { get; set; }
    public Guid CorrectionId { get; set; }
    public int Revision { get; set; }
    public int Direction { get; set; }
    public decimal FirstWeighingKg { get; set; }
    public decimal SecondWeighingKg { get; set; }
    public decimal NetKg { get; set; }
    public decimal PreviousNetKg { get; set; }
    public decimal LoadedKgAtWeighing { get; set; }
    public decimal DifferenceKg { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int SharesBefore { get; set; }
    public string CorrectedBy { get; set; } = string.Empty;
    public DateTimeOffset CorrectedAtUtc { get; set; }
}
