using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-027 / DEC-CAR-003 / CARI-AC-016 (ALC-S11): making a company cari account passive needs no separate approval; the
/// existing set-active-state command (permission, reason, expected version, Idempotency-Key, audit) now also stores one
/// <c>counterparty.party.deactivated.v1</c> outbox row in the same save. Only a real active-to-passive change writes it.
/// </summary>
public sealed class CounterpartyDeactivationOutboxSqlServerRelationalAcceptanceTests
{
    private const string PreviousMigration = "20261031143822_AddCustomerEDocumentProfiles";
    private const string CommandName = "counterparty.company-account.set-active-state.v1";
    private const string OutboxCountSql =
        "SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [CompanyId] = @CompanyId;";

    [SqlServerFact]
    public async Task Deactivation_writes_one_outbox_event_and_replay_no_op_reactivation_and_refusals_write_none()
    {
        await using var run = await Run.StartAsync();
        using var admin = run.Factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        var partyId = await run.CreatePartyAsync(admin, "SQL Pasife Alma Olay Cari A.Ş.");
        var accountId = await run.AccountIdAsync(partyId);

        // Refusals before any change: a wrong version, a missing party, an empty reason and a reader without the
        // status permission store no outbox row.
        using (var stale = await run.PutStateAsync(admin, partyId, false, 7, "Eski sürüm", NewKey()))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }

        using (var missing = await run.PutStateAsync(admin, Guid.NewGuid(), false, 1, "Olmayan cari", NewKey()))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using (var blank = await run.PutStateAsync(admin, partyId, false, 1, "   ", NewKey()))
        {
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        }

