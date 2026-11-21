using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// F-004 (BS-9/BS-13/BS-16/BS-17; URN-D7…D9): the persistent unit conversion policy versions of a company. The table carries
/// the company inside its key and the company query filter; the rows are insert-only and the primary key on the version
/// number is the concurrency fence (two writers of the same next version: one wins, the other is a version conflict). The
/// checks hold the published closed code lists and the technical bounds; no value is defaulted by the database.
/// </summary>
public sealed partial class SettingsDbContext
{
    internal const string UnitConversionRoundingCheckValues =
        "('ToEven','AwayFromZero','ToZero','ToNegativeInfinity','ToPositiveInfinity')";

    internal const string UnitConversionRoundingCheck =
        "[CoefficientRounding] IN " + UnitConversionRoundingCheckValues
        + " AND [DocumentTotalRounding] IN " + UnitConversionRoundingCheckValues
        + " AND [WholeUnitRounding] IN " + UnitConversionRoundingCheckValues;

    internal const string UnitConversionDecimalsCheck =
        "[CoefficientDecimals] BETWEEN 0 AND 12 AND [DocumentTotalDecimals] BETWEEN 0 AND 12";

    internal const string UnitConversionToleranceCheck =
        "[WeighbridgeTolerancePercent] >= 0 AND [WeighbridgeTolerancePercent] <= 100 "
        + "AND [ToleranceBoundary] IN ('Inclusive','Exclusive') AND [ToleranceBase] IN ('TheoreticalKg','WeighedKg')";

    internal DbSet<UnitConversionPolicyVersionRow> UnitConversionPolicyVersions => Set<UnitConversionPolicyVersionRow>();

    private void ConfigureUnitConversionPolicies(ModelBuilder modelBuilder)
    {
        const string emptyGuid = "'00000000-0000-0000-0000-000000000000'";

        var policy = modelBuilder.Entity<UnitConversionPolicyVersionRow>();
        policy.ToTable("UnitConversionPolicyVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsUnitConversionPolicyVersions_Identity",
                $"[CompanyId] <> {emptyGuid} AND [ProfileId] <> {emptyGuid} AND [Version] > 0");
            table.HasCheckConstraint("CK_SettingsUnitConversionPolicyVersions_Density", "[DensityGramsPerCubicCentimetre] > 0");
            table.HasCheckConstraint("CK_SettingsUnitConversionPolicyVersions_Decimals", UnitConversionDecimalsCheck);
            table.HasCheckConstraint("CK_SettingsUnitConversionPolicyVersions_Rounding", UnitConversionRoundingCheck);
            table.HasCheckConstraint("CK_SettingsUnitConversionPolicyVersions_Tolerance", UnitConversionToleranceCheck);
            table.HasCheckConstraint("CK_SettingsUnitConversionPolicyVersions_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        policy.HasKey(x => new { x.CompanyId, x.Version });
        policy.HasIndex(x => new { x.CompanyId, x.ProfileId }).IsUnique();
        policy.Property(x => x.DensityGramsPerCubicCentimetre).HasPrecision(12, 6);
        policy.Property(x => x.WeighbridgeTolerancePercent).HasPrecision(7, 4);
        policy.Property(x => x.CoefficientRounding).HasMaxLength(32).IsUnicode(false);
        policy.Property(x => x.DocumentTotalRounding).HasMaxLength(32).IsUnicode(false);
        policy.Property(x => x.WholeUnitRounding).HasMaxLength(32).IsUnicode(false);
        policy.Property(x => x.ToleranceBoundary).HasMaxLength(16).IsUnicode(false);
        policy.Property(x => x.ToleranceBase).HasMaxLength(16).IsUnicode(false);
        policy.Property(x => x.RecordedBy).HasMaxLength(128);
        policy.Property(x => x.Reason).HasMaxLength(4000);
        policy.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
