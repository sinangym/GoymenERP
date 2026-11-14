using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
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
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-029 S4b (URUN_F029 spec §5.4(1); URN-D2…D5) against a REAL SQL Server through the REAL host: the Sales delivery tolerance
/// (SAT-D5) and order-approval amount threshold (SAT-D6) answer from the compiled catalog until the one-time, audited baseline
/// transfer, then from the version table; a reasoned record is one save (row + execution + audit), the same key replays, a
/// stale version is 409, a past date 422, another company sees nothing, 403 without the family's name, and the migration's
/// Down refuses (51311) while any version or transfer marker exists. Every grant is a fresh synthetic role; every reason is
/// SENTETIK.
/// </summary>
public sealed class SettingsSalesPolicySqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Root = SettingsSalesPolicyEndpoints.SalesPoliciesPath;
    private const string Migration = "AddSalesPolicyVersions";
    private const string Tolerance = SettingsSalesPolicyPermissionManifestV1.DeliveryToleranceManage;
    private const string Threshold = SettingsSalesPolicyPermissionManifestV1.ApprovalThresholdManage;
    private const string CountRows = "SELECT (SELECT COUNT(*) FROM [settings].[SalesPolicyBaselineTransfers]) "
        + "+ (SELECT COUNT(*) FROM [settings].[SalesDeliveryToleranceVersions]) "
        + "+ (SELECT COUNT(*) FROM [settings].[SalesApprovalAmountThresholdVersions])";

    [SqlServerFact]
    public async Task Sales_policy_baseline_transfer_record_replay_isolation_audit_and_Down_guard_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var (editorSubject, editorActor) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Tolerance, Threshold);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SettingsExchangeRatePermissionManifestV1.Read);
        var (editorBSubject, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Tolerance, Threshold);
        using var editor = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        using var editorB = run.Client(editorBSubject, CompanyB);
        var transfer = new TransferSalesPolicyBaselineRequestV1("SENTETIK katalog devri");

        // Without the family's name: 403 (code-less); nothing is written.
        using (var forbidden = await viewer.GetAsync($"{Root}/delivery-tolerance"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var forbidden = await viewer.SendAsync(Json(HttpMethod.Post, $"{Root}/delivery-tolerance/baseline-transfer",
                   "k-forbidden", transfer)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        // Before the transfer the compiled catalog answers (version "0" stored, the catalog's version 1 listed).
        var before = await run.JsonAsync(editor, $"{Root}/delivery-tolerance");
        Assert.False(before.GetProperty("isBaselineTransferred").GetBoolean());
        Assert.Equal("0", before.GetProperty("version").GetString());
        var catalogVersion = Assert.Single(before.GetProperty("versions").EnumerateArray());
        Assert.Equal(("1", "1", "2", "3"), (catalogVersion.GetProperty("version").GetString(),
            catalogVersion.GetProperty("warnPercent").GetString(), catalogVersion.GetProperty("approvalPercent").GetString(),
            catalogVersion.GetProperty("blockPercent").GetString()));

        // A record before the transfer is a stored 409 (the same key replays it).
        var early = new RecordSalesDeliveryToleranceRequestV1("0", "1.5", "2", "3", "SENTETIK erken kayıt");
        using (var refused = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/delivery-tolerance/versions", "k-early", early)))
        {
            await AssertProblemAsync(refused, HttpStatusCode.Conflict, SalesPolicyProblemCodesV1.BaselineRequired);
        }

        using (var replayed = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/delivery-tolerance/versions", "k-early", early)))
        {
            await AssertProblemAsync(replayed, HttpStatusCode.Conflict, SalesPolicyProblemCodesV1.BaselineRequired);
            Assert.True(replayed.Headers.Contains("Idempotency-Replayed"));
        }

        // The transfer: one marker + the catalog's rows unchanged, one audit event; the same key replays; a second is 409.
        await run.CommandAsync(editor, $"{Root}/delivery-tolerance/baseline-transfer", "k-t-1", transfer, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, $"{Root}/delivery-tolerance/baseline-transfer", "k-t-1", transfer, HttpStatusCode.NoContent,
            replayed: true);
        using (var again = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/delivery-tolerance/baseline-transfer", "k-t-2",
                   transfer)))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict, SalesPolicyProblemCodesV1.AlreadyTransferred);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesPolicyBaselineTransfers] WHERE [CompanyId] = @c AND [PolicyFamily] = 'DeliveryTolerance'",
            ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesDeliveryToleranceVersions] WHERE [CompanyId] = @c AND [Version] = 1 "
            + "AND [WarnPercent] = 1 AND [ApprovalPercent] = 2 AND [BlockPercent] = 3 AND [SourceKind] = 'CompiledBaselineV0'",
            ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.sales-delivery-tolerance.baseline-transferred.v1' "
            + "AND [CompanyId] = @c AND [PermissionCode] = @p", ("@c", CompanyA), ("@p", Tolerance)));

        // After the transfer the table answers; the next version records with a reason (today, Europe/Istanbul).
        var transferred = await run.JsonAsync(editor, $"{Root}/delivery-tolerance");
        Assert.True(transferred.GetProperty("isBaselineTransferred").GetBoolean());
        Assert.Equal("1", transferred.GetProperty("version").GetString());
        using (var past = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/delivery-tolerance/versions", "k-past",
                   new RecordSalesDeliveryToleranceRequestV1("1", "1.5", "2", "3", "SENTETIK geçmiş", run.Today.AddDays(-1)))))
        {
            await AssertProblemAsync(past, HttpStatusCode.UnprocessableEntity, SalesPolicyProblemCodesV1.EffectiveFromPast);
        }

        var record = new RecordSalesDeliveryToleranceRequestV1("1", "1.5", "2", "3", "SENTETIK tolerans güncellemesi");
        await run.CommandAsync(editor, $"{Root}/delivery-tolerance/versions", "k-r-1", record, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, $"{Root}/delivery-tolerance/versions", "k-r-1", record, HttpStatusCode.NoContent,
            replayed: true);
        using (var reused = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/delivery-tolerance/versions", "k-r-1",
                   record with { WarnPercent = "1.25" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, SalesPolicyProblemCodesV1.IdempotencyConflict);
        }

        using (var stale = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/delivery-tolerance/versions", "k-r-stale", record)))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, SalesPolicyProblemCodesV1.VersionConflict);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesDeliveryToleranceVersions] WHERE [CompanyId] = @c AND [Version] = 2 "
            + "AND [WarnPercent] = 1.5 AND [EffectiveFrom] = @d AND [SourceKind] = 'UserMaintained' AND [RecordedBy] = @a",
            ("@c", CompanyA), ("@d", run.Today.ToDateTime(TimeOnly.MinValue)), ("@a", editorActor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.sales-delivery-tolerance.recorded.v1' "
            + "AND [CompanyId] = @c", ("@c", CompanyA)));
        var recorded = await run.JsonAsync(editor, $"{Root}/delivery-tolerance");
        Assert.Equal("2", recorded.GetProperty("version").GetString());
        Assert.Contains(recorded.GetProperty("versions").EnumerateArray(), version =>
            version.GetProperty("version").GetString() == "2" && version.GetProperty("warnPercent").GetString() == "1.5"
            && version.GetProperty("state").GetString() == SalesPolicyVersionStatesV1.InForce);

        // The approval threshold family is independent: its own transfer, then an enabled TRY amount.
        var thresholds = await run.JsonAsync(editor, $"{Root}/approval-thresholds");
        Assert.False(thresholds.GetProperty("isBaselineTransferred").GetBoolean());
        await run.CommandAsync(editor, $"{Root}/approval-thresholds/baseline-transfer", "k-th-t", transfer, HttpStatusCode.NoContent);
        var lira = SettingsCurrencyIdentitiesV1.TurkishLira;
        var amount = new RecordSalesApprovalThresholdRequestV1("1", true, "250000", "SENTETIK onay eşiği");
        await run.CommandAsync(editor, $"{Root}/approval-thresholds/{lira:D}/versions", "k-th-r", amount, HttpStatusCode.NoContent);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesApprovalAmountThresholdVersions] WHERE [CompanyId] = @c AND [CurrencyId] = @t "
            + "AND [Version] = 2 AND [IsEnabled] = 1 AND [ThresholdAmount] = 250000 AND [ThresholdAmountScale] = 0",
            ("@c", CompanyA), ("@t", lira)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.sales-approval-threshold.recorded.v1' "
            + "AND [CompanyId] = @c AND [PermissionCode] = @p", ("@c", CompanyA), ("@p", Threshold)));

        // Another company sees nothing of A's: its families are still answered by the catalog.
        var other = await run.JsonAsync(editorB, $"{Root}/delivery-tolerance");
        Assert.False(other.GetProperty("isBaselineTransferred").GetBoolean());
        Assert.Equal("0", other.GetProperty("version").GetString());

        // The rollback refuses while any row exists (forward fix); the rows stay.
        var guard = Assert.IsType<SqlOperation>(new AddSalesPolicyVersions().DownOperations[0]).Sql;
        var refusedRollback = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51311, refusedRollback.Number);
        Assert.Equal(6, await run.ScalarAsync<int>(CountRows));

        // A marker alone also blocks it; only an empty store passes.
        await run.ExecuteAsync("DELETE FROM [settings].[SalesDeliveryToleranceVersions]; "
            + "DELETE FROM [settings].[SalesApprovalAmountThresholdVersions];");
        var markerOnly = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51311, markerOnly.Number);
        await run.ExecuteAsync("DELETE FROM [settings].[SalesPolicyBaselineTransfers];");
        await run.ExecuteAsync(guard);
    }

    [SqlServerFact]
    public async Task The_sales_policy_migrations_Down_rolls_back_and_reapplies_freely_on_an_empty_store_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        await using var context = run.SettingsContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, name => name.EndsWith("_" + Migration, StringComparison.Ordinal));
        Assert.True(index > 0, "the migration is applied after an earlier Settings migration");
        await context.GetService<IMigrator>().MigrateAsync(applied[index - 1]);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'SalesDeliveryToleranceVersions' AND SCHEMA_NAME([schema_id]) = N'settings'"));
        await context.GetService<IMigrator>().MigrateAsync(applied[^1]);
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] IN (N'SalesPolicyBaselineTransfers', N'SalesDeliveryToleranceVersions', "
            + "N'SalesApprovalAmountThresholdVersions') AND SCHEMA_NAME([schema_id]) = N'settings'"));
    }

    private static HttpRequestMessage Json(HttpMethod method, string path, string key, object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"sales-policy-{key}");
        return request;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(status == response.StatusCode, $"{response.StatusCode}: {body}");
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private static DateOnly IstanbulToday(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>The REAL host over a fresh database, with Identity grants for the HTTP routes.</summary>
    private sealed class HostRun : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private HostRun(string baseConnectionString, SqlServerRelationalApiFactory factory, TimeProvider clock)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
            Today = IstanbulToday(clock);
        }

        public SqlServerRelationalApiFactory Factory { get; }

        /// <summary>The business date of the administration's fixed server clock (stable across Istanbul midnight).</summary>
        public DateOnly Today { get; }

        public static async Task<HostRun> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            // Only the sales policy administration takes the fixed clock; Identity keeps the system clock (grants in force).
            TimeProvider clock = new FixedClock(DateTimeOffset.UtcNow);
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, services =>
                services.Replace(ServiceDescriptor.Scoped<ISettingsSalesPolicyAdministrationV1>(provider =>
                    ActivatorUtilities.CreateInstance<SettingsSalesPolicyAdministration>(provider, clock))));
            var run = new HostRun(baseConnectionString, factory, clock);
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

        public SettingsDbContext SettingsContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema))
                .Options, companyContext);
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
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK satış politikası kullanıcısı", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer,
                    developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SPOL-{roleId:N}", "SENTETIK satış politikası rolü", true);
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
    }
}
