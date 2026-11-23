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

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FNS-D52 (F-014) against a REAL SQL Server through the REAL host: no bank CSV mapping is compiled or seeded (the list is
/// empty and the read port refuses until a person records a bank's first version); a reasoned record is one save (row +
/// execution + audit), the same key replays, a changed body under the same key is 409, a stale version 409, a broken mapping
/// 400 before anything is stored, an unchanged mapping a stored no-op without audit, versions are counted per bank, an
/// Inactive version stops the bank, another company sees nothing, 403 without the name, the read port answers the bank's
/// latest version as typed values, and the migration's Down refuses (51527) while any version exists. Every grant is a fresh
/// synthetic role; every bank key, name and reason is SENTETIK; every column number is test data, not a real bank's layout.
/// </summary>
public sealed class SettingsBankCsvMappingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Root = SettingsBankCsvMappingEndpoints.BankCsvMappingsPath;
    private const string Versions = Root + "/versions";
    private const string Migration = "AddBankCsvMappingVersions";
    private const string Manage = SettingsBankCsvMappingPermissionManifestV1.Manage;
    private const string RecordedEvent = "settings.bank-csv-mapping.recorded.v1";
    private const string BankOne = "SENTETIK-BIR";
    private const string BankTwo = "SENTETIK-IKI";
    private const string CountRows = "SELECT COUNT(*) FROM [settings].[BankCsvMappingVersions]";

    [SqlServerFact]
    public async Task Bank_csv_mapping_record_replay_per_bank_versions_isolation_audit_provider_and_Down_guard_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var (editorSubject, editorActor) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SettingsExchangeRatePermissionManifestV1.Read);
        var (editorBSubject, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Manage);
        using var editor = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        using var editorB = run.Client(editorBSubject, CompanyB);
        var record = new RecordBankCsvMappingRequestV1("0", BankOne, "SENTETIK Birinci Banka", BankCsvMappingStatesV1.Active,
            BankCsvDelimitersV1.Semicolon, 1, true, BankCsvDecimalSeparatorsV1.Comma, BankCsvThousandsSeparatorsV1.Period,
            "dd.MM.yyyy", BankCsvAmountLayoutsV1.SignedAmount, BankCsvSignedAmountDirectionsV1.Inflow, 1, 2, 3, 4, null, null, 5,
            null, null, "SENTETIK banka CSV eşlemesi");

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

        // Nothing compiled or seeded: an empty list, and the read port refuses every bank.
        var empty = await run.JsonAsync(editor, Root);
        Assert.Empty(empty.GetProperty("banks").EnumerateArray());
        await run.AssertPortRefusesAsync(CompanyA, BankOne, SettingsPrerequisiteCodesV1.BankCsvMappingNotFound);
        await using (var port = run.Port(CompanyA))
        {
            Assert.Empty(await port.Value.ListActiveBankCsvMappingsAsync());
        }

        // A non-exact key, a broken mapping or a missing reason is refused before anything is stored (not replayed).
        using (var lowerCase = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-key",
                   record with { BankKey = "sentetik-bir" })))
        {
            await AssertProblemAsync(lowerCase, HttpStatusCode.BadRequest, BankCsvMappingProblemCodesV1.RequestInvalid);
        }

        using (var twice = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-twice", record with { CurrencyColumn = 3 })))
        {
            await AssertProblemAsync(twice, HttpStatusCode.BadRequest, BankCsvMappingProblemCodesV1.RequestInvalid);
            Assert.False(twice.Headers.Contains("Idempotency-Replayed"));
        }

        using (var noDirection = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-direction",
                   record with { SignedAmountPositiveMeans = null })))
        {
            await AssertProblemAsync(noDirection, HttpStatusCode.BadRequest, BankCsvMappingProblemCodesV1.RequestInvalid);
        }

        using (var noReason = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-reason", record with { Reason = " " })))
        {
            await AssertProblemAsync(noReason, HttpStatusCode.BadRequest, BankCsvMappingProblemCodesV1.ReasonRequired);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        // The first version: one row + one audit event; the same key replays; a changed body under it is 409.
        await run.CommandAsync(editor, Versions, "k-r-1", record, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, Versions, "k-r-1", record, HttpStatusCode.NoContent, replayed: true);
        using (var reused = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-r-1", record with { BalanceColumn = 6 })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, BankCsvMappingProblemCodesV1.IdempotencyConflict);
        }

        using (var stale = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-r-stale", record)))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, BankCsvMappingProblemCodesV1.VersionConflict);
        }

        using (var replayedStale = await editor.SendAsync(Json(HttpMethod.Post, Versions, "k-r-stale", record)))
        {
            await AssertProblemAsync(replayedStale, HttpStatusCode.Conflict, BankCsvMappingProblemCodesV1.VersionConflict);
            Assert.True(replayedStale.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[BankCsvMappingVersions] WHERE [CompanyId] = @c AND [BankKey] = @k AND [Version] = 1 "
            + "AND [BankDisplayName] = N'SENTETIK Birinci Banka' AND [State] = 'Active' AND [Delimiter] = 'Semicolon' "
            + "AND [SkipLeadingRows] = 1 AND [HasHeaderRow] = 1 AND [DecimalSeparator] = 'Comma' AND [ThousandsSeparator] = 'Period' "
            + "AND [DateFormat] = 'dd.MM.yyyy' AND [AmountLayout] = 'SignedAmount' AND [SignedAmountPositiveMeans] = 'Inflow' "
            + "AND [TransactionDateColumn] = 1 AND [ValueDateColumn] = 2 AND [DescriptionColumn] = 3 AND [AmountColumn] = 4 "
            + "AND [InflowColumn] IS NULL AND [OutflowColumn] IS NULL AND [BalanceColumn] = 5 AND [ReferenceColumn] IS NULL "
            + "AND [CurrencyColumn] IS NULL AND [RecordedBy] = @a",
            ("@c", CompanyA), ("@k", BankOne), ("@a", editorActor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c AND [PermissionCode] = @p "
            + "AND [EntityId] = @s AND [EntityVersionBefore] IS NULL AND [EntityVersionAfter] = 1",
            ("@e", RecordedEvent), ("@c", CompanyA), ("@p", Manage), ("@s", CompanyA.ToString("D") + "/" + BankOne)));

        // The same values again under a new key: a stored no-op (204), no row, no audit.
        await run.CommandAsync(editor, Versions, "k-noop", record with { ExpectedVersion = "1" }, HttpStatusCode.NoContent);
        Assert.Equal(1, await run.ScalarAsync<int>(CountRows));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c", ("@e", RecordedEvent),
            ("@c", CompanyA)));

        var recorded = await run.JsonAsync(editor, Root);
        var bank = Assert.Single(recorded.GetProperty("banks").EnumerateArray());
        Assert.Equal((BankOne, "1"), (bank.GetProperty("bankKey").GetString(), bank.GetProperty("version").GetString()));
        var first = Assert.Single(bank.GetProperty("versions").EnumerateArray());
        Assert.Equal(("1", "SENTETIK Birinci Banka", BankCsvMappingStatesV1.Active, BankCsvAmountLayoutsV1.SignedAmount,
                BankCsvSignedAmountDirectionsV1.Inflow, 4),
            (first.GetProperty("version").GetString(), first.GetProperty("bankDisplayName").GetString(),
                first.GetProperty("state").GetString(), first.GetProperty("amountLayout").GetString(),
                first.GetProperty("signedAmountPositiveMeans").GetString(), first.GetProperty("amountColumn").GetInt32()));

        // The read port answers the bank's stored latest version as typed values.
        await using (var port = run.Port(CompanyA))
        {
            var mapping = await port.Value.GetBankCsvMappingAsync(BankOne);
            Assert.Equal((CompanyA, 1L, BankOne, "SENTETIK Birinci Banka"),
                (mapping.CompanyId, mapping.Profile.Version, mapping.BankKey, mapping.BankDisplayName));
            Assert.Equal((';', 1, true, ',', (char?)'.', "dd.MM.yyyy"),
                (mapping.Delimiter, mapping.SkipLeadingRows, mapping.HasHeaderRow, mapping.DecimalSeparator,
                    mapping.ThousandsSeparator, mapping.DateFormat));
            Assert.Equal(SettingsBankCsvAmountLayoutV1.SignedAmount, mapping.AmountLayout);
            Assert.Equal(SettingsBankCsvSignedAmountDirectionV1.Inflow, mapping.SignedAmountPositiveMeans);
            Assert.Equal(new SettingsBankCsvColumnsV1(1, 2, 3, 4, null, null, 5, null, null), mapping.Columns);
            var active = Assert.Single(await port.Value.ListActiveBankCsvMappingsAsync());
            Assert.Equal((BankOne, 1L), (active.BankKey, active.Profile.Version));
        }

        await run.AssertPortRefusesAsync(CompanyA, BankTwo, SettingsPrerequisiteCodesV1.BankCsvMappingNotFound);

        // Versions are counted per bank: a second bank starts at "0" with the other amount layout.
        var second = record with
        {
            BankKey = BankTwo, BankDisplayName = "SENTETIK İkinci Banka", Delimiter = BankCsvDelimitersV1.Comma,
            DecimalSeparator = BankCsvDecimalSeparatorsV1.Period, ThousandsSeparator = BankCsvThousandsSeparatorsV1.None,
            DateFormat = "yyyy-MM-dd", AmountLayout = BankCsvAmountLayoutsV1.InflowOutflowColumns,
            SignedAmountPositiveMeans = null, AmountColumn = null, InflowColumn = 4, OutflowColumn = 6,
            Reason = "SENTETIK ikinci banka eşlemesi",
        };
        await run.CommandAsync(editor, Versions, "k-two-1", second, HttpStatusCode.NoContent);
        await using (var port = run.Port(CompanyA))
        {
            var mapping = await port.Value.GetBankCsvMappingAsync(BankTwo);
            Assert.Equal((1L, ',', '.', (char?)null, SettingsBankCsvAmountLayoutV1.InflowOutflowColumns,
                    (SettingsBankCsvSignedAmountDirectionV1?)null),
                (mapping.Profile.Version, mapping.Delimiter, mapping.DecimalSeparator, mapping.ThousandsSeparator,
                    mapping.AmountLayout, mapping.SignedAmountPositiveMeans));
            Assert.Equal(new SettingsBankCsvColumnsV1(1, 2, 3, null, 4, 6, 5, null, null), mapping.Columns);
            Assert.Equal([BankOne, BankTwo], (await port.Value.ListActiveBankCsvMappingsAsync()).Select(summary => summary.BankKey));
        }

        // Stopping a bank is a new Inactive version (never a delete): the read port refuses it, the list leaves it out.
        await run.CommandAsync(editor, Versions, "k-r-2", record with
        {
            ExpectedVersion = "1", State = BankCsvMappingStatesV1.Inactive, Reason = "SENTETIK eşleme durduruldu",
        }, HttpStatusCode.NoContent);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c AND [EntityVersionBefore] = 1 "
            + "AND [EntityVersionAfter] = 2", ("@e", RecordedEvent), ("@c", CompanyA)));
        await run.AssertPortRefusesAsync(CompanyA, BankOne, SettingsPrerequisiteCodesV1.BankCsvMappingInactive);
        await using (var port = run.Port(CompanyA))
        {
            var active = Assert.Single(await port.Value.ListActiveBankCsvMappingsAsync());
            Assert.Equal(BankTwo, active.BankKey);
        }

        // Another company sees nothing of A's: its list is empty and its read port refuses.
        var other = await run.JsonAsync(editorB, Root);
        Assert.Empty(other.GetProperty("banks").EnumerateArray());
        await run.AssertPortRefusesAsync(CompanyB, BankTwo, SettingsPrerequisiteCodesV1.BankCsvMappingNotFound);

        // The rollback refuses while any row exists (forward fix); the rows stay. Only an empty store passes.
        var guard = Assert.IsType<SqlOperation>(new AddBankCsvMappingVersions().DownOperations[0]).Sql;
        var refusedRollback = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51527, refusedRollback.Number);
        Assert.Equal(3, await run.ScalarAsync<int>(CountRows));
        await run.ExecuteAsync("DELETE FROM [settings].[BankCsvMappingVersions];");
        await run.ExecuteAsync(guard);
    }

    [SqlServerFact]
    public async Task The_bank_csv_mapping_migration_Down_rolls_back_and_reapplies_freely_on_an_empty_store_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        await using var context = run.SettingsContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, name => name.EndsWith("_" + Migration, StringComparison.Ordinal));
        Assert.True(index > 0, "the migration is applied after an earlier Settings migration");
        await context.GetService<IMigrator>().MigrateAsync(applied[index - 1]);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'BankCsvMappingVersions' AND SCHEMA_NAME([schema_id]) = N'settings'"));
        await context.GetService<IMigrator>().MigrateAsync(applied[^1]);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'BankCsvMappingVersions' AND SCHEMA_NAME([schema_id]) = N'settings'"));
    }

    private static HttpRequestMessage Json(HttpMethod method, string path, string key, object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"bank-csv-mapping-{key}");
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

        public PortScope<ISettingsBankCsvMappingProviderV1> Port(Guid company)
        {
            var database = NewSettingsContext(company, out var companyContext);
            return new PortScope<ISettingsBankCsvMappingProviderV1>(database,
                new SettingsBankCsvMappingProviderV1(database, companyContext, new FixedActor("SENTETIK-bcm", company)));
        }

        public async Task AssertPortRefusesAsync(Guid company, string bankKey, string code)
        {
            await using var port = Port(company);
            var refused = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => port.Value.GetBankCsvMappingAsync(bankKey));
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
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK banka CSV eşleme kullanıcısı", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer,
                    developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"BCM-{roleId:N}", "SENTETIK banka CSV eşleme rolü", true);
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
