using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D2 / API-12 (with SAT-D34…D37, SAT-D42/D43) against a REAL SQL Server and the REAL host composition: a manual price
/// (no list-price reference) given through the real commercial-revision route needs approval whatever its discount fields
/// (SAT-D43) — it cannot be measured, so no limit is read (the actor's limit-group source, the one SYNTHETIC seam, is never
/// asked); the submission is refused while the exception waits; a holder of the draft read alone is 403; another person with <c>sales.orders.discount-exception.approve</c> approves it in ONE save (line pricing,
/// header version, attempt with both price-visibility replay variants, audit event); the same key replays with no second effect,
/// another body under it is 409, a stale version is the key's recorded 409; another company sees nothing; the replay-result
/// constraint holds and the migration refuses to roll back over a recorded result (51069). Orders are stored through the real
/// Sales store from SYNTHETIC owner snapshots (the AT-024 precedent); every grant is a fresh synthetic role. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesDiscountExceptionSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 10, 27);
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("4d15c0a1-6c7b-48a9-9c2d-3e4f5a6b7e01");
    private static readonly Guid MoneyProfile = Guid.Parse("4d15c0a1-6c7b-48a9-9c2d-3e4f5a6b7e02");
    private static readonly Guid KgProfile = Guid.Parse("4d15c0a1-6c7b-48a9-9c2d-3e4f5a6b7e03");
    private static readonly Guid Product = Guid.Parse("4d15c0a1-6c7b-48a9-9c2d-3e4f5a6b7e04");
    private static readonly Guid LimitGroup = Guid.Parse("4d15c0a1-6c7b-48a9-9c2d-3e4f5a6b7e05");
    private const string Command = "sales.order-draft.approve-discount-exceptions.v1";

    [SqlServerFact]
    public async Task A_revised_discount_waits_for_another_persons_reasoned_approval_through_the_real_routes_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.StoredDraftAsync(CompanyA, "k");
        var lineId = Assert.Single(draft.Lines).Id;
        var (preparerSubject, preparer) = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceWrite,
            SalesPermissionManifestV1.OrdersPriceRead, SalesPermissionManifestV1.OrdersDiscountExceptionApprove);
        var (readerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SalesPermissionManifestV1.DraftsRead);
        var (managerSubject, manager) = await run.GrantAsync(TestUsers.GroupAdmin, CompanyA,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceRead,
            SalesPermissionManifestV1.OrdersDiscountExceptionApprove);
        using var preparerClient = run.Client(preparerSubject, CompanyA);
        using var readerClient = run.Client(readerSubject, CompanyA);
        using var managerClient = run.Client(managerSubject, CompanyA);
        var draftPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}";
        var approvalPath = $"{draftPath}/discount-exception-approval";

        // The reviser gives a manual price with 5 %: no list price, so it goes to approval (SAT-D43) and no limit is read.
        using (var revised = await preparerClient.SendAsync(Json(HttpMethod.Post, $"{draftPath}/commercial-revision", "SENTETIK-revise",
                   new SalesDraftCommercialRevisionRequest(draft.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                       [new SalesDraftLineCommercialV1(lineId, "100", KgUnit, "5")]))))
        {
            Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        }

        Assert.Equal(0, run.Groups.Calls);
        var pricing = Assert.Single((await run.JsonAsync(preparerClient, $"{draftPath}/line-pricing")).GetProperty("lines").EnumerateArray());
        var check = pricing.GetProperty("discountCheck");
        Assert.Equal((preparer, JsonValueKind.Null, JsonValueKind.Null, true, JsonValueKind.Null), (check.GetProperty("checkedBy").GetString(),
            check.GetProperty("listUnitPrice").ValueKind, check.GetProperty("limitPercent").ValueKind,
            check.GetProperty("requiresApproval").GetBoolean(), check.GetProperty("approvedBy").ValueKind));
        Assert.Equal(("true", preparer), (
            await run.ScalarAsync<string>("SELECT JSON_VALUE([PricingJson], '$.discountCheck.requiresApproval') FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([PricingJson], '$.discountCheck.checkedBy') FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id))));
        var version = draft.Version + 1;

        // The submission waits for the exception.
        using (var blocked = await preparerClient.SendAsync(Json(HttpMethod.Post, $"{draftPath}/submission", "SENTETIK-submit-blocked",
                   new SalesDraftStateCommandRequest($"{version}"))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
            Assert.Equal("sales_discount_exception_pending",
                (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")[0].GetProperty("code").GetString());
        }

        // The draft read alone is 403, and nothing is written.
        var before = await run.EffectsAsync(draft.Id);
        using (var forbidden = await readerClient.SendAsync(Approval(approvalPath, "SENTETIK-reader", $"{version}", lineId, "SENTETIK")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(before, await run.EffectsAsync(draft.Id));

        // Another person with the dedicated permission approves it: ONE save.
        using (var approved = await managerClient.SendAsync(Approval(approvalPath, "SENTETIK-approve-1", $"{version}", lineId, "  SENTETIK yıllık anlaşma  ")))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            Assert.False(approved.Headers.Contains("Idempotency-Replayed"));
            var summary = (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("draft");
            Assert.Equal(($"{version + 1}", "Preparing"), (summary.GetProperty("version").GetString(), summary.GetProperty("approvalState").GetString()));
        }

        Assert.Equal((manager, "SENTETIK yıllık anlaşma", version + 1), (
            await run.ScalarAsync<string>("SELECT JSON_VALUE([PricingJson], '$.discountCheck.approvedBy') FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([PricingJson], '$.discountCheck.reason') FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @command AND [Outcome] = 1 AND [DraftId] = @id "
            + "AND [ActorId] = @actor AND ISJSON([ResultJson]) = 1 AND JSON_VALUE([ResultJson], '$.withheld.lines[0].priceDisclosure') = N'Withheld' "
            + "AND JSON_VALUE([ResultJson], '$.visible.lines[0].unitPrice') = N'100'",
            ("@command", Command), ("@id", draft.Id), ("@actor", manager)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.discount-exceptions-approved.v1' AND [EntityId] = @id "
            + "AND [SourceChannel] = N'Web' AND [EntityVersionBefore] = @before AND [EntityVersionAfter] = @after AND [ActorId] = @actor "
            + "AND [PermissionCode] = N'sales.orders.discount-exception.approve'",
            ("@id", draft.Id.ToString("D")), ("@before", version), ("@after", version + 1), ("@actor", manager)));

        // The same key and body: the first answer again, no second row, event or attempt; another body under it is 409.
        var afterApproval = await run.EffectsAsync(draft.Id);
        using (var replayed = await managerClient.SendAsync(Approval(approvalPath, "SENTETIK-approve-1", $"{version}", lineId, "  SENTETIK yıllık anlaşma  ")))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        using (var mismatch = await managerClient.SendAsync(Approval(approvalPath, "SENTETIK-approve-1", $"{version}", lineId, "SENTETIK başka gerekçe")))
        {
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
            Assert.Equal("same_key_payload_mismatch", (await mismatch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal(afterApproval, await run.EffectsAsync(draft.Id));

        // A stale version is the key's recorded terminal refusal, replayed as such.
        using (var stale = await managerClient.SendAsync(Approval(approvalPath, "SENTETIK-stale", $"{version}", lineId, "SENTETIK")))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("version_conflict", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        using (var staleAgain = await managerClient.SendAsync(Approval(approvalPath, "SENTETIK-stale", $"{version}", lineId, "SENTETIK")))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleAgain.StatusCode);
            Assert.Equal("true", Assert.Single(staleAgain.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @command AND [Outcome] = 2 AND [DraftId] = @id AND [ResultJson] IS NULL",
            ("@command", Command), ("@id", draft.Id)));

        // Another company's approver: the draft does not exist for it.
        var (otherSubject, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersDiscountExceptionApprove);
        using (var otherClient = run.Client(otherSubject, CompanyB))
        using (var other = await otherClient.SendAsync(Approval(approvalPath, "SENTETIK-other", $"{version + 1}", lineId, "SENTETIK")))
        {
            Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        }

        // With the exception approved, the preparer submits.
        using (var submitted = await preparerClient.SendAsync(Json(HttpMethod.Post, $"{draftPath}/submission", "SENTETIK-submit",
                   new SalesDraftStateCommandRequest($"{version + 1}"))))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
            Assert.Equal("PendingApproval", (await submitted.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("detail").GetProperty("draft").GetProperty("approvalState").GetString());
        }
    }

    [SqlServerFact]
    public async Task The_replay_result_constraint_admits_only_a_recorded_approval_and_Down_refuses_51069_over_one()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.StoredDraftAsync(CompanyA, "c", discountPercent: "15");
        var lineId = Assert.Single(draft.Lines).Id;
        await using (var scope = run.SalesStore(CompanyA))
        {
            var approved = await scope.Store.ReviseDraftCommercialAsync(run.SalesEnvelope("c-approve", "SENTETIK-manager"), Hash("c-approve"),
                SalesDraftCommands.ApproveDiscountExceptionsV1, draft.Id,
                (target, now) =>
                {
                    target.ApproveDiscountExceptions(target.Version, [new LineDiscountExceptionApprovalInput(lineId, "SENTETIK")], "SENTETIK-manager", now);
                    return null;
                }, new SalesDraftStateAudit("sales.order-draft.discount-exceptions-approved.v1", "ApproveDiscountExceptions", "SENTETIK"),
                _ => Task.CompletedTask, default, ReplayResult);
            Assert.NotNull(approved!.Result);
        }

        // A success of the command without its replay result, and a result on any other command, are refused by SQL Server.
        var missing = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO [sales].[DraftAttempts] ([CompanyId],[CommandName],[IdempotencyKeyHash],[RequestHash],[ActorId],[Outcome],[DraftId],[CorrelationId],[RecordedAtUtc]) "
            + "VALUES (@company,@command,@key,@hash,N'SENTETIK',1,@draft,N'SENTETIK-correlation',SYSUTCDATETIME())",
            ("@company", CompanyA), ("@command", Command), ("@key", Hash("missing").ToUpperInvariant()), ("@hash", Hash("missing-body")),
            ("@draft", draft.Id)));
        Assert.Equal(547, missing.Number);
        var refusalWithResult = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO [sales].[DraftAttempts] ([CompanyId],[CommandName],[IdempotencyKeyHash],[RequestHash],[ActorId],[Outcome],[DraftId],[RefusalJson],[ResultJson],[CorrelationId],[RecordedAtUtc]) "
            + "VALUES (@company,@command,@key,@hash,N'SENTETIK',2,@draft,N'{}',N'{}',N'SENTETIK-correlation',SYSUTCDATETIME())",
            ("@company", CompanyA), ("@command", Command), ("@key", Hash("refusal").ToUpperInvariant()), ("@hash", Hash("refusal-body")),
            ("@draft", draft.Id)));
        Assert.Equal(547, refusalWithResult.Number);

        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var migration = Array.IndexOf(applied, "20261027134109_AddSalesDiscountExceptionApprovalReplayResults");
        Assert.True(migration > 0);
        var refusal = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[migration - 1]));
        Assert.Equal(51069, refusal.Number);
        Assert.Equal(applied[..(migration + 1)], await context.Database.GetAppliedMigrationsAsync());
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
    }

    private static SalesDraftReplayResult ReplayResult(SalesOrderDraft draft) =>
        new(Detail(draft, SalesPriceDisclosure.Visible), Detail(draft, SalesPriceDisclosure.Withheld));

    private static SalesOrderDraftDetailView Detail(SalesOrderDraft draft, SalesPriceDisclosure disclosure)
    {
        var snapshots = draft.Lines.Select(line => new SalesOrderDraftProductSnapshotViewV1(
            line.Product.ProductId, line.Product.ProductVersion, line.Product.Code, line.Product.Name,
            line.OrderUnit.UnitId, line.OrderUnit.UnitCode)).Distinct().ToArray();
        var summary = new SalesOrderDraftSummaryView(draft.Id, draft.CompanyId, draft.DocumentNo, draft.Kind.ToString(),
            draft.State.ToString(), draft.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            draft.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), draft.Header.PartyId, draft.Header.CompanyAccountId,
            draft.Header.CurrencyId, draft.Header.BusinessDate, draft.CreatedAtUtc, draft.Lines.Count, draft.ApprovalState.ToString(),
            SalesCommercialStageV1.Order, snapshots);
        var lines = draft.Lines.Select(line => new SalesOrderDraftLineDetail(line.Id, line.LineNo, line.Product.ProductId,
            line.Product.ProductVersion, line.Product.Code, line.Product.Name, line.OrderUnit.UnitId, line.QuantityText,
            disclosure == SalesPriceDisclosure.Visible ? line.Commercial.UnitPrice : null,
            disclosure == SalesPriceDisclosure.Visible ? line.Commercial.PriceUnitId : null,
            line.RequestedDeliveryDate, line.Note, null,
            disclosure == SalesPriceDisclosure.Visible ? line.Commercial.DiscountPercent : null, disclosure)).ToArray();
        return new SalesOrderDraftDetailView(summary, draft.Header.RequestedDeliveryDate, draft.Header.Note, lines,
            null, null, null, null, null, null,
            new SalesDraftApprovalV1(draft.Approval.SubmittedBy, draft.Approval.SubmittedAtUtc, draft.Approval.DecidedBy,
                draft.Approval.DecidedAtUtc, draft.Approval.RejectionReason),
            new SalesDraftCustomerAcceptanceV1(false, null, null, null, false), false, false);
    }

    private static HttpRequestMessage Json(HttpMethod method, string path, string key, object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"sales-api12-{key}");
        return request;
    }

    private static HttpRequestMessage Approval(string path, string key, string version, Guid lineId, string reason) =>
        Json(HttpMethod.Post, path, key, new SalesDraftDiscountExceptionApprovalRequest(version,
            [new SalesDraftDiscountExceptionApprovalLineV1(lineId, reason)]));

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>(sales attempts, audit events, draft version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, long DraftVersion);

    /// <summary>The one SYNTHETIC seam: the Identity owner's role → limit group mapping (no owner registers it yet).</summary>
    private sealed class FixedLimitGroups : ISalesDiscountLimitGroupSourceV1
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public Task<IReadOnlyList<Guid>> GetCurrentActorLimitGroupsAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IReadOnlyList<Guid>>([LimitGroup]);
        }
    }

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory, FixedLimitGroups groups)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
            Groups = groups;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public FixedLimitGroups Groups { get; }
        public string Actor { get; } = "SENTETIK-creator-" + Guid.NewGuid().ToString("N");

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var groups = new FixedLimitGroups();
            var factory = new SqlServerRelationalApiFactory(baseConnectionString,
                services => services.AddSingleton<ISalesDiscountLimitGroupSourceV1>(groups));
            var run = new Run(baseConnectionString, factory, groups);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>A stored, unpriced (or, with a percent, an unchecked-then-checked priced) preparing draft of one 10 kg line.</summary>
        public async Task<SalesOrderDraft> StoredDraftAsync(Guid company, string keyPrefix, string? discountPercent = null)
        {
            await using var scope = SalesStore(company);
            return (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo, discountPercent))).Draft;
        }

        private SalesOrderDraft Draft(Guid company, string documentNo, string? discountPercent)
        {
            var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(7));
            var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "1", Day,
                true, Day.AddDays(-30), null, 2);
            var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
            var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "1", true, 3, 3, true, Day.AddDays(-30), null);
            var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "1");
            var product = new ProductSnapshot(Product, "1", "SENTETIK-API12", "Sentetik iskontolu ürün", Day, binding, binding, binding);
            var commercial = discountPercent is null
                ? null
                : SalesOrderDraft.WithDiscountLimitCheck(new CommercialDraftFields("100", KgUnit, discountPercent),
                    SalesDiscountLimit.Undefined, Actor, T0);
            draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "10", 10m, Commercial: commercial), T0);
            return draft;
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync(Guid draftId) => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)));

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], $"sales-api12-{key}", null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public SalesDbContext SalesContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

        public SalesScope SalesStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new GoymenERP.Modules.Audit.Infrastructure.Persistence.AuditSaveChangesInterceptor(collector, companyContext)).Options,
                companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        /// <summary>A fresh synthetic role of the given names for the subject in one company (company-wide); returns the subject and the actor id.</summary>
        public async Task<(string Subject, string ActorId)> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales discount-exception user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"API12-DISCOUNT-{roleId:N}", "Synthetic Sales discount-exception role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
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
}
