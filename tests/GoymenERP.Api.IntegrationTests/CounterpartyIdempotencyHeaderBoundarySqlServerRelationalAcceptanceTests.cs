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
    [SqlServerFact]
    public async Task InvalidIdempotencyHeaders_AreRejectedBeforeCreateOrStatusPersistence()
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
            var setupKey = $"cari-header-setup-{Guid.NewGuid():N}";
            var setupCorrelation = $"cari-header-setup-correlation-{Guid.NewGuid():N}";
            using var setupResponse = await SendAsync(
                setupClient,
                HttpMethod.Post,
                "/api/v1/counterparties",
                new CreateCounterparty(
                    PartyKind.LegalEntity,
                    "SQL Idempotency Header Sınır Cari A.Ş.",
                    null,
                    PartyRole.Customer),
                setupCorrelation,
                setupKey);
            Assert.Equal(HttpStatusCode.Created, setupResponse.StatusCode);
            using var setupJson = JsonDocument.Parse(
                await setupResponse.Content.ReadAsStringAsync());
            var partyId = setupJson.RootElement.GetProperty("id").GetGuid();

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            var baseline = await ReadSnapshotAsync(connection, partyId);
            Assert.Equal(1L, baseline.AccountVersion);
            Assert.True(baseline.IsActive);

            var invalidHeaders = new[]
            {
                HeaderCase.Missing("missing"),
                HeaderCase.WithValues("empty", string.Empty),
                HeaderCase.WithValues("whitespace", "   "),
                HeaderCase.WithValues("over-limit", new string('x', 129)),
                HeaderCase.WithValues("multiple-distinct", "first-key", "second-key"),
                HeaderCase.WithValues("multiple-duplicate", "duplicate-key", "duplicate-key"),
                HeaderCase.WithValues("comma-joined", "first-key,second-key"),
                HeaderCase.WithValues("comma-space-joined", "first-key, second-key"),
            };

            foreach (var headerCase in invalidHeaders)
            {
                using var createClient = factory.CreateClientFor(
                    TestUsers.GroupAdmin,
                    TestCompanyIds.CompanyA);
                using var createResponse = await SendAsync(
                    createClient,
                    HttpMethod.Post,
                    "/api/v1/counterparties",
                    new CreateCounterparty(
                        PartyKind.LegalEntity,
                        $"SQL Geçersiz Header {headerCase.Name} Cari A.Ş.",
                        null,
                        PartyRole.Supplier),
                    $"cari-header-create-{headerCase.Name}-{Guid.NewGuid():N}",
                    headerCase.Values);
                Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);

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
                    $"cari-header-status-{headerCase.Name}-{Guid.NewGuid():N}",
                    headerCase.Values);
                Assert.Equal(HttpStatusCode.BadRequest, statusResponse.StatusCode);
            }

            var afterInvalidRequests = await ReadSnapshotAsync(connection, partyId);
            Assert.Equal(baseline, afterInvalidRequests);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task PaddedSingleIdempotencyKey_IsTrimmedAndReplaysWithoutNewEffects()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using var healthClient = factory.CreateClient();
            using var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

            var canonicalKey = $"cari-padded-key-{Guid.NewGuid():N}";
            var originalCorrelation = $"cari-padded-original-{Guid.NewGuid():N}";
            var command = new CreateCounterparty(
                PartyKind.LegalEntity,
                "SQL Padded Idempotency Cari A.Ş.",
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
                $"cari-padded-replay-{Guid.NewGuid():N}",
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

            var afterReplay = await ReadSnapshotAsync(connection, partyId);
            Assert.Equal(baseline, afterReplay);
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
        // SqlServerRelationalApiFactory.CreateClientFor adds a valid default key.
        // Remove it so each boundary case controls the exact raw header set.
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
        var partyCount = await ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM [counterparty].[Parties];");
        var accountCount = await ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts];");
        var auditCount = await ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM [audit].[AuditEvents];");
        var executionCount = await ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions];");
        var accountVersion = await ScalarAsync<long>(
            connection,
            "SELECT [Version] FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
            new SqlParameter("@PartyId", partyId));
        var isActive = await ScalarAsync<bool>(
            connection,
            "SELECT CONVERT(bit, [IsActive]) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
            new SqlParameter("@PartyId", partyId));

        return new PersistenceSnapshot(
            partyCount,
            accountCount,
            auditCount,
            executionCount,
            accountVersion,
            isActive);
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

    private sealed record HeaderCase(string Name, string[] Values)
    {
        public static HeaderCase Missing(string name) => new(name, []);

        public static HeaderCase WithValues(string name, params string[] values) =>
            new(name, values);
    }

    private sealed record PersistenceSnapshot(
        int PartyCount,
        int AccountCount,
        int AuditCount,
        int ExecutionCount,
        long AccountVersion,
        bool IsActive);
}
