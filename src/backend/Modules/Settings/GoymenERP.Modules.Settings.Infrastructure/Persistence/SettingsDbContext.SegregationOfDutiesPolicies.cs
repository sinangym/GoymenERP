using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// F-024 (NS-01): the persistent segregation of duties versions of a company. The table carries the company inside its key
/// and the company query filter; the rows are insert-only and the primary key on the version number is the concurrency fence
/// (two writers of the same next version: one wins, the other is a version conflict). The database defaults nothing: the
/// decided default (the preparer may not approve) is the absence of a row.
/// </summary>
public sealed partial class SettingsDbContext
{
    internal DbSet<SegregationOfDutiesPolicyVersionRow> SegregationOfDutiesPolicyVersions =>
        Set<SegregationOfDutiesPolicyVersionRow>();

    private void ConfigureSegregationOfDutiesPolicies(ModelBuilder modelBuilder)
    {
        const string emptyGuid = "'00000000-0000-0000-0000-000000000000'";

        var policy = modelBuilder.Entity<SegregationOfDutiesPolicyVersionRow>();
        policy.ToTable("SegregationOfDutiesPolicyVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsSegregationOfDutiesPolicyVersions_Identity",
                $"[CompanyId] <> {emptyGuid} AND [Version] > 0");
            table.HasCheckConstraint("CK_SettingsSegregationOfDutiesPolicyVersions_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        policy.HasKey(x => new { x.CompanyId, x.Version });
        policy.Property(x => x.RecordedBy).HasMaxLength(128);
        policy.Property(x => x.Reason).HasMaxLength(4000);
        policy.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
