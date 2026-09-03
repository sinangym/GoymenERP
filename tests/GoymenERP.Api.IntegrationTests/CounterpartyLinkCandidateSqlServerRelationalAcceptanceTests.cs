using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyLinkCandidateSqlServerRelationalAcceptanceTests
{
    private const int BulkFixtureSize = 105;
    private const int ExpectedCandidateCount = BulkFixtureSize + 4;
    private const string ActorA = "group-bootstrap-admin";
    private const string ActorB = "other-authorized-actor";
    private const string SessionA = "session-a";
    private const string SessionB = "session-b";
    private const string SourceSecret = "CLINK-SOURCE-SECRET";
    private const string HiddenSourceSecret = "CLINK-HIDDEN-SOURCE-SECRET";
    private const string ForeignSourceSecret = "CLINK-FOREIGN-SOURCE-SECRET";

    private static readonly Guid ForeignCompanyId = Guid.Parse(
        "10000000-0000-0000-0000-000000000004");
    private static readonly Guid NarrowSiteId = Guid.Parse(
        "10000000-0000-0000-0000-000000000101");
    private static readonly Guid ActiveSourcePartyId = Guid.Parse(
        "71000000-0000-0000-0000-000000000001");
    private static readonly Guid PassiveSourcePartyId = Guid.Parse(
        "71000000-0000-0000-0000-000000000002");
    private static readonly Guid InactivePartyId = Guid.Parse(
        "71000000-0000-0000-0000-000000000003");
    private static readonly Guid AlreadyLinkedPartyId = Guid.Parse(
        "71000000-0000-0000-0000-000000000004");
    private static readonly Guid ForeignOnlyPartyId = Guid.Parse(
        "71000000-0000-0000-0000-000000000005");
    private static readonly Guid MultiSourcePartyId = Guid.Parse(
        "71000000-0000-0000-0000-000000000006");
    private static readonly Guid TargetBOnlyPartyId = Guid.Parse(
        "71000000-0000-0000-0000-000000000007");
    private static readonly Guid TurkishAccentPartyId = Guid.Parse(
        "71000000-0000-0000-0000-000000000008");

    [SqlServerFact]
    [Trait("Issue", "200")]
    [Trait("Security", "SourceCompanyScope")]
    public async Task NarrowSourceScope_OmitsCandidateAndKnownIdLinkHasNoEffect_ThenSameKeySucceedsAfterCompanyWidePromotion()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var currentUser = new MutableCurrentUser(
            ActorA,
            TestCompanyIds.CompanyA,
            TestCompanyIds.CompanyB,
            TestCompanyIds.CompanyC);
        var companyContext = new MutableCompanyContext();
        var authorization = new MutableAuthorizationService();
        authorization.Set(
            TestCompanyIds.CompanyA,
            "source-a-narrow-1",
            PermissionScope.Denied,
            NarrowScope);
        authorization.Set(
            TestCompanyIds.CompanyB,
            "target-b-wide-1",
            CompanyWideScope,
            CompanyWideScope);
        authorization.Set(
            TestCompanyIds.CompanyC,
            "source-c-denied-1",
            PermissionScope.Denied,
            PermissionScope.Denied);

        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            services =>
            {
                services.RemoveAll<ICurrentUser>();
                services.AddSingleton<ICurrentUser>(currentUser);
                services.RemoveAll<ICompanyContext>();
                services.AddSingleton<ICompanyContext>(companyContext);
                services.RemoveAll<IErpAuthorizationService>();
                services.AddSingleton<IErpAuthorizationService>(authorization);
            });

        try
        {
            using var healthClient = factory.CreateClient();
            using var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await SeedSourceAuthorityScenarioAsync(connection);
            var databaseBefore = await ReadDatabaseCountsAsync(connection);
            var sourceBefore = await ReadSourceAccountAsync(connection);

            companyContext.Set(TestCompanyIds.CompanyB);
            using var client = factory.CreateClientFor(ActorA, TestCompanyIds.CompanyB);
            using var narrowCandidateResponse = await client.GetAsync(
                "/api/v1/counterparties/company-account-link-candidates?q=CAR-700001&pageSize=25");
            Assert.Equal(HttpStatusCode.OK, narrowCandidateResponse.StatusCode);
            Assert.DoesNotContain(
                ActiveSourcePartyId,
                await ReadCandidatePartyIdsAsync(narrowCandidateResponse));

            var idempotencyKey = $"cari-link-source-scope-{Guid.NewGuid():N}";
            var narrowCorrelation = $"cari-link-source-narrow-{Guid.NewGuid():N}";
            SetRequestIdentity(client, idempotencyKey, narrowCorrelation);
            var command = new CreateCompanyPartyAccount(PartyRole.Supplier);
            using var narrowLinkResponse = await client.PostAsJsonAsync(
                $"/api/v1/counterparties/{ActiveSourcePartyId}/company-account",
                command);
            await AssertProblemCodeAsync(
                narrowLinkResponse,
                HttpStatusCode.NotFound,
                CounterpartyProblemCodesV1.NotFound);
            Assert.False(narrowLinkResponse.Headers.Contains("Idempotency-Replayed"));
            Assert.False(narrowLinkResponse.Headers.Contains(
                "Idempotency-Original-Correlation-Id"));
            Assert.Equal(databaseBefore, await ReadDatabaseCountsAsync(connection));
            Assert.Equal(sourceBefore, await ReadSourceAccountAsync(connection));

            authorization.Set(
                TestCompanyIds.CompanyA,
                "source-a-wide-2",
                PermissionScope.Denied,
                CompanyWideScope);
            using var authorizedCandidateResponse = await client.GetAsync(
                "/api/v1/counterparties/company-account-link-candidates?q=CAR-700001&pageSize=25");
            Assert.Equal(HttpStatusCode.OK, authorizedCandidateResponse.StatusCode);
            Assert.Contains(
                ActiveSourcePartyId,
                await ReadCandidatePartyIdsAsync(authorizedCandidateResponse));

            var createdCorrelation = $"cari-link-source-created-{Guid.NewGuid():N}";
            SetRequestIdentity(client, idempotencyKey, createdCorrelation);
            using var createdResponse = await client.PostAsJsonAsync(
                $"/api/v1/counterparties/{ActiveSourcePartyId}/company-account",
                command);
            Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
            Assert.False(createdResponse.Headers.Contains("Idempotency-Replayed"));
            var createdBody = await createdResponse.Content.ReadAsStringAsync();
            Guid createdAccountId;
            using (var createdDocument = JsonDocument.Parse(createdBody))
            {
                Assert.Equal(
                    ActiveSourcePartyId,
                    createdDocument.RootElement.GetProperty("id").GetGuid());
                var account = Assert.Single(createdDocument.RootElement
                    .GetProperty("companyAccounts")
                    .EnumerateArray());
                Assert.Equal(
                    TestCompanyIds.CompanyB,
                    account.GetProperty("companyId").GetGuid());
                createdAccountId = account.GetProperty("id").GetGuid();
                Assert.NotEqual(Guid.Empty, createdAccountId);
            }

            var afterCreated = databaseBefore with
            {
                Accounts = databaseBefore.Accounts + 1,
                AuditEvents = databaseBefore.AuditEvents + 1,
                CommandExecutions = databaseBefore.CommandExecutions + 1,
            };
            Assert.Equal(afterCreated, await ReadDatabaseCountsAsync(connection));
            Assert.Equal(sourceBefore, await ReadSourceAccountAsync(connection));
            await AssertSingleCreatedTargetEffectAsync(
                connection,
                idempotencyKey,
                createdCorrelation,
                createdAccountId,
                command.Roles);

            var replayCorrelation = $"cari-link-source-replay-{Guid.NewGuid():N}";
            SetRequestIdentity(client, idempotencyKey, replayCorrelation);
            using var replayResponse = await client.PostAsJsonAsync(
                $"/api/v1/counterparties/{ActiveSourcePartyId}/company-account",
                command);
            Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
            Assert.Equal(
                "true",
                Assert.Single(replayResponse.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                createdCorrelation,
                Assert.Single(replayResponse.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));
            Assert.Equal(createdBody, await replayResponse.Content.ReadAsStringAsync());
            Assert.Equal(afterCreated, await ReadDatabaseCountsAsync(connection));

            authorization.Set(
                TestCompanyIds.CompanyA,
                "source-a-revoked-3",
                PermissionScope.Denied,
                NarrowScope);
            SetRequestIdentity(
                client,
                idempotencyKey,
                $"cari-link-source-revoked-{Guid.NewGuid():N}");
            using var revokedReplayResponse = await client.PostAsJsonAsync(
                $"/api/v1/counterparties/{ActiveSourcePartyId}/company-account",
                command);
            await AssertProblemCodeAsync(
                revokedReplayResponse,
                HttpStatusCode.NotFound,
                CounterpartyProblemCodesV1.NotFound);
            Assert.False(revokedReplayResponse.Headers.Contains("Idempotency-Replayed"));
            Assert.False(revokedReplayResponse.Headers.Contains(
                "Idempotency-Original-Correlation-Id"));
            Assert.Equal(afterCreated, await ReadDatabaseCountsAsync(connection));
            Assert.Equal(sourceBefore, await ReadSourceAccountAsync(connection));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    [Trait("Acceptance", "CLINK-AC-001")]
    [Trait("Acceptance", "CLINK-AC-002")]
    [Trait("Acceptance", "CLINK-AC-003")]
    [Trait("Acceptance", "CLINK-AC-004")]
    [Trait("Acceptance", "CLINK-AC-005")]
    [Trait("Acceptance", "CLINK-AC-006")]
    [Trait("Acceptance", "CLINK-AC-007")]
    [Trait("Acceptance", "CLINK-AC-008")]
    [Trait("Acceptance", "CLINK-AC-009")]
    [Trait("Acceptance", "CLINK-AC-010")]
    public async Task CandidateRead_EnforcesVisibilityAuthorityCursorAndBoundedReadOnlySql()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var clock = new MutableTimeProvider(new DateTimeOffset(
            2026,
            9,
            1,
            9,
            0,
            0,
            TimeSpan.Zero));
        var currentUser = new MutableCurrentUser(
            ActorA,
            TestCompanyIds.CompanyA,
            TestCompanyIds.CompanyB,
            TestCompanyIds.CompanyC);
        var companyContext = new MutableCompanyContext();
        var authorization = new MutableAuthorizationService();
        var sqlObserver = new CapturingCounterpartyLinkCandidateSqlObserver();

        ConfigureDefaultAuthorization(authorization);
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            services =>
            {
                services.RemoveAll<ICurrentUser>();
                services.AddSingleton<ICurrentUser>(currentUser);
                services.RemoveAll<ICompanyContext>();
                services.AddSingleton<ICompanyContext>(companyContext);
                services.RemoveAll<IErpAuthorizationService>();
                services.AddSingleton<IErpAuthorizationService>(authorization);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.RemoveAll<ICounterpartyLinkCandidateSqlObserver>();
                services.AddSingleton<ICounterpartyLinkCandidateSqlObserver>(sqlObserver);
            });

        try
        {
            using var healthClient = factory.CreateClient();
            using var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.True(healthResponse.IsSuccessStatusCode);

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await SeedGoldenDataAsync(connection);
            var databaseBefore = await ReadDatabaseCountsAsync(connection);

            companyContext.Set(TestCompanyIds.CompanyB);
            sqlObserver.Clear();
            var firstExecution = await ExecuteAsync(
                factory,
                CreateRequest(query: null, pageSize: 25));
            var firstPage = AssertSucceeded(firstExecution);

            Assert.Equal(25, firstPage.Items.Count);
            Assert.True(firstPage.HasMore);
            Assert.False(string.IsNullOrWhiteSpace(firstPage.NextCursor));
            var firstIds = firstPage.Items.Select(item => item.Id).ToHashSet();
            Assert.Contains(ActiveSourcePartyId, firstIds);
            Assert.Contains(PassiveSourcePartyId, firstIds);
            Assert.Contains(MultiSourcePartyId, firstIds);
            Assert.Contains(TurkishAccentPartyId, firstIds);
            Assert.DoesNotContain(InactivePartyId, firstIds);
            Assert.DoesNotContain(AlreadyLinkedPartyId, firstIds);
            Assert.DoesNotContain(ForeignOnlyPartyId, firstIds);
            Assert.DoesNotContain(TargetBOnlyPartyId, firstIds);

            var active = firstPage.Items.Single(item => item.Id == ActiveSourcePartyId);
            var activeSource = Assert.Single(active.VisibleSourceAccounts);
            Assert.Equal(TestCompanyIds.CompanyA, activeSource.CompanyId);
            Assert.True(activeSource.IsActive);
            Assert.Equal("1", activeSource.Version);

            var passive = firstPage.Items.Single(item => item.Id == PassiveSourcePartyId);
            var passiveSource = Assert.Single(passive.VisibleSourceAccounts);
            Assert.Equal(TestCompanyIds.CompanyA, passiveSource.CompanyId);
            Assert.False(passiveSource.IsActive);

            var multiSource = firstPage.Items.Single(item => item.Id == MultiSourcePartyId);
            var onlyVisibleSource = Assert.Single(multiSource.VisibleSourceAccounts);
            Assert.Equal(TestCompanyIds.CompanyA, onlyVisibleSource.CompanyId);
            Assert.Equal(PartyRole.Customer, onlyVisibleSource.Roles);

            AssertCandidateContractIsRedacted(firstPage);
            AssertBoundedSql(sqlObserver.Snapshot(), expectedOperations: 2);

            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] "
                    + $"WHERE [PartyId] = '{PassiveSourcePartyId:D}' "
                    + $"AND [CompanyId] = '{TestCompanyIds.CompanyB:D}';"));
            Assert.False(await ScalarAsync<bool>(
                connection,
                "SELECT [IsActive] FROM [counterparty].[CompanyPartyAccounts] "
                + $"WHERE [PartyId] = '{PassiveSourcePartyId:D}' "
                + $"AND [CompanyId] = '{TestCompanyIds.CompanyA:D}';"));

            await AssertTargetPermissionMatrixAsync(
                factory,
                authorization,
                sqlObserver);

            ConfigureDefaultAuthorization(authorization);
            companyContext.Set(TestCompanyIds.CompanyA);
            sqlObserver.Clear();
            var targetAExecution = await ExecuteAsync(
                factory,
                CreateRequest(query: null, pageSize: 25));
            var targetAPage = AssertSucceeded(targetAExecution);
            Assert.Contains(targetAPage.Items, item => item.Id == TargetBOnlyPartyId);
            Assert.DoesNotContain(targetAPage.Items, item => item.Id == ActiveSourcePartyId);
            Assert.All(
                targetAPage.Items.SelectMany(item => item.VisibleSourceAccounts),
                account => Assert.Equal(TestCompanyIds.CompanyB, account.CompanyId));
            AssertBoundedSql(sqlObserver.Snapshot(), expectedOperations: 2);

            Assert.Equal(
                ["Cursor", "PageSize", "Query"],
                typeof(CounterpartyLinkCandidateQuery)
                    .GetProperties()
                    .Select(property => property.Name)
                    .Order(StringComparer.Ordinal));

            companyContext.Set(TestCompanyIds.CompanyB);
            await AssertSearchIdsAsync(factory, sqlObserver, "izmir", ActiveSourcePartyId);
            await AssertSearchIdsAsync(factory, sqlObserver, "ısparta", PassiveSourcePartyId);
            await AssertSearchIdsAsync(factory, sqlObserver, "isparta");
            await AssertSearchIdsAsync(factory, sqlObserver, "seker");
            await AssertSearchIdsAsync(factory, sqlObserver, "şeker", TurkishAccentPartyId);
            await AssertSearchIdsAsync(factory, sqlObserver, "I\u0307ZMİR", ActiveSourcePartyId);

            var page25Ids = await TraverseAsync(factory, sqlObserver, pageSize: 25);
            Assert.Equal(ExpectedCandidateCount, page25Ids.Count);
            Assert.Equal(ExpectedCandidateCount, page25Ids.Distinct().Count());

            var page100Ids = await TraverseAsync(factory, sqlObserver, pageSize: 100);
            Assert.Equal(ExpectedCandidateCount, page100Ids.Count);
            Assert.Equal(ExpectedCandidateCount, page100Ids.Distinct().Count());

            sqlObserver.Clear();
            var emptyExecution = await ExecuteAsync(
                factory,
                CreateRequest("bulunamayacak-cari", pageSize: 100));
            var emptyPage = AssertSucceeded(emptyExecution);
            Assert.Empty(emptyPage.Items);
            Assert.False(emptyPage.HasMore);
            Assert.Null(emptyPage.NextCursor);
            AssertBoundedSql(sqlObserver.Snapshot(), expectedOperations: 1);

            ConfigureDefaultAuthorization(authorization);
            currentUser.UserId = ActorA;
            companyContext.Set(TestCompanyIds.CompanyB);
            var cursorStart = AssertSucceeded(await ExecuteAsync(
                factory,
                CreateRequest(query: null, pageSize: 1)));
            var cursor = Assert.IsType<string>(cursorStart.NextCursor);

            sqlObserver.Clear();
            var forgedActor = await ExecuteAsync(
                factory,
                CreateRequest(null, 1, cursor, ActorB, SessionA));
            Assert.Equal(CounterpartyLinkCandidateResult.Forbidden, forgedActor.Result);
            Assert.Null(forgedActor.Page);
            Assert.Empty(sqlObserver.Snapshot());

            await AssertInvalidCursorAsync(
                factory,
                sqlObserver,
                CreateRequest(null, 1, Tamper(cursor)));

            currentUser.UserId = ActorB;
            await AssertInvalidCursorAsync(
                factory,
                sqlObserver,
                CreateRequest(null, 1, cursor, ActorB, SessionA));
            currentUser.UserId = ActorA;

            await AssertInvalidCursorAsync(
                factory,
                sqlObserver,
                CreateRequest(null, 1, cursor, ActorA, SessionB));
            await AssertInvalidCursorAsync(
                factory,
                sqlObserver,
                CreateRequest("izmir", 1, cursor));
            await AssertInvalidCursorAsync(
                factory,
                sqlObserver,
                CreateRequest(null, 2, cursor));

            companyContext.Set(TestCompanyIds.CompanyA);
            await AssertInvalidCursorAsync(
                factory,
                sqlObserver,
                CreateRequest(null, 1, cursor));
            companyContext.Set(TestCompanyIds.CompanyB);

            authorization.SetRevision(TestCompanyIds.CompanyA, "source-a-revision-2");
            await AssertInvalidCursorAsync(
                factory,
                sqlObserver,
                CreateRequest(null, 1, cursor));
            ConfigureDefaultAuthorization(authorization);

            authorization.SetRevision(TestCompanyIds.CompanyB, "target-b-revision-2");
            await AssertInvalidCursorAsync(
                factory,
                sqlObserver,
                CreateRequest(null, 1, cursor));
            ConfigureDefaultAuthorization(authorization);

            clock.UtcNow = clock.UtcNow.AddMinutes(31);
            await AssertInvalidCursorAsync(
                factory,
                sqlObserver,
                CreateRequest(null, 1, cursor));

            Assert.Equal(databaseBefore, await ReadDatabaseCountsAsync(connection));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static void ConfigureDefaultAuthorization(
        MutableAuthorizationService authorization)
    {
        authorization.Set(
            TestCompanyIds.CompanyA,
            "target-a-revision-1",
            CompanyWideScope,
            CompanyWideScope);
        authorization.Set(
            TestCompanyIds.CompanyB,
            "target-b-revision-1",
            CompanyWideScope,
            CompanyWideScope);
        authorization.Set(
            TestCompanyIds.CompanyC,
            "source-c-revision-1",
            PermissionScope.Denied,
            PermissionScope.Denied);
    }

    private static async Task AssertTargetPermissionMatrixAsync(
        SqlServerRelationalApiFactory factory,
        MutableAuthorizationService authorization,
        CapturingCounterpartyLinkCandidateSqlObserver sqlObserver)
    {
        var rejectedCases = new[]
        {
            new TargetPermissionCase(PermissionScope.Denied, CompanyWideScope),
            new TargetPermissionCase(CompanyWideScope, PermissionScope.Denied),
            new TargetPermissionCase(NarrowScope, CompanyWideScope),
            new TargetPermissionCase(CompanyWideScope, NarrowScope),
        };

        for (var index = 0; index < rejectedCases.Length; index++)
        {
            var rejectedCase = rejectedCases[index];
            authorization.Set(
                TestCompanyIds.CompanyB,
                $"target-b-matrix-{index}",
                rejectedCase.CreateScope,
                rejectedCase.ReadScope);
            sqlObserver.Clear();

            var execution = await ExecuteAsync(
                factory,
                CreateRequest(query: null, pageSize: 25));

            Assert.Equal(CounterpartyLinkCandidateResult.Forbidden, execution.Result);
            Assert.Null(execution.Page);
            Assert.Empty(sqlObserver.Snapshot());
        }

        authorization.Set(
            TestCompanyIds.CompanyB,
            "target-b-matrix-authorized",
            CompanyWideScope,
            CompanyWideScope);
        sqlObserver.Clear();
        var authorized = await ExecuteAsync(
            factory,
            CreateRequest(query: null, pageSize: 25));
        Assert.NotEmpty(AssertSucceeded(authorized).Items);
        AssertBoundedSql(sqlObserver.Snapshot(), expectedOperations: 2);
    }

    private static async Task AssertSearchIdsAsync(
        SqlServerRelationalApiFactory factory,
        CapturingCounterpartyLinkCandidateSqlObserver sqlObserver,
        string query,
        params Guid[] expectedIds)
    {
        sqlObserver.Clear();
        var execution = await ExecuteAsync(
            factory,
            CreateRequest(query, pageSize: 100));
        var page = AssertSucceeded(execution);
        Assert.Equal(
            expectedIds.Order(),
            page.Items.Select(item => item.Id).Order());
        AssertBoundedSql(
            sqlObserver.Snapshot(),
            expectedOperations: expectedIds.Length == 0 ? 1 : 2);
    }

    private static async Task<IReadOnlyList<Guid>> TraverseAsync(
        SqlServerRelationalApiFactory factory,
        CapturingCounterpartyLinkCandidateSqlObserver sqlObserver,
        int pageSize)
    {
        var ids = new List<Guid>();
        string? cursor = null;
        var pageCount = 0;
        do
        {
            sqlObserver.Clear();
            var execution = await ExecuteAsync(
                factory,
                CreateRequest(query: null, pageSize, cursor));
            var page = AssertSucceeded(execution);
            Assert.InRange(page.Items.Count, 1, pageSize);
            AssertBoundedSql(sqlObserver.Snapshot(), expectedOperations: 2);
            ids.AddRange(page.Items.Select(item => item.Id));
            cursor = page.NextCursor;
            pageCount++;
            Assert.True(pageCount <= 10, "Candidate cursor traversal failed to terminate.");
        }
        while (cursor is not null);

        return ids;
    }

    private static async Task AssertInvalidCursorAsync(
        SqlServerRelationalApiFactory factory,
        CapturingCounterpartyLinkCandidateSqlObserver sqlObserver,
        CounterpartyLinkCandidateRequest request)
    {
        sqlObserver.Clear();
        var execution = await ExecuteAsync(factory, request);
        Assert.Equal(CounterpartyLinkCandidateResult.InvalidCursor, execution.Result);
        Assert.Null(execution.Page);
        Assert.Empty(sqlObserver.Snapshot());
    }

    private static CounterpartyLinkCandidatePage AssertSucceeded(
        CounterpartyLinkCandidateExecution execution)
    {
        Assert.Equal(CounterpartyLinkCandidateResult.Succeeded, execution.Result);
        return Assert.IsType<CounterpartyLinkCandidatePage>(execution.Page);
    }

    private static void AssertCandidateContractIsRedacted(
        CounterpartyLinkCandidatePage page)
    {
        Assert.Equal(
            ["HasMore", "Items", "NextCursor"],
            typeof(CounterpartyLinkCandidatePage)
                .GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Code", "Id", "IsActive", "Kind", "LegalName", "TradeName", "VisibleSourceAccounts"],
            typeof(CounterpartyLinkCandidateItem)
                .GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            ["CompanyId", "Id", "IsActive", "Roles", "Version"],
            typeof(CounterpartyLinkCandidateSourceAccount)
                .GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));

        var serialized = JsonSerializer.Serialize(page);
        Assert.DoesNotContain("externalIntegrationCode", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SourceSecret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(HiddenSourceSecret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(ForeignSourceSecret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(
            TestCompanyIds.CompanyC.ToString("D"),
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            ForeignCompanyId.ToString("D"),
            serialized,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertBoundedSql(
        IReadOnlyList<ObservedCandidateCommand> commands,
        int expectedOperations)
    {
        Assert.InRange(commands.Count, 1, 2);
        Assert.Equal(expectedOperations, commands.Count);
        Assert.Equal(CounterpartyLinkCandidateSqlOperation.Page, commands[0].Operation);
        Assert.Contains("SELECT TOP (@Take)", commands[0].CommandText, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            @"(?i)\bCOUNT(?:_BIG)?\s*\(",
            commands[0].CommandText);

        if (expectedOperations == 2)
        {
            Assert.Equal(
                CounterpartyLinkCandidateSqlOperation.SourceAccounts,
                commands[1].Operation);
            Assert.Contains(
                "OPENJSON(@PartyIds)",
                commands[1].CommandText,
                StringComparison.Ordinal);
        }
    }

    private static async Task<CounterpartyLinkCandidateExecution> ExecuteAsync(
        SqlServerRelationalApiFactory factory,
        CounterpartyLinkCandidateRequest request)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider
            .GetRequiredService<ICounterpartyLinkCandidateReadService>();
        return await service.GetAuthorizedPageAsync(request);
    }

    private static CounterpartyLinkCandidateRequest CreateRequest(
        string? query,
        int pageSize,
        string? cursor = null,
        string actorId = ActorA,
        string sessionBinding = SessionA) =>
        new(
            new CounterpartyLinkCandidateQuery(
                query?.Trim().Normalize(),
                pageSize,
                cursor),
            actorId,
            sessionBinding);

    private static string Tamper(string cursor) =>
        cursor[..^1] + (cursor[^1] == 'A' ? 'B' : 'A');

    private static async Task SeedGoldenDataAsync(SqlConnection connection)
    {
        var parties = new List<PartySeed>
        {
            new(ActiveSourcePartyId, "CAR-700001", "LegalEntity", "İZMİR Aktif Çelik", "Aktif İzmir", true),
            new(PassiveSourcePartyId, "CAR-700002", "LegalEntity", "ISPARTA Pasif Kaynak", null, true),
            new(InactivePartyId, "CAR-700003", "LegalEntity", "Global Pasif Cari", null, false),
            new(AlreadyLinkedPartyId, "CAR-700004", "LegalEntity", "Hedefte Zaten Bağlı", null, true),
            new(ForeignOnlyPartyId, "CAR-700005", "LegalEntity", "Yabancı Tek Kaynak", null, true),
            new(MultiSourcePartyId, "CAR-700006", "LegalEntity", "Çok Kaynaklı Cari", null, true),
            new(TargetBOnlyPartyId, "CAR-700007", "LegalEntity", "Yalnız B Kaynak", null, true),
            new(TurkishAccentPartyId, "CAR-700008", "NaturalPerson", "Şeker Sanayi", null, true),
        };
        var accounts = new List<AccountSeed>
        {
            new(Guid.Parse("72000000-0000-0000-0000-000000000001"), TestCompanyIds.CompanyA, ActiveSourcePartyId, (int)PartyRole.Customer, SourceSecret, true),
            new(Guid.Parse("72000000-0000-0000-0000-000000000002"), TestCompanyIds.CompanyA, PassiveSourcePartyId, (int)PartyRole.Supplier, SourceSecret, false),
            new(Guid.Parse("72000000-0000-0000-0000-000000000003"), TestCompanyIds.CompanyA, InactivePartyId, (int)PartyRole.Customer, SourceSecret, true),
            new(Guid.Parse("72000000-0000-0000-0000-000000000004"), TestCompanyIds.CompanyA, AlreadyLinkedPartyId, (int)PartyRole.Customer, SourceSecret, true),
            new(Guid.Parse("72000000-0000-0000-0000-000000000005"), TestCompanyIds.CompanyB, AlreadyLinkedPartyId, (int)PartyRole.Supplier, "TARGET-SECRET", true),
            new(Guid.Parse("72000000-0000-0000-0000-000000000006"), ForeignCompanyId, ForeignOnlyPartyId, (int)PartyRole.Service, ForeignSourceSecret, true),
            new(Guid.Parse("72000000-0000-0000-0000-000000000007"), TestCompanyIds.CompanyA, MultiSourcePartyId, (int)PartyRole.Customer, SourceSecret, true),
            new(Guid.Parse("72000000-0000-0000-0000-000000000008"), TestCompanyIds.CompanyC, MultiSourcePartyId, (int)PartyRole.Supplier, HiddenSourceSecret, true),
            new(Guid.Parse("72000000-0000-0000-0000-000000000009"), ForeignCompanyId, MultiSourcePartyId, (int)PartyRole.Service, ForeignSourceSecret, true),
            new(Guid.Parse("72000000-0000-0000-0000-000000000010"), TestCompanyIds.CompanyB, TargetBOnlyPartyId, (int)PartyRole.Supplier, "TARGET-ONLY-SECRET", true),
            new(Guid.Parse("72000000-0000-0000-0000-000000000011"), TestCompanyIds.CompanyA, TurkishAccentPartyId, (int)PartyRole.Customer, SourceSecret, true),
        };

        for (var index = 1; index <= BulkFixtureSize; index++)
        {
            var partyId = Guid.Parse($"73000000-0000-0000-0000-{index:D12}");
            parties.Add(new PartySeed(
                partyId,
                $"CAR-{710000 + index:D6}",
                "LegalEntity",
                $"Yük Adayı {index:D3}",
                index % 2 == 0 ? $"Ticari Aday {index:D3}" : null,
                true));
            accounts.Add(new AccountSeed(
                Guid.Parse($"74000000-0000-0000-0000-{index:D12}"),
                TestCompanyIds.CompanyA,
                partyId,
                (int)PartyRole.Customer,
                $"BULK-SECRET-{index:D3}",
                true));
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO [organization].[Companies]
                ([Id], [Code], [LegalName], [Vkn], [IsPlaceholder], [InvoiceCapable], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@ForeignCompanyId, N'FOREIGN-CLINK-TEST', N'Yabancı Link Test Şirketi', NULL, 1, 0, 1, 1, @Now, @Now);

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
            SELECT [Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode], [IsActive], 1, @Now, @Now
            FROM OPENJSON(@Accounts)
            WITH
            (
                [Id] uniqueidentifier '$.Id',
                [CompanyId] uniqueidentifier '$.CompanyId',
                [PartyId] uniqueidentifier '$.PartyId',
                [Roles] int '$.Roles',
                [ExternalIntegrationCode] nvarchar(100) '$.ExternalIntegrationCode',
                [IsActive] bit '$.IsActive'
            );
            """;
        command.Parameters.AddWithValue("@ForeignCompanyId", ForeignCompanyId);
        command.Parameters.AddWithValue("@Parties", JsonSerializer.Serialize(parties));
        command.Parameters.AddWithValue("@Accounts", JsonSerializer.Serialize(accounts));
        command.Parameters.AddWithValue("@Now", new DateTimeOffset(
            2026,
            9,
            1,
            8,
            0,
            0,
            TimeSpan.Zero));
        Assert.Equal(parties.Count + accounts.Count + 1, await command.ExecuteNonQueryAsync());
    }

    private static async Task SeedSourceAuthorityScenarioAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO [counterparty].[Parties]
                ([Id], [Code], [Kind], [LegalName], [TradeName], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@PartyId, N'CAR-700001', N'LegalEntity', N'Kaynak Yetki Test Cari', NULL, 1, 1, @Now, @Now);

            INSERT INTO [counterparty].[CompanyPartyAccounts]
                ([Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@AccountId, @SourceCompanyId, @PartyId, @Roles, @ExternalIntegrationCode, 1, 1, @Now, @Now);
            """;
        command.Parameters.AddRange(
        [
            new SqlParameter("@PartyId", ActiveSourcePartyId),
            new SqlParameter(
                "@AccountId",
                Guid.Parse("72000000-0000-0000-0000-000000000001")),
            new SqlParameter("@SourceCompanyId", TestCompanyIds.CompanyA),
            new SqlParameter("@Roles", (int)PartyRole.Customer),
            new SqlParameter("@ExternalIntegrationCode", SourceSecret),
            new SqlParameter(
                "@Now",
                new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero)),
        ]);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task<IReadOnlySet<Guid>> ReadCandidatePartyIdsAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToHashSet();
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

    private static async Task AssertProblemCodeAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            (int)expectedStatus,
            document.RootElement.GetProperty("status").GetInt32());
    }

    private static async Task<SourceAccountState> ReadSourceAccountAsync(
        SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT [Roles], [ExternalIntegrationCode], [IsActive], [Version]
            FROM [counterparty].[CompanyPartyAccounts]
            WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;
            """;
        command.Parameters.AddRange(
        [
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
            new SqlParameter("@PartyId", ActiveSourcePartyId),
        ]);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new SourceAccountState(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetInt64(3));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task<DatabaseCounts> ReadDatabaseCountsAsync(
        SqlConnection connection) =>
        new(
            await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [counterparty].[Parties];"),
            await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [counterparty].[CompanyPartyAccounts];"),
            await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [audit].[AuditEvents];"),
            await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [idempotency].[CommandExecutions];"));

    private static async Task AssertSingleCreatedTargetEffectAsync(
        SqlConnection connection,
        string idempotencyKey,
        string correlationId,
        Guid accountId,
        PartyRole roles)
    {
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                """
                SELECT COUNT(*)
                FROM [counterparty].[CompanyPartyAccounts]
                WHERE [Id] = @AccountId
                  AND [CompanyId] = @CompanyId
                  AND [PartyId] = @PartyId
                  AND [Roles] = @Roles
                  AND [ExternalIntegrationCode] IS NULL
                  AND [IsActive] = 1
                  AND [Version] = 1;
                """,
                new SqlParameter("@AccountId", accountId),
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                new SqlParameter("@PartyId", ActiveSourcePartyId),
                new SqlParameter("@Roles", (int)roles)));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                """
                SELECT COUNT(*)
                FROM [audit].[AuditEvents]
                WHERE [CompanyId] = @CompanyId
                  AND [EventType] = N'counterparty.company-account.created.v1'
                  AND [EntityType] = N'Counterparty.CompanyPartyAccount'
                  AND [EntityId] = @EntityId
                  AND [Operation] = N'Create'
                  AND [ActorId] = @ActorId
                  AND [PermissionCode] = N'counterparty.accounts.create'
                  AND [CorrelationId] = @CorrelationId
                  AND [IdempotencyKeyHash] = @KeyHash
                  AND [Outcome] = N'Succeeded';
                """,
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                new SqlParameter("@EntityId", accountId.ToString("D")),
                new SqlParameter("@ActorId", ActorA),
                new SqlParameter("@CorrelationId", correlationId),
                new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(idempotencyKey))));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                """
                SELECT COUNT(*)
                FROM [idempotency].[CommandExecutions]
                WHERE [CompanyId] = @CompanyId
                  AND [CommandName] = N'counterparty.company-account.create.v1'
                  AND [IdempotencyKeyHash] = @KeyHash
                  AND [ActorId] = @ActorId
                  AND [Outcome] = N'Created'
                  AND [HttpStatusCode] = 201
                  AND [OriginalCorrelationId] = @CorrelationId
                  AND [SchemaVersion] = 1;
                """,
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(idempotencyKey)),
                new SqlParameter("@ActorId", ActorA),
                new SqlParameter("@CorrelationId", correlationId)));
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

    private static PermissionScope CompanyWideScope { get; } = new(
        true,
        true,
        new HashSet<string>(["SYSTEM_ADMINISTRATOR"], StringComparer.Ordinal),
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>());

    private static PermissionScope NarrowScope { get; } = new(
        true,
        false,
        new HashSet<string>(["SYSTEM_ADMINISTRATOR"], StringComparer.Ordinal),
        new HashSet<Guid>([NarrowSiteId]),
        new HashSet<Guid>(),
        new HashSet<Guid>());

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
        string ExternalIntegrationCode,
        bool IsActive);

    private sealed record DatabaseCounts(
        long Parties,
        long Accounts,
        long AuditEvents,
        long CommandExecutions);

    private sealed record SourceAccountState(
        int Roles,
        string ExternalIntegrationCode,
        bool IsActive,
        long Version);

    private sealed record TargetPermissionCase(
        PermissionScope CreateScope,
        PermissionScope ReadScope);

    private sealed record AuthorizationState(
        string Revision,
        PermissionScope CreateScope,
        PermissionScope ReadScope);

    private sealed record ObservedCandidateCommand(
        CounterpartyLinkCandidateSqlOperation Operation,
        string CommandText);

    private sealed class MutableCurrentUser(
        string userId,
        params Guid[] allowedCompanyIds) : ICurrentUser
    {
        private readonly HashSet<Guid> _allowedCompanyIds = [.. allowedCompanyIds];

        public string UserId { get; set; } = userId;

        string? ICurrentUser.UserId => UserId;

        public bool IsAuthenticated => true;

        public IReadOnlySet<Guid> AllowedCompanyIds => _allowedCompanyIds;

        public bool CanAccessCompany(Guid companyId) =>
            _allowedCompanyIds.Contains(companyId);
    }

    private sealed class MutableCompanyContext : ICompanyContext
    {
        public bool IsResolved { get; private set; }

        public Guid CompanyId { get; private set; }

        public void Set(Guid companyId)
        {
            if (companyId == Guid.Empty)
            {
                throw new ArgumentException("CompanyId boş olamaz.", nameof(companyId));
            }

            CompanyId = companyId;
            IsResolved = true;
        }
    }

    private sealed class MutableAuthorizationService : IErpAuthorizationService
    {
        private readonly Dictionary<Guid, AuthorizationState> _states = [];

        public void Set(
            Guid companyId,
            string revision,
            PermissionScope createScope,
            PermissionScope readScope) =>
            _states[companyId] = new AuthorizationState(
                revision,
                createScope,
                readScope);

        public void SetRevision(Guid companyId, string revision)
        {
            var current = _states[companyId];
            _states[companyId] = current with { Revision = revision };
        }

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(
            Guid companyId,
            IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = _states.GetValueOrDefault(
                companyId,
                new AuthorizationState(
                    $"missing-{companyId:N}",
                    PermissionScope.Denied,
                    PermissionScope.Denied));
            var scopes = permissions.Distinct(StringComparer.Ordinal).ToDictionary(
                permission => permission,
                permission => ResolveScope(state, permission),
                StringComparer.Ordinal);
            return Task.FromResult(new AuthorizationSnapshot(
                state.Revision,
                new HashSet<Guid>([companyId]),
                scopes));
        }

        public Task<bool> HasPermissionAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            GetPermissionScopeAsync(permission, companyId, cancellationToken)
                .ContinueWith(
                    task => task.Result.IsGranted,
                    cancellationToken,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

        public Task<PermissionScope> GetPermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = _states.GetValueOrDefault(
                companyId,
                new AuthorizationState(
                    $"missing-{companyId:N}",
                    PermissionScope.Denied,
                    PermissionScope.Denied));
            return Task.FromResult(ResolveScope(state, permission));
        }

        private static PermissionScope ResolveScope(
            AuthorizationState state,
            string permission) =>
            permission switch
            {
                ErpPermissions.CounterpartyAccountsCreate => state.CreateScope,
                ErpPermissions.CounterpartyPartiesRead => state.ReadScope,
                _ => PermissionScope.Denied,
            };
    }

    private sealed class CapturingCounterpartyLinkCandidateSqlObserver
        : ICounterpartyLinkCandidateSqlObserver
    {
        private readonly Lock _gate = new();
        private readonly List<ObservedCandidateCommand> _commands = [];

        public void Observe(
            CounterpartyLinkCandidateSqlOperation operation,
            string commandText)
        {
            lock (_gate)
            {
                _commands.Add(new ObservedCandidateCommand(operation, commandText));
            }
        }

        public IReadOnlyList<ObservedCandidateCommand> Snapshot()
        {
            lock (_gate)
            {
                return _commands.ToArray();
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _commands.Clear();
            }
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
