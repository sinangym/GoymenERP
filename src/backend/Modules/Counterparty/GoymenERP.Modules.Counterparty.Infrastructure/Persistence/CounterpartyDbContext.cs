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
    internal DbSet<Party> Parties => Set<Party>();

    internal DbSet<CompanyPartyAccount> CompanyPartyAccounts => Set<CompanyPartyAccount>();

    internal DbSet<PartyCodeCounter> PartyCodeCounters => Set<PartyCodeCounter>();

    internal DbSet<CommandExecution> CommandExecutions => Set<CommandExecution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureParty(modelBuilder);
        ConfigureCompanyPartyAccount(modelBuilder);
        ConfigurePartyCodeCounter(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.ConfigureCommandExecution(excludeFromMigrations: true);
        modelBuilder.Entity<CommandExecution>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
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
        entity.ToTable(
            "Parties",
            "counterparty",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Parties_Kind_Valid",
                    "[Kind] IN (1, 2)");
                table.HasCheckConstraint(
                    "CK_Parties_Version_Positive",
                    "[Version] > 0");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code)
            .HasConversion(
                code => code.Value,
                value => PartyCode.Parse(value))
            .HasMaxLength(32)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.Kind).HasConversion<int>();
        entity.Property(x => x.LegalName).HasMaxLength(250).IsRequired();
        entity.Property(x => x.TradeName).HasMaxLength(200);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasIndex(x => new { x.LegalName, x.Id });
    }

    private void ConfigureCompanyPartyAccount(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CompanyPartyAccount>();
        entity.ToTable(
            "CompanyPartyAccounts",
            "counterparty",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_CompanyPartyAccounts_CompanyId_NotEmpty",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_CompanyPartyAccounts_Roles_Valid",
                    "[Roles] BETWEEN 1 AND 15");
                table.HasCheckConstraint(
                    "CK_CompanyPartyAccounts_Version_Positive",
                    "[Version] > 0");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Roles).HasConversion<int>();
        entity.Property(x => x.ExternalIntegrationCode).HasMaxLength(128);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasOne<Party>()
            .WithMany()
            .HasForeignKey(x => x.PartyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.CompanyId, x.PartyId }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.IsActive, x.Id });
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private static void ConfigurePartyCodeCounter(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PartyCodeCounter>();
        entity.ToTable(
            "PartyCodeCounters",
            "counterparty",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_PartyCodeCounters_Singleton",
                    "[Id] = 1");
                table.HasCheckConstraint(
                    "CK_PartyCodeCounters_LastValue_NonNegative",
                    "[LastValue] >= 0");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasData(new
        {
            Id = PartyCodeCounter.SingletonId,
            LastValue = 0L,
        });
    }

    private void ApplyWriteGuards()
    {
        var now = DateTimeOffset.UtcNow;
        var addedAccounts = ChangeTracker.Entries<CompanyPartyAccount>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();

        foreach (var entry in ChangeTracker.Entries<Party>())
        {
            if (entry.State == EntityState.Unchanged)
            {
                continue;
            }

            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Grup Party kaydı cross-company write/approval politikası olmadan değiştirilemez veya silinemez.");
            }

            if (!companyContext.IsResolved)
            {
                throw new InvalidOperationException(
                    "Party oluşturma işlemi için aktif şirket bağlamı zorunludur.");
            }

            if (!addedAccounts.Any(account =>
                    account.PartyId == entry.Entity.Id
                    && account.CompanyId == companyContext.CompanyId))
            {
                throw new InvalidOperationException(
                    "Yeni Party aynı transaction içinde aktif şirkete bağlı bir CariHesap ile oluşturulmalıdır.");
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
                    "CariHesap fiziksel olarak silinemez; pasif yaşam döngüsü kullanılmalıdır.");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("CariHesap CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException(
                    "CariHesap CompanyId değeri aktif şirket bağlamıyla eşleşmelidir.");
            }

            if (entry.State == EntityState.Modified
                && entry.Property(x => x.CompanyId).IsModified)
            {
                throw new InvalidOperationException("CariHesap CompanyId değeri değiştirilemez.");
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.Touch(now);
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

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException(
                    "Idempotency kaydı aktif şirket bağlamıyla eşleşmelidir.");
            }
        }
    }
}
