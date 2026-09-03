using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyCompanyLinkSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task ExistingParty_CanBeLinkedToSecondAuthorizedCompany_WithoutDuplicatingParty()
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

            var created = await CreatePartyAsync(factory, "SQL Çok Şirketli Cari A.Ş.");
            var linkKey = $"cari-link-{Guid.NewGuid():N}";
            var firstCorrelation = $"cari-link-first-{Guid.NewGuid():N}";
            using var targetClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            SetRequestIdentity(targetClient, linkKey, firstCorrelation);
            var linkCommand = new CreateCompanyPartyAccount(
                PartyRole.Supplier | PartyRole.Service);

            using var firstLink = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{created.PartyId}/company-account",
                linkCommand);
            Assert.Equal(HttpStatusCode.Created, firstLink.StatusCode);
            Assert.False(firstLink.Headers.Contains("Idempotency-Replayed"));
            var firstLinkBody = await firstLink.Content.ReadAsStringAsync();
            using var firstLinkJson = JsonDocument.Parse(firstLinkBody);
            Assert.Equal(created.PartyId, firstLinkJson.RootElement.GetProperty("id").GetGuid());
            Assert.Equal(created.Code, firstLinkJson.RootElement.GetProperty("code").GetString());
            Assert.Equal(
                JsonValueKind.String,
                firstLinkJson.RootElement.GetProperty("version").ValueKind);
            Assert.DoesNotContain(
                "externalIntegrationCode",
                firstLinkBody,
                StringComparison.Ordinal);
            var targetAccounts = firstLinkJson.RootElement
                .GetProperty("companyAccounts")
                .EnumerateArray()
                .ToArray();
            Assert.Single(targetAccounts);
            Assert.Equal(
                TestCompanyIds.CompanyB,
                targetAccounts[0].GetProperty("companyId").GetGuid());
            Assert.Equal(
                JsonValueKind.String,
                targetAccounts[0].GetProperty("version").ValueKind);

            var replayCorrelation = $"cari-link-replay-{Guid.NewGuid():N}";
            SetRequestIdentity(targetClient, linkKey, replayCorrelation);
            using var replay = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{created.PartyId}/company-account",
                linkCommand);
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal(
                "true",
                Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                firstCorrelation,
                Assert.Single(replay.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));
            Assert.Equal(firstLinkBody, await replay.Content.ReadAsStringAsync());

            SetRequestIdentity(
                targetClient,
                linkKey,
                $"cari-link-mismatch-{Guid.NewGuid():N}");
            using var mismatch = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{created.PartyId}/company-account",
                new CreateCompanyPartyAccount(PartyRole.Customer));
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
            Assert.False(mismatch.Headers.Contains("Idempotency-Replayed"));

            var duplicateKey = $"cari-link-duplicate-{Guid.NewGuid():N}";
            var duplicateCorrelation = $"cari-link-duplicate-first-{Guid.NewGuid():N}";
            SetRequestIdentity(targetClient, duplicateKey, duplicateCorrelation);
            using var duplicate = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{created.PartyId}/company-account",
                linkCommand);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.False(duplicate.Headers.Contains("Idempotency-Replayed"));

            SetRequestIdentity(
                targetClient,
                duplicateKey,
                $"cari-link-duplicate-replay-{Guid.NewGuid():N}");
            using var duplicateReplay = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{created.PartyId}/company-account",
                linkCommand);
            Assert.Equal(HttpStatusCode.Conflict, duplicateReplay.StatusCode);
            Assert.Equal(
                "true",
                Assert.Single(duplicateReplay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(
                duplicateCorrelation,
                Assert.Single(duplicateReplay.Headers.GetValues(
                    "Idempotency-Original-Correlation-Id")));

            using var groupClient = CreateGroupClient(factory, TestUsers.GroupAdmin);
            using var groupResponse = await groupClient.GetAsync(
                $"/api/v1/counterparties/{created.PartyId}");
            Assert.Equal(HttpStatusCode.OK, groupResponse.StatusCode);
            using var groupJson = JsonDocument.Parse(
                await groupResponse.Content.ReadAsStringAsync());
            Assert.Equal(created.Code, groupJson.RootElement.GetProperty("code").GetString());
            var groupAccounts = groupJson.RootElement
                .GetProperty("companyAccounts")
                .EnumerateArray()
                .ToArray();
            Assert.Equal(2, groupAccounts.Length);
            Assert.Contains(
                groupAccounts,
                account => account.GetProperty("companyId").GetGuid() == TestCompanyIds.CompanyA);
            Assert.Contains(
                groupAccounts,
                account => account.GetProperty("companyId").GetGuid() == TestCompanyIds.CompanyB);

            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id] = @PartyId AND [Code] = @Code;",
                    new SqlParameter("@PartyId", created.PartyId),
                    new SqlParameter("@Code", created.Code)));
            Assert.Equal(
                2,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [PartyId] = @PartyId;",
                    new SqlParameter("@PartyId", created.PartyId)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId AND [ExternalIntegrationCode] IS NULL;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                    new SqlParameter("@PartyId", created.PartyId)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyId AND [CorrelationId] = @CorrelationId AND [EventType] = N'counterparty.company-account.created.v1';",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                    new SqlParameter("@CorrelationId", firstCorrelation)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = N'counterparty.company-account.create.v1' AND [IdempotencyKeyHash] = @KeyHash AND [Outcome] = N'Created';",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                    new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(linkKey))));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = N'counterparty.company-account.create.v1' AND [IdempotencyKeyHash] = @KeyHash AND [Outcome] = N'AlreadyLinked';",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                    new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(duplicateKey))));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task DurableCreatedReplay_FailsClosedForUnknownSchemaOrForeignAccountRelationship()
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

            var firstParty = await CreatePartyAsync(factory, "Replay Bütünlük Cari Bir A.Ş.");
            var secondParty = await CreatePartyAsync(factory, "Replay Bütünlük Cari İki A.Ş.");
            using var targetClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            var roles = PartyRole.Supplier | PartyRole.Service;
            var linkCommand = new CreateCompanyPartyAccount(roles);
            var firstLinkKey = $"cari-link-integrity-one-{Guid.NewGuid():N}";
            var secondLinkKey = $"cari-link-integrity-two-{Guid.NewGuid():N}";

            SetRequestIdentity(
                targetClient,
                firstLinkKey,
                $"cari-link-integrity-one-corr-{Guid.NewGuid():N}");
            using var firstLink = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{firstParty.PartyId}/company-account",
                linkCommand);
            Assert.Equal(HttpStatusCode.Created, firstLink.StatusCode);
            using var firstLinkJson = JsonDocument.Parse(
                await firstLink.Content.ReadAsStringAsync());
            var firstAccountId = Assert.Single(
                    firstLinkJson.RootElement.GetProperty("companyAccounts").EnumerateArray())
                .GetProperty("id")
                .GetGuid();

            SetRequestIdentity(
                targetClient,
                secondLinkKey,
                $"cari-link-integrity-two-corr-{Guid.NewGuid():N}");
            using var secondLink = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{secondParty.PartyId}/company-account",
                linkCommand);
            Assert.Equal(HttpStatusCode.Created, secondLink.StatusCode);
            using var secondLinkJson = JsonDocument.Parse(
                await secondLink.Content.ReadAsStringAsync());
            var secondAccountId = Assert.Single(
                    secondLinkJson.RootElement.GetProperty("companyAccounts").EnumerateArray())
                .GetProperty("id")
                .GetGuid();
            Assert.NotEqual(firstAccountId, secondAccountId);

            var unknownSchemaKey = $"cari-link-unknown-schema-{Guid.NewGuid():N}";
            await CloneCommandExecutionAsync(
                connection,
                TestCompanyIds.CompanyB,
                firstLinkKey,
                unknownSchemaKey,
                $"cari-link-unknown-schema-corr-{Guid.NewGuid():N}",
                schemaVersion: 2,
                responseJson: null);
            var beforeUnknownSchemaReplay = await ReadEffectCountsAsync(connection);

            SetRequestIdentity(
                targetClient,
                unknownSchemaKey,
                $"cari-link-unknown-schema-request-{Guid.NewGuid():N}");
            using var unknownSchemaReplay = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{firstParty.PartyId}/company-account",
                linkCommand);
            await AssertInternalErrorWithoutReplayHeadersAsync(unknownSchemaReplay);
            Assert.Equal(
                beforeUnknownSchemaReplay,
                await ReadEffectCountsAsync(connection));

            var durableJson = await ScalarAsync<string>(
                connection,
                "SELECT [ResponseJson] FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = N'counterparty.company-account.create.v1' AND [IdempotencyKeyHash] = @KeyHash;",
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(firstLinkKey)));
            var durableResponse = JsonSerializer.Deserialize<CounterpartyCommandResponse>(
                durableJson)
                ?? throw new InvalidOperationException("Kalıcı link yanıtı okunamadı.");
            var durableResource = durableResponse.Resource
                ?? throw new InvalidOperationException("Kalıcı link kaynağı bulunamadı.");
            var durableAccount = Assert.Single(durableResource.CompanyAccounts);
            Assert.Equal(firstAccountId, durableAccount.Id);
            var forgedResponseJson = JsonSerializer.Serialize(durableResponse with
            {
                Resource = durableResource with
                {
                    CompanyAccounts = [durableAccount with { Id = secondAccountId }],
                },
            });
            var forgedRelationshipKey =
                $"cari-link-forged-relationship-{Guid.NewGuid():N}";
            await CloneCommandExecutionAsync(
                connection,
                TestCompanyIds.CompanyB,
                firstLinkKey,
                forgedRelationshipKey,
                $"cari-link-forged-relationship-corr-{Guid.NewGuid():N}",
                schemaVersion: 1,
                responseJson: forgedResponseJson);
            var beforeForgedRelationshipReplay = await ReadEffectCountsAsync(connection);

            SetRequestIdentity(
                targetClient,
                forgedRelationshipKey,
                $"cari-link-forged-relationship-request-{Guid.NewGuid():N}");
            using var forgedRelationshipReplay = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{firstParty.PartyId}/company-account",
                linkCommand);
            await AssertInternalErrorWithoutReplayHeadersAsync(forgedRelationshipReplay);
            Assert.Equal(
                beforeForgedRelationshipReplay,
                await ReadEffectCountsAsync(connection));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task TargetAuthorizedActor_CannotLinkPartyThatIsNotVisibleThroughAnyAllowedRelationship()
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

            var created = await CreatePartyAsync(factory, "SQL Gizli Kaynak Cari A.Ş.");

            await ExecuteNonQueryAsync(
                connection,
                """
                DELETE FROM [identity].[RoleAssignments]
                WHERE [UserId] = '20000000-0000-0000-0000-000000000003'
                  AND [CompanyId] = @CompanyA;
                """,
                new SqlParameter("@CompanyA", TestCompanyIds.CompanyA));

            using var targetClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            var key = $"cari-hidden-link-{Guid.NewGuid():N}";
            var correlation = $"cari-hidden-link-correlation-{Guid.NewGuid():N}";
            SetRequestIdentity(targetClient, key, correlation);
            using var response = await targetClient.PostAsJsonAsync(
                $"/api/v1/counterparties/{created.PartyId}/company-account",
                new CreateCompanyPartyAccount(PartyRole.Customer));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyB AND [PartyId] = @PartyId;",
                    new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                    new SqlParameter("@PartyId", created.PartyId)));
            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyB AND [CorrelationId] = @CorrelationId;",
                    new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                    new SqlParameter("@CorrelationId", correlation)));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyB AND [IdempotencyKeyHash] = @KeyHash AND [Outcome] = N'NotFound';",
                    new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                    new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(key))));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task SameAndDifferentKeyLinkRaces_ProduceOneAccountAndNoServerError()
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

            var sameKeyParty = await CreatePartyAsync(factory, "SQL Same-Key Link Yarış Cari A.Ş.");
            var sameKey = $"cari-link-race-same-{Guid.NewGuid():N}";
            using var sameClientA = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            using var sameClientB = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            SetRequestIdentity(sameClientA, sameKey, $"cari-link-race-same-a-{Guid.NewGuid():N}");
            SetRequestIdentity(sameClientB, sameKey, $"cari-link-race-same-b-{Guid.NewGuid():N}");
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
                Assert.All(sameResponses, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode));
                Assert.Equal(1, sameResponses.Count(x => x.Headers.Contains("Idempotency-Replayed")));
                Assert.Equal(
                    1,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyB AND [PartyId] = @PartyId;",
                        new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                        new SqlParameter("@PartyId", sameKeyParty.PartyId)));
                Assert.Equal(
                    1,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyB AND [CommandName] = N'counterparty.company-account.create.v1' AND [IdempotencyKeyHash] = @KeyHash;",
                        new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                        new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(sameKey))));
            }
            finally
            {
                foreach (var response in sameResponses)
                {
                    response.Dispose();
                }
            }

            var differentKeyParty = await CreatePartyAsync(factory, "SQL Different-Key Link Yarış Cari A.Ş.");
            var differentKeyA = $"cari-link-race-diff-a-{Guid.NewGuid():N}";
            var differentKeyB = $"cari-link-race-diff-b-{Guid.NewGuid():N}";
            using var differentClientA = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            using var differentClientB = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            SetRequestIdentity(
                differentClientA,
                differentKeyA,
                $"cari-link-race-diff-corr-a-{Guid.NewGuid():N}");
            SetRequestIdentity(
                differentClientB,
                differentKeyB,
                $"cari-link-race-diff-corr-b-{Guid.NewGuid():N}");
            var differentResponses = await Task.WhenAll(
                differentClientA.PostAsJsonAsync(
                    $"/api/v1/counterparties/{differentKeyParty.PartyId}/company-account",
                    command),
                differentClientB.PostAsJsonAsync(
                    $"/api/v1/counterparties/{differentKeyParty.PartyId}/company-account",
                    command));
            try
            {
                Assert.Single(differentResponses, x => x.StatusCode == HttpStatusCode.Created);
                Assert.Single(differentResponses, x => x.StatusCode == HttpStatusCode.Conflict);
                Assert.DoesNotContain(
                    differentResponses,
                    x => x.StatusCode == HttpStatusCode.InternalServerError);
                Assert.Equal(
                    1,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyB AND [PartyId] = @PartyId;",
                        new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                        new SqlParameter("@PartyId", differentKeyParty.PartyId)));
                Assert.Equal(
                    2,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyB AND [CommandName] = N'counterparty.company-account.create.v1' AND [IdempotencyKeyHash] IN (@KeyHashA, @KeyHashB);",
                        new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                        new SqlParameter("@KeyHashA", IdempotencyHash.ComputeKey(differentKeyA)),
                        new SqlParameter("@KeyHashB", IdempotencyHash.ComputeKey(differentKeyB))));
                Assert.Equal(
                    1,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyB AND [CommandName] = N'counterparty.company-account.create.v1' AND [IdempotencyKeyHash] IN (@KeyHashA, @KeyHashB) AND [Outcome] = N'Created';",
                        new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                        new SqlParameter("@KeyHashA", IdempotencyHash.ComputeKey(differentKeyA)),
                        new SqlParameter("@KeyHashB", IdempotencyHash.ComputeKey(differentKeyB))));
                Assert.Equal(
                    1,
                    await ScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyB AND [CommandName] = N'counterparty.company-account.create.v1' AND [IdempotencyKeyHash] IN (@KeyHashA, @KeyHashB) AND [Outcome] = N'AlreadyLinked';",
                        new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                        new SqlParameter("@KeyHashA", IdempotencyHash.ComputeKey(differentKeyA)),
                        new SqlParameter("@KeyHashB", IdempotencyHash.ComputeKey(differentKeyB))));
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
    public async Task AuditOrIdempotencyFailure_RollsBackOnlyNewCompanyLink()
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

            var auditParty = await CreatePartyAsync(factory, "SQL Link Audit Rollback Cari A.Ş.");
            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TRIGGER [audit].[TR_AuditEvents_RejectCounterpartyLinkProbe]
                ON [audit].[AuditEvents]
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted WHERE [EventType] = N'counterparty.company-account.created.v1')
                        THROW 51031, 'Counterparty company-link audit rejection probe.', 1;
                END;
                """);
            var auditKey = $"cari-link-audit-reject-{Guid.NewGuid():N}";
            var auditCorrelation = $"cari-link-audit-reject-corr-{Guid.NewGuid():N}";
            HttpResponseMessage auditResponse;
            try
            {
                using var targetClient = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
                SetRequestIdentity(targetClient, auditKey, auditCorrelation);
                auditResponse = await targetClient.PostAsJsonAsync(
                    $"/api/v1/counterparties/{auditParty.PartyId}/company-account",
                    new CreateCompanyPartyAccount(PartyRole.Customer));
            }
            finally
            {
                await ExecuteNonQueryAsync(
                    connection,
                    "DROP TRIGGER [audit].[TR_AuditEvents_RejectCounterpartyLinkProbe];");
            }

            using (auditResponse)
            {
                Assert.Equal(HttpStatusCode.InternalServerError, auditResponse.StatusCode);
            }
            await AssertRejectedLinkLeftNoTargetSideEffectsAsync(
                connection,
                auditParty.PartyId,
                auditKey,
                auditCorrelation);

            var idempotencyParty = await CreatePartyAsync(factory, "SQL Link Idempotency Rollback Cari A.Ş.");
            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TRIGGER [idempotency].[TR_CommandExecutions_RejectCounterpartyLinkProbe]
                ON [idempotency].[CommandExecutions]
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted WHERE [CommandName] = N'counterparty.company-account.create.v1')
                        THROW 51032, 'Counterparty company-link idempotency rejection probe.', 1;
                END;
                """);
            var idempotencyKey = $"cari-link-idem-reject-{Guid.NewGuid():N}";
            var idempotencyCorrelation = $"cari-link-idem-reject-corr-{Guid.NewGuid():N}";
            HttpResponseMessage idempotencyResponse;
            try
            {
                using var targetClient = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
                SetRequestIdentity(targetClient, idempotencyKey, idempotencyCorrelation);
                idempotencyResponse = await targetClient.PostAsJsonAsync(
                    $"/api/v1/counterparties/{idempotencyParty.PartyId}/company-account",
                    new CreateCompanyPartyAccount(PartyRole.Supplier));
            }
            finally
            {
                await ExecuteNonQueryAsync(
                    connection,
                    "DROP TRIGGER [idempotency].[TR_CommandExecutions_RejectCounterpartyLinkProbe];");
            }

            using (idempotencyResponse)
            {
                Assert.Equal(HttpStatusCode.InternalServerError, idempotencyResponse.StatusCode);
            }
            await AssertRejectedLinkLeftNoTargetSideEffectsAsync(
                connection,
                idempotencyParty.PartyId,
                idempotencyKey,
                idempotencyCorrelation);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
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
            $"cari-source-create-{Guid.NewGuid():N}",
            $"cari-source-create-correlation-{Guid.NewGuid():N}");
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
            document.RootElement.GetProperty("code").GetString()
            ?? throw new InvalidOperationException("Cari kodu dönmedi."));
    }

    private static HttpClient CreateGroupClient(
        SqlServerRelationalApiFactory factory,
        string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", userId);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return client;
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

    private static async Task AssertRejectedLinkLeftNoTargetSideEffectsAsync(
        SqlConnection connection,
        Guid partyId,
        string idempotencyKey,
        string correlationId)
    {
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyB AND [PartyId] = @PartyId;",
                new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                new SqlParameter("@PartyId", partyId)));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyB AND [IdempotencyKeyHash] = @KeyHash;",
                new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(idempotencyKey))));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyB AND [CorrelationId] = @CorrelationId;",
                new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                new SqlParameter("@CorrelationId", correlationId)));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyA AND [PartyId] = @PartyId;",
                new SqlParameter("@CompanyA", TestCompanyIds.CompanyA),
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

    private static async Task CloneCommandExecutionAsync(
        SqlConnection connection,
        Guid companyId,
        string sourceIdempotencyKey,
        string targetIdempotencyKey,
        string originalCorrelationId,
        int schemaVersion,
        string? responseJson)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [idempotency].[CommandExecutions]
                ([Id], [CompanyId], [CommandName], [IdempotencyKeyHash],
                 [RequestHash], [ActorId], [Outcome], [HttpStatusCode],
                 [ResponseJson], [OriginalCorrelationId], [CompletedAtUtc], [SchemaVersion])
            SELECT NEWID(), [CompanyId], [CommandName], @TargetKeyHash,
                   [RequestHash], [ActorId], [Outcome], [HttpStatusCode],
                   COALESCE(@ResponseJson, [ResponseJson]), @OriginalCorrelationId,
                   SYSUTCDATETIME(), @SchemaVersion
            FROM [idempotency].[CommandExecutions]
            WHERE [CompanyId] = @CompanyId
              AND [CommandName] = N'counterparty.company-account.create.v1'
              AND [IdempotencyKeyHash] = @SourceKeyHash;
            """;
        command.Parameters.AddRange(
        [
            new SqlParameter("@CompanyId", companyId),
            new SqlParameter(
                "@SourceKeyHash",
                IdempotencyHash.ComputeKey(sourceIdempotencyKey)),
            new SqlParameter(
                "@TargetKeyHash",
                IdempotencyHash.ComputeKey(targetIdempotencyKey)),
            new SqlParameter("@OriginalCorrelationId", originalCorrelationId),
            new SqlParameter("@SchemaVersion", schemaVersion),
            new SqlParameter("@ResponseJson", (object?)responseJson ?? DBNull.Value),
        ]);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertInternalErrorWithoutReplayHeadersAsync(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        Assert.False(response.Headers.Contains("Idempotency-Original-Correlation-Id"));
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("internal_error", document.RootElement.GetProperty("code").GetString());
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

    private sealed record CreatedParty(Guid PartyId, string Code);

    private sealed record PersistenceCounts(
        int PartyCount,
        int AccountCount,
        int AuditCount,
        int ExecutionCount);
}
