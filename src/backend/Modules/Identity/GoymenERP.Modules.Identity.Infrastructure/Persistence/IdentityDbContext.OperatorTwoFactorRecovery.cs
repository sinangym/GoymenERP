using GoymenERP.Modules.Identity.Domain.Credentials;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence;

public sealed partial class IdentityDbContext
{
    public DbSet<LocalTwoFactorOperatorRecoveryAttempt> LocalTwoFactorOperatorRecoveryAttempts => Set<LocalTwoFactorOperatorRecoveryAttempt>();
    internal const string OperatorTwoFactorRecoveryCheck =
        "[Id] <> '00000000-0000-0000-0000-000000000000' AND [TargetUserId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [ConfirmedUserVersion] >= 1 AND [ConfirmedCredentialVersion] >= 1 AND [ConfirmedSecretVersion] >= 0 "
        + "AND [ResultCredentialVersion] >= [ConfirmedCredentialVersion] AND [ResultSecretVersion] >= 0 AND [Outcome] IN (1,2,3) "
        + "AND LEN([Reason]) BETWEEN 1 AND 1000 AND LEN([OriginalCorrelationId]) BETWEEN 1 AND 128 "
        + "AND LEN([KeyHash]) = 64 AND LEN([RequestHash]) = 64 AND LEN([TargetInputHash]) = 64 AND LEN([OperatorHash]) = 64 "
        + "AND DATEPART(TZOFFSET, [RecordedAtUtc]) = 0 "
        + "AND [KeyHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%' "
        + "AND [RequestHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%' "
        + "AND [TargetInputHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%' "
        + "AND [OperatorHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%' "
        + "AND (([Outcome] = 1 AND [ConfirmedTwoFactorEnabled] = 1 AND [ConfirmedSecretVersion] >= 1 "
        + "AND CONVERT(decimal(20,0),[ResultCredentialVersion]) - CONVERT(decimal(20,0),[ConfirmedCredentialVersion]) = 1 "
        + "AND CONVERT(decimal(20,0),[ResultSecretVersion]) - CONVERT(decimal(20,0),[ConfirmedSecretVersion]) = 1) "
        + "OR ([Outcome] = 2 AND [ConfirmedTwoFactorEnabled] = 0 AND [ResultCredentialVersion] = [ConfirmedCredentialVersion] "
        + "AND [ResultSecretVersion] = [ConfirmedSecretVersion]) OR [Outcome] = 3)";

    private static void ConfigureOperatorTwoFactorRecovery(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LocalTwoFactorOperatorRecoveryAttempt>();
        entity.ToTable("LocalTwoFactorOperatorRecoveryAttempts", "identity",
            table => table.HasCheckConstraint("CK_LocalTwoFactorOperatorRecoveryAttempts_State", OperatorTwoFactorRecoveryCheck));
        entity.HasKey(row => row.Id); entity.Property(row => row.Id).ValueGeneratedNever();
        entity.HasIndex(row => row.KeyHash).IsUnique().HasDatabaseName("UX_LocalTwoFactorOperatorRecoveryAttempts_KeyHash");
        entity.HasIndex(row => new { row.TargetUserId, row.RecordedAtUtc, row.Id });
        foreach (var name in new[] { nameof(LocalTwoFactorOperatorRecoveryAttempt.KeyHash), nameof(LocalTwoFactorOperatorRecoveryAttempt.RequestHash),
            nameof(LocalTwoFactorOperatorRecoveryAttempt.TargetInputHash), nameof(LocalTwoFactorOperatorRecoveryAttempt.OperatorHash) })
            entity.Property<string>(name).HasColumnType("char(64)").UseCollation(BinaryCollation).IsRequired();
        entity.Property(row => row.Reason).HasMaxLength(LocalTwoFactorOperatorRecoveryAttempt.ReasonMaximumLength).IsRequired();
        entity.Property(row => row.OriginalCorrelationId).HasMaxLength(LocalTwoFactorOperatorRecoveryAttempt.CorrelationMaximumLength).IsRequired();
        entity.HasOne<LocalCredential>().WithMany().HasForeignKey(row => row.TargetUserId).OnDelete(DeleteBehavior.Restrict);
    }
    private void ApplyOperatorTwoFactorRecoveryWriteGuards()
    {
        foreach (var entry in ChangeTracker.Entries<LocalTwoFactorOperatorRecoveryAttempt>())
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Operator recovery attempt history is append-only.");
    }
}
