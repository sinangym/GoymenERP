using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Audit.UnitTests;

/// <summary>
/// F-023 (F023-5a) audit persistence model without a database: the outcome-filtered company read has its own index on
/// the same (OccurredAtUtc DESC, Id DESC) keyset, the migration that adds it is pure DDL and its Down only drops the
/// index (no row is lost, so no forward-fix guard). What SQL Server builds is proven by the relational acceptance test.
/// </summary>
public sealed class AuditPersistenceModelTests
{
    private const string OutcomeIndexName = "IX_AuditEvents_CompanyId_Outcome_OccurredAtUtc_Id";

    [Fact]
    public void The_reviewed_audit_migration_chain_preserves_the_outcome_index_and_matches_the_snapshot()
    {
        using var context = Context();

        Assert.Equal(
            ["20260827160529_InitialAudit", "20260912173914_AddGlobalProductAuditSubject",
                "20261115121446_AddAuditOutcomeQueryIndex", "20261116220434_AddTypedFounderRecoverySystemAudit",
                "20261117003912_AddLocalCredentialSelfServiceAudit", "20261117160915_AddPlatformCredentialRecoveryAudit",
                "20261119131410_ProtectAuditHistoryRollback"],
            context.Database.GetMigrations().TakeLast(7));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_outcome_query_index_leads_with_company_and_outcome_and_keeps_the_descending_keyset()
    {
        using var context = Context();
        var auditEvents = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AuditEvent))!;

        var index = Assert.Single(auditEvents.GetIndexes(), value => value.GetDatabaseName() == OutcomeIndexName);

        Assert.Equal(
            [nameof(AuditEvent.CompanyId), nameof(AuditEvent.Outcome), nameof(AuditEvent.OccurredAtUtc), nameof(AuditEvent.Id)],
            index.Properties.Select(property => property.Name));
        Assert.Equal(new[] { false, false, true, true }, index.IsDescending!);
        Assert.False(index.IsUnique);
        Assert.Null(index.GetFilter());
    }

    [Fact]
    public void The_outcome_query_index_migration_is_pure_DDL_and_its_Down_only_drops_the_index()
    {
        var migration = new AddAuditOutcomeQueryIndex();

        var create = Assert.IsType<CreateIndexOperation>(Assert.Single(migration.UpOperations));
        Assert.Equal(OutcomeIndexName, create.Name);
        Assert.Equal("audit", create.Schema);
        Assert.Equal("AuditEvents", create.Table);
        Assert.Equal(
            [nameof(AuditEvent.CompanyId), nameof(AuditEvent.Outcome), nameof(AuditEvent.OccurredAtUtc), nameof(AuditEvent.Id)],
            create.Columns);
        Assert.Equal(new[] { false, false, true, true }, create.IsDescending!);
        Assert.False(create.IsUnique);

        var drop = Assert.IsType<DropIndexOperation>(Assert.Single(migration.DownOperations));
        Assert.Equal(OutcomeIndexName, drop.Name);
        Assert.Equal("audit", drop.Schema);
        Assert.Equal("AuditEvents", drop.Table);
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.DoesNotContain(migration.DownOperations, operation => operation is SqlOperation);
    }

    [Fact]
    public void The_history_rollback_boundary_changes_no_model_and_refuses_before_any_destructive_operation()
    {
        var migration = new ProtectAuditHistoryRollback();
        Assert.Empty(migration.UpOperations);
        var guard = Assert.IsType<SqlOperation>(Assert.Single(migration.DownOperations));
        Assert.Contains("[audit].[AuditEvents]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("TABLOCKX", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("HOLDLOCK", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51900", guard.Sql, StringComparison.Ordinal);
        Assert.False(guard.SuppressTransaction);
        Assert.DoesNotContain("DELETE FROM", guard.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", guard.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE", guard.Sql, StringComparison.OrdinalIgnoreCase);
    }

    private static AuditDbContext Context() =>
        new(
            new DbContextOptionsBuilder<AuditDbContext>()
                .UseSqlServer(
                    "Server=unused.invalid;Database=unused;Connect Timeout=1;",
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "audit"))
                .Options,
            new CompanyContext());
}
