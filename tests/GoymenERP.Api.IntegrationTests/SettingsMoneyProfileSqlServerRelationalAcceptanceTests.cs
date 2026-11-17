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

public sealed partial class SettingsMoneyProfileSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Root = SettingsMoneyProfileEndpoints.MoneyProfilesPath;
    private const string Manage = SettingsMoneyProfilePermissionManifestV1.Manage;
    private const string Operation = "purchasing.order.v1";

    [SqlServerFact]
    public async Task Money_profiles_preserve_baseline_snapshots_and_operation_isolation_with_atomic_audit_and_replay_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage);
        var viewerSubject = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, ErpPermissions.SalesDraftsRead);
        using var editor = run.Client(subject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        using (var denied = await viewer.GetAsync(Root)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var before = await run.ReadAsync(editor, Root);
        var series = Assert.Single(before.Profiles, profile => profile.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira && profile.Operation == Operation);
        var baseline = Assert.Single(series.Versions);
        Assert.Equal(("1", 2, 4), (series.Version, baseline.Scale, baseline.UnitPriceScale));
        var request = new RecordMoneyProfileRequestV1(series.Version, series.CurrencyId, Operation, 2, 3, true, "SENTETIK ölçek değişimi");
        using (var denied = await viewer.SendAsync(Json(Root + "/versions", "denied", request))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await run.CommandAsync(editor, Root + "/versions", "record", request);
        await run.CommandAsync(editor, Root + "/versions", "record", request, replay: true);
        using (var mismatched = await editor.SendAsync(Json(Root + "/versions", "record", request with { UnitPriceScale = 5 })))
            await ProblemAsync(mismatched, HttpStatusCode.Conflict, MoneyProfileProblemCodesV1.IdempotencyConflict);
        using (var stale = await editor.SendAsync(Json(Root + "/versions", "stale", request)))
            await ProblemAsync(stale, HttpStatusCode.Conflict, MoneyProfileProblemCodesV1.VersionConflict);
        using (var past = await editor.SendAsync(Json(Root + "/versions", "past", request with { ExpectedVersion = "2", EffectiveFrom = before.Today.AddDays(-1) })))
            await ProblemAsync(past, HttpStatusCode.UnprocessableEntity, MoneyProfileProblemCodesV1.EffectiveFromPast);
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [settings].[MoneyProfileVersions]"));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.MoneyProfile'"));

        await using (var port = run.Port(CompanyA))
        {
            var current = await port.Value.GetMoneyProfileAsync(series.CurrencyId, Operation, before.Today);
            Assert.Equal((2L, 2, 3), (current.Profile.Version, current.Scale, current.UnitPriceScale));
            Assert.NotEqual(baseline.ProfileId, current.Profile.ProfileId);
            var historical = await port.Value.GetMoneyProfileAsync(series.CurrencyId, Operation, before.Today.AddDays(-1));
            Assert.Equal((baseline.ProfileId, 1L, 4), (historical.Profile.ProfileId, historical.Profile.Version, historical.UnitPriceScale));
            var otherOperation = await port.Value.GetMoneyProfileAsync(series.CurrencyId, "sales.order-draft.v1", before.Today);
            Assert.Null(otherOperation.UnitPriceScale);
            Assert.Equal(1, otherOperation.Profile.Version);
        }
        await using (var port = run.Port(CompanyB))
        {
            var other = await port.Value.GetMoneyProfileAsync(series.CurrencyId, Operation, before.Today);
            Assert.Equal((CompanyB, 1L, 4), (other.CompanyId, other.Profile.Version, other.UnitPriceScale));
        }

        var guard = Assert.IsType<SqlOperation>(new AddMoneyProfileVersions().DownOperations[0]).Sql;
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51940, refused.Number);
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [settings].[MoneyProfileVersions]"));
    }

    private static HttpRequestMessage Json(string path, string key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"money-profile-{key}");
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

    private sealed class PortScope(SettingsDbContext database, ISettingsPrerequisiteProvider value) : IAsyncDisposable
    {
        public ISettingsPrerequisiteProvider Value { get; } = value;
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

        public async Task<MoneyProfileViewV1> ReadAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<MoneyProfileViewV1>())!;
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
            return new PortScope(database, new SettingsMoneyProfilePrerequisiteProvider(database, context, new FixedActor(company)));
        }

        public async Task<MoneyProfileV1> InventoryCostProfile(Guid scopedCompany, Guid requestedCompany, DateOnly date)
        {
            var company = new CompanyContext(); company.Set(scopedCompany);
            await using var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString).Options, company);
            return await new SettingsInventoryCostProfileReaderV1(database, company).ReadAsync(requestedCompany, date);
        }

        public async Task<IReadOnlyList<MoneyCurrencyCandidateV1>> CandidatesAsync(Guid company, string operation)
        {
            var context = new CompanyContext(); context.Set(company);
            await using var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString).Options, context);
            return await new SettingsMoneyCandidateReaderV2(database, context, new FixedActor(company), TimeProvider.System)
                .GetMoneyCurrencyCandidatesAsync(operation);
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
