using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyNoOpConcurrencySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Different_key_no_op_status_commands_are_serialized()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

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

            var responses = await Task.WhenAll(
                clientA.PutAsJsonAsync(
                    $"/api/v1/counterparties/{partyId}/company-account/active-state",
                    command),
                clientB.PutAsJsonAsync(
                    $"/api/v1/counterparties/{partyId}/company-account/active-state",
                    command));
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
                        "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] IN (@First, @Second) AND [EventType] = N'counterparty.company-account.active-state.changed.v1' AND [Operation] = N'NoChange';",
                        new SqlParameter("@First", correlations[0]),
                        new SqlParameter("@Second", correlations[1])));
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

                var conflictIndex = Array.FindIndex(
                    responses,
                    response => response.StatusCode == HttpStatusCode.Conflict);
                Assert.InRange(conflictIndex, 0, 1);
                var conflictClient = conflictIndex == 0 ? clientA : clientB;
                var replayCorrelation = $"cari-noop-replay-{Guid.NewGuid():N}";
                SetRequestIdentity(conflictClient, keys[conflictIndex], replayCorrelation);
                using var replay = await conflictClient.PutAsJsonAsync(
                    $"/api/v1/counterparties/{partyId}/company-account/active-state",
                    command);
                Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
                Assert.Equal(
                    "true",
                    Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
                Assert.Equal(
                    correlations[conflictIndex],
                    Assert.Single(replay.Headers.GetValues(
                        "Idempotency-Original-Correlation-Id")));
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
}
