using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

public sealed partial class CounterpartyDbContext
{
    internal DbSet<CounterpartyPartyIbanRow> PartyIbans => Set<CounterpartyPartyIbanRow>();
    internal DbSet<CounterpartyPartyIbanCommandRow> PartyIbanCommands => Set<CounterpartyPartyIbanCommandRow>();

    private const string NoGuid = "'00000000-0000-0000-0000-000000000000'";

    /// <summary>FNS-D121: a Turkish IBAN (TR + 24 digits; the mod-97 check is the service's), identity and text present.</summary>
    internal const string PartyIbanContentCheck = "[Id] <> " + NoGuid + " AND [CompanyId] <> " + NoGuid + " AND [PartyId] <> " + NoGuid
        + " AND LEN([Iban]) = 26 AND LEFT([Iban], 2) = 'TR' AND SUBSTRING([Iban], 3, 24) NOT LIKE '%[^0-9]%'"
        + " AND LEN([BankName]) > 0 AND LEN([AddedBy]) > 0";

    /// <summary>FNS-D121: active at version 1 without a deactivation; passive at version 2 with who/when/why.</summary>
    internal const string PartyIbanStateCheck = "([IsActive] = 1 AND [Version] = 1 AND [DeactivatedBy] IS NULL "
        + "AND [DeactivatedAtUtc] IS NULL AND [DeactivationReason] IS NULL) OR ([IsActive] = 0 AND [Version] = 2 "
        + "AND LEN([DeactivatedBy]) > 0 AND [DeactivatedAtUtc] IS NOT NULL AND LEN([DeactivationReason]) > 0)";

    private void ConfigurePartyIbans(ModelBuilder modelBuilder)
    {
        var iban = modelBuilder.Entity<CounterpartyPartyIbanRow>();
        iban.ToTable("PartyIbans", "counterparty", table =>
        {
            table.HasCheckConstraint("CK_PartyIbans_Content", PartyIbanContentCheck);
            table.HasCheckConstraint("CK_PartyIbans_State", PartyIbanStateCheck);
        });
        iban.HasKey(x => new { x.CompanyId, x.Id });
        iban.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        iban.Property(x => x.Version).IsConcurrencyToken();
        iban.Property(x => x.Iban).HasMaxLength(26).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        iban.Property(x => x.BankName).HasMaxLength(200);
        iban.Property(x => x.Description).HasMaxLength(500);
        iban.Property(x => x.AddedBy).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
        iban.Property(x => x.DeactivatedBy).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
        iban.Property(x => x.DeactivationReason).HasMaxLength(1000);
        iban.HasIndex(x => new { x.CompanyId, x.PartyId, x.Iban }).IsUnique().HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_PartyIbans_ActivePartyIban");
        iban.HasIndex(x => new { x.CompanyId, x.Iban }).HasFilter("[IsActive] = 1").HasDatabaseName("IX_PartyIbans_ActiveIban");

        var command = modelBuilder.Entity<CounterpartyPartyIbanCommandRow>();
        command.ToTable("PartyIbanCommands", "counterparty", table =>
            table.HasCheckConstraint("CK_PartyIbanCommands_Evidence", "LEN([KeyHash]) = 64 AND LEN([PayloadHash]) = 64 "
                + "AND LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND ISJSON([ResultJson]) = 1"));
        command.HasKey(x => new { x.CompanyId, x.KeyHash });
        command.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        command.Property(x => x.KeyHash).HasMaxLength(64).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        command.Property(x => x.CommandName).HasMaxLength(128).IsUnicode(false);
        command.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false);
        command.Property(x => x.ActorId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
        command.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
    }

    /// <summary>FNS-D121: no deletion; an IBAN row changes once (deactivation); command records are append-only.</summary>
    private void GuardPartyIbans()
    {
        foreach (var entry in ChangeTracker.Entries<CounterpartyPartyIbanRow>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;
            if (entry.State == EntityState.Added)
            {
                if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId || !entry.Entity.IsActive
                    || entry.Entity.Version != 1)
                    throw new InvalidOperationException("Cari IBAN kaydı yalnız etkin şirkette, etkin ve ilk sürümle eklenir.");
                continue;
            }

            var allowed = new[]
            {
                nameof(CounterpartyPartyIbanRow.IsActive), nameof(CounterpartyPartyIbanRow.Version),
                nameof(CounterpartyPartyIbanRow.DeactivatedBy), nameof(CounterpartyPartyIbanRow.DeactivatedAtUtc),
                nameof(CounterpartyPartyIbanRow.DeactivationReason),
            };
            if (entry.State != EntityState.Modified
                || entry.Property(x => x.IsActive).OriginalValue is not true || entry.Entity.IsActive
                || entry.Property(x => x.Version).OriginalValue != 1 || entry.Entity.Version != 2
                || entry.Properties.Any(property => property.IsModified && !allowed.Contains(property.Metadata.Name)))
                throw new InvalidOperationException("Cari IBAN kaydı silinmez; yalnız bir kez pasifleştirilir.");
        }

        foreach (var entry in ChangeTracker.Entries<CounterpartyPartyIbanCommandRow>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Cari IBAN komut kaydı değişmez.");
        }
    }
}
