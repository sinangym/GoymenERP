using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// URT-D89: the persistent work center capacity versions of a company. The table carries the company inside its key and the
/// company query filter; the rows are insert-only and the primary key on (company, work center, version) is the concurrency
/// fence (two writers of the same next version of one work center: one wins, the other is a version conflict). The checks hold
/// the published buffer units and the positive values; the database defaults nothing and nothing is seeded (no capacity is
/// the absence of a row, no buffer is the null pair).
/// </summary>
public sealed partial class SettingsDbContext
{
    internal const string WorkCenterCapacityCheck = "[InputCapacityKgPerHour] > 0";

    internal const string WorkCenterSafetyBufferCheck =
        "([SafetyBufferValue] IS NULL AND [SafetyBufferUnit] IS NULL) OR "
        + "([SafetyBufferValue] > 0 AND [SafetyBufferUnit] IN ('Hours','Days'))";

    internal DbSet<WorkCenterCapacityVersionRow> WorkCenterCapacityVersions => Set<WorkCenterCapacityVersionRow>();

    private void ConfigureWorkCenterCapacities(ModelBuilder modelBuilder)
    {
        const string emptyGuid = "'00000000-0000-0000-0000-000000000000'";

        var capacity = modelBuilder.Entity<WorkCenterCapacityVersionRow>();
        capacity.ToTable("WorkCenterCapacityVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsWorkCenterCapacityVersions_Identity",
                $"[CompanyId] <> {emptyGuid} AND [WorkCenterId] <> {emptyGuid} AND [ProfileId] <> {emptyGuid} AND [Version] > 0");
            table.HasCheckConstraint("CK_SettingsWorkCenterCapacityVersions_Capacity", WorkCenterCapacityCheck);
            table.HasCheckConstraint("CK_SettingsWorkCenterCapacityVersions_SafetyBuffer", WorkCenterSafetyBufferCheck);
            table.HasCheckConstraint("CK_SettingsWorkCenterCapacityVersions_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        capacity.HasKey(x => new { x.CompanyId, x.WorkCenterId, x.Version });
        capacity.Property(x => x.Version).ValueGeneratedNever();
        capacity.HasIndex(x => x.ProfileId).IsUnique();
        capacity.Property(x => x.InputCapacityKgPerHour).HasPrecision(18, 6);
        capacity.Property(x => x.SafetyBufferValue).HasPrecision(9, 3);
        capacity.Property(x => x.SafetyBufferUnit).HasMaxLength(16).IsUnicode(false);
        capacity.Property(x => x.RecordedBy).HasMaxLength(128);
        capacity.Property(x => x.Reason).HasMaxLength(WorkCenterCapacityInputLimitsV1.ReasonMaximumLength);
        capacity.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
