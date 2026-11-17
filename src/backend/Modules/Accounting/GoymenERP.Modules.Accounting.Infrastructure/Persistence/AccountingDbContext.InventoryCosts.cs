using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

internal sealed class AccountingInventoryCostSourceRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid EventId { get; set; }
    public int Kind { get; set; }
    public Guid InvoiceId { get; set; }
    public long InvoiceVersion { get; set; }
    public Guid AdmissionId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string SourceJson { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
}

/// <summary>Every preparation and posting appends a complete version. Earlier choices and proof are never overwritten.</summary>
internal sealed class AccountingInventoryCostProposalRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public long Version { get; set; }
    public Guid EventId { get; set; }
    public string ComponentKey { get; set; } = "";
    public int Status { get; set; }
    public Guid? PreparedBy { get; set; }
    public Guid? ApprovedBy { get; set; }
    public Guid? PeriodId { get; set; }
    public Guid? JournalId { get; set; }
    public string ViewJson { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed partial class AccountingDbContext
{
    internal const string InventoryCostProposalStateCheck = "([Status] IN (1,4,5) AND [Version] = 1 AND [PreparedBy] IS NULL AND [ApprovedBy] IS NULL AND [PeriodId] IS NULL AND [JournalId] IS NULL) OR ([Status] = 2 AND [Version] > 1 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NULL AND [JournalId] IS NULL) OR ([Status] IN (3,6) AND [Version] > 2 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND (([Status] = 3 AND [JournalId] IS NOT NULL) OR ([Status] = 6 AND [JournalId] IS NULL)) AND [ApprovedBy] <> [PreparedBy])";
    internal DbSet<AccountingInventoryCostSourceRow> InventoryCostSources => Set<AccountingInventoryCostSourceRow>();
    internal DbSet<AccountingInventoryCostProposalRow> InventoryCostProposals => Set<AccountingInventoryCostProposalRow>();

    private void ConfigureInventoryCosts(ModelBuilder model)
    {
        var source = model.Entity<AccountingInventoryCostSourceRow>();
        source.ToTable("InventoryCostSources", Schema, table =>
        {
            table.HasCheckConstraint("CK_InventoryCostSources_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [AdmissionId] <> '00000000-0000-0000-0000-000000000000' AND [Kind] IN (1,2) AND [InvoiceVersion] >= 1");
            table.HasCheckConstraint("CK_InventoryCostSources_Proof", "ISJSON([SourceJson]) = 1 AND LEN([PayloadHash]) = 64 AND DATEPART(TZOFFSET,[OccurredAtUtc]) = 0 AND DATEPART(TZOFFSET,[ReceivedAtUtc]) = 0");
        });
        source.HasKey(x => new { x.CompanyId, x.EventId });
        source.HasIndex(x => new { x.CompanyId, x.Kind, x.InvoiceId, x.InvoiceVersion }).IsUnique();
        source.Property(x => x.PayloadHash).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        source.Property(x => x.SourceJson).HasColumnType("nvarchar(max)");
        source.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        source.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);

        var proposal = model.Entity<AccountingInventoryCostProposalRow>();
        proposal.ToTable("InventoryCostProposals", Schema, table =>
        {
            table.HasCheckConstraint("CK_InventoryCostProposals_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND LEN([ComponentKey]) > 0");
            table.HasCheckConstraint("CK_InventoryCostProposals_State", InventoryCostProposalStateCheck);
            table.HasCheckConstraint("CK_InventoryCostProposals_Proof", "ISJSON([ViewJson]) = 1 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
            table.HasCheckConstraint("CK_InventoryCostProposals_Actors", "([PreparedBy] IS NULL OR [PreparedBy] <> '00000000-0000-0000-0000-000000000000') AND ([ApprovedBy] IS NULL OR [ApprovedBy] <> '00000000-0000-0000-0000-000000000000') AND ([PeriodId] IS NULL OR [PeriodId] <> '00000000-0000-0000-0000-000000000000') AND ([JournalId] IS NULL OR [JournalId] <> '00000000-0000-0000-0000-000000000000')");
        });
        proposal.HasKey(x => new { x.CompanyId, x.Id, x.Version });
        proposal.HasIndex(x => new { x.CompanyId, x.EventId, x.ComponentKey, x.Version }).IsUnique();
        proposal.HasIndex(x => new { x.CompanyId, x.JournalId }).IsUnique().HasFilter("[JournalId] IS NOT NULL");
        proposal.Property(x => x.ComponentKey).HasMaxLength(128).UseCollation(IdentityCollation);
        proposal.Property(x => x.ViewJson).HasColumnType("nvarchar(max)");
        proposal.HasOne<AccountingInventoryCostSourceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.EventId }).OnDelete(DeleteBehavior.Restrict);
        proposal.HasOne<PeriodRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        proposal.HasOne<JournalRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.JournalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        proposal.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        // No mutable-field exception: the existing Accounting tenant/write guard rejects update and delete.
    }
}
