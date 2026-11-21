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
using GoymenERP.Modules.Settings.Application;
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
/// F-029 S1/S2 (URUN_F029 spec §5.4(4); URN-D2…D5) against a REAL SQL Server through the REAL host: until the company's
/// one-time baseline transfer the unchanged prerequisite port answers from the approved catalog and no version can be recorded
/// (409, stored); the transfer copies the company's approved entries unchanged (one save: rows + marker + execution + audit),
/// the same key replays, a changed body under it is 409, a second transfer 409; from then on the port answers from the table,
/// byte-identical to the catalog's answer; a reasoned record is one save, an unknown unit 400, a kilogram scale above Ö2's
/// bound 400, a stale version 409, a past date 422, an unchanged in-force profile a stored no-op without audit, a later version
/// leaves today's answer unchanged; another company keeps its catalog answer; 403 without the name; and the migration's Down
/// refuses (51341) while any version or a transfer marker exists. Every grant is a fresh synthetic role; every reason is
/// SENTETIK; every value in the requests is test data, not a policy choice.
/// </summary>
public sealed class SettingsUnitProfileSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Kilogram = SettingsUnitIdentitiesV1.Kilogram;
    private const string Root = SettingsUnitProfileEndpoints.UnitProfilesPath;
    private const string Transfer = Root + "/baseline-transfer";
    private const string Versions = Root + "/versions";
    private const string Migration = "AddUnitProfileVersions";
    private const string Manage = SettingsUnitProfilePermissionManifestV1.Manage;
    private const string TransferredEvent = "settings.unit-profile.baseline-transferred.v1";
    private const string RecordedEvent = "settings.unit-profile.recorded.v1";
    private const string CountRows = "SELECT COUNT(*) FROM [settings].[UnitProfileVersions]";
    private const string CountMarkers = "SELECT COUNT(*) FROM [settings].[UnitProfileBaselineTransfers]";

    [SqlServerFact]
    public async Task Unit_profile_transfer_record_replay_isolation_audit_provider_switch_and_Down_guard_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var (editorSubject, editorActor) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SettingsExchangeRatePermissionManifestV1.Read);
        var (editorBSubject, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Manage);
        using var editor = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        using var editorB = run.Client(editorBSubject, CompanyB);
        var approvedA = ApprovedUnitProfileCatalog.Approved.Entries.Single(entry => entry.CompanyId == CompanyA && entry.UnitId == Kilogram);
        // AK-1: company A's approved entries are kg and piece; every count below follows the catalog, not a literal.
        var approvedEntriesA = ApprovedUnitProfileCatalog.Approved.Entries.Count(entry => entry.CompanyId == CompanyA);
        Assert.Equal(2, approvedEntriesA);
        var transfer = new TransferUnitProfileBaselineRequestV1("SENTETIK birim profili başlangıç aktarımı");
        var record = new RecordUnitProfileRequestV1("1", Kilogram, approvedA.Scale, approvedA.IsActive, "SENTETIK birim profili");

        // The outer persistent money provider composes the persistent unit provider behind the unchanged port.
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            Assert.IsType<SettingsMoneyProfilePrerequisiteProvider>(
                scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>());
        }

        // Without the name: 403 (code-less); nothing is written.
        using (var forbidden = await viewer.GetAsync(Root))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var forbidden = await viewer.SendAsync(Json(HttpMethod.Post, Transfer, "k-forbidden", transfer)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal((0, 0), (await run.ScalarAsync<int>(CountRows), await run.ScalarAsync<int>(CountMarkers)));

        // Before the transfer: the view shows the approved entries, the port answers exactly as the approved provider.
        var before = await run.JsonAsync(editor, Root);
        Assert.False(before.GetProperty("isBaselineTransferred").GetBoolean());
        Assert.Equal(approvedEntriesA, before.GetProperty("units").GetArrayLength());
        var catalogUnit = Assert.Single(before.GetProperty("units").EnumerateArray(),
            unit => unit.GetProperty("unitId").GetGuid() == Kilogram);
        Assert.Equal((Kilogram, "0"), (catalogUnit.GetProperty("unitId").GetGuid(), catalogUnit.GetProperty("version").GetString()));
        var catalogVersion = Assert.Single(catalogUnit.GetProperty("versions").EnumerateArray());
        Assert.Equal(("1", approvedA.Scale, SalesPolicySourceKindsV1.CompiledBaselineV0),
            (catalogVersion.GetProperty("version").GetString(), catalogVersion.GetProperty("scale").GetInt32(),
                catalogVersion.GetProperty("sourceKind").GetString()));
        var option = Assert.Single(before.GetProperty("unitOptions").EnumerateArray(),
            unit => unit.GetProperty("unitId").GetGuid() == Kilogram);
        Assert.Equal(UnitProfileInputLimitsV1.KilogramMaximumScale, option.GetProperty("maximumScale").GetInt32());
        var catalogAnswer = await HostRun.CatalogUnitProfileAsync(CompanyA, run.Today);
        Assert.Equal(catalogAnswer, await run.UnitProfileAsync(CompanyA, run.Today));

        // No version before the transfer: a stored refusal that replays under its key.
        using (var early = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-early", record with { ExpectedVersion = "0" })))
        {
            await AssertProblemAsync(early, HttpStatusCode.Conflict, UnitProfileProblemCodesV1.BaselineRequired);
        }

        using (var early = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-early", record with { ExpectedVersion = "0" })))
        {
            await AssertProblemAsync(early, HttpStatusCode.Conflict, UnitProfileProblemCodesV1.BaselineRequired);
            Assert.True(early.Headers.Contains("Idempotency-Replayed"));
        }

        using (var noReason = await editor.SendAsync(Json(HttpMethod.Post, Transfer, "k-t-reason", transfer with { Reason = " " })))
        {
            await AssertProblemAsync(noReason, HttpStatusCode.BadRequest, UnitProfileProblemCodesV1.ReasonRequired);
        }

        Assert.Equal((0, 0), (await run.ScalarAsync<int>(CountRows), await run.ScalarAsync<int>(CountMarkers)));

        // The transfer: the approved entry unchanged + the marker + one audit event; replay; changed body 409; second 409.
        await run.CommandAsync(editor, Transfer, "k-t-1", transfer, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, Transfer, "k-t-1", transfer, HttpStatusCode.NoContent, replayed: true);
        using (var reused = await editor.SendAsync(Json(HttpMethod.Post, Transfer, "k-t-1",
                   transfer with { Reason = "SENTETIK başka gerekçe" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, UnitProfileProblemCodesV1.IdempotencyConflict);
        }

        using (var again = await editor.SendAsync(Json(HttpMethod.Post, Transfer, "k-t-2", transfer)))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict, UnitProfileProblemCodesV1.AlreadyTransferred);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[UnitProfileVersions] WHERE [CompanyId] = @c AND [UnitId] = @u AND [Version] = 1 "
            + "AND [ProfileId] = @p AND [UnitCode] = @code AND [IsKilogram] = 1 AND [Scale] = @s AND [IsActive] = 1 "
            + "AND [EffectiveFrom] = @d AND [SourceKind] = 'CompiledBaselineV0' AND [DecisionReference] = @ref AND [RecordedBy] = @a",
            ("@c", CompanyA), ("@u", Kilogram), ("@p", approvedA.ProfileId), ("@code", approvedA.UnitCode), ("@s", approvedA.Scale),
            ("@d", approvedA.EffectiveFrom.ToDateTime(TimeOnly.MinValue)), ("@ref", approvedA.Source), ("@a", editorActor)));
        Assert.Equal(approvedEntriesA, await run.ScalarAsync<int>(CountRows));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[UnitProfileBaselineTransfers] WHERE [CompanyId] = @c AND [EntryCount] = @n "
            + "AND [TransferredBy] = @a", ("@c", CompanyA), ("@n", approvedEntriesA), ("@a", editorActor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c AND [PermissionCode] = @p",
            ("@e", TransferredEvent), ("@c", CompanyA), ("@p", Manage)));

        // From now on the table answers, byte-identical to the catalog's answer.
        Assert.Equal(catalogAnswer, await run.UnitProfileAsync(CompanyA, run.Today));
        var transferred = await run.JsonAsync(editor, Root);
        Assert.True(transferred.GetProperty("isBaselineTransferred").GetBoolean());
        Assert.Equal(approvedEntriesA, transferred.GetProperty("units").GetArrayLength());
        Assert.All(transferred.GetProperty("units").EnumerateArray(),
            unit => Assert.Equal("1", unit.GetProperty("version").GetString()));

        // Refusals before anything is stored (not replayed under their key): an unknown unit, a kilogram scale above Ö2.
        using (var unknown = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-unknown", record with { UnitId = Guid.NewGuid() })))
        {
            await AssertProblemAsync(unknown, HttpStatusCode.BadRequest, UnitProfileProblemCodesV1.UnitUnknown);
        }

        using (var tooFine = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-scale",
                   record with { Scale = UnitProfileInputLimitsV1.KilogramMaximumScale + 1 })))
        {
            await AssertProblemAsync(tooFine, HttpStatusCode.BadRequest, UnitProfileProblemCodesV1.RequestInvalid);
            Assert.False(tooFine.Headers.Contains("Idempotency-Replayed"));
        }

        using (var stale = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-stale", record with { ExpectedVersion = "0" })))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, UnitProfileProblemCodesV1.VersionConflict);
        }

        using (var past = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-past",
                   record with { Scale = approvedA.Scale - 1, EffectiveFrom = run.Today.AddDays(-1) })))
        {
            await AssertProblemAsync(past, HttpStatusCode.UnprocessableEntity, UnitProfileProblemCodesV1.EffectiveFromPast);
        }

        // The in-force values again: a stored no-op (204), no row, no audit.
        await run.CommandAsync(editor, Versions, "k-noop", record, HttpStatusCode.NoContent);
        Assert.Equal(approvedEntriesA, await run.ScalarAsync<int>(CountRows));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e", ("@e", RecordedEvent)));

        // A new version from tomorrow: one row + one audit event; today keeps the transferred version (now closed).
        var tomorrow = run.Today.AddDays(1);
        await run.CommandAsync(editor, Versions, "k-r-2", record with
        {
            Scale = approvedA.Scale - 1, EffectiveFrom = tomorrow, Reason = "SENTETIK ölçek güncellemesi",
        }, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, Versions, "k-r-2", record with
        {
            Scale = approvedA.Scale - 1, EffectiveFrom = tomorrow, Reason = "SENTETIK ölçek güncellemesi",
        }, HttpStatusCode.NoContent, replayed: true);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[UnitProfileVersions] WHERE [CompanyId] = @c AND [UnitId] = @u AND [Version] = 2 "
            + "AND [Scale] = @s AND [EffectiveFrom] = @d AND [SourceKind] = 'UserMaintained' AND [DecisionReference] IS NULL "
            + "AND [RecordedBy] = @a", ("@c", CompanyA), ("@u", Kilogram), ("@s", approvedA.Scale - 1),
            ("@d", tomorrow.ToDateTime(TimeOnly.MinValue)), ("@a", editorActor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c AND [EntityVersionBefore] = 1 "
            + "AND [EntityVersionAfter] = 2", ("@e", RecordedEvent), ("@c", CompanyA)));
        var today = await run.UnitProfileAsync(CompanyA, run.Today);
        Assert.Equal((1L, approvedA.Scale, (DateOnly?)run.Today), (today.Profile.Version, today.Scale, today.EffectiveThrough));
        var next = await run.UnitProfileAsync(CompanyA, tomorrow);
        Assert.Equal((2L, approvedA.Scale - 1, tomorrow, (DateOnly?)null),
            (next.Profile.Version, next.Scale, next.EffectiveFrom, next.EffectiveThrough));

        // Another company is untouched: not transferred, and its port still answers from the approved catalog.
        var other = await run.JsonAsync(editorB, Root);
        Assert.False(other.GetProperty("isBaselineTransferred").GetBoolean());
        Assert.Equal(await HostRun.CatalogUnitProfileAsync(CompanyB, run.Today), await run.UnitProfileAsync(CompanyB, run.Today));

        // The rollback refuses while any version or a transfer marker exists (forward fix); only an empty store passes.
        var guard = Assert.IsType<SqlOperation>(new AddUnitProfileVersions().DownOperations[0]).Sql;
        var refusedRollback = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51341, refusedRollback.Number);
        Assert.Equal(approvedEntriesA + 1, await run.ScalarAsync<int>(CountRows));
        await run.ExecuteAsync("DELETE FROM [settings].[UnitProfileVersions];");
        var markerOnly = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51341, markerOnly.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(CountMarkers));
        await run.ExecuteAsync("DELETE FROM [settings].[UnitProfileBaselineTransfers];");
        await run.ExecuteAsync(guard);
    }

    [SqlServerFact]
    public async Task The_unit_profile_migration_Down_rolls_back_and_reapplies_freely_on_an_empty_store_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        Assert.Equal((0, 0), (await run.ScalarAsync<int>(CountRows), await run.ScalarAsync<int>(CountMarkers)));
        const string tables = "SELECT COUNT(*) FROM sys.tables WHERE [name] IN (N'UnitProfileVersions', "
            + "N'UnitProfileBaselineTransfers') AND SCHEMA_NAME([schema_id]) = N'settings'";

        await using var context = run.SettingsContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, name => name.EndsWith("_" + Migration, StringComparison.Ordinal));
        Assert.True(index > 0, "the migration is applied after an earlier Settings migration");
        await context.GetService<IMigrator>().MigrateAsync(applied[index - 1]);
        Assert.Equal(0, await run.ScalarAsync<int>(tables));
        await context.GetService<IMigrator>().MigrateAsync(applied[^1]);
        Assert.Equal(2, await run.ScalarAsync<int>(tables));
    }

    private static HttpRequestMessage Json(HttpMethod method, string path, string key, object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"unit-profile-{key}");
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
            // Only the unit profile administration takes the fixed clock; Identity keeps the system clock.
            TimeProvider clock = new FixedClock(DateTimeOffset.UtcNow);
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, services =>
                services.Replace(ServiceDescriptor.Scoped<ISettingsUnitProfileAdministrationV1>(provider =>
                    ActivatorUtilities.CreateInstance<SettingsUnitProfileAdministration>(provider, clock))));
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

        public PortScope<ISettingsPrerequisiteProvider> Port(Guid company)
        {
            var database = NewSettingsContext(company, out var companyContext);
            return new PortScope<ISettingsPrerequisiteProvider>(database,
                new SettingsUnitProfilePrerequisiteProvider(database, companyContext, new FixedActor("SENTETIK-brp", company)));
        }

        public async Task<UnitProfileV1> UnitProfileAsync(Guid company, DateOnly effectiveOn)
        {
            await using var port = Port(company);
            return await port.Value.GetUnitProfileAsync(SettingsUnitIdentitiesV1.Kilogram, effectiveOn);
        }

        /// <summary>The approved catalog provider's own answer (the byte-identical reference of the table's answer).</summary>
        public static Task<UnitProfileV1> CatalogUnitProfileAsync(Guid company, DateOnly effectiveOn)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new ApprovedSettingsPrerequisiteProvider(companyContext, new FixedActor("SENTETIK-brp", company))
                .GetUnitProfileAsync(SettingsUnitIdentitiesV1.Kilogram, effectiveOn);
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
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK birim profili kullanıcısı", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer,
                    developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"BRP-{roleId:N}", "SENTETIK birim profili rolü", true);
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
