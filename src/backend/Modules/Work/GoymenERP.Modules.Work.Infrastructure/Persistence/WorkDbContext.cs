using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

public sealed class WorkDbContext(
    DbContextOptions<WorkDbContext> options,
    ICompanyContext companyContext) : DbContext(options)
{
    public const string Schema = "work";
    internal const string KeyBytesCheck =
        "DATALENGTH([SourceKeyUtf16Be]) > 0 AND DATALENGTH([SourceKeyUtf16Be]) % 2 = 0 "
        + "AND DATALENGTH([ScopeKeyUtf16Be]) > 0 AND DATALENGTH([ScopeKeyUtf16Be]) % 2 = 0 "
        + "AND [SourceKeyDigest] = CONVERT(binary(32), HASHBYTES('SHA2_256', [SourceKeyUtf16Be])) "
        + "AND [ScopeKeyDigest] = CONVERT(binary(32), HASHBYTES('SHA2_256', [ScopeKeyUtf16Be]))";
    internal const string PayloadHashCheck =
        "DATALENGTH([{0}]) = 64 AND [{0}] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'";

    internal DbSet<WorkDesiredStateSnapshotRow> DesiredStateSnapshots => Set<WorkDesiredStateSnapshotRow>();
    internal DbSet<WorkObservationInboxRow> ObservationInbox => Set<WorkObservationInboxRow>();
    internal DbSet<WorkOutboxRow> OutboxMessages => Set<WorkOutboxRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var company = modelBuilder.Entity<OrganizationCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        ConfigureSnapshots(modelBuilder);
        ConfigureInbox(modelBuilder);
        ConfigureOutbox(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ConfigureSnapshots(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkDesiredStateSnapshotRow>();
        entity.ToTable("DesiredStateSnapshots", Schema, table =>
        {
            table.HasCheckConstraint("CK_WorkSnapshots_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_WorkSnapshots_KeyBytes", KeyBytesCheck);
            table.HasCheckConstraint("CK_WorkSnapshots_HighestHash", string.Format(PayloadHashCheck, "HighestPayloadSha256"));
            table.HasCheckConstraint("CK_WorkSnapshots_EffectiveHash",
                "[EffectivePayloadSha256] IS NULL OR (" + string.Format(PayloadHashCheck, "EffectivePayloadSha256") + ")");
            table.HasCheckConstraint("CK_WorkSnapshots_State",
                "[HighestObservationOrdinal] >= 1 AND [HighestEvidenceId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "(([HighestAvailability] = 1 AND [HighestDesiredState] IN (1,2) "
                + "AND [EffectiveObservationOrdinal] = [HighestObservationOrdinal] "
                + "AND [EffectiveDesiredState] = [HighestDesiredState] "
                + "AND [EffectiveEvidenceId] = [HighestEvidenceId] "
                + "AND [EffectivePayloadSha256] = [HighestPayloadSha256]) OR "
                + "([HighestAvailability] = 2 AND [HighestDesiredState] IS NULL AND "
                + "(([EffectiveObservationOrdinal] IS NULL AND [EffectiveDesiredState] IS NULL "
                + "AND [EffectiveEvidenceId] IS NULL AND [EffectivePayloadSha256] IS NULL) OR "
                + "([EffectiveObservationOrdinal] >= 1 AND [EffectiveObservationOrdinal] < [HighestObservationOrdinal] "
                + "AND [EffectiveDesiredState] IN (1,2) "
                + "AND [EffectiveEvidenceId] IS NOT NULL AND [EffectiveEvidenceId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [EffectivePayloadSha256] IS NOT NULL))))");
            table.HasCheckConstraint("CK_WorkSnapshots_Version", "[Version] >= 1");
        });
        entity.HasKey(x => new { x.CompanyId, x.SourceKeyDigest, x.ScopeKeyDigest });
        ConfigureKey(entity.Property(x => x.SourceKeyDigest), "binary(32)");
        ConfigureKey(entity.Property(x => x.ScopeKeyDigest), "binary(32)");
        entity.Property(x => x.SourceKeyUtf16Be).HasColumnType("varbinary(max)").IsRequired();
        entity.Property(x => x.ScopeKeyUtf16Be).HasColumnType("varbinary(max)").IsRequired();
        entity.Property(x => x.HighestPayloadSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength().IsRequired();
        entity.Property(x => x.EffectivePayloadSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureInbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkObservationInboxRow>();
        entity.ToTable("ObservationInbox", Schema, table =>
        {
            table.HasCheckConstraint("CK_WorkInbox_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_WorkInbox_KeyBytes", KeyBytesCheck);
            table.HasCheckConstraint("CK_WorkInbox_Hash", string.Format(PayloadHashCheck, "PayloadSha256"));
            table.HasCheckConstraint("CK_WorkInbox_State",
                "[ObservationOrdinal] >= 1 AND [EvidenceId] <> '00000000-0000-0000-0000-000000000000' AND "
                + "(([Availability] = 1 AND [DesiredState] IN (1,2)) OR ([Availability] = 2 AND [DesiredState] IS NULL))");
        });
        entity.HasKey(x => new { x.CompanyId, x.SourceKeyDigest, x.ScopeKeyDigest, x.ObservationOrdinal });
        ConfigureKey(entity.Property(x => x.SourceKeyDigest), "binary(32)");
        ConfigureKey(entity.Property(x => x.ScopeKeyDigest), "binary(32)");
        entity.Property(x => x.SourceKeyUtf16Be).HasColumnType("varbinary(max)").IsRequired();
        entity.Property(x => x.ScopeKeyUtf16Be).HasColumnType("varbinary(max)").IsRequired();
        entity.Property(x => x.PayloadSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength().IsRequired();
        entity.HasOne<WorkDesiredStateSnapshotRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceKeyDigest, x.ScopeKeyDigest })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkOutboxRow>();
        entity.ToTable("OutboxMessages", Schema, table =>
        {
            table.HasCheckConstraint("CK_WorkOutbox_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ObservationOrdinal] >= 1 AND [AggregateVersion] >= 1 AND [SchemaVersion] = 2 "
                + "AND LEN([EventType]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_WorkOutbox_KeyBytes", KeyBytesCheck);
            table.HasCheckConstraint("CK_WorkOutbox_Payload", "ISJSON([PayloadJson]) = 1");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureKey(entity.Property(x => x.SourceKeyDigest), "binary(32)");
        ConfigureKey(entity.Property(x => x.ScopeKeyDigest), "binary(32)");
        entity.Property(x => x.SourceKeyUtf16Be).HasColumnType("varbinary(max)").IsRequired();
        entity.Property(x => x.ScopeKeyUtf16Be).HasColumnType("varbinary(max)").IsRequired();
        entity.Property(x => x.EventType).HasMaxLength(160).IsRequired();
        entity.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CausationId).HasMaxLength(128);
        entity.HasIndex(x => new { x.CompanyId, x.SourceKeyDigest, x.ScopeKeyDigest, x.ObservationOrdinal })
            .IsUnique()
            .HasDatabaseName("UX_WorkOutbox_ScopedOrdinal");
        entity.HasOne<WorkDesiredStateSnapshotRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceKeyDigest, x.ScopeKeyDigest })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private static void ConfigureKey(
        Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<byte[]> property,
        string columnType) => property.HasColumnType(columnType).IsRequired();

    private void ApplyWriteGuards()
    {
        WorkAuditEventTenantGuard.Validate(this, companyContext);
        foreach (var entry in ChangeTracker.Entries<IWorkTenantRow>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Work records require the active company context.");
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Work records cannot be deleted.");
            }

            if (entry.Entity is WorkObservationInboxRow or WorkOutboxRow
                && entry.State != EntityState.Added)
            {
                throw new InvalidOperationException("Work inbox and outbox records are append-only.");
            }

            if (entry.Entity is WorkDesiredStateSnapshotRow snapshot
                && entry.State == EntityState.Modified)
            {
                var original = entry.OriginalValues;
                if (original.GetValue<Guid>(nameof(snapshot.CompanyId)) != snapshot.CompanyId
                    || !original.GetValue<byte[]>(nameof(snapshot.SourceKeyDigest)).AsSpan().SequenceEqual(snapshot.SourceKeyDigest)
                    || !original.GetValue<byte[]>(nameof(snapshot.ScopeKeyDigest)).AsSpan().SequenceEqual(snapshot.ScopeKeyDigest)
                    || !original.GetValue<byte[]>(nameof(snapshot.SourceKeyUtf16Be)).AsSpan().SequenceEqual(snapshot.SourceKeyUtf16Be)
                    || !original.GetValue<byte[]>(nameof(snapshot.ScopeKeyUtf16Be)).AsSpan().SequenceEqual(snapshot.ScopeKeyUtf16Be)
                    || snapshot.Version != checked(original.GetValue<long>(nameof(snapshot.Version)) + 1)
                    || snapshot.HighestObservationOrdinal <= original.GetValue<long>(nameof(snapshot.HighestObservationOrdinal)))
                {
                    throw new InvalidOperationException("Work snapshot identity and monotonic version are immutable.");
                }
            }
        }
    }
}
