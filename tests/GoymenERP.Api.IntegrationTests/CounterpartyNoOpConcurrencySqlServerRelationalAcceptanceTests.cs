using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyNoOpConcurrencySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Different_key_no_op_status_commands_are_serialized()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var concurrencyProbe = new DeterministicActiveStateConcurrencyProbe(2);
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            services =>
            {
                services.RemoveAll<ICounterpartyActiveStateCommandProbe>();
                services.AddSingleton<ICounterpartyActiveStateCommandProbe>(concurrencyProbe);
            });

        try
        {
            using var healthClient = factory.CreateClient();
            using var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();

            using var createClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            SetRequestIdentity(
                createClient,
                $"cari-noop-create-{Guid.NewGuid():N}",
                $"cari-noop-create-correlation-{Guid.NewGuid():N}");
            using var createResponse = await createClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                new CreateCounterparty(
                    PartyKind.LegalEntity,
                    "SQL No-Op Concurrency Cari A.Ş.",
                    null,
                    PartyRole.Customer));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            using var createJson = JsonDocument.Parse(
                await createResponse.Content.ReadAsStringAsync());
            var partyId = createJson.RootElement.GetProperty("id").GetGuid();
            var accountId = await ScalarAsync<Guid>(
                connection,
                "SELECT [Id] FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                new SqlParameter("@PartyId", partyId));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    connection,
                    "SELECT [Version] FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@PartyId", partyId)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT CONVERT(int, [IsActive]) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@PartyId", partyId)));

            var keys = new[]
            {
                $"cari-noop-key-a-{Guid.NewGuid():N}",
                $"cari-noop-key-b-{Guid.NewGuid():N}",
            };
            var correlations = new[]
            {
                $"cari-noop-correlation-a-{Guid.NewGuid():N}",
                $"cari-noop-correlation-b-{Guid.NewGuid():N}",
            };
            using var clientA = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            using var clientB = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            SetRequestIdentity(clientA, keys[0], correlations[0]);
            SetRequestIdentity(clientB, keys[1], correlations[1]);
            var command = new SetCompanyPartyAccountActiveState(
                true,
                1,
                "No-op optimistic concurrency doğrulaması");

            var responseTasks = new[]
            {
                clientA.PutAsJsonAsync(
                    $"/api/v1/counterparties/{partyId}/company-account/active-state",
                    command),
                clientB.PutAsJsonAsync(
                    $"/api/v1/counterparties/{partyId}/company-account/active-state",
                    command),
            };
            try
            {
                await concurrencyProbe.AllParticipantsArrived.WaitAsync(TimeSpan.FromSeconds(30));
                var observations = concurrencyProbe.Observations;
                Assert.Equal(2, observations.Count);
                Assert.All(observations, observation =>
                {
                    Assert.Equal(TestCompanyIds.CompanyA, observation.CompanyId);
                    Assert.Equal(partyId, observation.PartyId);
                    Assert.Equal(1L, observation.ExpectedVersion);
                    Assert.Equal(1L, observation.LoadedVersion);
                });
                Assert.Equal(
                    correlations.Order(StringComparer.Ordinal),
                    observations
                        .Select(observation => observation.CorrelationId)
                        .Order(StringComparer.Ordinal));
                Assert.Equal(
                    keys
                        .Select(IdempotencyHash.ComputeKey)
                        .Order(StringComparer.Ordinal),
                    observations
                        .Select(observation => observation.IdempotencyKeyHash)
                        .Order(StringComparer.Ordinal));
            }
            finally
            {
                concurrencyProbe.Release();
            }

            var responses = await Task.WhenAll(responseTasks)
                .WaitAsync(TimeSpan.FromSeconds(30));
            try
            {
                Assert.Equal(
                    1,
                    responses.Count(response => response.StatusCode == HttpStatusCode.NoContent));
                Assert.Equal(
                    1,
                    responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

                Assert.Equal(
                    2L,
                    await ScalarAsync<long>(
                        connection,
                        "SELECT [Version] FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                        new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                        new SqlParameter("@PartyId", partyId)));
                Assert.Equal(
                    1,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT CONVERT(int, [IsActive]) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                        new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                        new SqlParameter("@PartyId", partyId)));
                Assert.Equal(
                    1,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyId AND [EntityType] = N'Counterparty.CompanyPartyAccount' AND [EntityId] = @EntityId AND [EventType] = N'counterparty.company-account.active-state.changed.v1' AND [Operation] = N'NoChange' AND [IdempotencyKeyHash] IN (@FirstKeyHash, @SecondKeyHash);",
                        new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                        new SqlParameter("@EntityId", accountId.ToString("D")),
                        new SqlParameter("@FirstKeyHash", IdempotencyHash.ComputeKey(keys[0])),
                        new SqlParameter("@SecondKeyHash", IdempotencyHash.ComputeKey(keys[1]))));
                Assert.Equal(
                    1,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [OriginalCorrelationId] IN (@First, @Second) AND [CommandName] = N'counterparty.company-account.set-active-state.v1' AND [Outcome] = N'Succeeded';",
                        new SqlParameter("@First", correlations[0]),
                        new SqlParameter("@Second", correlations[1])));
                Assert.Equal(
                    1,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [OriginalCorrelationId] IN (@First, @Second) AND [CommandName] = N'counterparty.company-account.set-active-state.v1' AND [Outcome] = N'VersionConflict';",
                        new SqlParameter("@First", correlations[0]),
                        new SqlParameter("@Second", correlations[1])));

                var versionBeforeReplay = await ScalarAsync<long>(
                    connection,
                    "SELECT [Version] FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@PartyId", partyId));
                var rowVersionBeforeReplay = await ScalarAsync<byte[]>(
                    connection,
                    "SELECT [RowVersion] FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@PartyId", partyId));
                var auditCountBeforeReplay = await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyId AND [EntityType] = N'Counterparty.CompanyPartyAccount' AND [EntityId] = @EntityId AND [EventType] = N'counterparty.company-account.active-state.changed.v1' AND [IdempotencyKeyHash] IN (@FirstKeyHash, @SecondKeyHash);",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@EntityId", accountId.ToString("D")),
                    new SqlParameter("@FirstKeyHash", IdempotencyHash.ComputeKey(keys[0])),
                    new SqlParameter("@SecondKeyHash", IdempotencyHash.ComputeKey(keys[1])));
                var executionCountBeforeReplay = await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = N'counterparty.company-account.set-active-state.v1' AND [IdempotencyKeyHash] IN (@FirstKeyHash, @SecondKeyHash);",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@FirstKeyHash", IdempotencyHash.ComputeKey(keys[0])),
                    new SqlParameter("@SecondKeyHash", IdempotencyHash.ComputeKey(keys[1])));
                Assert.Equal(2L, versionBeforeReplay);
                Assert.Equal(1, auditCountBeforeReplay);
                Assert.Equal(2, executionCountBeforeReplay);

                var successIndex = Array.FindIndex(
                    responses,
                    response => response.StatusCode == HttpStatusCode.NoContent);
                var conflictIndex = Array.FindIndex(
                    responses,
                    response => response.StatusCode == HttpStatusCode.Conflict);
                Assert.InRange(successIndex, 0, 1);
                Assert.InRange(conflictIndex, 0, 1);
                Assert.NotEqual(successIndex, conflictIndex);
                Assert.Equal(
                    new CommandExecutionSnapshot(
                        "Succeeded",
                        (int)HttpStatusCode.NoContent,
                        correlations[successIndex]),
                    await ReadCommandExecutionAsync(
                        connection,
                        IdempotencyHash.ComputeKey(keys[successIndex])));
                Assert.Equal(
                    new CommandExecutionSnapshot(
                        "VersionConflict",
                        (int)HttpStatusCode.Conflict,
                        correlations[conflictIndex]),
                    await ReadCommandExecutionAsync(
                        connection,
                        IdempotencyHash.ComputeKey(keys[conflictIndex])));
                await AssertVersionConflictAsync(responses[conflictIndex]);
                Assert.Equal(
                    string.Empty,
                    await responses[successIndex].Content.ReadAsStringAsync());

                var clients = new[] { clientA, clientB };
                foreach (var replayIndex in new[] { successIndex, conflictIndex })
                {
                    SetRequestIdentity(
                        clients[replayIndex],
                        keys[replayIndex],
                        $"cari-noop-replay-{Guid.NewGuid():N}");
                    using var replay = await clients[replayIndex].PutAsJsonAsync(
                        $"/api/v1/counterparties/{partyId}/company-account/active-state",
                        command);
                    Assert.Equal(responses[replayIndex].StatusCode, replay.StatusCode);
                    Assert.Equal(
                        "true",
                        Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
                    Assert.Equal(
                        correlations[replayIndex],
                        Assert.Single(replay.Headers.GetValues(
                            "Idempotency-Original-Correlation-Id")));
                    if (replayIndex == conflictIndex)
                    {
                        await AssertVersionConflictAsync(replay);
                    }
                    else
                    {
                        Assert.Equal(
                            string.Empty,
                            await replay.Content.ReadAsStringAsync());
                    }
                }

                Assert.Equal(
                    versionBeforeReplay,
                    await ScalarAsync<long>(
                        connection,
                        "SELECT [Version] FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                        new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                        new SqlParameter("@PartyId", partyId)));
                Assert.Equal(
                    rowVersionBeforeReplay,
                    await ScalarAsync<byte[]>(
                        connection,
                        "SELECT [RowVersion] FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                        new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                        new SqlParameter("@PartyId", partyId)));
                Assert.Equal(
                    auditCountBeforeReplay,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyId AND [EntityType] = N'Counterparty.CompanyPartyAccount' AND [EntityId] = @EntityId AND [EventType] = N'counterparty.company-account.active-state.changed.v1' AND [IdempotencyKeyHash] IN (@FirstKeyHash, @SecondKeyHash);",
                        new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                        new SqlParameter("@EntityId", accountId.ToString("D")),
                        new SqlParameter("@FirstKeyHash", IdempotencyHash.ComputeKey(keys[0])),
                        new SqlParameter("@SecondKeyHash", IdempotencyHash.ComputeKey(keys[1]))));
                Assert.Equal(
                    executionCountBeforeReplay,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = N'counterparty.company-account.set-active-state.v1' AND [IdempotencyKeyHash] IN (@FirstKeyHash, @SecondKeyHash);",
                        new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                        new SqlParameter("@FirstKeyHash", IdempotencyHash.ComputeKey(keys[0])),
                        new SqlParameter("@SecondKeyHash", IdempotencyHash.ComputeKey(keys[1]))));
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static void SetRequestIdentity(
        HttpClient client,
        string idempotencyKey,
        string correlationId)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
    }

    private static async Task<T> ScalarAsync<T>(
        SqlConnection connection,
        string commandText,
        params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        return Assert.IsType<T>(value);
    }

    private static async Task<CommandExecutionSnapshot> ReadCommandExecutionAsync(
        SqlConnection connection,
        string idempotencyKeyHash)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [Outcome], [HttpStatusCode], [OriginalCorrelationId]
            FROM [idempotency].[CommandExecutions]
            WHERE [CompanyId] = @CompanyId
              AND [CommandName] = N'counterparty.company-account.set-active-state.v1'
              AND [IdempotencyKeyHash] = @IdempotencyKeyHash;
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", TestCompanyIds.CompanyA));
        command.Parameters.Add(new SqlParameter("@IdempotencyKeyHash", idempotencyKeyHash));

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var snapshot = new CommandExecutionSnapshot(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2));
        Assert.False(await reader.ReadAsync());
        return snapshot;
    }

    private static async Task AssertVersionConflictAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(
            "version_conflict",
            document.RootElement.GetProperty("code").GetString());
        var conflict = document.RootElement.GetProperty("conflict");
        Assert.Equal(1L, conflict.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(2L, conflict.GetProperty("currentVersion").GetInt64());
    }

    private static async Task DropDatabaseAsync(
        string baseConnectionString,
        string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
        };
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

    private sealed class DeterministicActiveStateConcurrencyProbe(int participants)
        : ICounterpartyActiveStateCommandProbe
    {
        private readonly ConcurrentQueue<CounterpartyActiveStateCommandObservation>
            _observations = new();
        private readonly TaskCompletionSource _allParticipantsArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivalCount;

        public Task AllParticipantsArrived => _allParticipantsArrived.Task;

        public IReadOnlyList<CounterpartyActiveStateCommandObservation> Observations =>
            _observations.ToArray();

        public async ValueTask AfterExpectedVersionValidatedAsync(
            CounterpartyActiveStateCommandObservation observation,
            CancellationToken cancellationToken)
        {
            _observations.Enqueue(observation);
            var arrival = Interlocked.Increment(ref _arrivalCount);
            if (arrival > participants)
            {
                throw new InvalidOperationException(
                    "Deterministik yarış probu beklenenden fazla katılımcı aldı.");
            }

            if (arrival == participants)
            {
                _allParticipantsArrived.TrySetResult();
            }

            await _release.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed record CommandExecutionSnapshot(
        string Outcome,
        int HttpStatusCode,
        string OriginalCorrelationId);
}
