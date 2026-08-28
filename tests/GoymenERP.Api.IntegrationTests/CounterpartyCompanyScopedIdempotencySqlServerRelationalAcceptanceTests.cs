using System.Net;
using System.Net.Http.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyCompanyScopedIdempotencySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task SameIdempotencyKey_InDifferentCompanies_IsIndependent()
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

            var sharedKey = $"cari-company-scoped-{Guid.NewGuid():N}";
            using var companyAClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            using var companyBClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(
                companyAClient,
                sharedKey,
                $"cari-company-a-{Guid.NewGuid():N}");
            SetRequestIdentity(
                companyBClient,
                sharedKey,
                $"cari-company-b-{Guid.NewGuid():N}");

            using var companyAResponse = await companyAClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                new CreateCounterparty(
                    PartyKind.LegalEntity,
                    "SQL Company A Scoped Cari A.Ş.",
                    null,
                    PartyRole.Customer));
            using var companyBResponse = await companyBClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                new CreateCounterparty(
                    PartyKind.LegalEntity,
                    "SQL Company B Scoped Cari A.Ş.",
                    null,
                    PartyRole.Supplier));

            Assert.Equal(HttpStatusCode.Created, companyAResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Created, companyBResponse.StatusCode);
            Assert.False(companyAResponse.Headers.Contains("Idempotency-Replayed"));
            Assert.False(companyBResponse.Headers.Contains("Idempotency-Replayed"));

            var keyHash = IdempotencyHash.ComputeKey(sharedKey);
            Assert.Equal(
                2,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName] = N'counterparty.party.create.v1' AND [IdempotencyKeyHash] = @KeyHash;",
                    new SqlParameter("@KeyHash", keyHash)));
            Assert.Equal(
                2,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(DISTINCT [CompanyId]) FROM [idempotency].[CommandExecutions] WHERE [CommandName] = N'counterparty.party.create.v1' AND [IdempotencyKeyHash] = @KeyHash;",
                    new SqlParameter("@KeyHash", keyHash)));
            Assert.Equal(
                2,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [LegalName] IN (N'SQL Company A Scoped Cari A.Ş.', N'SQL Company B Scoped Cari A.Ş.');"));
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
