using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;

/// <summary>
/// WP-A4 knowledge package approvals (schema <c>aiknowledge</c>). Every row carries CompanyId and is read only through the
/// company query filter; approvals are never deleted (Approved → Withdrawn by one version step), attempts are append-only.
/// </summary>
public sealed class AiKnowledgeDbContext(
    DbContextOptions<AiKnowledgeDbContext> options,
    ICompanyContext companyContext) : DbContext(options)
{
    public const string Schema = "aiknowledge";
    internal const string EmptyGuid = "'00000000-0000-0000-0000-000000000000'";
    internal const string HexCheck = "DATALENGTH([{0}]) = 64 AND [{0}] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'";
    internal const string ActiveApprovalIndexName = "UX_AiKnowledgePackageApprovals_Active";
    internal const string ApprovalIdentityCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [Id] <> " + EmptyGuid + " AND [ApprovedByUserId] <> " + EmptyGuid
        + " AND [SourceKind] IN (1, 2) AND LEN([CorrelationId]) > 0"
        + " AND DATALENGTH([PackageSha256]) = 64 AND [PackageSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'";
    internal const string ApprovalStateCheck =
        "([Status] = 1 AND [Version] = 1 AND [WithdrawnByUserId] IS NULL AND [WithdrawnAtUtc] IS NULL AND [WithdrawReason] IS NULL)"
        + " OR ([Status] = 2 AND [Version] = 2 AND [WithdrawnByUserId] IS NOT NULL AND [WithdrawnByUserId] <> " + EmptyGuid
        + " AND [WithdrawnAtUtc] IS NOT NULL AND [WithdrawnAtUtc] >= [ApprovedAtUtc]"
        + " AND [WithdrawReason] IS NOT NULL AND LEN([WithdrawReason]) > 0)";
    internal const string AttemptCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [ActorUserId] <> " + EmptyGuid + " AND [TargetId] <> " + EmptyGuid
        + " AND LEN([Command]) > 0 AND LEN([CorrelationId]) > 0"
        + " AND DATALENGTH([IdempotencyKeySha256]) = 64 AND [IdempotencyKeySha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'"
        + " AND DATALENGTH([RequestSha256]) = 64 AND [RequestSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'"
        + " AND (([Status] IN (200, 201) AND [RefusalCode] IS NULL) OR ([Status] = 409 AND [RefusalCode] IS NOT NULL AND LEN([RefusalCode]) > 0))";

    internal DbSet<AiKnowledgeApprovalRow> Approvals => Set<AiKnowledgeApprovalRow>();
    internal DbSet<AiKnowledgeCommandAttemptRow> Attempts => Set<AiKnowledgeCommandAttemptRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var company = modelBuilder.Entity<AiKnowledgeCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        var approval = modelBuilder.Entity<AiKnowledgeApprovalRow>();
        approval.ToTable("PackageApprovals", Schema, table =>
        {
            table.HasCheckConstraint("CK_AiKnowledgePackageApprovals_Identity", ApprovalIdentityCheck);
            table.HasCheckConstraint("CK_AiKnowledgePackageApprovals_State", ApprovalStateCheck);
        });
        approval.HasKey(row => new { row.CompanyId, row.Id });
        approval.Property(row => row.PackageSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength().IsRequired();
        approval.Property(row => row.WithdrawReason).HasMaxLength(500);
        approval.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        approval.Property(row => row.CausationId).HasMaxLength(128);
        approval.Property(row => row.Version).IsConcurrencyToken();
        approval.Property(row => row.RowVersion).IsRowVersion();
        // At most one active approval per company, kind and packaged bytes (a race leaves one).
        approval.HasIndex(row => new { row.CompanyId, row.SourceKind, row.PackageSha256 }).IsUnique()
            .HasFilter("[Status] = 1").HasDatabaseName(ActiveApprovalIndexName);
        approval.HasIndex(row => new { row.CompanyId, row.SourceKind, row.ApprovedAtUtc, row.Id });
        approval.HasOne<AiKnowledgeCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        approval.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var attempt = modelBuilder.Entity<AiKnowledgeCommandAttemptRow>();
        attempt.ToTable("CommandAttempts", Schema, table => table.HasCheckConstraint("CK_AiKnowledgeCommandAttempts_Identity", AttemptCheck));
        attempt.HasKey(row => new { row.CompanyId, row.ActorUserId, row.IdempotencyKeySha256 });
        attempt.Property(row => row.IdempotencyKeySha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength();
        attempt.Property(row => row.RequestSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength();
        attempt.Property(row => row.Command).HasMaxLength(80).IsUnicode(false).IsRequired();
        attempt.Property(row => row.RefusalCode).HasMaxLength(64).IsUnicode(false);
        attempt.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.HasOne<AiKnowledgeCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>().HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
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

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached || entry.Entity is AuditEvent) continue;
            var companyId = entry.Entity switch
            {
                AiKnowledgeApprovalRow row => row.CompanyId,
                AiKnowledgeCommandAttemptRow row => row.CompanyId,
                _ => throw new InvalidOperationException("Unknown AI knowledge record."),
            };
            if (!companyContext.IsResolved || companyId != companyContext.CompanyId)
                throw new InvalidOperationException("AI knowledge records require the active company context.");
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("AI knowledge records cannot be deleted.");
            if (entry.Entity is AiKnowledgeCommandAttemptRow && entry.State != EntityState.Added)
                throw new InvalidOperationException("AI knowledge attempts are append-only.");
            if (entry.Entity is AiKnowledgeApprovalRow approval && entry.State == EntityState.Modified)
            {
                if (!Equals(entry.Property(nameof(approval.SourceKind)).OriginalValue, approval.SourceKind)
                    || !Equals(entry.Property(nameof(approval.PackageSha256)).OriginalValue, approval.PackageSha256)
                    || !Equals(entry.Property(nameof(approval.ApprovedByUserId)).OriginalValue, approval.ApprovedByUserId)
                    || !Equals(entry.Property(nameof(approval.ApprovedAtUtc)).OriginalValue, approval.ApprovedAtUtc)
                    || entry.Property(nameof(approval.Status)).OriginalValue is not 1
                    || approval.Status != 2
                    || entry.Property(nameof(approval.Version)).OriginalValue is not long version
                    || approval.Version != checked(version + 1))
                    throw new InvalidOperationException("An approval is only withdrawn once, by one version step, and keeps its package.");
            }
        }
    }
}
