using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryDbContext
{
    internal const string PurchaseInvoiceCostApplicationSourceCheck = "[SourceEventId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [AdmissionId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [InvoiceVersion] >= 1 AND LEN([SourceSha256]) = 64 AND ISJSON([SourceJson], OBJECT) = 1 AND ISJSON([ResultJson], OBJECT) = 1";
    internal const string PurchaseInvoiceCostApplicationEnvelopeCheck = "DATEPART(TZOFFSET,[OccurredAtUtc]) = 0 "
        + "AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0 AND LEN([CorrelationId]) > 0";
    internal const string PurchaseInvoiceReceiptCostEffectSourceCheck = "[AcceptanceId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000' AND [MovementLineNumber] >= 1 AND ISJSON([ResultJson], OBJECT) = 1";
    internal static string PurchaseInvoiceReceiptCostEffectQuantityCheck => CanonicalCostDecimal("MatchedQuantity")
        + " AND " + CanonicalCostDecimal("AcceptedQuantity") + " AND TRY_CONVERT(decimal(38,18),[MatchedQuantity]) > 0 "
        + "AND TRY_CONVERT(decimal(38,18),[AcceptedQuantity]) >= TRY_CONVERT(decimal(38,18),[MatchedQuantity])";
    internal DbSet<PurchaseInvoiceCostApplicationRow> PurchaseInvoiceCostApplications => Set<PurchaseInvoiceCostApplicationRow>();
    internal DbSet<PurchaseInvoiceReceiptCostEffectRow> PurchaseInvoiceReceiptCostEffects => Set<PurchaseInvoiceReceiptCostEffectRow>();

    private void ConfigurePurchaseInvoiceCosts(ModelBuilder modelBuilder)
    {
        var source = modelBuilder.Entity<PurchaseInvoiceCostApplicationRow>();
        source.ToTable("PurchaseInvoiceCostApplications", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseInvoiceCostApplications_Source", PurchaseInvoiceCostApplicationSourceCheck);
            table.HasCheckConstraint("CK_PurchaseInvoiceCostApplications_Envelope", PurchaseInvoiceCostApplicationEnvelopeCheck);
        });
        source.HasKey(row => new { row.CompanyId, row.SourceEventId });
        source.HasIndex(row => new { row.CompanyId, row.InvoiceId }).IsUnique();
        source.HasIndex(row => new { row.CompanyId, row.AdmissionId }).IsUnique();
        source.Property(row => row.SourceSha256).HasMaxLength(64).IsUnicode(false).IsRequired();
        source.Property(row => row.SourceJson).HasColumnType("nvarchar(max)").IsRequired();
        source.Property(row => row.ResultJson).HasColumnType("nvarchar(max)").IsRequired();
        source.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        source.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
        source.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);

        var effect = modelBuilder.Entity<PurchaseInvoiceReceiptCostEffectRow>();
        effect.ToTable("PurchaseInvoiceReceiptCostEffects", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseInvoiceReceiptCostEffects_Source", PurchaseInvoiceReceiptCostEffectSourceCheck);
            table.HasCheckConstraint("CK_PurchaseInvoiceReceiptCostEffects_Quantity", PurchaseInvoiceReceiptCostEffectQuantityCheck);
        });
        effect.HasKey(row => new { row.CompanyId, row.SourceEventId, row.AcceptanceId });
        effect.HasIndex(row => new { row.CompanyId, row.AcceptanceId, row.SourceEventId }).IsUnique();
        var exactDecimal = new ValueConverter<decimal, string>(value => value.ToString("0.############################", CultureInfo.InvariantCulture),
            value => decimal.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
        effect.Property(row => row.MatchedQuantity).HasConversion(exactDecimal).HasColumnType("varchar(64)");
        effect.Property(row => row.AcceptedQuantity).HasConversion(exactDecimal).HasColumnType("varchar(64)");
        effect.Property(row => row.ResultJson).HasColumnType("nvarchar(max)").IsRequired();
        effect.HasOne<PurchaseInvoiceCostApplicationRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.SourceEventId }).OnDelete(DeleteBehavior.Restrict);
        effect.HasOne<MovementLineRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.MovementId, LineNumber = row.MovementLineNumber }).OnDelete(DeleteBehavior.Restrict);
        effect.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
    }
}
