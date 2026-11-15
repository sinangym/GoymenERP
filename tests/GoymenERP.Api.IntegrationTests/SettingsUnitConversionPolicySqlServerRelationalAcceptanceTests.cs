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
/// F-004 (BS-9/BS-13/BS-16/BS-17; URN-D7…D9) against a REAL SQL Server through the REAL host: the unit conversion policy has
/// no compiled value (the timeline is empty and the read port refuses until a person records the first version); a reasoned
/// record is one save (row + execution + audit), the same key replays, a changed body under the same key is 409, a stale
/// version 409, a past date 422, an unchanged in-force policy a stored no-op without audit, another company sees nothing, 403
/// without the name, the read port answers the version actually in force on the date, and the migration's Down refuses
/// (51330) while any version exists. Every grant is a fresh synthetic role; every reason is SENTETIK; every value in the
/// requests is test data, not a policy choice.
/// </summary>
public sealed class SettingsUnitConversionPolicySqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Root = SettingsUnitConversionPolicyEndpoints.UnitConversionPolicyPath;
    private const string Versions = Root + "/versions";
    private const string Migration = "AddUnitConversionPolicyVersions";
    private const string Manage = SettingsUnitConversionPolicyPermissionManifestV1.Manage;
    private const string RecordedEvent = "settings.unit-conversion-policy.recorded.v1";
    private const string CountRows = "SELECT COUNT(*) FROM [settings].[UnitConversionPolicyVersions]";

    [SqlServerFact]
    public async Task Unit_conversion_policy_record_replay_isolation_audit_provider_and_Down_guard_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var (editorSubject, editorActor) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SettingsExchangeRatePermissionManifestV1.Read);
        var (editorBSubject, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Manage);
        using var editor = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        using var editorB = run.Client(editorBSubject, CompanyB);
        var record = new RecordUnitConversionPolicyRequestV1("0", "7.85", 3, UnitConversionRoundingModesV1.AwayFromZero, 2,
            UnitConversionRoundingModesV1.AwayFromZero, UnitConversionRoundingModesV1.ToNegativeInfinity, "1",
            UnitConversionToleranceBoundariesV1.Inclusive, UnitConversionToleranceBasesV1.TheoreticalKg,
            "SENTETIK birim çevrim politikası");

        // Without the name: 403 (code-less); nothing is written.
        using (var forbidden = await viewer.GetAsync(Root))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var forbidden = await viewer.SendAsync(Json(HttpMethod.Post, Versions, "k-forbidden", record)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        // No compiled value: an empty timeline (version "0") and the read port refuses.
        var empty = await run.JsonAsync(editor, Root);
        Assert.Equal("0", empty.GetProperty("version").GetString());
        Assert.Empty(empty.GetProperty("versions").EnumerateArray());
        await run.AssertPortRefusesAsync(CompanyA, run.Today, SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound);

        // A culture-formatted or out-of-range value is refused before anything is stored (not replayed under its key).
        using (var comma = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-comma",
                   record with { DensityGramsPerCubicCentimetre = "7,85" })))
        {
            await AssertProblemAsync(comma, HttpStatusCode.BadRequest, UnitConversionPolicyProblemCodesV1.RequestInvalid);
        }

        using (var tooMany = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-decimals",
                   record with { CoefficientDecimals = UnitConversionPolicyInputLimitsV1.MaximumRoundingDecimals + 1 })))
        {
            await AssertProblemAsync(tooMany, HttpStatusCode.BadRequest, UnitConversionPolicyProblemCodesV1.RequestInvalid);
            Assert.False(tooMany.Headers.Contains("Idempotency-Replayed"));
        }

        using (var noReason = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-reason", record with { Reason = " " })))
        {
            await AssertProblemAsync(noReason, HttpStatusCode.BadRequest, UnitConversionPolicyProblemCodesV1.ReasonRequired);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        // The first version: one row + one audit event; the same key replays; a changed body under it is 409.
        await run.CommandAsync(editor, Versions, "k-r-1", record, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, Versions, "k-r-1", record, HttpStatusCode.NoContent, replayed: true);
        using (var reused = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-r-1",
                   record with { DensityGramsPerCubicCentimetre = "7.86" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, UnitConversionPolicyProblemCodesV1.IdempotencyConflict);
        }

        using (var stale = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-r-stale", record)))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, UnitConversionPolicyProblemCodesV1.VersionConflict);
        }

        using (var replayedStale = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-r-stale", record)))
        {
            await AssertProblemAsync(replayedStale, HttpStatusCode.Conflict, UnitConversionPolicyProblemCodesV1.VersionConflict);
            Assert.True(replayedStale.Headers.Contains("Idempotency-Replayed"));
        }

        using (var past = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-past",
                   record with { ExpectedVersion = "1", EffectiveFrom = run.Today.AddDays(-1) })))
        {
            await AssertProblemAsync(past, HttpStatusCode.UnprocessableEntity, UnitConversionPolicyProblemCodesV1.EffectiveFromPast);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[UnitConversionPolicyVersions] WHERE [CompanyId] = @c AND [Version] = 1 "
            + "AND [DensityGramsPerCubicCentimetre] = 7.85 AND [CoefficientDecimals] = 3 AND [CoefficientRounding] = 'AwayFromZero' "
            + "AND [DocumentTotalDecimals] = 2 AND [WholeUnitRounding] = 'ToNegativeInfinity' AND [WeighbridgeTolerancePercent] = 1 "
            + "AND [ToleranceBoundary] = 'Inclusive' AND [ToleranceBase] = 'TheoreticalKg' AND [EffectiveFrom] = @d "
            + "AND [RecordedBy] = @a", ("@c", CompanyA), ("@d", run.Today.ToDateTime(TimeOnly.MinValue)), ("@a", editorActor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c AND [PermissionCode] = @p "
            + "AND [EntityVersionBefore] IS NULL AND [EntityVersionAfter] = 1", ("@e", RecordedEvent), ("@c", CompanyA), ("@p", Manage)));

        // The same values again under a new key: a stored no-op (204), no row, no audit.
        await run.CommandAsync(editor, Versions, "k-noop", record with { ExpectedVersion = "1" }, HttpStatusCode.NoContent);
        Assert.Equal(1, await run.ScalarAsync<int>(CountRows));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c", ("@e", RecordedEvent),
            ("@c", CompanyA)));

        var recorded = await run.JsonAsync(editor, Root);
        Assert.Equal("1", recorded.GetProperty("version").GetString());
        var first = Assert.Single(recorded.GetProperty("versions").EnumerateArray());
        Assert.Equal(("1", "7.85", 3, "1", UnitConversionToleranceBoundariesV1.Inclusive, UnitConversionToleranceBasesV1.TheoreticalKg),
            (first.GetProperty("version").GetString(), first.GetProperty("densityGramsPerCubicCentimetre").GetString(),
                first.GetProperty("coefficientDecimals").GetInt32(), first.GetProperty("weighbridgeTolerancePercent").GetString(),
                first.GetProperty("toleranceBoundary").GetString(), first.GetProperty("toleranceBase").GetString()));
        Assert.Equal(SalesPolicyVersionStatesV1.InForce, first.GetProperty("state").GetString());

        // The read port answers the stored version actually in force; nothing before its start.
        await using (var port = run.Port(CompanyA))
        {
            var policy = await port.Value.GetUnitConversionPolicyAsync(run.Today);
            Assert.Equal(CompanyA, policy.CompanyId);
            Assert.Equal(1L, policy.Profile.Version);
            Assert.Equal(7.85m, policy.DensityGramsPerCubicCentimetre);
            Assert.Equal((3, MidpointRounding.AwayFromZero, 2, MidpointRounding.AwayFromZero, MidpointRounding.ToNegativeInfinity),
                (policy.CoefficientDecimals, policy.CoefficientRounding, policy.DocumentTotalDecimals, policy.DocumentTotalRounding,
                    policy.WholeUnitRounding));
            Assert.Equal(1m, policy.WeighbridgeTolerancePercent);
            Assert.Equal(SettingsWeighbridgeToleranceBoundaryV1.Inclusive, policy.ToleranceBoundary);
            Assert.Equal(SettingsWeighbridgeToleranceBaseV1.TheoreticalKg, policy.ToleranceBase);
            Assert.Equal(run.Today, policy.EffectiveFrom);
            Assert.Null(policy.EffectiveThrough);
        }

        await run.AssertPortRefusesAsync(CompanyA, run.Today.AddDays(-1), SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound);

        // A later version from tomorrow: today keeps version 1 (now closed), tomorrow answers version 2.
        var tomorrow = run.Today.AddDays(1);
        await run.CommandAsync(editor, Versions, "k-r-2", record with
        {
            ExpectedVersion = "1", DensityGramsPerCubicCentimetre = "7.9", EffectiveFrom = tomorrow,
            Reason = "SENTETIK yoğunluk güncellemesi",
        }, HttpStatusCode.NoContent);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c AND [EntityVersionBefore] = 1 "
            + "AND [EntityVersionAfter] = 2", ("@e", RecordedEvent), ("@c", CompanyA)));
        await using (var port = run.Port(CompanyA))
        {
            var today = await port.Value.GetUnitConversionPolicyAsync(run.Today);
            Assert.Equal((1L, 7.85m, (DateOnly?)run.Today), (today.Profile.Version, today.DensityGramsPerCubicCentimetre,
                today.EffectiveThrough));
            var next = await port.Value.GetUnitConversionPolicyAsync(tomorrow);
            Assert.Equal((2L, 7.9m, (DateOnly?)null), (next.Profile.Version, next.DensityGramsPerCubicCentimetre,
                next.EffectiveThrough));
        }

        // Another company sees nothing of A's: its timeline is empty and its read port refuses.
        var other = await run.JsonAsync(editorB, Root);
        Assert.Equal("0", other.GetProperty("version").GetString());
        Assert.Empty(other.GetProperty("versions").EnumerateArray());
        await run.AssertPortRefusesAsync(CompanyB, run.Today, SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound);

        // The rollback refuses while any row exists (forward fix); the rows stay. Only an empty store passes.
        var guard = Assert.IsType<SqlOperation>(new AddUnitConversionPolicyVersions().DownOperations[0]).Sql;
        var refusedRollback = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51330, refusedRollback.Number);
        Assert.Equal(2, await run.ScalarAsync<int>(CountRows));
        await run.ExecuteAsync("DELETE FROM [settings].[UnitConversionPolicyVersions];");
        await run.ExecuteAsync(guard);
    }

    [SqlServerFact]
    public async Task The_unit_conversion_policy_migration_Down_rolls_back_and_reapplies_freely_on_an_empty_store_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        await using var context = run.SettingsContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, name => name.EndsWith("_" + Migration, StringComparison.Ordinal));
        Assert.True(index > 0, "the migration is applied after an earlier Settings migration");
        await context.GetService<IMigrator>().MigrateAsync(applied[index - 1]);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'UnitConversionPolicyVersions' AND SCHEMA_NAME([schema_id]) = N'settings'"));
        await context.GetService<IMigrator>().MigrateAsync(applied[^1]);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'UnitConversionPolicyVersions' AND SCHEMA_NAME([schema_id]) = N'settings'"));
    }

    private static HttpRequestMessage Json(HttpMethod method, string path, string key, object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"unit-conversion-policy-{key}");
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
            // Only the unit conversion policy administration takes the fixed clock; Identity keeps the system clock.
            TimeProvider clock = new FixedClock(DateTimeOffset.UtcNow);
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, services =>
                services.Replace(ServiceDescriptor.Scoped<ISettingsUnitConversionPolicyAdministrationV1>(provider =>
                    ActivatorUtilities.CreateInstance<SettingsUnitConversionPolicyAdministration>(provider, clock))));
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

        public SettingsDbContext SettingsContext(Guid company) => NewSettingsContext(company, out _);

        public PortScope<ISettingsUnitConversionPolicyProviderV1> Port(Guid company)
        {
            var database = NewSettingsContext(company, out var companyContext);
            return new PortScope<ISettingsUnitConversionPolicyProviderV1>(database,
                new SettingsUnitConversionPolicyProviderV1(database, companyContext, new FixedActor("SENTETIK-ucp", company)));
        }

        public async Task AssertPortRefusesAsync(Guid company, DateOnly effectiveOn, string code)
        {
            await using var port = Port(company);
            var refused = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
                port.Value.GetUnitConversionPolicyAsync(effectiveOn));
            Assert.Equal(code, refused.Code);
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
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK birim çevrim politikası kullanıcısı", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer,
                    developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"UCP-{roleId:N}", "SENTETIK birim çevrim politikası rolü", true);
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
