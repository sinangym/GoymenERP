using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
    public void The_outcome_query_index_migration_is_the_last_one_and_the_snapshot_matches_the_model()
    {
        using var context = Context();

        Assert.Equal(
            ["20260827160529_InitialAudit", "20260912173914_AddGlobalProductAuditSubject",
                "20261115121446_AddAuditOutcomeQueryIndex"],
            context.Database.GetMigrations().TakeLast(3));
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

    private static AuditDbContext Context() =>
        new(
            new DbContextOptionsBuilder<AuditDbContext>()
                .UseSqlServer(
                    "Server=unused.invalid;Database=unused;Connect Timeout=1;",
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "audit"))
                .Options,
            new CompanyContext());
}
