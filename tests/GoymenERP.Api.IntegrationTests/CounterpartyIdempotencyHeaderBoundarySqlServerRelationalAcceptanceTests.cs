using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyIdempotencyHeaderBoundarySqlServerRelationalAcceptanceTests
{
    public static IEnumerable<object[]> InvalidHeaderCases()
    {
        yield return ["missing", Array.Empty<string>()];
        yield return ["empty", new[] { string.Empty }];
        yield return ["whitespace", new[] { "   " }];
        yield return ["over-limit", new[] { new string('x', 129) }];
        yield return ["multiple-distinct", new[] { "first-key", "second-key" }];
        yield return ["multiple-duplicate", new[] { "duplicate-key", "duplicate-key" }];
        yield return ["comma-joined", new[] { "first-key,second-key" }];
        yield return ["comma-space-joined", new[] { "first-key, second-key" }];
    }

    [CounterpartySqlServerTheory]
    [MemberData(nameof(InvalidHeaderCases))]
    public async Task InvalidIdempotencyHeader_IsRejectedBeforeCreateOrStatusPersistence(
        string caseName,
        string[] idempotencyHeaderValues)
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using var healthClient = factory.CreateClient();
            using var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

            using var setupClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            using var setupResponse = await SendAsync(
                setupClient,
                HttpMethod.Post,
                "/api/v1/counterparties",
                new CreateCounterparty(
                    PartyKind.LegalEntity,
                    $"SQL Header Sınır Setup {caseName} Cari A.Ş.",
                    null,
                    PartyRole.Customer),
                $"cari-header-setup-correlation-{caseName}-{Guid.NewGuid():N}",
                $"cari-header-setup-key-{caseName}-{Guid.NewGuid():N}");
            Assert.Equal(HttpStatusCode.Created, setupResponse.StatusCode);
            using var setupJson = JsonDocument.Parse(
                await setupResponse.Content.ReadAsStringAsync());
            var partyId = setupJson.RootElement.GetProperty("id").GetGuid();

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            var baseline = await ReadSnapshotAsync(connection, partyId);
            Assert.Equal(1L, baseline.AccountVersion);
            Assert.True(baseline.IsActive);

            using var createClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            using var createResponse = await SendAsync(
                createClient,
                HttpMethod.Post,
                "/api/v1/counterparties",
                new CreateCounterparty(
                    PartyKind.LegalEntity,
                    $"SQL Geçersiz Header {caseName} Cari A.Ş.",
                    null,
                    PartyRole.Supplier),
                $"cari-header-create-{caseName}-{Guid.NewGuid():N}",
                idempotencyHeaderValues);
            Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
            Assert.False(createResponse.Headers.Contains("Idempotency-Replayed"));
            Assert.False(createResponse.Headers.Contains(
                "Idempotency-Original-Correlation-Id"));

            using var statusClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            using var statusResponse = await SendAsync(
                statusClient,
                HttpMethod.Put,
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                new SetCompanyPartyAccountActiveState(
                    false,
                    baseline.AccountVersion,
                    "Geçersiz idempotency header sınır doğrulaması"),
                $"cari-header-status-{caseName}-{Guid.NewGuid():N}",
                idempotencyHeaderValues);
            Assert.Equal(HttpStatusCode.BadRequest, statusResponse.StatusCode);
            Assert.False(statusResponse.Headers.Contains("Idempotency-Replayed"));
            Assert.False(statusResponse.Headers.Contains(
                "Idempotency-Original-Correlation-Id"));

            Assert.Equal(baseline, await ReadSnapshotAsync(connection, partyId));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task ExactMaximumLengthPaddedKey_IsAcceptedAndTrimmedReplayAddsNoEffects()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using var healthClient = factory.CreateClient();
            using var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

            var canonicalKey = new string('k', 128);
            var originalCorrelation = $"cari-max-key-original-{Guid.NewGuid():N}";
            var command = new CreateCounterparty(
                PartyKind.LegalEntity,
                "SQL 128 Karakter Idempotency Cari A.Ş.",
                null,
                PartyRole.Customer);

            using var createClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            using var firstResponse = await SendAsync(
                createClient,
                HttpMethod.Post,
                "/api/v1/counterparties",
                command,
                originalCorrelation,
                $"  {canonicalKey}  ");
            Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
            Assert.False(firstResponse.Headers.Contains("Idempotency-Replayed"));
            using var firstJson = JsonDocument.Parse(
                await firstResponse.Content.ReadAsStringAsync());
            var partyId = firstJson.RootElement.GetProperty("id").GetGuid();

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            var baseline = await ReadSnapshotAsync(connection, partyId);

            using var replayClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            using var replayResponse = await SendAsync(
                replayClient,
                HttpMethod.Post,
                "/api/v1/counterparties",
                command,
                $"cari-max-key-replay-{Guid.NewGuid():N}",
                canonicalKey);
            Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
            Assert.Equal(
                "true",
                Assert.Single(replayResponse.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                originalCorrelation,
                Assert.Single(replayResponse.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));
            using var replayJson = JsonDocument.Parse(
                await replayResponse.Content.ReadAsStringAsync());
            Assert.Equal(partyId, replayJson.RootElement.GetProperty("id").GetGuid());

            Assert.Equal(baseline, await ReadSnapshotAsync(connection, partyId));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        object command,
        string correlationId,
        params string[] idempotencyHeaderValues)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");

        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = JsonContent.Create(command),
        };
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        if (idempotencyHeaderValues.Length > 0)
        {
            request.Headers.TryAddWithoutValidation(
                "Idempotency-Key",
                idempotencyHeaderValues);
        }

        return await client.SendAsync(request);
    }

    private static async Task<PersistenceSnapshot> ReadSnapshotAsync(
        SqlConnection connection,
        Guid partyId)
    {
        return new PersistenceSnapshot(
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [counterparty].[Parties];"),
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts];"),
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents];"),
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions];"),
            await ScalarAsync<long>(
                connection,
                "SELECT [Version] FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                new SqlParameter("@PartyId", partyId)),
            await ScalarAsync<bool>(
                connection,
                "SELECT CONVERT(bit, [IsActive]) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                new SqlParameter("@PartyId", partyId)));
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

    private sealed record PersistenceSnapshot(
        int PartyCount,
        int AccountCount,
        int AuditCount,
        int ExecutionCount,
        long AccountVersion,
        bool IsActive);
}

public sealed class CounterpartySqlServerTheoryAttribute : TheoryAttribute
{
    public CounterpartySqlServerTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)))
        {
            Skip = $"{SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable} tanımlı değil.";
        }
    }
}
