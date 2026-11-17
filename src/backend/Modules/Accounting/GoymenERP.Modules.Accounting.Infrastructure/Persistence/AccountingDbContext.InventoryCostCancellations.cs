using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

internal sealed class InventoryCostCancellationVersionRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InventoryCostProposalId { get; set; }
    public long Version { get; set; }
    public long InitialProposalVersion { get; set; }
    public Guid Id { get; set; }
    public Guid CancellationId { get; set; }
    public int Status { get; set; }
    public Guid PeriodId { get; set; }
    public Guid PreparedBy { get; set; }
    public Guid? ApprovedBy { get; set; }
    public string ViewJson { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>The exact economic component is answered once, independently of other shares in the aggregate journal.</summary>
internal sealed class InventoryCostCancellationClaimRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public long FinalizedVersion { get; set; }
    public string EconomicKey { get; set; } = "";
    public Guid InventoryCostProposalId { get; set; }
    public long CancellationVersion { get; set; }
    public Guid? SourceJournalId { get; set; }
    public Guid? ReversingJournalId { get; set; }
    public string ViewJson { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed partial class AccountingDbContext
{
    internal const string InventoryCostCancellationVersionsIdentityCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InventoryCostProposalId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [CancellationId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND [InitialProposalVersion] >= 1 AND [PreparedBy] <> '00000000-0000-0000-0000-000000000000'";
    internal const string InventoryCostCancellationVersionsStateCheck = "([Status] = 1 AND [ApprovedBy] IS NULL) OR ([Status] = 2 AND [Version] >= 2 AND [ApprovedBy] IS NOT NULL AND [ApprovedBy] <> [PreparedBy] AND [ApprovedBy] <> '00000000-0000-0000-0000-000000000000')";
    internal const string InventoryCostCancellationVersionsProofCheck = "ISJSON([ViewJson]) = 1 AND LEN([Sha256]) = 64 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0";
    internal const string InventoryCostCancellationClaimsIdentityCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000' AND [FinalizedVersion] >= 1 AND [CancellationVersion] >= 2 AND LEN([EconomicKey]) = 64";
    internal const string InventoryCostCancellationClaimsProofCheck = "ISJSON([ViewJson]) = 1 AND LEN([Sha256]) = 64 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0 AND ([ReversingJournalId] IS NULL OR [SourceJournalId] IS NOT NULL)";
    internal DbSet<InventoryCostCancellationVersionRow> InventoryCostCancellationVersions => Set<InventoryCostCancellationVersionRow>();
    internal DbSet<InventoryCostCancellationClaimRow> InventoryCostCancellationClaims => Set<InventoryCostCancellationClaimRow>();

    private void ConfigureInventoryCostCancellations(ModelBuilder model)
    {
        var versions = model.Entity<InventoryCostCancellationVersionRow>();
        versions.ToTable("InventoryCostCancellationVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_InventoryCostCancellationVersions_Identity", InventoryCostCancellationVersionsIdentityCheck);
            table.HasCheckConstraint("CK_InventoryCostCancellationVersions_State", InventoryCostCancellationVersionsStateCheck);
            table.HasCheckConstraint("CK_InventoryCostCancellationVersions_Proof", InventoryCostCancellationVersionsProofCheck);
        });
        versions.HasKey(x => new { x.CompanyId, x.InventoryCostProposalId, x.Version });
        versions.HasIndex(x => new { x.CompanyId, x.Id, x.Version }).IsUnique();
        versions.Property(x => x.ViewJson).HasColumnType("nvarchar(max)");
        versions.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        versions.HasOne<AccountingInventoryCostProposalRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, Id = x.InventoryCostProposalId, Version = x.InitialProposalVersion }).OnDelete(DeleteBehavior.Restrict);
        versions.HasOne<PeriodRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        versions.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var claims = model.Entity<InventoryCostCancellationClaimRow>();
        claims.ToTable("InventoryCostCancellationClaims", Schema, table =>
        {
            table.HasCheckConstraint("CK_InventoryCostCancellationClaims_Identity", InventoryCostCancellationClaimsIdentityCheck);
            table.HasCheckConstraint("CK_InventoryCostCancellationClaims_Proof", InventoryCostCancellationClaimsProofCheck);
        });
        claims.HasKey(x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.FinalizedVersion, x.EconomicKey });
        claims.Property(x => x.EconomicKey).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        claims.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        claims.Property(x => x.ViewJson).HasColumnType("nvarchar(max)");
        claims.HasOne<InventoryCostCancellationVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.InventoryCostProposalId, Version = x.CancellationVersion }).OnDelete(DeleteBehavior.Restrict);
        claims.HasOne<JournalRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SourceJournalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        claims.HasOne<JournalRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ReversingJournalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        claims.HasIndex(x => new { x.CompanyId, x.ReversingJournalId }).IsUnique().HasFilter("[ReversingJournalId] IS NOT NULL");
        claims.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }
}
