using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Domain.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using static GoymenERP.SecurityEvents.UnitTests.SecurityEventFixtures;

namespace GoymenERP.SecurityEvents.UnitTests;

public sealed class SecurityEventsPersistenceModelTests
{
    [Fact]
    public void Security_events_table_lives_in_the_security_schema()
    {
        using var context = Context();
        var entity = DesignEntity(context);

        Assert.Equal("security", entity.GetSchema());
        Assert.Equal("SecurityEvents", entity.GetTableName());
        Assert.Equal("security", SecurityEventModelBuilderExtensions.Schema);
    }

    [Fact]
    public void Check_constraints_pin_scope_legal_hold_codes_retention_and_aggregation()
    {
        using var context = Context();
        var checks = DesignEntity(context).GetCheckConstraints().ToDictionary(c => c.Name!, c => c.Sql);

        Assert.Equal(
            "([ScopeKind] = 'Company' AND [CompanyId] IS NOT NULL) OR ([ScopeKind] = 'Platform' AND [CompanyId] IS NULL)",
            checks["CK_SecurityEvents_ScopeCompany"]);
        Assert.Equal("[LegalHold] = 0", checks["CK_SecurityEvents_LegalHold"]);
        Assert.Equal("[RetainUntilUtc] > [OccurredAtUtc]", checks["CK_SecurityEvents_RetainUntil"]);
        Assert.Equal("[OccurrenceCount] >= 1", checks["CK_SecurityEvents_OccurrenceCount"]);
        Assert.Equal(
            "([AggregationWindowStartUtc] IS NULL AND [AggregationWindowEndUtc] IS NULL AND [OccurrenceCount] = 1) OR ([AggregationWindowStartUtc] IS NOT NULL AND [AggregationWindowEndUtc] IS NOT NULL AND [AggregationWindowStartUtc] <= [AggregationWindowEndUtc])",
            checks["CK_SecurityEvents_AggregationWindow"]);
        Assert.Equal(
            "([EventType] = 'ai.tool.called' AND [Phase] IN ('Started', 'Completed')) OR ([EventType] <> 'ai.tool.called' AND [Phase] = 'Single')",
            checks["CK_SecurityEvents_PhaseEventType"]);
        Assert.All(SecurityEventTypesV1.All, type => Assert.Contains($"'{type}'", checks["CK_SecurityEvents_EventType"], StringComparison.Ordinal));
        Assert.All(SecurityEventPhasesV1.All, phase => Assert.Contains($"'{phase}'", checks["CK_SecurityEvents_Phase"], StringComparison.Ordinal));
        Assert.All(SecurityEventOutcomesV1.All, outcome => Assert.Contains($"'{outcome}'", checks["CK_SecurityEvents_Outcome"], StringComparison.Ordinal));
        Assert.Equal("[ActorType] IN ('User', 'System', 'Ai', 'Anonymous')", checks["CK_SecurityEvents_ActorType"]);
        Assert.Equal(
            "([ActorType] = 'Anonymous' AND [ActorId] IS NULL) OR ([ActorType] <> 'Anonymous' AND [ActorId] IS NOT NULL)",
            checks["CK_SecurityEvents_ActorId"]);
        Assert.Equal("[Severity] IN ('Info', 'Warning', 'High')", checks["CK_SecurityEvents_Severity"]);
        Assert.Equal(
            "[SourceChannel] IN ('Web', 'Mobile', 'Import', 'Integration', 'AiAssisted', 'System')",
            checks["CK_SecurityEvents_SourceChannel"]);
        Assert.Equal(
            "[AiRiskClass] IS NULL OR [AiRiskClass] IN ('Read', 'Draft', 'Simulate', 'Write')",
            checks["CK_SecurityEvents_AiRiskClass"]);
        Assert.Equal(
            "[AiDataClass] IS NULL OR [AiDataClass] IN ('Public', 'Internal', 'Confidential', 'StrictlyConfidential')",
            checks["CK_SecurityEvents_AiDataClass"]);
        Assert.StartsWith("[ReasonCode] IS NULL OR [ReasonCode] IN (", checks["CK_SecurityEvents_ReasonCode"], StringComparison.Ordinal);
        Assert.All(SecurityEventReasonCodesV1.All, code => Assert.Contains($"'{code}'", checks["CK_SecurityEvents_ReasonCode"], StringComparison.Ordinal));
        Assert.Equal(SecurityEventModelBuilderExtensions.RateLimitSummaryCheck, checks["CK_SecurityEvents_RateLimitSummary"]);
        Assert.Equal(17, checks.Count);
    }

