using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task CounterpartyMigrationConstraintsAndGroupRead_AreEnforced()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using var healthClient = factory.CreateClient();
            var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();

            Assert.Equal(
                2,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [counterparty].[__EFMigrationsHistory];"));
            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [counterparty].[Parties];"));

            await AssertInvalidPartyCodeRejectedAsync(connection);

            var partyId = Guid.Parse("61000000-0000-0000-0000-000000000001");
            await InsertPartyAsync(connection, partyId, "CAR-000001", "SQL Ortak Cari A.Ş.");

            await AssertUnknownCompanyRejectedAsync(connection, partyId);

            await InsertAccountAsync(
                connection,
                Guid.Parse("62000000-0000-0000-0000-000000000001"),
                TestCompanyIds.CompanyA,
                partyId,
                roles: 1);
            await InsertAccountAsync(
                connection,
                Guid.Parse("62000000-0000-0000-0000-000000000002"),
                TestCompanyIds.CompanyB,
                partyId,
                roles: 2);

            await AssertDuplicateCompanyPartyRejectedAsync(connection, partyId);
            await AssertGroupAndCompanyReadAsync(factory, partyId);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task AssertInvalidPartyCodeRejectedAsync(SqlConnection connection)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(() => ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO [counterparty].[Parties]
                ([Id], [Code], [Kind], [LegalName], [TradeName], [IsActive], [Version],
                 [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@Id, N'CAR-ABCDEF', N'LegalEntity', N'Geçersiz Kod', NULL, 1, 1,
                 SYSUTCDATETIME(), SYSUTCDATETIME());
            """,
            new SqlParameter("@Id", Guid.NewGuid())));

        Assert.Equal(547, exception.Number);
    }

    private static async Task AssertUnknownCompanyRejectedAsync(
        SqlConnection connection,
        Guid partyId)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(() => InsertAccountAsync(
            connection,
            Guid.NewGuid(),
            Guid.Parse("6f000000-0000-0000-0000-000000000099"),
            partyId,
            roles: 1));

        Assert.Equal(547, exception.Number);
    }

    private static async Task AssertDuplicateCompanyPartyRejectedAsync(
        SqlConnection connection,
        Guid partyId)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(() => InsertAccountAsync(
            connection,
            Guid.NewGuid(),
            TestCompanyIds.CompanyA,
            partyId,
            roles: 1));

        Assert.Contains(exception.Number, new[] { 2601, 2627 });
    }

    private static async Task AssertGroupAndCompanyReadAsync(
        SqlServerRelationalApiFactory factory,
        Guid partyId)
    {
        using var groupClient = CreateGroupClient(factory, TestUsers.GroupAdmin);

        using var groupResponse = await groupClient.GetAsync("/api/v1/counterparties");
        Assert.Equal(HttpStatusCode.OK, groupResponse.StatusCode);
        using var groupJson = JsonDocument.Parse(
            await groupResponse.Content.ReadAsStringAsync());
        var groupParties = groupJson.RootElement.EnumerateArray().ToArray();
        Assert.Single(groupParties);
        Assert.Equal(partyId, groupParties[0].GetProperty("id").GetGuid());
        var groupAccounts = groupParties[0]
            .GetProperty("companyAccounts")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(2, groupAccounts.Length);

        using var companyClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        using var companyResponse = await companyClient.GetAsync("/api/v1/counterparties");
        Assert.Equal(HttpStatusCode.OK, companyResponse.StatusCode);
        using var companyJson = JsonDocument.Parse(
            await companyResponse.Content.ReadAsStringAsync());
        var companyParties = companyJson.RootElement.EnumerateArray().ToArray();
        Assert.Single(companyParties);
        var companyAccounts = companyParties[0]
            .GetProperty("companyAccounts")
            .EnumerateArray()
            .ToArray();
        Assert.Single(companyAccounts);
        Assert.Equal(
            TestCompanyIds.CompanyA,
            companyAccounts[0].GetProperty("companyId").GetGuid());

        using var conflictingFilter = await companyClient.GetAsync(
            $"/api/v1/counterparties?companyId={TestCompanyIds.CompanyB}");
        Assert.Equal(HttpStatusCode.OK, conflictingFilter.StatusCode);
        using var conflictingJson = JsonDocument.Parse(
            await conflictingFilter.Content.ReadAsStringAsync());
        Assert.Empty(conflictingJson.RootElement.EnumerateArray());
    }

    private static HttpClient CreateGroupClient(
        SqlServerRelationalApiFactory factory,
        string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", userId);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return client;
    }

    private static Task InsertPartyAsync(
        SqlConnection connection,
        Guid id,
        string code,
        string legalName)
    {
        return ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO [counterparty].[Parties]
                ([Id], [Code], [Kind], [LegalName], [TradeName], [IsActive], [Version],
                 [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@Id, @Code, N'LegalEntity', @LegalName, NULL, 1, 1,
                 SYSUTCDATETIME(), SYSUTCDATETIME());
            """,
            new SqlParameter("@Id", id),
            new SqlParameter("@Code", code),
            new SqlParameter("@LegalName", legalName));
    }

    private static Task InsertAccountAsync(
        SqlConnection connection,
        Guid id,
        Guid companyId,
        Guid partyId,
        int roles)
    {
        return ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO [counterparty].[CompanyPartyAccounts]
                ([Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode],
                 [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@Id, @CompanyId, @PartyId, @Roles, NULL,
                 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            """,
            new SqlParameter("@Id", id),
            new SqlParameter("@CompanyId", companyId),
            new SqlParameter("@PartyId", partyId),
            new SqlParameter("@Roles", roles));
    }

    private static async Task<T> ScalarAsync<T>(
        SqlConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync();
        return Assert.IsType<T>(value);
    }

    private static async Task ExecuteNonQueryAsync(
        SqlConnection connection,
        string commandText,
        params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
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
