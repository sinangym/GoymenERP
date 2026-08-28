using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyWriteSafetySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task ConcurrentCreatesAndDifferentKeyStatusWrites_AreSerialized()
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

            var sameKey = $"cari-race-same-key-{Guid.NewGuid():N}";
            var sameKeyCorrelations = new[]
            {
                $"cari-race-same-a-{Guid.NewGuid():N}",
                $"cari-race-same-b-{Guid.NewGuid():N}",
            };
            using var sameKeyClientA = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            using var sameKeyClientB = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            SetRequestIdentity(sameKeyClientA, sameKey, sameKeyCorrelations[0]);
            SetRequestIdentity(sameKeyClientB, sameKey, sameKeyCorrelations[1]);
            var sameKeyCommand = new CreateCounterparty(
                PartyKind.LegalEntity,
                "SQL Aynı Anahtar Yarış Cari A.Ş.",
                null,
                PartyRole.Customer);

            var sameKeyResponses = await Task.WhenAll(
                sameKeyClientA.PostAsJsonAsync("/api/v1/counterparties", sameKeyCommand),
                sameKeyClientB.PostAsJsonAsync("/api/v1/counterparties", sameKeyCommand));
            try
            {
                Assert.All(
                    sameKeyResponses,
                    response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
                Assert.Equal(
                    1,
                    sameKeyResponses.Count(response =>
                        response.Headers.Contains("Idempotency-Replayed")));

                var partyIds = new List<Guid>();
                var partyCodes = new List<string>();
                foreach (var response in sameKeyResponses)
                {
                    using var document = JsonDocument.Parse(
                        await response.Content.ReadAsStringAsync());
                    partyIds.Add(document.RootElement.GetProperty("id").GetGuid());
                    partyCodes.Add(
                        document.RootElement.GetProperty("code").GetString()
                        ?? throw new InvalidOperationException("Cari kodu dönmedi."));
                }

                Assert.Single(partyIds.Distinct());
                Assert.Single(partyCodes.Distinct(StringComparer.Ordinal));
                Assert.Equal(
                    1,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [LegalName] = N'SQL Aynı Anahtar Yarış Cari A.Ş.';"));
                Assert.Equal(
                    1,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = N'counterparty.party.create.v1' AND [IdempotencyKeyHash] = @KeyHash;",
                        new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                        new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(sameKey))));
                Assert.Equal(
                    1,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] IN (@First, @Second) AND [EventType] = N'counterparty.party.created.v1';",
                        new SqlParameter("@First", sameKeyCorrelations[0]),
                        new SqlParameter("@Second", sameKeyCorrelations[1])));

                var partyId = partyIds[0];
                var currentVersion = await ScalarAsync<long>(
                    connection,
                    "SELECT [Version] FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                    new SqlParameter("@PartyId", partyId));
                Assert.Equal(1L, currentVersion);

                var statusCorrelations = new[]
                {
                    $"cari-status-race-a-{Guid.NewGuid():N}",
                    $"cari-status-race-b-{Guid.NewGuid():N}",
                };
                using var statusClientA = factory.CreateClientFor(
                    TestUsers.GroupAdmin,
                    TestCompanyIds.CompanyA);
                using var statusClientB = factory.CreateClientFor(
                    TestUsers.GroupAdmin,
                    TestCompanyIds.CompanyA);
                SetRequestIdentity(
                    statusClientA,
                    $"cari-status-race-key-a-{Guid.NewGuid():N}",
                    statusCorrelations[0]);
                SetRequestIdentity(
                    statusClientB,
                    $"cari-status-race-key-b-{Guid.NewGuid():N}",
                    statusCorrelations[1]);
                var statusCommand = new SetCompanyPartyAccountActiveState(
                    false,
                    currentVersion,
                    "Farklı anahtar optimistic concurrency testi");
                var statusResponses = await Task.WhenAll(
                    statusClientA.PutAsJsonAsync(
                        $"/api/v1/counterparties/{partyId}/company-account/active-state",
                        statusCommand),
                    statusClientB.PutAsJsonAsync(
                        $"/api/v1/counterparties/{partyId}/company-account/active-state",
                        statusCommand));
                try
                {
                    Assert.Single(
                        statusResponses,
                        response => response.StatusCode == HttpStatusCode.NoContent);
                    Assert.Single(
                        statusResponses,
                        response => response.StatusCode == HttpStatusCode.Conflict);
                    Assert.Equal(
                        2L,
                        await ScalarAsync<long>(
                            connection,
                            "SELECT [Version] FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                            new SqlParameter("@PartyId", partyId)));
                    Assert.Equal(
                        0,
                        await ScalarAsync<int>(
                            connection,
                            "SELECT CONVERT(int, [IsActive]) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
                            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                            new SqlParameter("@PartyId", partyId)));
                    Assert.Equal(
                        1,
                        await ScalarAsync<int>(
                            connection,
                            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] IN (@First, @Second) AND [EventType] = N'counterparty.company-account.active-state.changed.v1';",
                            new SqlParameter("@First", statusCorrelations[0]),
                            new SqlParameter("@Second", statusCorrelations[1])));
                    Assert.Equal(
                        2,
                        await ScalarAsync<int>(
                            connection,
                            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [OriginalCorrelationId] IN (@First, @Second) AND [CommandName] = N'counterparty.company-account.set-active-state.v1';",
                            new SqlParameter("@First", statusCorrelations[0]),
                            new SqlParameter("@Second", statusCorrelations[1])));
                }
                finally
                {
                    foreach (var response in statusResponses)
                    {
                        response.Dispose();
                    }
                }
            }
            finally
            {
                foreach (var response in sameKeyResponses)
                {
                    response.Dispose();
                }
            }

            const int parallelCreateCount = 4;
            var parallelClients = Enumerable.Range(0, parallelCreateCount)
                .Select(_ => factory.CreateClientFor(
                    TestUsers.GroupAdmin,
                    TestCompanyIds.CompanyA))
                .ToArray();
            try
            {
                var parallelTasks = parallelClients
                    .Select((client, index) =>
                    {
                        SetRequestIdentity(
                            client,
                            $"cari-sequence-key-{index}-{Guid.NewGuid():N}",
                            $"cari-sequence-correlation-{index}-{Guid.NewGuid():N}");
                        return client.PostAsJsonAsync(
                            "/api/v1/counterparties",
                            new CreateCounterparty(
                                PartyKind.LegalEntity,
                                $"SQL Paralel Kod Cari {index} A.Ş.",
                                null,
                                PartyRole.Supplier));
                    })
                    .ToArray();
                var parallelResponses = await Task.WhenAll(parallelTasks);
                try
                {
                    Assert.All(
                        parallelResponses,
                        response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
                    var codes = new List<string>();
                    foreach (var response in parallelResponses)
                    {
                        using var document = JsonDocument.Parse(
                            await response.Content.ReadAsStringAsync());
                        codes.Add(
                            document.RootElement.GetProperty("code").GetString()
                            ?? throw new InvalidOperationException("Cari kodu dönmedi."));
                    }

                    Assert.Equal(parallelCreateCount, codes.Distinct(StringComparer.Ordinal).Count());
                    Assert.Equal(
                        parallelCreateCount,
                        await ScalarAsync<int>(
                            connection,
                            "SELECT COUNT(DISTINCT [Code]) FROM [counterparty].[Parties] WHERE [LegalName] LIKE N'SQL Paralel Kod Cari % A.Ş.';"));
                }
                finally
                {
                    foreach (var response in parallelResponses)
                    {
                        response.Dispose();
                    }
                }
            }
            finally
            {
                foreach (var client in parallelClients)
                {
                    client.Dispose();
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
    public async Task AuditAndIdempotencyInsertFailures_RollBackCounterpartyCreate()
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

            var auditKey = $"cari-audit-reject-{Guid.NewGuid():N}";
            var auditCorrelation = $"cari-audit-reject-correlation-{Guid.NewGuid():N}";
            const string auditLegalName = "SQL Audit Rollback Cari A.Ş.";
            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TRIGGER [audit].[TR_AuditEvents_RejectCounterpartyInsertProbe]
                ON [audit].[AuditEvents]
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51021, 'Counterparty audit atomicity rejection probe.', 1;
                END;
                """);
            HttpResponseMessage auditRejectedResponse;
            try
            {
                using var client = factory.CreateClientFor(
                    TestUsers.GroupAdmin,
                    TestCompanyIds.CompanyA);
                SetRequestIdentity(client, auditKey, auditCorrelation);
                auditRejectedResponse = await client.PostAsJsonAsync(
                    "/api/v1/counterparties",
                    new CreateCounterparty(
                        PartyKind.LegalEntity,
                        auditLegalName,
                        null,
                        PartyRole.Customer));
            }
            finally
            {
                await ExecuteNonQueryAsync(
                    connection,
                    "DROP TRIGGER [audit].[TR_AuditEvents_RejectCounterpartyInsertProbe];");
            }

            using (auditRejectedResponse)
            {
                Assert.Equal(
                    HttpStatusCode.InternalServerError,
                    auditRejectedResponse.StatusCode);
            }
            await AssertRejectedCreateLeftNoBusinessOrSidecarRowsAsync(
                connection,
                auditLegalName,
                auditKey,
                auditCorrelation);

            var idempotencyKey = $"cari-idem-reject-{Guid.NewGuid():N}";
            var idempotencyCorrelation = $"cari-idem-reject-correlation-{Guid.NewGuid():N}";
            const string idempotencyLegalName = "SQL Idempotency Rollback Cari A.Ş.";
            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TRIGGER [idempotency].[TR_CommandExecutions_RejectCounterpartyInsertProbe]
                ON [idempotency].[CommandExecutions]
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51022, 'Counterparty idempotency atomicity rejection probe.', 1;
                END;
                """);
            HttpResponseMessage idempotencyRejectedResponse;
            try
            {
                using var client = factory.CreateClientFor(
                    TestUsers.GroupAdmin,
                    TestCompanyIds.CompanyA);
                SetRequestIdentity(client, idempotencyKey, idempotencyCorrelation);
                idempotencyRejectedResponse = await client.PostAsJsonAsync(
                    "/api/v1/counterparties",
                    new CreateCounterparty(
                        PartyKind.LegalEntity,
                        idempotencyLegalName,
                        null,
                        PartyRole.Supplier));
            }
            finally
            {
                await ExecuteNonQueryAsync(
                    connection,
                    "DROP TRIGGER [idempotency].[TR_CommandExecutions_RejectCounterpartyInsertProbe];");
            }

            using (idempotencyRejectedResponse)
            {
                Assert.Equal(
                    HttpStatusCode.InternalServerError,
                    idempotencyRejectedResponse.StatusCode);
            }
            await AssertRejectedCreateLeftNoBusinessOrSidecarRowsAsync(
                connection,
                idempotencyLegalName,
                idempotencyKey,
                idempotencyCorrelation);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task CounterpartyWrites_RequireAuthorizedCompanyWideContext()
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

            var command = new CreateCounterparty(
                PartyKind.LegalEntity,
                "Yetkisiz Yazma Deneme Cari A.Ş.",
                null,
                PartyRole.Customer);

            using var noWritePermissionClient = factory.CreateClientFor(
                TestUsers.CompanyA,
                TestCompanyIds.CompanyA);
            SetRequestIdentity(
                noWritePermissionClient,
                $"cari-no-permission-{Guid.NewGuid():N}",
                $"cari-no-permission-correlation-{Guid.NewGuid():N}");
            using var noWritePermissionResponse = await noWritePermissionClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                command);
            Assert.Equal(HttpStatusCode.Forbidden, noWritePermissionResponse.StatusCode);

            using var unauthorizedCompanyClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                Guid.Parse("6f000000-0000-0000-0000-000000000099"));
            SetRequestIdentity(
                unauthorizedCompanyClient,
                $"cari-wrong-company-{Guid.NewGuid():N}",
                $"cari-wrong-company-correlation-{Guid.NewGuid():N}");
            using var unauthorizedCompanyResponse = await unauthorizedCompanyClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                command);
            Assert.Equal(HttpStatusCode.Forbidden, unauthorizedCompanyResponse.StatusCode);

            using var missingCompanyClient = factory.CreateClient();
            missingCompanyClient.DefaultRequestHeaders.Add(
                "X-Development-User",
                TestUsers.GroupAdmin);
            SetRequestIdentity(
                missingCompanyClient,
                $"cari-missing-company-{Guid.NewGuid():N}",
                $"cari-missing-company-correlation-{Guid.NewGuid():N}");
            using var missingCompanyResponse = await missingCompanyClient.PostAsJsonAsync(
                "/api/v1/counterparties",
                command);
            Assert.Equal(HttpStatusCode.BadRequest, missingCompanyResponse.StatusCode);

            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [LegalName] = @LegalName;",
                    new SqlParameter("@LegalName", command.LegalName)));
            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName] LIKE N'counterparty.%';"));
            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] LIKE N'counterparty.%';"));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task AssertRejectedCreateLeftNoBusinessOrSidecarRowsAsync(
        SqlConnection connection,
        string legalName,
        string idempotencyKey,
        string correlationId)
    {
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [LegalName] = @LegalName;",
                new SqlParameter("@LegalName", legalName)));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [IdempotencyKeyHash] = @KeyHash;",
                new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(idempotencyKey))));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
                new SqlParameter("@CorrelationId", correlationId)));
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
}
