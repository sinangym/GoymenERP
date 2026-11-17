using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed partial class SettingsDbContext
{
    internal DbSet<ManufacturingProfileBaselineTransferRow> ManufacturingProfileBaselineTransfers =>
        Set<ManufacturingProfileBaselineTransferRow>();

    private void ConfigureManufacturingProfileBaselines(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<ManufacturingProfileBaselineTransferRow>();
        row.ToTable("ManufacturingProfileBaselineTransfers", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsMfgBaseline_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [TransferId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_SettingsMfgBaseline_Kind", "[Kind] IN ('parameters','tolerances') AND [Version] = 1");
            table.HasCheckConstraint("CK_SettingsMfgBaseline_Snapshot", "ISJSON([SnapshotJson]) = 1 AND LEN([SnapshotHash]) = 64 AND [SnapshotHash] NOT LIKE '%[^0-9A-F]%'");
            table.HasCheckConstraint("CK_SettingsMfgBaseline_Actor", "LEN(LTRIM(RTRIM([ActorId]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        row.HasKey(value => new { value.CompanyId, value.Kind });
        row.HasIndex(value => new { value.CompanyId, value.TransferId }).IsUnique();
        row.Property(value => value.Kind).HasMaxLength(16).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        row.Property(value => value.Version).IsConcurrencyToken();
        row.Property(value => value.SnapshotHash).HasMaxLength(64).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        row.Property(value => value.ActorId).HasMaxLength(200);
        row.Property(value => value.Reason).HasMaxLength(500);
        row.HasQueryFilter(value => CompanyContext.IsResolved && value.CompanyId == CompanyContext.CompanyId);
    }
}
