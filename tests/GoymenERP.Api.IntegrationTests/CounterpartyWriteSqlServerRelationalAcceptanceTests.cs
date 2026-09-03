using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyWriteSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task WriteIdempotencyAndTerminalReplay_AreDurable()
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

            var emptyBaseline = await ReadEffectCountsAsync(connection);
            Assert.Equal(new PersistenceCounts(0, 0, 0, 0), emptyBaseline);

            using (var invalidCreateClient = factory.CreateClientFor(
                       TestUsers.GroupAdmin,
                       TestCompanyIds.CompanyA))
            {
                SetRequestIdentity(
                    invalidCreateClient,
                    $"cari-validation-{Guid.NewGuid():N}",
                    $"cari-validation-corr-{Guid.NewGuid():N}");
                using var invalidCreate = await invalidCreateClient.PostAsJsonAsync(
                    "/api/v1/counterparties",
                    new CreateCounterparty(
                        PartyKind.LegalEntity,
                        "   ",
                        null,
                        PartyRole.Customer));
                await AssertProblemCodeAsync(
                    invalidCreate,
                    HttpStatusCode.BadRequest,
                    CounterpartyProblemCodesV1.ValidationFailed);
            }

            using (var invalidOptionalCompanyClient = factory.CreateClient())
            {
                invalidOptionalCompanyClient.DefaultRequestHeaders.Add(
                    "X-Development-User",
                    TestUsers.GroupAdmin);
                invalidOptionalCompanyClient.DefaultRequestHeaders.Add(
                    "X-Correlation-Id",
                    $"cari-optional-company-corr-{Guid.NewGuid():N}");
                invalidOptionalCompanyClient.DefaultRequestHeaders.Add(
                    "X-Company-Id",
                    "not-a-company-guid");
                using var invalidCompany = await invalidOptionalCompanyClient.GetAsync(
                    "/api/v1/counterparties");
                await AssertProblemCodeAsync(
                    invalidCompany,
                    HttpStatusCode.BadRequest,
                    CounterpartyProblemCodesV1.ValidationFailed);
            }

            Assert.Equal(emptyBaseline, await ReadEffectCountsAsync(connection));

            var createKey = $"cari-create-{Guid.NewGuid():N}";
            var createCorrelation = $"cari-create-first-{Guid.NewGuid():N}";
            using var createClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            SetRequestIdentity(createClient, createKey, createCorrelation);
            var createCommand = new CreateCounterparty(
                PartyKind.LegalEntity,
                "SQL Replay Cari A.Ş.",
                "SQL Replay",
                PartyRole.Customer | PartyRole.Supplier);

            using var firstCreate = await createClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                createCommand);
            Assert.Equal(HttpStatusCode.Created, firstCreate.StatusCode);
            Assert.False(firstCreate.Headers.Contains("Idempotency-Replayed"));
            var firstCreateBody = await firstCreate.Content.ReadAsStringAsync();
            using var firstCreateJson = JsonDocument.Parse(firstCreateBody);
            var partyId = firstCreateJson.RootElement.GetProperty("id").GetGuid();
            var partyCode = firstCreateJson.RootElement.GetProperty("code").GetString();
            Assert.False(string.IsNullOrWhiteSpace(partyCode));
            Assert.Equal(
                JsonValueKind.String,
                firstCreateJson.RootElement.GetProperty("version").ValueKind);
            var firstAccount = Assert.Single(
                firstCreateJson.RootElement.GetProperty("companyAccounts").EnumerateArray());
            Assert.Equal(
                JsonValueKind.String,
                firstAccount.GetProperty("version").ValueKind);
            Assert.DoesNotContain(
                "externalIntegrationCode",
                firstCreateBody,
                StringComparison.Ordinal);

            var afterCreate = await ReadEffectCountsAsync(connection);
            var directMissingPartyId = Guid.NewGuid();
            using var directMissing = await createClient.GetAsync(
                $"/api/v1/counterparties/{directMissingPartyId:D}");
            var directMissingBody = await AssertProblemCodeAsync(
                directMissing,
                HttpStatusCode.NotFound,
                CounterpartyProblemCodesV1.NotFound);
            Assert.DoesNotContain(
                directMissingPartyId.ToString("D"),
                directMissingBody,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(afterCreate, await ReadEffectCountsAsync(connection));

            var replayCorrelation = $"cari-create-replay-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, createKey, replayCorrelation);
            using var replayCreate = await createClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                createCommand);
            Assert.Equal(HttpStatusCode.Created, replayCreate.StatusCode);
            Assert.Equal(
                "true",
                Assert.Single(replayCreate.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                createCorrelation,
                Assert.Single(replayCreate.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));
            var replayCreateBody = await replayCreate.Content.ReadAsStringAsync();
            Assert.Equal(firstCreateBody, replayCreateBody);
            using var replayCreateJson = JsonDocument.Parse(replayCreateBody);
            Assert.Equal(
                partyId,
                replayCreateJson.RootElement.GetProperty("id").GetGuid());
            Assert.Equal(
                partyCode,
                replayCreateJson.RootElement.GetProperty("code").GetString());

            var mismatchCorrelation = $"cari-create-mismatch-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, createKey, mismatchCorrelation);
            using var mismatchCreate = await createClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                createCommand with { LegalName = "Farklı Payload A.Ş." });
            await AssertProblemCodeAsync(
                mismatchCreate,
                HttpStatusCode.Conflict,
                CounterpartyProblemCodesV1.IdempotencyConflict);
            Assert.False(mismatchCreate.Headers.Contains("Idempotency-Replayed"));

            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id] = @PartyId;",
                    new SqlParameter("@PartyId", partyId)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@PartyId", partyId)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = N'counterparty.party.create.v1' AND [IdempotencyKeyHash] = @KeyHash;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(createKey))));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyId AND [CorrelationId] = @CorrelationId AND [EventType] = N'counterparty.party.created.v1';",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@CorrelationId", createCorrelation)));

            var unknownSchemaKey = $"cari-create-unknown-schema-{Guid.NewGuid():N}";
            var unknownSchemaCorrelation =
                $"cari-create-unknown-schema-corr-{Guid.NewGuid():N}";
            await CloneCommandExecutionAsync(
                connection,
                TestCompanyIds.CompanyA,
                "counterparty.party.create.v1",
                createKey,
                unknownSchemaKey,
                unknownSchemaCorrelation,
                schemaVersion: 2);
            var beforeUnknownSchemaReplay = await ReadEffectCountsAsync(connection);

            SetRequestIdentity(
                createClient,
                unknownSchemaKey,
                unknownSchemaCorrelation);
            using var unknownSchemaReplay = await createClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                createCommand);
            await AssertProblemCodeAsync(
                unknownSchemaReplay,
                HttpStatusCode.InternalServerError,
                "internal_error");
            Assert.False(unknownSchemaReplay.Headers.Contains("Idempotency-Replayed"));
            Assert.False(unknownSchemaReplay.Headers.Contains(
                "Idempotency-Original-Correlation-Id"));
            Assert.Equal(
                beforeUnknownSchemaReplay,
                await ReadEffectCountsAsync(connection));

            var statusKey = $"cari-status-{Guid.NewGuid():N}";
            var statusCorrelation = $"cari-status-first-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, statusKey, statusCorrelation);
            using var firstStatus = await createClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                new SetCompanyPartyAccountActiveState(
                    false,
                    1,
                    "Gerçek SQL durum güncellemesi"));
            Assert.Equal(HttpStatusCode.NoContent, firstStatus.StatusCode);

            var staleKey = $"cari-status-stale-{Guid.NewGuid():N}";
            var staleCorrelation = $"cari-status-stale-first-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, staleKey, staleCorrelation);
            var staleCommand = new SetCompanyPartyAccountActiveState(
                true,
                1,
                "Stale sürüm terminal sonucu");
            using var firstStale = await createClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                staleCommand);
            await AssertConflictProblemAsync(
                firstStale,
                CounterpartyProblemCodesV1.VersionConflict,
                expectedVersion: 1,
                currentVersion: 2);
            Assert.False(firstStale.Headers.Contains("Idempotency-Replayed"));

            var staleReplayCorrelation = $"cari-status-stale-replay-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, staleKey, staleReplayCorrelation);
            using var staleReplay = await createClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                staleCommand);
            await AssertConflictProblemAsync(
                staleReplay,
                CounterpartyProblemCodesV1.VersionConflict,
                expectedVersion: 1,
                currentVersion: 2);
            Assert.Equal(
                "true",
                Assert.Single(staleReplay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                staleCorrelation,
                Assert.Single(staleReplay.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));

            var missingPartyId = Guid.NewGuid();
            var notFoundKey = $"cari-status-not-found-{Guid.NewGuid():N}";
            var notFoundCorrelation = $"cari-status-not-found-first-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, notFoundKey, notFoundCorrelation);
            var notFoundCommand = new SetCompanyPartyAccountActiveState(
                false,
                1,
                "Olmayan cari terminal sonucu");
            using var firstNotFound = await createClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{missingPartyId}/company-account/active-state",
                notFoundCommand);
            var firstNotFoundBody = await AssertProblemCodeAsync(
                firstNotFound,
                HttpStatusCode.NotFound,
                CounterpartyProblemCodesV1.NotFound);
            Assert.DoesNotContain(
                missingPartyId.ToString("D"),
                firstNotFoundBody,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(firstNotFound.Headers.Contains("Idempotency-Replayed"));

            var notFoundReplayCorrelation = $"cari-status-not-found-replay-{Guid.NewGuid():N}";
            SetRequestIdentity(createClient, notFoundKey, notFoundReplayCorrelation);
            using var replayNotFound = await createClient.PutAsJsonAsync(
                $"/api/v1/counterparties/{missingPartyId}/company-account/active-state",
                notFoundCommand);
            var replayNotFoundBody = await AssertProblemCodeAsync(
                replayNotFound,
                HttpStatusCode.NotFound,
                CounterpartyProblemCodesV1.NotFound);
            Assert.DoesNotContain(
                missingPartyId.ToString("D"),
                replayNotFoundBody,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                "true",
                Assert.Single(replayNotFound.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                notFoundCorrelation,
                Assert.Single(replayNotFound.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = N'counterparty.company-account.set-active-state.v1' AND [IdempotencyKeyHash] = @KeyHash AND [Outcome] = N'NotFound' AND [OriginalCorrelationId] = @CorrelationId;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(notFoundKey)),
                    new SqlParameter("@CorrelationId", notFoundCorrelation)));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
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

    private static async Task<string> AssertProblemCodeAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("type").GetString()));
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            (int)expectedStatus,
            document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("detail").GetString()));
        return body;
    }

    private static async Task AssertConflictProblemAsync(
        HttpResponseMessage response,
        string expectedCode,
        long expectedVersion,
        long currentVersion)
    {
        var body = await AssertProblemCodeAsync(
            response,
            HttpStatusCode.Conflict,
            expectedCode);
        using var document = JsonDocument.Parse(body);
        var conflict = document.RootElement.GetProperty("conflict");
        Assert.Equal(expectedVersion, conflict.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(currentVersion, conflict.GetProperty("currentVersion").GetInt64());
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

    private static async Task CloneCommandExecutionAsync(
        SqlConnection connection,
        Guid companyId,
        string commandName,
        string sourceIdempotencyKey,
        string targetIdempotencyKey,
        string originalCorrelationId,
        int schemaVersion)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [idempotency].[CommandExecutions]
                ([Id], [CompanyId], [CommandName], [IdempotencyKeyHash],
                 [RequestHash], [ActorId], [Outcome], [HttpStatusCode],
                 [ResponseJson], [OriginalCorrelationId], [CompletedAtUtc], [SchemaVersion])
            SELECT NEWID(), [CompanyId], [CommandName], @TargetKeyHash,
                   [RequestHash], [ActorId], [Outcome], [HttpStatusCode],
                   [ResponseJson], @OriginalCorrelationId, SYSUTCDATETIME(), @SchemaVersion
            FROM [idempotency].[CommandExecutions]
            WHERE [CompanyId] = @CompanyId
              AND [CommandName] = @CommandName
              AND [IdempotencyKeyHash] = @SourceKeyHash;
            """;
        command.Parameters.AddRange(
        [
            new SqlParameter("@CompanyId", companyId),
            new SqlParameter("@CommandName", commandName),
            new SqlParameter(
                "@SourceKeyHash",
                IdempotencyHash.ComputeKey(sourceIdempotencyKey)),
            new SqlParameter(
                "@TargetKeyHash",
                IdempotencyHash.ComputeKey(targetIdempotencyKey)),
            new SqlParameter("@OriginalCorrelationId", originalCorrelationId),
            new SqlParameter("@SchemaVersion", schemaVersion),
        ]);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<PersistenceCounts> ReadEffectCountsAsync(
        SqlConnection connection)
    {
        return new PersistenceCounts(
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [counterparty].[Parties];"),
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts];"),
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [audit].[AuditEvents];"),
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions];"));
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

    private sealed record PersistenceCounts(
        int PartyCount,
        int AccountCount,
        int AuditCount,
        int ExecutionCount);
}
