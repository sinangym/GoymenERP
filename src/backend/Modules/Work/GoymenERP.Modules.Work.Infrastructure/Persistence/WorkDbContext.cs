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
    internal DbSet<WorkExternalEventHighWaterRow> ExternalEventHighWater => Set<WorkExternalEventHighWaterRow>();
    internal DbSet<WorkExternalEventInboxRow> ExternalEventInbox => Set<WorkExternalEventInboxRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var company = modelBuilder.Entity<OrganizationCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        ConfigureSnapshots(modelBuilder);
        ConfigureInbox(modelBuilder);
        ConfigureOutbox(modelBuilder);
        ConfigureExternalEventHighWater(modelBuilder);
        ConfigureExternalEventInbox(modelBuilder);
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
                + "(([HighestAvailability] = 1 AND [HighestDesiredState] IS NOT NULL AND [HighestDesiredState] IN (1,2) "
                + "AND [EffectiveObservationOrdinal] IS NOT NULL AND [EffectiveObservationOrdinal] = [HighestObservationOrdinal] "
                + "AND [EffectiveDesiredState] IS NOT NULL AND [EffectiveDesiredState] = [HighestDesiredState] "
                + "AND [EffectiveEvidenceId] IS NOT NULL AND [EffectiveEvidenceId] = [HighestEvidenceId] "
                + "AND [EffectivePayloadSha256] IS NOT NULL AND [EffectivePayloadSha256] = [HighestPayloadSha256]) OR "
                + "([HighestAvailability] = 2 AND [HighestDesiredState] IS NULL AND "
                + "(([EffectiveObservationOrdinal] IS NULL AND [EffectiveDesiredState] IS NULL "
                + "AND [EffectiveEvidenceId] IS NULL AND [EffectivePayloadSha256] IS NULL) OR "
                + "([EffectiveObservationOrdinal] IS NOT NULL AND [EffectiveObservationOrdinal] >= 1 AND [EffectiveObservationOrdinal] < [HighestObservationOrdinal] "
                + "AND [EffectiveDesiredState] IS NOT NULL AND [EffectiveDesiredState] IN (1,2) "
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
                + "(([Availability] = 1 AND [DesiredState] IS NOT NULL AND [DesiredState] IN (1,2)) OR ([Availability] = 2 AND [DesiredState] IS NULL))");
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

    private void ConfigureExternalEventHighWater(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkExternalEventHighWaterRow>();
        entity.ToTable("ExternalEventHighWater", Schema, table =>
        {
            table.HasCheckConstraint("CK_WorkExternalHighWater_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [AggregateId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ConsumerName] = 'work.shipment-evidence.v1' "
                + "AND [EventType] = 'inventory.definite-shipment.recorded.v1' "
                + "AND [AggregateType] = 'Inventory.StockAllocation'");
            table.HasCheckConstraint("CK_WorkExternalHighWater_Sequence",
                "[InitialSequence] >= 1 AND ([HighestCompletedSequence] IS NULL "
                + "OR [HighestCompletedSequence] >= [InitialSequence]) AND [Version] >= 1");
        });
        entity.HasKey(x => new
        {
            x.CompanyId,
            x.ConsumerName,
            x.EventType,
            x.AggregateType,
            x.AggregateId
        });
        entity.Property(x => x.ConsumerName).HasMaxLength(96).IsRequired();
        entity.Property(x => x.EventType).HasMaxLength(96).IsRequired();
        entity.Property(x => x.AggregateType).HasMaxLength(96).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureExternalEventInbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkExternalEventInboxRow>();
        entity.ToTable("ExternalEventInbox", Schema, table =>
        {
            table.HasCheckConstraint("CK_WorkExternalInbox_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [MessageId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [AggregateId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ConsumerName] = 'work.shipment-evidence.v1' "
                + "AND [EventType] = 'inventory.definite-shipment.recorded.v1' "
                + "AND [SchemaVersion] = 1 "
                + "AND [SourceOperation] = 'inventory.definite-shipment.record.v1' "
                + "AND [AggregateType] = 'Inventory.StockAllocation'");
            table.HasCheckConstraint("CK_WorkExternalInbox_Sequence",
                "[AggregateVersion] >= 1 AND [Sequence] = [AggregateVersion]");
            table.HasCheckConstraint("CK_WorkExternalInbox_Payload",
                "ISJSON([PayloadJson]) = 1 AND " + string.Format(PayloadHashCheck, "PayloadSha256"));
            table.HasCheckConstraint("CK_WorkExternalInbox_Time",
                "[CompletedAtUtc] IS NULL OR [CompletedAtUtc] >= [ReceivedAtUtc]");
        });
        entity.HasKey(x => new { x.CompanyId, x.ConsumerName, x.MessageId });
        entity.Property(x => x.ConsumerName).HasMaxLength(96).IsRequired();
        entity.Property(x => x.EventType).HasMaxLength(96).IsRequired();
        entity.Property(x => x.SourceOperation).HasMaxLength(160).IsRequired();
        entity.Property(x => x.AggregateType).HasMaxLength(96).IsRequired();
        entity.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.PayloadSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength().IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CausationId).HasMaxLength(128);
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new
            {
                x.CompanyId,
                x.ConsumerName,
                x.EventType,
                x.AggregateType,
                x.AggregateId,
                x.Sequence
            })
            .IsUnique()
            .HasDatabaseName("UX_WorkExternalInbox_AggregateSequence");
        entity.HasOne<WorkExternalEventHighWaterRow>().WithMany()
            .HasForeignKey(x => new
            {
                x.CompanyId,
                x.ConsumerName,
                x.EventType,
                x.AggregateType,
                x.AggregateId
            })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

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

            if (entry.Entity is WorkExternalEventInboxRow externalInbox
                && entry.State == EntityState.Modified)
            {
                var original = entry.OriginalValues;
                if (original.GetValue<Guid>(nameof(externalInbox.CompanyId)) != externalInbox.CompanyId
                    || !string.Equals(original.GetValue<string>(nameof(externalInbox.ConsumerName)), externalInbox.ConsumerName, StringComparison.Ordinal)
                    || original.GetValue<Guid>(nameof(externalInbox.MessageId)) != externalInbox.MessageId
                    || !string.Equals(original.GetValue<string>(nameof(externalInbox.EventType)), externalInbox.EventType, StringComparison.Ordinal)
                    || original.GetValue<int>(nameof(externalInbox.SchemaVersion)) != externalInbox.SchemaVersion
                    || !string.Equals(original.GetValue<string>(nameof(externalInbox.SourceOperation)), externalInbox.SourceOperation, StringComparison.Ordinal)
                    || !string.Equals(original.GetValue<string>(nameof(externalInbox.AggregateType)), externalInbox.AggregateType, StringComparison.Ordinal)
                    || original.GetValue<Guid>(nameof(externalInbox.AggregateId)) != externalInbox.AggregateId
                    || original.GetValue<long>(nameof(externalInbox.AggregateVersion)) != externalInbox.AggregateVersion
                    || original.GetValue<long>(nameof(externalInbox.Sequence)) != externalInbox.Sequence
                    || !string.Equals(original.GetValue<string>(nameof(externalInbox.PayloadJson)), externalInbox.PayloadJson, StringComparison.Ordinal)
                    || !string.Equals(original.GetValue<string>(nameof(externalInbox.PayloadSha256)), externalInbox.PayloadSha256, StringComparison.Ordinal)
                    || original.GetValue<DateTimeOffset>(nameof(externalInbox.OccurredAtUtc)) != externalInbox.OccurredAtUtc
                    || !string.Equals(original.GetValue<string>(nameof(externalInbox.CorrelationId)), externalInbox.CorrelationId, StringComparison.Ordinal)
                    || !string.Equals(original.GetValue<string?>(nameof(externalInbox.CausationId)), externalInbox.CausationId, StringComparison.Ordinal)
                    || original.GetValue<DateTimeOffset>(nameof(externalInbox.ReceivedAtUtc)) != externalInbox.ReceivedAtUtc
                    || original.GetValue<DateTimeOffset?>(nameof(externalInbox.CompletedAtUtc)) is not null
                    || externalInbox.CompletedAtUtc is null)
                {
                    throw new InvalidOperationException("External Work inbox identity is immutable and completion is one-way.");
                }
            }

            if (entry.Entity is WorkExternalEventHighWaterRow highWater
                && entry.State == EntityState.Modified)
            {
                var original = entry.OriginalValues;
                var previousCompleted = original.GetValue<long?>(nameof(highWater.HighestCompletedSequence));
                var expectedCompleted = previousCompleted is null
                    ? highWater.InitialSequence
                    : checked(previousCompleted.Value + 1);
                if (original.GetValue<Guid>(nameof(highWater.CompanyId)) != highWater.CompanyId
                    || !string.Equals(original.GetValue<string>(nameof(highWater.ConsumerName)), highWater.ConsumerName, StringComparison.Ordinal)
                    || !string.Equals(original.GetValue<string>(nameof(highWater.EventType)), highWater.EventType, StringComparison.Ordinal)
                    || !string.Equals(original.GetValue<string>(nameof(highWater.AggregateType)), highWater.AggregateType, StringComparison.Ordinal)
                    || original.GetValue<Guid>(nameof(highWater.AggregateId)) != highWater.AggregateId
                    || original.GetValue<long>(nameof(highWater.InitialSequence)) != highWater.InitialSequence
                    || highWater.HighestCompletedSequence != expectedCompleted
                    || highWater.Version != checked(original.GetValue<long>(nameof(highWater.Version)) + 1))
                {
                    throw new InvalidOperationException("External Work high-water identity and contiguous sequence are immutable.");
                }
            }
        }
    }
}
