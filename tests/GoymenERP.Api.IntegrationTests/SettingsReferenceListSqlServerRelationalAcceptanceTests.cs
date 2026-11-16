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
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>KIM-D49/D51/D52, KLN-D113 TK-43: real host/SQL evidence for the existing, unseeded reference lists.</summary>
public sealed class SettingsReferenceListSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Root = SettingsReferenceListEndpoints.ReferenceListsPath;
    private const string Manage = SettingsReferenceListPermissionManifestV1.PurchasingListsManage;
    private const string Expenses = Root + "/expense-types";

    [SqlServerFact]
    public async Task Reference_lists_preserve_company_latest_version_replay_authority_and_audit_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var editorSubject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage);
        var viewerSubject = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, ErpPermissions.SalesDraftsRead);
        var otherSubject = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Manage);
        using var editor = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        using var other = run.Client(otherSubject, CompanyB);

        using (var denied = await viewer.GetAsync(Expenses)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await viewer.SendAsync(Json(Expenses + "/entries", "denied", new CreateSettingsReferenceListEntryRequestV1("NAKLIYE", "SENTETIK nakliye", "SENTETIK"))))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var production = await editor.GetAsync(Root + "/paint-colors")) Assert.Equal(HttpStatusCode.Forbidden, production.StatusCode);
        using (var closed = await editor.GetAsync(Root + "/operation-types")) Assert.Equal(HttpStatusCode.NotFound, closed.StatusCode);
        Assert.Empty((await run.ReadAsync(editor, Expenses)).Entries);
        Assert.Empty((await run.ReadAsync(editor, Root + "/service-types")).Entries);

        var create = new CreateSettingsReferenceListEntryRequestV1("NAKLIYE", "SENTETIK nakliye", "SENTETIK oluşturma");
        await run.CommandAsync(editor, Expenses + "/entries", "create", create);
        await run.CommandAsync(editor, Expenses + "/entries", "create", create, replay: true);
        using (var changed = await editor.SendAsync(Json(Expenses + "/entries", "create", create with { Name = "SENTETIK başka" })))
            await ProblemAsync(changed, HttpStatusCode.Conflict, SettingsReferenceListProblemCodesV1.IdempotencyConflict);

        var original = Assert.Single((await run.ReadAsync(editor, Expenses)).Entries);
        Assert.Equal(("NAKLIYE", "SENTETIK nakliye", true, "1"), (original.Code, original.Name, original.Active, original.Version));
        var versions = $"{Expenses}/entries/{original.Id:D}/versions";
        var deactivate = new RecordSettingsReferenceListEntryRequestV1("1", "SENTETIK yeni ad", false, "SENTETIK pasif");
        await run.CommandAsync(editor, versions, "deactivate", deactivate);
        await run.CommandAsync(editor, versions, "deactivate", deactivate, replay: true);
        using (var stale = await editor.SendAsync(Json(versions, "stale", deactivate)))
            await ProblemAsync(stale, HttpStatusCode.Conflict, SettingsReferenceListProblemCodesV1.VersionConflict);

        var inactive = Assert.Single((await run.ReadAsync(editor, Expenses)).Entries);
        Assert.Equal((original.Id, "SENTETIK yeni ad", false, "2"), (inactive.Id, inactive.Name, inactive.Active, inactive.Version));
        await run.CommandAsync(editor, versions, "noop", deactivate with { ExpectedVersion = "2" });
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [settings].[ReferenceListEntryVersions]"));
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.ReferenceListEntry'"));

        await using (var port = run.Port(CompanyA))
        {
            var list = await port.Value.ListAsync(CompanyA, SettingsReferenceListKindV1.ExpenseType);
            Assert.True(list.Enabled);
            var entry = Assert.Single(list.Entries);
            Assert.Equal((original.Id, false, 2L), (entry.Id, entry.Active, entry.Version));
            var lookup = await port.Value.GetAsync(CompanyA, SettingsReferenceListKindV1.ExpenseType, original.Id);
            Assert.Equal(SettingsReferenceListLookupOutcomeV1.Inactive, lookup.Outcome);
            Assert.Equal(SettingsReferenceListLookupOutcomeV1.NotFound,
                (await port.Value.GetAsync(CompanyA, SettingsReferenceListKindV1.ServiceType, original.Id)).Outcome);
            Assert.False((await port.Value.ListAsync(CompanyA, SettingsReferenceListKindV1.OperationType)).Enabled);
            Assert.Equal(SettingsReferenceListLookupOutcomeV1.Unavailable,
                (await port.Value.GetAsync(CompanyA, SettingsReferenceListKindV1.OperationType, original.Id)).Outcome);
        }

        Assert.Empty((await run.ReadAsync(other, Expenses)).Entries);
        using (var foreign = await other.SendAsync(Json(versions, "foreign", deactivate)))
            await ProblemAsync(foreign, HttpStatusCode.NotFound, SettingsReferenceListProblemCodesV1.EntryNotFound);
        await using (var port = run.Port(CompanyB))
        {
            Assert.Equal(SettingsReferenceListLookupOutcomeV1.NotFound,
                (await port.Value.GetAsync(CompanyB, SettingsReferenceListKindV1.ExpenseType, original.Id)).Outcome);
            var refused = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
                port.Value.ListAsync(CompanyA, SettingsReferenceListKindV1.ExpenseType));
            Assert.Equal(SettingsReferenceListProblemCodesV1.AccessDenied, refused.Code);
        }

        await run.CommandAsync(editor, versions, "reactivate", deactivate with { ExpectedVersion = "2", Active = true });
        await using (var port = run.Port(CompanyA))
            Assert.Equal(SettingsReferenceListLookupOutcomeV1.Found,
                (await port.Value.GetAsync(CompanyA, SettingsReferenceListKindV1.ExpenseType, original.Id)).Outcome);
        Assert.Equal(3, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.ReferenceListEntry'"));
    }

    [SqlServerFact]
    public async Task Reference_list_migration_Down_refuses_existing_entries_and_passes_on_an_empty_store_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddReferenceListEntries().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage);
        using var client = run.Client(subject, CompanyA);
        await run.CommandAsync(client, Expenses + "/entries", "guard", new CreateSettingsReferenceListEntryRequestV1("SENTETIK", "SENTETIK", "SENTETIK"));
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51886, refused.Number);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[ReferenceListEntries]"));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[ReferenceListEntryVersions]"));
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

    private sealed class PortScope(SettingsDbContext database, ISettingsReferenceListReaderV1 value) : IAsyncDisposable
    {
        public ISettingsReferenceListReaderV1 Value { get; } = value;
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

        public async Task<SettingsReferenceListViewV1> ReadAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<SettingsReferenceListViewV1>())!;
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
            return new PortScope(database, new SettingsReferenceListReaderV1(database, context, new FixedActor(company)));
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
