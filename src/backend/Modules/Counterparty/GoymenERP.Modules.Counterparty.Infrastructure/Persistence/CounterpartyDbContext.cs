using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
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

    public DbSet<PartyTaxIdentityChangeRequest> PartyTaxIdentityChangeRequests =>
        Set<PartyTaxIdentityChangeRequest>();

    public DbSet<CommandExecution> CommandExecutions => Set<CommandExecution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasSequence<long>("PartyCodeSequence", "counterparty")
            .StartsAt(1L)
            .IncrementsBy(1);
        ConfigureParty(modelBuilder);
        ConfigureCompanyPartyAccount(modelBuilder);
        ConfigurePartyTaxIdentityChangeRequest(modelBuilder);
        ConfigureOrganizationCompanyReference(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
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
            table.HasCheckConstraint(
                "CK_Parties_TaxIdentityNumber",
                TaxIdentityNumberFormatSql("TaxIdentityNumber", nullable: true));
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
        entity.Property(x => x.TaxIdentityNumber).HasMaxLength(11).IsUnicode(false);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasIndex(x => x.IsActive);
        // AT-009: the duplicate warning's lookup. NOT unique: the same number on two cards is a warning (branch, same
        // firm), never a refusal (user decision 2026-09-30).
        entity.HasIndex(x => x.TaxIdentityNumber)
            .HasFilter("[TaxIdentityNumber] IS NOT NULL");
    }

    private void ConfigurePartyTaxIdentityChangeRequest(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PartyTaxIdentityChangeRequest>();
        entity.ToTable("PartyTaxIdentityChangeRequests", "counterparty", table =>
        {
            table.HasCheckConstraint(
                "CK_PartyTaxIdentityChangeRequests_Status",
                "[Status] IN (N'Pending', N'Approved', N'Rejected')");
            table.HasCheckConstraint(
                "CK_PartyTaxIdentityChangeRequests_Current",
                TaxIdentityNumberFormatSql("CurrentTaxIdentityNumber", nullable: false));
            table.HasCheckConstraint(
                "CK_PartyTaxIdentityChangeRequests_Proposed",
                TaxIdentityNumberFormatSql("ProposedTaxIdentityNumber", nullable: false)
                + " AND [ProposedTaxIdentityNumber] <> [CurrentTaxIdentityNumber]");
            // Maker-checker in the database too: a decision names its decider, who is never the requester.
            table.HasCheckConstraint(
                "CK_PartyTaxIdentityChangeRequests_Decision",
                "([Status] = N'Pending' AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionNote] IS NULL)"
                + " OR ([Status] <> N'Pending' AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL"
                + " AND [DecidedBy] <> [RequestedBy])");
            table.HasCheckConstraint(
                "CK_PartyTaxIdentityChangeRequests_Version",
                "[Version] >= 1 AND [PartyVersionAtRequest] >= 1");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.CurrentTaxIdentityNumber).HasMaxLength(11).IsUnicode(false).IsRequired();
        entity.Property(x => x.ProposedTaxIdentityNumber).HasMaxLength(11).IsUnicode(false).IsRequired();
        entity.Property(x => x.Reason).HasMaxLength(PartyTaxIdentityChangeRequest.ReasonMaximumLength).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(x => x.RequestedBy)
            .HasMaxLength(PartyTaxIdentityChangeRequest.ActorMaximumLength).IsRequired();
        entity.Property(x => x.DecidedBy).HasMaxLength(PartyTaxIdentityChangeRequest.ActorMaximumLength);
        entity.Property(x => x.DecisionNote).HasMaxLength(PartyTaxIdentityChangeRequest.NoteMaximumLength);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        // One pending change per party per company; a concurrent second request loses on this index.
        entity.HasIndex(x => new { x.CompanyId, x.PartyId })
            .IsUnique()
            .HasFilter("[Status] = N'Pending'")
            .HasDatabaseName("UX_PartyTaxIdentityChangeRequests_Pending");
        entity.HasIndex(x => new { x.CompanyId, x.PartyId, x.RequestedAtUtc });

        entity.HasOne<Party>()
            .WithMany()
            .HasForeignKey(x => x.PartyId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<OrganizationCompanyReference>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>10 (VKN) or 11 (TCKN) ASCII digits; the check digits are the domain's (TaxIdentityNumber).</summary>
    private static string TaxIdentityNumberFormatSql(string column, bool nullable)
    {
        var format = $"LEN([{column}]) IN (10, 11) AND DATALENGTH([{column}]) = LEN([{column}])"
            + $" AND [{column}] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%'";
        return nullable ? $"[{column}] IS NULL OR ({format})" : format;
    }

    private static void ConfigureCompanyPartyAccount(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CompanyPartyAccount>();
        entity.ToTable("CompanyPartyAccounts", "counterparty", table =>
        {
            table.HasCheckConstraint(
                "CK_CompanyPartyAccounts_ContactInformationJson",
                "[ContactInformationJson] IS NULL OR ISJSON([ContactInformationJson]) = 1");
            table.HasCheckConstraint(
                "CK_CompanyPartyAccounts_Roles",
                "[Roles] BETWEEN 0 AND 15");
            table.HasCheckConstraint(
                "CK_CompanyPartyAccounts_Version",
                "[Version] >= 1");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Roles)
            .HasConversion<int>()
            .IsRequired();
        entity.Property(x => x.ContactInformationJson).HasColumnType("nvarchar(max)");
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
        CounterpartyAuditEventTenantGuard.Validate(this, companyContext);

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

            if (entry.State == EntityState.Modified && !IsTaxIdentityOnlyPartyChange(entry))
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

        foreach (var entry in ChangeTracker.Entries<PartyTaxIdentityChangeRequest>())
        {
            if (entry.State == EntityState.Unchanged)
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Vergi/kimlik değişiklik talebi silinemez; karar geçmişi korunur.");
            }

            if (entry.State == EntityState.Modified
                && ImmutableChangeRequestProperties.Any(name => entry.Property(name).IsModified))
            {
                throw new InvalidOperationException(
                    "Vergi/kimlik değişiklik talebinin kimlik, şirket, cari, numara ve talep eden bilgisi değiştirilemez.");
            }

            if (!companyContext.IsResolved
                || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException(
                    "Vergi/kimlik değişiklik talebi aktif şirket bağlamıyla eşleşmelidir.");
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

    private static readonly string[] MutableTaxIdentityPartyProperties =
    [
        nameof(Party.TaxIdentityNumber),
        nameof(Party.Version),
        nameof(Party.UpdatedAtUtc),
        nameof(Party.RowVersion),
    ];

    private static readonly string[] ImmutableChangeRequestProperties =
    [
        nameof(PartyTaxIdentityChangeRequest.Id),
        nameof(PartyTaxIdentityChangeRequest.CompanyId),
        nameof(PartyTaxIdentityChangeRequest.PartyId),
        nameof(PartyTaxIdentityChangeRequest.CurrentTaxIdentityNumber),
        nameof(PartyTaxIdentityChangeRequest.ProposedTaxIdentityNumber),
        nameof(PartyTaxIdentityChangeRequest.PartyVersionAtRequest),
        nameof(PartyTaxIdentityChangeRequest.Reason),
        nameof(PartyTaxIdentityChangeRequest.RequestedBy),
        nameof(PartyTaxIdentityChangeRequest.RequestedAtUtc),
    ];

    /// <summary>
    /// AT-009: the one Party update this module writes — its VKN/TCKN with the version and timestamp it moves. Every
    /// other Party column stays read-only here, exactly as before.
    /// </summary>
    private static bool IsTaxIdentityOnlyPartyChange(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<Party> entry)
    {
        var modified = entry.Properties.Where(property => property.IsModified)
            .Select(property => property.Metadata.Name)
            .ToArray();
        return modified.Contains(nameof(Party.TaxIdentityNumber), StringComparer.Ordinal)
            && modified.All(name => MutableTaxIdentityPartyProperties.Contains(name, StringComparer.Ordinal));
    }

    private sealed class OrganizationCompanyReference
    {
        public Guid Id { get; private set; }
    }
}
