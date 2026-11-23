using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// F-029 S1 (URUN_F029 spec §5.4(4); URN-D2…D5): the persistent unit profile versions of a company and the company's one-time
/// baseline transfer marker. Both tables carry the company inside their key and the company query filter; the version rows are
/// insert-only and the primary key on (company, unit, version) is the concurrency fence (two writers of the same next version
/// of one unit: one wins, the other is a version conflict). The checks hold the technical scale bounds, Ö2's kilogram bound
/// and the published source kinds; no value is defaulted by the database and nothing is seeded.
/// </summary>
public sealed partial class SettingsDbContext
{
    internal const string UnitProfileScaleCheck = "[Scale] BETWEEN 0 AND 28 AND ([IsKilogram] = 0 OR [Scale] <= 3)";

    internal const string UnitProfileCodesCheck =
        "LEN(LTRIM(RTRIM([UnitCode]))) > 0 AND LEN(LTRIM(RTRIM([DimensionCode]))) > 0";

    internal DbSet<UnitProfileVersionRow> UnitProfileVersions => Set<UnitProfileVersionRow>();

    internal DbSet<UnitProfileBaselineTransferRow> UnitProfileBaselineTransfers => Set<UnitProfileBaselineTransferRow>();

    private void ConfigureUnitProfiles(ModelBuilder modelBuilder)
    {
        const string emptyGuid = "'00000000-0000-0000-0000-000000000000'";

        var transfer = modelBuilder.Entity<UnitProfileBaselineTransferRow>();
        transfer.ToTable("UnitProfileBaselineTransfers", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsUnitProfileBaselineTransfers_Company", $"[CompanyId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsUnitProfileBaselineTransfers_EntryCount", "[EntryCount] >= 0");
            table.HasCheckConstraint("CK_SettingsUnitProfileBaselineTransfers_Recorded",
                "LEN(LTRIM(RTRIM([TransferredBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        transfer.HasKey(x => x.CompanyId);
        transfer.Property(x => x.CompanyId).ValueGeneratedNever();
        transfer.Property(x => x.TransferredBy).HasMaxLength(128);
        transfer.Property(x => x.Reason).HasMaxLength(4000);
        transfer.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var profile = modelBuilder.Entity<UnitProfileVersionRow>();
        profile.ToTable("UnitProfileVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsUnitProfileVersions_Identity",
                $"[CompanyId] <> {emptyGuid} AND [UnitId] <> {emptyGuid} AND [ProfileId] <> {emptyGuid} AND [Version] > 0");
            table.HasCheckConstraint("CK_SettingsUnitProfileVersions_Codes", UnitProfileCodesCheck);
            table.HasCheckConstraint("CK_SettingsUnitProfileVersions_Scale", UnitProfileScaleCheck);
            table.HasCheckConstraint("CK_SettingsUnitProfileVersions_Source", SalesPolicySourceKindCheck);
            table.HasCheckConstraint("CK_SettingsUnitProfileVersions_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        profile.HasKey(x => new { x.CompanyId, x.UnitId, x.Version });
        profile.HasIndex(x => new { x.CompanyId, x.ProfileId }).IsUnique();
        profile.Property(x => x.UnitCode).HasMaxLength(32);
        profile.Property(x => x.DimensionCode).HasMaxLength(64);
        profile.Property(x => x.SourceKind).HasMaxLength(32).IsUnicode(false);
        profile.Property(x => x.DecisionReference).HasMaxLength(400);
        profile.Property(x => x.RecordedBy).HasMaxLength(128);
        profile.Property(x => x.Reason).HasMaxLength(4000);
        profile.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
