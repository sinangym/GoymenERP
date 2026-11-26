using GoymenERP.Api.Ai.Commands;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.AiKnowledge.Infrastructure;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiKnowledgeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_command_survives_new_contexts_and_a_version_race_writes_one_state_and_one_event()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var record = CommandRecord();
            await using (var first = Db(connectionString, record.CompanyId))
            {
                Assert.True(await Ledger(first, record.CompanyId).TryAddAsync(record, [CommandEvent(record)]));
            }
            await using var leftDb = Db(connectionString, record.CompanyId);
            await using var rightDb = Db(connectionString, record.CompanyId);
            var left = Ledger(leftDb, record.CompanyId);
            var right = Ledger(rightDb, record.CompanyId);
            var recovered = await left.GetAsync(record.CompanyId, record.PreviewId);
            Assert.NotNull(recovered);
            Assert.Equal(record.Preview.CanonicalCommandJson, recovered.Preview.CanonicalCommandJson);
            Assert.Equal(record.PreviewIdentityJson, recovered.PreviewIdentityJson);
            Assert.Equal(record.Context, recovered.Context);
            var approved = recovered with { State = AiCommandStateV1.Approved, Version = 2,
                ApprovalId = Guid.NewGuid(), ApprovedBy = record.ActorId, ApprovedAtUtc = SqlAiCommandLedgerTests.At };
            var competitor = approved with { ApprovalId = Guid.NewGuid() };
            var results = await Task.WhenAll(
                left.TryReplaceAsync(approved, 1, CommandEvent(approved) with { Kind = AiCommandEventKindV1.Approved }),
                right.TryReplaceAsync(competitor, 1, CommandEvent(competitor) with { Kind = AiCommandEventKindV1.Approved }));
            Assert.Single(results, result => result);
            await using var restarted = Db(connectionString, record.CompanyId);
            var final = await Ledger(restarted, record.CompanyId).GetAsync(record.CompanyId, record.PreviewId);
            Assert.Equal(2, final!.Version);
            Assert.Equal(results[0] ? approved.ApprovalId : competitor.ApprovalId, final.ApprovalId);
            Assert.Equal(2, await restarted.CommandEvents.CountAsync());
            await using var other = Db(connectionString, TestCompanyIds.CompanyB);
            Assert.Empty(await other.CommandRecords.ToArrayAsync());
            Assert.Empty(await other.CommandEvents.ToArrayAsync());
            Assert.Null(await Ledger(other, TestCompanyIds.CompanyB).GetAsync(record.CompanyId, record.PreviewId));
            Assert.False(await Ledger(other, TestCompanyIds.CompanyB).TryAddAsync(record, [CommandEvent(record)]));
        });
    }

    [SqlServerFact]
    public async Task An_event_failure_rolls_back_the_command_CAS_and_a_changed_preview_cannot_reuse_approval()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var record = CommandRecord(); var initialEvent = CommandEvent(record);
            await using var db = Db(connectionString, record.CompanyId);
            var ledger = Ledger(db, record.CompanyId);
            Assert.True(await ledger.TryAddAsync(record, [initialEvent]));
            Assert.False(await ledger.TryAddAsync(record, [CommandEvent(record)]));
            var approved = record with { Version = 2, State = AiCommandStateV1.Approved, ApprovalId = Guid.NewGuid() };
            // Duplicate event key must undo the state change in the same SaveChanges transaction.
            Assert.False(await ledger.TryReplaceAsync(approved, 1, initialEvent with { Kind = AiCommandEventKindV1.Approved }));
            await using var restarted = Db(connectionString, record.CompanyId);
            var restored = await Ledger(restarted, record.CompanyId).GetAsync(record.CompanyId, record.PreviewId);
            Assert.Equal(AiCommandStateV1.Previewed, restored!.State);
            Assert.Equal(1, restored.Version);
            Assert.Null(restored.ApprovalId);
            Assert.Single(await restarted.CommandEvents.ToArrayAsync());
            Assert.False(await ledger.TryReplaceAsync(approved with { CanonicalArgumentsJson = "{\"changed\":true}" },
                1, CommandEvent(approved)));
            Assert.Equal(1, (await ledger.GetAsync(record.CompanyId, record.PreviewId))!.Version);
        });
    }

    [SqlServerFact]
    public async Task The_SQL_constraints_refuse_invalid_command_identity_content_and_cross_company_event_links()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var record = CommandRecord();
            await using var db = Db(connectionString, record.CompanyId);
            Assert.True(await Ledger(db, record.CompanyId).TryAddAsync(record, [CommandEvent(record)]));
            foreach (var change in new[] { "[Version] = 0", "[State] = 10", "[RecordJson] = N'broken'",
                         "[BindingSha256] = REPLICATE('A', 64)" })
            {
                var failure = Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString,
                    $"UPDATE [ai].[CommandRecords] SET {change} WHERE [CompanyId] = '{record.CompanyId}' AND [PreviewId] = '{record.PreviewId}'")));
                Assert.Equal(547, failure.Number);
            }
            var foreign = Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString,
                $"UPDATE [ai].[CommandEvents] SET [CompanyId] = '{TestCompanyIds.CompanyB}' WHERE [CompanyId] = '{record.CompanyId}'")));
            Assert.Equal(547, foreign.Number);
            Assert.Equal(1, (await Ledger(db, record.CompanyId).GetAsync(record.CompanyId, record.PreviewId))!.Version);
            Assert.Single(await db.CommandEvents.ToArrayAsync());
        });
    }

    [SqlServerFact]
    public async Task Ledger_Down_allows_empty_tables_and_refuses_52200_while_a_command_or_event_exists()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var db = Db(connectionString, TestCompanyIds.CompanyA);
            var migrator = db.GetService<IMigrator>();
            var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.EndsWith("_AddAiCommandLedger", migrations[^1]);
            var beforeLedger = migrations[^2];
            await migrator.MigrateAsync(beforeLedger);
            await migrator.MigrateAsync();
            var record = CommandRecord();
            Assert.True(await Ledger(db, record.CompanyId).TryAddAsync(record, [CommandEvent(record)]));
            var failure = Sql(await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(beforeLedger)));
            Assert.Equal(52200, failure.Number);
            Assert.Equal(1, await db.CommandRecords.CountAsync());
            Assert.Equal(1, await db.CommandEvents.CountAsync());
        });
    }

    private static AiCommandRecordV1 CommandRecord()
    {
        var record = SqlAiCommandLedgerTests.Record();
        return record with { Context = record.Context with { CompanyId = TestCompanyIds.CompanyA } };
    }

    private static AiCommandLedgerEventV1 CommandEvent(AiCommandRecordV1 record) =>
        SqlAiCommandLedgerTests.Event(record) with { CompanyId = record.CompanyId };

    private static SqlAiCommandLedger Ledger(AiKnowledgeDbContext db, Guid companyId)
    {
        var company = new CompanyContext(); company.Set(companyId);
        return new SqlAiCommandLedger(new AiCommandPersistenceV1(db, company));
    }
}
