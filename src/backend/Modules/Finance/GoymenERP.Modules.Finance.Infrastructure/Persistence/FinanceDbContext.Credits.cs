using Microsoft.EntityFrameworkCore;
namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

public sealed partial class FinanceDbContext
{
    internal DbSet<FinanceCreditMovementRow> CreditMovements => Set<FinanceCreditMovementRow>();
    internal DbSet<FinanceCreditMovementCommandRow> CreditMovementCommands => Set<FinanceCreditMovementCommandRow>();
    internal DbSet<FinanceOutboxRow> OutboxMessages => Set<FinanceOutboxRow>();

    private void ConfigureCreditMovements(ModelBuilder model)
    {
        var movement = model.Entity<FinanceCreditMovementRow>();
        movement.ToTable("CreditMovements", Schema, table =>
        {
            table.HasCheckConstraint("CK_CreditMovements_Source", "ISJSON([SourceJson]) = 1 AND ISJSON([MoneyProfileJson]) = 1 AND LEN([SourceIdentityHash]) = 64");
            table.HasCheckConstraint("CK_CreditMovements_Kind", "[Kind] IN (1,2,3,4)");
            table.HasCheckConstraint("CK_CreditMovements_Decision", "([Status] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [Version] = 1) OR ([Status] IN (2,3) AND [DecidedBy] IS NOT NULL AND [DecidedBy] <> [ProposedBy] AND [DecidedAtUtc] IS NOT NULL AND [Version] >= 2 AND (([Status] = 2 AND [RejectionReason] IS NULL) OR ([Status] = 3 AND LEN([RejectionReason]) > 0)))");
            table.HasCheckConstraint("CK_CreditMovements_Bank", "([BankAccountId] IS NULL AND [BankAmountText] IS NULL) OR ([BankAccountId] IS NOT NULL AND [BankAmountText] IS NOT NULL AND [Kind] IN (1,2))");
            table.HasCheckConstraint("CK_CreditMovements_Opening", "([Kind] = 4 AND [BankAccountId] IS NULL) OR ([Kind] <> 4 AND [ReplacesOpeningMovementId] IS NULL AND [OpeningPeriodVersion] IS NULL AND [ReversedByMovementId] IS NULL)");
        });
        movement.HasKey(x => new { x.CompanyId, x.Id });
        movement.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        movement.Property(x => x.Version).IsConcurrencyToken();
        movement.Property(x => x.ProposedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        movement.Property(x => x.DecidedBy).HasMaxLength(128).UseCollation(IdentityCollation);
        movement.Property(x => x.RejectionReason).HasMaxLength(1000);
        movement.Property(x => x.SourceIdentityHash).HasMaxLength(64).IsUnicode(false);
        movement.Property(x => x.BankAmountText).HasMaxLength(128).IsUnicode(false);
        movement.Property(x => x.OpeningPeriodVersion).HasMaxLength(64);
        movement.HasIndex(x => new { x.CompanyId, x.CreditAccountId, x.SourceIdentityHash }).IsUnique().HasFilter("[Status] IN (1,2)");
        movement.HasIndex(x => new { x.CompanyId, x.ReplacesOpeningMovementId }).IsUnique().HasFilter("[ReplacesOpeningMovementId] IS NOT NULL AND [Status] IN (1,2)");
        movement.HasIndex(x => new { x.CompanyId, x.CreditAccountId }).IsUnique()
            .HasFilter("[Kind] = 4 AND [ReplacesOpeningMovementId] IS NULL AND [Status] IN (1,2)");
        movement.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.Status });
        movement.HasOne<CreditAccountRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CreditAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        movement.HasOne<BankAccountRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BankAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        movement.HasOne<FinanceCreditMovementRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ReplacesOpeningMovementId }).OnDelete(DeleteBehavior.Restrict);
        var command = model.Entity<FinanceCreditMovementCommandRow>();
        command.ToTable("CreditMovementCommands", Schema, table => table.HasCheckConstraint("CK_CreditMovementCommands_Json", "ISJSON([ResultJson]) = 1 AND LEN([KeyHash]) = 64 AND LEN([PayloadHash]) = 64"));
        command.HasKey(x => new { x.CompanyId, x.Command, x.KeyHash });
        command.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        command.Property(x => x.Command).HasMaxLength(100); command.Property(x => x.KeyHash).HasMaxLength(64).IsUnicode(false);
        command.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false); command.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        var outbox = model.Entity<FinanceOutboxRow>();
        outbox.ToTable("OutboxMessages", Schema, table => table.HasCheckConstraint("CK_OutboxMessages_Source", "[SchemaVersion] > 0 AND [AggregateVersion] > 0 AND ISJSON([PayloadJson]) = 1"));
        outbox.HasKey(x => new { x.CompanyId, x.Id });
        outbox.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        outbox.Property(x => x.Position).IsRowVersion().HasConversion<byte[]>(); outbox.HasIndex(x => x.Position);
        outbox.Property(x => x.EventType).HasMaxLength(160); outbox.Property(x => x.CorrelationId).HasMaxLength(128); outbox.Property(x => x.CausationId).HasMaxLength(128);
        outbox.HasIndex(x => new { x.CompanyId, x.EventType, x.AggregateId, x.AggregateVersion }).IsUnique();
    }

    private bool CreditMovementChangeAllowed(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IFinanceTenantRow> entry, FinanceCreditMovementRow row)
    {
        var previousStatus = (int)entry.OriginalValues[nameof(row.Status)]!;
        var previousVersion = (long)entry.OriginalValues[nameof(row.Version)]!;
        var changed = entry.Properties.Where(x => x.IsModified).Select(x => x.Metadata.Name).ToHashSet();
        if (row.Version != previousVersion + 1) return false;
        if (previousStatus == 1 && row.Status is 2 or 3)
            return !string.IsNullOrWhiteSpace(row.DecidedBy) && row.DecidedBy != row.ProposedBy && row.DecidedAtUtc is not null
                && (row.Status == 2 ? row.RejectionReason is null : !string.IsNullOrWhiteSpace(row.RejectionReason))
                && changed.IsSubsetOf([nameof(row.Version), nameof(row.Status), nameof(row.DecidedBy), nameof(row.DecidedAtUtc), nameof(row.RejectionReason)]);
        return previousStatus == 2 && row.Status == 2 && row.Kind == 4 && row.ReversedByMovementId is not null
            && entry.OriginalValues[nameof(row.ReversedByMovementId)] is null
            && changed.SetEquals([nameof(row.Version), nameof(row.ReversedByMovementId)]);
    }
}
