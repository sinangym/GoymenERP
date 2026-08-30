using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyCompanyLinkProblemCodeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task ConflictResponses_ExposeStableCodes_OnInitialReplayAndRacePaths()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            await AssertHealthyAsync(factory);
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();

            var party = await CreatePartyAsync(
                factory,
                "SQL Cari Problem Code Sözleşmesi A.Ş.");
            using var targetClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            var command = new CreateCompanyPartyAccount(PartyRole.Customer);

            var createdKey = $"cari-link-code-created-{Guid.NewGuid():N}";
            var createdCorrelation = $"cari-link-code-created-corr-{Guid.NewGuid():N}";
            SetRequestIdentity(targetClient, createdKey, createdCorrelation);
            using var created = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{party.PartyId}/company-account",
                command);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var alreadyLinkedKey = $"cari-link-code-already-{Guid.NewGuid():N}";
            var alreadyLinkedCorrelation = $"cari-link-code-already-corr-{Guid.NewGuid():N}";
            SetRequestIdentity(targetClient, alreadyLinkedKey, alreadyLinkedCorrelation);
            using var alreadyLinked = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{party.PartyId}/company-account",
                command);
            await AssertProblemCodeAsync(
                alreadyLinked,
                CounterpartyProblemCodesV1.AlreadyLinked);
            Assert.False(alreadyLinked.Headers.Contains("Idempotency-Replayed"));

            var alreadyLinkedReplayCorrelation =
                $"cari-link-code-already-replay-corr-{Guid.NewGuid():N}";
            SetRequestIdentity(
                targetClient,
                alreadyLinkedKey,
                alreadyLinkedReplayCorrelation);
            using var alreadyLinkedReplay = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{party.PartyId}/company-account",
                command);
            await AssertProblemCodeAsync(
                alreadyLinkedReplay,
                CounterpartyProblemCodesV1.AlreadyLinked);
            Assert.Equal(
                "true",
                Assert.Single(alreadyLinkedReplay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                alreadyLinkedCorrelation,
                Assert.Single(alreadyLinkedReplay.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));

            var idempotencyConflictCorrelation =
                $"cari-link-code-idem-conflict-corr-{Guid.NewGuid():N}";
            SetRequestIdentity(
                targetClient,
                createdKey,
                idempotencyConflictCorrelation);
            using var idempotencyConflict = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{party.PartyId}/company-account",
                new CreateCompanyPartyAccount(PartyRole.Supplier));
            await AssertProblemCodeAsync(
                idempotencyConflict,
                CounterpartyProblemCodesV1.IdempotencyConflict);
            Assert.False(idempotencyConflict.Headers.Contains("Idempotency-Replayed"));

            Assert.Equal(
                1,
                await CountTargetAccountsAsync(connection, party.PartyId));
            Assert.Equal(
                1,
                await CountCreatedAuditByCorrelationsAsync(
                    connection,
                    createdCorrelation,
                    alreadyLinkedCorrelation,
                    alreadyLinkedReplayCorrelation,
                    idempotencyConflictCorrelation));
            Assert.Equal(
                1,
                await CountExecutionAsync(
                    connection,
                    createdKey,
                    "Created"));
            Assert.Equal(
                1,
                await CountExecutionAsync(
                    connection,
                    alreadyLinkedKey,
                    "AlreadyLinked"));

            var raceParty = await CreatePartyAsync(
                factory,
                "SQL Cari Problem Code Yarış A.Ş.");
            var raceKeyA = $"cari-link-code-race-a-{Guid.NewGuid():N}";
            var raceKeyB = $"cari-link-code-race-b-{Guid.NewGuid():N}";
            var raceCorrelationA = $"cari-link-code-race-corr-a-{Guid.NewGuid():N}";
            var raceCorrelationB = $"cari-link-code-race-corr-b-{Guid.NewGuid():N}";
            using var raceClientA = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            using var raceClientB = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(raceClientA, raceKeyA, raceCorrelationA);
            SetRequestIdentity(raceClientB, raceKeyB, raceCorrelationB);

            var raceResponses = await Task.WhenAll(
                raceClientA.PostAsJsonAsync(
                    $"/api/v1/counterparties/{raceParty.PartyId}/company-account",
                    command),
                raceClientB.PostAsJsonAsync(
                    $"/api/v1/counterparties/{raceParty.PartyId}/company-account",
                    command));
            try
            {
                Assert.Single(
                    raceResponses,
                    response => response.StatusCode == HttpStatusCode.Created);
                var raceConflict = Assert.Single(
                    raceResponses,
                    response => response.StatusCode == HttpStatusCode.Conflict);
                await AssertProblemCodeAsync(
                    raceConflict,
                    CounterpartyProblemCodesV1.AlreadyLinked);
                Assert.DoesNotContain(
                    raceResponses,
                    response => response.StatusCode == HttpStatusCode.InternalServerError);

                Assert.Equal(
                    1,
                    await CountTargetAccountsAsync(connection, raceParty.PartyId));
                Assert.Equal(
                    1,
                    await CountCreatedAuditByCorrelationsAsync(
                        connection,
                        raceCorrelationA,
                        raceCorrelationB));
                Assert.Equal(
                    2,
                    await CountExecutionsAsync(connection, raceKeyA, raceKeyB));
                Assert.Equal(
                    1,
                    await CountExecutionOutcomeAsync(
                        connection,
                        "Created",
                        raceKeyA,
                        raceKeyB));
                Assert.Equal(
                    1,
                    await CountExecutionOutcomeAsync(
                        connection,
                        "AlreadyLinked",
                        raceKeyA,
                        raceKeyB));
            }
            finally
            {
                foreach (var response in raceResponses)
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
    public async Task GloballyActiveParty_WithInactiveSourceAccount_CanBeLinkedToTargetCompany()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            await AssertHealthyAsync(factory);
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();

            var party = await CreatePartyAsync(
                factory,
                "SQL Pasif Kaynak Hesaptan Yeni Şirket Bağlama A.Ş.");
            Assert.Equal(
                1,
                await ExecuteNonQueryAsync(
                    connection,
                    """
                    UPDATE [counterparty].[CompanyPartyAccounts]
                    SET [IsActive] = 0,
                        [Version] = [Version] + 1,
                        [UpdatedAtUtc] = SYSUTCDATETIME()
                    WHERE [CompanyId] = @CompanyA
                      AND [PartyId] = @PartyId
                      AND [IsActive] = 1;
                    """,
                    new SqlParameter("@CompanyA", TestCompanyIds.CompanyA),
                    new SqlParameter("@PartyId", party.PartyId)));

            var key = $"cari-link-inactive-source-{Guid.NewGuid():N}";
            var correlation = $"cari-link-inactive-source-corr-{Guid.NewGuid():N}";
            using var targetClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(targetClient, key, correlation);
            using var response = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{party.PartyId}/company-account",
                new CreateCompanyPartyAccount(PartyRole.Supplier));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COUNT(*)
                    FROM [counterparty].[Parties]
                    WHERE [Id] = @PartyId
                      AND [IsActive] = 1;
                    """,
                    new SqlParameter("@PartyId", party.PartyId)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COUNT(*)
                    FROM [counterparty].[CompanyPartyAccounts]
                    WHERE [CompanyId] = @CompanyA
                      AND [PartyId] = @PartyId
                      AND [IsActive] = 0;
                    """,
                    new SqlParameter("@CompanyA", TestCompanyIds.CompanyA),
                    new SqlParameter("@PartyId", party.PartyId)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COUNT(*)
                    FROM [counterparty].[CompanyPartyAccounts]
                    WHERE [CompanyId] = @CompanyB
                      AND [PartyId] = @PartyId
                      AND [IsActive] = 1;
                    """,
                    new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                    new SqlParameter("@PartyId", party.PartyId)));
            Assert.Equal(
                1,
                await CountCreatedAuditByCorrelationsAsync(connection, correlation));
            Assert.Equal(
                1,
                await CountExecutionAsync(connection, key, "Created"));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
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
            $"cari-link-code-source-{Guid.NewGuid():N}",
            $"cari-link-code-source-corr-{Guid.NewGuid():N}");
        using var response = await sourceClient.PostAsJsonAsync(
            "/api/v1/counterparties",
            new CreateCounterparty(
                PartyKind.LegalEntity,
                legalName,
                null,
                PartyRole.Customer));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new CreatedParty(
            document.RootElement.GetProperty("id").GetGuid(),
            document.RootElement.GetProperty("code").GetString()!);
    }

    private static async Task AssertProblemCodeAsync(
        HttpResponseMessage response,
        string expectedCode)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            expectedCode,
            document.RootElement.GetProperty("code").GetString());
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
        return ScalarAsync<int>(
            connection,
            """
            SELECT COUNT(*)
            FROM [counterparty].[CompanyPartyAccounts]
            WHERE [CompanyId] = @CompanyB
              AND [PartyId] = @PartyId;
            """,
            new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
            new SqlParameter("@PartyId", partyId));
    }

    private static Task<int> CountCreatedAuditByCorrelationsAsync(
        SqlConnection connection,
        params string[] correlations)
    {
        if (correlations.Length == 0)
        {
            throw new ArgumentException(
                "En az bir correlation kimliği zorunludur.",
                nameof(correlations));
        }

        var parameterNames = new string[correlations.Length];
        var parameters = new SqlParameter[correlations.Length + 1];
        parameters[0] = new SqlParameter("@CompanyB", TestCompanyIds.CompanyB);
        for (var index = 0; index < correlations.Length; index++)
        {
            parameterNames[index] = $"@Correlation{index}";
            parameters[index + 1] = new SqlParameter(
                parameterNames[index],
                correlations[index]);
        }

        return ScalarAsync<int>(
            connection,
            $"""
            SELECT COUNT(*)
            FROM [audit].[AuditEvents]
            WHERE [CompanyId] = @CompanyB
              AND [EventType] = N'counterparty.company-account.created.v1'
              AND [CorrelationId] IN ({string.Join(", ", parameterNames)});
            """,
            parameters);
    }

    private static Task<int> CountExecutionAsync(
        SqlConnection connection,
        string key,
        string outcome)
    {
        return ScalarAsync<int>(
            connection,
            """
            SELECT COUNT(*)
            FROM [idempotency].[CommandExecutions]
            WHERE [CompanyId] = @CompanyB
              AND [CommandName] = N'counterparty.company-account.create.v1'
              AND [IdempotencyKeyHash] = @KeyHash
              AND [Outcome] = @Outcome;
            """,
            new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
            new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(key)),
            new SqlParameter("@Outcome", outcome));
    }

    private static Task<int> CountExecutionsAsync(
        SqlConnection connection,
        string keyA,
        string keyB)
    {
        return ScalarAsync<int>(
            connection,
            """
            SELECT COUNT(*)
            FROM [idempotency].[CommandExecutions]
            WHERE [CompanyId] = @CompanyB
              AND [CommandName] = N'counterparty.company-account.create.v1'
              AND [IdempotencyKeyHash] IN (@KeyHashA, @KeyHashB);
            """,
            new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
            new SqlParameter("@KeyHashA", IdempotencyHash.ComputeKey(keyA)),
            new SqlParameter("@KeyHashB", IdempotencyHash.ComputeKey(keyB)));
    }

    private static Task<int> CountExecutionOutcomeAsync(
        SqlConnection connection,
        string outcome,
        string keyA,
        string keyB)
    {
        return ScalarAsync<int>(
            connection,
            """
            SELECT COUNT(*)
            FROM [idempotency].[CommandExecutions]
            WHERE [CompanyId] = @CompanyB
              AND [CommandName] = N'counterparty.company-account.create.v1'
              AND [IdempotencyKeyHash] IN (@KeyHashA, @KeyHashB)
              AND [Outcome] = @Outcome;
            """,
            new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
            new SqlParameter("@KeyHashA", IdempotencyHash.ComputeKey(keyA)),
            new SqlParameter("@KeyHashB", IdempotencyHash.ComputeKey(keyB)),
            new SqlParameter("@Outcome", outcome));
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

    private static async Task<int> ExecuteNonQueryAsync(
        SqlConnection connection,
        string commandText,
        params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
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

    private sealed record CreatedParty(Guid PartyId, string Code);
}
