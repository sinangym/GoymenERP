using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-024 (NS-01) against a REAL SQL Server through the REAL host: with no recorded version the setting is the decided default
/// (the preparer may not approve) and the read port answers it with version 0; a reasoned record is one save (row + execution
/// + audit), the same key replays, a changed body under the same key is 409, a stale version 409, an unchanged setting a
/// stored no-op without audit, restoring the default is a new version, another company sees nothing and its port refuses a
/// foreign company id, 403 without the name, and the migration's Down refuses (51622) while any version exists. KIM-D39: the
/// relaxation is recorded only while the company has exactly one active user (otherwise 409, stored for its key); a second
/// active user makes the read port answer the default at once and the administration's read close it durably with one
/// system version and one system audit event. Every grant is a fresh synthetic role; every reason is SENTETIK; every value in
/// the requests is test data, not a policy choice.
/// </summary>
public sealed class SettingsSegregationOfDutiesPolicySqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Root = SettingsSegregationOfDutiesPolicyEndpoints.SegregationOfDutiesPolicyPath;
    private const string Versions = Root + "/versions";
    private const string Migration = "AddSegregationOfDutiesPolicyVersions";
    private const string Manage = SettingsSegregationOfDutiesPolicyPermissionManifestV1.Manage;
    private const string RecordedEvent = "settings.segregation-of-duties.recorded.v1";
    private const string ClosedAutomaticallyEvent = "settings.segregation-of-duties.closed-automatically.v1";
    private const string CountRows = "SELECT COUNT(*) FROM [settings].[SegregationOfDutiesPolicyVersions]";

    [SqlServerFact]
    public async Task Segregation_of_duties_record_replay_isolation_audit_provider_and_Down_guard_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var (editorSubject, editorActor) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SettingsExchangeRatePermissionManifestV1.Read);
        var (editorBSubject, editorBActor) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Manage);
        using var editor = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        using var editorB = run.Client(editorBSubject, CompanyB);
        var relax = new RecordSegregationOfDutiesPolicyRequestV1("0", true, "SENTETIK tek kişilik şirket gevşetmesi");

        // Without the name: 403 (code-less); nothing is written.
        using (var forbidden = await viewer.GetAsync(Root))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var forbidden = await viewer.SendAsync(Json(HttpMethod.Post, Versions, "k-forbidden", relax)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        // KIM-D39: the relaxation needs a company run by one person; the editor is made the only active member of A.
        await run.MakeSoleActiveMemberAsync(CompanyA, editorActor);

        // No recorded version: the decided default (preparer may not approve), version "0"; the port answers it, version 0.
        var empty = await run.JsonAsync(editor, Root);
        Assert.False(empty.GetProperty("preparerMayApprove").GetBoolean());
        Assert.Equal("0", empty.GetProperty("version").GetString());
        Assert.Empty(empty.GetProperty("versions").EnumerateArray());
        Assert.True(empty.GetProperty("relaxationPermitted").GetBoolean());
        await run.AssertPortAnswersAsync(CompanyA, editorActor, preparerMayApprove: false, version: 0);

        // A missing choice, a malformed version or a blank reason is refused before anything is stored (not replayed).
        using (var missing = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-missing",
                   relax with { PreparerMayApprove = null })))
        {
            await AssertProblemAsync(missing, HttpStatusCode.BadRequest, SegregationOfDutiesPolicyProblemCodesV1.RequestInvalid);
            Assert.False(missing.Headers.Contains("Idempotency-Replayed"));
        }

        using (var badVersion = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-version",
                   relax with { ExpectedVersion = "00" })))
        {
            await AssertProblemAsync(badVersion, HttpStatusCode.BadRequest, SegregationOfDutiesPolicyProblemCodesV1.RequestInvalid);
        }

        using (var noReason = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-reason", relax with { Reason = " " })))
        {
            await AssertProblemAsync(noReason, HttpStatusCode.BadRequest, SegregationOfDutiesPolicyProblemCodesV1.ReasonRequired);
        }

        // Recording the default itself on an empty store is a stored no-op: no row, no audit.
        await run.CommandAsync(editor, Versions, "k-default-noop", relax with { PreparerMayApprove = false },
            HttpStatusCode.NoContent);
        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e", ("@e", RecordedEvent)));

        // The relaxation: one row + one audit event; the same key replays; a changed body under it is 409.
        await run.CommandAsync(editor, Versions, "k-r-1", relax, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, Versions, "k-r-1", relax, HttpStatusCode.NoContent, replayed: true);
        using (var reused = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-r-1",
                   relax with { Reason = "SENTETIK başka gerekçe" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, SegregationOfDutiesPolicyProblemCodesV1.IdempotencyConflict);
        }

        using (var stale = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-r-stale", relax)))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, SegregationOfDutiesPolicyProblemCodesV1.VersionConflict);
        }

        using (var replayedStale = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-r-stale", relax)))
        {
            await AssertProblemAsync(replayedStale, HttpStatusCode.Conflict, SegregationOfDutiesPolicyProblemCodesV1.VersionConflict);
            Assert.True(replayedStale.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SegregationOfDutiesPolicyVersions] WHERE [CompanyId] = @c AND [Version] = 1 "
            + "AND [PreparerMayApprove] = 1 AND [RecordedBy] = @a", ("@c", CompanyA), ("@a", editorActor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c AND [PermissionCode] = @p "
            + "AND [EntityVersionBefore] IS NULL AND [EntityVersionAfter] = 1", ("@e", RecordedEvent), ("@c", CompanyA), ("@p", Manage)));

        // The same setting again under a new key: a stored no-op (204), no row, no audit.
        await run.CommandAsync(editor, Versions, "k-noop", relax with { ExpectedVersion = "1" }, HttpStatusCode.NoContent);
        Assert.Equal(1, await run.ScalarAsync<int>(CountRows));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c", ("@e", RecordedEvent),
            ("@c", CompanyA)));

        var relaxed = await run.JsonAsync(editor, Root);
        Assert.True(relaxed.GetProperty("preparerMayApprove").GetBoolean());
        Assert.Equal("1", relaxed.GetProperty("version").GetString());
        var first = Assert.Single(relaxed.GetProperty("versions").EnumerateArray());
        Assert.Equal(("1", true, "SENTETIK tek kişilik şirket gevşetmesi", false),
            (first.GetProperty("version").GetString(), first.GetProperty("preparerMayApprove").GetBoolean(),
                first.GetProperty("reason").GetString(), first.GetProperty("recordedAutomatically").GetBoolean()));
        await run.AssertPortAnswersAsync(CompanyA, editorActor, preparerMayApprove: true, version: 1);

        // Restoring the default is a new version (the history keeps the relaxation).
        await run.CommandAsync(editor, Versions, "k-r-2", new RecordSegregationOfDutiesPolicyRequestV1("1", false,
            "SENTETIK ikinci kişi katıldı"), HttpStatusCode.NoContent);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c AND [EntityVersionBefore] = 1 "
            + "AND [EntityVersionAfter] = 2", ("@e", RecordedEvent), ("@c", CompanyA)));
        var restored = await run.JsonAsync(editor, Root);
        Assert.False(restored.GetProperty("preparerMayApprove").GetBoolean());
        Assert.Equal(new[] { "2", "1" }, restored.GetProperty("versions").EnumerateArray()
            .Select(version => version.GetProperty("version").GetString() ?? string.Empty).ToArray());
        await run.AssertPortAnswersAsync(CompanyA, editorActor, preparerMayApprove: false, version: 2);

        // Another company sees nothing of A's, and a port asked for a foreign company refuses.
        var other = await run.JsonAsync(editorB, Root);
        Assert.False(other.GetProperty("preparerMayApprove").GetBoolean());
        Assert.Equal("0", other.GetProperty("version").GetString());
        Assert.Empty(other.GetProperty("versions").EnumerateArray());
        await run.AssertPortAnswersAsync(CompanyB, editorBActor, preparerMayApprove: false, version: 0);
        await using (var port = run.Port(CompanyB, editorBActor))
        {
            var refused = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
                port.Value.GetSegregationOfDutiesPolicyAsync(CompanyA));
            Assert.Equal(SegregationOfDutiesPolicyProblemCodesV1.AccessDenied, refused.Code);
        }

        // The rollback refuses while any row exists (forward fix); the rows stay. Only an empty store passes.
        var guard = Assert.IsType<SqlOperation>(new AddSegregationOfDutiesPolicyVersions().DownOperations[0]).Sql;
        var refusedRollback = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51622, refusedRollback.Number);
        Assert.Equal(2, await run.ScalarAsync<int>(CountRows));
        await run.ExecuteAsync("DELETE FROM [settings].[SegregationOfDutiesPolicyVersions];");
        await run.ExecuteAsync(guard);
    }

    [SqlServerFact]
    public async Task Segregation_of_duties_relaxation_needs_a_single_active_user_and_a_second_user_closes_it_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var (editorSubject, editorActor) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage);
        await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SettingsExchangeRatePermissionManifestV1.Read);
        using var editor = run.Client(editorSubject, CompanyA);
        var relax = new RecordSegregationOfDutiesPolicyRequestV1("0", true, "SENTETIK tek kişilik şirket gevşetmesi");

        // Another active user in the company: the relaxation is refused (409, stored and replayed under its key); no row,
        // no audit. The view says it is not permitted now.
        var shared = await run.JsonAsync(editor, Root);
        Assert.False(shared.GetProperty("relaxationPermitted").GetBoolean());
        Assert.False(shared.GetProperty("preparerMayApprove").GetBoolean());
        using (var refused = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-shared", relax)))
        {
            await AssertProblemAsync(refused, HttpStatusCode.Conflict,
                SegregationOfDutiesPolicyProblemCodesV1.RelaxationRequiresSingleUser);
            Assert.False(refused.Headers.Contains("Idempotency-Replayed"));
        }

        using (var replayedRefusal = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-shared", relax)))
        {
            await AssertProblemAsync(replayedRefusal, HttpStatusCode.Conflict,
                SegregationOfDutiesPolicyProblemCodesV1.RelaxationRequiresSingleUser);
            Assert.True(replayedRefusal.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e", ("@e", RecordedEvent)));

        // The editor alone: the relaxation is recorded and answered.
        await run.MakeSoleActiveMemberAsync(CompanyA, editorActor);
        Assert.True((await run.JsonAsync(editor, Root)).GetProperty("relaxationPermitted").GetBoolean());
        await run.CommandAsync(editor, Versions, "k-single", relax, HttpStatusCode.NoContent);
        await run.AssertPortAnswersAsync(CompanyA, editorActor, preparerMayApprove: true, version: 1);

        // A second active user: the read port answers the default at once and writes nothing.
        await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SettingsExchangeRatePermissionManifestV1.Read);
        await run.AssertPortAnswersAsync(CompanyA, editorActor, preparerMayApprove: false, version: 1);
        Assert.Equal(1, await run.ScalarAsync<int>(CountRows));

        // The administration's read closes it durably: one system version and one system audit event.
        var closed = await run.JsonAsync(editor, Root);
        Assert.False(closed.GetProperty("preparerMayApprove").GetBoolean());
        Assert.False(closed.GetProperty("relaxationPermitted").GetBoolean());
        Assert.Equal("2", closed.GetProperty("version").GetString());
        var history = closed.GetProperty("versions").EnumerateArray().ToArray();
        Assert.Equal(2, history.Length);
        Assert.Equal(("2", false, true, SegregationOfDutiesRuleV1.AutomaticCloseActorId),
            (history[0].GetProperty("version").GetString(), history[0].GetProperty("preparerMayApprove").GetBoolean(),
                history[0].GetProperty("recordedAutomatically").GetBoolean(), history[0].GetProperty("recordedBy").GetString()));
        Assert.Equal(("1", true, false),
            (history[1].GetProperty("version").GetString(), history[1].GetProperty("preparerMayApprove").GetBoolean(),
                history[1].GetProperty("recordedAutomatically").GetBoolean()));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SegregationOfDutiesPolicyVersions] WHERE [CompanyId] = @c AND [Version] = 2 "
            + "AND [PreparerMayApprove] = 0 AND [RecordedBy] = @a", ("@c", CompanyA), ("@a", SegregationOfDutiesRuleV1.AutomaticCloseActorId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c AND [ActorType] = @t "
            + "AND [ActorId] = @a AND [EntityVersionBefore] = 1 AND [EntityVersionAfter] = 2",
            ("@e", ClosedAutomaticallyEvent), ("@c", CompanyA), ("@t", AuditActorTypes.System),
            ("@a", SegregationOfDutiesRuleV1.AutomaticCloseActorId)));

        // Reading again closes nothing more; the port answers the system version.
        Assert.Equal("2", (await run.JsonAsync(editor, Root)).GetProperty("version").GetString());
        Assert.Equal(2, await run.ScalarAsync<int>(CountRows));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e", ("@e", ClosedAutomaticallyEvent)));
        await run.AssertPortAnswersAsync(CompanyA, editorActor, preparerMayApprove: false, version: 2);

        // With several users again the relaxation stays refused under a new key.
        using (var again = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-again", relax with { ExpectedVersion = "2" })))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict,
                SegregationOfDutiesPolicyProblemCodesV1.RelaxationRequiresSingleUser);
        }
    }

    [SqlServerFact]
    public async Task The_segregation_of_duties_migration_Down_rolls_back_and_reapplies_freely_on_an_empty_store_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        await using var context = run.SettingsContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, name => name.EndsWith("_" + Migration, StringComparison.Ordinal));
        Assert.True(index > 0, "the migration is applied after an earlier Settings migration");
        await context.GetService<IMigrator>().MigrateAsync(applied[index - 1]);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'SegregationOfDutiesPolicyVersions' AND SCHEMA_NAME([schema_id]) = N'settings'"));
        await context.GetService<IMigrator>().MigrateAsync(applied[^1]);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'SegregationOfDutiesPolicyVersions' AND SCHEMA_NAME([schema_id]) = N'settings'"));
    }

    private static HttpRequestMessage Json(HttpMethod method, string path, string key, object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"segregation-of-duties-{key}");
        return request;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(status == response.StatusCode, $"{response.StatusCode}: {body}");
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private sealed class FixedActor(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; } = userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid candidate) => AllowedCompanyIds.Contains(candidate);
    }

    private sealed class PortScope<T>(IAsyncDisposable owner, T value) : IAsyncDisposable
    {
        public T Value { get; } = value;

        public ValueTask DisposeAsync() => owner.DisposeAsync();
    }

    private sealed class Owners(SettingsDbContext settings, IdentityDbContext identity) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await settings.DisposeAsync();
            await identity.DisposeAsync();
        }
    }

    /// <summary>The REAL host over a fresh database, with Identity grants for the HTTP routes.</summary>
    private sealed class HostRun : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private HostRun(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public static async Task<HostRun> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, _ => { });
            var run = new HostRun(baseConnectionString, factory);
            using var client = run.Factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        public async Task CommandAsync(HttpClient client, string path, string key, object body, HttpStatusCode expected,
            bool replayed = false)
        {
            using var response = await client.SendAsync(Json(HttpMethod.Post, path, key, body));
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(expected == response.StatusCode, $"{response.StatusCode}: {text}");
            Assert.Equal(replayed, response.Headers.Contains("Idempotency-Replayed"));
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
            return body;
        }

        public SettingsDbContext SettingsContext(Guid company) => NewSettingsContext(company, out _);

        /// <summary>
        /// The read port as a consumer module composes it: the Settings provider over its own context, asking the Identity
        /// owner (its own context, through the host adapter) for the single active user check (KIM-D39).
        /// </summary>
        public PortScope<ISettingsSegregationOfDutiesPolicyProviderV1> Port(Guid company, string actorId)
        {
            var database = NewSettingsContext(company, out var companyContext);
            var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "identity"))
                .Options);
            var actor = new FixedActor(actorId, company);
            var activeUsers = new SettingsCompanyActiveUsersHost(new UserDirectoryReadPortV1(identity, companyContext, actor));
            return new PortScope<ISettingsSegregationOfDutiesPolicyProviderV1>(new Owners(database, identity),
                new SettingsSegregationOfDutiesPolicyProviderV1(database, companyContext, actor, activeUsers));
        }

        public async Task AssertPortAnswersAsync(Guid company, string actorId, bool preparerMayApprove, long version)
        {
            await using var port = Port(company, actorId);
            var policy = await port.Value.GetSegregationOfDutiesPolicyAsync(company);
            Assert.Equal((company, preparerMayApprove, version), (policy.CompanyId, policy.PreparerMayApprove, policy.Version));
        }

        public async Task<(string Subject, string ActorId)> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK görev ayrılığı kullanıcısı", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer,
                    developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SOD-{roleId:N}", "SENTETIK görev ayrılığı rolü", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null,
                approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return (developmentUser, actor.ToString("D"));
        }

        /// <summary>
        /// Leaves <paramref name="actorId"/> the company's only active member: every other active assignment in the company
        /// (seeded or granted) is marked expired. A later grant makes another user active again.
        /// </summary>
        public async Task MakeSoleActiveMemberAsync(Guid company, string actorId)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "UPDATE [identity].[RoleAssignments] SET [Status] = N'Expired' "
                + "WHERE [CompanyId] = @c AND [UserId] <> @u AND [Status] = N'Active';", connection);
            command.Parameters.AddWithValue("@c", company);
            command.Parameters.AddWithValue("@u", Guid.Parse(actorId));
            await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
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

        private SettingsDbContext NewSettingsContext(Guid company, out CompanyContext companyContext)
        {
            companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema))
                .Options, companyContext);
        }
    }
}
