using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyReadPageSqlServerRelationalAcceptanceTests
{
    private const int LargeFixtureSize = 240;
    private static readonly Guid SharedPartyId = Guid.Parse(
        "61000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyBOnlyPartyId = Guid.Parse(
        "61000000-0000-0000-0000-000000000003");
    private static readonly Guid CompanyAOnlyPartyId = Guid.Parse(
        "61000000-0000-0000-0000-000000000002");
    private static readonly Guid NaturalPersonPartyId = Guid.Parse(
        "61000000-0000-0000-0000-000000000006");

    [SqlServerFact]
    public async Task C01ReadPage_EnforcesAuthorizationFiltersTurkishSearchKeysetAndRedaction()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var clock = new MutableTimeProvider(new DateTimeOffset(
            2026,
            8,
            31,
            12,
            0,
            0,
            TimeSpan.Zero));
        var sqlObserver = new CapturingCounterpartyReadSqlObserver();
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.RemoveAll<ICounterpartyReadSqlObserver>();
                services.AddSingleton<ICounterpartyReadSqlObserver>(sqlObserver);
                services.RemoveAll<IAuthorizationSnapshotReadObserver>();
                services.AddSingleton<IAuthorizationSnapshotReadObserver>(sqlObserver);
            });

        try
        {
            using var healthClient = factory.CreateClient();
            using var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await SeedGoldenDataAsync(connection);
            await GrantCompanyOperatorReadPermissionAsync(connection);
            var auditBefore = await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [audit].[AuditEvents];");
            var executionsBefore = await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [idempotency].[CommandExecutions];");

            using var groupClient = CreateHeaderlessClient(factory, TestUsers.GroupAdmin);
            using var groupResponse = await groupClient.GetAsync(
                "/api/v1/counterparties?partyState=all&includeCount=true&pageSize=50");
            var groupPage = await ReadPageAsync(groupResponse);
            var expectedTotal = 8 + LargeFixtureSize;
            Assert.Equal(expectedTotal, groupPage.RootElement.GetProperty("totalCount").GetInt64());
            var groupItems = groupPage.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(50, groupItems.Length);
            Assert.Equal(
                1,
                groupItems.Count(item => item.GetProperty("id").GetGuid() == SharedPartyId));
            var shared = groupItems.Single(
                item => item.GetProperty("id").GetGuid() == SharedPartyId);
            Assert.Equal(2, shared.GetProperty("companyAccounts").GetArrayLength());
            Assert.DoesNotContain(
                "ERP-SECRET",
                groupPage.RootElement.GetRawText(),
                StringComparison.Ordinal);
            Assert.False(
                shared.GetProperty("companyAccounts")[0]
                    .TryGetProperty("externalIntegrationCode", out _));

            using var companyAClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            using var companyAResponse = await companyAClient.GetAsync(
                "/api/v1/counterparties?partyState=all&includeCount=true&pageSize=50");
            var companyAPage = await ReadPageAsync(companyAResponse);
            var companyAItems = companyAPage.RootElement
                .GetProperty("items")
                .EnumerateArray()
                .ToArray();
            Assert.Equal(
                7 + LargeFixtureSize,
                companyAPage.RootElement.GetProperty("totalCount").GetInt64());
            Assert.DoesNotContain(
                companyAItems,
                item => item.GetProperty("id").GetGuid() == CompanyBOnlyPartyId);
            Assert.All(
                companyAItems.SelectMany(item =>
                    item.GetProperty("companyAccounts").EnumerateArray()),
                account => Assert.Equal(
                    TestCompanyIds.CompanyA,
                    account.GetProperty("companyId").GetGuid()));

            using var companyAOnlyClient = CreateHeaderlessClient(factory, TestUsers.CompanyA);
            using var companyAOnlyResponse = await companyAOnlyClient.GetAsync(
                "/api/v1/counterparties?partyState=all&includeCount=true&pageSize=50");
            using var companyAOnlyPage = await ReadPageAsync(companyAOnlyResponse);
            Assert.Equal(
                7 + LargeFixtureSize,
                companyAOnlyPage.RootElement.GetProperty("totalCount").GetInt64());
            Assert.DoesNotContain(CompanyBOnlyPartyId, ReadIds(companyAOnlyPage));
            Assert.All(
                companyAOnlyPage.RootElement.GetProperty("items")
                    .EnumerateArray()
                    .SelectMany(item => item.GetProperty("companyAccounts").EnumerateArray()),
                account => Assert.Equal(
                    TestCompanyIds.CompanyA,
                    account.GetProperty("companyId").GetGuid()));

            using var companyBOnlyClient = CreateHeaderlessClient(factory, TestUsers.CompanyB);
            using var companyBOnlyResponse = await companyBOnlyClient.GetAsync(
                "/api/v1/counterparties?partyState=all&includeCount=true&pageSize=50");
            using var companyBOnlyPage = await ReadPageAsync(companyBOnlyResponse);
            Assert.Equal(2, companyBOnlyPage.RootElement.GetProperty("totalCount").GetInt64());
            Assert.DoesNotContain(CompanyAOnlyPartyId, ReadIds(companyBOnlyPage));
            Assert.All(
                companyBOnlyPage.RootElement.GetProperty("items")
                    .EnumerateArray()
                    .SelectMany(item => item.GetProperty("companyAccounts").EnumerateArray()),
                account => Assert.Equal(
                    TestCompanyIds.CompanyB,
                    account.GetProperty("companyId").GetGuid()));

            using var groupSelectedBClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            using var groupSelectedBResponse = await groupSelectedBClient.GetAsync(
                "/api/v1/counterparties?partyState=all&includeCount=true&pageSize=50");
            using var groupSelectedBPage = await ReadPageAsync(groupSelectedBResponse);
            Assert.Equal(2, groupSelectedBPage.RootElement.GetProperty("totalCount").GetInt64());
            Assert.DoesNotContain(CompanyAOnlyPartyId, ReadIds(groupSelectedBPage));
            Assert.All(
                groupSelectedBPage.RootElement.GetProperty("items")
                    .EnumerateArray()
                    .SelectMany(item => item.GetProperty("companyAccounts").EnumerateArray()),
                account => Assert.Equal(
                    TestCompanyIds.CompanyB,
                    account.GetProperty("companyId").GetGuid()));

            sqlObserver.Clear();
            using var boundedResponse = await groupClient.GetAsync(
                "/api/v1/counterparties?partyState=all&pageSize=17");
            using var boundedPage = await ReadPageAsync(boundedResponse);
            Assert.Equal(17, boundedPage.RootElement.GetProperty("items").GetArrayLength());
            Assert.True(boundedPage.RootElement.GetProperty("hasMore").GetBoolean());
            Assert.Equal(JsonValueKind.Null, boundedPage.RootElement.GetProperty("totalCount").ValueKind);
            var boundedCommands = sqlObserver.Snapshot();
            Assert.Equal(
                [CounterpartyReadSqlOperation.Page, CounterpartyReadSqlOperation.Accounts],
                boundedCommands.Select(command => command.Operation));
            Assert.Contains(
                "SELECT TOP (@Take)",
                boundedCommands.Single(command =>
                    command.Operation == CounterpartyReadSqlOperation.Page).CommandText,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                boundedCommands,
                command => command.Operation == CounterpartyReadSqlOperation.Count
                    || command.CommandText.Contains("COUNT_BIG", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, sqlObserver.AuthorizationDatabaseReads);

            await AssertSearchIdsAsync(
                groupClient,
                "izmir",
                Guid.Parse("61000000-0000-0000-0000-000000000004"));
            await AssertSearchIdsAsync(
                groupClient,
                "ısparta",
                Guid.Parse("61000000-0000-0000-0000-000000000005"));
            await AssertSearchIdsAsync(groupClient, "isparta");
            await AssertSearchIdsAsync(groupClient, "seker");
            await AssertSearchIdsAsync(
                groupClient,
                "şeker",
                Guid.Parse("61000000-0000-0000-0000-000000000006"));
            await AssertSearchIdsAsync(
                groupClient,
                "I\u0307ZMİR",
                Guid.Parse("61000000-0000-0000-0000-000000000004"));

            using var naturalPersonResponse = await groupClient.GetAsync(
                "/api/v1/counterparties?kind=NaturalPerson&includeCount=true");
            using var naturalPersonPage = await ReadPageAsync(naturalPersonResponse);
            Assert.Equal(1, naturalPersonPage.RootElement.GetProperty("totalCount").GetInt64());
            Assert.Equal([NaturalPersonPartyId], ReadIds(naturalPersonPage));

            using var customerRoleResponse = await groupClient.GetAsync(
                "/api/v1/counterparties?role=Customer");
            using var customerRolePage = await ReadPageAsync(customerRoleResponse);
            var customerShared = customerRolePage.RootElement.GetProperty("items")
                .EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == SharedPartyId);
            var customerSharedAccount = Assert.Single(
                customerShared.GetProperty("companyAccounts").EnumerateArray());
            Assert.Equal(
                TestCompanyIds.CompanyA,
                customerSharedAccount.GetProperty("companyId").GetGuid());

            using var supplierRoleResponse = await groupClient.GetAsync(
                "/api/v1/counterparties?role=Supplier&includeCount=true");
            using var supplierRolePage = await ReadPageAsync(supplierRoleResponse);
            Assert.Equal(3, supplierRolePage.RootElement.GetProperty("totalCount").GetInt64());
            var supplierShared = supplierRolePage.RootElement.GetProperty("items")
                .EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == SharedPartyId);
            var supplierSharedAccount = Assert.Single(
                supplierShared.GetProperty("companyAccounts").EnumerateArray());
            Assert.Equal(
                TestCompanyIds.CompanyB,
                supplierSharedAccount.GetProperty("companyId").GetGuid());

            using var allRoleResponse = await groupClient.GetAsync(
                "/api/v1/counterparties?role=Customer&role=Supplier&roleMatch=all");
            using var allRolePage = await ReadPageAsync(allRoleResponse);
            Assert.Equal(
                [Guid.Parse("61000000-0000-0000-0000-000000000004")],
                ReadIds(allRolePage));
            Assert.Single(
                allRolePage.RootElement.GetProperty("items")[0]
                    .GetProperty("companyAccounts")
                    .EnumerateArray());

            using var inactiveAccountResponse = await groupClient.GetAsync(
                "/api/v1/counterparties?accountState=inactive");
            using var inactiveAccountPage = await ReadPageAsync(inactiveAccountResponse);
            Assert.Equal(
                [Guid.Parse("61000000-0000-0000-0000-000000000008")],
                ReadIds(inactiveAccountPage));
            Assert.All(
                inactiveAccountPage.RootElement.GetProperty("items")[0]
                    .GetProperty("companyAccounts")
                    .EnumerateArray(),
                account => Assert.False(account.GetProperty("isActive").GetBoolean()));

            using var inactivePartyResponse = await groupClient.GetAsync(
                "/api/v1/counterparties?partyState=inactive");
            using var inactivePartyPage = await ReadPageAsync(inactivePartyResponse);
            Assert.Equal(
                [Guid.Parse("61000000-0000-0000-0000-000000000007")],
                ReadIds(inactivePartyPage));

            foreach (var sort in new[] { "code", "legalName", "updatedAt" })
            {
                foreach (var direction in new[] { "asc", "desc" })
                {
                    var traversed = await TraverseAsync(
                        groupClient,
                        sort,
                        direction,
                        pageSize: 200);
                    Assert.Equal(expectedTotal, traversed.Count);
                    Assert.Equal(expectedTotal, traversed.Distinct().Count());
                }
            }

            using var mutationStartResponse = await groupClient.GetAsync(
                "/api/v1/counterparties?partyState=all&sort=code&direction=asc&pageSize=10");
            using var mutationStartPage = await ReadPageAsync(mutationStartResponse);
            var mutationCursor = mutationStartPage.RootElement.GetProperty("nextCursor").GetString();
            Assert.False(string.IsNullOrWhiteSpace(mutationCursor));
            var mutationTraversal = ReadIds(mutationStartPage).ToList();
            var insertedDuringTraversal = Guid.Parse(
                "65000000-0000-0000-0000-000000000001");
            await InsertConcurrentTraversalPartyAsync(connection, insertedDuringTraversal);
            mutationTraversal.AddRange(await TraverseAsync(
                groupClient,
                "code",
                "asc",
                pageSize: 10,
                mutationCursor));

            Assert.Equal(expectedTotal + 1, mutationTraversal.Count);
            Assert.Equal(expectedTotal + 1, mutationTraversal.Distinct().Count());
            Assert.Contains(insertedDuringTraversal, mutationTraversal);

            using var forbiddenClient = factory.CreateClientFor(
                TestUsers.CompanyAViewer,
                TestCompanyIds.CompanyA);
            using var narrowScopeForbidden = await forbiddenClient.GetAsync(
                "/api/v1/counterparties");
            await AssertProblemCodeAsync(
                narrowScopeForbidden,
                HttpStatusCode.Forbidden,
                "forbidden");

            using var noPermissionClient = factory.CreateClientFor(
                TestUsers.NoAssignment,
                TestCompanyIds.CompanyA);
            using var noPermissionForbidden = await noPermissionClient.GetAsync(
                "/api/v1/counterparties");
            await AssertProblemCodeAsync(
                noPermissionForbidden,
                HttpStatusCode.Forbidden,
                "forbidden");

            Assert.Equal(
                auditBefore,
                await ScalarAsync<long>(connection, "SELECT COUNT_BIG(*) FROM [audit].[AuditEvents];"));
            Assert.Equal(
                executionsBefore,
                await ScalarAsync<long>(
                    connection,
                    "SELECT COUNT_BIG(*) FROM [idempotency].[CommandExecutions];"));

            using var cursorStart = await groupClient.GetAsync(
                "/api/v1/counterparties?partyState=all&pageSize=1");
            using var cursorStartPage = await ReadPageAsync(cursorStart);
            var boundCursor = cursorStartPage.RootElement.GetProperty("nextCursor").GetString();
            Assert.False(string.IsNullOrWhiteSpace(boundCursor));

            using var changedContext = await companyAClient.GetAsync(
                $"/api/v1/counterparties?partyState=all&pageSize=1&cursor={Uri.EscapeDataString(boundCursor!)}");
            await AssertProblemCodeAsync(
                changedContext,
                HttpStatusCode.BadRequest,
                CounterpartyProblemCodesV1.InvalidCursor);

            await using (var revisionCommand = connection.CreateCommand())
            {
                revisionCommand.CommandText =
                    "UPDATE [identity].[Roles] SET [Version] = [Version] + 1 "
                    + "WHERE [Code] = N'SYSTEM_ADMINISTRATOR';";
                Assert.Equal(1, await revisionCommand.ExecuteNonQueryAsync());
            }

            using var changedPolicy = await groupClient.GetAsync(
                $"/api/v1/counterparties?partyState=all&pageSize=1&cursor={Uri.EscapeDataString(boundCursor)}");
            await AssertProblemCodeAsync(
                changedPolicy,
                HttpStatusCode.BadRequest,
                CounterpartyProblemCodesV1.InvalidCursor);

            using var refreshedCursorResponse = await groupClient.GetAsync(
                "/api/v1/counterparties?partyState=all&pageSize=1");
            using var refreshedCursorPage = await ReadPageAsync(refreshedCursorResponse);
            var refreshedCursor = refreshedCursorPage.RootElement
                .GetProperty("nextCursor")
                .GetString();
            Assert.False(string.IsNullOrWhiteSpace(refreshedCursor));

            clock.UtcNow = clock.UtcNow.AddMinutes(31);
            using var expired = await groupClient.GetAsync(
                $"/api/v1/counterparties?partyState=all&pageSize=1&cursor={Uri.EscapeDataString(refreshedCursor!)}");
            await AssertProblemCodeAsync(
                expired,
                HttpStatusCode.BadRequest,
                CounterpartyProblemCodesV1.InvalidCursor);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static HttpClient CreateHeaderlessClient(
        SqlServerRelationalApiFactory factory,
        string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", userId);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return client;
    }

    private static async Task AssertSearchIdsAsync(
        HttpClient client,
        string query,
        params Guid[] expected)
    {
        using var response = await client.GetAsync(
            $"/api/v1/counterparties?q={Uri.EscapeDataString(query)}");
        using var page = await ReadPageAsync(response);
        Assert.Equal(expected.Order(), ReadIds(page).Order());
    }

    private static async Task<List<Guid>> TraverseAsync(
        HttpClient client,
        string sort,
        string direction,
        int pageSize,
        string? cursor = null)
    {
        var result = new List<Guid>();
        var pageCount = 0;
        do
        {
            var url = $"/api/v1/counterparties?partyState=all&sort={sort}"
                + $"&direction={direction}&pageSize={pageSize}";
            if (cursor is not null)
            {
                url += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            using var response = await client.GetAsync(url);
            using var page = await ReadPageAsync(response);
            result.AddRange(ReadIds(page));
            cursor = page.RootElement.GetProperty("nextCursor").ValueKind == JsonValueKind.Null
                ? null
                : page.RootElement.GetProperty("nextCursor").GetString();
            pageCount++;
            Assert.True(pageCount <= 1000, "Cursor traversal failed to terminate.");
        }
        while (cursor is not null);

        return result;
    }

    private static async Task InsertConcurrentTraversalPartyAsync(
        SqlConnection connection,
        Guid partyId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO [counterparty].[Parties]
                ([Id], [Code], [Kind], [LegalName], [TradeName], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@PartyId, N'CAR-999999', N'LegalEntity', N'Eşzamanlı Yeni Cari', NULL, 1, 1, @Now, @Now);

            INSERT INTO [counterparty].[CompanyPartyAccounts]
                ([Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                ('65000000-0000-0000-0000-000000000002', @CompanyId, @PartyId, 1, NULL, 1, 1, @Now, @Now);
            """;
        command.Parameters.AddWithValue("@PartyId", partyId);
        command.Parameters.AddWithValue("@CompanyId", TestCompanyIds.CompanyA);
        command.Parameters.AddWithValue("@Now", new DateTimeOffset(
            2026,
            8,
            31,
            11,
            0,
            0,
            TimeSpan.Zero));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task<JsonDocument> ReadPageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("items").ValueKind);
        return document;
    }

    private static Guid[] ReadIds(JsonDocument page)
    {
        return page.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToArray();
    }

    private static async Task AssertProblemCodeAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }

    private static async Task SeedGoldenDataAsync(SqlConnection connection)
    {
        var parties = new List<PartySeed>
        {
            new PartySeed(SharedPartyId, "CAR-900001", "LegalEntity", "Ortak Çelik", null, true),
            new PartySeed(Guid.Parse("61000000-0000-0000-0000-000000000002"), "CAR-900002", "LegalEntity", "Eşit Unvan", null, true),
            new PartySeed(CompanyBOnlyPartyId, "CAR-900003", "LegalEntity", "Eşit Unvan", null, true),
            new PartySeed(Guid.Parse("61000000-0000-0000-0000-000000000004"), "CAR-900004", "LegalEntity", "İZMİR Çelik", null, true),
            new PartySeed(Guid.Parse("61000000-0000-0000-0000-000000000005"), "CAR-900005", "LegalEntity", "ISPARTA Metal", null, true),
            new PartySeed(Guid.Parse("61000000-0000-0000-0000-000000000006"), "CAR-900006", "NaturalPerson", "Şeker Sanayi", null, true),
            new PartySeed(Guid.Parse("61000000-0000-0000-0000-000000000007"), "CAR-900007", "LegalEntity", "Pasif Taraf", null, false),
            new PartySeed(Guid.Parse("61000000-0000-0000-0000-000000000008"), "CAR-900008", "LegalEntity", "Pasif Hesap", null, true),
        };
        var accounts = new List<AccountSeed>
        {
            new AccountSeed(Guid.Parse("62000000-0000-0000-0000-000000000001"), TestCompanyIds.CompanyA, SharedPartyId, 1, true),
            new AccountSeed(Guid.Parse("62000000-0000-0000-0000-000000000002"), TestCompanyIds.CompanyB, SharedPartyId, 2, true),
            new AccountSeed(Guid.Parse("62000000-0000-0000-0000-000000000003"), TestCompanyIds.CompanyA, parties[1].Id, 1, true),
            new AccountSeed(Guid.Parse("62000000-0000-0000-0000-000000000004"), TestCompanyIds.CompanyB, parties[2].Id, 2, true),
            new AccountSeed(Guid.Parse("62000000-0000-0000-0000-000000000005"), TestCompanyIds.CompanyA, parties[3].Id, 3, true),
            new AccountSeed(Guid.Parse("62000000-0000-0000-0000-000000000006"), TestCompanyIds.CompanyA, parties[4].Id, 1, true),
            new AccountSeed(Guid.Parse("62000000-0000-0000-0000-000000000007"), TestCompanyIds.CompanyA, parties[5].Id, 1, true),
            new AccountSeed(Guid.Parse("62000000-0000-0000-0000-000000000008"), TestCompanyIds.CompanyA, parties[6].Id, 1, true),
            new AccountSeed(Guid.Parse("62000000-0000-0000-0000-000000000009"), TestCompanyIds.CompanyA, parties[7].Id, 1, false),
        };

        for (var index = 1; index <= LargeFixtureSize; index++)
        {
            var partyId = Guid.Parse($"63000000-0000-0000-0000-{index:D12}");
            parties.Add(new PartySeed(
                partyId,
                $"CAR-{910000 + index:D6}",
                "LegalEntity",
                $"Yük Testi {index:D3}",
                null,
                true));
            accounts.Add(new AccountSeed(
                Guid.Parse($"64000000-0000-0000-0000-{index:D12}"),
                TestCompanyIds.CompanyA,
                partyId,
                1,
                true));
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO [counterparty].[Parties]
                ([Id], [Code], [Kind], [LegalName], [TradeName], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            SELECT [Id], [Code], [Kind], [LegalName], [TradeName], [IsActive], 1, @Now, @Now
            FROM OPENJSON(@Parties)
            WITH
            (
                [Id] uniqueidentifier '$.Id',
                [Code] nvarchar(32) '$.Code',
                [Kind] nvarchar(32) '$.Kind',
                [LegalName] nvarchar(250) '$.LegalName',
                [TradeName] nvarchar(200) '$.TradeName',
                [IsActive] bit '$.IsActive'
            );

            INSERT INTO [counterparty].[CompanyPartyAccounts]
                ([Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            SELECT [Id], [CompanyId], [PartyId], [Roles], N'ERP-SECRET', [IsActive], 1, @Now, @Now
            FROM OPENJSON(@Accounts)
            WITH
            (
                [Id] uniqueidentifier '$.Id',
                [CompanyId] uniqueidentifier '$.CompanyId',
                [PartyId] uniqueidentifier '$.PartyId',
                [Roles] int '$.Roles',
                [IsActive] bit '$.IsActive'
            );
            """;
        command.Parameters.AddWithValue("@Parties", JsonSerializer.Serialize(parties));
        command.Parameters.AddWithValue("@Accounts", JsonSerializer.Serialize(accounts));
        command.Parameters.AddWithValue("@Now", new DateTimeOffset(
            2026,
            8,
            31,
            10,
            0,
            0,
            TimeSpan.Zero));
        Assert.Equal(parties.Count + accounts.Count, await command.ExecuteNonQueryAsync());
    }

    private static async Task GrantCompanyOperatorReadPermissionAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            IF NOT EXISTS
            (
                SELECT 1
                FROM [identity].[RolePermissions]
                WHERE [RoleId] IN
                    (
                        '30000000-0000-0000-0000-000000000001',
                        '30000000-0000-0000-0000-000000000002'
                    )
                  AND [PermissionCode] = N'counterparty.parties.read'
            )
            BEGIN
                INSERT INTO [identity].[RolePermissions] ([RoleId], [PermissionCode])
                SELECT [RoleId], N'counterparty.parties.read'
                FROM
                    (VALUES
                        (CONVERT(uniqueidentifier, '30000000-0000-0000-0000-000000000001')),
                        (CONVERT(uniqueidentifier, '30000000-0000-0000-0000-000000000002')))
                    AS [Expected]([RoleId])
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM [identity].[RolePermissions] AS [Existing]
                    WHERE [Existing].[RoleId] = [Expected].[RoleId]
                      AND [Existing].[PermissionCode] = N'counterparty.parties.read'
                );
            END;
            """;
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task<T> ScalarAsync<T>(
        SqlConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
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

    private sealed record PartySeed(
        Guid Id,
        string Code,
        string Kind,
        string LegalName,
        string? TradeName,
        bool IsActive);

    private sealed record AccountSeed(
        Guid Id,
        Guid CompanyId,
        Guid PartyId,
        int Roles,
        bool IsActive);

    private sealed class CapturingCounterpartyReadSqlObserver
        : ICounterpartyReadSqlObserver, IAuthorizationSnapshotReadObserver
    {
        private readonly Lock _gate = new();
        private readonly List<ObservedReadCommand> _commands = [];
        private int _authorizationDatabaseReads;

        public void Observe(
            CounterpartyReadSqlOperation operation,
            string commandText)
        {
            lock (_gate)
            {
                _commands.Add(new ObservedReadCommand(operation, commandText));
            }
        }

        public void ObserveDatabaseRead()
        {
            lock (_gate)
            {
                _authorizationDatabaseReads++;
            }
        }

        public IReadOnlyList<ObservedReadCommand> Snapshot()
        {
            lock (_gate)
            {
                return _commands.ToArray();
            }
        }

        public int AuthorizationDatabaseReads
        {
            get
            {
                lock (_gate)
                {
                    return _authorizationDatabaseReads;
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _commands.Clear();
                _authorizationDatabaseReads = 0;
            }
        }
    }

    private sealed record ObservedReadCommand(
        CounterpartyReadSqlOperation Operation,
        string CommandText);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
