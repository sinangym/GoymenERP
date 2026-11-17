using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal sealed class PurchaseInvoiceVariancePolicyRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid PolicyId { get; set; }
    public decimal AmountTolerance { get; set; }
    public decimal RateTolerance { get; set; }
    public string PublishedBy { get; set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed partial class SettingsDbContext
{
    internal const string PurchaseInvoiceVarianceIdentityCheck =
        "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0";
    internal const string PurchaseInvoiceVarianceValuesCheck = "[AmountTolerance] >= 0 AND [RateTolerance] >= 0";
    internal const string PurchaseInvoiceVariancePublicationCheck =
        "LEN(LTRIM(RTRIM([PublishedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND DATEPART(TZOFFSET,[PublishedAtUtc]) = 0";
    internal DbSet<PurchaseInvoiceVariancePolicyRow> PurchaseInvoiceVariancePolicies => Set<PurchaseInvoiceVariancePolicyRow>();

    private void ConfigurePurchaseInvoiceVariancePolicies(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<PurchaseInvoiceVariancePolicyRow>();
        row.ToTable("PurchaseInvoiceVariancePolicies", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseInvoiceVariancePolicies_Identity", PurchaseInvoiceVarianceIdentityCheck);
            table.HasCheckConstraint("CK_PurchaseInvoiceVariancePolicies_Values", PurchaseInvoiceVarianceValuesCheck);
            table.HasCheckConstraint("CK_PurchaseInvoiceVariancePolicies_Publication", PurchaseInvoiceVariancePublicationCheck);
        });
        row.HasKey(x => new { x.CompanyId, x.Version });
        row.HasIndex(x => new { x.CompanyId, x.PolicyId }).IsUnique();
        row.Property(x => x.AmountTolerance).HasPrecision(24, 6);
        row.Property(x => x.RateTolerance).HasPrecision(24, 6);
        row.Property(x => x.PublishedBy).HasMaxLength(128);
        row.Property(x => x.Reason).HasMaxLength(4000);
        row.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
