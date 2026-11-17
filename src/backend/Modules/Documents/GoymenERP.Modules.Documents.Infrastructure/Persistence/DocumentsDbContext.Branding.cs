using GoymenERP.Modules.Documents.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence;

public sealed partial class DocumentsDbContext
{
    internal DbSet<CompanyBrandingRevisionRow> BrandingRevisions => Set<CompanyBrandingRevisionRow>();
    internal DbSet<DocumentTemplateDraftRow> TemplateDrafts => Set<DocumentTemplateDraftRow>();
    internal DbSet<DocumentTemplatePublicationRow> TemplatePublications => Set<DocumentTemplatePublicationRow>();
    internal DbSet<DocumentBrandingAttemptRow> BrandingAttempts => Set<DocumentBrandingAttemptRow>();

    private const string TemplateModuleCheck = "([Module] = 1 AND [TemplateCode] = 'sales.order-pdf') "
        + "OR ([Module] = 2 AND [TemplateCode] = 'inventory.material-label.technical') "
        + "OR ([Module] = 3 AND [TemplateCode] = 'logistics.dispatch-note')";

    private void ConfigureBranding(ModelBuilder model)
    {
        var documentVersion = model.Entity<DocumentVersionRow>();
        documentVersion.ToTable("DocumentVersions", Schema, table => table.HasCheckConstraint("CK_DocumentVersions_Publication",
            $"([TemplatePublicationId] IS NULL AND [TemplatePublicationJson] IS NULL) OR ([TemplatePublicationId] IS NOT NULL AND [TemplatePublicationId] <> {EmptyGuid} AND [TemplatePublicationJson] IS NOT NULL AND ISJSON([TemplatePublicationJson]) = 1 AND [ScanState] = 3)"));
        documentVersion.HasOne<DocumentTemplatePublicationRow>().WithMany()
            .HasForeignKey(row => new { row.CompanyId, Id = row.TemplatePublicationId })
            .HasPrincipalKey(row => new { row.CompanyId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        var branding = model.Entity<CompanyBrandingRevisionRow>();
        branding.ToTable("CompanyBrandingRevisions", Schema, table =>
        {
            table.HasCheckConstraint("CK_CompanyBrandingRevisions_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_CompanyBrandingRevisions_Logo", AssetReferenceCheck("Logo"));
            table.HasCheckConstraint("CK_CompanyBrandingRevisions_Letterhead", AssetReferenceCheck("Letterhead"));
            table.HasCheckConstraint("CK_CompanyBrandingRevisions_Text", "LEN(LTRIM(RTRIM([PreparedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        branding.HasKey(row => new { row.CompanyId, row.Version });
        branding.Property(row => row.PreparedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        branding.Property(row => row.Reason).HasMaxLength(DocumentBrandingContractV1.MaximumReasonLength).IsRequired();
        branding.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        branding.HasOne<DocumentVersionRow>().WithMany()
            .HasForeignKey(row => new { row.CompanyId, DocumentId = row.LogoDocumentId, VersionNo = row.LogoVersionNo })
            .HasPrincipalKey(row => new { row.CompanyId, row.DocumentId, row.VersionNo }).OnDelete(DeleteBehavior.Restrict);
        branding.HasOne<DocumentVersionRow>().WithMany()
            .HasForeignKey(row => new { row.CompanyId, DocumentId = row.LetterheadDocumentId, VersionNo = row.LetterheadVersionNo })
            .HasPrincipalKey(row => new { row.CompanyId, row.DocumentId, row.VersionNo }).OnDelete(DeleteBehavior.Restrict);
        branding.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var draft = model.Entity<DocumentTemplateDraftRow>();
        draft.ToTable("DocumentTemplateDrafts", Schema, table =>
        {
            table.HasCheckConstraint("CK_DocumentTemplateDrafts_Module", TemplateModuleCheck);
            table.HasCheckConstraint("CK_DocumentTemplateDrafts_Versions", "[Version] >= 1 AND [BrandingVersion] >= 1");
            table.HasCheckConstraint("CK_DocumentTemplateDrafts_Text", "LEN(LTRIM(RTRIM([PreparedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        draft.HasKey(row => new { row.CompanyId, row.Module, row.TemplateCode, row.Version });
        draft.Property(row => row.TemplateCode).HasMaxLength(64).UseCollation(IdentityCollation).IsRequired();
        draft.Property(row => row.PreparedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        draft.Property(row => row.Reason).HasMaxLength(DocumentBrandingContractV1.MaximumReasonLength).IsRequired();
        draft.HasAlternateKey(row => new { row.CompanyId, row.Module, row.TemplateCode, row.Version, row.BrandingVersion, row.PreparedBy });
        draft.HasOne<CompanyBrandingRevisionRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.BrandingVersion })
            .HasPrincipalKey(row => new { row.CompanyId, row.Version }).OnDelete(DeleteBehavior.Restrict);
        draft.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var publication = model.Entity<DocumentTemplatePublicationRow>();
        publication.ToTable("DocumentTemplatePublications", Schema, table =>
        {
            table.HasCheckConstraint("CK_DocumentTemplatePublications_Module", TemplateModuleCheck);
            table.HasCheckConstraint("CK_DocumentTemplatePublications_Identity", $"[Id] <> {EmptyGuid} AND [Version] >= 1 AND [DraftVersion] >= 1 AND [BrandingVersion] >= 1");
            table.HasCheckConstraint("CK_DocumentTemplatePublications_MakerChecker", "[PreparedBy] <> [PublishedBy] AND LEN(LTRIM(RTRIM([PublishedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        publication.HasKey(row => new { row.CompanyId, row.Module, row.TemplateCode, row.Version });
        publication.HasIndex(row => new { row.CompanyId, row.Id }).IsUnique();
        publication.HasIndex(row => new { row.CompanyId, row.Module, row.TemplateCode, row.DraftVersion }).IsUnique();
        publication.Property(row => row.TemplateCode).HasMaxLength(64).UseCollation(IdentityCollation).IsRequired();
        publication.Property(row => row.PreparedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        publication.Property(row => row.PublishedBy).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        publication.Property(row => row.Reason).HasMaxLength(DocumentBrandingContractV1.MaximumReasonLength).IsRequired();
        publication.HasOne<DocumentTemplateDraftRow>().WithMany()
            .HasForeignKey(row => new { row.CompanyId, row.Module, row.TemplateCode, Version = row.DraftVersion, row.BrandingVersion, row.PreparedBy })
            .HasPrincipalKey(row => new { row.CompanyId, row.Module, row.TemplateCode, row.Version, row.BrandingVersion, row.PreparedBy })
            .OnDelete(DeleteBehavior.Restrict);
        publication.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var attempt = model.Entity<DocumentBrandingAttemptRow>();
        attempt.ToTable("DocumentBrandingAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_DocumentBrandingAttempts_Command", "[CommandName] IN ('documents.company-branding.set.v1','documents.template.prepare.v1','documents.template.publish.v1')");
            table.HasCheckConstraint("CK_DocumentBrandingAttempts_Identity", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_DocumentBrandingAttempts_Result", "([Status] = 200 AND [Code] IS NULL AND [ResultJson] IS NOT NULL AND ISJSON([ResultJson]) = 1) OR ([Status] = 409 AND [Code] IS NOT NULL AND LEN([Code]) > 0 AND [ResultJson] IS NULL)");
        });
        attempt.HasKey(row => new { row.CompanyId, row.CommandName, row.IdempotencyKeyHash });
        attempt.Property(row => row.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(row => row.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(row => row.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        attempt.Property(row => row.ActorId).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.Property(row => row.Code).HasMaxLength(128);
        attempt.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
    }

    private static string AssetReferenceCheck(string prefix) =>
        $"([{prefix}DocumentId] IS NULL AND [{prefix}VersionNo] IS NULL) OR ([{prefix}DocumentId] IS NOT NULL AND [{prefix}DocumentId] <> {EmptyGuid} AND [{prefix}VersionNo] IS NOT NULL AND [{prefix}VersionNo] >= 1)";
}
