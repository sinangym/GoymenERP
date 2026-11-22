using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// WP-A1 (AI-D24, AI-D30, AI-D34 d): the versioned AI gateway policy (insert-only, the version primary key is the
/// concurrency fence) and the append-only usage ledger with its per-month committed total. Nothing is seeded. The route
/// check restates the fixed placement rule: a cloud route never carries more than Internal data (AI-D3/AI-D4).
/// </summary>
public sealed partial class SettingsDbContext
{
    internal const string AiGatewayPolicyVersionCheck =
        "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 "
        + "AND ([MonthlyUsageCeiling] IS NULL OR [MonthlyUsageCeiling] BETWEEN 0 AND 1000000000000) "
        + "AND ([MaxUnitsPerRequest] IS NULL OR [MaxUnitsPerRequest] BETWEEN 1 AND 10000000) "
        + "AND [FeatureCount] BETWEEN 0 AND 9 AND [RouteCount] BETWEEN 0 AND 24 "
        + "AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0";
    internal const string AiGatewayFeatureCheck = "[Feature] BETWEEN 1 AND 9";
    internal const string AiGatewayModelRouteCheck =
        "[TaskKind] BETWEEN 1 AND 6 AND [DataClass] BETWEEN 1 AND 4 AND [Placement] IN (1, 2) "
        + "AND ([Placement] = 2 OR [DataClass] <= 2) AND LEN(LTRIM(RTRIM([ModelDeployment]))) > 0";
    internal const string AiBudgetMonthTotalCheck =
        "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [BudgetMonth] BETWEEN 200001 AND 210012 "
        + "AND [BudgetMonth] % 100 BETWEEN 1 AND 12 AND [CommittedUnits] >= 0";
    internal const string AiBudgetLedgerEntryCheck =
        "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EntryId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [BudgetMonth] BETWEEN 200001 AND 210012 AND [PolicyVersion] > 0 AND [Feature] BETWEEN 1 AND 9 "
        + "AND [TaskKind] BETWEEN 1 AND 6 AND [Placement] IN (1, 2) AND [ReservedUnits] > 0 "
        + "AND LEN(LTRIM(RTRIM([ModelDeployment]))) > 0 AND LEN(LTRIM(RTRIM([ActorId]))) > 0 "
        + "AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0 "
        + "AND (([State] = 1 AND [UsedUnits] IS NULL AND [SettledAtUtc] IS NULL) "
        + "OR ([State] = 2 AND [UsedUnits] IS NOT NULL AND [UsedUnits] >= 0 AND [SettledAtUtc] IS NOT NULL))";

    internal DbSet<AiGatewayPolicyVersionRow> AiGatewayPolicyVersions => Set<AiGatewayPolicyVersionRow>();

    internal DbSet<AiGatewayFeatureRow> AiGatewayFeatures => Set<AiGatewayFeatureRow>();

    internal DbSet<AiGatewayModelRouteRow> AiGatewayModelRoutes => Set<AiGatewayModelRouteRow>();

    internal DbSet<AiBudgetMonthTotalRow> AiBudgetMonthTotals => Set<AiBudgetMonthTotalRow>();

    internal DbSet<AiBudgetLedgerEntryRow> AiBudgetLedgerEntries => Set<AiBudgetLedgerEntryRow>();

    private void ConfigureAiGateway(ModelBuilder modelBuilder)
    {
        var version = modelBuilder.Entity<AiGatewayPolicyVersionRow>();
        version.ToTable("AiGatewayPolicyVersions", Schema,
            table => table.HasCheckConstraint("CK_SettingsAiGatewayPolicyVersions_Identity", AiGatewayPolicyVersionCheck));
        version.HasKey(x => new { x.CompanyId, x.Version });
        version.Property(x => x.RecordedBy).HasMaxLength(128);
        version.Property(x => x.Reason).HasMaxLength(4000);
        version.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var feature = modelBuilder.Entity<AiGatewayFeatureRow>();
        feature.ToTable("AiGatewayFeatures", Schema,
            table => table.HasCheckConstraint("CK_SettingsAiGatewayFeatures_Feature", AiGatewayFeatureCheck));
        feature.HasKey(x => new { x.CompanyId, x.Version, x.Feature });
        feature.HasOne<AiGatewayPolicyVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        feature.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var route = modelBuilder.Entity<AiGatewayModelRouteRow>();
        route.ToTable("AiGatewayModelRoutes", Schema,
            table => table.HasCheckConstraint("CK_SettingsAiGatewayModelRoutes_Route", AiGatewayModelRouteCheck));
        route.HasKey(x => new { x.CompanyId, x.Version, x.TaskKind, x.DataClass });
        route.Property(x => x.ModelDeployment).HasMaxLength(128);
        route.HasOne<AiGatewayPolicyVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        route.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var total = modelBuilder.Entity<AiBudgetMonthTotalRow>();
        total.ToTable("AiBudgetMonthTotals", Schema,
            table => table.HasCheckConstraint("CK_SettingsAiBudgetMonthTotals_Total", AiBudgetMonthTotalCheck));
        total.HasKey(x => new { x.CompanyId, x.BudgetMonth });
        total.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var entry = modelBuilder.Entity<AiBudgetLedgerEntryRow>();
        entry.ToTable("AiBudgetLedgerEntries", Schema,
            table => table.HasCheckConstraint("CK_SettingsAiBudgetLedgerEntries_Entry", AiBudgetLedgerEntryCheck));
        entry.HasKey(x => new { x.CompanyId, x.EntryId });
        entry.HasIndex(x => new { x.CompanyId, x.BudgetMonth });
        entry.Property(x => x.ModelDeployment).HasMaxLength(128);
        entry.Property(x => x.ActorId).HasMaxLength(128);
        entry.Property(x => x.CorrelationId).HasMaxLength(128);
        entry.HasOne<AiBudgetMonthTotalRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BudgetMonth }).OnDelete(DeleteBehavior.Restrict);
        entry.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
