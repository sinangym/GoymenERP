using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D57 (Sinan, 2026-10-27) — the customer (price) group on the Cari card, against real SQL Server through the real
/// API: the company's own list (nothing seeded; create / rename / deactivate, never delete), the group on the company
/// ACCOUNT (another company's group cannot be assigned, a passive group is never newly assigned), the critical-command
/// shape (company-wide permission, Idempotency-Key replay, version fence, one audit per real change), the read port
/// consumers use, and the refused rollback.
/// </summary>
public sealed class CounterpartyCustomerGroupSqlServerRelationalAcceptanceTests
{
    private const string GroupsPath = "/api/v1/counterparties/customer-groups";
    private const string CreatedEvent = "counterparty.customer-group.created.v1";
    private const string RenamedEvent = "counterparty.customer-group.renamed.v1";
    private const string ActiveStateEvent = "counterparty.customer-group.active-state.changed.v1";
    private const string AssignedEvent = "counterparty.company-account.customer-group.changed.v1";

    [SqlServerFact]
    public async Task The_company_list_starts_empty_and_is_created_renamed_and_deactivated_with_replay_and_audit()
    {
        await using var test = new GroupTest();
        using var admin = test.Client();

        // Nothing is seeded: the list is the company's own and starts empty.
        var empty = await ReadAsync(admin, GroupsPath);
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal(TestCompanyIds.CompanyA, empty.GetProperty("companyId").GetGuid());
        Assert.True(empty.GetProperty("viewer").GetProperty("canManage").GetBoolean());

        var wholesale = Guid.NewGuid();
        var key = NewKey(admin);
        var create = Body(new { id = wholesale, name = "  Toptancı  " });
        using (var created = await PostAsync(admin, GroupsPath, create))
        {
            Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
        }

        using (var replay = await PostAsync(admin, GroupsPath, create))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        }

        // The same key with another body is not a replay.
        using (var reused = await PostAsync(admin, GroupsPath, Body(new { id = Guid.NewGuid(), name = "Başka" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, "idempotency_conflict");
        }

        Assert.NotEqual(string.Empty, key);
        await CreateAsync(admin, Guid.NewGuid(), "Perakendeci");

        NewKey(admin);
        using (var sameName = await PostAsync(admin, GroupsPath, Body(new { id = Guid.NewGuid(), name = "Toptancı" })))
        {
            await AssertProblemAsync(sameName, HttpStatusCode.Conflict, "customer_group_name_taken");
        }

        NewKey(admin);
        using (var sameId = await PostAsync(admin, GroupsPath, Body(new { id = wholesale, name = "İmalatçı" })))
        {
            await AssertProblemAsync(sameId, HttpStatusCode.Conflict, "customer_group_exists");
        }

        foreach (var invalid in new object[]
                 {
                     new { id = Guid.Empty, name = "Boş kimlik" },
                     new { id = Guid.NewGuid(), name = "   " },
                     new { id = Guid.NewGuid(), name = new string('x', 101) },
                 })
        {
            NewKey(admin);
            using var refused = await PostAsync(admin, GroupsPath, Body(invalid));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        var list = await ReadAsync(admin, GroupsPath);
        var names = list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(new[] { "Perakendeci", "Toptancı" }, names);

        // Rename: a stale version is a conflict, the current one renames, an unknown group is 404.
        NewKey(admin);
        using (var stale = await PutAsync(admin, $"{GroupsPath}/{wholesale}/name", Body(new { expectedVersion = "2", name = "Bayi" })))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, "version_conflict");
        }

        NewKey(admin);
        using (var taken = await PutAsync(admin, $"{GroupsPath}/{wholesale}/name", Body(new { expectedVersion = "1", name = "Perakendeci" })))
        {
            await AssertProblemAsync(taken, HttpStatusCode.Conflict, "customer_group_name_taken");
        }

        NewKey(admin);
        using (var renamed = await PutAsync(admin, $"{GroupsPath}/{wholesale}/name", Body(new { expectedVersion = "1", name = "Toptancı (bölge)" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        }

        NewKey(admin);
        using (var unknown = await PutAsync(admin, $"{GroupsPath}/{Guid.NewGuid()}/name", Body(new { expectedVersion = "1", name = "X" })))
        {
            await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "not_found");
        }

        // Deactivate (passive) — never a delete; the group stays listed and its name stays reserved.
        NewKey(admin);
        using (var passive = await PutAsync(admin, $"{GroupsPath}/{wholesale}/active-state", Body(new { expectedVersion = "2", isActive = false })))
        {
            Assert.Equal(HttpStatusCode.NoContent, passive.StatusCode);
        }

        NewKey(admin);
        using (var noop = await PutAsync(admin, $"{GroupsPath}/{wholesale}/active-state", Body(new { expectedVersion = "3", isActive = false })))
        {
            Assert.Equal(HttpStatusCode.NoContent, noop.StatusCode);
        }

        var group = (await ReadAsync(admin, GroupsPath)).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == wholesale);
        Assert.Equal("Toptancı (bölge)", group.GetProperty("name").GetString());
        Assert.False(group.GetProperty("isActive").GetBoolean());
        Assert.Equal("3", group.GetProperty("version").GetString());

        Assert.Equal(2, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CustomerGroups];"));
        Assert.Equal(2, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", CreatedEvent));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", RenamedEvent));
        // The no-op deactivation wrote no second event.
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", ActiveStateEvent));
    }

