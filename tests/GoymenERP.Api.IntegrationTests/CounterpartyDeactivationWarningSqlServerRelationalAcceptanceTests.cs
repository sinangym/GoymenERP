using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ALS-D99 / ALS-D100 (Sinan, 2026-11-15) against real SQL Server through the real API. The deactivation warning lists
/// the owner modules' answers (fail-closed <c>unavailable</c> while no owner port is registered), the deactivation needs
/// the acknowledgement when the warning is not clear (422, nothing stored), the audit records the warning, and the
/// group-wide command runs each company as its own command under the separate
/// <c>counterparty.parties.group-deactivate</c> permission (granted here only on the disposable database; no shipped role
/// carries it).
/// </summary>
public sealed class CounterpartyDeactivationWarningSqlServerRelationalAcceptanceTests
{
    private const string SetActiveStateCommandName = "counterparty.company-account.set-active-state.v1";
    private const string GroupCommandName = "counterparty.party.group-deactivate.v1";

    [SqlServerFact]
    public async Task Without_owner_ports_every_line_is_unavailable_and_an_unacknowledged_deactivation_is_refused_unstored()
    {
        await using var run = await Run.StartAsync();
        using var admin = run.Factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        var partyId = await run.CreatePartyAsync(admin, "SQL Pasife Alma Uyarı Cari A.Ş.");
        var accountId = await run.AccountIdAsync(TestCompanyIds.CompanyA, partyId);

        var warning = await run.ReadWarningAsync(admin, partyId);
        Assert.Equal(TestCompanyIds.CompanyA, warning.GetProperty("companyId").GetGuid());
        Assert.Equal(partyId, warning.GetProperty("partyId").GetGuid());
        Assert.Equal("1", warning.GetProperty("accountVersion").GetString());
        Assert.True(warning.GetProperty("isActive").GetBoolean());
        Assert.True(warning.GetProperty("acknowledgementRequired").GetBoolean());
        Assert.Equal(
            ["finance-open-balance", "sales-open-documents", "purchasing-open-documents"],
            warning.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("kind").GetString()));
        Assert.All(warning.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.Equal("unavailable", item.GetProperty("outcome").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("openItemCount").ValueKind);
        });

        // The reader without the status permission may not see the warning.
        using (var viewer = run.Factory.CreateClientFor(TestUsers.CompanyAViewer, TestCompanyIds.CompanyA))
        using (var forbidden = await viewer.GetAsync(Run.WarningPath(partyId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var key = NewKey();
        using (var refused = await run.PutStateAsync(admin, partyId, false, 1, "Çalışma sona erdi", key, acknowledged: false))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Equal(
                CounterpartyProblemCodesV1.DeactivationWarningAcknowledgementRequired,
                (await JsonAsync(refused)).GetProperty("code").GetString());
        }

        // Nothing was decided: the account stays active and no execution, audit or outbox row exists.
        Assert.Equal(1, await run.IsActiveAsync(accountId));
        Assert.Equal(0, await run.CommandExecutionCountAsync(TestCompanyIds.CompanyA, SetActiveStateCommandName));
        Assert.Equal(0, await run.DeactivateAuditCountAsync(TestCompanyIds.CompanyA, accountId));
        Assert.Equal(0, await run.OutboxCountAsync(TestCompanyIds.CompanyA));

        // The same key with the acknowledgement is a first execution, not a conflict.
        using (var accepted = await run.PutStateAsync(admin, partyId, false, 1, "Çalışma sona erdi", key, acknowledged: true))
        {
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
            Assert.False(accepted.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal(0, await run.IsActiveAsync(accountId));
        Assert.Equal(1, await run.OutboxCountAsync(TestCompanyIds.CompanyA));
        using (var afterJson = JsonDocument.Parse(await run.DeactivateAuditAfterJsonAsync(TestCompanyIds.CompanyA, accountId)))
        {
            var snapshot = afterJson.RootElement.GetProperty("DeactivationWarning");
            Assert.Equal("company", snapshot.GetProperty("Scope").GetString());
            Assert.True(snapshot.GetProperty("Shown").GetBoolean());
            Assert.True(snapshot.GetProperty("AcknowledgementRequired").GetBoolean());
            Assert.True(snapshot.GetProperty("Acknowledged").GetBoolean());
            Assert.Equal(3, snapshot.GetProperty("Items").GetArrayLength());
            Assert.All(snapshot.GetProperty("Items").EnumerateArray(), item =>
                Assert.Equal("unavailable", item.GetProperty("Outcome").GetString()));
        }

        // A reactivation never needs the acknowledgement.
        using (var reactivated = await run.PutStateAsync(admin, partyId, true, 2, "Yeniden çalışma", NewKey(), acknowledged: false))
        {
            Assert.Equal(HttpStatusCode.NoContent, reactivated.StatusCode);
        }

        Assert.Equal(1, await run.IsActiveAsync(accountId));
    }

    [SqlServerFact]
    public async Task Clear_owner_answers_need_no_acknowledgement_and_an_open_failing_or_malformed_answer_needs_it()
    {
        var finance = new TestExposureSource(CounterpartyDeactivationExposureKindV1.FinanceOpenBalance);
        var sales = new TestExposureSource(CounterpartyDeactivationExposureKindV1.SalesOpenDocuments);
        var purchasing = new TestExposureSource(CounterpartyDeactivationExposureKindV1.PurchasingOpenDocuments);
        await using var run = await Run.StartAsync(services =>
        {
            services.AddSingleton<ICounterpartyDeactivationExposureSourceV1>(finance);
            services.AddSingleton<ICounterpartyDeactivationExposureSourceV1>(sales);
            services.AddSingleton<ICounterpartyDeactivationExposureSourceV1>(purchasing);
        });
        using var admin = run.Factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        var partyId = await run.CreatePartyAsync(admin, "SQL Temiz Uyarı Cari A.Ş.");
        var accountId = await run.AccountIdAsync(TestCompanyIds.CompanyA, partyId);

        var clear = await run.ReadWarningAsync(admin, partyId);
        Assert.False(clear.GetProperty("acknowledgementRequired").GetBoolean());
        Assert.All(clear.GetProperty("items").EnumerateArray(), item =>
            Assert.Equal("clear", item.GetProperty("outcome").GetString()));
        Assert.All(new[] { finance, sales, purchasing }, source =>
        {
            var request = Assert.Single(source.Requests);
            Assert.Equal(TestCompanyIds.CompanyA, request.CompanyId);
            Assert.Equal(partyId, request.PartyId);
        });

        using (var deactivated = await run.PutStateAsync(admin, partyId, false, 1, "Temiz pasife alma", NewKey(), acknowledged: false))
        {
            Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        }

        using (var afterJson = JsonDocument.Parse(await run.DeactivateAuditAfterJsonAsync(TestCompanyIds.CompanyA, accountId)))
        {
            var snapshot = afterJson.RootElement.GetProperty("DeactivationWarning");
            Assert.False(snapshot.GetProperty("AcknowledgementRequired").GetBoolean());
            Assert.False(snapshot.GetProperty("Acknowledged").GetBoolean());
        }

        using (var reactivated = await run.PutStateAsync(admin, partyId, true, 2, "Yeniden çalışma", NewKey(), acknowledged: false))
        {
            Assert.Equal(HttpStatusCode.NoContent, reactivated.StatusCode);
        }

        // Sales answers open (count only), Finance fails and Purchasing answers a malformed "clear with a count".
        sales.Answer = new CounterpartyDeactivationExposureAnswerV1(CounterpartyDeactivationExposureOutcomeV1.Open, 2);
        finance.Fails = true;
        purchasing.Answer = new CounterpartyDeactivationExposureAnswerV1(CounterpartyDeactivationExposureOutcomeV1.Clear, 4);
        var open = await run.ReadWarningAsync(admin, partyId);
        Assert.True(open.GetProperty("acknowledgementRequired").GetBoolean());
        var items = open.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal("unavailable", items[0].GetProperty("outcome").GetString());
        Assert.Equal("open", items[1].GetProperty("outcome").GetString());
        Assert.Equal(2, items[1].GetProperty("openItemCount").GetInt32());
        Assert.Equal("unavailable", items[2].GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, items[2].GetProperty("openItemCount").ValueKind);

        using (var refused = await run.PutStateAsync(admin, partyId, false, 3, "Açık belge var", NewKey(), acknowledged: false))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        }

        Assert.Equal(1, await run.IsActiveAsync(accountId));
        Assert.Equal(1, await run.OutboxCountAsync(TestCompanyIds.CompanyA));

        using (var acknowledged = await run.PutStateAsync(admin, partyId, false, 3, "Açık belge var", NewKey(), acknowledged: true))
        {
            Assert.Equal(HttpStatusCode.NoContent, acknowledged.StatusCode);
        }

        Assert.Equal(0, await run.IsActiveAsync(accountId));
        Assert.Equal(2, await run.OutboxCountAsync(TestCompanyIds.CompanyA));
    }

    [SqlServerFact]
    public async Task The_group_command_and_preview_are_forbidden_without_the_separate_permission()
    {
        await using var run = await Run.StartAsync();
        using var admin = run.Factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        var partyId = await run.CreatePartyAsync(admin, "SQL Grup Yetkisiz Cari A.Ş.");
        var accountId = await run.AccountIdAsync(TestCompanyIds.CompanyA, partyId);

        using (var preview = await admin.GetAsync(Run.GroupPreviewPath(partyId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, preview.StatusCode);
        }

        using (var command = await run.PostGroupAsync(
                   admin,
                   partyId,
                   [new CounterpartyGroupDeactivationTarget(TestCompanyIds.CompanyA, 1)],
                   "Grup pasife alma",
                   NewKey()))
        {
            Assert.Equal(HttpStatusCode.Forbidden, command.StatusCode);
        }

        Assert.Equal(1, await run.IsActiveAsync(accountId));
        Assert.Equal(0, await run.CommandExecutionCountAsync(TestCompanyIds.CompanyA, GroupCommandName));
        Assert.Equal(0, await run.OutboxCountAsync(TestCompanyIds.CompanyA));
    }

    [SqlServerFact]
    public async Task The_group_command_runs_each_company_as_its_own_command_and_reports_every_outcome()
    {
        await using var run = await Run.StartAsync(services =>
        {
            foreach (var kind in Enum.GetValues<CounterpartyDeactivationExposureKindV1>())
            {
                services.AddSingleton<ICounterpartyDeactivationExposureSourceV1>(new TestExposureSource(kind));
            }

            // Test-only grant on the disposable database; no shipped seed role is changed.
            services.PostConfigure<IdentitySeedOptions>(options =>
                options.Roles.Single(role => role.Code == "SYSTEM_ADMINISTRATOR")
                    .Permissions.Add(ErpPermissions.CounterpartyPartiesGroupDeactivate));
        });
        using var adminA = run.Factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        using var adminB = run.Factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
        var partyId = await run.CreatePartyAsync(adminA, "SQL Grup Pasife Alma Cari A.Ş.");
        await run.LinkAsync(adminB, partyId);
        var accountA = await run.AccountIdAsync(TestCompanyIds.CompanyA, partyId);
        var accountB = await run.AccountIdAsync(TestCompanyIds.CompanyB, partyId);

        using (var previewResponse = await adminA.GetAsync(Run.GroupPreviewPath(partyId)))
        {
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = await JsonAsync(previewResponse);
            Assert.Equal(partyId, preview.GetProperty("partyId").GetGuid());
            var companies = preview.GetProperty("companies").EnumerateArray().ToArray();
            Assert.Equal(
                [TestCompanyIds.CompanyA, TestCompanyIds.CompanyB],
                companies.Select(company => company.GetProperty("companyId").GetGuid()));
            Assert.All(companies, company =>
            {
                Assert.True(company.GetProperty("groupPermissionGranted").GetBoolean());
                Assert.False(company.GetProperty("exposure").GetProperty("acknowledgementRequired").GetBoolean());
                Assert.Equal("1", company.GetProperty("exposure").GetProperty("accountVersion").GetString());
            });
        }

        // Only company A is named: A is deactivated, B is reported as still active and not requested.
        var groupKey = NewKey();
        var onlyA = new[] { new CounterpartyGroupDeactivationTarget(TestCompanyIds.CompanyA, 1) };
        using (var first = await run.PostGroupAsync(adminA, partyId, onlyA, "Grup kararı", groupKey))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var outcomes = (await JsonAsync(first)).GetProperty("companies").EnumerateArray().ToArray();
            Assert.Equal(2, outcomes.Length);
            AssertOutcome(outcomes[0], TestCompanyIds.CompanyA, "deactivated", replayed: false, currentVersion: null);
            AssertOutcome(outcomes[1], TestCompanyIds.CompanyB, "not-requested", replayed: false, currentVersion: null);
        }

        Assert.Equal(0, await run.IsActiveAsync(accountA));
        Assert.Equal(1, await run.IsActiveAsync(accountB));
        Assert.Equal(1, await run.OutboxCountAsync(TestCompanyIds.CompanyA));
        Assert.Equal(0, await run.OutboxCountAsync(TestCompanyIds.CompanyB));
        Assert.Equal(1, await run.CommandExecutionCountAsync(TestCompanyIds.CompanyA, GroupCommandName));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyId AND [EntityId] = @EntityId AND [Operation] = N'Deactivate' AND [PermissionCode] = @Permission;",
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
            new SqlParameter("@EntityId", accountA.ToString("D")),
            new SqlParameter("@Permission", ErpPermissions.CounterpartyPartiesGroupDeactivate)));
        using (var afterJson = JsonDocument.Parse(await run.DeactivateAuditAfterJsonAsync(TestCompanyIds.CompanyA, accountA)))
        {
            Assert.Equal("group", afterJson.RootElement.GetProperty("DeactivationWarning").GetProperty("Scope").GetString());
        }

        // The same key and body replays company A's stored answer and writes nothing new.
        using (var replay = await run.PostGroupAsync(adminA, partyId, onlyA, "Grup kararı", groupKey))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            var outcomes = (await JsonAsync(replay)).GetProperty("companies").EnumerateArray().ToArray();
            AssertOutcome(outcomes[0], TestCompanyIds.CompanyA, "deactivated", replayed: true, currentVersion: null);
        }

        Assert.Equal(1, await run.OutboxCountAsync(TestCompanyIds.CompanyA));
        Assert.Equal(1, await run.CommandExecutionCountAsync(TestCompanyIds.CompanyA, GroupCommandName));

        // A stale version in B is that company's version conflict (its current version is reported).
        using (var stale = await run.PostGroupAsync(
                   adminA,
                   partyId,
                   [new CounterpartyGroupDeactivationTarget(TestCompanyIds.CompanyB, 9)],
                   "Grup kararı",
                   NewKey()))
        {
            Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
            var outcomes = (await JsonAsync(stale)).GetProperty("companies").EnumerateArray().ToArray();
            AssertOutcome(Assert.Single(outcomes), TestCompanyIds.CompanyB, "version-conflict", replayed: false, currentVersion: "1");
        }

        Assert.Equal(1, await run.IsActiveAsync(accountB));

        // Both named: A is already passive (no second event), B is deactivated with its own event.
        using (var both = await run.PostGroupAsync(
                   adminA,
                   partyId,
                   [
                       new CounterpartyGroupDeactivationTarget(TestCompanyIds.CompanyB, 1),
                       new CounterpartyGroupDeactivationTarget(TestCompanyIds.CompanyA, 2),
                   ],
                   "Grup kararı",
                   NewKey()))
        {
            Assert.Equal(HttpStatusCode.OK, both.StatusCode);
            var outcomes = (await JsonAsync(both)).GetProperty("companies").EnumerateArray().ToArray();
            Assert.Equal(2, outcomes.Length);
            AssertOutcome(outcomes[0], TestCompanyIds.CompanyA, "already-passive", replayed: false, currentVersion: null);
            AssertOutcome(outcomes[1], TestCompanyIds.CompanyB, "deactivated", replayed: false, currentVersion: null);
        }

        Assert.Equal(0, await run.IsActiveAsync(accountB));
        Assert.Equal(1, await run.OutboxCountAsync(TestCompanyIds.CompanyA));
        Assert.Equal(1, await run.OutboxCountAsync(TestCompanyIds.CompanyB));

        // A duplicate company and an empty reason are refused before any company runs.
        using (var duplicate = await run.PostGroupAsync(
                   adminA,
                   partyId,
                   [
                       new CounterpartyGroupDeactivationTarget(TestCompanyIds.CompanyB, 3),
                       new CounterpartyGroupDeactivationTarget(TestCompanyIds.CompanyB, 3),
                   ],
                   "Grup kararı",
                   NewKey()))
        {
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        }

        using (var blank = await run.PostGroupAsync(
                   adminA,
                   partyId,
                   [new CounterpartyGroupDeactivationTarget(TestCompanyIds.CompanyB, 3)],
                   "   ",
                   NewKey()))
        {
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        }
    }

    private static void AssertOutcome(
        JsonElement outcome,
        Guid companyId,
        string expected,
        bool replayed,
        string? currentVersion)
    {
        Assert.Equal(companyId, outcome.GetProperty("companyId").GetGuid());
        Assert.Equal(expected, outcome.GetProperty("outcome").GetString());
        Assert.Equal(replayed, outcome.GetProperty("replayed").GetBoolean());
        if (currentVersion is null)
        {
            Assert.Equal(JsonValueKind.Null, outcome.GetProperty("currentVersion").ValueKind);
        }
        else
        {
            Assert.Equal(currentVersion, outcome.GetProperty("currentVersion").GetString());
        }
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string NewKey() => $"cari-uyari-{Guid.NewGuid():N}";

    /// <summary>A test owner port: answers clear unless told otherwise, and records what it was asked.</summary>
    private sealed class TestExposureSource(CounterpartyDeactivationExposureKindV1 kind)
        : ICounterpartyDeactivationExposureSourceV1
    {
        private readonly List<CounterpartyDeactivationExposureRequestV1> _requests = [];
        private readonly Lock _gate = new();

        public CounterpartyDeactivationExposureKindV1 Kind { get; } = kind;

        public CounterpartyDeactivationExposureAnswerV1 Answer { get; set; } =
            CounterpartyDeactivationExposureAnswerV1.Clear;

        public bool Fails { get; set; }

        public IReadOnlyList<CounterpartyDeactivationExposureRequestV1> Requests
        {
            get
            {
                lock (_gate)
                {
                    return _requests.ToArray();
                }
            }
        }

        public Task<CounterpartyDeactivationExposureAnswerV1> ReadAsync(
            CounterpartyDeactivationExposureRequestV1 request,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _requests.Add(request);
            }

            if (Fails)
            {
                throw new InvalidOperationException("Test sahibi kaynağı yanıt veremedi.");
            }

            return Task.FromResult(Answer);
        }
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string _baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            _baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public static string WarningPath(Guid partyId) =>
            $"/api/v1/counterparties/{partyId}/company-account/deactivation-warning";

        public static string GroupPreviewPath(Guid partyId) =>
            $"/api/v1/counterparties/{partyId}/group-deactivation-preview";

        public static async Task<Run> StartAsync(Action<IServiceCollection>? configureServices = null)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var run = new Run(
                baseConnectionString,
                new SqlServerRelationalApiFactory(baseConnectionString, configureServices));
            using var health = run.Factory.CreateClient();
            using var live = await health.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            return run;
        }

        public async Task<Guid> CreatePartyAsync(HttpClient client, string name)
        {
            SetRequestIdentity(client, NewKey());
            using var response = await client.PostAsJsonAsync(
                "/api/v1/counterparties",
                new CreateCounterparty(PartyKind.LegalEntity, name, null, PartyRole.Customer));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await JsonAsync(response)).GetProperty("id").GetGuid();
        }

        public async Task LinkAsync(HttpClient client, Guid partyId)
        {
            SetRequestIdentity(client, NewKey());
            using var response = await client.PostAsJsonAsync(
                $"/api/v1/counterparties/{partyId}/company-account",
                new CreateCompanyPartyAccount(PartyRole.Customer));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        public Task<Guid> AccountIdAsync(Guid companyId, Guid partyId) => ScalarAsync<Guid>(
            "SELECT [Id] FROM [counterparty].[CompanyPartyAccounts] WHERE [CompanyId] = @CompanyId AND [PartyId] = @PartyId;",
            new SqlParameter("@CompanyId", companyId),
            new SqlParameter("@PartyId", partyId));

        public async Task<JsonElement> ReadWarningAsync(HttpClient client, Guid partyId)
        {
            using var response = await client.GetAsync(WarningPath(partyId));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await JsonAsync(response);
        }

        public Task<HttpResponseMessage> PutStateAsync(
            HttpClient client,
            Guid partyId,
            bool isActive,
            long expectedVersion,
            string reason,
            string idempotencyKey,
            bool acknowledged)
        {
            SetRequestIdentity(client, idempotencyKey);
            return client.PutAsJsonAsync(
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                new SetCompanyPartyAccountActiveState(isActive, expectedVersion, reason, acknowledged));
        }

        public Task<HttpResponseMessage> PostGroupAsync(
            HttpClient client,
            Guid partyId,
            IReadOnlyList<CounterpartyGroupDeactivationTarget> targets,
            string reason,
            string idempotencyKey)
        {
            SetRequestIdentity(client, idempotencyKey);
            return client.PostAsJsonAsync(
                $"/api/v1/counterparties/{partyId}/group-deactivation",
                new GroupDeactivateCounterparty(targets, reason, ExposureWarningAcknowledged: true));
        }

        public Task<int> IsActiveAsync(Guid accountId) => ScalarAsync<int>(
            "SELECT CONVERT(int, [IsActive]) FROM [counterparty].[CompanyPartyAccounts] WHERE [Id] = @Id;",
            new SqlParameter("@Id", accountId));

        public Task<int> OutboxCountAsync(Guid companyId) => ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [CompanyId] = @CompanyId;",
            new SqlParameter("@CompanyId", companyId));

        public Task<int> CommandExecutionCountAsync(Guid companyId, string commandName) => ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId AND [CommandName] = @CommandName;",
            new SqlParameter("@CompanyId", companyId),
            new SqlParameter("@CommandName", commandName));

        public Task<int> DeactivateAuditCountAsync(Guid companyId, Guid accountId) => ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyId AND [EntityId] = @EntityId AND [Operation] = N'Deactivate';",
            new SqlParameter("@CompanyId", companyId),
            new SqlParameter("@EntityId", accountId.ToString("D")));

        public Task<string> DeactivateAuditAfterJsonAsync(Guid companyId, Guid accountId) => ScalarAsync<string>(
            "SELECT TOP (1) [AfterJson] FROM [audit].[AuditEvents] WHERE [CompanyId] = @CompanyId AND [EntityId] = @EntityId AND [Operation] = N'Deactivate' ORDER BY [EntityVersionAfter] DESC;",
            new SqlParameter("@CompanyId", companyId),
            new SqlParameter("@EntityId", accountId.ToString("D")));

        public async Task<T> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddRange(parameters);
            return Assert.IsType<T>(await command.ExecuteScalarAsync());
        }

        public async ValueTask DisposeAsync()
        {
            var databaseName = Factory.DatabaseName;
            await Factory.DisposeAsync();
            var builder = new SqlConnectionStringBuilder(_baseConnectionString) { InitialCatalog = "master" };
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

        private static void SetRequestIdentity(HttpClient client, string idempotencyKey)
        {
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            client.DefaultRequestHeaders.Add("X-Correlation-Id", $"cari-uyari-correlation-{Guid.NewGuid():N}");
        }
    }
}
