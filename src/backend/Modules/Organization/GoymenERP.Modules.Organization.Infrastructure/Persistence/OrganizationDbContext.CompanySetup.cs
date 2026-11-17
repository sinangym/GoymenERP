using GoymenERP.Modules.Organization.Domain.CompanySetup;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

public sealed partial class OrganizationDbContext
{
    internal const string CompanyLifecycleCheck = "[LifecycleState] IN (N'Active', N'SetupPending') AND ([OpenedByUserId] IS NULL OR [OpenedByUserId] <> '00000000-0000-0000-0000-000000000000') AND ([LifecycleState] <> N'SetupPending' OR [OpenedByUserId] IS NOT NULL)";
    internal const string CompanySetupReceiptIdentityCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND LEN([PayloadHash]) = 64 AND [PayloadHash] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%'";
    public DbSet<CompanySetupInboxReceipt> CompanySetupInboxReceipts => Set<CompanySetupInboxReceipt>();
    public DbSet<OrganizationCompanySetupOutboxMessage> CompanySetupOutboxMessages => Set<OrganizationCompanySetupOutboxMessage>();
    private void ConfigureCompanySetup(ModelBuilder modelBuilder)
    {
        var receipt = modelBuilder.Entity<CompanySetupInboxReceipt>();
        receipt.ToTable("CompanySetupInboxReceipts", "organization", table => table.HasCheckConstraint("CK_CompanySetupInbox_Identity",
            CompanySetupReceiptIdentityCheck));
        receipt.HasKey(x => new { x.CompanyId, x.EventId });
        receipt.Property(x => x.PayloadHash).HasMaxLength(64).IsFixedLength().IsRequired();
        receipt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        var message = modelBuilder.Entity<OrganizationCompanySetupOutboxMessage>();
        message.ToTable("CompanySetupOutboxMessages", "organization", table =>
            table.HasCheckConstraint("CK_CompanySetupOutbox_Payload", "ISJSON([PayloadJson]) = 1"));
        message.HasKey(x => x.Id);
        message.Property(x => x.Id).ValueGeneratedNever();
        message.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        message.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        message.Property(x => x.CausationId).HasMaxLength(128);
        message.Property(x => x.Position).IsRowVersion().HasConversion<byte[]>();
        message.HasIndex(x => new { x.CompanyId, x.Position });
        message.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }
    private void ApplyCompanySetupWriteGuards()
    {
        ApplyCompanyOpeningWriteGuards();
        foreach (var entry in ChangeTracker.Entries<GoymenERP.Modules.Organization.Domain.Companies.Company>().Where(x => x.State == EntityState.Modified))
        {
            if (entry.Property(x => x.OpenedByUserId).IsModified)
                throw new InvalidOperationException("Company opener identity is immutable.");
            if (entry.Property(x => x.LifecycleState).IsModified
                && (entry.Property(x => x.LifecycleState).OriginalValue != "SetupPending" || entry.Entity.LifecycleState != "Active"
                    || entry.Entity.Version != entry.Property(x => x.Version).OriginalValue + 1))
                throw new InvalidOperationException("Company lifecycle only completes setup once.");
        }
        foreach (var entry in ChangeTracker.Entries<CompanySetupInboxReceipt>().Where(x => x.State is not (EntityState.Unchanged or EntityState.Detached)))
        {
            if (entry.State != EntityState.Added || !companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
                throw new InvalidOperationException("Company setup inbox is immutable and company fenced.");
        }
        foreach (var entry in ChangeTracker.Entries<OrganizationCompanySetupOutboxMessage>().Where(x => x.State is not (EntityState.Unchanged or EntityState.Detached)))
        {
            if (entry.State != EntityState.Added || !companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
                throw new InvalidOperationException("Company setup outbox is immutable and company fenced.");
        }
    }
}
