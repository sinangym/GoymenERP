using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Requests;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// API-20 / API-21 against a REAL SQL Server: the sales request's one-save create and decisions with their attempt records and
/// audit events, the ADR-011 §3/§4/§7/§8 answers under the real unique keys, the check constraints that bind a decision to
/// its state and a request target to a request command, company isolation, the guarded rollback, and the routes through the
/// REAL host with fresh synthetic grants. The create runs the real draft service over the real store with SYNTHETIC owner
/// ports (Cari / Product / Settings answers): it proves the request slice and the store, not any owner's rule; through the
/// real host a create meets the real Cari owner and answers its honest refusal. Every identity is SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesRequestSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 10, 26);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly Guid KgUnit = Guid.Parse("6b1d2c3b-4a59-4687-9a0b-1c2d3e4f5b93");
    private static readonly Guid KgProfile = Guid.Parse("6b1d2c3b-4a59-4687-9a0b-1c2d3e4f5b94");
    private static readonly Guid Product = Guid.Parse("6b1d2c3b-4a59-4687-9a0b-1c2d3e4f5b95");

    [SqlServerFact]
    public async Task A_request_is_recorded_in_one_save_replayed_by_its_key_and_decided_along_its_axis_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var party = Guid.NewGuid();
        var account = Guid.NewGuid();
        var create = Create(party, account, lines: 2);

        SalesRequestResponse created;
        await using (var scope = run.Store(CompanyA))
        {
            created = await run.Service(scope, run.Actor).CreateRequestAsync(create, run.Metadata("r-create"));
        }

        var id = created.Detail.Request.Id;
        Assert.Equal(("Submitted", "1", 2), (created.Detail.Request.State, created.Detail.Request.Version, created.Detail.Request.LineCount));
        Assert.Matches("^SR-[0-9]{4}-000001$", created.Detail.Request.DocumentNo);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Requests WHERE Id = @id AND State = 1 AND Version = 1 AND CreatedBy = @a",
            ("@id", id), ("@a", run.Actor)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.RequestLines WHERE RequestId = @id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.DraftAttempts WHERE CommandName = N'sales.request.create.v1' AND RequestId = @id AND DraftId IS NULL AND Outcome = 1",
            ("@id", id)));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT [Next] FROM sales.DocumentCounters WHERE CompanyId = @c AND Series = 'SR'", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.Request' AND EventType = N'sales.request.created.v1' AND EntityId = @id "
            + "AND EntityVersionAfter = 1 AND PermissionCode = N'sales.requests.write'", ("@id", id.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));

        // ADR-011 §4: the same key, content and actor answers the stored request; nothing is written twice. Another content is one 409.
        await using (var scope = run.Store(CompanyA))
        {
            var replay = await run.Service(scope, run.Actor).CreateRequestAsync(create, run.Metadata("r-create", "SENTETIK-again"));
            Assert.Equal((true, id, run.Correlation), (replay.IsReplay, replay.Detail.Request.Id, replay.OriginalCorrelationId));
            var mismatch = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                run.Service(scope, run.Actor).CreateRequestAsync(Create(party, account, lines: 1), run.Metadata("r-create")));
            Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), (mismatch.Status, mismatch.Code));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Requests"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.Request'"));

        // §7: a stale version is the key's recorded 409, named by the request; the retry answers it again with its first correlation.
        await using (var scope = run.Store(CompanyA))
        {
            var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                run.Service(scope, "SENTETIK-approver").ApproveRequestAsync(id, new("5"), run.Metadata("r-approve-stale")));
            Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, false), (stale.Status, stale.Code, stale.IsReplay));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                run.Service(scope, "SENTETIK-approver").ApproveRequestAsync(id, new("5"), run.Metadata("r-approve-stale")));
            Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, true, run.Correlation),
                (again.Status, again.Code, again.IsReplay, again.OriginalCorrelationId));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.DraftAttempts WHERE CommandName = N'sales.request.approve.v1' AND RequestId = @id AND Outcome = 2 AND ISJSON(RefusalJson) = 1",
            ("@id", id)));

        // One approver approves; a blank rejection reason is refused and records nothing; a reasoned rejection of the approved request holds.
        await using (var scope = run.Store(CompanyA))
        {
            var approved = await run.Service(scope, "SENTETIK-approver").ApproveRequestAsync(id, new("1"), run.Metadata("r-approve"));
            Assert.Equal(("Approved", "2", "SENTETIK-approver"),
                (approved.Detail.Request.State, approved.Detail.Request.Version, approved.Detail.Decision!.DecidedBy));
            var blank = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                run.Service(scope, "SENTETIK-approver").RejectRequestAsync(id, new("2", " "), run.Metadata("r-reject-blank")));
            Assert.Equal((422, SalesRuleCodes.RequestRejectionReasonRequired), (blank.Status, blank.Code));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash = @h",
            ("@h", SalesStore.IdempotencyKeyHash("r-reject-blank"))));
        await using (var scope = run.Store(CompanyA))
        {
            var rejected = await run.Service(scope, "SENTETIK-approver").RejectRequestAsync(id, new("2", "SENTETIK müşteri vazgeçti"), run.Metadata("r-reject"));
            Assert.Equal(("Rejected", "3"), (rejected.Detail.Request.State, rejected.Detail.Request.Version));
            var closed = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                run.Service(scope, "SENTETIK-approver").ApproveRequestAsync(id, new("3"), run.Metadata("r-approve-rejected")));
            Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (closed.Status, closed.Code));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.Requests WHERE Id = @id AND State = 3 AND Version = 3 AND RejectionReason = N'SENTETIK müşteri vazgeçti'", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'sales.request.rejected.v1' AND EntityId = @id AND EntityVersionBefore = 2 AND EntityVersionAfter = 3",
            ("@id", id.ToString("D"))));
        // The request's lines never moved.
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.RequestLines WHERE RequestId = @id AND QuantityText = N'1.25'", ("@id", id)));

        // The schema binds the facts to the state and a request target to a request command — SQL Server itself refuses.
        var decision = await Assert.ThrowsAsync<SqlException>(() =>
            run.ExecuteAsync("UPDATE sales.Requests SET RejectionReason = NULL WHERE Id = @id", ("@id", id)));
        Assert.Equal(547, decision.Number);
        Assert.Contains("CK_Requests_Decision", decision.Message, StringComparison.Ordinal);
        var target = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO sales.DraftAttempts (CompanyId, CommandName, IdempotencyKeyHash, RequestHash, ActorId, Outcome, RequestId, CorrelationId, RecordedAtUtc) "
            + "VALUES (@c, N'sales.order-draft.submit.v1', REPLICATE('B', 64), REPLICATE('0', 64), N'SENTETIK', 1, @id, N'SENTETIK', SYSUTCDATETIME())",
            ("@c", CompanyA), ("@id", id)));
        Assert.Equal(547, target.Number);
        Assert.Contains("CK_DraftAttempts_RequestTarget", target.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task Another_companys_request_is_invisible_its_counter_is_its_own_and_a_same_key_race_stores_one_request()
    {
        await using var run = await Run.StartAsync();
        SalesRequest stored;
        await using (var a = run.Store(CompanyA))
        {
            stored = (await a.Requests.StoreRequestAsync(run.Envelope("r-a"), Hash("payload-a"), documentNo => Request(CompanyA, documentNo, run.Actor))).Request;
        }

        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Requests.FindRequestAsync(stored.Id));
            Assert.Empty((await b.Requests.ListRequestsAsync(50, null, null)).Items);
            Assert.Null(await b.Requests.DecideRequestAsync(run.Envelope("r-b-approve"), Hash("approve-b"), SalesRequestCommands.ApproveV1, stored.Id,
                (request, now) => request.Approve(1, "SENTETIK-approver", now), Approved));
            // The key of another company is invisible: the same key is a new attempt here.
            Assert.Null(await b.Requests.FindRequestReplayAsync(run.Envelope("r-a"), Hash("payload-a"), SalesRequestCommands.CreateV1));
            var own = (await b.Requests.StoreRequestAsync(run.Envelope("r-a"), Hash("payload-a"), documentNo => Request(CompanyB, documentNo, run.Actor))).Request;
            Assert.EndsWith("-000001", own.DocumentNo, StringComparison.Ordinal);
            // A request of another company than the context is refused before anything is written.
            var foreign = await Assert.ThrowsAsync<SalesDraftStoreException>(() =>
                b.Requests.StoreRequestAsync(run.Envelope("r-b-foreign"), Hash("payload-b-foreign"), documentNo => Request(CompanyA, documentNo, run.Actor)));
            Assert.Equal(SalesDraftStoreCodes.CompanyContextMismatch, foreign.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Requests WHERE CompanyId = @c AND State = 1", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Requests WHERE CompanyId = @c", ("@c", CompanyB)));

        // §8: two writers with the same key and content both pass the attempt lookup; the attempt key lets exactly one through.
        var barrier = new Barrier(2);
        async Task<object> StoreAsync()
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return (await writer.Requests.StoreRequestAsync(run.Envelope("r-race"), Hash("payload-race"),
                    documentNo => Request(CompanyA, documentNo, run.Actor))).Outcome;
            }
            catch (SalesDraftStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(StoreAsync), Task.Run(StoreAsync));
        Assert.Single(results, result => result is SalesDraftWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == SalesDraftStoreCodes.ConcurrentChange);
        await using (var a = run.Store(CompanyA))
        {
            var retry = await a.Requests.StoreRequestAsync(run.Envelope("r-race"), Hash("payload-race"),
                _ => throw new InvalidOperationException("a replay never builds"));
            Assert.Equal(SalesDraftWriteOutcome.Replayed, retry.Outcome);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash = @h AND CompanyId = @c",
            ("@h", SalesStore.IdempotencyKeyHash("r-race")), ("@c", CompanyA)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Requests WHERE CompanyId = @c", ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task The_request_routes_answer_only_with_fresh_grants_and_the_rollback_refuses_while_a_request_exists()
    {
        await using var run = await Run.StartAsync();
        SalesRequest stored;
        await using (var a = run.Store(CompanyA))
        {
            stored = (await a.Requests.StoreRequestAsync(run.Envelope("r-route"), Hash("payload-route"), documentNo => Request(CompanyA, documentNo, run.Actor))).Request;
        }

        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.RequestsRead, SalesPermissionManifestV1.RequestsWrite);
        var approver = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SalesPermissionManifestV1.RequestsApprove);
        using var readerClient = run.Factory.CreateClientFor(reader, CompanyA);
        using var approverClient = run.Factory.CreateClientFor(approver, CompanyA);
        foreach (var client in new[] { readerClient, approverClient })
        {
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        }

        using (var list = await readerClient.GetAsync(SalesEndpoints.RequestsPath))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var body = await list.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(stored.Id, Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }

        using (var detail = await readerClient.GetAsync($"{SalesEndpoints.RequestsPath}/{stored.Id:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var body = await detail.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(("Submitted", "1.25"), (body.GetProperty("request").GetProperty("state").GetString(),
                body.GetProperty("lines")[0].GetProperty("quantity").GetString()));
        }

        // The approver holds no read: the list is refused; the reader holds no approve: the decision is refused and records nothing.
        using (var noRead = await approverClient.GetAsync(SalesEndpoints.RequestsPath))
        {
            Assert.Equal(HttpStatusCode.Forbidden, noRead.StatusCode);
        }

        using (var noDecision = await readerClient.SendAsync(Command(stored.Id, "approval", "SENTETIK-reader-approves", "{\"expectedVersion\":\"1\"}")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, noDecision.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash = @h",
            ("@h", SalesStore.IdempotencyKeyHash("SENTETIK-reader-approves"))));

        var correlation = Guid.NewGuid().ToString("N");
        using (var approved = await approverClient.SendAsync(Command(stored.Id, "approval", "SENTETIK-approve", "{\"expectedVersion\":\"1\"}", correlation)))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var body = await approved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(("Approved", "2"), (body.GetProperty("detail").GetProperty("request").GetProperty("state").GetString(),
                body.GetProperty("detail").GetProperty("request").GetProperty("version").GetString()));
        }

        using (var replay = await approverClient.SendAsync(Command(stored.Id, "approval", "SENTETIK-approve", "{\"expectedVersion\":\"1\"}")))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(correlation, Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        using (var unknown = await approverClient.SendAsync(Command(Guid.NewGuid(), "approval", "SENTETIK-unknown", "{\"expectedVersion\":\"1\"}")))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        // Through the real host a create meets the REAL Cari owner: an unknown customer is its honest 409, and nothing is written.
        using (var refused = await readerClient.SendAsync(CreateCommand("SENTETIK-create", Create(Guid.NewGuid(), Guid.NewGuid(), lines: 1))))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.AccountState, (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Requests"));

        // Another company's grant sees nothing of company A's request.
        var other = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.RequestsRead);
        using var otherClient = run.Factory.CreateClientFor(other, CompanyB);
        using (var foreign = await otherClient.GetAsync($"{SalesEndpoints.RequestsPath}/{stored.Id:D}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        // Rollback refuses while a request exists (forward fix); nothing is dropped.
        await using var context = run.Context(CompanyA);
        var refusal = await Assert.ThrowsAsync<SqlException>(() =>
            context.GetService<IMigrator>().MigrateAsync("20261004160000_AddSalesPriceFloorApprovalReplayResults"));
        Assert.Equal(51065, refusal.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Requests"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.__EFMigrationsHistory WHERE MigrationId LIKE N'%_AddSalesRequests'"));
    }

    private static readonly SalesDraftStateAudit Approved = new("sales.request.approved.v1", "Approve", "SENTETIK onay");

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    private static CreateSalesRequestRequest Create(Guid party, Guid account, int lines) => new(party, account, Day,
        [.. Enumerable.Range(0, lines).Select(_ => new SalesRequestLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "1.25"))]);

    /// <summary>A request of the company from SYNTHETIC owner snapshots (one kg line of 1.25).</summary>
    private static SalesRequest Request(Guid company, string documentNo, string actor)
    {
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        return SalesRequest.Create(Guid.NewGuid(), company, documentNo, new SalesRequestHeader(Guid.NewGuid(), Guid.NewGuid(), Day),
            [new SalesRequestLineInput(Guid.NewGuid(), product, unit, "1.25", 1.25m)], actor, DateTimeOffset.UtcNow);
    }

    private static HttpRequestMessage Command(Guid requestId, string segment, string idempotencyKey, string json, string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.RequestsPath}/{requestId:D}/{segment}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", correlationId ?? Guid.NewGuid().ToString("N"));
        return request;
    }

    private static HttpRequestMessage CreateCommand(string idempotencyKey, CreateSalesRequestRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.RequestsPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return request;
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = "SENTETIK-requester-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"sales-api20-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context(CompanyA);
            await context.Database.MigrateAsync();
            return run;
        }

        public SalesDraftWriteEnvelope Envelope(string key, string? correlation = null, string? actor = null) =>
            new(actor ?? Actor, SalesPermissionManifestV1.RequestsWrite, ["SENTETIK-SALES"], correlation ?? Correlation, null, key, "Web",
                "127.0.0.1", "SENTETIK-agent");

        public SalesCommandMetadata Metadata(string key, string? correlation = null) => new(key, correlation ?? Correlation, null, DateTimeOffset.UtcNow);

        /// <summary>The REAL draft service over this scope's real store, with SYNTHETIC owner ports and every Sales request name granted.</summary>
        public SalesDraftService Service(StoreScope scope, string actor)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            var owners = new SyntheticOwners();
            return new SalesDraftService(companyContext, new FixedUser(actor, CompanyA), new GrantedAuthority(), new NoRoles(),
                new FixedCorrelation(Correlation), owners, owners, owners, scope.Drafts, new SalesDraftServiceBindings(), TimeProvider.System,
                requests: scope.Requests);
        }

        public SalesDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

        public StoreScope Store(Guid company, IInterceptor? extra = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var builder = new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext));
            if (extra is not null)
            {
                builder.AddInterceptors(extra);
            }

            var dbContext = new SalesDbContext(builder.Options, companyContext);
            return new StoreScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        /// <summary>A synthetic role with exactly these permissions in one company, assigned to a configured development user.</summary>
        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales request user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SALES-API20-{roleId:N}", "Synthetic Sales request test role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return developmentUser;
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
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

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

    private sealed class StoreScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesRequestStore Requests => store;

        public ISalesOrderDraftStore Drafts => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class FixedUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([company]);
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }

    private sealed class NoRoles : IActiveRoleCodeReader
    {
        public Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }

    private sealed class GrantedAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, true, new HashSet<string>(["SENTETIK-SALES"], StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }

    private sealed class FixedCorrelation(string correlationId) : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => correlationId;
        public string? CausationId => null;
        public string SourceChannel => "Web";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-agent";
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent)
        {
        }
    }

    /// <summary>SYNTHETIC Cari / Settings / Product answers for company A: an active account, the kg profile, a sellable product.</summary>
    private sealed class SyntheticOwners : ISalesPartyAccountPortV1, ISettingsPrerequisiteProvider, IProductSalesEligibilityV1
    {
        public Task<SalesPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesPartyAccountAnswerV1(SalesPartyAccountOutcomeV1.Found,
                new SalesPartyAccountV1(partyId, companyAccountId, CompanyA, true, true, "1", "1")));

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UnitProfileV1(CompanyA, unitId, "kg", "weight", true, new ProfileVersionV1(KgProfile, 4), 3, true, From, null));

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A sales request reads no money profile.");

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection, CancellationToken cancellationToken)
        {
            var unit = new ProductSalesUnitProfileV1(CompanyA, selection.OrderUnitId, KgProfile, "4");
            return Task.FromResult(new ProductSalesEligibilityV1(
                new ProductSalesSnapshotV1(selection.ProductId, "7", "GDC-001", "Sentetik bant", unit, unit, unit, selection.EffectiveOn), []));
        }
    }

    /// <summary>Holds each save until both writers have reached it, so both pass the attempt lookup first.</summary>
    private sealed class BarrierInterceptor(Barrier barrier) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return ValueTask.FromResult(result);
        }
    }
}
