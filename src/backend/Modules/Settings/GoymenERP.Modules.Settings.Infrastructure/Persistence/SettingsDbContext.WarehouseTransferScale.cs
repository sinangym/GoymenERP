using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal sealed class WarehouseTransferScaleVersionRow
{
    public Guid CompanyId { get; set; }
    public Guid LocationId { get; set; }
    public long Version { get; set; }
    public Guid RecordId { get; set; }
    public bool RequireExitWeighing { get; set; }
    public long LocationVersion { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed partial class SettingsDbContext
{
    internal DbSet<WarehouseTransferScaleVersionRow> WarehouseTransferScaleVersions => Set<WarehouseTransferScaleVersionRow>();
    internal const string WarehouseTransferScaleIdentityCheck =
        "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [LocationId] <> '00000000-0000-0000-0000-000000000000' AND [RecordId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND [LocationVersion] > 0";
    internal const string WarehouseTransferScaleAuditCheck =
        "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0";

    private void ConfigureWarehouseTransferScale(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<WarehouseTransferScaleVersionRow>();
        row.ToTable("WarehouseTransferScaleVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_WarehouseTransferScaleVersions_Identity", WarehouseTransferScaleIdentityCheck);
            table.HasCheckConstraint("CK_WarehouseTransferScaleVersions_Audit", WarehouseTransferScaleAuditCheck);
        });
        row.HasKey(x => new { x.CompanyId, x.LocationId, x.Version });
        row.HasIndex(x => new { x.CompanyId, x.RecordId }).IsUnique();
        row.Property(x => x.RecordedBy).HasMaxLength(128);
        row.Property(x => x.Reason).HasMaxLength(4000);
        row.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
