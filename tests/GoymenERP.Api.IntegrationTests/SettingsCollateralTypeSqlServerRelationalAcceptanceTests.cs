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

public sealed partial class SettingsCollateralTypeSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Root = SettingsCollateralTypeEndpoints.CollateralTypesPath;
    private const string Manage = SettingsCollateralTypePermissionsV1.Manage;

    [SqlServerFact]
    public async Task Collateral_rate_CHECK_preserves_leading_zeros_and_long_exact_fractions_without_overflow_or_rounding_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        using var editor = run.Client(await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage), CompanyA);
        await run.CommandAsync(editor, Root, "rate-check", new CreateSettingsCollateralTypeRequestV1("RATE", "Synthetic", SettingsCollateralKindV1.Mortgage, "Synthetic"));
        var entry = Assert.Single((await run.ReadAsync(editor, Root)).Entries);
        var previousShapeGuard = Assert.IsType<SqlOperation>(new AddSalesContractDiscountLimit().DownOperations[0]).Sql;
        await run.ExecuteAsync(previousShapeGuard); // Initial 100 is representable by the old schema.
        var version = 1;
        foreach (var rate in new[] { "000100.000", new string('0', 64), "0." + new string('0', 60) + "1", "99." + new string('9', 61) })
        {
            version++;
            await run.ExecuteAsync($"INSERT INTO [settings].[CollateralTypeVersions] ([CompanyId],[EntryId],[Version],[Name],[Active],[RatePercent],[RecordedAtUtc],[RecordedBy],[Reason]) VALUES ('{CompanyA:D}','{entry.Id:D}',{version},N'Synthetic',1,'{rate}',SYSUTCDATETIME(),N'Synthetic',N'Synthetic')");
        }
        foreach (var rate in new[] { "100." + new string('0', 59) + "1", new string('9', 64), "101", "100.1", "1,5", "+1" })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync($"INSERT INTO [settings].[CollateralTypeVersions] ([CompanyId],[EntryId],[Version],[Name],[Active],[RatePercent],[RecordedAtUtc],[RecordedBy],[Reason]) VALUES ('{CompanyA:D}','{entry.Id:D}',99,N'Synthetic',1,'{rate}',SYSUTCDATETIME(),N'Synthetic',N'Synthetic')"));
            Assert.Equal(547, refused.Number);
        }
        Assert.Equal("99." + new string('9', 61), Assert.Single((await run.ReadAsync(editor, Root)).Entries).RatePercent);
        // Reads normalize a view only; the original history is untouched.
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CollateralTypeVersions] WHERE [RatePercent] = '000100.000'"));
        var guarded = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(previousShapeGuard));
        Assert.Equal(51942, guarded.Number);
    }

    [SqlServerFact]
    public async Task Collateral_types_preserve_company_replay_immutable_kind_exact_rates_and_inactive_consumer_reads_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        using var editor = run.Client(await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage), CompanyA);
        using var other = run.Client(await run.GrantAsync(TestUsers.CompanyB, CompanyB, Manage), CompanyB);
        using var viewer = run.Client(await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, ErpPermissions.SalesDraftsRead), CompanyA);
        Assert.Empty((await run.ReadAsync(editor, Root)).Entries);
        using (var denied = await viewer.GetAsync(Root)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var create = new CreateSettingsCollateralTypeRequestV1("DBS", "Synthetic DBS", SettingsCollateralKindV1.Dbs, "Synthetic create");
        await run.CommandAsync(editor, Root, "create", create);
        await run.CommandAsync(editor, Root, "create", create, replay: true);
        using (var conflict = await editor.SendAsync(Json(Root, "create", create with { Name = "Changed" })))
            await ProblemAsync(conflict, HttpStatusCode.Conflict, SettingsCollateralTypeProblemCodesV1.IdempotencyConflict);
        var original = Assert.Single((await run.ReadAsync(editor, Root)).Entries);
        Assert.Equal(("100", "1", SettingsCollateralKindV1.Dbs), (original.RatePercent, original.Version, original.Kind));
        var path = $"{Root}/{original.Id:D}/versions";
        var change = new RecordSettingsCollateralTypeRequestV1("1", "Synthetic DBS", false, "85.1234567890123456789", "Synthetic change");
        await run.CommandAsync(editor, path, "change", change);
        await run.CommandAsync(editor, path, "change", change, replay: true);
        using (var stale = await editor.SendAsync(Json(path, "stale", change)))
            await ProblemAsync(stale, HttpStatusCode.Conflict, SettingsCollateralTypeProblemCodesV1.VersionConflict);
        using (var invalid = await editor.SendAsync(Json(path, "over100", change with { ExpectedVersion = "2", RatePercent = "100.0000000000000000001" })))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest, SettingsCollateralTypeProblemCodesV1.RequestInvalid);
        await run.CommandAsync(editor, path, "noop", change with { ExpectedVersion = "2" });
        Assert.Empty((await run.ReadAsync(other, Root)).Entries);
        using (var foreign = await other.SendAsync(Json(path, "foreign", change)))
            await ProblemAsync(foreign, HttpStatusCode.NotFound, SettingsCollateralTypeProblemCodesV1.EntryNotFound);
        var saved = Assert.Single((await run.ReadAsync(editor, Root)).Entries);
        Assert.Equal((original.Id, "2", false, "85.1234567890123456789"), (saved.Id, saved.Version, saved.Active, saved.RatePercent));
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CollateralTypeVersions]"));
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.CollateralType'"));
        var context = new CompanyContext(); context.Set(CompanyA);
        await using var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>().UseSqlServer(run.Connection).Options, context);
        var catalog = new SettingsCollateralTypeCatalogV1(database, context, new FixedActor(CompanyA));
        var consumed = Assert.Single((await catalog.ListAsync(CompanyA)).Entries);
        Assert.False(consumed.Active); Assert.Equal(saved.RatePercent, consumed.RatePercent);
        var deniedRead = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => catalog.ListAsync(CompanyB));
        Assert.Equal(SettingsCollateralTypeProblemCodesV1.AccessDenied, deniedRead.Code);
    }

    [SqlServerFact]
    public async Task Collateral_catalog_Audit_owner_failure_rolls_back_rows_and_terminal_answer_and_same_key_retry_and_replay_are_exactly_once()
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
        var create = new CreateSettingsCollateralTypeRequestV1("LETTER", "Synthetic letter", SettingsCollateralKindV1.GuaranteeLetter, "Synthetic atomicity");
        using (var failed = await editor.SendAsync(Json(Root, "atomic", create))) Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(1, fault.Calls);
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CollateralTypes]"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CollateralTypeVersions]"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName] = N'settings.collateral-type.create.v1'"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.CollateralType'"));
        fault.Fail = false;
        await run.CommandAsync(editor, Root, "atomic", create);
        await run.CommandAsync(editor, Root, "atomic", create, replay: true);
        Assert.Equal(2, fault.Calls);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.CollateralType'"));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CollateralTypeVersions]"));
    }

    [SqlServerFact]
    public async Task Collateral_catalog_Down_passes_empty_and_refuses_existing_rows_without_data_loss_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddCollateralTypeCatalog().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        using var client = run.Client(await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage), CompanyA);
        await run.CommandAsync(client, Root, "guard", new CreateSettingsCollateralTypeRequestV1("VINOV", "Synthetic", SettingsCollateralKindV1.Vinov, "Synthetic"));
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51941, refused.Number);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CollateralTypeVersions]"));
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

        public async Task<SettingsCollateralTypesViewV1> ReadAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<SettingsCollateralTypesViewV1>())!;
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
