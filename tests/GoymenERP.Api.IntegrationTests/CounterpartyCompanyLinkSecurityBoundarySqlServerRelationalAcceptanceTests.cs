using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyCompanyLinkSecurityBoundarySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task PermissionInputAndHeaderBoundaries_AreFailClosedAndDoNotPoisonValidRetry()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using var healthClient = factory.CreateClient();
            using var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

            var party = await CreatePartyAsync(factory);
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();

            await AssertDefaultDenyLeavesNoEffectsAsync(factory, connection, party.PartyId);
            await AssertInvalidHeadersLeaveNoEffectsAsync(factory, connection, party.PartyId);
            await AssertInvalidRolesLeaveNoEffectsAsync(factory, connection, party.PartyId);

            var validKey = $"cari-link-boundary-valid-{Guid.NewGuid():N}";
            var validCorrelation = $"cari-link-boundary-valid-corr-{Guid.NewGuid():N}";
            using var adminClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(adminClient, validKey, validCorrelation);
            using var validResponse = await adminClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{party.PartyId}/company-account",
                new CreateCompanyPartyAccount(PartyRole.Supplier));

            Assert.Equal(HttpStatusCode.Created, validResponse.StatusCode);
            Assert.Equal(
                1,
                await CountTargetAccountsAsync(connection, party.PartyId));
            Assert.Equal(
                1,
                await CountLinkExecutionsAsync(connection));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyId AND [CorrelationId] = @CorrelationId AND [EventType] = N'counterparty.company-account.created.v1';",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                    new SqlParameter("@CorrelationId", validCorrelation)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = N'counterparty.company-account.create.v1' AND [IdempotencyKeyHash] = @KeyHash AND [Outcome] = N'Created';",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                    new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(validKey))));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task AssertDefaultDenyLeavesNoEffectsAsync(
        SqlServerRelationalApiFactory factory,
        SqlConnection connection,
        Guid partyId)
    {
        var key = $"cari-link-denied-{Guid.NewGuid():N}";
        var correlation = $"cari-link-denied-corr-{Guid.NewGuid():N}";
        using var client = factory.CreateClientFor(
            TestUsers.CompanyB,
            TestCompanyIds.CompanyB);
        SetRequestIdentity(client, key, correlation);

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/counterparties/{partyId}/company-account",
            new CreateCompanyPartyAccount(PartyRole.Customer));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNoTargetEffectsAsync(connection, partyId, correlation);
        Assert.Equal(0, await CountLinkExecutionsAsync(connection));
    }

    private static async Task AssertInvalidHeadersLeaveNoEffectsAsync(
        SqlServerRelationalApiFactory factory,
        SqlConnection connection,
        Guid partyId)
    {
        using var multipleClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyB);
        multipleClient.DefaultRequestHeaders.Remove("Idempotency-Key");
        multipleClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Idempotency-Key",
            [
                $"cari-link-header-a-{Guid.NewGuid():N}",
                $"cari-link-header-b-{Guid.NewGuid():N}",
            ]);
        var multipleCorrelation = $"cari-link-header-multiple-{Guid.NewGuid():N}";
        multipleClient.DefaultRequestHeaders.Remove("X-Correlation-Id");
        multipleClient.DefaultRequestHeaders.Add("X-Correlation-Id", multipleCorrelation);
        using var multipleResponse = await multipleClient.PostAsJsonAsync(
            $"/api/v1/counterparties/{partyId}/company-account",
            new CreateCompanyPartyAccount(PartyRole.Customer));
        Assert.Equal(HttpStatusCode.BadRequest, multipleResponse.StatusCode);
        await AssertNoTargetEffectsAsync(connection, partyId, multipleCorrelation);

        using var commaClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyB);
        commaClient.DefaultRequestHeaders.Remove("Idempotency-Key");
        commaClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Idempotency-Key",
            $"cari-link-comma-a-{Guid.NewGuid():N},cari-link-comma-b-{Guid.NewGuid():N}");
        var commaCorrelation = $"cari-link-header-comma-{Guid.NewGuid():N}";
        commaClient.DefaultRequestHeaders.Remove("X-Correlation-Id");
        commaClient.DefaultRequestHeaders.Add("X-Correlation-Id", commaCorrelation);
        using var commaResponse = await commaClient.PostAsJsonAsync(
            $"/api/v1/counterparties/{partyId}/company-account",
            new CreateCompanyPartyAccount(PartyRole.Customer));
        Assert.Equal(HttpStatusCode.BadRequest, commaResponse.StatusCode);
        await AssertNoTargetEffectsAsync(connection, partyId, commaCorrelation);

        using var missingClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyB);
        missingClient.DefaultRequestHeaders.Remove("Idempotency-Key");
        var missingCorrelation = $"cari-link-header-missing-{Guid.NewGuid():N}";
        missingClient.DefaultRequestHeaders.Remove("X-Correlation-Id");
        missingClient.DefaultRequestHeaders.Add("X-Correlation-Id", missingCorrelation);
        using var missingResponse = await missingClient.PostAsJsonAsync(
            $"/api/v1/counterparties/{partyId}/company-account",
            new CreateCompanyPartyAccount(PartyRole.Customer));
        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
        await AssertNoTargetEffectsAsync(connection, partyId, missingCorrelation);

        Assert.Equal(0, await CountLinkExecutionsAsync(connection));
    }

    private static async Task AssertInvalidRolesLeaveNoEffectsAsync(
        SqlServerRelationalApiFactory factory,
        SqlConnection connection,
        Guid partyId)
    {
        var correlation = $"cari-link-invalid-role-{Guid.NewGuid():N}";
        using var client = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyB);
        SetRequestIdentity(
            client,
            $"cari-link-invalid-role-key-{Guid.NewGuid():N}",
            correlation);

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/counterparties/{partyId}/company-account",
            new CreateCompanyPartyAccount((PartyRole)16));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoTargetEffectsAsync(connection, partyId, correlation);
        Assert.Equal(0, await CountLinkExecutionsAsync(connection));
    }

    private static async Task AssertNoTargetEffectsAsync(
        SqlConnection connection,
        Guid partyId,
        string correlationId)
    {
        Assert.Equal(0, await CountTargetAccountsAsync(connection, partyId));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyId AND [CorrelationId] = @CorrelationId;",
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                new SqlParameter("@CorrelationId", correlationId)));
    }

    private static Task<int> CountTargetAccountsAsync(
        SqlConnection connection,
        Guid partyId)
    {
        return ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
            new SqlParameter("@PartyId", partyId));
    }

    private static Task<int> CountLinkExecutionsAsync(SqlConnection connection)
    {
        return ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = N'counterparty.company-account.create.v1';",
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyB));
    }

    private static async Task<CreatedParty> CreatePartyAsync(
        SqlServerRelationalApiFactory factory)
    {
        using var sourceClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(
            sourceClient,
            $"cari-link-boundary-source-{Guid.NewGuid():N}",
            $"cari-link-boundary-source-corr-{Guid.NewGuid():N}");
        using var response = await sourceClient.PostAsJsonAsync(
            "/api/v1/counterparties",
            new CreateCounterparty(
                PartyKind.LegalEntity,
                "SQL Cari Link Güvenlik Sınırı A.Ş.",
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
        return Assert.IsType<T>(await command.ExecuteScalarAsync());
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