    [SqlServerFact]
    public async Task The_account_group_is_assigned_read_back_cleared_and_a_passive_group_is_never_newly_assigned()
    {
        await using var test = new GroupTest();
        using var admin = test.Client();
        var party = await CreatePartyAsync(admin, "Grup Müşterisi");
        var retail = await CreateAsync(admin, Guid.NewGuid(), "Perakendeci");
        var manufacturer = await CreateAsync(admin, Guid.NewGuid(), "İmalatçı");
        var path = AccountPath(party);

        var none = await ReadAsync(admin, path);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("customerGroup").ValueKind);
        Assert.Equal("1", none.GetProperty("version").GetString());
        Assert.True(none.GetProperty("viewer").GetProperty("canAssign").GetBoolean());
        var accountId = none.GetProperty("accountId").GetGuid();

        NewKey(admin);
        var assign = Body(new { expectedVersion = "1", customerGroupId = retail });
        using (var assigned = await PutAsync(admin, path, assign))
        {
            Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        }

        using (var replay = await PutAsync(admin, path, assign))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        }

        using (var reopened = test.Client())
        {
            var view = await ReadAsync(reopened, path);
            Assert.Equal(retail, view.GetProperty("customerGroup").GetProperty("id").GetGuid());
            Assert.Equal("Perakendeci", view.GetProperty("customerGroup").GetProperty("name").GetString());
            Assert.Equal("2", view.GetProperty("version").GetString());
        }

        // The account's version fences the command (shared with the other account commands).
        NewKey(admin);
        using (var stale = await PutAsync(admin, path, Body(new { expectedVersion = "1", customerGroupId = manufacturer })))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, "version_conflict");
        }

        // A passive group keeps its existing assignment and is reported as passive…
        await SetActiveAsync(admin, retail, "1", false);
        var kept = await ReadAsync(admin, path);
        Assert.Equal(retail, kept.GetProperty("customerGroup").GetProperty("id").GetGuid());
        Assert.False(kept.GetProperty("customerGroup").GetProperty("isActive").GetBoolean());
        Assert.Equal(CounterpartyAccountCustomerGroupOutcomeV1.Found, (await test.ReadPortAsync(accountId)).Outcome);
        Assert.False((await test.ReadPortAsync(accountId)).Account!.CustomerGroupIsActive);

        // …re-sending it is a no-op, but it is never newly assigned.
        NewKey(admin);
        using (var same = await PutAsync(admin, path, Body(new { expectedVersion = "2", customerGroupId = retail })))
        {
            Assert.Equal(HttpStatusCode.NoContent, same.StatusCode);
        }

        await SetActiveAsync(admin, manufacturer, "1", false);
        NewKey(admin);
        using (var passive = await PutAsync(admin, path, Body(new { expectedVersion = "2", customerGroupId = manufacturer })))
        {
            await AssertProblemAsync(passive, HttpStatusCode.Conflict, "customer_group_inactive");
        }

        await SetActiveAsync(admin, manufacturer, "2", true);
        NewKey(admin);
        using (var moved = await PutAsync(admin, path, Body(new { expectedVersion = "2", customerGroupId = manufacturer })))
        {
            Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        }

        var port = await test.ReadPortAsync(accountId);
        Assert.Equal(CounterpartyAccountCustomerGroupOutcomeV1.Found, port.Outcome);
        Assert.Equal(manufacturer, port.Account!.CustomerGroupId);
        Assert.True(port.Account.CustomerGroupIsActive);
        Assert.Equal(party, port.Account.PartyId);
        Assert.Equal("3", port.Account.AccountVersion);

        // The property must be present: an omitted group is not a clear.
        NewKey(admin);
        using (var omitted = await PutAsync(admin, path, Body(new { expectedVersion = "3" })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, omitted.StatusCode);
        }

        NewKey(admin);
        using (var cleared = await PutAsync(admin, path, Body(new { expectedVersion = "3", customerGroupId = (Guid?)null })))
        {
            Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        }

        Assert.Equal(JsonValueKind.Null, (await ReadAsync(admin, path)).GetProperty("customerGroup").ValueKind);
        var noGroup = await test.ReadPortAsync(accountId);
        Assert.Equal(CounterpartyAccountCustomerGroupOutcomeV1.Found, noGroup.Outcome);
        Assert.Null(noGroup.Account!.CustomerGroupId);
        Assert.Null(noGroup.Account.CustomerGroupIsActive);

        // A missing account is 404 and the port answers NotFound.
        NewKey(admin);
        using (var missing = await PutAsync(admin, AccountPath(Guid.NewGuid()), Body(new { expectedVersion = "1", customerGroupId = retail })))
        {
            await AssertProblemAsync(missing, HttpStatusCode.NotFound, "not_found");
        }

        Assert.Equal(CounterpartyAccountCustomerGroupOutcomeV1.NotFound, (await test.ReadPortAsync(Guid.NewGuid())).Outcome);
        Assert.Equal(3, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", AssignedEvent));
    }

    [SqlServerFact]
    public async Task Another_companys_group_is_never_listed_renamed_or_assigned_and_the_database_refuses_it_too()
    {
        await using var test = new GroupTest();
        using var adminA = test.Client();
        using var adminB = test.Client(TestCompanyIds.CompanyB);
        var groupOfA = await CreateAsync(adminA, Guid.NewGuid(), "Toptancı");
        var party = await CreatePartyAsync(adminB, "B Müşterisi");

        Assert.Empty((await ReadAsync(adminB, GroupsPath)).GetProperty("items").EnumerateArray());

        NewKey(adminB);
        using (var rename = await PutAsync(adminB, $"{GroupsPath}/{groupOfA}/name", Body(new { expectedVersion = "1", name = "Ele geçirildi" })))
        {
            await AssertProblemAsync(rename, HttpStatusCode.NotFound, "not_found");
        }

        NewKey(adminB);
        using (var assign = await PutAsync(adminB, AccountPath(party), Body(new { expectedVersion = "1", customerGroupId = groupOfA })))
        {
            await AssertProblemAsync(assign, HttpStatusCode.UnprocessableEntity, "customer_group_not_found");
        }

        // Company A's id in company B's namespace is a separate group only if B creates its own; the same id is refused.
        NewKey(adminB);
        using (var reuse = await PostAsync(adminB, GroupsPath, Body(new { id = groupOfA, name = "Toptancı" })))
        {
            await AssertProblemAsync(reuse, HttpStatusCode.Conflict, "customer_group_exists");
        }

        // B can still use the same NAME for its own group: names are unique per company.
        await CreateAsync(adminB, Guid.NewGuid(), "Toptancı");

        // The composite foreign key refuses a direct cross-company assignment as well.
        var error = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(
            "UPDATE [counterparty].[CompanyPartyAccounts] SET [CustomerGroupId]=@Group WHERE [PartyId]=@Party;",
            party, groupOfA));
        Assert.Equal(547, error.Number);
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[CustomerGroups] WHERE [CompanyId]=@Company;"));
    }

    [SqlServerFact]
    public async Task Without_the_grants_the_list_reads_but_every_command_is_403_and_nothing_is_written()
    {
        await using var test = new GroupTest(grant: false);
        using var reader = test.Client();
        var party = await CreatePartyAsync(reader, "Yetkisiz");

        var list = await ReadAsync(reader, GroupsPath);
        Assert.False(list.GetProperty("viewer").GetProperty("canManage").GetBoolean());
        Assert.False((await ReadAsync(reader, AccountPath(party))).GetProperty("viewer").GetProperty("canAssign").GetBoolean());

        NewKey(reader);
        using (var create = await PostAsync(reader, GroupsPath, Body(new { id = Guid.NewGuid(), name = "Toptancı" })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        }

        NewKey(reader);
        using (var assign = await PutAsync(reader, AccountPath(party), Body(new { expectedVersion = "1", customerGroupId = (Guid?)null })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
        }

        // Without counterparty.parties.read the port is Denied — never "no group".
        Assert.Equal(CounterpartyAccountCustomerGroupOutcomeV1.Denied,
            (await test.ReadPortAsync(Guid.NewGuid(), granted: false)).Outcome);
        Assert.Equal(CounterpartyAccountCustomerGroupOutcomeV1.Unavailable,
            (await test.ReadPortAsync(Guid.NewGuid(), failing: true)).Outcome);
        Assert.Equal(CounterpartyAccountCustomerGroupOutcomeV1.Denied,
            (await test.ReadPortAsync(Guid.NewGuid(), company: TestCompanyIds.CompanyB)).Outcome);

        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CustomerGroups];"));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName] LIKE N'counterparty.%customer-group%';"));
    }

    [SqlServerFact]
    public async Task Rollback_is_refused_while_a_group_exists_and_allowed_on_an_empty_list()
    {
        await using var test = new GroupTest();
        using var admin = test.Client();
        var group = await CreateAsync(admin, Guid.NewGuid(), "Toptancı");

        var error = await Assert.ThrowsAnyAsync<SqlException>(() => test.MigrateCounterpartyToAsync("20260930150000_AddPartyTaxIdentity"));
        Assert.Equal(51091, error.Number);
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CustomerGroups];"));
        Assert.NotEqual(Guid.Empty, group);

        await test.ExecuteAsync("DELETE FROM [counterparty].[CustomerGroups];", Guid.Empty, Guid.Empty);
        await test.MigrateCounterpartyToAsync("20260930150000_AddPartyTaxIdentity");
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE [object_id]=OBJECT_ID(N'[counterparty].[CompanyPartyAccounts]') AND [name]=N'CustomerGroupId';"));
        await test.MigrateCounterpartyToAsync(null);
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE [object_id]=OBJECT_ID(N'[counterparty].[CompanyPartyAccounts]') AND [name]=N'CustomerGroupId';"));
    }

    private static string AccountPath(Guid party) => $"/api/v1/counterparties/{party}/company-account/customer-group";

    private static string Body(object value) => JsonSerializer.Serialize(value);

    private static async Task<Guid> CreateAsync(HttpClient client, Guid id, string name)
    {
        NewKey(client);
        using var response = await PostAsync(client, GroupsPath, Body(new { id, name }));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return id;
    }

    private static async Task SetActiveAsync(HttpClient client, Guid group, string version, bool isActive)
    {
        NewKey(client);
        using var response = await PutAsync(client, $"{GroupsPath}/{group}/active-state",
            Body(new { expectedVersion = version, isActive }));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<Guid> CreatePartyAsync(HttpClient client, string legalName)
    {
        NewKey(client);
        using var response = await client.PostAsJsonAsync("/api/v1/counterparties", new { kind = 1, legalName, roles = 1 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("code").GetString());
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string path, string body) =>
        client.PutAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string body) =>
        client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));

    private static string NewKey(HttpClient client)
    {
        var key = Guid.NewGuid().ToString("N");
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return key;
    }

    private sealed class GroupTest : IAsyncDisposable
    {
        private readonly string _baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;

        public GroupTest(bool grant = true)
        {
            Factory = new SqlServerRelationalApiFactory(_baseConnection, services =>
            {
                if (!grant)
                {
                    return;
                }

                // Test-only grants on the disposable database; no shipped seed role carries these names.
                services.PostConfigure<IdentitySeedOptions>(options =>
                {
                    var administrator = options.Roles.Single(role => role.Code == "SYSTEM_ADMINISTRATOR");
                    administrator.Permissions.Add(CounterpartyCustomerGroupPermissionsV1.Manage);
                    administrator.Permissions.Add(CounterpartyCustomerGroupPermissionsV1.Assign);
                });
            });
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public HttpClient Client(Guid? company = null, string user = TestUsers.GroupAdmin) =>
            Factory.CreateClientFor(user, company ?? TestCompanyIds.CompanyA);

        /// <summary>The consumer port on the real database, for company A and a fixed actor.</summary>
        public async Task<CounterpartyAccountCustomerGroupAnswerV1> ReadPortAsync(
            Guid accountId, bool granted = true, bool failing = false, Guid? company = null)
        {
            _ = Factory.Server;
            var context = new FixedCompanyContext(TestCompanyIds.CompanyA);
            await using var dbContext = new CounterpartyDbContext(
                new DbContextOptionsBuilder<CounterpartyDbContext>()
                    .UseSqlServer(Factory.DatabaseConnectionString)
                    .Options,
                context);
            var source = new CounterpartyAccountCustomerGroupSourceV1(
                dbContext,
                context,
                new FixedUser("customer-group-port-reader", TestCompanyIds.CompanyA, TestCompanyIds.CompanyB),
                new FixedAuthorization(granted, failing));
            return await source.GetAuthorizedAsync(company ?? TestCompanyIds.CompanyA, accountId);
        }

        public async Task MigrateCounterpartyToAsync(string? migration)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            await dbContext.GetService<IMigrator>().MigrateAsync(migration);
        }

        public async Task<T> ScalarAsync<T>(string sql, string? eventType = null)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (sql.Contains("@Event", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Event", eventType!);
            if (sql.Contains("@Company", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Company", TestCompanyIds.CompanyA);
            return Assert.IsType<T>(await command.ExecuteScalarAsync());
        }

        public async Task ExecuteAsync(string sql, Guid party, Guid group)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (sql.Contains("@Party", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Party", party);
            if (sql.Contains("@Group", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Group", group);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            Assert.Matches("^goymen_erp_ci_[a-f0-9]{32}$", Factory.DatabaseName);
            var builder = new SqlConnectionStringBuilder(_baseConnection) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class FixedCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;

        public Guid CompanyId { get; } = companyId;

        public void Set(Guid companyId) => throw new InvalidOperationException("Sabit test bağlamı.");
    }

    private sealed class FixedUser(string actorId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => actorId;

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();

        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class FixedAuthorization(bool granted, bool failing) : IErpAuthorizationService
    {
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(
            Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PermissionScope> GetPermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            if (failing)
            {
                throw new InvalidOperationException("simulated outage");
            }

            Assert.Equal(ErpPermissions.CounterpartyPartiesRead, permission);
            return Task.FromResult(granted
                ? new PermissionScope(
                    true,
                    true,
                    new HashSet<string>(StringComparer.Ordinal) { "SENTETIK-CARI-OKUMA" },
                    new HashSet<Guid>(),
                    new HashSet<Guid>(),
                    new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }
}
