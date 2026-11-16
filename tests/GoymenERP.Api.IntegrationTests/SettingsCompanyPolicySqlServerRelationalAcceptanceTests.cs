using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsCompanyPolicySqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Root = "/api/v1/settings/company-policies/finance-opening-period";
    private const string Manage = SettingsCompanyPolicyPermissionManifestV1.FinanceOpeningPeriodManage;

    [SqlServerFact]
    public async Task Opening_period_versions_preserve_company_fences_replay_concurrency_audit_and_forward_fix_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage);
        var viewerSubject = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, ErpPermissions.SalesDraftsRead);
        using var editor = run.Client(subject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        var guard = Assert.IsType<SqlOperation>(new AddCompanyPolicyVersions().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        using (var denied = await viewer.GetAsync(Root)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var initial = await run.ReadAsync(editor, Root);
        Assert.Equal((CompanyA, FinanceOpeningPeriodStateV1.NotRecorded, "0"), (initial.CompanyId, initial.State, initial.Version));
        Assert.Empty(initial.Versions);
        var request = new RecordFinanceOpeningPeriodRequestV1("0", true, "SENTETIK dönem açılışı");
        using (var denied = await viewer.SendAsync(Json(Root + "/versions", "denied", request))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await run.CommandAsync(editor, Root + "/versions", "open", request);
        await run.CommandAsync(editor, Root + "/versions", "open", request, replay: true);
        using (var mismatch = await editor.SendAsync(Json(Root + "/versions", "open", request with { Open = false })))
            await ProblemAsync(mismatch, HttpStatusCode.Conflict, CompanyPolicyProblemCodesV1.IdempotencyConflict);
        using (var stale = await editor.SendAsync(Json(Root + "/versions", "stale", request)))
            await ProblemAsync(stale, HttpStatusCode.Conflict, CompanyPolicyProblemCodesV1.VersionConflict);
        await using (var port = run.Port(CompanyA))
        {
            var opened = await port.Value.GetFinancePolicyAsync(CompanyA);
            Assert.Equal((true, 1L), (opened.OpeningPeriodOpen, opened.Version));
            var foreign = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => port.Value.GetFinancePolicyAsync(CompanyB));
            Assert.Equal(CompanyPolicyProblemCodesV1.AccessDenied, foreign.Code);
        }
        await using (var port = run.Port(CompanyB))
        {
            var missing = await port.Value.GetFinancePolicyAsync(CompanyB);
            Assert.Equal((false, FinanceOpeningPeriodStateV1.NotRecorded, 0L), (missing.OpeningPeriodOpen, missing.OpeningPeriod, missing.Version));
        }
        await run.CommandAsync(editor, Root + "/versions", "close", request with { ExpectedVersion = "1", Open = false });
        var closed = await run.ReadAsync(editor, Root);
        Assert.Equal((FinanceOpeningPeriodStateV1.Closed, "2", 2), (closed.State, closed.Version, closed.Versions.Count));
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.FinanceOpeningPeriod'"));
        await using (var port = run.Port(CompanyA)) Assert.False((await port.Value.GetFinancePolicyAsync(CompanyA)).OpeningPeriodOpen);
        var invalid = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync($"INSERT INTO [settings].[CompanyPolicyVersions] ([CompanyId],[PolicyKey],[Version],[BooleanValue],[RecordedAtUtc],[RecordedBy],[Reason]) VALUES ('{CompanyA:D}','finance.opening-period',3,NULL,SYSUTCDATETIME(),N'SENTETIK',N'SENTETIK')"));
        Assert.Equal(547, invalid.Number);
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51853, refused.Number);
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CompanyPolicyVersions]"));
    }
    private static HttpRequestMessage Json(string path, string key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"company-policy-{key}");
        return request;
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    private sealed class FixedActor(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK-reference-reader";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid candidate) => AllowedCompanyIds.Contains(candidate);
    }

    private sealed class PortScope(SettingsDbContext database, ISettingsFinancePolicyProviderV1 value) : IAsyncDisposable
    {
        public ISettingsFinancePolicyProviderV1 Value { get; } = value;
        public ValueTask DisposeAsync() => database.DisposeAsync();
    }

    private sealed class HostRun(string baseConnectionString, SqlServerRelationalApiFactory factory) : IAsyncDisposable
    {
        public static async Task<HostRun> StartAsync()
        {
            var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var run = new HostRun(connection, new SqlServerRelationalApiFactory(connection));
            using var client = run.Client(TestUsers.CompanyA, CompanyA);
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        public async Task<FinanceOpeningPeriodViewV1> ReadAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<FinanceOpeningPeriodViewV1>())!;
        }

        public async Task CommandAsync(HttpClient client, string path, string key, object body, bool replay = false)
        {
            using var response = await client.SendAsync(Json(path, key, body));
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Assert.Equal(replay, response.Headers.Contains("Idempotency-Replayed"));
        }

        public PortScope Port(Guid company)
        {
            var context = new CompanyContext();
            context.Set(company);
            var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema)).Options, context);
            return new PortScope(database, new SettingsCompanyPolicyProviderV1(database, context, new FixedActor(company)));
        }

        public async Task<string> GrantAsync(string subject, Guid company, string permission)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await database.ExternalIdentities.SingleOrDefaultAsync(row => row.Issuer == BootstrapIdentityOptions.Issuer && row.Subject == subject);
            var actor = link?.UserId ?? Guid.NewGuid();
            if (link is null)
            {
                database.Users.Add(new UserAccount(actor, "SENTETIK liste kullanıcısı", null));
                database.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, subject));
            }
            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"REF-{roleId:N}", "SENTETIK liste rolü", true);
            role.AddPermission(permission);
            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            database.Roles.Add(role);
            database.RoleAssignments.Add(assignment);
            await database.SaveChangesAsync();
            return subject;
        }

        public async Task<int> CountAsync(string sql)
        {
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return (int)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
