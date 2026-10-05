using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkDesiredStateSqlServerRelationalAcceptanceTests
{
    private static readonly Guid EvidenceA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EvidenceB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [SqlServerFact]
    public async Task Apply_replay_conflict_older_and_unavailable_watermark_are_atomic()
    {
        await WithDatabaseAsync(async factory =>
        {
            await using var scope = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA);
            var v1 = Available(1, WorkDesiredStateV2.NeedsAction);

            Assert.Equal(WorkDesiredStateDecisionV2.Apply,
                (await scope.Store.ReconcileAsync(v1, Metadata("apply-1"))).Decision);
            Assert.Equal(WorkDesiredStateDecisionV2.Replay,
                (await scope.Store.ReconcileAsync(v1, Metadata("replay-1"))).Decision);
            Assert.Equal(WorkDesiredStateDecisionV2.Conflict,
                (await scope.Store.ReconcileAsync(v1 with { EvidenceId = EvidenceB }, Metadata("conflict-1"))).Decision);
            await using (var newer = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA))
            {
                Assert.Equal(WorkDesiredStateDecisionV2.Apply,
                    (await newer.Store.ReconcileAsync(Unavailable(3), Metadata("unavailable-3"))).Decision);
            }

            var staleRetry = await scope.Store.ReconcileAsync(v1, Metadata("stale-v1"));
            Assert.Equal(WorkDesiredStateDecisionV2.IgnoreOlder, staleRetry.Decision);
            Assert.Equal(3, staleRetry.State!.HighestReliableObservation.ObservationOrdinal);
            Assert.Equal(1, staleRetry.State.EffectiveState!.ObservationOrdinal);
            Assert.Equal(WorkDesiredStateDecisionV2.IgnoreOlder,
                (await scope.Store.ReconcileAsync(Available(2, WorkDesiredStateV2.Satisfied), Metadata("older-2"))).Decision);

            var versionless = await scope.Store.ReconcileAsync(Unavailable(null), Metadata("versionless"));
            Assert.Equal(WorkDesiredStateDecisionV2.Unavailable, versionless.Decision);
            Assert.Equal(3, versionless.State!.HighestReliableObservation.ObservationOrdinal);
            Assert.Equal(1, versionless.State.EffectiveState!.ObservationOrdinal);
            Assert.Equal(WorkDesiredStateV2.NeedsAction, versionless.State.EffectiveState.DesiredState);

            Assert.Equal(1, await scope.Db.DesiredStateSnapshots.CountAsync());
            Assert.Equal(2, await scope.Db.ObservationInbox.CountAsync());
            Assert.Equal(2, await scope.Db.OutboxMessages.CountAsync());
            Assert.Equal(2, await scope.Db.Set<AuditEvent>().CountAsync());
            foreach (var outbox in await scope.Db.OutboxMessages.OrderBy(x => x.ObservationOrdinal).ToListAsync())
            {
                Assert.Equal(outbox.ObservationOrdinal,
                    WorkDesiredStateStoreV2.DecodeOutboxPayload(outbox).HighestReliableObservation.ObservationOrdinal);
            }

            await AssertStorageCompletenessConstraintsAsync(factory);
        });
    }

    [SqlServerFact]
    public async Task Versionless_and_foreign_observations_write_nothing_and_tenant_filter_hides_other_company()
    {
        await WithDatabaseAsync(async factory =>
        {
            await using (var changedAfterRead = await WorkSqlScope.OpenAsync(
                factory,
                TestCompanyIds.CompanyA,
                afterReadContext: static (company, _) =>
                {
                    company.Set(TestCompanyIds.CompanyB);
                    return Task.CompletedTask;
                }))
            {
                var result = await changedAfterRead.Store.ReconcileAsync(
                    Available(1, WorkDesiredStateV2.NeedsAction) with
                    {
                        SourceKey = "after-read-source",
                        ScopeKey = "after-read-scope"
                    }, Metadata("after-read-company-change"));
                Assert.Equal(WorkDesiredStateDecisionV2.Refused, result.Decision);
                Assert.Null(result.State);
            }

            await using (var changedBeforeSave = await WorkSqlScope.OpenAsync(
                factory,
                TestCompanyIds.CompanyA,
                beforeSaveContext: static (company, _) =>
                {
                    company.Set(Guid.Empty);
                    return Task.CompletedTask;
                }))
            {
                var result = await changedBeforeSave.Store.ReconcileAsync(
                    Available(1, WorkDesiredStateV2.NeedsAction) with
                    {
                        SourceKey = "before-save-source",
                        ScopeKey = "before-save-scope"
                    }, Metadata("before-save-company-change"));
                Assert.Equal(WorkDesiredStateDecisionV2.Refused, result.Decision);
                Assert.Null(result.State);
            }

            await using (var changedAfterCommit = await WorkSqlScope.OpenAsync(
                factory,
                TestCompanyIds.CompanyA,
                afterCommitContext: static (company, _) =>
                {
                    company.Set(TestCompanyIds.CompanyB);
                    return Task.CompletedTask;
                }))
            {
                var result = await changedAfterCommit.Store.ReconcileAsync(
                    Available(1, WorkDesiredStateV2.NeedsAction) with
                    {
                        SourceKey = "after-commit-source",
                        ScopeKey = "after-commit-scope"
                    }, Metadata("after-commit-company-change"));
                Assert.Equal(WorkDesiredStateDecisionV2.Refused, result.Decision);
                Assert.Null(result.State);
            }

            await using (var committed = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA))
            {
                Assert.Equal(1, await committed.Db.DesiredStateSnapshots.CountAsync());
                Assert.Equal(1, await committed.Db.ObservationInbox.CountAsync());
                Assert.Equal(1, await committed.Db.OutboxMessages.CountAsync());
                Assert.Equal(1, await committed.Db.Set<AuditEvent>().CountAsync());
            }

            await using (var companyA = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA))
            {
                var versionless = await companyA.Store.ReconcileAsync(Unavailable(null), Metadata("versionless-empty"));
                Assert.Equal(WorkDesiredStateDecisionV2.Unavailable, versionless.Decision);
                Assert.Null(versionless.State);

                var foreign = await companyA.Store.ReconcileAsync(
                    Available(1, WorkDesiredStateV2.NeedsAction) with { CompanyId = TestCompanyIds.CompanyB },
                    Metadata("foreign"));
                Assert.Equal(WorkDesiredStateDecisionV2.Refused, foreign.Decision);
                Assert.Null(foreign.State);
                Assert.Empty(companyA.Db.ChangeTracker.Entries<IWorkTenantRow>());

                Assert.Equal(WorkDesiredStateDecisionV2.Apply,
                    (await companyA.Store.ReconcileAsync(Available(1, WorkDesiredStateV2.NeedsAction), Metadata("company-a"))).Decision);
            }

            await using var companyB = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyB);
            Assert.Equal(0, await companyB.Db.DesiredStateSnapshots.CountAsync());
            Assert.Equal(0, await companyB.Db.ObservationInbox.CountAsync());
            Assert.Equal(0, await companyB.Db.OutboxMessages.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task Digest_collision_is_refused_before_SQL_and_preserves_the_first_identity()
    {
        await WithDatabaseAsync(async factory =>
        {
            await using (var first = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA))
            {
                Assert.Equal(WorkDesiredStateDecisionV2.Apply,
                    (await first.Store.ReconcileAsync(Available(1, WorkDesiredStateV2.NeedsAction), Metadata("first"))).Decision);
            }

            var stored = WorkStorageKeyV1.Create("source-a");
            var colliding = WorkStorageKeyV1.Create("source-b");
            var digest = new ForcedCollisionDigest(colliding.Utf16Be, stored.Digest);
            await using var second = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA, digest);

            await Assert.ThrowsAsync<WorkStorageKeyCollisionException>(() =>
                second.Store.ReconcileAsync(
                    Available(2, WorkDesiredStateV2.Satisfied) with { SourceKey = "source-b" },
                    Metadata("collision")));

            Assert.Equal(1, await second.Db.DesiredStateSnapshots.CountAsync());
            Assert.Equal(1, await second.Db.ObservationInbox.CountAsync());
            Assert.Equal(1, await second.Db.OutboxMessages.CountAsync());
            Assert.Equal(1, await second.Db.Set<AuditEvent>().CountAsync());
        });
    }

    [SqlServerFact]
    public async Task Same_and_newer_ordinal_races_have_one_CAS_winner_without_blind_overwrite()
    {
        await WithDatabaseAsync(async factory =>
        {
            var sameGate = new AsyncPairGate();
            await using var sameA = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA, beforeSave: sameGate.WaitAsync);
            await using var sameB = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA, beforeSave: sameGate.WaitAsync);
            var same = Available(1, WorkDesiredStateV2.NeedsAction);
            var sameResults = await Task.WhenAll(
                sameA.Store.ReconcileAsync(same, Metadata("same-a")),
                sameB.Store.ReconcileAsync(same, Metadata("same-b")));
            Assert.Equal(1, sameResults.Count(x => x.Decision == WorkDesiredStateDecisionV2.Apply));
            Assert.Equal(1, sameResults.Count(x => x.Decision == WorkDesiredStateDecisionV2.Replay));

            var newerGate = new AsyncPairGate();
            await using var newerA = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA, beforeSave: newerGate.WaitAsync);
            await using var newerB = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA, beforeSave: newerGate.WaitAsync);
            var newerResults = await Task.WhenAll(
                CaptureAsync(() => newerA.Store.ReconcileAsync(Available(2, WorkDesiredStateV2.Satisfied), Metadata("newer-2"))),
                CaptureAsync(() => newerB.Store.ReconcileAsync(Unavailable(3), Metadata("newer-3"))));

            Assert.Equal(1, newerResults.Count(x => x.Result?.Decision == WorkDesiredStateDecisionV2.Apply));
            Assert.All(newerResults.Where(x => x.Result?.Decision != WorkDesiredStateDecisionV2.Apply), loser =>
                Assert.True(
                    loser.Result?.Decision == WorkDesiredStateDecisionV2.IgnoreOlder
                    || loser.Exception is DbUpdateConcurrencyException));

            await using var read = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA);
            Assert.Equal(2, await read.Db.OutboxMessages.CountAsync());
            Assert.Equal(2, await read.Db.ObservationInbox.CountAsync());
            Assert.Equal(2, await read.Db.Set<AuditEvent>().CountAsync());
            Assert.Single(await read.Db.DesiredStateSnapshots.ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Audit_failure_rolls_back_snapshot_inbox_outbox_and_write_guard_refuses_foreign_rows()
    {
        await WithDatabaseAsync(async factory =>
        {
            await using (var migrations = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA))
            {
                var migrator = migrations.Db.Database.GetService<IMigrator>();
                await migrator.MigrateAsync("0");
                await migrator.MigrateAsync();
            }

            await using (var failing = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA))
            {
                var invalidAudit = Metadata("audit-failure") with { ActorType = "NotAnAuditActor" };
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                    failing.Store.ReconcileAsync(Available(1, WorkDesiredStateV2.NeedsAction), invalidAudit));
            }

            await AssertWorkEffectCountsAsync(factory, 0);

            await using (var rollback = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA))
            {
                await rollback.Db.Database.ExecuteSqlRawAsync("""
                    CREATE OR ALTER TRIGGER [work].[TR_JV03_ForcedRollback]
                    ON [work].[OutboxMessages]
                    AFTER INSERT
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        IF NOT EXISTS (SELECT 1 FROM [work].[DesiredStateSnapshots])
                           OR NOT EXISTS (SELECT 1 FROM [work].[ObservationInbox])
                            THROW 51083, 'work_test_participant_write_not_visible', 1;
                        THROW 51082, 'work_test_forced_rollback', 1;
                    END
                    """);

                try
                {
                    var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
                        rollback.Store.ReconcileAsync(
                            Available(1, WorkDesiredStateV2.NeedsAction),
                            Metadata("forced-db-rollback")));
                    AssertSqlNumber(failure, 51082);
                }
                finally
                {
                    await rollback.Db.Database.ExecuteSqlRawAsync(
                        "DROP TRIGGER IF EXISTS [work].[TR_JV03_ForcedRollback]");
                }

                await AssertWorkEffectCountsAsync(factory, 0);
                Assert.Equal(WorkDesiredStateDecisionV2.Apply,
                    (await rollback.Store.ReconcileAsync(
                        Available(1, WorkDesiredStateV2.NeedsAction),
                        Metadata("forced-db-rollback-retry"))).Decision);
            }

            await AssertWorkEffectCountsAsync(factory, 1);

            await using (var occupied = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA))
            {
                var refusal = await Assert.ThrowsAnyAsync<Exception>(() =>
                    occupied.Db.Database.GetService<IMigrator>().MigrateAsync("0"));
                AssertSqlNumber(refusal, 51081);
            }

            await AssertWorkEffectCountsAsync(factory, 1);

            await using var verify = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA);

            var source = WorkStorageKeyV1.Create("source-a");
            var scope = WorkStorageKeyV1.Create("scope-a");
            verify.Db.DesiredStateSnapshots.Add(new WorkDesiredStateSnapshotRow
            {
                CompanyId = TestCompanyIds.CompanyB,
                SourceKeyDigest = source.Digest,
                ScopeKeyDigest = scope.Digest,
                SourceKeyUtf16Be = source.Utf16Be,
                ScopeKeyUtf16Be = scope.Utf16Be,
                HighestObservationOrdinal = 1,
                HighestAvailability = (int)WorkSourceAvailabilityV2.Available,
                HighestDesiredState = (int)WorkDesiredStateV2.NeedsAction,
                HighestEvidenceId = EvidenceA,
                HighestPayloadSha256 = HashA,
                EffectiveObservationOrdinal = 1,
                EffectiveDesiredState = (int)WorkDesiredStateV2.NeedsAction,
                EffectiveEvidenceId = EvidenceA,
                EffectivePayloadSha256 = HashA,
                Version = 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => verify.Db.SaveChangesAsync());
        });
    }

    private static async Task<(WorkDesiredStateResultV2? Result, Exception? Exception)> CaptureAsync(
        Func<Task<WorkDesiredStateResultV2>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            return (null, exception);
        }
    }

    private static async Task AssertStorageCompletenessConstraintsAsync(
        SqlServerRelationalApiFactory factory)
    {
        var snapshotMutations = new Action<WorkDesiredStateSnapshotRow>[]
        {
            row => row.HighestDesiredState = null,
            row => row.EffectiveObservationOrdinal = null,
            row => row.EffectiveDesiredState = null,
            row => row.EffectiveEvidenceId = null,
            row => row.EffectivePayloadSha256 = null
        };

        var baseline = await ReadWorkEffectCountsAsync(factory);
        for (var index = 0; index < snapshotMutations.Length; index++)
        {
            await using var invalid = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA);
            var row = SnapshotRow($"invalid-available-{index}", WorkSourceAvailabilityV2.Available, retainEffective: true);
            snapshotMutations[index](row);
            invalid.Db.DesiredStateSnapshots.Add(row);
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.Db.SaveChangesAsync());
            AssertSqlNumber(failure, 547);
            Assert.Equal(baseline, await ReadWorkEffectCountsAsync(factory));
        }

        var retainedMutations = new Action<WorkDesiredStateSnapshotRow>[]
        {
            row => row.EffectiveObservationOrdinal = null,
            row => row.EffectiveDesiredState = null,
            row => row.EffectiveEvidenceId = null,
            row => row.EffectivePayloadSha256 = null
        };
        for (var index = 0; index < retainedMutations.Length; index++)
        {
            await using var invalid = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA);
            var row = SnapshotRow($"invalid-unavailable-{index}", WorkSourceAvailabilityV2.Unavailable, retainEffective: true);
            retainedMutations[index](row);
            invalid.Db.DesiredStateSnapshots.Add(row);
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.Db.SaveChangesAsync());
            AssertSqlNumber(failure, 547);
            Assert.Equal(baseline, await ReadWorkEffectCountsAsync(factory));
        }

        await using (var invalidInbox = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA))
        {
            var source = WorkStorageKeyV1.Create("invalid-inbox");
            var scope = WorkStorageKeyV1.Create("invalid-inbox-scope");
            invalidInbox.Db.ObservationInbox.Add(new WorkObservationInboxRow
            {
                CompanyId = TestCompanyIds.CompanyA,
                SourceKeyDigest = source.Digest,
                ScopeKeyDigest = scope.Digest,
                SourceKeyUtf16Be = source.Utf16Be,
                ScopeKeyUtf16Be = scope.Utf16Be,
                ObservationOrdinal = 7,
                Availability = (int)WorkSourceAvailabilityV2.Available,
                DesiredState = null,
                EvidenceId = EvidenceA,
                PayloadSha256 = HashA,
                RecordedAtUtc = DateTimeOffset.UtcNow
            });
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => invalidInbox.Db.SaveChangesAsync());
            AssertSqlNumber(failure, 547);
            Assert.Equal(baseline, await ReadWorkEffectCountsAsync(factory));
        }

        await using (var allNullUnavailable = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA))
        {
            allNullUnavailable.Db.DesiredStateSnapshots.Add(
                SnapshotRow("valid-unavailable-all-null", WorkSourceAvailabilityV2.Unavailable, retainEffective: false));
            await allNullUnavailable.Db.SaveChangesAsync();
        }

        var afterPositive = await ReadWorkEffectCountsAsync(factory);
        Assert.Equal(baseline.Snapshots + 1, afterPositive.Snapshots);
        Assert.Equal(baseline.Inbox, afterPositive.Inbox);
        Assert.Equal(baseline.Outbox, afterPositive.Outbox);
        Assert.Equal(baseline.Audit, afterPositive.Audit);
    }

    private static WorkDesiredStateSnapshotRow SnapshotRow(
        string identity,
        WorkSourceAvailabilityV2 availability,
        bool retainEffective)
    {
        var source = WorkStorageKeyV1.Create(identity);
        var scope = WorkStorageKeyV1.Create(identity + "-scope");
        return new WorkDesiredStateSnapshotRow
        {
            CompanyId = TestCompanyIds.CompanyA,
            SourceKeyDigest = source.Digest,
            ScopeKeyDigest = scope.Digest,
            SourceKeyUtf16Be = source.Utf16Be,
            ScopeKeyUtf16Be = scope.Utf16Be,
            HighestObservationOrdinal = 7,
            HighestAvailability = (int)availability,
            HighestDesiredState = availability == WorkSourceAvailabilityV2.Available
                ? (int)WorkDesiredStateV2.NeedsAction
                : null,
            HighestEvidenceId = EvidenceA,
            HighestPayloadSha256 = HashA,
            EffectiveObservationOrdinal = retainEffective ? 5 : null,
            EffectiveDesiredState = retainEffective ? (int)WorkDesiredStateV2.NeedsAction : null,
            EffectiveEvidenceId = retainEffective ? EvidenceA : null,
            EffectivePayloadSha256 = retainEffective ? HashA : null,
            Version = 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static async Task AssertWorkEffectCountsAsync(
        SqlServerRelationalApiFactory factory,
        int expected)
    {
        var counts = await ReadWorkEffectCountsAsync(factory);
        Assert.Equal(expected, counts.Snapshots);
        Assert.Equal(expected, counts.Inbox);
        Assert.Equal(expected, counts.Outbox);
        Assert.Equal(expected, counts.Audit);
    }

    private static async Task<(int Snapshots, int Inbox, int Outbox, int Audit)> ReadWorkEffectCountsAsync(
        SqlServerRelationalApiFactory factory)
    {
        await using var read = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA);
        return (
            await read.Db.DesiredStateSnapshots.CountAsync(),
            await read.Db.ObservationInbox.CountAsync(),
            await read.Db.OutboxMessages.CountAsync(),
            await read.Db.Set<AuditEvent>().CountAsync());
    }

    private static void AssertSqlNumber(Exception exception, int expected)
    {
        var sql = FindSqlException(exception);
        Assert.NotNull(sql);
        Assert.Equal(expected, sql.Number);
    }

    private static SqlException? FindSqlException(Exception? exception) => exception switch
    {
        null => null,
        SqlException sql => sql,
        AggregateException aggregate => aggregate.InnerExceptions
            .Select(FindSqlException)
            .FirstOrDefault(candidate => candidate is not null),
        _ => FindSqlException(exception.InnerException)
    };

    private static WorkDesiredStateObservationV2 Available(long ordinal, WorkDesiredStateV2 state) => new(
        TestCompanyIds.CompanyA, "source-a", "scope-a", ordinal,
        WorkSourceAvailabilityV2.Available, state, EvidenceA, HashA);

    private static WorkDesiredStateObservationV2 Unavailable(long? ordinal) => new(
        TestCompanyIds.CompanyA, "source-a", "scope-a", ordinal,
        WorkSourceAvailabilityV2.Unavailable, null, EvidenceB, HashB);

    private static WorkDesiredStateWriteMetadataV2 Metadata(string id) => new(
        AuditActorTypes.Integration,
        "work-sql-test",
        "work.test.admit",
        [],
        GoymenERP.BuildingBlocks.Observability.SourceChannels.Integration,
        "correlation-" + id,
        null,
        "idempotency-" + id,
        "Work desired-state SQL acceptance");

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Work_DesiredState_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(connection, databaseName: databaseName);
        Exception? primaryFailure = null;
        Exception? cleanupFailure = null;
        try
        {
            await using var initialize = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA);
            await initialize.Db.Database.MigrateAsync();
            await test(factory);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }

        try
        {
            await using var cleanupScope = factory.Services.CreateAsyncScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            if (!string.Equals(
                    databaseName,
                    cleanupDb.Database.GetDbConnection().Database,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Disposable Work SQL database identity changed before cleanup.");
            }

            await cleanupDb.Database.EnsureDeletedAsync();
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }

        if (primaryFailure is not null && cleanupFailure is not null)
        {
            throw new AggregateException(primaryFailure, cleanupFailure);
        }

        if (primaryFailure is not null)
        {
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

    private sealed class WorkSqlScope : IAsyncDisposable
    {
        private WorkSqlScope(
            WorkDbContext db,
            WorkDesiredStateStoreV2 store,
            MutableCompanyContext company)
        {
            Db = db;
            Store = store;
            Company = company;
        }

        public WorkDbContext Db { get; }
        public WorkDesiredStateStoreV2 Store { get; }
        public MutableCompanyContext Company { get; }

        public static async Task<WorkSqlScope> OpenAsync(
            SqlServerRelationalApiFactory factory,
            Guid companyId,
            IWorkStorageKeyDigestV1? digest = null,
            Func<CancellationToken, Task>? beforeSave = null,
            Func<MutableCompanyContext, CancellationToken, Task>? beforeSaveContext = null,
            Func<MutableCompanyContext, CancellationToken, Task>? afterReadContext = null,
            Func<MutableCompanyContext, CancellationToken, Task>? afterCommitContext = null)
        {
            string connectionString;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                connectionString = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>()
                    .Database.GetConnectionString()!;
            }

            var company = new MutableCompanyContext();
            company.Set(companyId);
            var collector = new AuditEventCollector();
            var interceptor = new AuditSaveChangesInterceptor(collector, company);
            var options = new DbContextOptionsBuilder<WorkDbContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", WorkDbContext.Schema))
                .AddInterceptors(interceptor)
                .Options;
            var db = new WorkDbContext(options, company);
            async Task BeforeSave(CancellationToken cancellationToken)
            {
                if (beforeSave is not null)
                {
                    await beforeSave(cancellationToken);
                }

                if (beforeSaveContext is not null)
                {
                    await beforeSaveContext(company, cancellationToken);
                }
            }

            var store = new WorkDesiredStateStoreV2(
                db,
                company,
                collector,
                TimeProvider.System,
                digest ?? WorkSha256StorageKeyDigestV1.Instance,
                beforeSave is null && beforeSaveContext is null ? null : BeforeSave,
                afterReadContext is null
                    ? null
                    : cancellationToken => afterReadContext(company, cancellationToken),
                afterCommitContext is null
                    ? null
                    : cancellationToken => afterCommitContext(company, cancellationToken));
            return new WorkSqlScope(db, store, company);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class MutableCompanyContext : ICompanyContext
    {
        public bool IsResolved { get; private set; }
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId)
        {
            CompanyId = companyId;
            IsResolved = companyId != Guid.Empty;
        }
    }

    private sealed class ForcedCollisionDigest(byte[] collidingBytes, byte[] existingDigest)
        : IWorkStorageKeyDigestV1
    {
        public byte[] Compute(ReadOnlySpan<byte> value) =>
            value.SequenceEqual(collidingBytes)
                ? existingDigest.ToArray()
                : SHA256.HashData(value);
    }

    private sealed class AsyncPairGate
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
            {
                ready.TrySetResult();
            }

            await ready.Task.WaitAsync(cancellationToken);
        }
    }
}
