using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>One actual output's allocation of an already posted aggregate journal; never a second journal.</summary>
internal sealed class ManufacturingCostAttributionRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid SourceEventId { get; set; }
    public Guid OutputId { get; set; }
    public Guid GenerationId { get; set; }
    public string GenerationVersion { get; set; } = "";
    public Guid ReceiptMovementId { get; set; }
    public Guid JournalId { get; set; }
    public Guid CurrencyId { get; set; }
    public Guid MoneyProfileId { get; set; }
    public long MoneyProfileVersion { get; set; }
    public int MoneyScale { get; set; }
    public string SourceHash { get; set; } = "";
    public string LineageHash { get; set; } = "";
    public string ViewJson { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>Separate roots and signed generation differences may legitimately share the same physical movement line.</summary>
internal sealed class ManufacturingCostAttributionShareRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid SourceEventId { get; set; }
    public Guid OutputId { get; set; }
    public Guid MovementId { get; set; }
    public int MovementLineNumber { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid ProductId { get; set; }
    public string SourceHash { get; set; } = "";
    public string ViewJson { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>
/// The one whole physical issue line's initial invoice owner. This is not uniqueness of root/output cost shares:
/// later posted generation differences attach through the same physical identity and retain their own source keys.
/// </summary>
internal sealed class InvoiceCogsEconomicClaimRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid MovementId { get; set; }
    public int MovementLineNumber { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid SalesEventId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public Guid AdmissionId { get; set; }
    public long FinalizedVersion { get; set; }
    public int Origin { get; set; }
    public string SourceHash { get; set; } = "";
    public string ViewJson { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed partial class AccountingDbContext
{
    internal DbSet<ManufacturingCostAttributionRow> ManufacturingCostAttributions => Set<ManufacturingCostAttributionRow>();
    internal DbSet<ManufacturingCostAttributionShareRow> ManufacturingCostAttributionShares => Set<ManufacturingCostAttributionShareRow>();
    internal DbSet<InvoiceCogsEconomicClaimRow> InvoiceCogsEconomicClaims => Set<InvoiceCogsEconomicClaimRow>();

    private void ConfigureCostAttributions(ModelBuilder model)
    {
        const string empty = "'00000000-0000-0000-0000-000000000000'";
        model.Entity<ManufacturingCostDifferenceRow>().HasIndex(x => new { x.CompanyId, x.ManufacturingOrderId, x.Status });
        var output = model.Entity<ManufacturingCostAttributionRow>();
        output.ToTable("ManufacturingCostAttributions", Schema, table =>
        {
            table.HasCheckConstraint("CK_ManufacturingCostAttributions_Identity",
                $"[CompanyId] <> {empty} AND [SourceEventId] <> {empty} AND [OutputId] <> {empty} AND [GenerationId] <> {empty} AND [ReceiptMovementId] <> {empty} AND [JournalId] <> {empty} AND LEN([GenerationVersion]) > 0");
            table.HasCheckConstraint("CK_ManufacturingCostAttributions_Profile",
                $"[CurrencyId] <> {empty} AND [MoneyProfileId] <> {empty} AND [MoneyProfileVersion] > 0 AND [MoneyScale] BETWEEN 0 AND 28");
            table.HasCheckConstraint("CK_ManufacturingCostAttributions_Proof",
                "LEN([SourceHash]) = 64 AND LEN([LineageHash]) = 64 AND ISJSON([ViewJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
        });
        output.HasKey(x => new { x.CompanyId, x.SourceEventId, x.OutputId });
        output.HasIndex(x => new { x.CompanyId, x.GenerationId, x.GenerationVersion, x.OutputId }).IsUnique();
        output.Property(x => x.GenerationVersion).HasMaxLength(20).UseCollation(IdentityCollation);
        output.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        output.Property(x => x.LineageHash).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        output.Property(x => x.ViewJson).HasColumnType("nvarchar(max)");
        output.HasOne<ManufacturingCostDifferenceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SourceEventId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        output.HasOne<JournalRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.JournalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        output.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var share = model.Entity<ManufacturingCostAttributionShareRow>();
        share.ToTable("ManufacturingCostAttributionShares", Schema, table =>
        {
            table.HasCheckConstraint("CK_ManufacturingCostAttributionShares_Identity",
                $"[CompanyId] <> {empty} AND [SourceEventId] <> {empty} AND [OutputId] <> {empty} AND [MovementId] <> {empty} AND [MovementLineNumber] > 0 AND [ShipmentId] <> {empty} AND [ProductId] <> {empty}");
            table.HasCheckConstraint("CK_ManufacturingCostAttributionShares_Proof",
                "LEN([SourceHash]) = 64 AND ISJSON([ViewJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
        });
        share.HasKey(x => new { x.CompanyId, x.SourceEventId, x.OutputId, x.MovementId, x.MovementLineNumber });
        share.HasIndex(x => new { x.CompanyId, x.MovementId, x.MovementLineNumber });
        share.HasIndex(x => new { x.CompanyId, x.ShipmentId });
        share.HasIndex(x => new { x.CompanyId, x.SourceEventId, x.OutputId, x.ShipmentId }).IsUnique();
        share.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        share.Property(x => x.ViewJson).HasColumnType("nvarchar(max)");
        share.HasOne<ManufacturingCostAttributionRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SourceEventId, x.OutputId })
            .OnDelete(DeleteBehavior.Restrict);
        share.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var claim = model.Entity<InvoiceCogsEconomicClaimRow>();
        claim.ToTable("InvoiceCogsEconomicClaims", Schema, table =>
        {
            table.HasCheckConstraint("CK_InvoiceCogsEconomicClaims_Identity",
                $"[CompanyId] <> {empty} AND [MovementId] <> {empty} AND [MovementLineNumber] > 0 AND [ShipmentId] <> {empty} AND [SalesEventId] <> {empty} AND [InvoiceId] <> {empty} AND [InvoiceLineId] <> {empty} AND [AdmissionId] <> {empty} AND [FinalizedVersion] > 0 AND [Origin] IN (1,2)");
            table.HasCheckConstraint("CK_InvoiceCogsEconomicClaims_Proof",
                "LEN([SourceHash]) = 64 AND ISJSON([ViewJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
        });
        claim.HasKey(x => new { x.CompanyId, x.MovementId, x.MovementLineNumber });
        claim.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.FinalizedVersion });
        claim.HasIndex(x => new { x.CompanyId, x.ShipmentId });
        claim.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        claim.Property(x => x.ViewJson).HasColumnType("nvarchar(max)");
        claim.HasOne<AccountingInventoryCostSourceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SalesEventId })
            .HasPrincipalKey(x => new { x.CompanyId, x.EventId }).OnDelete(DeleteBehavior.Restrict);
        claim.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        // No mutable-field exception: Accounting's tenant guard rejects updates and deletes to every row above.
    }
}
