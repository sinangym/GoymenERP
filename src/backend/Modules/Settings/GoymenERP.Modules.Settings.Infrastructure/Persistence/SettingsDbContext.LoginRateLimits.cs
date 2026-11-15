using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// KIM-D34: the persistent group-wide login attempt limit versions. One record for the whole group, so the table has no company
/// in its key and no company query filter (the Counterparty e-document profile precedent of a group-wide record); the rows are
/// insert-only and the primary key on the version number is the concurrency fence (two writers of the same next version: one
/// wins, the other is a version conflict). The checks hold the published partition names and the technical bounds; no value is
/// defaulted by the database and nothing is seeded.
/// </summary>
public sealed partial class SettingsDbContext
{
    internal const string LoginRateLimitPartitionCheck = "[PartitionBy] IN ('IpAddress','IpAddressAndUserName')";

    internal const string LoginRateLimitBoundsCheck =
        "[PermitLimit] BETWEEN 1 AND 1000000 AND [WindowSeconds] BETWEEN 1 AND 86400";

    internal DbSet<LoginRateLimitVersionRow> LoginRateLimitVersions => Set<LoginRateLimitVersionRow>();

    private static void ConfigureLoginRateLimits(ModelBuilder modelBuilder)
    {
        const string emptyGuid = "'00000000-0000-0000-0000-000000000000'";

        var limit = modelBuilder.Entity<LoginRateLimitVersionRow>();
        limit.ToTable("LoginRateLimitVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsLoginRateLimitVersions_Identity",
                $"[ProfileId] <> {emptyGuid} AND [RecordedInCompanyId] <> {emptyGuid} AND [Version] > 0");
            table.HasCheckConstraint("CK_SettingsLoginRateLimitVersions_Bounds", LoginRateLimitBoundsCheck);
            table.HasCheckConstraint("CK_SettingsLoginRateLimitVersions_Partition", LoginRateLimitPartitionCheck);
            table.HasCheckConstraint("CK_SettingsLoginRateLimitVersions_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        limit.HasKey(x => x.Version);
        limit.Property(x => x.Version).ValueGeneratedNever();
        limit.HasIndex(x => x.ProfileId).IsUnique();
        limit.Property(x => x.PartitionBy).HasMaxLength(32).IsUnicode(false);
        limit.Property(x => x.RecordedBy).HasMaxLength(128);
        limit.Property(x => x.Reason).HasMaxLength(4000);
    }
}
