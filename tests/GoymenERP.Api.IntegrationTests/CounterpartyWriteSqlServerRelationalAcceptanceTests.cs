using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyWriteSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task WriteIdempotencyAndTerminalReplay_AreDurable()
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

            var createKey = $"cari-create-{Guid.NewGuid():N}";
            var createCorrelation = $"cari-create-first-{Guid.NewGuid():N}";
            using var createClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            SetRequestIdentity(createClient, createKey, createCorrelation);
            var createCommand = new CreateCounterparty(
                PartyKind.LegalEntity,
                "SQL Replay Cari A.Ş.",
                "SQL Replay",
                PartyRole.Customer | PartyRole.Supplier);

            using var firstCreate = await createClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                createCommand);
            Assert.Equal(HttpStatusCode.Created, firstCreate.StatusCode);
            Assert.False(firstCreate.Headers.Contains("Idempotency-Replayed"));
            using var firstCreateJson = JsonDocument.Parse(
                await firstCreate.Content.ReadAsStringAsync());
            var partyId = firstCreateJson.RootElement.GetProperty("id").GetGuid();
            var partyCode = firstCreateJson.RootElement.GetProperty("code").GetString();
            Assert.False(string.IsNullOrWhiteSpace(partyCode));

            var replayCorrelation = $"cari-create-replay-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, createKey, replayCorrelation);
            using var replayCreate = await createClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                createCommand);
            Assert.Equal(HttpStatusCode.Created, replayCreate.StatusCode);
            Assert.Equal(
                "true",
                Assert.Single(replayCreate.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                createCorrelation,
                Assert.Single(replayCreate.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));
            using var replayCreateJson = JsonDocument.Parse(
                await replayCreate.Content.ReadAsStringAsync());
            Assert.Equal(
                partyId,
                replayCreateJson.RootElement.GetProperty("id").GetGuid());
            Assert.Equal(
                partyCode,
                replayCreateJson.RootElement.GetProperty("code").GetString());

            var mismatchCorrelation = $"cari-create-mismatch-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, createKey, mismatchCorrelation);
            using var mismatchCreate = await createClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                createCommand with { LegalName = "Farklı Payload A.Ş." });
            Assert.Equal(HttpStatusCode.Conflict, mismatchCreate.StatusCode);
            Assert.False(mismatchCreate.Headers.Contains("Idempotency-Replayed"));

            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id] = @PartyId;",
                    new SqlParameter("@PartyId", partyId)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@PartyId", partyId)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = N'counterparty.party.create.v1' AND [IdempotencyKeyHash] = @KeyHash;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(createKey))));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyId AND [CorrelationId] = @CorrelationId AND [EventType] = N'counterparty.party.created.v1';",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@CorrelationId", createCorrelation)));

            var statusKey = $"cari-status-{Guid.NewGuid():N}";
            var statusCorrelation = $"cari-status-first-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, statusKey, statusCorrelation);
            using var firstStatus = await createClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                new SetCompanyPartyAccountActiveState(
                    false,
                    1,
                    "Gerçek SQL durum güncellemesi"));
            Assert.Equal(HttpStatusCode.NoContent, firstStatus.StatusCode);

            var staleKey = $"cari-status-stale-{Guid.NewGuid():N}";
            var staleCorrelation = $"cari-status-stale-first-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, staleKey, staleCorrelation);
            var staleCommand = new SetCompanyPartyAccountActiveState(
                true,
                1,
                "Stale sürüm terminal sonucu");
            using var firstStale = await createClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                staleCommand);
            Assert.Equal(HttpStatusCode.Conflict, firstStale.StatusCode);
            Assert.False(firstStale.Headers.Contains("Idempotency-Replayed"));

            var staleReplayCorrelation = $"cari-status-stale-replay-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, staleKey, staleReplayCorrelation);
            using var staleReplay = await createClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                staleCommand);
            Assert.Equal(HttpStatusCode.Conflict, staleReplay.StatusCode);
            Assert.Equal(
                "true",
                Assert.Single(staleReplay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                staleCorrelation,
                Assert.Single(staleReplay.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));

            var missingPartyId = Guid.NewGuid();
            var notFoundKey = $"cari-status-not-found-{Guid.NewGuid():N}";
            var notFoundCorrelation = $"cari-status-not-found-first-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, notFoundKey, notFoundCorrelation);
            var notFoundCommand = new SetCompanyPartyAccountActiveState(
                false,
                1,
                "Olmayan cari terminal sonucu");
            using var firstNotFound = await createClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{missingPartyId}/company-account/active-state",
                notFoundCommand);
            Assert.Equal(HttpStatusCode.NotFound, firstNotFound.StatusCode);
            Assert.False(firstNotFound.Headers.Contains("Idempotency-Replayed"));

            var notFoundReplayCorrelation = $"cari-status-not-found-replay-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, notFoundKey, notFoundReplayCorrelation);
            using var replayNotFound = await createClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{missingPartyId}/company-account/active-state",
                notFoundCommand);
            Assert.Equal(HttpStatusCode.NotFound, replayNotFound.StatusCode);
            Assert.Equal(
                "true",
                Assert.Single(replayNotFound.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                notFoundCorrelation,
                Assert.Single(replayNotFound.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = N'counterparty.company-account.set-active-state.v1' AND [IdempotencyKeyHash] = @KeyHash AND [Outcome] = N'NotFound' AND [OriginalCorrelationId] = @CorrelationId;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(notFoundKey)),
                    new SqlParameter("@CorrelationId", notFoundCorrelation)));
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
