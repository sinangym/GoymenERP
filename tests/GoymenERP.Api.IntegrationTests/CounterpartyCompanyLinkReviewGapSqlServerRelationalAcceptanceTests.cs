using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyCompanyLinkReviewGapSqlServerRelationalAcceptanceTests
{
    private static readonly Guid SystemAdministratorRoleId = Guid.Parse(
        "30000000-0000-0000-0000-000000000003");

    [SqlServerFact]
    public async Task SameAndDifferentKeyRaces_ProduceExactlyOneCreatedAuditPerParty()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            await AssertHealthyAsync(factory);
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();

            var sameKeyParty = await CreatePartyAsync(
                factory,
                "SQL Same-Key Audit Tekillik Cari A.Ş.");
            var sameKey = $"cari-link-audit-race-same-{Guid.NewGuid():N}";
            var sameCorrelationA = $"cari-link-audit-race-same-a-{Guid.NewGuid():N}";
            var sameCorrelationB = $"cari-link-audit-race-same-b-{Guid.NewGuid():N}";
            using var sameClientA = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            using var sameClientB = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(sameClientA, sameKey, sameCorrelationA);
            SetRequestIdentity(sameClientB, sameKey, sameCorrelationB);
            var command = new CreateCompanyPartyAccount(PartyRole.Customer);

            var sameResponses = await Task.WhenAll(
                sameClientA.PostAsJsonAsync(
                    $"/api/v1/counterparties/{sameKeyParty.PartyId}/company-account",
                    command),
                sameClientB.PostAsJsonAsync(
                    $"/api/v1/counterparties/{sameKeyParty.PartyId}/company-account",
                    command));
            try
            {
                Assert.All(
                    sameResponses,
                    response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
                Assert.Equal(
                    1,
                    sameResponses.Count(response =>
                        response.Headers.Contains("Idempotency-Replayed")));
                Assert.Equal(
                    1,
                    await CountTargetAccountsAsync(connection, sameKeyParty.PartyId));
                Assert.Equal(
                    1,
                    await CountExecutionsAsync(connection, sameKey));
                Assert.Equal(
                    1,
                    await CountCreatedLinkAuditsAsync(connection));
            }
            finally
            {
                foreach (var response in sameResponses)
                {
                    response.Dispose();
                }
            }

            var differentKeyParty = await CreatePartyAsync(
                factory,
                "SQL Different-Key Audit Tekillik Cari A.Ş.");
            var differentKeyA = $"cari-link-audit-race-diff-a-{Guid.NewGuid():N}";
            var differentKeyB = $"cari-link-audit-race-diff-b-{Guid.NewGuid():N}";
            using var differentClientA = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            using var differentClientB = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(
                differentClientA,
                differentKeyA,
                $"cari-link-audit-race-diff-corr-a-{Guid.NewGuid():N}");
            SetRequestIdentity(
                differentClientB,
                differentKeyB,
                $"cari-link-audit-race-diff-corr-b-{Guid.NewGuid():N}");

            var differentResponses = await Task.WhenAll(
                differentClientA.PostAsJsonAsync(
                    $"/api/v1/counterparties/{differentKeyParty.PartyId}/company-account",
                    command),
                differentClientB.PostAsJsonAsync(
                    $"/api/v1/counterparties/{differentKeyParty.PartyId}/company-account",
                    command));
            try
            {
                Assert.Single(
                    differentResponses,
                    response => response.StatusCode == HttpStatusCode.Created);
                Assert.Single(
                    differentResponses,
                    response => response.StatusCode == HttpStatusCode.Conflict);
                Assert.DoesNotContain(
                    differentResponses,
                    response => response.StatusCode == HttpStatusCode.InternalServerError);
                Assert.Equal(
                    1,
                    await CountTargetAccountsAsync(connection, differentKeyParty.PartyId));
                Assert.Equal(
                    2,
                    await CountExecutionsAsync(
                        connection,
                        differentKeyA,
                        differentKeyB));
                Assert.Equal(
                    1,
                    await CountExecutionOutcomeAsync(
                        connection,
                        "Created",
                        differentKeyA,
                        differentKeyB));
                Assert.Equal(
                    1,
                    await CountExecutionOutcomeAsync(
                        connection,
                        "AlreadyLinked",
                        differentKeyA,
                        differentKeyB));
                Assert.Equal(
                    2,
                    await CountCreatedLinkAuditsAsync(connection));
            }
            finally
            {
                foreach (var response in differentResponses)
                {
                    response.Dispose();
                }
            }
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task SameKeyDifferentActor_ReturnsConflictWithoutSecondEffect()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            await AssertHealthyAsync(factory);
            await GrantSystemAdministratorAsync(
                factory,
                TestUsers.CompanyB,
                TestCompanyIds.CompanyB);
            await GrantSystemAdministratorAsync(
                factory,
                TestUsers.CompanyB,
                TestCompanyIds.CompanyA);
            var party = await CreatePartyAsync(
                factory,
                "SQL Actor Mismatch Cari A.Ş.");
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();

            var key = $"cari-link-actor-mismatch-{Guid.NewGuid():N}";
            var firstCorrelation = $"cari-link-actor-first-{Guid.NewGuid():N}";
            using var firstClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(firstClient, key, firstCorrelation);
            var command = new CreateCompanyPartyAccount(PartyRole.Supplier);
            using var firstResponse = await firstClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{party.PartyId}/company-account",
                command);
            Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

            var secondCorrelation = $"cari-link-actor-second-{Guid.NewGuid():N}";
            using var secondClient = factory.CreateClientFor(
                TestUsers.CompanyB,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(secondClient, key, secondCorrelation);
            using var secondResponse = await secondClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{party.PartyId}/company-account",
                command);

            Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
            Assert.False(secondResponse.Headers.Contains("Idempotency-Replayed"));
            Assert.Equal(1, await CountTargetAccountsAsync(connection, party.PartyId));
            Assert.Equal(1, await CountExecutionsAsync(connection, key));
            Assert.Equal(1, await CountCreatedLinkAuditsAsync(connection));
            Assert.Equal(
                0,
                await CountAuditByCorrelationAsync(connection, secondCorrelation));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task HiddenPartyNotFound_IsDurablyReplayedWithoutDisclosureOrEffects()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            await AssertHealthyAsync(factory);
            var party = await CreatePartyAsync(
                factory,
                "SQL Durable NotFound Cari A.Ş.");
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();

            await ExecuteNonQueryAsync(
                connection,
                """
                DELETE FROM [identity].[RoleAssignments]
                WHERE [UserId] = '20000000-0000-0000-0000-000000000003'
                  AND [CompanyId] = @CompanyA;
                """,
                new SqlParameter("@CompanyA", TestCompanyIds.CompanyA));

            var key = $"cari-link-hidden-replay-{Guid.NewGuid():N}";
            var firstCorrelation = $"cari-link-hidden-first-{Guid.NewGuid():N}";
            using var targetClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(targetClient, key, firstCorrelation);
            var command = new CreateCompanyPartyAccount(PartyRole.Customer);
            using var firstResponse = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{party.PartyId}/company-account",
                command);

            Assert.Equal(HttpStatusCode.NotFound, firstResponse.StatusCode);
            Assert.False(firstResponse.Headers.Contains("Idempotency-Replayed"));

            var replayCorrelation = $"cari-link-hidden-replay-corr-{Guid.NewGuid():N}";
            SetRequestIdentity(targetClient, key, replayCorrelation);
            using var replayResponse = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{party.PartyId}/company-account",
                command);

            Assert.Equal(HttpStatusCode.NotFound, replayResponse.StatusCode);
            Assert.Equal(
                "true",
                Assert.Single(replayResponse.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                firstCorrelation,
                Assert.Single(replayResponse.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));
            Assert.Equal(0, await CountTargetAccountsAsync(connection, party.PartyId));
            Assert.Equal(1, await CountExecutionsAsync(connection, key));
            Assert.Equal(
                1,
                await CountExecutionOutcomeAsync(connection, "NotFound", key));
            Assert.Equal(0, await CountCreatedLinkAuditsAsync(connection));
            Assert.Equal(0, await CountAuditByCorrelationAsync(connection, firstCorrelation));
            Assert.Equal(0, await CountAuditByCorrelationAsync(connection, replayCorrelation));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task CompanyInputsCannotOverrideActiveContext_AndWrongScopeIsForbidden()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            await AssertHealthyAsync(factory);
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();

            var overrideParty = await CreatePartyAsync(
                factory,
                "SQL Company Override Cari A.Ş.");
            var overrideKey = $"cari-link-company-override-{Guid.NewGuid():N}";
            var overrideCorrelation = $"cari-link-company-override-corr-{Guid.NewGuid():N}";
            using var overrideClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(overrideClient, overrideKey, overrideCorrelation);
            using var overrideResponse = await overrideClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{overrideParty.PartyId}/company-account?companyId={TestCompanyIds.CompanyA}",
                new
                {
                    companyId = TestCompanyIds.CompanyA,
                    roles = PartyRole.Service,
                });

            Assert.Equal(HttpStatusCode.Created, overrideResponse.StatusCode);
            Assert.Equal(
                1,
                await CountCompanyAccountsAsync(
                    connection,
                    TestCompanyIds.CompanyA,
                    overrideParty.PartyId));
            Assert.Equal(
                1,
                await CountCompanyAccountsAsync(
                    connection,
                    TestCompanyIds.CompanyB,
                    overrideParty.PartyId));
            Assert.Equal(
                1,
                await CountAuditByCorrelationAsync(connection, overrideCorrelation));
            Assert.Equal(1, await CountExecutionsAsync(connection, overrideKey));

            var routeParty = await CreatePartyAsync(
                factory,
                "SQL Route Override Cari A.Ş.");
            var routeKey = $"cari-link-route-override-{Guid.NewGuid():N}";
            var routeCorrelation = $"cari-link-route-override-corr-{Guid.NewGuid():N}";
            using var routeClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(routeClient, routeKey, routeCorrelation);
            using var routeResponse = await routeClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{routeParty.PartyId}/company-account/{TestCompanyIds.CompanyA}",
                new CreateCompanyPartyAccount(PartyRole.Customer));

            Assert.Equal(HttpStatusCode.NotFound, routeResponse.StatusCode);
            Assert.Equal(0, await CountTargetAccountsAsync(connection, routeParty.PartyId));
            Assert.Equal(0, await CountAuditByCorrelationAsync(connection, routeCorrelation));
            Assert.Equal(0, await CountExecutionsAsync(connection, routeKey));

            await GrantSystemAdministratorAsync(
                factory,
                TestUsers.CompanyA,
                TestCompanyIds.CompanyA);
            var wrongScopeParty = await CreatePartyAsync(
                factory,
                "SQL Wrong Scope Cari A.Ş.");
            var wrongScopeKey = $"cari-link-wrong-scope-{Guid.NewGuid():N}";
            var wrongScopeCorrelation = $"cari-link-wrong-scope-corr-{Guid.NewGuid():N}";
            using var wrongScopeClient = factory.CreateClientFor(
                TestUsers.CompanyA,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(
                wrongScopeClient,
                wrongScopeKey,
                wrongScopeCorrelation);
            using var wrongScopeResponse = await wrongScopeClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{wrongScopeParty.PartyId}/company-account",
                new CreateCompanyPartyAccount(PartyRole.Customer));

            Assert.Equal(HttpStatusCode.Forbidden, wrongScopeResponse.StatusCode);
            Assert.Equal(
                0,
                await CountTargetAccountsAsync(connection, wrongScopeParty.PartyId));
            Assert.Equal(
                0,
                await CountAuditByCorrelationAsync(connection, wrongScopeCorrelation));
            Assert.Equal(0, await CountExecutionsAsync(connection, wrongScopeKey));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task GrantSystemAdministratorAsync(
        SqlServerRelationalApiFactory factory,
        string externalSubject,
        Guid companyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userId = await dbContext.ExternalIdentities
            .Where(identity => identity.Subject == externalSubject)
            .Select(identity => identity.UserId)
            .SingleAsync();
        var roleId = await dbContext.Roles
            .Where(role => role.Id == SystemAdministratorRoleId
                && role.Code == "SYSTEM_ADMINISTRATOR")
            .Select(role => role.Id)
            .SingleAsync();

        if (await dbContext.RoleAssignments.AnyAsync(assignment =>
                assignment.UserId == userId
                && assignment.RoleId == roleId
                && assignment.CompanyId == companyId))
        {
            return;
        }

        var roleAssignment = new RoleAssignment(
            Guid.NewGuid(),
            userId,
            roleId,
            companyId,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null,
            null);
        roleAssignment.AddScope(AccessScopeType.Company, companyId);
        dbContext.RoleAssignments.Add(roleAssignment);
        await dbContext.SaveChangesAsync();
    }

    private static async Task AssertHealthyAsync(SqlServerRelationalApiFactory factory)
    {
        using var healthClient = factory.CreateClient();
        using var healthResponse = await healthClient.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
    }

    private static async Task<CreatedParty> CreatePartyAsync(
        SqlServerRelationalApiFactory factory,
        string legalName)
    {
        using var sourceClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(
            sourceClient,
            $"cari-review-source-{Guid.NewGuid():N}",
            $"cari-review-source-corr-{Guid.NewGuid():N}");
        using var response = await sourceClient.PostAsJsonAsync(
            "/api/v1/counterparties",
            new CreateCounterparty(
                PartyKind.LegalEntity,
                legalName,
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

    private static Task<int> CountTargetAccountsAsync(
        SqlConnection connection,
        Guid partyId)
    {
        return CountCompanyAccountsAsync(
            connection,
            TestCompanyIds.CompanyB,
            partyId);
    }

    private static Task<int> CountCompanyAccountsAsync(
        SqlConnection connection,
        Guid companyId,
        Guid partyId)
    {
        return ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
            new SqlParameter("@CompanyId", companyId),
            new SqlParameter("@PartyId", partyId));
    }

    private static Task<int> CountCreatedLinkAuditsAsync(SqlConnection connection)
    {
        return ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyId AND [EventType] = N'counterparty.company-account.created.v1';",
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyB));
    }

    private static Task<int> CountAuditByCorrelationAsync(
        SqlConnection connection,
        string correlationId)
    {
        return ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
            new SqlParameter("@CorrelationId", correlationId));
    }

    private static Task<int> CountExecutionsAsync(
        SqlConnection connection,
        params string[] idempotencyKeys)
    {
        var hashes = idempotencyKeys.Select(IdempotencyHash.ComputeKey).ToArray();
        var parameters = hashes
            .Select((hash, index) => new SqlParameter($"@KeyHash{index}", hash))
            .ToArray();
        var names = parameters.Select(parameter => parameter.ParameterName);
        return ScalarAsync<int>(
            connection,
            $"SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = '{TestCompanyIds.CompanyB}' AND [CommandName] = N'counterparty.company-account.create.v1' AND [IdempotencyKeyHash] IN ({string.Join(", ", names)});",
            parameters);
    }

    private static Task<int> CountExecutionOutcomeAsync(
        SqlConnection connection,
        string outcome,
        params string[] idempotencyKeys)
    {
        var hashes = idempotencyKeys.Select(IdempotencyHash.ComputeKey).ToArray();
        var parameters = hashes
            .Select((hash, index) => new SqlParameter($"@KeyHash{index}", hash))
            .Append(new SqlParameter("@Outcome", outcome))
            .ToArray();
        var names = parameters
            .Where(parameter => parameter.ParameterName != "@Outcome")
            .Select(parameter => parameter.ParameterName);
        return ScalarAsync<int>(
            connection,
            $"SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = '{TestCompanyIds.CompanyB}' AND [CommandName] = N'counterparty.company-account.create.v1' AND [IdempotencyKeyHash] IN ({string.Join(", ", names)}) AND [Outcome] = @Outcome;",
            parameters);
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

    private sealed record CreatedParty(Guid PartyId);
}
