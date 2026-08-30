using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyStableProblemCodesSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task ExistingErrorProducers_ExposeStableCodes_AndPreserveEffects()
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
            var baseline = await ReadCountsAsync(connection);

            using var invalidCreateClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            SetRequestIdentity(
                invalidCreateClient,
                $"cari-code-invalid-create-{Guid.NewGuid():N}",
                $"cari-code-invalid-create-corr-{Guid.NewGuid():N}");
            using var invalidCreate = await invalidCreateClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                new CreateCounterparty(
                    PartyKind.LegalEntity,
                    "   ",
                    null,
                    PartyRole.Customer));
            using (await AssertProblemAsync(
                       invalidCreate,
                       HttpStatusCode.BadRequest,
                       CounterpartyProblemCodesV1.ValidationFailed))
            {
            }
            Assert.Equal(baseline, await ReadCountsAsync(connection));

            using var invalidCompanyClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            invalidCompanyClient.DefaultRequestHeaders.Remove("X-Company-Id");
            invalidCompanyClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-Company-Id",
                "not-a-company-guid");
            using var invalidCompany = await invalidCompanyClient.GetAsync(
                "/api/v1/counterparties");
            using (await AssertProblemAsync(
                       invalidCompany,
                       HttpStatusCode.BadRequest,
                       CounterpartyProblemCodesV1.ValidationFailed))
            {
            }
            Assert.Equal(baseline, await ReadCountsAsync(connection));

            using var missingKeyClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            SetCorrelation(
                missingKeyClient,
                $"cari-code-missing-key-corr-{Guid.NewGuid():N}");
            using var missingKey = await missingKeyClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                new CreateCounterparty(
                    PartyKind.LegalEntity,
                    "SQL Eksik Command Metadata Cari A.Ş.",
                    null,
                    PartyRole.Customer));
            using (await AssertProblemAsync(
                       missingKey,
                       HttpStatusCode.BadRequest,
                       CounterpartyProblemCodesV1.CommandMetadataInvalid))
            {
            }
            Assert.Equal(baseline, await ReadCountsAsync(connection));

            using var ambiguousKeyClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            SetRequestIdentity(
                ambiguousKeyClient,
                "first-key,second-key",
                $"cari-code-ambiguous-key-corr-{Guid.NewGuid():N}");
            using var ambiguousKey = await ambiguousKeyClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                new CreateCounterparty(
                    PartyKind.LegalEntity,
                    "SQL Belirsiz Command Metadata Cari A.Ş.",
                    null,
                    PartyRole.Supplier));
            using (await AssertProblemAsync(
                       ambiguousKey,
                       HttpStatusCode.BadRequest,
                       CounterpartyProblemCodesV1.CommandMetadataInvalid))
            {
            }
            Assert.Equal(baseline, await ReadCountsAsync(connection));

            using var commandClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            var createKey = $"cari-code-create-{Guid.NewGuid():N}";
            var createCorrelation = $"cari-code-create-corr-{Guid.NewGuid():N}";
            SetRequestIdentity(commandClient, createKey, createCorrelation);
            using var created = await commandClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                new CreateCounterparty(
                    PartyKind.LegalEntity,
                    "SQL Stable Problem Code Cari A.Ş.",
                    null,
                    PartyRole.Customer));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var createdJson = JsonDocument.Parse(
                await created.Content.ReadAsStringAsync());
            var partyId = createdJson.RootElement.GetProperty("id").GetGuid();

            var afterCreate = await ReadCountsAsync(connection);
            Assert.Equal(baseline.PartyCount + 1, afterCreate.PartyCount);
            Assert.Equal(baseline.AccountCount + 1, afterCreate.AccountCount);
            Assert.Equal(baseline.AuditCount + 1, afterCreate.AuditCount);
            Assert.Equal(baseline.ExecutionCount + 1, afterCreate.ExecutionCount);

            using var directMissing = await commandClient.GetAsync(
                $"/api/v1/counterparties/{Guid.NewGuid():D}");
            using (await AssertProblemAsync(
                       directMissing,
                       HttpStatusCode.NotFound,
                       CounterpartyProblemCodesV1.NotFound))
            {
            }
            Assert.Equal(afterCreate, await ReadCountsAsync(connection));

            var deactivateKey = $"cari-code-deactivate-{Guid.NewGuid():N}";
            SetRequestIdentity(
                commandClient,
                deactivateKey,
                $"cari-code-deactivate-corr-{Guid.NewGuid():N}");
            using var deactivated = await commandClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                new SetCompanyPartyAccountActiveState(
                    false,
                    1,
                    "Stable code relational setup"));
            Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);

            var afterDeactivate = await ReadCountsAsync(connection);
            Assert.Equal(afterCreate.PartyCount, afterDeactivate.PartyCount);
            Assert.Equal(afterCreate.AccountCount, afterDeactivate.AccountCount);
            Assert.Equal(afterCreate.AuditCount + 1, afterDeactivate.AuditCount);
            Assert.Equal(afterCreate.ExecutionCount + 1, afterDeactivate.ExecutionCount);
            Assert.Equal(
                2L,
                await ReadAccountVersionAsync(connection, partyId));

            var staleKey = $"cari-code-version-conflict-{Guid.NewGuid():N}";
            var staleCorrelation = $"cari-code-version-conflict-corr-{Guid.NewGuid():N}";
            var staleCommand = new SetCompanyPartyAccountActiveState(
                true,
                1,
                "Stable version conflict code");
            SetRequestIdentity(commandClient, staleKey, staleCorrelation);
            using var stale = await commandClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                staleCommand);
            using (var staleJson = await AssertProblemAsync(
                       stale,
                       HttpStatusCode.Conflict,
                       CounterpartyProblemCodesV1.VersionConflict))
            {
                var conflict = staleJson.RootElement.GetProperty("conflict");
                Assert.Equal(1L, conflict.GetProperty("expectedVersion").GetInt64());
                Assert.Equal(2L, conflict.GetProperty("currentVersion").GetInt64());
            }

            var afterStale = await ReadCountsAsync(connection);
            Assert.Equal(afterDeactivate.AuditCount, afterStale.AuditCount);
            Assert.Equal(afterDeactivate.ExecutionCount + 1, afterStale.ExecutionCount);
            Assert.Equal(2L, await ReadAccountVersionAsync(connection, partyId));

            SetRequestIdentity(
                commandClient,
                staleKey,
                $"cari-code-version-conflict-replay-{Guid.NewGuid():N}");
            using var staleReplay = await commandClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                staleCommand);
            using (var staleReplayJson = await AssertProblemAsync(
                       staleReplay,
                       HttpStatusCode.Conflict,
                       CounterpartyProblemCodesV1.VersionConflict))
            {
                var conflict = staleReplayJson.RootElement.GetProperty("conflict");
                Assert.Equal(1L, conflict.GetProperty("expectedVersion").GetInt64());
                Assert.Equal(2L, conflict.GetProperty("currentVersion").GetInt64());
            }
            AssertReplayHeaders(staleReplay, staleCorrelation);
            Assert.Equal(afterStale, await ReadCountsAsync(connection));

            var missingPartyId = Guid.NewGuid();
            var notFoundKey = $"cari-code-not-found-{Guid.NewGuid():N}";
            var notFoundCorrelation = $"cari-code-not-found-corr-{Guid.NewGuid():N}";
            var missingCommand = new SetCompanyPartyAccountActiveState(
                false,
                1,
                "Stable non-disclosing not-found code");
            SetRequestIdentity(commandClient, notFoundKey, notFoundCorrelation);
            using var notFound = await commandClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{missingPartyId}/company-account/active-state",
                missingCommand);
            string notFoundBody;
            using (var notFoundJson = await AssertProblemAsync(
                       notFound,
                       HttpStatusCode.NotFound,
                       CounterpartyProblemCodesV1.NotFound))
            {
                notFoundBody = notFoundJson.RootElement.GetRawText();
            }
            Assert.DoesNotContain(
                missingPartyId.ToString("D"),
                notFoundBody,
                StringComparison.OrdinalIgnoreCase);

            var afterNotFound = await ReadCountsAsync(connection);
            Assert.Equal(afterStale.AuditCount, afterNotFound.AuditCount);
            Assert.Equal(afterStale.ExecutionCount + 1, afterNotFound.ExecutionCount);

            SetRequestIdentity(
                commandClient,
                notFoundKey,
                $"cari-code-not-found-replay-{Guid.NewGuid():N}");
            using var notFoundReplay = await commandClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{missingPartyId}/company-account/active-state",
                missingCommand);
            using (await AssertProblemAsync(
                       notFoundReplay,
                       HttpStatusCode.NotFound,
                       CounterpartyProblemCodesV1.NotFound))
            {
            }
            AssertReplayHeaders(notFoundReplay, notFoundCorrelation);
            Assert.Equal(afterNotFound, await ReadCountsAsync(connection));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task<JsonDocument> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            expectedCode,
            document.RootElement.GetProperty("code").GetString());
        Assert.Equal((int)expectedStatus, document.RootElement.GetProperty("status").GetInt32());
        return document;
    }

    private static void AssertReplayHeaders(
        HttpResponseMessage response,
        string expectedOriginalCorrelation)
    {
        Assert.Equal(
            "true",
            Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(
            expectedOriginalCorrelation,
            Assert.Single(response.Headers.GetValues(
                "Idempotency-Original-Correlation-Id")));
    }

    private static void SetRequestIdentity(
        HttpClient client,
        string idempotencyKey,
        string correlationId)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Idempotency-Key",
            idempotencyKey);
        SetCorrelation(client, correlationId);
    }

    private static void SetCorrelation(HttpClient client, string correlationId)
    {
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Correlation-Id",
            correlationId);
    }

    private static async Task<DatabaseCounts> ReadCountsAsync(SqlConnection connection)
    {
        return new DatabaseCounts(
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
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions];"));
    }

    private static Task<long> ReadAccountVersionAsync(
        SqlConnection connection,
        Guid partyId)
    {
        return ScalarAsync<long>(
            connection,
            """
            SELECT [Version]
            FROM [counterparty].[CompanyPartyAccounts]
            WHERE [CompanyId] = @CompanyId
              AND [PartyId] = @PartyId;
            """,
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
            new SqlParameter("@PartyId", partyId));
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

    private sealed record DatabaseCounts(
        int PartyCount,
        int AccountCount,
        int AuditCount,
        int ExecutionCount);
}
