using GoymenERP.Modules.Identity.Domain.Credentials;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence;

public sealed partial class IdentityDbContext
{
    public DbSet<LocalTwoFactorSecret> LocalTwoFactorSecrets => Set<LocalTwoFactorSecret>();
    internal const string TwoFactorStateCheck = "[UserId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND ([LastAcceptedStep] IS NULL OR [LastAcceptedStep] >= 0) AND ISJSON([RecoveryHashesJson]) = 1";
    private static void ConfigureTwoFactor(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalCredentialCommand>().Property(row => row.OriginalCorrelationId).HasMaxLength(128);
        var entity = modelBuilder.Entity<LocalTwoFactorSecret>();
        entity.ToTable("LocalTwoFactorSecrets", "identity", table => table.HasCheckConstraint("CK_LocalTwoFactorSecrets_State", TwoFactorStateCheck));
        entity.HasKey(row => row.UserId);
        entity.Property(row => row.ProtectedSecret).HasMaxLength(4000).IsRequired();
        entity.Property(row => row.RecoveryHashesJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(row => row.Version).IsConcurrencyToken();
        entity.Ignore(row => row.RecoveryCodeCount);
        entity.HasOne<LocalCredential>().WithOne().HasForeignKey<LocalTwoFactorSecret>(row => row.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
