using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KLN-D38 (Sinan, 2026-11-14) Cari VKN/TCKN change flow against real SQL Server through the real API: a NEW legal person's
/// cari succeeds the old one in the active company under the separate <c>counterparty.parties.succession.record</c>
/// permission (granted here only on the disposable database; no shipped role carries it). The old account becomes passive
/// in the same save as the link, one audit event and one passive-cari outbox event; the Idempotency-Key replays; nothing
/// moves a balance (the balance moves only by an approved transfer voucher through a Finance port). The identity-history
/// migration rolls back only while its three tables are empty.
/// </summary>
public sealed class CounterpartyIdentityHistorySqlServerRelationalAcceptanceTests
{
    private const string PreviousMigration = "20261115132003_AddCounterpartyOutbox";
    private const string CommandName = "counterparty.party.succession.record.v1";
    private const string AuditEventType = "counterparty.party.succession.recorded.v1";
    private const string EffectiveOn = "2026-01-02";

    [SqlServerFact]
    public async Task Dated_history_race_classifier_accepts_only_its_three_real_unique_indexes_and_rejects_other_sql_failures()
    {
        await using var run = await Run.StartAsync(grantSuccession: false);
        using var client = run.Client();
        var predecessor = await run.CreatePartyAsync(client, "SQL Tarihçe yarış sınıflaması öncül");
        var successor = await run.CreatePartyAsync(client, "SQL Tarihçe yarış sınıflaması halef");
        var profileId = Guid.NewGuid();
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var context = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            var profile = new CustomerEDocumentProfile(profileId, "1234567890", DateTimeOffset.UtcNow);
            profile.RecordStatus(EDocumentStatus.EInvoiceTaxpayer, "synthetic-classifier", TestCompanyIds.CompanyA, DateTimeOffset.UtcNow);
            context.CustomerEDocumentProfiles.Add(profile);
            await context.SaveChangesAsync();
        }
        await run.ExecuteAsync("""
            INSERT INTO counterparty.PartyIdentityRevisions
                (Id, PartyId, Attribute, Sequence, Value, Kind, RecordedAtUtc)
            VALUES (NEWID(), @Party, 2, 1, N'Sentetik unvan', 1, SYSDATETIMEOFFSET());
            INSERT INTO counterparty.CustomerEDocumentStatusRevisions
                (Id, ProfileId, Sequence, Status, EffectiveFrom, Kind, RecordedAtUtc)
            VALUES (NEWID(), @Profile, 1, 0, '2026-01-02', 1, SYSDATETIMEOFFSET());
            """, new SqlParameter("@Party", predecessor), new SqlParameter("@Profile", profileId));
        await run.InsertSuccessionAsync(predecessor, successor, "Sentetik sınıflama kaynağı");

        foreach (var entityType in new[] { typeof(PartyIdentityRevision), typeof(CustomerEDocumentStatusRevision), typeof(PartySuccession) })
        {
            await using var scope = run.Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var context = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            object entity = entityType == typeof(PartyIdentityRevision)
                ? await context.PartyIdentityRevisions.AsNoTracking().SingleAsync(row => row.PartyId == predecessor)
                : entityType == typeof(CustomerEDocumentStatusRevision)
                    ? await context.CustomerEDocumentStatusRevisions.AsNoTracking().SingleAsync(row => row.ProfileId == profileId)
                    : await context.PartySuccessions.AsNoTracking().SingleAsync(row => row.PredecessorPartyId == predecessor);
            var entry = context.Entry(entity);
            entry.Property("Id").CurrentValue = Guid.NewGuid();
            entry.State = EntityState.Added;
            var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Contains(Assert.IsType<SqlException>(duplicate.InnerException).Number, new[] { 2601, 2627 });
            Assert.True(CounterpartyCommandService.IsDatedHistoryRace(duplicate));
            Assert.False(CounterpartyCommandService.IsDatedHistoryRace(new DbUpdateException(
                "Same real SQL index failure without a matching dated entity.", duplicate.InnerException,
                new[] { context.Entry(await context.Parties.SingleAsync(row => row.Id == successor)) })));
            context.ChangeTracker.Clear();
        }