        using (var viewer = run.Factory.CreateClientFor(TestUsers.CompanyAViewer, TestCompanyIds.CompanyA))
        using (var forbidden = await run.PutStateAsync(viewer, partyId, false, 1, "Yetkisiz", NewKey()))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, await run.OutboxCountAsync(TestCompanyIds.CompanyA));

        var deactivateKey = NewKey();
        var correlation = $"cari-pasif-correlation-{Guid.NewGuid():N}";
        using (var deactivated = await run.PutStateAsync(
                   admin, partyId, false, 1, "  Çalışma sona erdi  ", deactivateKey, correlation))
        {
            Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        }

        var row = Assert.Single(await run.ReadOutboxAsync(TestCompanyIds.CompanyA));
        Assert.Equal(CounterpartyPartyDeactivatedOutboxV1.EventType, row.EventType);
        Assert.Equal(CounterpartyPartyDeactivatedOutboxV1.SchemaVersion, row.SchemaVersion);
        Assert.Equal(CounterpartyPartyDeactivatedOutboxV1.SourceOperation, row.SourceOperation);
        Assert.Equal(CounterpartyPartyDeactivatedOutboxV1.AggregateType, row.AggregateType);
        Assert.Equal(accountId, row.AggregateId);
        Assert.Equal(2L, row.AggregateVersion);
        Assert.Equal(2L, row.Sequence);
        Assert.Equal(correlation, row.CorrelationId);
        using (var payload = JsonDocument.Parse(row.PayloadJson))
        {
            Assert.Equal(
                ["companyId", "partyId", "reason", "version"],
                payload.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal(TestCompanyIds.CompanyA, payload.RootElement.GetProperty("companyId").GetGuid());
            Assert.Equal(partyId, payload.RootElement.GetProperty("partyId").GetGuid());
            Assert.Equal("Çalışma sona erdi", payload.RootElement.GetProperty("reason").GetString());
            Assert.Equal(2L, payload.RootElement.GetProperty("version").GetInt64());
        }

        // The event commits with the account change, the command execution and the audit event.
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT CONVERT(int, [IsActive]) FROM [counterparty].[CompanyPartyAccounts] WHERE [Id] = @Id;",
            new SqlParameter("@Id", accountId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyId AND [EntityId] = @EntityId AND [Operation] = N'Deactivate';",
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
            new SqlParameter("@EntityId", accountId.ToString("D"))));

        // The same key replays the stored 204 and writes nothing new.
        using (var replay = await run.PutStateAsync(
                   admin, partyId, false, 1, "  Çalışma sona erdi  ", deactivateKey))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(1, await run.OutboxCountAsync(TestCompanyIds.CompanyA));

        // A passive-to-passive save (version 2 -> 3) and a reactivation (3 -> 4) are not deactivations.
        using (var noOp = await run.PutStateAsync(admin, partyId, false, 2, "Tekrar pasif", NewKey()))
        {
            Assert.Equal(HttpStatusCode.NoContent, noOp.StatusCode);
        }

        using (var reactivated = await run.PutStateAsync(admin, partyId, true, 3, "Yeniden çalışma", NewKey()))
        {
            Assert.Equal(HttpStatusCode.NoContent, reactivated.StatusCode);
        }

        Assert.Equal(1, await run.OutboxCountAsync(TestCompanyIds.CompanyA));

        // A second real deactivation (4 -> 5) is a second event keyed by the new account version.
        using (var again = await run.PutStateAsync(admin, partyId, false, 4, "İkinci kez pasif", NewKey()))
        {
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        }

        var rows = await run.ReadOutboxAsync(TestCompanyIds.CompanyA);
        Assert.Equal([2L, 5L], rows.Select(outbox => outbox.Sequence));
        Assert.All(rows, outbox => Assert.Equal(accountId, outbox.AggregateId));
        Assert.Equal(0, await run.OutboxCountAsync(TestCompanyIds.CompanyB));
    }

    [SqlServerFact]
    public async Task Concurrent_deactivations_with_different_keys_write_exactly_one_outbox_event()
    {
        var probe = new TwoParticipantActiveStateProbe();
        await using var run = await Run.StartAsync(services =>
        {
            services.RemoveAll<ICounterpartyActiveStateCommandProbe>();
            services.AddSingleton<ICounterpartyActiveStateCommandProbe>(probe);
        });
        using var admin = run.Factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        var partyId = await run.CreatePartyAsync(admin, "SQL Eşzamanlı Pasife Alma Cari A.Ş.");
        using var first = run.Factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        using var second = run.Factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);

        var requests = new[]
        {
            run.PutStateAsync(first, partyId, false, 1, "Eşzamanlı pasif A", NewKey()),
            run.PutStateAsync(second, partyId, false, 1, "Eşzamanlı pasif B", NewKey()),
        };
        try
        {
            await probe.AllParticipantsArrived.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            probe.Release();
        }

        var responses = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.NoContent));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            var row = Assert.Single(await run.ReadOutboxAsync(TestCompanyIds.CompanyA));
            Assert.Equal(2L, row.Sequence);
            Assert.Equal(1, await run.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = N'{CommandName}' AND [Outcome] = N'Succeeded';",
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyA)));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [SqlServerFact]
    public async Task The_outbox_table_rejects_invalid_json_and_a_duplicate_aggregate_sequence()
    {
        await using var run = await Run.StartAsync();
        var aggregateId = Guid.NewGuid();

        await run.InsertRawOutboxAsync(TestCompanyIds.CompanyA, aggregateId, 2, "{\"reason\":\"ham\"}");
        var invalidJson = await Assert.ThrowsAsync<SqlException>(() =>
            run.InsertRawOutboxAsync(TestCompanyIds.CompanyA, aggregateId, 3, "not-json"));
        Assert.Equal(547, invalidJson.Number);
        var duplicate = await Assert.ThrowsAsync<SqlException>(() =>
            run.InsertRawOutboxAsync(TestCompanyIds.CompanyA, aggregateId, 2, "{\"reason\":\"kopya\"}"));
        Assert.Equal(2601, duplicate.Number);
        Assert.Equal(1, await run.OutboxCountAsync(TestCompanyIds.CompanyA));
    }

    [SqlServerFact]
    public async Task The_outbox_migrations_Down_passes_with_an_empty_outbox_and_refuses_while_an_event_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddCounterpartyOutbox().DownOperations[0]).Sql;

        // An empty outbox rolls back freely and migrates forward again.
        await run.ExecuteAsync(guard);
        await run.MigrateCounterpartyToAsync(PreviousMigration);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [object_id] = OBJECT_ID(N'[counterparty].[OutboxMessages]');"));
        await run.MigrateCounterpartyToAsync(null);

        using var admin = run.Factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        var partyId = await run.CreatePartyAsync(admin, "SQL Geri Alma Korumalı Cari A.Ş.");
        using (var deactivated = await run.PutStateAsync(admin, partyId, false, 1, "Geri alma koruması", NewKey()))
        {
            Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51441, refused.Number);
        var migratorRefused = await Assert.ThrowsAnyAsync<SqlException>(() =>
            run.MigrateCounterpartyToAsync(PreviousMigration));
        Assert.Equal(51441, migratorRefused.Number);
        Assert.Equal(1, await run.OutboxCountAsync(TestCompanyIds.CompanyA));
    }

    private static string NewKey() => $"cari-pasif-{Guid.NewGuid():N}";

    private sealed record OutboxSnapshot(
        string EventType,
        int SchemaVersion,
        string SourceOperation,
        string AggregateType,
        Guid AggregateId,
        long AggregateVersion,
        long Sequence,
        string PayloadJson,
        string CorrelationId);

    private sealed class Run : IAsyncDisposable
    {
        private readonly string _baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            _baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync(Action<IServiceCollection>? configureServices = null)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var run = new Run(
                baseConnectionString,
                new SqlServerRelationalApiFactory(baseConnectionString, configureServices));
            using var health = run.Factory.CreateClient();
            using var live = await health.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            return run;
        }

        public async Task<Guid> CreatePartyAsync(HttpClient client, string name)
        {
            SetRequestIdentity(client, NewKey(), $"cari-pasif-create-{Guid.NewGuid():N}");
            using var response = await client.PostAsJsonAsync(
                "/api/v1/counterparties",
                new CreateCounterparty(PartyKind.LegalEntity, name, null, PartyRole.Customer));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("id").GetGuid();
        }

        public Task<Guid> AccountIdAsync(Guid partyId) => ScalarAsync<Guid>(
            "SELECT [Id] FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
            new SqlParameter("@PartyId", partyId));

        public Task<HttpResponseMessage> PutStateAsync(
            HttpClient client,
            Guid partyId,
            bool isActive,
            long expectedVersion,
            string reason,
            string idempotencyKey,
            string? correlationId = null)
        {
            SetRequestIdentity(
                client,
                idempotencyKey,
                correlationId ?? $"cari-pasif-correlation-{Guid.NewGuid():N}");
            return client.PutAsJsonAsync(
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                new SetCompanyPartyAccountActiveState(
                    isActive,
                    expectedVersion,
                    reason,
                    // ALS-D99: no owner warning source in the test host, so every line is unavailable and needs it.
                    ExposureWarningAcknowledged: !isActive));
        }

        public Task<int> OutboxCountAsync(Guid companyId) =>
            ScalarAsync<int>(OutboxCountSql, new SqlParameter("@CompanyId", companyId));

        public async Task<IReadOnlyList<OutboxSnapshot>> ReadOutboxAsync(Guid companyId)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT [EventType], [SchemaVersion], [SourceOperation], [AggregateType], [AggregateId],
                       [AggregateVersion], [Sequence], [PayloadJson], [CorrelationId]
                FROM [counterparty].[OutboxMessages]
                WHERE [CompanyId] = @CompanyId
                ORDER BY [Sequence];
                """;
            command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            var rows = new List<OutboxSnapshot>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new OutboxSnapshot(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetGuid(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetString(7),
                    reader.GetString(8)));
            }

            return rows;
        }

        public Task InsertRawOutboxAsync(Guid companyId, Guid aggregateId, long sequence, string payloadJson) =>
            ExecuteAsync(
                """
                INSERT INTO [counterparty].[OutboxMessages]
                    ([Id], [CompanyId], [EventType], [SchemaVersion], [SourceOperation], [AggregateType], [AggregateId],
                     [AggregateVersion], [Sequence], [PayloadJson], [OccurredAtUtc], [CorrelationId], [CausationId],
                     [RecordedAtUtc])
                VALUES
                    (NEWID(), @CompanyId, @EventType, 1, @SourceOperation, @AggregateType, @AggregateId,
                     @Sequence, @Sequence, @PayloadJson, SYSDATETIMEOFFSET(), N'sql-raw-correlation', NULL,
                     SYSDATETIMEOFFSET());
                """,
                new SqlParameter("@CompanyId", companyId),
                new SqlParameter("@EventType", CounterpartyPartyDeactivatedOutboxV1.EventType),
                new SqlParameter("@SourceOperation", CounterpartyPartyDeactivatedOutboxV1.SourceOperation),
                new SqlParameter("@AggregateType", CounterpartyPartyDeactivatedOutboxV1.AggregateType),
                new SqlParameter("@AggregateId", aggregateId),
                new SqlParameter("@Sequence", sequence),
                new SqlParameter("@PayloadJson", payloadJson));

        public async Task MigrateCounterpartyToAsync(string? migration)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            await dbContext.GetService<IMigrator>().MigrateAsync(migration);
        }

        public async Task<T> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddRange(parameters);
            return Assert.IsType<T>(await command.ExecuteScalarAsync());
        }

        public async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddRange(parameters);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            var databaseName = Factory.DatabaseName;
            await Factory.DisposeAsync();
            var builder = new SqlConnectionStringBuilder(_baseConnectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private static void SetRequestIdentity(HttpClient client, string idempotencyKey, string correlationId)
        {
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
        }
    }

    /// <summary>Holds both deactivations after their expected-version check so they race on the same account version.</summary>
    private sealed class TwoParticipantActiveStateProbe : ICounterpartyActiveStateCommandProbe
    {
        private const int Participants = 2;
        private readonly TaskCompletionSource _allParticipantsArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivalCount;

        public Task AllParticipantsArrived => _allParticipantsArrived.Task;

        public async ValueTask AfterExpectedVersionValidatedAsync(
            CounterpartyActiveStateCommandObservation observation,
            CancellationToken cancellationToken)
        {
            var arrival = Interlocked.Increment(ref _arrivalCount);
            if (arrival > Participants)
            {
                throw new InvalidOperationException("Pasife alma yarış probu beklenenden fazla katılımcı aldı.");
            }

            if (arrival == Participants)
            {
                _allParticipantsArrived.TrySetResult();
            }

            await _release.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _release.TrySetResult();
    }
}