    [Fact]
    public void Code_check_migration_is_pure_ddl_and_its_down_only_loosens_checks()
    {
        var migration = new HardenSecurityEventCodeChecks();

        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal(7, migration.UpOperations.OfType<AddCheckConstraintOperation>().Count());
        Assert.All(migration.UpOperations, op => Assert.IsType<AddCheckConstraintOperation>(op));
        // Down drops only CHECK constraints; no row can lose data, so no forward-fix guard is needed.
        Assert.All(migration.DownOperations, op => Assert.IsType<DropCheckConstraintOperation>(op));
        Assert.Equal(7, migration.DownOperations.Count);
    }

    [Fact]
    public void Migrations_are_ordered_and_the_snapshot_matches_the_model()
    {
        using var context = Context();
        Assert.Equal(
            [
                "20261111205852_InitialSecurityEvents",
                "20261111222917_HardenSecurityEventCodeChecks",
                "20261115163955_AddLoginRateLimitedSecurityEvent",
                "20261116100000_AddFounderRecoverySecurityEvents",
            ],
            context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Rate_limit_summary_check_binds_the_columns_to_the_platform_anonymous_rate_limited_row()
    {
        var check = SecurityEventModelBuilderExtensions.RateLimitSummaryCheck;

        Assert.Contains("[EventType] = 'auth.login.rate_limited' AND [ScopeKind] = 'Platform' AND [ActorType] = 'Anonymous'", check, StringComparison.Ordinal);
        Assert.Contains("[RateLimitPermitLimit] > 0", check, StringComparison.Ordinal);
        Assert.Contains("[RateLimitWindowSeconds] > 0", check, StringComparison.Ordinal);
        Assert.Contains("[AggregationWindowStartUtc] < [AggregationWindowEndUtc]", check, StringComparison.Ordinal);
        Assert.Contains(
            "[EventType] <> 'auth.login.rate_limited' AND [RateLimitPermitLimit] IS NULL AND [RateLimitWindowSeconds] IS NULL",
            check,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rate_limit_columns_are_nullable_integers()
    {
        using var context = Context();
        var entity = DesignEntity(context);

        foreach (var name in new[] { nameof(SecurityEventRecord.RateLimitPermitLimit), nameof(SecurityEventRecord.RateLimitWindowSeconds) })
        {
            var property = entity.FindProperty(name)!;
            Assert.True(property.IsNullable, name);
            Assert.Equal(typeof(int?), property.ClrType);
        }
    }

    [Fact]
    public void Rate_limited_migration_widens_the_type_check_and_its_down_is_a_forward_fix_guard()
    {
        var migration = new AddLoginRateLimitedSecurityEvent();

        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        var widened = Assert.Single(
            migration.UpOperations.OfType<AddCheckConstraintOperation>(),
            op => op.Name == "CK_SecurityEvents_EventType");
        Assert.Contains("'auth.login.rate_limited'", widened.Sql, StringComparison.Ordinal);
        Assert.Contains(
            migration.UpOperations,
            op => op is AddCheckConstraintOperation { Name: "CK_SecurityEvents_RateLimitSummary" });
        Assert.Equal(2, migration.UpOperations.OfType<AddColumnOperation>().Count());

        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("[EventType] = N'auth.login.rate_limited'", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[RateLimitPermitLimit] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51591", guard.Sql, StringComparison.Ordinal);
        var restored = Assert.Single(
            migration.DownOperations.OfType<AddCheckConstraintOperation>(),
            op => op.Name == "CK_SecurityEvents_EventType");
        Assert.DoesNotContain("auth.login.rate_limited", restored.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Founder_recovery_migration_only_widens_the_type_check_and_its_down_is_a_forward_fix_guard()
    {
        var migration = new AddFounderRecoverySecurityEvents();

        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal(2, migration.UpOperations.Count);
        Assert.IsType<DropCheckConstraintOperation>(migration.UpOperations[0]);
        var widened = Assert.IsType<AddCheckConstraintOperation>(migration.UpOperations[1]);
        Assert.Equal("CK_SecurityEvents_EventType", widened.Name);
        using (var context = Context())
        {
            var modelCheck = DesignEntity(context).GetCheckConstraints().Single(c => c.Name == "CK_SecurityEvents_EventType");
            Assert.Equal(modelCheck.Sql, widened.Sql);
        }

        Assert.All(SecurityEventTypesV1.FounderRecoveryTypes, type => Assert.Contains($"'{type}'", widened.Sql, StringComparison.Ordinal));

        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.All(SecurityEventTypesV1.FounderRecoveryTypes, type => Assert.Contains($"N'{type}'", guard.Sql, StringComparison.Ordinal));
        Assert.Contains("THROW 51851", guard.Sql, StringComparison.Ordinal);
        var restored = Assert.Single(migration.DownOperations.OfType<AddCheckConstraintOperation>());
        Assert.Contains("'auth.login.rate_limited'", restored.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("identity.founder.", restored.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Columns_are_bounded_and_occurrence_count_defaults_to_one()
    {
        using var context = Context();
        var entity = DesignEntity(context);

        var occurrenceCount = entity.FindProperty(nameof(SecurityEventRecord.OccurrenceCount))!;
        Assert.False(occurrenceCount.IsNullable);
        Assert.Equal(1, occurrenceCount.GetDefaultValue());
        Assert.True(entity.FindProperty(nameof(SecurityEventRecord.AggregationWindowStartUtc))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(SecurityEventRecord.AggregationWindowEndUtc))!.IsNullable);

        var hash = entity.FindProperty(nameof(SecurityEventRecord.IntegrityHash))!;
        Assert.Equal(64, hash.GetMaxLength());
        Assert.True(hash.IsFixedLength());
        Assert.Equal("Latin1_General_100_BIN2", entity.FindProperty(nameof(SecurityEventRecord.CorrelationId))!.GetCollation());
        Assert.Equal(64, entity.FindProperty(nameof(SecurityEventRecord.RetentionPolicyRef))!.GetMaxLength());
        Assert.All(
            entity.GetProperties().Where(p => p.ClrType == typeof(string)),
            p => Assert.False(p.IsUnicode() ?? true, p.Name));
    }

    [Fact]
    public void Indexes_cover_company_time_and_correlation_reads()
    {
        using var context = Context();
        var indexes = DesignEntity(context).GetIndexes().Select(i => i.GetDatabaseName()!).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(["IX_SecurityEvents_CompanyId_OccurredAtUtc", "IX_SecurityEvents_CorrelationId"], indexes);
    }

    [Fact]
    public void Initial_migration_creates_append_only_trigger_and_guards_its_down()
    {
        var migration = new InitialSecurityEvents();

        var trigger = Assert.Single(migration.UpOperations.OfType<SqlOperation>());
        Assert.Contains("CREATE TRIGGER [security].[TR_SecurityEvents_RejectUpdateDelete]", trigger.Sql, StringComparison.Ordinal);
        Assert.Contains("INSTEAD OF UPDATE, DELETE", trigger.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51271", trigger.Sql, StringComparison.Ordinal);

        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("SELECT 1 FROM [security].[SecurityEvents]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51270", guard.Sql, StringComparison.Ordinal);
        Assert.Contains(migration.DownOperations, op => op is DropTableOperation { Name: "SecurityEvents", Schema: "security" });
    }

    [Fact]
    public async Task Normal_save_changes_is_refused_in_every_overload()
    {
        await using var context = Context();
        context.SecurityEvents.Add(SecurityEventRecord.Create(Denial(), OccurredAt.AddYears(2), "TEST-No134"));

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges(acceptAllChangesOnSuccess: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(acceptAllChangesOnSuccess: true));
    }

    [Fact]
    public async Task Append_requires_a_fresh_context()
    {
        await using var context = Context();
        context.SecurityEvents.Add(SecurityEventRecord.Create(Denial(), OccurredAt.AddYears(2), "TEST-No134"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.AppendAsync(
            SecurityEventRecord.Create(Denial(), OccurredAt.AddYears(2), "TEST-No134"), CancellationToken.None));
    }

    private static IEntityType DesignEntity(SecurityEventsDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(SecurityEventRecord))!;

    private static SecurityEventsDbContext Context()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var options = new DbContextOptionsBuilder<SecurityEventsDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new SecurityEventsDbContext(options, company);
    }
}
