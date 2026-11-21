using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

internal sealed class InvoiceLineCogsProposalRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public long ExpectedLineVersion { get; set; }
    public Guid PreparedBy { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public string SourceJson { get; set; } = "";
    public string ViewJson { get; set; } = "";
}

internal sealed class InvoiceLineCogsRevisionRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public long Version { get; set; }
    public Guid ProposalId { get; set; }
    public Guid PreparedBy { get; set; }
    public Guid ApprovedBy { get; set; }
    public DateTimeOffset ApprovedAtUtc { get; set; }
    public string ViewJson { get; set; } = "";
    public string RevisionJson { get; set; } = "";
}

public sealed partial class AccountingDbContext
{
    internal const string InvoiceLineCogsProposalIdentityCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000' AND [PreparedBy] <> '00000000-0000-0000-0000-000000000000'";
    internal const string InvoiceLineCogsProposalVersionCheck = "[ExpectedLineVersion] >= 0";
    internal const string InvoiceLineCogsProposalJsonCheck = "ISJSON([SourceJson]) = 1 AND ISJSON([ViewJson]) = 1";
    internal const string InvoiceLineCogsProposalUtcCheck = "DATEPART(TZOFFSET, [PreparedAtUtc]) = 0";
    internal const string InvoiceLineCogsRevisionIdentityCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000'";
    internal const string InvoiceLineCogsRevisionVersionCheck = "[Version] >= 1";
    internal const string InvoiceLineCogsRevisionMakerCheckerCheck = "[PreparedBy] <> '00000000-0000-0000-0000-000000000000' AND [ApprovedBy] <> '00000000-0000-0000-0000-000000000000' AND [PreparedBy] <> [ApprovedBy]";
    internal const string InvoiceLineCogsRevisionJsonCheck = "ISJSON([ViewJson]) = 1 AND ISJSON([RevisionJson]) = 1";
    internal const string InvoiceLineCogsRevisionUtcCheck = "DATEPART(TZOFFSET, [ApprovedAtUtc]) = 0";
    internal DbSet<InvoiceLineCogsProposalRow> InvoiceLineCogsProposals => Set<InvoiceLineCogsProposalRow>();
    internal DbSet<InvoiceLineCogsRevisionRow> InvoiceLineCogsRevisions => Set<InvoiceLineCogsRevisionRow>();

    private void ConfigureInvoiceLineCogs(ModelBuilder modelBuilder)
    {
        var proposal = modelBuilder.Entity<InvoiceLineCogsProposalRow>();
        proposal.ToTable("InvoiceLineCogsProposals", Schema, table =>
        {
            table.HasCheckConstraint("CK_InvoiceLineCogsProposals_Identity", InvoiceLineCogsProposalIdentityCheck);
            table.HasCheckConstraint("CK_InvoiceLineCogsProposals_Version", InvoiceLineCogsProposalVersionCheck);
            table.HasCheckConstraint("CK_InvoiceLineCogsProposals_Json", InvoiceLineCogsProposalJsonCheck);
            table.HasCheckConstraint("CK_InvoiceLineCogsProposals_Utc", InvoiceLineCogsProposalUtcCheck);
        });
        proposal.HasKey(x => new { x.CompanyId, x.Id });
        proposal.HasAlternateKey(x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.Id });
        proposal.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.ExpectedLineVersion });
        proposal.Property(x => x.SourceJson).HasColumnType("nvarchar(max)").IsRequired();
        proposal.Property(x => x.ViewJson).HasColumnType("nvarchar(max)").IsRequired();
        proposal.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        proposal.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var revision = modelBuilder.Entity<InvoiceLineCogsRevisionRow>();
        revision.ToTable("InvoiceLineCogsRevisions", Schema, table =>
        {
            table.HasCheckConstraint("CK_InvoiceLineCogsRevisions_Identity", InvoiceLineCogsRevisionIdentityCheck);
            table.HasCheckConstraint("CK_InvoiceLineCogsRevisions_Version", InvoiceLineCogsRevisionVersionCheck);
            table.HasCheckConstraint("CK_InvoiceLineCogsRevisions_MakerChecker", InvoiceLineCogsRevisionMakerCheckerCheck);
            table.HasCheckConstraint("CK_InvoiceLineCogsRevisions_Json", InvoiceLineCogsRevisionJsonCheck);
            table.HasCheckConstraint("CK_InvoiceLineCogsRevisions_Utc", InvoiceLineCogsRevisionUtcCheck);
        });
        revision.HasKey(x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.Version });
        revision.HasIndex(x => new { x.CompanyId, x.ProposalId }).IsUnique();
        revision.HasOne<InvoiceLineCogsProposalRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.ProposalId })
            .HasPrincipalKey(x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        revision.Property(x => x.ViewJson).HasColumnType("nvarchar(max)").IsRequired();
        revision.Property(x => x.RevisionJson).HasColumnType("nvarchar(max)").IsRequired();
        revision.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        // No mutable-field admission: both tables remain append-only under the common Accounting write guard.
    }
}
