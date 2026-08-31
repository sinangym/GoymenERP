using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Observability;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class PlatformRequestMetadataProblemCodesSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task RejectedMetadata_IsStableNonDisclosingAndLeavesNoPersistentEffect()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using (var healthClient = factory.CreateClient())
            using (var healthResponse = await healthClient.GetAsync("/health/live"))
            {
                Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
            }

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            var before = await ReadLocationAsync(connection);
            var baselineAuditCount = await CountLocationAuditEventsAsync(
                connection,
                before.Id);
            var baselineTotalAuditCount = await CountTotalAuditEventsAsync(connection);
            var baselineTotalExecutionCount = await CountTotalExecutionsAsync(connection);
            var reusableIdempotencyKey =
                $"platform-metadata-retry-{Guid.NewGuid():N}";

            var invalidCorrelationValues = new[]
            {
                new[] { "   " },
                new[] { "unsafe/correlation" },
                new[] { new string('a', 129) },
                new[] { "correlation-a,correlation-b" },
                new[] { "correlation-a", "correlation-b" },
                new[] { "correlation-ğ" },
            };

            for (var index = 0; index < invalidCorrelationValues.Length; index++)
            {
                var values = invalidCorrelationValues[index];
                var idempotencyKey = index == 0
                    ? reusableIdempotencyKey
                    : $"platform-correlation-reject-{Guid.NewGuid():N}";
                using var response = await SendWriteAsync(
                    factory,
                    before,
                    idempotencyKey,
                    [TestCompanyIds.CompanyA.ToString()],
                    values,
                    causationValues: null);
                var generatedCorrelationId = await AssertProblemAsync(
                    response,
                    ErpProblemCodes.CorrelationMetadataInvalid,
                    values);
                Assert.True(Guid.TryParseExact(generatedCorrelationId, "N", out _));
                await AssertNoEffectsAsync(
                    connection,
                    before,
                    baselineAuditCount,
                    baselineTotalAuditCount,
                    baselineTotalExecutionCount,
                    idempotencyKey,
                    generatedCorrelationId);
            }

            var invalidCausationValues = new[]
            {
                new[] { "   " },
                new[] { "unsafe/causation" },
                new[] { new string('b', 129) },
                new[] { "causation-a,causation-b" },
                new[] { "causation-a", "causation-b" },
                new[] { "causation-\u007f" },
            };

            foreach (var values in invalidCausationValues)
            {
                var invalidCausationKey =
                    $"platform-causation-reject-{Guid.NewGuid():N}";
                var validCorrelationForInvalidCausation =
                    $"platform-valid-correlation-{Guid.NewGuid():N}";
                using var invalidCausationResponse = await SendWriteAsync(
                    factory,
                    before,
                    invalidCausationKey,
                    [TestCompanyIds.CompanyA.ToString()],
                    [validCorrelationForInvalidCausation],
                    values);
                var generatedCorrelationId = await AssertProblemAsync(
                    invalidCausationResponse,
                    ErpProblemCodes.CorrelationMetadataInvalid,
                    values.Append(validCorrelationForInvalidCausation).ToArray());
                Assert.True(Guid.TryParseExact(generatedCorrelationId, "N", out _));
                Assert.NotEqual(validCorrelationForInvalidCausation, generatedCorrelationId);
                await AssertNoEffectsAsync(
                    connection,
                    before,
                    baselineAuditCount,
                    baselineTotalAuditCount,
                    baselineTotalExecutionCount,
                    invalidCausationKey,
                    generatedCorrelationId);
            }

            var invalidCompanyValues = new string[]?[]
            {
                null,
                [string.Empty],
                ["   "],
                ["not-a-company-guid"],
                [Guid.Empty.ToString()],
                [$"{TestCompanyIds.CompanyA},{TestCompanyIds.CompanyB}"],
                [TestCompanyIds.CompanyA.ToString(), TestCompanyIds.CompanyB.ToString()],
            };

            for (var index = 0; index < invalidCompanyValues.Length; index++)
            {
                var values = invalidCompanyValues[index];
                var idempotencyKey = index == 0
                    ? reusableIdempotencyKey
                    : $"platform-company-reject-{Guid.NewGuid():N}";
                var correlationId = $"platform-company-correlation-{Guid.NewGuid():N}";
                using var response = await SendWriteAsync(
                    factory,
                    before,
                    idempotencyKey,
                    values,
                    [correlationId],
                    causationValues: null);
                var responseCorrelationId = await AssertProblemAsync(
                    response,
                    ErpProblemCodes.CompanyContextInvalid,
                    values ?? []);
                Assert.Equal(correlationId, responseCorrelationId);
                await AssertNoEffectsAsync(
                    connection,
                    before,
                    baselineAuditCount,
                    baselineTotalAuditCount,
                    baselineTotalExecutionCount,
                    idempotencyKey,
                    correlationId);
            }

            var unauthorizedKey = $"platform-company-forbidden-{Guid.NewGuid():N}";
            var unauthorizedCorrelation =
                $"platform-company-forbidden-correlation-{Guid.NewGuid():N}";
            using (var unauthorizedResponse = await SendWriteAsync(
                       factory,
                       before,
                       unauthorizedKey,
                       [TestCompanyIds.CompanyB.ToString()],
                       [unauthorizedCorrelation],
                       causationValues: null))
            {
                await AssertProblemAsync(
                    unauthorizedResponse,
                    ErpProblemCodes.Forbidden,
                    TestCompanyIds.CompanyB.ToString(),
                    "permission",
                    "role");
                await AssertNoEffectsAsync(
                    connection,
                    before,
                    baselineAuditCount,
                    baselineTotalAuditCount,
                    baselineTotalExecutionCount,
                    unauthorizedKey,
                    unauthorizedCorrelation);
            }

            using (var unknownClient = factory.CreateClient())
            using (var unknownRequest = new HttpRequestMessage(
                       HttpMethod.Get,
                       "/_acceptance/platform-request-metadata-unknown"))
            {
                Assert.True(unknownRequest.Headers.TryAddWithoutValidation(
                    CorrelationContextMiddleware.CorrelationHeaderName,
                    "unsafe/unknown-correlation"));
                var unknownKey = $"platform-unknown-{Guid.NewGuid():N}";
                Assert.True(unknownRequest.Headers.TryAddWithoutValidation(
                    "Idempotency-Key",
                    unknownKey));
                using var unknownResponse = await unknownClient.SendAsync(unknownRequest);
                var generatedCorrelationId = await AssertProblemAsync(
                    unknownResponse,
                    ErpProblemCodes.CorrelationMetadataInvalid,
                    "unsafe/unknown-correlation",
                    "platform-request-metadata-unknown");
                await AssertNoEffectsAsync(
                    connection,
                    before,
                    baselineAuditCount,
                    baselineTotalAuditCount,
                    baselineTotalExecutionCount,
                    unknownKey,
                    generatedCorrelationId);
            }

            var counterpartyKey = $"platform-counterparty-reject-{Guid.NewGuid():N}";
            var counterpartyCorrelation = "unsafe/counterparty-correlation";
            var baselinePartyCount = await CountCounterpartiesAsync(connection);
            using (var counterpartyResponse = await SendCounterpartyWriteAsync(
                       factory,
                       counterpartyKey,
                       [TestCompanyIds.CompanyA.ToString()],
                       [counterpartyCorrelation]))
            {
                var generatedCorrelationId = await AssertProblemAsync(
                    counterpartyResponse,
                    ErpProblemCodes.CorrelationMetadataInvalid,
                    counterpartyCorrelation,
                    "counterpart",
                    "command_metadata_invalid");
                Assert.Equal(
                    baselinePartyCount,
                    await CountCounterpartiesAsync(connection));
                await AssertNoEffectsAsync(
                    connection,
                    before,
                    baselineAuditCount,
                    baselineTotalAuditCount,
                    baselineTotalExecutionCount,
                    counterpartyKey,
                    generatedCorrelationId);
            }

            var invalidCounterpartyCompanyValues = new string[]?[]
            {
                null,
                ["not-a-company-guid"],
            };
            foreach (var companyValues in invalidCounterpartyCompanyValues)
            {
                var invalidCompanyKey =
                    $"platform-counterparty-company-{Guid.NewGuid():N}";
                var invalidCompanyCorrelation =
                    $"platform-counterparty-company-corr-{Guid.NewGuid():N}";
                using var response = await SendCounterpartyWriteAsync(
                    factory,
                    invalidCompanyKey,
                    companyValues,
                    [invalidCompanyCorrelation]);
                var responseCorrelation = await AssertProblemAsync(
                    response,
                    ErpProblemCodes.CompanyContextInvalid,
                    companyValues ?? []);
                Assert.Equal(invalidCompanyCorrelation, responseCorrelation);
                Assert.Equal(
                    baselinePartyCount,
                    await CountCounterpartiesAsync(connection));
                await AssertNoEffectsAsync(
                    connection,
                    before,
                    baselineAuditCount,
                    baselineTotalAuditCount,
                    baselineTotalExecutionCount,
                    invalidCompanyKey,
                    invalidCompanyCorrelation);
            }

            var successKey = reusableIdempotencyKey;
            using var successResponse = await SendWriteAsync(
                factory,
                before,
                successKey,
                [TestCompanyIds.CompanyA.ToString()],
                correlationValues: null,
                causationValues: null);
            Assert.Equal(HttpStatusCode.NoContent, successResponse.StatusCode);
            var successCorrelationId = Assert.Single(
                successResponse.Headers.GetValues(
                    CorrelationContextMiddleware.CorrelationHeaderName));
            Assert.True(Guid.TryParseExact(successCorrelationId, "N", out _));

            var afterSuccess = await ReadLocationAsync(connection);
            Assert.Equal(!before.IsActive, afterSuccess.IsActive);
            Assert.Equal(before.Version + 1, afterSuccess.Version);
            Assert.Equal(
                baselineAuditCount + 1,
                await CountLocationAuditEventsAsync(connection, before.Id));
            Assert.Equal(
                1,
                await CountExecutionAsync(connection, successKey));
            Assert.Equal(
                successCorrelationId,
                await ScalarAsync<string>(
                    connection,
                    """
                    SELECT [OriginalCorrelationId]
                    FROM [idempotency].[CommandExecutions]
                    WHERE [IdempotencyKeyHash] = @KeyHash;
                    """,
                    new SqlParameter(
                        "@KeyHash",
                        IdempotencyHash.ComputeKey(successKey))));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COUNT(*)
                    FROM [audit].[AuditEvents]
                    WHERE [EntityId] = @EntityId
                      AND [CorrelationId] = @CorrelationId;
                    """,
                    new SqlParameter("@EntityId", before.Id.ToString("D")),
                    new SqlParameter("@CorrelationId", successCorrelationId)));

            var replayCorrelationId =
                $"platform-metadata-replay-{Guid.NewGuid():N}";
            using var replayResponse = await SendWriteAsync(
                factory,
                before,
                successKey,
                [TestCompanyIds.CompanyA.ToString()],
                [replayCorrelationId],
                causationValues: null);
            Assert.Equal(HttpStatusCode.NoContent, replayResponse.StatusCode);
            Assert.Equal(
                "true",
                Assert.Single(replayResponse.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                successCorrelationId,
                Assert.Single(replayResponse.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));
            Assert.Equal(afterSuccess, await ReadLocationAsync(connection));
            Assert.Equal(
                baselineAuditCount + 1,
                await CountLocationAuditEventsAsync(connection, before.Id));
            Assert.Equal(baselineTotalAuditCount + 1, await CountTotalAuditEventsAsync(connection));
            Assert.Equal(
                baselineTotalExecutionCount + 1,
                await CountTotalExecutionsAsync(connection));
            Assert.Equal(1, await CountExecutionAsync(connection, successKey));
            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
                    new SqlParameter("@CorrelationId", replayCorrelationId)));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task<HttpResponseMessage> SendWriteAsync(
        SqlServerRelationalApiFactory factory,
        LocationProbe location,
        string idempotencyKey,
        string[]? companyValues,
        string[]? correlationValues,
        string[]? causationValues)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/organization/locations/{location.Id}/active-state")
        {
            Content = JsonContent.Create(new SetLocationActiveState(
                !location.IsActive,
                location.Version,
                "Platform istek metadata kabul testi")),
        };
        Assert.True(request.Headers.TryAddWithoutValidation(
            "X-Development-User",
            TestUsers.CompanyA));
        Assert.True(request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            idempotencyKey));
        if (companyValues is not null)
        {
            Assert.True(request.Headers.TryAddWithoutValidation(
                "X-Company-Id",
                companyValues));
        }

        if (correlationValues is not null)
        {
            Assert.True(request.Headers.TryAddWithoutValidation(
                CorrelationContextMiddleware.CorrelationHeaderName,
                correlationValues));
        }

        if (causationValues is not null)
        {
            Assert.True(request.Headers.TryAddWithoutValidation(
                CorrelationContextMiddleware.CausationHeaderName,
                causationValues));
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendCounterpartyWriteAsync(
        SqlServerRelationalApiFactory factory,
        string idempotencyKey,
        string[]? companyValues,
        string[]? correlationValues)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/counterparties")
        {
            Content = JsonContent.Create(new { }),
        };
        Assert.True(request.Headers.TryAddWithoutValidation(
            "X-Development-User",
            TestUsers.GroupAdmin));
        Assert.True(request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            idempotencyKey));
        if (companyValues is not null)
        {
            Assert.True(request.Headers.TryAddWithoutValidation(
                "X-Company-Id",
                companyValues));
        }

        if (correlationValues is not null)
        {
            Assert.True(request.Headers.TryAddWithoutValidation(
                CorrelationContextMiddleware.CorrelationHeaderName,
                correlationValues));
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> AssertProblemAsync(
        HttpResponseMessage response,
        string expectedCode,
        params string[] forbiddenFragments)
    {
        Assert.Equal(
            expectedCode == ErpProblemCodes.Forbidden
                ? HttpStatusCode.Forbidden
                : HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("instance", out _));
        var correlationId = Assert.Single(
            response.Headers.GetValues(
                CorrelationContextMiddleware.CorrelationHeaderName));
        Assert.Equal(
            correlationId,
            root.GetProperty("correlationId").GetString());
        foreach (var fragment in forbiddenFragments.Where(
                     value => !string.IsNullOrEmpty(value)))
        {
            Assert.DoesNotContain(fragment, body, StringComparison.OrdinalIgnoreCase);
        }

        return correlationId;
    }

    private static async Task AssertNoEffectsAsync(
        SqlConnection connection,
        LocationProbe expectedLocation,
        int expectedAuditCount,
        int expectedTotalAuditCount,
        int expectedTotalExecutionCount,
        string idempotencyKey,
        string correlationId)
    {
        Assert.Equal(expectedLocation, await ReadLocationAsync(connection));
        Assert.Equal(
            expectedAuditCount,
            await CountLocationAuditEventsAsync(connection, expectedLocation.Id));
        Assert.Equal(expectedTotalAuditCount, await CountTotalAuditEventsAsync(connection));
        Assert.Equal(expectedTotalExecutionCount, await CountTotalExecutionsAsync(connection));
        Assert.Equal(0, await CountExecutionAsync(connection, idempotencyKey));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
                new SqlParameter("@CorrelationId", correlationId)));
    }

    private static Task<LocationProbe> ReadLocationAsync(SqlConnection connection)
    {
        return ReadLocationAsync(
            connection,
            TestCompanyIds.CompanyA,
            "OSM-H5");
    }

    private static async Task<LocationProbe> ReadLocationAsync(
        SqlConnection connection,
        Guid companyId,
        string code)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) [Id], [IsActive], [Version]
            FROM [organization].[Locations]
            WHERE [CompanyId] = @CompanyId AND [Code] = @Code;
            """;
        command.Parameters.AddWithValue("@CompanyId", companyId);
        command.Parameters.AddWithValue("@Code", code);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new LocationProbe(
            reader.GetGuid(0),
            reader.GetBoolean(1),
            reader.GetInt64(2));
    }

    private static Task<int> CountLocationAuditEventsAsync(
        SqlConnection connection,
        Guid locationId)
    {
        return ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityId] = @EntityId;",
            new SqlParameter("@EntityId", locationId.ToString("D")));
    }

    private static Task<int> CountExecutionAsync(
        SqlConnection connection,
        string idempotencyKey)
    {
        return ScalarAsync<int>(
            connection,
            """
            SELECT COUNT(*)
            FROM [idempotency].[CommandExecutions]
            WHERE [IdempotencyKeyHash] = @KeyHash;
            """,
            new SqlParameter(
                "@KeyHash",
                IdempotencyHash.ComputeKey(idempotencyKey)));
    }

    private static Task<int> CountTotalAuditEventsAsync(SqlConnection connection)
    {
        return ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM [audit].[AuditEvents];");
    }

    private static Task<int> CountTotalExecutionsAsync(SqlConnection connection)
    {
        return ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions];");
    }

    private static Task<int> CountCounterpartiesAsync(SqlConnection connection)
    {
        return ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM [counterparty].[Parties];");
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

    private sealed record LocationProbe(Guid Id, bool IsActive, long Version);
}
