using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.IntegrationTests.Fixtures;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class C01E2eGoldenFixtureSqlServerRelationalAcceptanceTests
{
    private static readonly Guid GdcCompanyId = TestCompanyIds.CompanyA;
    private static readonly Guid GspCompanyId = TestCompanyIds.CompanyB;

    [Fact]
    public void FixtureArtifact_IsTheExactCanonicalContract()
    {
        using var artifact = C01E2eGoldenFixture.Load();

        Assert.Equal(C01E2eGoldenFixture.ExpectedByteLength, artifact.Bytes.Length);
        Assert.Equal(C01E2eGoldenFixture.ExpectedSha256, artifact.Sha256);
        Assert.Equal(
            Enumerable.Range(1, 54).Select(sequence => $"CAR-{sequence:D6}"),
            artifact.Root.GetProperty("parties")
                .EnumerateArray()
                .Select(party => party.GetProperty("code").GetString()));
        Assert.Equal(
            53,
            artifact.Root.GetProperty("companyPartyAccounts")
                .EnumerateArray()
                .Count(account => account.GetProperty("companyId").GetGuid() == GdcCompanyId));
        Assert.Equal(
            2,
            artifact.Root.GetProperty("companyPartyAccounts")
                .EnumerateArray()
                .Count(account => account.GetProperty("companyId").GetGuid() == GspCompanyId));
    }

    [SqlServerFact]
    public async Task Fixture_MaterializesOnceAndProvesC01HttpSqlAndSequenceContract()
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
            var auditBefore = await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [audit].[AuditEvents];");
            var executionsBefore = await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [idempotency].[CommandExecutions];");

            using var artifact = C01E2eGoldenFixture.Load();
            await C01E2eGoldenFixture.ApplyAsync(connection, artifact);

            var reusableEvidence =
                await C01E2eGoldenFixtureDatabaseVerifier.VerifyAsync(connection);
            Assert.Equal(54, reusableEvidence.PartyCount);
            Assert.Equal(55, reusableEvidence.AccountCount);
            Assert.Equal(55, reusableEvidence.Sequence.CurrentValue);
            Assert.Null(reusableEvidence.Sequence.LastUsedValue);

            var secondApply = await Assert.ThrowsAsync<SqlException>(
                () => C01E2eGoldenFixture.ApplyAsync(connection, artifact));
            Assert.Equal(51021, secondApply.Number);
            Assert.Contains("fixture_already_applied", secondApply.Message, StringComparison.Ordinal);

            await AssertRelationalCardinalitiesAsync(connection);
            await AssertSpecialRowsAsync(connection);

            using var gdcClient = factory.CreateClientFor(TestUsers.GroupAdmin, GdcCompanyId);
            using var gspClient = factory.CreateClientFor(TestUsers.GroupAdmin, GspCompanyId);
            await AssertGdcGoldenQueriesAsync(gdcClient);
            await AssertGspGoldenQueriesAsync(gspClient);
            await AssertUnauthorizedCompanyIsFailClosedAsync(factory);

            Assert.Equal(
                auditBefore,
                await ScalarAsync<long>(connection, "SELECT COUNT_BIG(*) FROM [audit].[AuditEvents];"));
            Assert.Equal(
                executionsBefore,
                await ScalarAsync<long>(
                    connection,
                    "SELECT COUNT_BIG(*) FROM [idempotency].[CommandExecutions];"));

            Assert.Equal(55L, await ReadNextPartyCodeSequenceValueAsync(connection));
            var sequenceDriftApply = await Assert.ThrowsAsync<SqlException>(
                () => C01E2eGoldenFixture.ApplyAsync(connection, artifact));
            Assert.Equal(51022, sequenceDriftApply.Number);
            Assert.Contains("c01_fixture_conflict", sequenceDriftApply.Message, StringComparison.Ordinal);
            await ResetPartyCodeSequenceAsync(connection);

            var createdCode = await CreateNextPartyAsync(gdcClient);
            Assert.Equal("CAR-000055", createdCode);
            Assert.Equal(
                55L,
                await ScalarAsync<long>(
                    connection,
                    "SELECT COUNT_BIG(*) FROM [counterparty].[Parties];"));
            Assert.Equal(
                56L,
                await ScalarAsync<long>(
                    connection,
                    "SELECT COUNT_BIG(*) FROM [counterparty].[CompanyPartyAccounts];"));

            var conflictingApply = await Assert.ThrowsAsync<SqlException>(
                () => C01E2eGoldenFixture.ApplyAsync(connection, artifact));
            Assert.Equal(51022, conflictingApply.Number);
            Assert.Contains("c01_fixture_conflict", conflictingApply.Message, StringComparison.Ordinal);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task AssertRelationalCardinalitiesAsync(SqlConnection connection)
    {
        Assert.Equal(
            54L,
            await ScalarAsync<long>(connection, "SELECT COUNT_BIG(*) FROM [counterparty].[Parties];"));
        Assert.Equal(
            55L,
            await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [counterparty].[CompanyPartyAccounts];"));
        Assert.Equal(
            53L,
            await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId;",
                new SqlParameter("@CompanyId", GdcCompanyId)));
        Assert.Equal(
            2L,
            await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId;",
                new SqlParameter("@CompanyId", GspCompanyId)));
        Assert.Equal(
            52L,
            await ScalarAsync<long>(
                connection,
                """
                SELECT COUNT_BIG(*)
                FROM [counterparty].[Parties] AS [p]
                INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                    ON [a].[PartyId] = [p].[Id]
                WHERE [a].[CompanyId] = @CompanyId AND [p].[IsActive] = 1;
                """,
                new SqlParameter("@CompanyId", GdcCompanyId)));
    }

    private static async Task AssertSpecialRowsAsync(SqlConnection connection)
    {
        Assert.Equal(
            "Örnek Metal Sanayi ve Ticaret A.Ş.|Örnek Metal|3|1",
            await ScalarAsync<string>(
                connection,
                """
                SELECT CONCAT([p].[LegalName], N'|', [p].[TradeName], N'|', [a].[Roles], N'|', CONVERT(int, [a].[IsActive]))
                FROM [counterparty].[Parties] AS [p]
                INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                    ON [a].[PartyId] = [p].[Id]
                WHERE [p].[Code] = N'CAR-000001' AND [a].[CompanyId] = @CompanyId;
                """,
                new SqlParameter("@CompanyId", GdcCompanyId)));
        Assert.Equal(
            "Pasif Hesap Metal A.Ş.|1|0",
            await ScalarAsync<string>(
                connection,
                """
                SELECT CONCAT([p].[LegalName], N'|', [a].[Roles], N'|', CONVERT(int, [a].[IsActive]))
                FROM [counterparty].[Parties] AS [p]
                INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                    ON [a].[PartyId] = [p].[Id]
                WHERE [p].[Code] = N'CAR-000002' AND [a].[CompanyId] = @CompanyId;
                """,
                new SqlParameter("@CompanyId", GdcCompanyId)));
        Assert.Equal(
            "Ayşe Örnek|8|2",
            await ScalarAsync<string>(
                connection,
                """
                SELECT CONCAT(
                    [p].[LegalName], N'|',
                    MAX(CASE WHEN [a].[CompanyId] = @Gdc THEN [a].[Roles] END), N'|',
                    MAX(CASE WHEN [a].[CompanyId] = @Gsp THEN [a].[Roles] END))
                FROM [counterparty].[Parties] AS [p]
                INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                    ON [a].[PartyId] = [p].[Id]
                WHERE [p].[Code] = N'CAR-000003'
                GROUP BY [p].[LegalName];
                """,
                new SqlParameter("@Gdc", GdcCompanyId),
                new SqlParameter("@Gsp", GspCompanyId)));
        Assert.Equal(
            "Tarihsel Cari Sanayi A.Ş.|0|1|4",
            await ScalarAsync<string>(
                connection,
                """
                SELECT CONCAT([p].[LegalName], N'|', CONVERT(int, [p].[IsActive]), N'|', CONVERT(int, [a].[IsActive]), N'|', [a].[Roles])
                FROM [counterparty].[Parties] AS [p]
                INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                    ON [a].[PartyId] = [p].[Id]
                WHERE [p].[Code] = N'CAR-000053' AND [a].[CompanyId] = @CompanyId;
                """,
                new SqlParameter("@CompanyId", GdcCompanyId)));
        Assert.Equal(
            "Yalnız GSP Metal A.Ş.|2",
            await ScalarAsync<string>(
                connection,
                """
                SELECT CONCAT([p].[LegalName], N'|', [a].[Roles])
                FROM [counterparty].[Parties] AS [p]
                INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                    ON [a].[PartyId] = [p].[Id]
                WHERE [p].[Code] = N'CAR-000054' AND [a].[CompanyId] = @CompanyId;
                """,
                new SqlParameter("@CompanyId", GspCompanyId)));
    }

    private static async Task AssertGdcGoldenQueriesAsync(HttpClient client)
    {
        using var firstPage = await GetPageAsync(
            client,
            "/api/v1/counterparties?sort=code&direction=asc&pageSize=50");
        Assert.Equal(
            Enumerable.Range(1, 50).Select(sequence => $"CAR-{sequence:D6}"),
            ReadCodes(firstPage));
        Assert.True(firstPage.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, firstPage.RootElement.GetProperty("totalCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, firstPage.RootElement.GetProperty("countScope").ValueKind);
        var cursor = firstPage.RootElement.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));

        using var secondPage = await GetPageAsync(
            client,
            "/api/v1/counterparties?sort=code&direction=asc&pageSize=50&cursor="
            + Uri.EscapeDataString(cursor!));
        Assert.Equal(["CAR-000051", "CAR-000052"], ReadCodes(secondPage));
        Assert.False(secondPage.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, secondPage.RootElement.GetProperty("totalCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, secondPage.RootElement.GetProperty("countScope").ValueKind);

        using var counted = await GetPageAsync(
            client,
            "/api/v1/counterparties?includeCount=true&pageSize=1");
        Assert.Equal(52, counted.RootElement.GetProperty("totalCount").GetInt64());
        Assert.Equal(
            "filtered-authorized-visible-parties",
            counted.RootElement.GetProperty("countScope").GetString());

        await AssertCodesAsync(client, "?accountState=inactive", "CAR-000002");
        await AssertCodesAsync(client, "?partyState=inactive", "CAR-000053");
        await AssertCodesAsync(
            client,
            "?role=Customer&role=Supplier&roleMatch=all",
            "CAR-000001");
        await AssertCodesAsync(client, "?q=I%C5%9F%C4%B1k", "CAR-000004");
        await AssertCodesAsync(client, "?q=%C4%B0pek", "CAR-000005");

        using var all = await GetPageAsync(
            client,
            "/api/v1/counterparties?partyState=all&pageSize=100");
        var allItems = all.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.DoesNotContain("CAR-000054", ReadCodes(all));
        Assert.All(
            allItems
                .SelectMany(item => item.GetProperty("companyAccounts").EnumerateArray()),
            account => Assert.Equal(GdcCompanyId, account.GetProperty("companyId").GetGuid()));
        AssertItemProjection(allItems, "CAR-000001", true, GdcCompanyId, 3, true);
        AssertItemProjection(allItems, "CAR-000002", true, GdcCompanyId, 1, false);
        AssertItemProjection(allItems, "CAR-000003", true, GdcCompanyId, 8, true);
        AssertItemProjection(allItems, "CAR-000053", false, GdcCompanyId, 4, true);
    }

    private static async Task AssertGspGoldenQueriesAsync(HttpClient client)
    {
        using var page = await GetPageAsync(
            client,
            "/api/v1/counterparties?includeCount=true&sort=code&direction=asc");
        Assert.Equal(["CAR-000003", "CAR-000054"], ReadCodes(page));
        Assert.Equal(2, page.RootElement.GetProperty("totalCount").GetInt64());
        Assert.Equal(
            "filtered-authorized-visible-parties",
            page.RootElement.GetProperty("countScope").GetString());
        Assert.All(
            page.RootElement.GetProperty("items")
                .EnumerateArray()
                .SelectMany(item => item.GetProperty("companyAccounts").EnumerateArray()),
            account => Assert.Equal(GspCompanyId, account.GetProperty("companyId").GetGuid()));
        var items = page.RootElement.GetProperty("items").EnumerateArray().ToArray();
        AssertItemProjection(items, "CAR-000003", true, GspCompanyId, 2, true);
        AssertItemProjection(items, "CAR-000054", true, GspCompanyId, 2, true);
    }

    private static async Task AssertUnauthorizedCompanyIsFailClosedAsync(
        SqlServerRelationalApiFactory factory)
    {
        using var client = factory.CreateClientFor(TestUsers.CompanyA, GspCompanyId);
        using var response = await client.GetAsync("/api/v1/counterparties");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("CAR-000054", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Yalnız GSP", body, StringComparison.Ordinal);
    }

    private static async Task<string> CreateNextPartyAsync(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "c01-e2e-golden-next-party");
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "c01-e2e-golden-next-party-correlation");

        using var response = await client.PostAsJsonAsync(
            "/api/v1/counterparties",
            new CreateCounterparty(
                PartyKind.LegalEntity,
                "Golden Sonrası Cari A.Ş.",
                "Golden Sonrası",
                PartyRole.Customer));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Assert.IsType<string>(document.RootElement.GetProperty("code").GetString());
    }

    private static Task<long> ReadNextPartyCodeSequenceValueAsync(SqlConnection connection)
    {
        return ScalarAsync<long>(
            connection,
            "SELECT NEXT VALUE FOR [counterparty].[PartyCodeSequence];");
    }

    private static async Task ResetPartyCodeSequenceAsync(SqlConnection connection)
    {
        await using var reset = connection.CreateCommand();
        reset.CommandText = "ALTER SEQUENCE [counterparty].[PartyCodeSequence] RESTART WITH 55;";
        await reset.ExecuteNonQueryAsync();
    }

    private static async Task AssertCodesAsync(
        HttpClient client,
        string query,
        params string[] expected)
    {
        using var page = await GetPageAsync(client, "/api/v1/counterparties" + query);
        Assert.Equal(expected, ReadCodes(page));
    }

    private static void AssertItemProjection(
        IEnumerable<JsonElement> items,
        string code,
        bool partyIsActive,
        Guid companyId,
        int roles,
        bool accountIsActive)
    {
        var item = items.Single(candidate =>
            candidate.GetProperty("code").GetString() == code);
        Assert.Equal(partyIsActive, item.GetProperty("isActive").GetBoolean());
        var account = Assert.Single(item.GetProperty("companyAccounts").EnumerateArray());
        Assert.Equal(companyId, account.GetProperty("companyId").GetGuid());
        Assert.Equal(roles, account.GetProperty("roles").GetInt32());
        Assert.Equal(accountIsActive, account.GetProperty("isActive").GetBoolean());
    }

    private static async Task<JsonDocument> GetPageAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string[] ReadCodes(JsonDocument page)
    {
        return page.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("code").GetString()!)
            .ToArray();
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
}
