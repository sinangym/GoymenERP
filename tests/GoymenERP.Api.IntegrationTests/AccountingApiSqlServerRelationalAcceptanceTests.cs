using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ACC-V0 against the REAL host and SQL Server: the Accounting periods over authorized HTTP (Ö4: proposed
/// by one user, approved by ANOTHER; Ö5: closed by one authorised act), the ADR-011 answers of every key,
/// company isolation, the narrow read names, the journal and queue reads of what the ledger stored, and
/// the Ö2 scale check against Settings' REAL approved GDC/TRY money profile. Every identity is synthetic
/// (SENTETIK) in this test's own database. This is API and SQL evidence, not the browser user outcome.
/// </summary>
public sealed class AccountingApiSqlServerRelationalAcceptanceTests
{
    private const string PeriodsPath = "/api/v1/accounting/periods";
    private const string JournalsPath = "/api/v1/accounting/journals";
    private const string QueuesPath = "/api/v1/accounting/posting-queues";
    private const string Preparer = TestUsers.CompanyA;
    private const string Approver = TestUsers.CompanyAViewer;
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Lira = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly DateOnly September1 = new DateOnly(2026, 9, 1);
    private static readonly DateOnly September30 = new DateOnly(2026, 9, 30);

    private static readonly string[] PreparerGrants =
    [
        AccountingPermissionManifestV1.PeriodsRead, AccountingPermissionManifestV1.PeriodsPropose,
        AccountingPermissionManifestV1.PeriodsApprove,
    ];

    private static readonly string[] ApproverGrants =
    [
        AccountingPermissionManifestV1.PeriodsRead, AccountingPermissionManifestV1.PeriodsApprove,
        AccountingPermissionManifestV1.PeriodsClose,
    ];

    private static readonly JsonSerializerOptions Web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>
    /// Ö4 and Ö5 over HTTP with every ADR-011 answer: a proposal replays; its preparer's approval is 403
    /// although the preparer holds the approve grant; another user's stale approval and an approval of an
    /// unknown period are the key's terminal answers (§7); the approval replays; a second approval is
    /// refused; the close is one act; an overlapping proposal is refused; one key never carries two requests.
    /// </summary>
    [SqlServerFact]
    public async Task A_period_is_proposed_approved_by_another_user_and_closed_and_every_answer_of_a_key_holds()
    {
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Preparer, CompanyA, PreparerGrants);
        await run.GrantAsync(Approver, CompanyA, ApproverGrants);
        using var preparer = run.Client(Preparer, CompanyA);
        using var approver = run.Client(Approver, CompanyA);
        var periodId = Guid.NewGuid();

        // Propose; the same key again is the stored answer with the first correlation.
        var proposeCorrelation = Guid.NewGuid().ToString("N");
        using (var proposed = await preparer.SendAsync(Post(PeriodsPath, "SENTETIK-p-1", proposeCorrelation,
                   new ProposePeriodRequestV1(periodId, September1, September30))))
        {
            Assert.Equal(HttpStatusCode.Created, proposed.StatusCode);
            Assert.True(proposed.Headers.CacheControl is { Private: true, NoStore: true });
            Assert.Equal($"{PeriodsPath}/{periodId:D}", proposed.Headers.Location!.OriginalString);
            var period = await Json(proposed);
            Assert.Equal(("Proposed", 1L, true), (period.GetProperty("status").GetString(),
                period.GetProperty("version").GetInt64(), period.GetProperty("isOwnProposal").GetBoolean()));
            Assert.False(proposed.Headers.Contains("Idempotency-Replayed"));
        }

        using (var replayed = await preparer.SendAsync(Post(PeriodsPath, "SENTETIK-p-1", Guid.NewGuid().ToString("N"),
                   new ProposePeriodRequestV1(periodId, September1, September30))))
        {
            Assert.Equal(HttpStatusCode.Created, replayed.StatusCode);
            AssertReplayOf(replayed, proposeCorrelation);
        }

        // One key never carries two requests (§3): the same key with other dates discloses nothing.
        using (var other = await preparer.SendAsync(Post(PeriodsPath, "SENTETIK-p-1", Guid.NewGuid().ToString("N"),
                   new ProposePeriodRequestV1(periodId, September1, new DateOnly(2026, 9, 29)))))
        {
            Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
            Assert.Equal(AccountingApiCodes.IdempotencyConflict, Code(await Json(other)));
        }

        // A proposal covers its dates: an overlapping one is refused and stores nothing.
        using (var overlap = await preparer.SendAsync(Post(PeriodsPath, "SENTETIK-p-2", Guid.NewGuid().ToString("N"),
                   new ProposePeriodRequestV1(Guid.NewGuid(), new DateOnly(2026, 9, 15), new DateOnly(2026, 10, 14)))))
        {
            Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
            Assert.Equal(AccountingStoreCodes.PeriodOverlap, Code(await Json(overlap)));
        }

        // Each caller's list tells whether the proposal is theirs.
        Assert.True(SingleItem(await Json(await preparer.GetAsync(PeriodsPath))).GetProperty("isOwnProposal").GetBoolean());
        Assert.False(SingleItem(await Json(await approver.GetAsync(PeriodsPath))).GetProperty("isOwnProposal").GetBoolean());

        // Ö4: the preparer holds the approve grant, and is still refused; nothing is recorded for it.
        using (var own = await preparer.SendAsync(Approval(periodId, "SENTETIK-a-own", 1)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
            Assert.Equal("forbidden", Code(await Json(own)));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands WHERE CommandName = @n",
            ("@n", AccountingCommandNames.ApprovePeriod)));

        // §7: another user's stale version is the key's answer for good, even after the period moves on.
        var staleCorrelation = Guid.NewGuid().ToString("N");
        using (var stale = await approver.SendAsync(Approval(periodId, "SENTETIK-a-stale", 7, staleCorrelation)))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var problem = await Json(stale);
            Assert.Equal((AccountingApiCodes.VersionConflict, "7", "1"), (Code(problem),
                problem.GetProperty("expectedVersion").GetString(), problem.GetProperty("currentVersion").GetString()));
        }

        // §7: an unknown period is a code-less 404, the key's answer for good.
        var unknownCorrelation = Guid.NewGuid().ToString("N");
        var unknownPeriod = Guid.NewGuid();
        using (var unknown = await approver.SendAsync(Approval(unknownPeriod, "SENTETIK-a-unknown", 1, unknownCorrelation)))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        // The approval by another user opens the period; its key replays.
        var approveCorrelation = Guid.NewGuid().ToString("N");
        using (var approved = await approver.SendAsync(Approval(periodId, "SENTETIK-a-1", 1, approveCorrelation)))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var period = await Json(approved);
            Assert.Equal(("Open", 2L), (period.GetProperty("status").GetString(), period.GetProperty("version").GetInt64()));
        }

        using (var replayed = await approver.SendAsync(Approval(periodId, "SENTETIK-a-1", 1)))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            AssertReplayOf(replayed, approveCorrelation);
        }

        using (var staleAgain = await approver.SendAsync(Approval(periodId, "SENTETIK-a-stale", 7)))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleAgain.StatusCode);
            AssertReplayOf(staleAgain, staleCorrelation);
        }

        using (var unknownAgain = await approver.SendAsync(Approval(unknownPeriod, "SENTETIK-a-unknown", 1)))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknownAgain.StatusCode);
            AssertReplayOf(unknownAgain, unknownCorrelation);
        }

        using (var otherUnknown = await approver.SendAsync(Approval(Guid.NewGuid(), "SENTETIK-a-unknown", 1)))
        {
            // The same key with another period is another request: a conflict, never the stored 404.
            Assert.Equal(HttpStatusCode.Conflict, otherUnknown.StatusCode);
            Assert.Equal(AccountingApiCodes.IdempotencyConflict, Code(await Json(otherUnknown)));
        }

        // A second approval at the current version is refused by the rule: it is no longer a proposal.
        using (var twice = await approver.SendAsync(Approval(periodId, "SENTETIK-a-2", 2)))
        {
            Assert.Equal((HttpStatusCode)422, twice.StatusCode);
            Assert.Equal("accounting_period_not_proposed", Code(await Json(twice)));
        }

        // Ö5: one authorised close.
        using (var closed = await approver.SendAsync(Close(periodId, "SENTETIK-c-1", 2)))
        {
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            var period = await Json(closed);
            Assert.Equal(("Closed", 3L), (period.GetProperty("status").GetString(), period.GetProperty("version").GetInt64()));
        }

        // The preparer has no close grant: 403, before anything is read.
        using (var noGrant = await preparer.SendAsync(Close(periodId, "SENTETIK-c-2", 3)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, noGrant.StatusCode);
        }

        // The database holds Ö4 itself, and one audit event per stored change, the approval with its O4 context.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.Periods WHERE Id = @id AND Status = 2 AND ApprovedByUserId <> PreparedByUserId",
            ("@id", periodId)));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Accounting.Period' AND EntityId = @id",
            ("@id", periodId.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'accounting.period.approved.v1' AND EntityId = @id "
            + "AND JSON_VALUE(ApprovalContextJson, '$.rule') = N'O4'",
            ("@id", periodId.ToString("D"))));
        // Records: propose, approve and close succeeded; the stale approval and the unknown period are terminal.
        Assert.Equal(5, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Periods"));
    }

    /// <summary>
    /// Company B sees nothing of company A (a code-less 404 or an empty list), and every read needs its own
    /// narrow name: a period reader reads no journal, and a journal reader reads no queue (persona rule).
    /// </summary>
    [SqlServerFact]
    public async Task Another_company_sees_nothing_and_each_read_needs_its_own_name()
    {
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Preparer, CompanyA, PreparerGrants);
        await run.GrantAsync(TestUsers.CompanyB, CompanyB, [.. ApproverGrants, AccountingPermissionManifestV1.JournalsRead]);
        using var preparer = run.Client(Preparer, CompanyA);
        using var ofB = run.Client(TestUsers.CompanyB, CompanyB);
        var periodId = Guid.NewGuid();
        using (var proposed = await preparer.SendAsync(Post(PeriodsPath, "SENTETIK-p-a", Guid.NewGuid().ToString("N"),
                   new ProposePeriodRequestV1(periodId, September1, September30))))
        {
            Assert.Equal(HttpStatusCode.Created, proposed.StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await ofB.GetAsync($"{PeriodsPath}/{periodId:D}")).StatusCode);
        Assert.Empty((await Json(await ofB.GetAsync(PeriodsPath))).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await ofB.SendAsync(Approval(periodId, "SENTETIK-b-a", 1))).StatusCode);

        // The preparer reads periods, but no journal and no queue; B reads journals (its own: none), but no queue.
        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.GetAsync(JournalsPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.GetAsync(QueuesPath)).StatusCode);
        Assert.Empty((await Json(await ofB.GetAsync(JournalsPath))).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await ofB.GetAsync(QueuesPath)).StatusCode);

        // A malformed list request is refused, never read as "everything".
        Assert.Equal(HttpStatusCode.BadRequest, (await preparer.GetAsync($"{PeriodsPath}?pageSize=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await preparer.GetAsync($"{PeriodsPath}?cursor=not-a-cursor")).StatusCode);

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Periods WHERE CompanyId = @c", ("@c", CompanyA)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Periods WHERE CompanyId = @c", ("@c", CompanyB)));
    }

    /// <summary>
    /// The M-04 and M-01 reads answer exactly what the ledger stored, and Ö2 holds against Settings' REAL
    /// approved GDC/TRY money profile under Accounting's operation key: two decimals post, a third is
    /// refused and stores nothing; company B (no approved source) is refused with Settings' own code.
    /// </summary>
    [SqlServerFact]
    public async Task The_journal_and_queue_reads_answer_what_the_ledger_stored_at_the_real_money_scale()
    {
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Preparer, CompanyA, [.. PreparerGrants, AccountingPermissionManifestV1.JournalsRead,
            AccountingPermissionManifestV1.PostingQueuesRead]);
        await run.GrantAsync(Approver, CompanyA, ApproverGrants);
        using var preparer = run.Client(Preparer, CompanyA);
        using var approver = run.Client(Approver, CompanyA);
        var periodId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await preparer.SendAsync(Post(PeriodsPath, "SENTETIK-p-1", Guid.NewGuid().ToString("N"),
            new ProposePeriodRequestV1(periodId, September1, September30)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SendAsync(Approval(periodId, "SENTETIK-a-1", 1))).StatusCode);

        var debit = new AccountRef(CompanyA, Guid.NewGuid());
        var credit = new AccountRef(CompanyA, Guid.NewGuid());
        var source = Guid.NewGuid();
        Journal posted;
        await using (var store = run.Store(CompanyA))
        {
            // Ö2 with the REAL profile: 1250.50 TRY posts; 1250.505 is refused and stores nothing.
            var refused = await Assert.ThrowsAsync<AccountingAmountScaleException>(() => store.Postings.ReceiveAsync(
                Request(source, "m-scale", debit, credit, 1250.505m), periodId, run.Metadata()));
            Assert.Equal(AccountingAmountScaleCodes.ScaleExceeded, refused.Code);
            posted = (await store.Postings.ReceiveAsync(Request(source, "m-1", debit, credit, 1250.50m), periodId, run.Metadata())).Journal!;
            // A second fact without a credit mapping is an exception-queue entry, never a guessed account.
            var queued = await store.Postings.ReceiveAsync(Request(Guid.NewGuid(), "m-2", debit, null, 10m), periodId, run.Metadata());
            Assert.Equal(PostingOutcomeKind.ExceptionQueued, queued.Kind);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Deliveries WHERE MessageId = N'm-scale'"));

        await using (var ofB = run.Store(CompanyB))
        {
            var noSource = await Assert.ThrowsAsync<AccountingAmountScaleException>(() => ofB.Postings.ReceiveAsync(
                Request(Guid.NewGuid(), "m-b", new AccountRef(CompanyB, Guid.NewGuid()), new AccountRef(CompanyB, Guid.NewGuid()), 1m,
                    CompanyB), Guid.NewGuid(), run.Metadata()));
            Assert.Equal("settings_provider_unavailable", noSource.Code);
        }

        var page = await Json(await preparer.GetAsync($"{JournalsPath}?periodId={periodId:D}"));
        var summary = SingleItem(page);
        Assert.Equal((posted.Id, "Original", source, "synthetic-purpose-a", "1250.5", "1250.5", 2),
            (summary.GetProperty("journalId").GetGuid(), summary.GetProperty("kind").GetString(), summary.GetProperty("sourceId").GetGuid(),
                summary.GetProperty("postingPurpose").GetString(), summary.GetProperty("totalDebit").GetString(),
                summary.GetProperty("totalCredit").GetString(), summary.GetProperty("lineCount").GetInt32()));

        var detail = await Json(await preparer.GetAsync($"{JournalsPath}/{posted.Id:D}"));
        var lines = detail.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Equal(("1250.5", "0"), (lines[0].GetProperty("debit").GetString(), lines[0].GetProperty("credit").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await preparer.GetAsync($"{JournalsPath}/{Guid.NewGuid():D}")).StatusCode);

        var queues = await Json(await preparer.GetAsync(QueuesPath));
        var exception = Assert.Single(queues.GetProperty("exceptions").EnumerateArray());
        Assert.Equal(["synthetic-credit-key"], exception.GetProperty("missingMappingKeys").EnumerateArray().Select(key => key.GetString()));
        Assert.Empty(queues.GetProperty("pending").EnumerateArray());
    }

    private static PostingRequest Request(Guid source, string messageId, AccountRef debit, AccountRef? credit, decimal amount,
        Guid? company = null) =>
        new PostingRequest(
            "synthetic-accounting-consumer",
            messageId,
            Guid.NewGuid(),
            new SourceReference(company ?? CompanyA, source, "synthetic-purpose-a"),
            "synthetic-rule-1",
            Lira,
            new PostingDates(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 15), null,
                new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero)),
            [
                new ProposedLine("synthetic-debit-key", debit, PostingSide.Debit, amount),
                new ProposedLine("synthetic-credit-key", credit, PostingSide.Credit, amount),
            ]);

    private static HttpRequestMessage Post<T>(string path, string key, string correlation, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", correlation);
        return request;
    }

    private static HttpRequestMessage Approval(Guid periodId, string key, long expectedVersion, string? correlation = null) =>
        Post($"{PeriodsPath}/{periodId:D}/approval", key, correlation ?? Guid.NewGuid().ToString("N"),
            new PeriodCommandRequestV1(expectedVersion));

    private static HttpRequestMessage Close(Guid periodId, string key, long expectedVersion) =>
        Post($"{PeriodsPath}/{periodId:D}/close", key, Guid.NewGuid().ToString("N"), new PeriodCommandRequestV1(expectedVersion));

    private static void AssertReplayOf(HttpResponseMessage response, string originalCorrelation)
    {
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(originalCorrelation, Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
    }

    private static string? Code(JsonElement problem) => problem.GetProperty("code").GetString();

    private static JsonElement SingleItem(JsonElement page) => Assert.Single(page.GetProperty("items").EnumerateArray());

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public Guid Writer { get; } = Guid.NewGuid();

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            // Idempotent when the host already applied Accounting's migrations.
            await using var context = run.Context(CompanyA);
            await context.Database.MigrateAsync();
            return run;
        }

        public HttpClient Client(string developmentUser, Guid company)
        {
            var client = Factory.CreateClientFor(developmentUser, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        public AccountingCommandMetadata Metadata() => new AccountingCommandMetadata(Writer.ToString("D"), "synthetic.accounting.test-write",
            ["SYNTHETIC-ACCOUNTING-TEST"], $"accv0-{Guid.NewGuid():N}", null, Guid.NewGuid().ToString("N"), "Web");

        public AccountingDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema))
                .Options, companyContext);
        }

        /// <summary>The ledger store of one company, checked against Settings' REAL approved money profiles.</summary>
        public StoreScope Store(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options, companyContext);
            var settings = new ApprovedSettingsPrerequisiteProvider(companyContext, new FixedCurrentUser(Writer, company));
            return new StoreScope(dbContext, new AccountingStore(dbContext, companyContext, collector, TimeProvider.System,
                new AccountingAmountScaleVerifier(settings)));
        }

        public async Task GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Accounting user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor,
                    BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"ACC-V0-{roleId:N}", "Synthetic Accounting test role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
                DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
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

    private sealed class StoreScope(AccountingDbContext dbContext, AccountingStore store) : IAsyncDisposable
    {
        public IAccountingPostingStore Postings => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>The ledger writer as Settings sees it: authenticated, with access to exactly one company.</summary>
    private sealed class FixedCurrentUser(Guid userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => userId.ToString("D");

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };

        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }
}