        // These are real provider failures with a dated entry, not fabricated SqlExceptions.
        // Keep an existing PK for the wrong-unique-index case; otherwise give the clone a fresh key
        // and unused sequence so the intended CHECK / length failure wins over uniqueness.
        foreach (var failure in new[] { "wrong_unique_index", "check", "length" })
        {
            await using var scope = run.Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var context = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            var clone = await context.PartyIdentityRevisions.AsNoTracking().SingleAsync(row => row.PartyId == predecessor);
            var entry = context.Entry(clone);
            entry.Property("Sequence").CurrentValue = 2L;
            if (failure != "wrong_unique_index")
            {
                entry.Property("Id").CurrentValue = Guid.NewGuid();
                entry.Property("Sequence").CurrentValue = failure == "check" ? 0L : 2L;
                if (failure == "length") entry.Property("Value").CurrentValue = new string('x', PartyIdentityRevision.ValueMaximumLength + 1);
            }
            entry.State = EntityState.Added;
            var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            var number = Assert.IsType<SqlException>(rejected.InnerException).Number;
            Assert.Contains(number, failure switch
            {
                "wrong_unique_index" => new[] { 2601, 2627 },
                "check" => new[] { 547 },
                _ => new[] { 8152, 2628 },
            });
            Assert.False(CounterpartyCommandService.IsDatedHistoryRace(rejected));
            context.ChangeTracker.Clear();
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM counterparty.PartyIdentityRevisions WHERE PartyId=@Party", new SqlParameter("@Party", predecessor)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM counterparty.CustomerEDocumentStatusRevisions WHERE ProfileId=@Profile", new SqlParameter("@Profile", profileId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM counterparty.PartySuccessions WHERE PredecessorPartyId=@Party", new SqlParameter("@Party", predecessor)));
    }

    [SqlServerFact]
    public async Task A_successor_closes_the_old_account_once_with_replay_one_audit_and_one_passive_event()
    {
        await using var run = await Run.StartAsync(grantSuccession: true);
        using var admin = run.Client();
        var predecessor = await run.CreatePartyAsync(admin, "SQL Halef Öncül Şahıs İşletmesi");
        var successor = await run.CreatePartyAsync(admin, "SQL Halef Yeni Anonim Şirketi");

        var before = await ReadHistoryAsync(admin, predecessor);
        Assert.True(before.GetProperty("accountIsActive").GetBoolean());
        Assert.True(before.GetProperty("viewer").GetProperty("canRecordSuccession").GetBoolean());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("successor").ValueKind);
        Assert.Empty(before.GetProperty("revisions").EnumerateArray());
        var version = before.GetProperty("accountVersion").GetString()!;

        var key = NewKey();
        var body = Body(version, successor, "Merger", "  Ticaret sicili gazetesi birleşme ilanı  ");
        using (var recorded = await PutSuccessionAsync(admin, predecessor, body, key))
        {
            Assert.Equal(HttpStatusCode.NoContent, recorded.StatusCode);
        }

        using (var replay = await PutSuccessionAsync(admin, predecessor, body, key))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        }

