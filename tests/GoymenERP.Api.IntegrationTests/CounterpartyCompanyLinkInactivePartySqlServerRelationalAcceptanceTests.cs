using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyCompanyLinkInactivePartySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task GloballyInactiveParty_IsNonDisclosingAndDurablyReplayedWithoutEffects()
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

            var created = await CreatePartyAsync(factory);
            Assert.Equal(
                1,
                await ExecuteNonQueryAsync(
                    connection,
                    """
                    UPDATE [counterparty].[Parties]
                    SET [IsActive] = 0,
                        [Version] = [Version] + 1,
                        [UpdatedAtUtc] = SYSUTCDATETIME()
                    WHERE [Id] = @PartyId
                      AND [IsActive] = 1;
                    """,
                    new SqlParameter("@PartyId", created.PartyId)));

            var key = $"cari-link-inactive-{Guid.NewGuid():N}";
            var firstCorrelation = $"cari-link-inactive-first-{Guid.NewGuid():N}";
            using var targetClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(targetClient, key, firstCorrelation);

            using var first = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{created.PartyId}/company-account",
                new CreateCompanyPartyAccount(PartyRole.Supplier));

            Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
            Assert.False(first.Headers.Contains("Idempotency-Replayed"));

            var replayCorrelation = $"cari-link-inactive-replay-{Guid.NewGuid():N}";
            SetRequestIdentity(targetClient, key, replayCorrelation);
            using var replay = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{created.PartyId}/company-account",
                new CreateCompanyPartyAccount(PartyRole.Supplier));

            Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);
            Assert.Equal(
                "true",
                Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                firstCorrelation,
                Assert.Single(replay.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));

            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COUNT(*)
                    FROM [counterparty].[Parties]
                    WHERE [Id] = @PartyId
                      AND [IsActive] = 0;
                    """,
                    new SqlParameter("@PartyId", created.PartyId)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COUNT(*)
                    FROM [counterparty].[CompanyPartyAccounts]
                    WHERE [CompanyId] = @CompanyA
                      AND [PartyId] = @PartyId
                      AND [IsActive] = 1;
                    """,
                    new SqlParameter("@CompanyA", TestCompanyIds.CompanyA),
                    new SqlParameter("@PartyId", created.PartyId)));
            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COUNT(*)
                    FROM [counterparty].[CompanyPartyAccounts]
                    WHERE [CompanyId] = @CompanyB
                      AND [PartyId] = @PartyId;
                    """,
                    new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                    new SqlParameter("@PartyId", created.PartyId)));
            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COUNT(*)
                    FROM [audit].[AuditEvents]
                    WHERE [CompanyId] = @CompanyB
                      AND [EventType] = N'counterparty.company-account.created.v1'
                      AND [CorrelationId] IN (@FirstCorrelation, @ReplayCorrelation);
                    """,
                    new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                    new SqlParameter("@FirstCorrelation", firstCorrelation),
                    new SqlParameter("@ReplayCorrelation", replayCorrelation)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COUNT(*)
                    FROM [idempotency].[CommandExecutions]
                    WHERE [CompanyId] = @CompanyB
                      AND [CommandName] = N'counterparty.company-account.create.v1'
                      AND [IdempotencyKeyHash] = @KeyHash
                      AND [Outcome] = N'NotFound'
                      AND [HttpStatusCode] = 404
                      AND [OriginalCorrelationId] = @FirstCorrelation;
                    """,
                    new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                    new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(key)),
                    new SqlParameter("@FirstCorrelation", firstCorrelation)));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task<CreatedParty> CreatePartyAsync(
        SqlServerRelationalApiFactory factory)
    {
        using var sourceClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(
            sourceClient,
            $"cari-inactive-source-{Guid.NewGuid():N}",
            $"cari-inactive-source-correlation-{Guid.NewGuid():N}");

        using var response = await sourceClient.PostAsJsonAsync(
            "/api/v1/counterparties",
            new CreateCounterparty(
                PartyKind.LegalEntity,
                "SQL Global Pasif Cari Bağlama Testi A.Ş.",
                null,
                PartyRole.Customer));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new CreatedParty(document.RootElement.GetProperty("id").GetGuid());
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

    private static async Task<int> ExecuteNonQueryAsync(
        SqlConnection connection,
        string commandText,
        params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
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

    private sealed record CreatedParty(Guid PartyId);
}
