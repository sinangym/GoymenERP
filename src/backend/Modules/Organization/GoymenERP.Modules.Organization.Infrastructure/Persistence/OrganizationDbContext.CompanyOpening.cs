using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Domain.CompanySetup;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

public sealed partial class OrganizationDbContext
{
    internal const string CompanyOpeningExecutionCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ActorUserId] <> '00000000-0000-0000-0000-000000000000' AND [StatusCode] IN (201,409) AND ISJSON([ResponseJson]) = 1 AND LEN([IdempotencyKeyHash]) = 64 AND [IdempotencyKeyHash] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND LEN([RequestHash]) = 64 AND [RequestHash] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND LEN([OriginalCorrelationId]) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0";
    private (Guid Company, Guid Actor)? companyOpeningProof;
    public DbSet<CompanyOpeningExecution> CompanyOpeningExecutions => Set<CompanyOpeningExecution>();
    private static void ConfigureCompanyOpening(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CompanyOpeningExecution>();
        entity.ToTable("CompanyOpeningExecutions", "organization", table =>
            table.HasCheckConstraint("CK_CompanyOpeningExecutions_Shape", CompanyOpeningExecutionCheck));
        entity.HasKey(x => x.IdempotencyKeyHash);
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsFixedLength().UseCollation("Latin1_General_100_BIN2");
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsFixedLength().UseCollation("Latin1_General_100_BIN2");
        entity.Property(x => x.OriginalCorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.RecordedAtUtc });
        // Exact global command key is queried only by the group-authorized owner command. It is never a
        // company-filter bypass or a public terminal read, and the guard below binds every append to the target.
    }
    internal IDisposable BeginCompanyOpeningWrite(Guid companyId, CompanySetupGroupAuthoritySnapshotV1 authority)
    {
        if (companyOpeningProof is not null || Database.CurrentTransaction is null || !companyContext.IsResolved
            || companyContext.CompanyId != companyId || !authority.IsGranted || authority.ActorUserId == Guid.Empty
            || authority.Operation != CompanySetupGroupOperationV1.OpenCompany || string.IsNullOrWhiteSpace(authority.Revision))
            throw new InvalidOperationException("Company opening requires explicit group authority and the owner transaction.");
        companyOpeningProof = (companyId, authority.ActorUserId);
        return new CompanyOpeningLease(this);
    }
    private sealed class CompanyOpeningLease(OrganizationDbContext owner) : IDisposable
    {
        public void Dispose() => owner.companyOpeningProof = null;
    }
    private bool IsAuthorizedPendingCompanyAddition(GoymenERP.Modules.Organization.Domain.Companies.Company company) =>
        companyOpeningProof is { } proof && Database.CurrentTransaction is not null && companyContext.IsResolved
        && company.Id == companyContext.CompanyId && proof.Company == company.Id && proof.Actor == company.OpenedByUserId
        && company.LifecycleState == "SetupPending" && company.Version == 1 && !company.IsPlaceholder && !company.InvoiceCapable
        && ChangeTracker.Entries<CompanyOpeningExecution>().Any(entry => entry.State == EntityState.Added
            && entry.Entity.CompanyId == company.Id && entry.Entity.ActorUserId == proof.Actor && entry.Entity.StatusCode == 201)
        && ChangeTracker.Entries<OrganizationCompanySetupOutboxMessage>().Any(entry => entry.State == EntityState.Added
            && entry.Entity.CompanyId == company.Id);
    private void ApplyCompanyOpeningWriteGuards()
    {
        foreach (var entry in ChangeTracker.Entries<CompanyOpeningExecution>().Where(entry => entry.State is not (EntityState.Unchanged or EntityState.Detached)))
            if (entry.State != EntityState.Added || companyOpeningProof != (entry.Entity.CompanyId, entry.Entity.ActorUserId)
                || Database.CurrentTransaction is null || !companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
                throw new InvalidOperationException("Company-opening terminals are append-only and require the exact owner proof.");
    }
}
