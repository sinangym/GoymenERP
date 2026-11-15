using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Idempotency.Domain.Retention;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Idempotency.Infrastructure.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Idempotency.UnitTests;

/// <summary>
/// OPEN-021 / NS-12 persistence model without a database: the purge evidence table, its checks and tenant filter, and
/// the forward-fix migration. What SQL Server enforces is proven by the relational acceptance tests.
/// </summary>
public sealed class IdempotencyPersistenceModelTests
{
    [Fact]
    public void The_retention_migration_is_the_last_command_execution_migration_and_the_snapshot_matches_the_model()
    {
        using var context = Context();

        Assert.Equal(
            ["20260827175405_InitialIdempotency", "20261115162318_AddIdempotencyRetentionPurge"],
            context.Database.GetMigrations().TakeLast(2));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_purge_evidence_table_is_company_filtered_and_its_checks_hold_the_reviewed_sql()
    {
        using var context = Context();
        var runtime = context.Model.FindEntityType(typeof(IdempotencyRetentionPurgeRecord))!;
        Assert.Equal(("idempotency", "RetentionPurgeRecords"), (runtime.GetSchema(), runtime.GetTableName()));
        Assert.Single(runtime.GetDeclaredQueryFilters());

        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(IdempotencyRetentionPurgeRecord))!;
        Assert.Equal(
            new Dictionary<string, string>
            {
                [IdempotencyRetentionPurgeRecordModelBuilderExtensions.CompanyIdNotEmptyCheckName] =
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000'",
                [IdempotencyRetentionPurgeRecordModelBuilderExtensions.DeletedCountPositiveCheckName] = "[DeletedCount] > 0",
                [IdempotencyRetentionPurgeRecordModelBuilderExtensions.IdentifiersNotEmptyCheckName] =
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [RunId] <> '00000000-0000-0000-0000-000000000000'",
                [IdempotencyRetentionPurgeRecordModelBuilderExtensions.PolicyRefNotBlankCheckName] =
                    "LEN(LTRIM(RTRIM([PolicyRef]))) > 0",
                [IdempotencyRetentionPurgeRecordModelBuilderExtensions.RetentionPeriodFormatCheckName] =
                    "[RetentionPeriod] LIKE 'P%' AND LEN([RetentionPeriod]) > 1 AND [RetentionPeriod] NOT LIKE '%[^0-9PYMD]%'",
                [IdempotencyRetentionPurgeRecordModelBuilderExtensions.UtcInstantsCheckName] =
                    "DATEPART(TZOFFSET, [CutoffUtc]) = 0 AND DATEPART(TZOFFSET, [RecordedAtUtc]) = 0 AND [CutoffUtc] < [RecordedAtUtc]",
            },
            design.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql));
        Assert.Equal(["Id"], design.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(ValueGenerated.Never, design.FindProperty(nameof(IdempotencyRetentionPurgeRecord.Id))!.ValueGenerated);
        var period = design.FindProperty(nameof(IdempotencyRetentionPurgeRecord.RetentionPeriod))!;
        Assert.Equal(IdempotencyRetentionPeriod.MaxTextLength, period.GetMaxLength());
        Assert.False(period.IsUnicode());
        Assert.False(period.IsNullable);
        Assert.Equal(
            IdempotencyRetentionPurgeRecord.PolicyRefMaxLength,
            design.FindProperty(nameof(IdempotencyRetentionPurgeRecord.PolicyRef))!.GetMaxLength());
        Assert.Contains(design.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual(["CompanyId", "RecordedAtUtc", "Id"]));
    }

    [Fact]
    public void The_migration_makes_the_evidence_append_only_opens_only_the_cutoff_window_and_its_Down_refuses_while_evidence_exists()
    {
        var migration = new AddIdempotencyRetentionPurge();

        var upSql = migration.UpOperations.OfType<SqlOperation>().Select(operation => operation.Sql).ToArray();
        Assert.Equal(2, upSql.Length);
        Assert.Contains("TR_RetentionPurgeRecords_RejectMutation", upSql[0], StringComparison.Ordinal);
        Assert.Contains("THROW 51561", upSql[0], StringComparison.Ordinal);
        Assert.Contains("CREATE OR ALTER TRIGGER [idempotency].[TR_CommandExecutions_RejectMutation]", upSql[1], StringComparison.Ordinal);
        Assert.Contains("THROW 51010", upSql[1], StringComparison.Ordinal);
        Assert.Contains("THROW 51562", upSql[1], StringComparison.Ordinal);
        Assert.Contains("[CompletedAtUtc] >= @cutoff", upSql[1], StringComparison.Ordinal);
        // The trigger reads the same session key the purge store writes.
        Assert.Contains("N'" + SqlIdempotencyRetentionPurgeStore.CutoffSessionKey + "'", upSql[1], StringComparison.Ordinal);

        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("[idempotency].[RetentionPurgeRecords]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51563", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("(forward fix)", guard.Sql, StringComparison.Ordinal);
        var restore = Assert.IsType<SqlOperation>(migration.DownOperations[1]);
        Assert.Contains("THROW 51010", restore.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SESSION_CONTEXT", restore.Sql, StringComparison.Ordinal);
        Assert.Equal(2, migration.DownOperations.OfType<SqlOperation>().Count());
        Assert.IsType<DropTableOperation>(migration.DownOperations.Last());
    }

    private static CommandExecutionDbContext Context() => new(
        new DbContextOptionsBuilder<CommandExecutionDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options,
        new CompanyContext());
}
