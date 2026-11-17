using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

internal sealed class PurchaseCostAttributionSourceRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid SourceEventId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid ProductId { get; set; }
    public string SourceHash { get; set; } = "";
    public string LineageHash { get; set; } = "";
    public string LineageJson { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal sealed class PurchaseCostAttributionCandidateRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid SourceEventId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public Guid AcceptanceId { get; set; }
    public Guid ReceiptMovementId { get; set; }
    public Guid RootLabelId { get; set; }
    public Guid MovementId { get; set; }
    public int MovementLineNumber { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid ProductId { get; set; }
    public string SourceHash { get; set; } = "";
    public string ViewJson { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal sealed class PurchaseCostAttributionApprovalRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid SourceEventId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public Guid ProposalId { get; set; }
    public long ProposalVersion { get; set; }
    public Guid? JournalId { get; set; }
    public int Status { get; set; }
    public string SourceHash { get; set; } = "";
    public string ViewJson { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed partial class AccountingDbContext
{
    internal DbSet<PurchaseCostAttributionSourceRow> PurchaseCostAttributionSources => Set<PurchaseCostAttributionSourceRow>();
    internal DbSet<PurchaseCostAttributionCandidateRow> PurchaseCostAttributionCandidates => Set<PurchaseCostAttributionCandidateRow>();
    internal DbSet<PurchaseCostAttributionApprovalRow> PurchaseCostAttributionApprovals => Set<PurchaseCostAttributionApprovalRow>();

    private void ConfigurePurchaseCostAttributions(ModelBuilder model)
    {
        const string empty = "'00000000-0000-0000-0000-000000000000'";
        var source = model.Entity<PurchaseCostAttributionSourceRow>();
        source.ToTable("PurchaseCostAttributionSources", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseCostAttributionSources_Identity", $"[CompanyId] <> {empty} AND [SourceEventId] <> {empty} AND [InvoiceLineId] <> {empty} AND [InvoiceId] <> {empty} AND [ProductId] <> {empty}");
            table.HasCheckConstraint("CK_PurchaseCostAttributionSources_Proof", "LEN([SourceHash]) = 64 AND LEN([LineageHash]) = 64 AND ISJSON([LineageJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
        });
        source.HasKey(x => new { x.CompanyId, x.SourceEventId, x.InvoiceLineId });
        source.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId });
        source.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        source.Property(x => x.LineageHash).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        source.Property(x => x.LineageJson).HasColumnType("nvarchar(max)");
        source.HasOne<AccountingInventoryCostSourceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, EventId = x.SourceEventId })
            .HasPrincipalKey(x => new { x.CompanyId, x.EventId }).OnDelete(DeleteBehavior.Restrict);
        source.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var candidate = model.Entity<PurchaseCostAttributionCandidateRow>();
        candidate.ToTable("PurchaseCostAttributionCandidates", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseCostAttributionCandidates_Identity", $"[CompanyId] <> {empty} AND [SourceEventId] <> {empty} AND [InvoiceLineId] <> {empty} AND [AcceptanceId] <> {empty} AND [ReceiptMovementId] <> {empty} AND [RootLabelId] <> {empty} AND [MovementId] <> {empty} AND [MovementLineNumber] > 0 AND [ShipmentId] <> {empty} AND [ProductId] <> {empty}");
            table.HasCheckConstraint("CK_PurchaseCostAttributionCandidates_Proof", "LEN([SourceHash]) = 64 AND ISJSON([ViewJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
        });
        candidate.HasKey(x => new { x.CompanyId, x.SourceEventId, x.InvoiceLineId, x.AcceptanceId, x.MovementId, x.MovementLineNumber });
        candidate.HasIndex(x => new { x.CompanyId, x.MovementId, x.MovementLineNumber });
        candidate.HasIndex(x => new { x.CompanyId, x.ShipmentId });
        candidate.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        candidate.Property(x => x.ViewJson).HasColumnType("nvarchar(max)");
        candidate.HasOne<PurchaseCostAttributionSourceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SourceEventId, x.InvoiceLineId }).OnDelete(DeleteBehavior.Restrict);
        candidate.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var approval = model.Entity<PurchaseCostAttributionApprovalRow>();
        approval.ToTable("PurchaseCostAttributionApprovals", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseCostAttributionApprovals_Identity", $"[CompanyId] <> {empty} AND [SourceEventId] <> {empty} AND [InvoiceLineId] <> {empty} AND [ProposalId] <> {empty} AND [ProposalVersion] > 2");
            table.HasCheckConstraint("CK_PurchaseCostAttributionApprovals_State", "([Status] = 3 AND [JournalId] IS NOT NULL) OR ([Status] = 6 AND [JournalId] IS NULL)");
            table.HasCheckConstraint("CK_PurchaseCostAttributionApprovals_Proof", "LEN([SourceHash]) = 64 AND ISJSON([ViewJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
        });
        approval.HasKey(x => new { x.CompanyId, x.SourceEventId, x.InvoiceLineId });
        approval.HasIndex(x => new { x.CompanyId, x.ProposalId, x.ProposalVersion }).IsUnique();
        approval.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        approval.Property(x => x.ViewJson).HasColumnType("nvarchar(max)");
        approval.HasOne<PurchaseCostAttributionSourceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SourceEventId, x.InvoiceLineId }).OnDelete(DeleteBehavior.Restrict);
        approval.HasOne<AccountingInventoryCostProposalRow>().WithMany().HasForeignKey(x => new { x.CompanyId, Id = x.ProposalId, Version = x.ProposalVersion })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id, x.Version }).OnDelete(DeleteBehavior.Restrict);
        approval.HasOne<JournalRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.JournalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        approval.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }
}