        // The same key with another body is not a replay.
        using (var reused = await PutSuccessionAsync(
                   admin, predecessor, Body(version, successor, "Death", "Başka gerekçe"), key))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, CounterpartyProblemCodesV1.IdempotencyConflict);
        }

        var after = await ReadHistoryAsync(admin, predecessor);
        Assert.False(after.GetProperty("accountIsActive").GetBoolean());
        var link = after.GetProperty("successor");
        Assert.Equal(predecessor, link.GetProperty("predecessorPartyId").GetGuid());
        Assert.Equal(successor, link.GetProperty("successorPartyId").GetGuid());
        Assert.Equal(successor, link.GetProperty("otherPartyId").GetGuid());
        Assert.Equal("Merger", link.GetProperty("kind").GetString());
        Assert.Equal("Ticaret sicili gazetesi birleşme ilanı", link.GetProperty("legalBasis").GetString());
        Assert.Equal(EffectiveOn, link.GetProperty("effectiveOn").GetString());

        var successorView = await ReadHistoryAsync(admin, successor);
        Assert.True(successorView.GetProperty("accountIsActive").GetBoolean());
        var predecessorLink = Assert.Single(successorView.GetProperty("predecessors").EnumerateArray());
        Assert.Equal(predecessor, predecessorLink.GetProperty("otherPartyId").GetGuid());

        // One link, one audit event carrying the legal basis, one passive-cari outbox event, one stored command.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[PartySuccessions] WHERE [CompanyId]=@CompanyId "
            + "AND [PredecessorPartyId]=@Predecessor AND [SuccessorPartyId]=@Successor AND [Kind]=2 AND [EffectiveOn]=@EffectiveOn;",
            Parameters(predecessor, successor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@AuditEvent AND [CompanyId]=@CompanyId "
            + "AND [PermissionCode]=@Permission AND [Reason]=N'Ticaret sicili gazetesi birleşme ilanı';",
            Parameters(predecessor, successor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [CompanyId]=@CompanyId AND [EventType]=@OutboxEvent "
            + "AND [AggregateId]=(SELECT [Id] FROM [counterparty].[CompanyPartyAccounts] "
            + "WHERE [CompanyId]=@CompanyId AND [PartyId]=@Predecessor);",
            Parameters(predecessor, successor)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [CompanyId]=@CompanyId AND [EventType]=@OutboxEvent "
            + "AND [AggregateId]=(SELECT [Id] FROM [counterparty].[CompanyPartyAccounts] "
            + "WHERE [CompanyId]=@CompanyId AND [PartyId]=@Successor);",
            Parameters(predecessor, successor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command AND [CompanyId]=@CompanyId "
            + "AND [HttpStatusCode]=204;",
            Parameters(predecessor, successor)));

        // A second link for the same predecessor is a stored 409; a passive cari cannot be a successor; a cari cannot
        // succeed itself. None of them writes a link, an audit event or an outbox event.
        using (var again = await PutSuccessionAsync(admin, predecessor,
                   Body(after.GetProperty("accountVersion").GetString()!, successor, "Merger", "Tekrar"), NewKey()))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict, CounterpartyProblemCodesV1.SuccessionAlreadyRecorded);
        }

        var successorVersion = successorView.GetProperty("accountVersion").GetString()!;
        using (var reverse = await PutSuccessionAsync(admin, successor,
                   Body(successorVersion, predecessor, "Conversion", "Ters yön"), NewKey()))
        {
            await AssertProblemAsync(reverse, HttpStatusCode.Conflict, CounterpartyProblemCodesV1.SuccessionSuccessorInactive);
        }

        using (var self = await PutSuccessionAsync(admin, successor,
                   Body(successorVersion, successor, "Conversion", "Kendisi"), NewKey()))
        {
            await AssertProblemAsync(self, HttpStatusCode.UnprocessableEntity, CounterpartyProblemCodesV1.SuccessionSameParty);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[PartySuccessions] WHERE [CompanyId]=@CompanyId;",
            Parameters(predecessor, successor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@AuditEvent;",
            Parameters(predecessor, successor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [CompanyId]=@CompanyId AND [EventType]=@OutboxEvent;",
            Parameters(predecessor, successor)));
    }

    [SqlServerFact]
    public async Task Without_the_succession_grant_the_command_is_403_and_writes_nothing()
    {
        await using var run = await Run.StartAsync(grantSuccession: false);
        using var admin = run.Client();
        var predecessor = await run.CreatePartyAsync(admin, "SQL Yetkisiz Halef Öncül Cari");
        var successor = await run.CreatePartyAsync(admin, "SQL Yetkisiz Halef Yeni Cari");

        var view = await ReadHistoryAsync(admin, predecessor);
        Assert.False(view.GetProperty("viewer").GetProperty("canRecordSuccession").GetBoolean());

        using (var refused = await PutSuccessionAsync(admin, predecessor,
                   Body(view.GetProperty("accountVersion").GetString()!, successor, "Conversion", "Şirkete dönüşüm"),
                   NewKey()))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        Assert.True((await ReadHistoryAsync(admin, predecessor)).GetProperty("accountIsActive").GetBoolean());
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[PartySuccessions];", Parameters(predecessor, successor)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@AuditEvent;", Parameters(predecessor, successor)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;",
            Parameters(predecessor, successor)));
    }

    [SqlServerFact]
    public async Task The_identity_history_migrations_Down_passes_with_empty_tables_and_refuses_while_a_succession_exists()
    {
        await using var run = await Run.StartAsync(grantSuccession: false);
        var guard = Assert.IsType<SqlOperation>(new AddCounterpartyIdentityHistoryAndSuccession().DownOperations[0]).Sql;

        // Empty history and succession tables roll back freely and migrate forward again.
        await run.ExecuteAsync(guard);
        await run.MigrateCounterpartyToAsync(PreviousMigration);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [object_id] IN (OBJECT_ID(N'[counterparty].[PartyIdentityRevisions]'), "
            + "OBJECT_ID(N'[counterparty].[CustomerEDocumentStatusRevisions]'), OBJECT_ID(N'[counterparty].[PartySuccessions]'));"));
        await run.MigrateCounterpartyToAsync(null);
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [object_id] IN (OBJECT_ID(N'[counterparty].[PartyIdentityRevisions]'), "
            + "OBJECT_ID(N'[counterparty].[CustomerEDocumentStatusRevisions]'), OBJECT_ID(N'[counterparty].[PartySuccessions]'));"));

        using var admin = run.Client();
        var predecessor = await run.CreatePartyAsync(admin, "SQL Geri Alma Halef Öncül Cari");
        var successor = await run.CreatePartyAsync(admin, "SQL Geri Alma Halef Yeni Cari");

        // The database itself refuses a self-succession and a blank legal basis.
        var selfLink = await Assert.ThrowsAsync<SqlException>(() => run.InsertSuccessionAsync(predecessor, predecessor, "Gerekçe"));
        Assert.Equal(547, selfLink.Number);
        var blank = await Assert.ThrowsAsync<SqlException>(() => run.InsertSuccessionAsync(predecessor, successor, ""));
        Assert.Equal(547, blank.Number);

        await run.InsertSuccessionAsync(predecessor, successor, "Birleşme ilanı");
        var duplicate = await Assert.ThrowsAsync<SqlException>(() =>
            run.InsertSuccessionAsync(predecessor, successor, "İkinci bağ"));
        Assert.Equal(2601, duplicate.Number);

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51530, refused.Number);
        var migratorRefused = await Assert.ThrowsAnyAsync<SqlException>(() =>
            run.MigrateCounterpartyToAsync(PreviousMigration));
        Assert.Equal(51530, migratorRefused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[PartySuccessions];", Parameters(predecessor, successor)));
    }

    private static string NewKey() => $"cari-halef-{Guid.NewGuid():N}";

    private static string Body(string expectedVersion, Guid successorPartyId, string kind, string legalBasis) =>
        JsonSerializer.Serialize(new
        {
            expectedVersion,
            successorPartyId,
            kind,
            legalBasis,
            effectiveOn = EffectiveOn,
        });

    private static SqlParameter[] Parameters(Guid predecessor, Guid successor) =>
    [
        new("@CompanyId", TestCompanyIds.CompanyA),
        new("@Predecessor", predecessor),
        new("@Successor", successor),
        new("@EffectiveOn", DateOnly.Parse(EffectiveOn, System.Globalization.CultureInfo.InvariantCulture)
            .ToDateTime(TimeOnly.MinValue)),
        new("@AuditEvent", AuditEventType),
        new("@OutboxEvent", CounterpartyPartyDeactivatedOutboxV1.EventType),
        new("@Command", CommandName),
        new("@Permission", ErpPermissions.CounterpartyPartiesSuccessionRecord),
    ];

    private static async Task<JsonElement> ReadHistoryAsync(HttpClient client, Guid partyId)
    {
        using var response = await client.GetAsync($"/api/v1/counterparties/{partyId}/identity-history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    private static Task<HttpResponseMessage> PutSuccessionAsync(HttpClient client, Guid partyId, string body, string key)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", $"cari-halef-correlation-{Guid.NewGuid():N}");
        return client.PutAsync(
            $"/api/v1/counterparties/{partyId}/succession",
            new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("code").GetString());
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string _baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            _baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync(bool grantSuccession)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var run = new Run(
                baseConnectionString,
                new SqlServerRelationalApiFactory(baseConnectionString, services =>
                {
                    if (grantSuccession)
                    {
                        // Test-only grant on the disposable database; no shipped seed role is changed.
                        services.PostConfigure<IdentitySeedOptions>(options =>
                            options.Roles.Single(role => role.Code == "SYSTEM_ADMINISTRATOR")
                                .Permissions.Add(ErpPermissions.CounterpartyPartiesSuccessionRecord));
                    }
                }));
            using var health = run.Factory.CreateClient();
            using var live = await health.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            return run;
        }

        public HttpClient Client() => Factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);

        public async Task<Guid> CreatePartyAsync(HttpClient client, string name)
        {
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add("Idempotency-Key", NewKey());
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            client.DefaultRequestHeaders.Add("X-Correlation-Id", $"cari-halef-create-{Guid.NewGuid():N}");
            using var response = await client.PostAsJsonAsync(
                "/api/v1/counterparties",
                new CreateCounterparty(PartyKind.LegalEntity, name, null, PartyRole.Customer));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("id").GetGuid();
        }

        public Task InsertSuccessionAsync(Guid predecessor, Guid successor, string legalBasis) =>
            ExecuteAsync(
                """
                INSERT INTO [counterparty].[PartySuccessions]
                    ([Id], [CompanyId], [PredecessorPartyId], [SuccessorPartyId], [Kind], [LegalBasis], [EffectiveOn],
                     [RecordedByUserId], [RecordedAtUtc])
                VALUES
                    (NEWID(), @CompanyId, @Predecessor, @Successor, 2, @LegalBasis, @EffectiveOn,
                     N'sql-raw-actor', SYSDATETIMEOFFSET());
                """,
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
                new SqlParameter("@Predecessor", predecessor),
                new SqlParameter("@Successor", successor),
                new SqlParameter("@LegalBasis", legalBasis),
                new SqlParameter("@EffectiveOn", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified)));

        public async Task MigrateCounterpartyToAsync(string? migration)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            await dbContext.GetService<IMigrator>().MigrateAsync(migration);
        }

        public async Task<T> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddRange(parameters.Where(parameter =>
                sql.Contains(parameter.ParameterName, StringComparison.Ordinal)).ToArray());
            return Assert.IsType<T>(await command.ExecuteScalarAsync());
        }

        public async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddRange(parameters);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            var databaseName = Factory.DatabaseName;
            await Factory.DisposeAsync();
            Assert.Matches("^goymen_erp_ci_[a-f0-9]{32}$", databaseName);
            var builder = new SqlConnectionStringBuilder(_baseConnectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
