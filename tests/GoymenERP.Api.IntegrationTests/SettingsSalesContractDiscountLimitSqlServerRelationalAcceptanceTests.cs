using System.Data;
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
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsSalesContractDiscountLimitSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Root = SettingsCompanyPolicyEndpoints.CompanyPolicyPath + "/sales-contract-discount-limit";
    private const string Manage = SettingsSalesContractDiscountLimitPermissionsV1.Manage;

    [SqlServerFact]
    public async Task The_separate_contract_limit_preserves_missing_zero_company_version_replay_and_Audit_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        using var editor = run.Client(await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage), CompanyA);
        using var other = run.Client(await run.GrantAsync(TestUsers.CompanyB, CompanyB, Manage), CompanyB);
        using var viewer = run.Client(await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, ErpPermissions.SettingsSalesDiscountLimitsManage), CompanyA);
        using (var denied = await viewer.GetAsync(Root)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var missing = await run.ReadAsync(editor, Root);
        Assert.Null(missing.LimitPercent); Assert.Equal("0", missing.Version); Assert.Empty(missing.Versions);
        var context = new CompanyContext(); context.Set(CompanyA);
        await using var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>().UseSqlServer(run.Connection).Options, context);
        var provider = new SettingsCompanyPolicyProviderV1(database, context, new FixedActor(CompanyA));
        var host = new SettingsSalesContractDiscountLimitHost(provider, context);
        Assert.Null(await host.GetContractDiscountLimitPercentAsync(new DateOnly(2026, 11, 16)));
        var create = new RecordSalesContractDiscountLimitRequestV1("0", "0", "Synthetic explicit zero");
        await run.CommandAsync(editor, Root + "/versions", "create", create);
        await run.CommandAsync(editor, Root + "/versions", "create", create, replay: true);
        Assert.Equal(0m, await host.GetContractDiscountLimitPercentAsync(new DateOnly(2026, 11, 16)));
        using (var conflict = await editor.SendAsync(Json(Root + "/versions", "create", create with { LimitPercent = "5" })))
            await ProblemAsync(conflict, HttpStatusCode.Conflict, CompanyPolicyProblemCodesV1.IdempotencyConflict);
        using (var stale = await editor.SendAsync(Json(Root + "/versions", "stale", create)))
            await ProblemAsync(stale, HttpStatusCode.Conflict, CompanyPolicyProblemCodesV1.VersionConflict);
        using (var invalid = await editor.SendAsync(Json(Root + "/versions", "invalid", create with { ExpectedVersion = "1", LimitPercent = "100.0001" })))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid);
        await run.CommandAsync(editor, Root + "/versions", "noop", create with { ExpectedVersion = "1" });
        await run.CommandAsync(editor, Root + "/versions", "change", create with { ExpectedVersion = "1", LimitPercent = "12.3456" });
        Assert.Equal(12.3456m, await host.GetContractDiscountLimitPercentAsync(new DateOnly(2026, 11, 16)));
        var changed = await run.ReadAsync(editor, Root);
        Assert.Equal(("2", "12.3456"), (changed.Version, changed.LimitPercent)); Assert.Equal(2, changed.Versions.Count);
        Assert.Null((await run.ReadAsync(other, Root)).LimitPercent);
        var deniedRead = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => provider.GetContractDiscountLimitAsync(CompanyB));
        Assert.Equal(CompanyPolicyProblemCodesV1.AccessDenied, deniedRead.Code);
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.SalesContractDiscountLimit'"));
        // The new key cannot carry another key's value shape or exceed its published range.
        var invalidShape = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync("UPDATE [settings].[CompanyPolicyVersions] SET [BooleanValue]=1 WHERE [PolicyKey]='sales.contract-discount-limit'"));
        Assert.Equal(547, invalidShape.Number);
    }

    [SqlServerFact]
    public async Task Contract_limit_Audit_owner_failure_is_retryable_and_does_not_commit_a_value_or_terminal_answer()
    {
        var fault = new OwnerFault();
        await using var run = await HostRun.StartAsync(services =>
        {
            var registration = services.Single(value => value.ServiceType == typeof(IAuditTransactionWriterV1));
            services.Remove(registration);
            services.AddScoped<IAuditTransactionWriterV1>(provider => new FaultingOwner(
                (IAuditTransactionWriterV1)ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!), fault));
        });
        using var editor = run.Client(await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage), CompanyA);
        var create = new RecordSalesContractDiscountLimitRequestV1("0", "10", "Synthetic atomicity");
        using (var failed = await editor.SendAsync(Json(Root + "/versions", "atomic", create))) Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(1, fault.Calls);
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CompanyPolicyVersions] WHERE [PolicyKey]='sales.contract-discount-limit'"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]='settings.sales-contract-discount-limit.record.v1'"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType]=N'Settings.SalesContractDiscountLimit'"));
        fault.Fail = false;
        await run.CommandAsync(editor, Root + "/versions", "atomic", create);
        await run.CommandAsync(editor, Root + "/versions", "atomic", create, replay: true);
        Assert.Equal(2, fault.Calls);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CompanyPolicyVersions] WHERE [PolicyKey]='sales.contract-discount-limit'"));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType]=N'Settings.SalesContractDiscountLimit'"));
    }

    [SqlServerFact]
    public async Task Contract_limit_Down_refuses_existing_values_and_leaves_them_intact_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddSalesContractDiscountLimit().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        using var editor = run.Client(await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage), CompanyA);
        await run.CommandAsync(editor, Root + "/versions", "guard", new RecordSalesContractDiscountLimitRequestV1("0", "10", "Synthetic"));
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard)); Assert.Equal(51942, refused.Number);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CompanyPolicyVersions] WHERE [PolicyKey]='sales.contract-discount-limit'"));
    }

    private sealed class OwnerFault { public bool Fail { get; set; } = true; public int Calls { get; set; } }
    private sealed class FaultingOwner(IAuditTransactionWriterV1 inner, OwnerFault fault) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => inner.HasPendingDrafts;

        public async Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
        {
            await inner.WritePendingAsync(transaction, cancellationToken); fault.Calls++;
            if (fault.Fail) throw new InvalidOperationException("Synthetic owner failure after append");
        }
        public void DiscardPending() => inner.DiscardPending();
    }

    private static HttpRequestMessage Json(string path, string key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"reference-list-{key}");
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

    private sealed class HostRun(string baseConnectionString, SqlServerRelationalApiFactory factory) : IAsyncDisposable
    {
        public string Connection => factory.DatabaseConnectionString;
        public static async Task<HostRun> StartAsync(Action<IServiceCollection>? configureServices = null)
        {
            var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var run = new HostRun(connection, new SqlServerRelationalApiFactory(connection, configureServices));
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

        public async Task<SalesContractDiscountLimitViewV1> ReadAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<SalesContractDiscountLimitViewV1>())!;
        }

        public async Task CommandAsync(HttpClient client, string path, string key, object body, bool replay = false)
        {
            using var response = await client.SendAsync(Json(path, key, body));
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Assert.Equal(replay, response.Headers.Contains("Idempotency-Replayed"));
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
