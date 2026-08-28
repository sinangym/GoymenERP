using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

public sealed class CounterpartyDbContext(
    DbContextOptions<CounterpartyDbContext> options,
    ICompanyContext companyContext)
    : DbContext(options)
{
    public DbSet<Party> Parties => Set<Party>();

    public DbSet<CompanyPartyAccount> CompanyPartyAccounts => Set<CompanyPartyAccount>();

    public DbSet<CommandExecution> CommandExecutions => Set<CommandExecution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasSequence<long>("PartyCodeSequence", "counterparty")
            .StartsAt(1L)
            .IncrementsBy(1);
        ConfigureParty(modelBuilder);
        ConfigureCompanyPartyAccount(modelBuilder);
        ConfigureOrganizationCompanyReference(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.ConfigureCommandExecution(excludeFromMigrations: true);
    }

    public override int SaveChanges()
    {
        return SaveChanges(acceptAllChangesOnSuccess: true);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private static void ConfigureParty(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Party>();
        entity.ToTable("Parties", "counterparty", table =>
        {
            table.HasCheckConstraint(
                "CK_Parties_Code_Format",
                "LEFT([Code], 4) COLLATE Latin1_General_100_BIN2 = N'CAR-' AND SUBSTRING([Code], 5, 28) COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%' AND TRY_CONVERT(bigint, SUBSTRING([Code], 5, 28)) IS NOT NULL AND ((LEN([Code]) = 10 AND TRY_CONVERT(bigint, SUBSTRING([Code], 5, 28)) BETWEEN 1 AND 999999) OR (LEN([Code]) > 10 AND SUBSTRING([Code], 5, 1) <> N'0' AND TRY_CONVERT(bigint, SUBSTRING([Code], 5, 28)) >= 1000000))");
            table.HasCheckConstraint(
                "CK_Parties_Kind",
                "[Kind] IN (N'LegalEntity', N'NaturalPerson')");
            table.HasCheckConstraint(
                "CK_Parties_Version",
                "[Version] >= 1");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code)
            .HasConversion(
                value => value.Value,
                value => PartyCode.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        entity.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        entity.Property(x => x.LegalName).HasMaxLength(250).IsRequired();
        entity.Property(x => x.TradeName).HasMaxLength(200);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasIndex(x => x.IsActive);
    }

    private static void ConfigureCompanyPartyAccount(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CompanyPartyAccount>();
        entity.ToTable("CompanyPartyAccounts", "counterparty", table =>
        {
            table.HasCheckConstraint(
                "CK_CompanyPartyAccounts_Roles",
                "[Roles] BETWEEN 1 AND 15");
            table.HasCheckConstraint(
                "CK_CompanyPartyAccounts_Version",
                "[Version] >= 1");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Roles)
            .HasConversion<int>()
            .IsRequired();
        entity.Property(x => x.ExternalIntegrationCode).HasMaxLength(128);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.CompanyId, x.PartyId }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.IsActive });
        entity.HasIndex(x => x.PartyId);

        entity.HasOne<Party>()
            .WithMany()
            .HasForeignKey(x => x.PartyId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<OrganizationCompanyReference>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureOrganizationCompanyReference(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OrganizationCompanyReference>();
        entity.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        entity.HasKey(x => x.Id);
    }

    private void ApplyWriteGuards()
    {
        foreach (var entry in ChangeTracker.Entries<Party>())
        {
            if (entry.State == EntityState.Unchanged)
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Grup seviyesindeki Party kaydı fiziksel olarak silinemez.");
            }

            if (entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException(
                    "Grup seviyesindeki Party kaydı bu dilimde doğrudan değiştirilemez.");
            }

            if (!companyContext.IsResolved)
            {
                throw new InvalidOperationException(
                    "Yeni cari oluşturmak için aktif şirket bağlamı zorunludur.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<CompanyPartyAccount>())
        {
            if (entry.State == EntityState.Unchanged)
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Cari hesap fiziksel olarak silinemez; pasif/ters işlem akışı kullanılmalıdır.");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Cari hesap CompanyId olmadan yazılamaz.");
            }

            if (entry.State == EntityState.Modified
                && entry.Property(x => x.CompanyId).IsModified)
            {
                throw new InvalidOperationException("Cari hesabın CompanyId değeri değiştirilemez.");
            }

            if (!companyContext.IsResolved
                || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException(
                    "Cari hesap yazması aktif şirket bağlamıyla eşleşmelidir.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<CommandExecution>())
        {
            if (entry.State == EntityState.Unchanged)
            {
                continue;
            }

            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Idempotency kayıtları değiştirilemez veya silinemez.");
            }

            if (!companyContext.IsResolved
                || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException(
                    "Idempotency kaydı aktif şirket bağlamıyla eşleşmelidir.");
            }
        }
    }

    private sealed class OrganizationCompanyReference
    {
        public Guid Id { get; private set; }
    }
}
