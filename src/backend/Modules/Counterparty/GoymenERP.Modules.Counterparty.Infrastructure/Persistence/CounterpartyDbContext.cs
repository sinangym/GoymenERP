using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

public sealed class CounterpartyDbContext(DbContextOptions<CounterpartyDbContext> options)
    : DbContext(options)
{
    public DbSet<Party> Parties => Set<Party>();

    public DbSet<CompanyPartyAccount> CompanyPartyAccounts => Set<CompanyPartyAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureParty(modelBuilder);
        ConfigureCompanyPartyAccount(modelBuilder);
        ConfigureOrganizationCompanyReference(modelBuilder);
    }

    public override int SaveChanges()
    {
        RejectPhysicalDeletes();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RejectPhysicalDeletes();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        RejectPhysicalDeletes();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        RejectPhysicalDeletes();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private static void ConfigureParty(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Party>();
        entity.ToTable("Parties", "counterparty", table =>
        {
            table.HasCheckConstraint(
                "CK_Parties_Code_Format",
                "[Code] LIKE N'CAR-%' AND ((LEN([Code]) = 10 AND TRY_CONVERT(bigint, SUBSTRING([Code], 5, 28)) BETWEEN 1 AND 999999) OR (LEN([Code]) > 10 AND SUBSTRING([Code], 5, 1) <> N'0' AND TRY_CONVERT(bigint, SUBSTRING([Code], 5, 28)) >= 1000000))");
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

    private void RejectPhysicalDeletes()
    {
        if (ChangeTracker.Entries<Party>().Any(entry => entry.State == EntityState.Deleted)
            || ChangeTracker.Entries<CompanyPartyAccount>()
                .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Cari çekirdek kayıtları fiziksel olarak silinemez; pasif/ters işlem akışı kullanılmalıdır.");
        }
    }

    private sealed class OrganizationCompanyReference
    {
        public Guid Id { get; private set; }
    }
}
