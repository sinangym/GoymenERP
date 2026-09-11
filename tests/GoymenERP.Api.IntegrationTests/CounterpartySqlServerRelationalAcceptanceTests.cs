using System.Data;
using System.Net;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartySqlServerRelationalAcceptanceTests
{
    private const long PartyVersion = 9_007_199_254_740_993;
    private const long CompanyAAccountVersion = 9_007_199_254_740_995;
    private const long CompanyBAccountVersion = 9_007_199_254_740_997;
    private const string CompanyAIntegrationSecret = "SQL-C03-SECRET-A";
    private const string CompanyBIntegrationSecret = "SQL-C03-SECRET-B";
    private static readonly Guid CompanyAAccountId = Guid.Parse(
        "62000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyBAccountId = Guid.Parse(
        "62000000-0000-0000-0000-000000000002");

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
                3,
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
            await InsertPartyAsync(
                connection,
                partyId,
                "CAR-000001",
                "SQL Ortak Cari A.Ş.",
                PartyVersion);

            await AssertUnknownCompanyRejectedAsync(connection, partyId);

            await InsertAccountAsync(
                connection,
                CompanyAAccountId,
                TestCompanyIds.CompanyA,
                partyId,
                roles: 1,
                externalIntegrationCode: CompanyAIntegrationSecret,
                version: CompanyAAccountVersion);
            await InsertAccountAsync(
                connection,
                CompanyBAccountId,
                TestCompanyIds.CompanyB,
                partyId,
                roles: 2,
                externalIntegrationCode: CompanyBIntegrationSecret,
                version: CompanyBAccountVersion);

            Assert.Equal(
                PartyVersion,
                await ScalarAsync<long>(
                    connection,
                    $"SELECT [Version] FROM [counterparty].[Parties] WHERE [Id] = '{partyId:D}';"));
            Assert.Equal(
                CompanyAIntegrationSecret,
                await ScalarAsync<string>(
                    connection,
                    $"SELECT [ExternalIntegrationCode] FROM [counterparty].[CompanyPartyAccounts] WHERE [Id] = '{CompanyAAccountId:D}';"));
            Assert.Equal(
                CompanyBAccountVersion,
                await ScalarAsync<long>(
                    connection,
                    $"SELECT [Version] FROM [counterparty].[CompanyPartyAccounts] WHERE [Id] = '{CompanyBAccountId:D}';"));

            await AssertDuplicateCompanyPartyRejectedAsync(connection, partyId);
            var auditBefore = await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [audit].[AuditEvents];");
            var commandExecutionsBefore = await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [idempotency].[CommandExecutions];");
            await AssertGroupAndCompanyReadAsync(factory, partyId);
            Assert.Equal(
                auditBefore,
                await ScalarAsync<long>(
                    connection,
                    "SELECT COUNT_BIG(*) FROM [audit].[AuditEvents];"));
            Assert.Equal(
                commandExecutionsBefore,
                await ScalarAsync<long>(
                    connection,
                    "SELECT COUNT_BIG(*) FROM [idempotency].[CommandExecutions];"));
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
        var groupParties = groupJson.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToArray();
        Assert.Single(groupParties);
        Assert.Equal(partyId, groupParties[0].GetProperty("id").GetGuid());
        var groupAccounts = groupParties[0]
            .GetProperty("companyAccounts")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(2, groupAccounts.Length);

        using var groupDetailResponse = await groupClient.GetAsync(
            $"/api/v1/counterparties/{partyId}");
        Assert.Equal(HttpStatusCode.OK, groupDetailResponse.StatusCode);
        AssertPrivateNoStore(groupDetailResponse);
        var groupDetailBody = await groupDetailResponse.Content.ReadAsStringAsync();
        AssertRedacted(groupDetailBody);
        using var groupDetail = JsonDocument.Parse(groupDetailBody);
        Assert.Equal(JsonValueKind.String, groupDetail.RootElement.GetProperty("version").ValueKind);
        Assert.Equal(PartyVersion.ToString(), groupDetail.RootElement.GetProperty("version").GetString());
        var groupDetailAccounts = groupDetail.RootElement
            .GetProperty("companyAccounts")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(2, groupDetailAccounts.Length);
        Assert.Equal(
            [TestCompanyIds.CompanyA, TestCompanyIds.CompanyB],
            groupDetailAccounts.Select(account => account.GetProperty("companyId").GetGuid()));
        Assert.Equal(
            [CompanyAAccountVersion.ToString(), CompanyBAccountVersion.ToString()],
            groupDetailAccounts.Select(account =>
                account.GetProperty("version").GetString()));

        using var companyClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        using var companyResponse = await companyClient.GetAsync("/api/v1/counterparties");
        Assert.Equal(HttpStatusCode.OK, companyResponse.StatusCode);
        using var companyJson = JsonDocument.Parse(
            await companyResponse.Content.ReadAsStringAsync());
        var companyParties = companyJson.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToArray();
        Assert.Single(companyParties);
        var companyAccounts = companyParties[0]
            .GetProperty("companyAccounts")
            .EnumerateArray()
            .ToArray();
        Assert.Single(companyAccounts);
        Assert.Equal(
            TestCompanyIds.CompanyA,
            companyAccounts[0].GetProperty("companyId").GetGuid());

        using var companyDetailResponse = await companyClient.GetAsync(
            $"/api/v1/counterparties/{partyId}");
        Assert.Equal(HttpStatusCode.OK, companyDetailResponse.StatusCode);
        AssertPrivateNoStore(companyDetailResponse);
        var companyDetailBody = await companyDetailResponse.Content.ReadAsStringAsync();
        AssertRedacted(companyDetailBody);
        using var companyDetail = JsonDocument.Parse(companyDetailBody);
        var companyDetailAccount = Assert.Single(
            companyDetail.RootElement.GetProperty("companyAccounts").EnumerateArray());
        Assert.Equal(
            TestCompanyIds.CompanyA,
            companyDetailAccount.GetProperty("companyId").GetGuid());
        Assert.Equal(
            CompanyAAccountVersion.ToString(),
            companyDetailAccount.GetProperty("version").GetString());
        Assert.DoesNotContain(
            TestCompanyIds.CompanyB.ToString("D"),
            companyDetailBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            CompanyBAccountId.ToString("D"),
            companyDetailBody,
            StringComparison.OrdinalIgnoreCase);

        using var companyBClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyB);
        using var companyBDetailResponse = await companyBClient.GetAsync(
            $"/api/v1/counterparties/{partyId}");
        Assert.Equal(HttpStatusCode.OK, companyBDetailResponse.StatusCode);
        using var companyBDetail = JsonDocument.Parse(
            await companyBDetailResponse.Content.ReadAsStringAsync());
        var companyBDetailAccount = Assert.Single(
            companyBDetail.RootElement.GetProperty("companyAccounts").EnumerateArray());
        Assert.Equal(
            TestCompanyIds.CompanyB,
            companyBDetailAccount.GetProperty("companyId").GetGuid());
        Assert.Equal(
            CompanyBAccountVersion.ToString(),
            companyBDetailAccount.GetProperty("version").GetString());

        using var explicitCompanyDetailResponse = await groupClient.GetAsync(
            $"/api/v1/counterparties/{partyId}?companyId={TestCompanyIds.CompanyA:D}");
        await AssertValidationFailedAsync(explicitCompanyDetailResponse, partyId);

        using var conflictingDetailResponse = await companyClient.GetAsync(
            $"/api/v1/counterparties/{partyId}?companyId={TestCompanyIds.CompanyB:D}");
        await AssertValidationFailedAsync(conflictingDetailResponse, partyId);

        using var narrowClient = factory.CreateClientFor(
            TestUsers.CompanyAViewer,
            TestCompanyIds.CompanyA);
        using var narrowDetailResponse = await narrowClient.GetAsync(
            $"/api/v1/counterparties/{partyId}");
        await AssertNotFoundAsync(narrowDetailResponse, partyId);

        var missingPartyId = Guid.Parse("61000000-0000-0000-0000-000000000099");
        using var missingDetailResponse = await companyClient.GetAsync(
            $"/api/v1/counterparties/{missingPartyId:D}");
        await AssertNotFoundAsync(missingDetailResponse, missingPartyId);

        using var conflictingFilter = await companyClient.GetAsync(
            $"/api/v1/counterparties?companyId={TestCompanyIds.CompanyB}");
        Assert.Equal(HttpStatusCode.BadRequest, conflictingFilter.StatusCode);
        using var conflictingJson = JsonDocument.Parse(
            await conflictingFilter.Content.ReadAsStringAsync());
        Assert.Equal(
            "validation_failed",
            conflictingJson.RootElement.GetProperty("code").GetString());
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

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private static void AssertRedacted(string body)
    {
        Assert.DoesNotContain(
            "externalIntegrationCode",
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CompanyAIntegrationSecret, body, StringComparison.Ordinal);
        Assert.DoesNotContain(CompanyBIntegrationSecret, body, StringComparison.Ordinal);
    }

    private static async Task AssertNotFoundAsync(
        HttpResponseMessage response,
        Guid hiddenPartyId)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("not_found", problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(
            hiddenPartyId.ToString("D"),
            body,
            StringComparison.OrdinalIgnoreCase);
        AssertRedacted(body);
    }

    private static async Task AssertValidationFailedAsync(
        HttpResponseMessage response,
        Guid hiddenPartyId)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(
            CounterpartyProblemCodesV1.ValidationFailed,
            problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(
            hiddenPartyId.ToString("D"),
            body,
            StringComparison.OrdinalIgnoreCase);
        AssertRedacted(body);
    }

    private static Task InsertPartyAsync(
        SqlConnection connection,
        Guid id,
        string code,
        string legalName,
        long version = 1)
    {
        return ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO [counterparty].[Parties]
                ([Id], [Code], [Kind], [LegalName], [TradeName], [IsActive], [Version],
                 [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@Id, @Code, N'LegalEntity', @LegalName, NULL, 1, @Version,
                  SYSUTCDATETIME(), SYSUTCDATETIME());
            """,
            new SqlParameter("@Id", id),
            new SqlParameter("@Code", code),
            new SqlParameter("@LegalName", legalName),
            new SqlParameter("@Version", SqlDbType.BigInt) { Value = version });
    }

    private static Task InsertAccountAsync(
        SqlConnection connection,
        Guid id,
        Guid companyId,
        Guid partyId,
        int roles,
        string? externalIntegrationCode = null,
        long version = 1)
    {
        return ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO [counterparty].[CompanyPartyAccounts]
                ([Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode],
                 [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@Id, @CompanyId, @PartyId, @Roles, @ExternalIntegrationCode,
                 1, @Version, SYSUTCDATETIME(), SYSUTCDATETIME());
            """,
            new SqlParameter("@Id", id),
            new SqlParameter("@CompanyId", companyId),
            new SqlParameter("@PartyId", partyId),
            new SqlParameter("@Roles", roles),
            new SqlParameter("@ExternalIntegrationCode", SqlDbType.NVarChar, 128)
            {
                Value = (object?)externalIntegrationCode ?? DBNull.Value,
            },
            new SqlParameter("@Version", SqlDbType.BigInt) { Value = version });
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
