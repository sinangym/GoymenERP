using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

internal sealed class InvoiceCogsEconomicClaimSuccessionRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid MovementId { get; set; }
    public int MovementLineNumber { get; set; }
    public long Generation { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid SalesEventId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public Guid AdmissionId { get; set; }
    public long FinalizedVersion { get; set; }
    public string PreviousClaimSha256 { get; set; } = "";
    public Guid CancellationId { get; set; }
    public string SourceHash { get; set; } = "";
    public string ViewJson { get; set; } = "";
    public string SuccessionJson { get; set; } = "";
    public string SuccessionSha256 { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed partial class AccountingDbContext
{
    internal DbSet<InvoiceCogsEconomicClaimSuccessionRow> InvoiceCogsEconomicClaimSuccessions => Set<InvoiceCogsEconomicClaimSuccessionRow>();

    private void ConfigureCostClaimSuccessions(ModelBuilder model)
    {
        var row = model.Entity<InvoiceCogsEconomicClaimSuccessionRow>();
        row.ToTable("InvoiceCogsEconomicClaimSuccessions", Schema, table =>
        {
            table.HasCheckConstraint("CK_InvoiceCogsEconomicClaimSuccessions_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [MovementId] <> '00000000-0000-0000-0000-000000000000' AND [MovementLineNumber] > 0 AND [Generation] >= 2 AND [ShipmentId] <> '00000000-0000-0000-0000-000000000000' AND [SalesEventId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000' AND [AdmissionId] <> '00000000-0000-0000-0000-000000000000' AND [FinalizedVersion] > 0 AND [CancellationId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_InvoiceCogsEconomicClaimSuccessions_Proof",
                "LEN([PreviousClaimSha256]) = 64 AND LEN([SourceHash]) = 64 AND LEN([SuccessionSha256]) = 64 AND ISJSON([ViewJson]) = 1 AND ISJSON([SuccessionJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
        });
        row.HasKey(x => new { x.CompanyId, x.MovementId, x.MovementLineNumber, x.Generation });
        row.HasIndex(x => new { x.CompanyId, x.SalesEventId, x.MovementId, x.MovementLineNumber }).IsUnique();
        row.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.FinalizedVersion });
        row.HasOne<InvoiceCogsEconomicClaimRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MovementId, x.MovementLineNumber }).OnDelete(DeleteBehavior.Restrict);
        row.HasOne<AccountingInventoryCostSourceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SalesEventId })
            .HasPrincipalKey(x => new { x.CompanyId, x.EventId }).OnDelete(DeleteBehavior.Restrict);
        foreach (var name in new[] { nameof(InvoiceCogsEconomicClaimSuccessionRow.PreviousClaimSha256),
            nameof(InvoiceCogsEconomicClaimSuccessionRow.SourceHash), nameof(InvoiceCogsEconomicClaimSuccessionRow.SuccessionSha256) })
            row.Property<string>(name).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        row.Property(x => x.ViewJson).HasColumnType("nvarchar(max)");
        row.Property(x => x.SuccessionJson).HasColumnType("nvarchar(max)");
        row.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }
}
