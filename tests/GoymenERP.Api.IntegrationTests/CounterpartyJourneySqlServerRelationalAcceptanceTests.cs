using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyJourneySqlServerRelationalAcceptanceTests(ITestOutputHelper output)
{
    private const string Route = "/api/v1/counterparties";

    [SqlServerFact]
    public async Task CreateFindDetailAndLinkSecondCompany_PreserveIdentityVisibilityAndExactlyOnceEffects()
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
            var baseline = await ReadEffectsAsync(connection);

            using var companyA = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            using var companyB = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            var createCommand = new CreateCounterparty(
                PartyKind.LegalEntity, "Yolculuk Test Metal A.Ş.", "Yolculuk Metal",
                PartyRole.Customer | PartyRole.Supplier);
            using var createResponse = await companyA.PostAsJsonAsync(Route, createCommand);
            var created = await ReadJsonAsync(createResponse, HttpStatusCode.Created);
            var partyId = created.GetProperty("id").GetGuid();
            var code = Assert.IsType<string>(created.GetProperty("code").GetString());
            Assert.NotEqual(Guid.Empty, partyId);
            Assert.Matches("^CAR-[0-9]{6,}$", code);
            Assert.Equal("1", created.GetProperty("version").GetString());
            var accountA = AssertSingleAccount(created, TestCompanyIds.CompanyA, JsonValueKind.String);
            Assert.Equal((int)createCommand.Roles, accountA.GetProperty("roles").GetInt32());
            var afterCreate = baseline with
            {
                Parties = baseline.Parties + 1,
                Accounts = baseline.Accounts + 1,
                CreateAudits = baseline.CreateAudits + 1,
                CreateExecutions = baseline.CreateExecutions + 1,
            };
            Assert.Equal(afterCreate, await ReadEffectsAsync(connection));

            var listRoute = $"{Route}?q={Uri.EscapeDataString(code)}&includeCount=true";
            var detailRoute = $"{Route}/{partyId:D}";
            var candidateRoute = $"{Route}/company-account-link-candidates?q={Uri.EscapeDataString(code)}";

            // Read the server-assigned code back through the actual paged endpoint, not a test seed.
            var pageA = await GetJsonAsync(companyA, listRoute);
            var listItemA = AssertSingleListItem(pageA, partyId, code, TestCompanyIds.CompanyA);
            Assert.Equal(accountA.GetProperty("id").GetGuid(),
                listItemA.GetProperty("companyAccounts")[0].GetProperty("id").GetGuid());
            var detailA = await GetJsonAsync(companyA, detailRoute);
            AssertIdentity(detailA, partyId, code, JsonValueKind.String);
            Assert.Equal(accountA.GetProperty("id").GetGuid(),
                AssertSingleAccount(detailA, TestCompanyIds.CompanyA, JsonValueKind.String)
                    .GetProperty("id").GetGuid());

            // The same user switches company: ordinary target reads must not expose A's account.
            AssertEmptyPage(await GetJsonAsync(companyB, listRoute), hasCount: true);
            using var beforeLinkDetail = await companyB.GetAsync(detailRoute);
            var notFound = await ReadJsonAsync(beforeLinkDetail, HttpStatusCode.NotFound);
            Assert.Equal("not_found", notFound.GetProperty("code").GetString());
            var candidates = await GetJsonAsync(companyB, candidateRoute);
            var candidate = Assert.Single(candidates.GetProperty("items").EnumerateArray());
            AssertIdentity(candidate, partyId, code, versionKind: null);
            var sourceAccount = Assert.Single(candidate.GetProperty("visibleSourceAccounts").EnumerateArray());
            Assert.Equal(TestCompanyIds.CompanyA, sourceAccount.GetProperty("companyId").GetGuid());
            Assert.Equal(accountA.GetProperty("id").GetGuid(), sourceAccount.GetProperty("id").GetGuid());
            Assert.Equal(JsonValueKind.String, sourceAccount.GetProperty("version").ValueKind);
            Assert.Equal(afterCreate, await ReadEffectsAsync(connection));

            var linkRoute = $"{detailRoute}/company-account";
            var linkCommand = new CreateCompanyPartyAccount(PartyRole.Supplier | PartyRole.Service);
            var originalCorrelation = Assert.Single(companyB.DefaultRequestHeaders.GetValues("X-Correlation-Id"));
            using var linkResponse = await companyB.PostAsJsonAsync(linkRoute, linkCommand);
            var linked = await ReadJsonAsync(linkResponse, HttpStatusCode.Created);
            AssertIdentity(linked, partyId, code, JsonValueKind.String);
            var accountB = AssertSingleAccount(linked, TestCompanyIds.CompanyB, JsonValueKind.String);
            Assert.NotEqual(accountA.GetProperty("id").GetGuid(), accountB.GetProperty("id").GetGuid());
            Assert.Equal((int)linkCommand.Roles, accountB.GetProperty("roles").GetInt32());
            var afterLink = afterCreate with
            {
                Accounts = afterCreate.Accounts + 1,
                LinkAudits = afterCreate.LinkAudits + 1,
                LinkExecutions = afterCreate.LinkExecutions + 1,
            };
            Assert.Equal(afterLink, await ReadEffectsAsync(connection));

            var pageB = await GetJsonAsync(companyB, listRoute);
            AssertSingleListItem(pageB, partyId, code, TestCompanyIds.CompanyB);
            var detailB = await GetJsonAsync(companyB, detailRoute);
            AssertIdentity(detailB, partyId, code, JsonValueKind.String);
            Assert.Equal(accountB.GetProperty("id").GetGuid(),
                AssertSingleAccount(detailB, TestCompanyIds.CompanyB, JsonValueKind.String)
                    .GetProperty("id").GetGuid());
            AssertEmptyPage(await GetJsonAsync(companyB, candidateRoute), hasCount: false);
            AssertSingleListItem(await GetJsonAsync(companyA, listRoute), partyId, code, TestCompanyIds.CompanyA);
            var accountAAfterLink = AssertSingleAccount(await GetJsonAsync(companyA, detailRoute),
                TestCompanyIds.CompanyA, JsonValueKind.String);
            Assert.Equal(accountA.GetProperty("id").GetGuid(), accountAAfterLink.GetProperty("id").GetGuid());
            Assert.Equal(accountA.GetProperty("roles").GetInt32(), accountAAfterLink.GetProperty("roles").GetInt32());
            Assert.Equal(accountA.GetProperty("version").GetString(), accountAAfterLink.GetProperty("version").GetString());

            using var group = factory.CreateClient();
            group.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
            var groupDetail = await GetJsonAsync(group, detailRoute);
            AssertIdentity(groupDetail, partyId, code, JsonValueKind.String);
            var groupAccounts = groupDetail.GetProperty("companyAccounts").EnumerateArray().ToArray();
            Assert.Equal(2, groupAccounts.Length);
            Assert.Contains(groupAccounts, account => account.GetProperty("id").GetGuid() == accountA.GetProperty("id").GetGuid());
            Assert.Contains(groupAccounts, account => account.GetProperty("id").GetGuid() == accountB.GetProperty("id").GetGuid());
            var groupPage = await GetJsonAsync(group, listRoute);
            Assert.Equal(1, groupPage.GetProperty("totalCount").GetInt64());
            var groupItem = Assert.Single(groupPage.GetProperty("items").EnumerateArray());
            AssertIdentity(groupItem, partyId, code, JsonValueKind.Number);
            Assert.Equal(2, groupItem.GetProperty("companyAccounts").GetArrayLength());
            Assert.Equal(afterLink, await ReadEffectsAsync(connection));

            // A network retry keeps the original command key and payload, even after company switching.
            companyB.DefaultRequestHeaders.Remove("X-Correlation-Id");
            companyB.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
            using var replayResponse = await companyB.PostAsJsonAsync(linkRoute, linkCommand);
            var replay = await ReadJsonAsync(replayResponse, HttpStatusCode.Created);
            Assert.Equal(linked.GetRawText(), replay.GetRawText());
            Assert.Equal("true", Assert.Single(replayResponse.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(originalCorrelation,
                Assert.Single(replayResponse.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            Assert.Equal(afterLink, await ReadEffectsAsync(connection));
            output.WriteLine("SQL effect delta: Party=1, CompanyPartyAccount=2, create audit/execution=1/1, link audit/execution=1/1; reads and replay added no effects.");
        }
        finally
        {
            await factory.DisposeAsync();
            await DropOwnedDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private async Task<JsonElement> GetJsonAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        var result = await ReadJsonAsync(response, HttpStatusCode.OK);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return result;
    }

    private async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        // Only synthetic fixture responses are recorded; no authorization or connection headers.
        output.WriteLine($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery} -> {(int)response.StatusCode}\n{body}");
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("externalIntegrationCode", body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static void AssertIdentity(JsonElement item, Guid partyId, string code, JsonValueKind? versionKind)
    {
        Assert.Equal(partyId, item.GetProperty("id").GetGuid());
        Assert.Equal(code, item.GetProperty("code").GetString());
        if (versionKind.HasValue)
        {
            Assert.Equal(versionKind.Value, item.GetProperty("version").ValueKind);
        }
    }

    private static JsonElement AssertSingleAccount(JsonElement item, Guid companyId, JsonValueKind versionKind)
    {
        var account = Assert.Single(item.GetProperty("companyAccounts").EnumerateArray());
        Assert.Equal(companyId, account.GetProperty("companyId").GetGuid());
        Assert.True(account.GetProperty("isActive").GetBoolean());
        Assert.Equal(versionKind, account.GetProperty("version").ValueKind);
        return account;
    }

    private static JsonElement AssertSingleListItem(JsonElement page, Guid partyId, string code, Guid companyId)
    {
        Assert.Equal(1, page.GetProperty("totalCount").GetInt64());
        Assert.Equal("filtered-authorized-visible-parties", page.GetProperty("countScope").GetString());
        Assert.False(page.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        AssertIdentity(item, partyId, code, JsonValueKind.Number);
        AssertSingleAccount(item, companyId, JsonValueKind.Number);
        return item;
    }

    private static void AssertEmptyPage(JsonElement page, bool hasCount)
    {
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.False(page.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
        if (hasCount)
        {
            Assert.Equal(0, page.GetProperty("totalCount").GetInt64());
        }
    }

    private static async Task<Effects> ReadEffectsAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM [counterparty].[Parties]),
                (SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts]),
                (SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'counterparty.party.created.v1'),
                (SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'counterparty.company-account.created.v1'),
                (SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName] = N'counterparty.party.create.v1'),
                (SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName] = N'counterparty.company-account.create.v1');
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new Effects(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5));
    }

    private static async Task DropOwnedDatabaseAsync(string baseConnectionString, string databaseName)
    {
        // Never allow cleanup to target a supplied application database or arbitrary SQL identifier.
        Assert.Matches("^goymen_erp_ci_[0-9a-f]{32}$", databaseName);
        var builder = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" };
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

    private sealed record Effects(int Parties, int Accounts, int CreateAudits, int LinkAudits,
        int CreateExecutions, int LinkExecutions);
}
