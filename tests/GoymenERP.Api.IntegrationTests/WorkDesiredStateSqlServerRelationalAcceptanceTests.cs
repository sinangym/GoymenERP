using System.Security.Cryptography;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
            Assert.Equal(WorkDesiredStateDecisionV2.Apply,
                (await scope.Store.ReconcileAsync(Unavailable(3), Metadata("unavailable-3"))).Decision);
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
        });
    }

    [SqlServerFact]
    public async Task Versionless_and_foreign_observations_write_nothing_and_tenant_filter_hides_other_company()
    {
        await WithDatabaseAsync(async factory =>
        {
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
            await using (var failing = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA))
            {
                var invalidAudit = Metadata("audit-failure") with { ActorType = "NotAnAuditActor" };
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                    failing.Store.ReconcileAsync(Available(1, WorkDesiredStateV2.NeedsAction), invalidAudit));
            }

            await using var verify = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA);
            Assert.Equal(0, await verify.Db.DesiredStateSnapshots.CountAsync());
            Assert.Equal(0, await verify.Db.ObservationInbox.CountAsync());
            Assert.Equal(0, await verify.Db.OutboxMessages.CountAsync());
            Assert.Equal(0, await verify.Db.Set<AuditEvent>().CountAsync());

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
        await using var initialize = await WorkSqlScope.OpenAsync(factory, TestCompanyIds.CompanyA);
        await initialize.Db.Database.MigrateAsync();
        try
        {
            await test(factory);
        }
        finally
        {
            await using var cleanupScope = factory.Services.CreateAsyncScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            Assert.Equal(databaseName, cleanupDb.Database.GetDbConnection().Database);
            await cleanupDb.Database.EnsureDeletedAsync();
        }
    }

    private sealed class WorkSqlScope : IAsyncDisposable
    {
        private WorkSqlScope(WorkDbContext db, WorkDesiredStateStoreV2 store)
        {
            Db = db;
            Store = store;
        }

        public WorkDbContext Db { get; }
        public WorkDesiredStateStoreV2 Store { get; }

        public static async Task<WorkSqlScope> OpenAsync(
            SqlServerRelationalApiFactory factory,
            Guid companyId,
            IWorkStorageKeyDigestV1? digest = null,
            Func<CancellationToken, Task>? beforeSave = null)
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
            var store = new WorkDesiredStateStoreV2(
                db,
                company,
                collector,
                TimeProvider.System,
                digest ?? WorkSha256StorageKeyDigestV1.Instance,
                beforeSave);
            return new WorkSqlScope(db, store);
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
