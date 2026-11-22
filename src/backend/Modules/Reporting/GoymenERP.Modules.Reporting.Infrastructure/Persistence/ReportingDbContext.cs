using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Reporting.Infrastructure.Persistence;

/// <summary>
/// WP14: the report centre's own schema. It holds ONLY the centre's records (personal saved views, export records, command
/// attempts) and the shared audit table it appends to; no report number is stored here and no other module's table is mapped.
/// </summary>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options, ICompanyContext companyContext) : DbContext(options)
{
    public const string Schema = "reporting";
    internal const string EmptyGuid = "'00000000-0000-0000-0000-000000000000'";
    internal const string Sha256Check = "DATALENGTH([{0}]) = 64 AND [{0}] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'";
    internal const string BinaryCollation = "Latin1_General_100_BIN2";

    internal const string SavedViewCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [Id] <> " + EmptyGuid + " AND [OwnerUserId] <> " + EmptyGuid
        + " AND LEN([ReportId]) > 0 AND LEN([Name]) > 0 AND [Version] >= 1 AND [UpdatedAtUtc] >= [CreatedAtUtc]"
        + " AND ISJSON([ParametersJson]) = 1 AND ISJSON([ColumnsJson]) = 1";
    internal const string ExportCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [Id] <> " + EmptyGuid + " AND [ActorUserId] <> " + EmptyGuid
        + " AND [Kind] IN (1, 2) AND LEN([ReportId]) > 0 AND [Format] IN ('csv', 'xlsx', 'zip')"
        + " AND (([Kind] = 1 AND [Format] IN ('csv', 'xlsx') AND [Page] IS NOT NULL AND [Page] >= 1 AND [PageSize] IS NOT NULL AND [PageSize] >= 1)"
        + " OR ([Kind] = 2 AND [Format] = 'zip' AND [Page] IS NULL AND [PageSize] IS NULL))"
        + " AND [RowCount] >= 0 AND [ByteLength] > 0 AND LEN([CorrelationId]) > 0"
        + " AND ISJSON([ParametersJson]) = 1 AND ISJSON([ColumnsJson]) = 1";
    internal const string AttemptCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [ActorUserId] <> " + EmptyGuid + " AND [TargetId] <> " + EmptyGuid
        + " AND LEN([Command]) > 0 AND LEN([CorrelationId]) > 0"
        + " AND (([Status] IN (200, 201) AND [RefusalCode] IS NULL) OR ([Status] = 409 AND [RefusalCode] IS NOT NULL AND LEN([RefusalCode]) > 0))";

    internal DbSet<ReportingSavedViewRow> SavedViews => Set<ReportingSavedViewRow>();
    internal DbSet<ReportingExportRow> Exports => Set<ReportingExportRow>();
    internal DbSet<ReportingAttemptRow> Attempts => Set<ReportingAttemptRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var company = modelBuilder.Entity<ReportingCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(row => row.Id);

        var view = modelBuilder.Entity<ReportingSavedViewRow>();
        view.ToTable("SavedViews", Schema, table => table.HasCheckConstraint("CK_ReportingSavedViews_Identity", SavedViewCheck));
        view.HasKey(row => new { row.CompanyId, row.Id });
        view.Property(row => row.ReportId).HasMaxLength(64).IsUnicode(false).UseCollation(BinaryCollation).IsRequired();
        view.Property(row => row.Name).HasMaxLength(80).IsRequired();
        view.Property(row => row.ParametersJson).HasMaxLength(4000).IsRequired();
        view.Property(row => row.ColumnsJson).HasMaxLength(2000).IsRequired();
        view.Property(row => row.Version).IsConcurrencyToken();
        view.Property(row => row.RowVersion).IsRowVersion();
        view.HasIndex(row => new { row.CompanyId, row.OwnerUserId, row.ReportId, row.Name }).IsUnique()
            .HasFilter("[IsActive] = 1").HasDatabaseName("UX_ReportingSavedViews_ActiveName");
        view.HasOne<ReportingCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        view.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var export = modelBuilder.Entity<ReportingExportRow>();
        export.ToTable("ExportRecords", Schema, table =>
        {
            table.HasCheckConstraint("CK_ReportingExportRecords_Identity", ExportCheck);
            table.HasCheckConstraint("CK_ReportingExportRecords_Hash", string.Format(Sha256Check, "ContentSha256"));
        });
        export.HasKey(row => new { row.CompanyId, row.Id });
        export.Property(row => row.ReportId).HasMaxLength(64).IsUnicode(false).UseCollation(BinaryCollation).IsRequired();
        export.Property(row => row.Format).HasMaxLength(8).IsUnicode(false).UseCollation(BinaryCollation).IsRequired();
        export.Property(row => row.ParametersJson).HasMaxLength(4000).IsRequired();
        export.Property(row => row.ColumnsJson).HasMaxLength(2000).IsRequired();
        export.Property(row => row.ContentSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength().IsRequired();
        export.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        export.Property(row => row.CausationId).HasMaxLength(128);
        export.HasIndex(row => new { row.CompanyId, row.RecordedAtUtc, row.Id });
        export.HasIndex(row => new { row.CompanyId, row.ActorUserId, row.RecordedAtUtc });
        export.HasOne<ReportingCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        export.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var attempt = modelBuilder.Entity<ReportingAttemptRow>();
        attempt.ToTable("CommandAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_ReportingCommandAttempts_Identity", AttemptCheck);
            table.HasCheckConstraint("CK_ReportingCommandAttempts_KeyHash", string.Format(Sha256Check, "IdempotencyKeySha256"));
            table.HasCheckConstraint("CK_ReportingCommandAttempts_RequestHash", string.Format(Sha256Check, "RequestSha256"));
        });
        attempt.HasKey(row => new { row.CompanyId, row.ActorUserId, row.IdempotencyKeySha256 });
        attempt.Property(row => row.IdempotencyKeySha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength();
        attempt.Property(row => row.RequestSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength();
        attempt.Property(row => row.Command).HasMaxLength(80).IsUnicode(false).UseCollation(BinaryCollation).IsRequired();
        attempt.Property(row => row.RefusalCode).HasMaxLength(64).IsUnicode(false);
        attempt.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.HasOne<ReportingCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

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

    private void ApplyWriteGuards()
    {
        foreach (var entry in ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Audit records are append-only.");
            if (entry.State == EntityState.Added && (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId))
                throw new InvalidOperationException("Audit record company must match the active company.");
        }

        foreach (var entry in ChangeTracker.Entries<IReportingTenantRow>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;
            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
                throw new InvalidOperationException("Reporting records require the active company context.");
            if (entry.State == EntityState.Deleted) throw new InvalidOperationException("Reporting records cannot be deleted.");
            if (entry.Entity is ReportingExportRow or ReportingAttemptRow && entry.State != EntityState.Added)
                throw new InvalidOperationException("Export records and command attempts are append-only.");
            if (entry.Entity is ReportingSavedViewRow view && entry.State == EntityState.Modified)
            {
                if (!Equals(entry.Property(nameof(view.Id)).OriginalValue, view.Id)
                    || !Equals(entry.Property(nameof(view.OwnerUserId)).OriginalValue, view.OwnerUserId)
                    || !Equals(entry.Property(nameof(view.ReportId)).OriginalValue, view.ReportId)
                    || !Equals(entry.Property(nameof(view.CreatedAtUtc)).OriginalValue, view.CreatedAtUtc)
                    || entry.Property(nameof(view.IsActive)).OriginalValue is not true
                    || entry.Property(nameof(view.Version)).OriginalValue is not long version
                    || view.Version != checked(version + 1))
                    throw new InvalidOperationException("A saved view keeps its identity, owner and report and moves one version at a time while active.");
            }
        }
    }
}
