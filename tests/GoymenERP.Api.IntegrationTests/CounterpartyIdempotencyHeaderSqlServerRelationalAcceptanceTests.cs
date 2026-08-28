using System.Net;
using System.Net.Http.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyIdempotencyHeaderSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task MultipleOrCommaJoinedIdempotencyKeys_AreRejectedFailClosed()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using var healthClient = factory.CreateClient();
            using var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

            const string legalName = "SQL Çoklu Idempotency Header Red Cari A.Ş.";
            var command = new CreateCounterparty(
                PartyKind.LegalEntity,
                legalName,
                null,
                PartyRole.Customer);

            using var multipleHeaderClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            multipleHeaderClient.DefaultRequestHeaders.Remove("Idempotency-Key");
            multipleHeaderClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "Idempotency-Key",
                new[]
                {
                    $"cari-multi-a-{Guid.NewGuid():N}",
                    $"cari-multi-b-{Guid.NewGuid():N}",
                });
            multipleHeaderClient.DefaultRequestHeaders.Remove("X-Correlation-Id");
            multipleHeaderClient.DefaultRequestHeaders.Add(
                "X-Correlation-Id",
                $"cari-multi-correlation-{Guid.NewGuid():N}");

            using var multipleHeaderResponse = await multipleHeaderClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                command);
            Assert.Equal(HttpStatusCode.BadRequest, multipleHeaderResponse.StatusCode);

            using var commaJoinedClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            commaJoinedClient.DefaultRequestHeaders.Remove("Idempotency-Key");
            commaJoinedClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "Idempotency-Key",
                $"cari-comma-a-{Guid.NewGuid():N},cari-comma-b-{Guid.NewGuid():N}");
            commaJoinedClient.DefaultRequestHeaders.Remove("X-Correlation-Id");
            commaJoinedClient.DefaultRequestHeaders.Add(
                "X-Correlation-Id",
                $"cari-comma-correlation-{Guid.NewGuid():N}");

            using var commaJoinedResponse = await commaJoinedClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                command);
            Assert.Equal(HttpStatusCode.BadRequest, commaJoinedResponse.StatusCode);

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var sql = connection.CreateCommand();
            sql.CommandText =
                "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [LegalName] = @LegalName;";
            sql.Parameters.AddWithValue("@LegalName", legalName);
            var count = Convert.ToInt32(await sql.ExecuteScalarAsync());
            Assert.Equal(0, count);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
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
