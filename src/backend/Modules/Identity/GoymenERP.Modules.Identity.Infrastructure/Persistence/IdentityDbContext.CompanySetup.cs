using GoymenERP.Modules.Identity.Domain.CompanySetup;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Assignments;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence;
public sealed partial class IdentityDbContext
{
    internal const string CompanySetupReceiptIdentityCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND LEN([PayloadHash]) = 64 AND [PayloadHash] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%'";
    private (Guid Company, Guid User, Guid Role)? companyOpenerProof;
    internal IDisposable BeginCompanyOpenerWrite(Guid companyId, Guid userId, Guid roleId)
    {
        if (companyOpenerProof is not null || Database.CurrentTransaction is null || companyContext is not { IsResolved: true }
            || companyContext.CompanyId != companyId) throw new InvalidOperationException("Company opener proof requires the locked owner transaction.");
        companyOpenerProof = (companyId, userId, roleId);
        return new CompanyOpenerLease(this);
    }
    private sealed class CompanyOpenerLease(IdentityDbContext owner) : IDisposable
    {
        public void Dispose() => owner.companyOpenerProof = null;
    }
    public DbSet<CompanySetupInboxReceipt> CompanySetupInboxReceipts => Set<CompanySetupInboxReceipt>();
    private Guid CompanySetupCompanyId => companyContext is { IsResolved: true } ? companyContext.CompanyId : Guid.Empty;
    private void ConfigureCompanySetup(ModelBuilder modelBuilder)
    {
        var receipt = modelBuilder.Entity<CompanySetupInboxReceipt>();
        receipt.ToTable("CompanySetupInboxReceipts", "identity", table => table.HasCheckConstraint("CK_CompanySetupInbox_Identity",
            CompanySetupReceiptIdentityCheck));
        receipt.HasQueryFilter(x => x.CompanyId == CompanySetupCompanyId);
        receipt.HasKey(x => new { x.CompanyId, x.EventId });
        receipt.Property(x => x.PayloadHash).HasMaxLength(64).IsFixedLength().IsRequired();
    }
    private void ApplyCompanySetupWriteGuards()
    {
        foreach (var entry in ChangeTracker.Entries<AccessGrantRequest>().Where(x => x.State is EntityState.Added or EntityState.Modified
            && x.Entity.ApprovalMode == AccessGrantApprovalMode.CompanyOpener))
        {
            var request = entry.Entity;
            if (companyContext is not { IsResolved: true } || request.CompanyId != companyContext.CompanyId
                || request.RequestedByUserId != request.TargetUserId || request.DecidedByUserId != request.TargetUserId
                || request.ValidUntilUtc is not null || Database.CurrentTransaction is null
                || companyOpenerProof != (request.CompanyId, request.TargetUserId, request.RoleId)
                || !ChangeTracker.Entries<CompanySetupInboxReceipt>().Any(x => x.State == EntityState.Added && x.Entity.CompanyId == request.CompanyId)
                || !ChangeTracker.Entries<RoleAssignment>().Any(x => x.State == EntityState.Added
                    && x.Entity.Id == request.ResultingRoleAssignmentId && x.Entity.CompanyId == request.CompanyId
                    && x.Entity.RoleId == request.RoleId && x.Entity.UserId == request.TargetUserId && x.Entity.ValidUntilUtc is null
                    && x.Entity.Scopes.Any(scope => scope.ScopeType == AccessScopeType.Company && scope.ScopeId == request.CompanyId)))
                throw new InvalidOperationException("Company opener approval requires the company inbox and its indefinite assignment in one transaction.");
        }
        foreach (var entry in ChangeTracker.Entries<GoymenERP.Modules.Identity.Domain.Outbox.IdentityOutboxMessage>()
            .Where(x => x.State is not (EntityState.Unchanged or EntityState.Detached)
                && (x.Entity.EventType == GoymenERP.BuildingBlocks.Messaging.IdentityCompanyFirstAdminAssignedV1.EventType
                    || x.Property(row => row.EventType).OriginalValue == GoymenERP.BuildingBlocks.Messaging.IdentityCompanyFirstAdminAssignedV1.EventType)))
        {
            var message = entry.Entity;
            if (entry.State != EntityState.Added || companyContext is not { IsResolved: true }
                || message.CompanyId != companyContext.CompanyId || message.AggregateId != companyContext.CompanyId
                || message.SchemaVersion != 1 || message.AggregateVersion != 1
                || message.Id != GoymenERP.BuildingBlocks.Messaging.IdentityCompanyFirstAdminAssignedV1.EventId(companyContext.CompanyId))
                throw new InvalidOperationException("First Admin event is immutable, deterministic and company fenced.");
        }
        foreach (var entry in ChangeTracker.Entries<CompanySetupInboxReceipt>().Where(x => x.State is not (EntityState.Unchanged or EntityState.Detached)))
        {
            if (entry.State != EntityState.Added || companyContext is not { IsResolved: true }
                || entry.Entity.CompanyId != companyContext.CompanyId)
                throw new InvalidOperationException("Company setup inbox is immutable and company fenced.");
        }
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyCompanySetupWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyCompanySetupWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
