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
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
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

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-D87/URT-D89/URT-D91 against a REAL SQL Server through the REAL host: no capacity is compiled or seeded (the list is
/// empty and the read port answers null, "hesaplanamadı"); a reasoned record of an existing work center is one save (row +
/// execution + audit), the same key replays, a changed body under the same key is 409, a stale version 409, a broken capacity
/// or buffer 400 before anything is stored, an unchanged record a stored no-op without audit, an unknown or foreign work center
/// a stored 404, a version without a buffer answers buffer 0 through the read port, another company sees nothing, 403 without
/// the name, and the migration's Down refuses (51333) while any version exists. The work center comes from the Organization
/// owner's own development data (Settings writes no Organization table); every grant is a fresh synthetic role; every
/// capacity, buffer and reason is SENTETIK test data, not a real line's figure.
/// </summary>
public sealed class SettingsWorkCenterCapacitySqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Root = SettingsWorkCenterCapacityEndpoints.WorkCenterCapacitiesPath;
    private const string Migration = "AddWorkCenterCapacityVersions";
    private const string Manage = SettingsWorkCenterCapacityPermissionManifestV1.Manage;
    private const string RecordedEvent = "settings.work-center-capacity.recorded.v1";
    private const int DownGuardNumber = 51333;
    private const string CountRows = "SELECT COUNT(*) FROM [settings].[WorkCenterCapacityVersions]";

    [SqlServerFact]
    public async Task Work_center_capacity_record_replay_no_op_isolation_audit_provider_and_Down_guard_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var workCenterId = await run.FirstWorkCenterAsync(CompanyA);
        var versions = $"{Root}/{workCenterId:D}/versions";
        var (editorSubject, editorActor) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SettingsExchangeRatePermissionManifestV1.Read);
        var (editorBSubject, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Manage);
        using var editor = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        using var editorB = run.Client(editorBSubject, CompanyB);
        var record = new RecordWorkCenterCapacityRequestV1("0", "1250.5", null, null, "SENTETIK kapasite ölçümü");

        // Without the name: 403 (code-less); nothing is written.
        using (var forbidden = await viewer.GetAsync(Root))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var forbidden = await viewer.SendAsync(Json(versions, "k-forbidden", record)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        // Nothing compiled or seeded: an empty list in kg/h, and the read port answers null (no capacity: "hesaplanamadı").
        var empty = await run.JsonAsync(editor, Root);
        Assert.Equal(WorkCenterCapacityKeysV1.InputCapacityUnit, empty.GetProperty("capacityUnit").GetString());
        Assert.Empty(empty.GetProperty("workCenters").EnumerateArray());
        await using (var port = run.Port(CompanyA))
        {
            Assert.Null(await port.Value.GetWorkCenterCapacityAsync(CompanyA, workCenterId));
        }

        // A zero capacity, a buffer without its unit, an unknown unit or a missing reason is refused before anything is stored.
        using (var zero = await editor.SendAsync(Json(versions, "k-zero", record with { InputCapacityKgPerHour = "0" })))
        {
            await AssertProblemAsync(zero, HttpStatusCode.BadRequest, WorkCenterCapacityProblemCodesV1.RequestInvalid);
            Assert.False(zero.Headers.Contains("Idempotency-Replayed"));
        }

        using (var halfBuffer = await editor.SendAsync(Json(versions, "k-half", record with { SafetyBufferValue = "4" })))
        {
            await AssertProblemAsync(halfBuffer, HttpStatusCode.BadRequest, WorkCenterCapacityProblemCodesV1.RequestInvalid);
        }

        using (var unknownUnit = await editor.SendAsync(Json(versions, "k-unit",
                   record with { SafetyBufferValue = "4", SafetyBufferUnit = "Weeks" })))
        {
            await AssertProblemAsync(unknownUnit, HttpStatusCode.BadRequest, WorkCenterCapacityProblemCodesV1.RequestInvalid);
        }

        using (var noReason = await editor.SendAsync(Json(versions, "k-reason", record with { Reason = " " })))
        {
            await AssertProblemAsync(noReason, HttpStatusCode.BadRequest, WorkCenterCapacityProblemCodesV1.ReasonRequired);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        // An unknown work center is a stored 404 (replayed under the same key); nothing else is written.
        var unknown = $"{Root}/{Guid.NewGuid():D}/versions";
        using (var missing = await editor.SendAsync(Json(unknown, "k-missing", record)))
        {
            await AssertProblemAsync(missing, HttpStatusCode.NotFound, WorkCenterCapacityProblemCodesV1.WorkCenterNotFound);
        }

        using (var replayedMissing = await editor.SendAsync(Json(unknown, "k-missing", record)))
        {
            await AssertProblemAsync(replayedMissing, HttpStatusCode.NotFound, WorkCenterCapacityProblemCodesV1.WorkCenterNotFound);
            Assert.True(replayedMissing.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        // The first version: one row + one audit event; the same key replays; a changed body under it is 409.
        await run.CommandAsync(editor, versions, "k-r-1", record, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, versions, "k-r-1", record, HttpStatusCode.NoContent, replayed: true);
        using (var reused = await editor.SendAsync(Json(versions, "k-r-1", record with { InputCapacityKgPerHour = "1300" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, WorkCenterCapacityProblemCodesV1.IdempotencyConflict);
        }

        using (var stale = await editor.SendAsync(Json(versions, "k-r-stale", record)))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, WorkCenterCapacityProblemCodesV1.VersionConflict);
        }

        using (var replayedStale = await editor.SendAsync(Json(versions, "k-r-stale", record)))
        {
            await AssertProblemAsync(replayedStale, HttpStatusCode.Conflict, WorkCenterCapacityProblemCodesV1.VersionConflict);
            Assert.True(replayedStale.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[WorkCenterCapacityVersions] WHERE [CompanyId] = @c AND [WorkCenterId] = @w "
            + "AND [Version] = 1 AND [InputCapacityKgPerHour] = 1250.5 AND [SafetyBufferValue] IS NULL "
            + "AND [SafetyBufferUnit] IS NULL AND [RecordedBy] = @a AND [Reason] = N'SENTETIK kapasite ölçümü'",
            ("@c", CompanyA), ("@w", workCenterId), ("@a", editorActor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c AND [PermissionCode] = @p "
            + "AND [EntityId] = @s AND [EntityVersionBefore] IS NULL AND [EntityVersionAfter] = 1",
            ("@e", RecordedEvent), ("@c", CompanyA), ("@p", Manage), ("@s", workCenterId.ToString("D"))));

        // A version without a buffer: the read port answers the capacity with buffer 0 and no unit.
        await using (var port = run.Port(CompanyA))
        {
            var capacity = await port.Value.GetWorkCenterCapacityAsync(CompanyA, workCenterId);
            Assert.NotNull(capacity);
            Assert.Equal((CompanyA, workCenterId, 1L, 1250.5m, 0m, (string?)null),
                (capacity.CompanyId, capacity.WorkCenterId, capacity.Profile.Version, capacity.InputCapacityKgPerHour,
                    capacity.SafetyBufferValue, capacity.SafetyBufferUnit));
        }

        // The same values again under a new key: a stored no-op (204), no row, no audit.
        await run.CommandAsync(editor, versions, "k-noop", record with { ExpectedVersion = "1", Reason = "SENTETIK tekrar" },
            HttpStatusCode.NoContent);
        Assert.Equal(1, await run.ScalarAsync<int>(CountRows));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c", ("@e", RecordedEvent),
            ("@c", CompanyA)));

        // A second version with a buffer in days: stored as value + unit; the read port answers the latest version.
        await run.CommandAsync(editor, versions, "k-r-2", record with
        {
            ExpectedVersion = "1", InputCapacityKgPerHour = "1300", SafetyBufferValue = "1.5",
            SafetyBufferUnit = WorkCenterSafetyBufferUnitsV1.Days, Reason = "SENTETIK güvenlik payı eklendi",
        }, HttpStatusCode.NoContent);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[WorkCenterCapacityVersions] WHERE [CompanyId] = @c AND [WorkCenterId] = @w "
            + "AND [Version] = 2 AND [InputCapacityKgPerHour] = 1300 AND [SafetyBufferValue] = 1.5 AND [SafetyBufferUnit] = 'Days'",
            ("@c", CompanyA), ("@w", workCenterId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c AND [EntityVersionBefore] = 1 "
            + "AND [EntityVersionAfter] = 2", ("@e", RecordedEvent), ("@c", CompanyA)));
        await using (var port = run.Port(CompanyA))
        {
            var capacity = await port.Value.GetWorkCenterCapacityAsync(CompanyA, workCenterId);
            Assert.NotNull(capacity);
            Assert.Equal((2L, 1300m, 1.5m, WorkCenterSafetyBufferUnitsV1.Days),
                (capacity.Profile.Version, capacity.InputCapacityKgPerHour, capacity.SafetyBufferValue,
                    capacity.SafetyBufferUnit));
        }

        // The view lists the timeline newest first in canonical invariant text.
        var recorded = await run.JsonAsync(editor, Root);
        var timeline = Assert.Single(recorded.GetProperty("workCenters").EnumerateArray());
        Assert.Equal((workCenterId, "2"), (timeline.GetProperty("workCenterId").GetGuid(), timeline.GetProperty("version").GetString()));
        Assert.Equal(
            [("2", "1300", "1.5", WorkCenterSafetyBufferUnitsV1.Days, true), ("1", "1250.5", null, null, false)],
            timeline.GetProperty("versions").EnumerateArray().Select(version => (
                version.GetProperty("version").GetString(), version.GetProperty("inputCapacityKgPerHour").GetString(),
                version.GetProperty("safetyBufferValue").GetString(), version.GetProperty("safetyBufferUnit").GetString(),
                version.GetProperty("isCurrent").GetBoolean())).ToArray());

        // Another company sees nothing of A's: its list is empty, A's work center is not found for it, its port answers null.
        var other = await run.JsonAsync(editorB, Root);
        Assert.Empty(other.GetProperty("workCenters").EnumerateArray());
        using (var foreign = await editorB.SendAsync(Json(versions, "k-foreign", record)))
        {
            await AssertProblemAsync(foreign, HttpStatusCode.NotFound, WorkCenterCapacityProblemCodesV1.WorkCenterNotFound);
        }

        await using (var port = run.Port(CompanyB))
        {
            Assert.Null(await port.Value.GetWorkCenterCapacityAsync(CompanyB, workCenterId));
            var crossed = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => port.Value.GetWorkCenterCapacityAsync(CompanyA, workCenterId));
            Assert.Equal(WorkCenterCapacityProblemCodesV1.AccessDenied, crossed.Code);
        }

        Assert.Equal(2, await run.ScalarAsync<int>(CountRows));

        // The rollback refuses while any row exists (forward fix); the rows stay. Only an empty store passes.
        var guard = Assert.IsType<SqlOperation>(new AddWorkCenterCapacityVersions().DownOperations[0]).Sql;
        var refusedRollback = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(DownGuardNumber, refusedRollback.Number);
        Assert.Equal(2, await run.ScalarAsync<int>(CountRows));
        await run.ExecuteAsync("DELETE FROM [settings].[WorkCenterCapacityVersions];");
        await run.ExecuteAsync(guard);
    }

    [SqlServerFact]
    public async Task The_work_center_capacity_migration_Down_rolls_back_and_reapplies_freely_on_an_empty_store_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        await using var context = run.SettingsContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, name => name.EndsWith("_" + Migration, StringComparison.Ordinal));
        Assert.True(index > 0, "the migration is applied after an earlier Settings migration");
        await context.GetService<IMigrator>().MigrateAsync(applied[index - 1]);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'WorkCenterCapacityVersions' AND SCHEMA_NAME([schema_id]) = N'settings'"));
        await context.GetService<IMigrator>().MigrateAsync(applied[^1]);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'WorkCenterCapacityVersions' AND SCHEMA_NAME([schema_id]) = N'settings'"));
    }

    [SqlServerFact]
    public async Task Work_center_safety_buffer_constraint_rejects_partial_null_pairs_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        const string insert = "INSERT INTO [settings].[WorkCenterCapacityVersions] "
            + "([CompanyId],[WorkCenterId],[Version],[ProfileId],[InputCapacityKgPerHour],"
            + "[SafetyBufferValue],[SafetyBufferUnit],[RecordedAtUtc],[RecordedBy],[Reason]) "
            + "VALUES (@c,@w,1,@p,1000,@v,@u,SYSUTCDATETIME(),N'synthetic',N'SENTETIK constraint regression')";
        foreach (var pair in new (object Value, object Unit)[]
                 { (DBNull.Value, "Hours"), (1m, DBNull.Value), (0m, "Hours"), (1m, "Weeks") })
        {
            var refusal = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(insert,
                ("@c", CompanyA), ("@w", Guid.NewGuid()), ("@p", Guid.NewGuid()), ("@v", pair.Value), ("@u", pair.Unit)));
            Assert.Equal(547, refusal.Number);
            Assert.Contains("CK_SettingsWorkCenterCapacityVersions_SafetyBuffer", refusal.Message, StringComparison.Ordinal);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));
        foreach (var pair in new (object Value, object Unit)[]
                 { (DBNull.Value, DBNull.Value), (1m, "Hours"), (1m, "Days") })
        {
            await run.ExecuteAsync(insert, ("@c", CompanyA), ("@w", Guid.NewGuid()), ("@p", Guid.NewGuid()),
                ("@v", pair.Value), ("@u", pair.Unit));
        }

        Assert.Equal(3, await run.ScalarAsync<int>(CountRows));
    }

    [SqlServerFact]
    public async Task Work_center_history_rejects_cross_company_and_unresolved_writes_and_persisted_mutation_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        static WorkCenterCapacityVersionRow Row(Guid company) => new()
        {
            CompanyId = company, WorkCenterId = Guid.NewGuid(), ProfileId = Guid.NewGuid(), Version = 1,
            InputCapacityKgPerHour = 1000, RecordedAtUtc = DateTimeOffset.UtcNow,
            RecordedBy = "synthetic", Reason = "SENTETIK write fence",
        };
        await using (var foreign = run.SettingsContext(CompanyA))
        {
            foreign.Add(Row(CompanyB));
            await Assert.ThrowsAsync<InvalidOperationException>(() => foreign.SaveChangesAsync());
        }

        await using (var unresolved = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
                         .UseSqlServer(run.Factory.DatabaseConnectionString).Options, new CompanyContext()))
        {
            unresolved.Add(Row(CompanyA));
            await Assert.ThrowsAsync<InvalidOperationException>(() => unresolved.SaveChangesAsync());
        }

        await using var own = run.SettingsContext(CompanyA);
        var row = Row(CompanyA);
        own.Add(row);
        await own.SaveChangesAsync();
        row.InputCapacityKgPerHour = 2000;
        await Assert.ThrowsAsync<InvalidOperationException>(() => own.SaveChangesAsync());
        own.Entry(row).State = EntityState.Unchanged;
        own.Remove(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => own.SaveChangesAsync());
        Assert.Equal(1, await run.ScalarAsync<int>(CountRows));
        Assert.Equal(1000m, await run.ScalarAsync<decimal>(
            "SELECT [InputCapacityKgPerHour] FROM [settings].[WorkCenterCapacityVersions] WHERE [CompanyId] = @c", ("@c", CompanyA)));
    }

    private static HttpRequestMessage Json(string path, string key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"work-center-capacity-{key}");
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
            var run = new HostRun(baseConnectionString, new SqlServerRelationalApiFactory(baseConnectionString));
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

        /// <summary>The company's first work center by code, read through the Organization owner's own context.</summary>
        public async Task<Guid> FirstWorkCenterAsync(Guid company)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
            return await scope.ServiceProvider.GetRequiredService<OrganizationDbContext>().WorkCenters
                .AsNoTracking()
                .OrderBy(workCenter => workCenter.Code)
                .Select(workCenter => workCenter.Id)
                .FirstAsync();
        }

        public async Task CommandAsync(HttpClient client, string path, string key, object body, HttpStatusCode expected,
            bool replayed = false)
        {
            using var response = await client.SendAsync(Json(path, key, body));
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

        public PortScope<ISettingsWorkCenterCapacityProviderV1> Port(Guid company)
        {
            var database = NewSettingsContext(company, out var companyContext);
            return new PortScope<ISettingsWorkCenterCapacityProviderV1>(database,
                new SettingsWorkCenterCapacityProviderV1(database, companyContext, new FixedActor("SENTETIK-wcc", company)));
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
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK iş merkezi kapasitesi kullanıcısı", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer,
                    developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"WCC-{roleId:N}", "SENTETIK iş merkezi kapasitesi rolü", true);
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

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
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
