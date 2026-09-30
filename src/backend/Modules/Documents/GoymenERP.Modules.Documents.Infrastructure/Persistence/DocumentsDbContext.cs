using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Documents.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence;

/// <summary>
/// The vault's own tables in the <c>documents</c> schema (ERP-000G; DEC-DOC-001 Blob → Document → Version → Link;
/// ADR-002, ADR-005, ADR-009, ADR-011). Every row is a tenant row of its company: a global query filter on the
/// server-resolved company and a write guard that refuses any row of another company and ANY update or delete — the
/// whole schema is append-only (nothing is overwritten, DEC-DOC-003; nothing is deleted while retention is undecided,
/// OPEN-018/021). Hashes and link kinds are compared under a binary collation. The audit sidecar is Audit's table,
/// written in the same save and filtered the same way.
/// </summary>
public sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "documents";
    public const string IdentityCollation = "Latin1_General_100_BIN2";
    private const string EmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    internal DbSet<ContentBlobRow> Blobs => Set<ContentBlobRow>();

    internal DbSet<DocumentRow> Documents => Set<DocumentRow>();

    internal DbSet<DocumentVersionRow> Versions => Set<DocumentVersionRow>();

    internal DbSet<DocumentLinkRow> Links => Set<DocumentLinkRow>();

    internal DbSet<DocumentAttemptRow> Attempts => Set<DocumentAttemptRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var company = modelBuilder.Entity<OrganizationCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        ConfigureBlobs(modelBuilder);
        ConfigureDocuments(modelBuilder);
        ConfigureVersions(modelBuilder);
        ConfigureLinks(modelBuilder);
        ConfigureAttempts(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ConfigureBlobs(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ContentBlobRow>();
        entity.ToTable("ContentBlobs", Schema, table =>
        {
            table.HasCheckConstraint("CK_ContentBlobs_Identity",
                $"[CompanyId] <> {EmptyGuid} AND LEN([Sha256]) = 64 AND LEN([ObjectKey]) > 0 AND LEN([FirstStoredBy]) > 0");
            table.HasCheckConstraint("CK_ContentBlobs_Size", "[SizeBytes] > 0");
            table.HasCheckConstraint("CK_ContentBlobs_Kind", "[ContentKind] IN (1, 2, 3)");
        });
        entity.HasKey(x => new { x.CompanyId, x.Sha256 });
        entity.Property(x => x.Sha256).HasMaxLength(64).IsUnicode(false).IsFixedLength().UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.ObjectKey).HasMaxLength(200).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.FirstStoredBy).HasMaxLength(128).IsRequired();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureDocuments(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DocumentRow>();
        entity.ToTable("Documents", Schema, table =>
        {
            table.HasCheckConstraint("CK_Documents_Identity",
                $"[Id] <> {EmptyGuid} AND [CompanyId] <> {EmptyGuid} AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureVersions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DocumentVersionRow>();
        entity.ToTable("DocumentVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_DocumentVersions_VersionNo", "[VersionNo] >= 1");
            table.HasCheckConstraint("CK_DocumentVersions_Text",
                "LEN([FileName]) > 0 AND LEN([UploadedBy]) > 0 AND LEN([CorrelationId]) > 0");
            // DEC-DOC-009: a version is Clean only with the scanner that cleared it; NotScanned names no scanner.
            table.HasCheckConstraint("CK_DocumentVersions_Scan",
                "([ScanState] = 2 AND [ScannerId] IS NOT NULL AND LEN([ScannerId]) > 0) OR ([ScanState] = 1 AND [ScannerId] IS NULL)");
        });
        entity.HasKey(x => new { x.CompanyId, x.DocumentId, x.VersionNo });
        entity.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Sha256).HasMaxLength(64).IsUnicode(false).IsFixedLength().UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.ScannerId).HasMaxLength(64).IsUnicode(false);
        entity.Property(x => x.UploadedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        // AT-057: the same content within the company, newest first.
        entity.HasIndex(x => new { x.CompanyId, x.Sha256, x.UploadedAtUtc }).HasDatabaseName("IX_DocumentVersions_CompanyId_Sha256_UploadedAtUtc");
        entity.HasOne<DocumentRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DocumentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ContentBlobRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Sha256 })
            .HasPrincipalKey(x => new { x.CompanyId, x.Sha256 })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureLinks(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DocumentLinkRow>();
        entity.ToTable("DocumentLinks", Schema, table =>
        {
            table.HasCheckConstraint("CK_DocumentLinks_Identity",
                $"[OwnerId] <> {EmptyGuid} AND LEN([OwnerKind]) > 0 AND LEN([LinkedBy]) > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.DocumentId, x.OwnerKind, x.OwnerId });
        entity.Property(x => x.OwnerKind).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.LinkedBy).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.OwnerKind, x.OwnerId, x.LinkedAtUtc })
            .HasDatabaseName("IX_DocumentLinks_CompanyId_OwnerKind_OwnerId_LinkedAtUtc");
        entity.HasOne<DocumentRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DocumentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureAttempts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DocumentAttemptRow>();
        entity.ToTable("DocumentAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_DocumentAttempts_Identity",
                $"[CompanyId] <> {EmptyGuid} AND [DocumentId] <> {EmptyGuid} AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 "
                + "AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_DocumentAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
        });
        entity.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        entity.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        // One upload attempt per stored document. DocumentId is NOT NULL; the filter names the command explicitly.
        entity.HasIndex(x => new { x.CompanyId, x.DocumentId }).IsUnique().HasDatabaseName("UX_DocumentAttempts_DocumentId")
            .HasFilter("[CommandName] = N'" + DocumentCommands.UploadV1 + "'");
        entity.HasOne<DocumentRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DocumentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ApplyWriteGuards()
    {
        DocumentsAuditEventTenantGuard.Validate(this, companyContext);
        foreach (var entry in ChangeTracker.Entries<IDocumentsTenantRow>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Belge kayıtları değiştirilemez veya silinemez; yeni bilgi yeni kayıttır.");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Belge kaydı CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Belge kaydı yalnız aktif şirket bağlamında ve o şirket için yazılır.");
            }
        }
    }
}
