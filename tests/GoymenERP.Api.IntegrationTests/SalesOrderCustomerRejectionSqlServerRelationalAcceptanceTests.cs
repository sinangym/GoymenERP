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
using GoymenERP.Modules.Sales.Domain;
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
/// SAT-D115 (EK14) against a REAL SQL Server and the REAL host composition: Rev.0 (10 kg) accepted by the customer, Rev.1 (15 kg)
/// approved inside and awaiting the customer, then the customer's rejection through the real route — ONE save: the append-only
/// <c>sales.DraftCustomerRejections</c> row, the header version, the attempt and the audit event. The rejected Rev.1 stays as it is
/// (nothing deleted); the line port, the invoicing gate and the realization go back to Rev.0. A missing reason is a 422 (not
/// recorded), a replay answers the recorded result, a stale version is the key's 409, another company's order is a 404 and the
/// migration refuses to roll back over a recorded rejection (51134). SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesOrderCustomerRejectionSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("4a3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7f01");
    private static readonly Guid MoneyProfile = Guid.Parse("4a3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7f02");
    private static readonly Guid KgProfile = Guid.Parse("4a3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7f03");
    private static readonly Guid Product = Guid.Parse("4a3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7f04");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit Accepted = new("sales.order-draft.customer-acceptance-recorded.v1", "RecordCustomerAcceptance", "SENTETIK müşteri kabul etti");
    private static readonly SalesDraftStateAudit Rejected = new("sales.order-draft.customer-rejection-recorded.v1", "RecordCustomerRejection", "SENTETIK müşteri reddetti");
    private static readonly SalesDraftStateAudit QuantityRevised = new("sales.order-draft.quantity-revised.v1", "ReviseQuantity", "SENTETIK miktar revizyonu");

    [SqlServerFact]
    public async Task SAT_D115_a_customer_rejection_through_the_real_route_puts_shipping_invoicing_and_realization_back_on_rev0_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.AwaitingCustomerAsync(CompanyA, "k");
        var orderLine = Assert.Single(draft.Lines);
        var (subject, _) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord, SalesPermissionManifestV1.OrdersPriceRead);
        using var client = run.Client(subject, CompanyA);
        var draftPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}";

        // SAT-D113 / SAT-D114: while Rev.1 awaits the customer nothing ships and invoicing waits.
        var waiting = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "CustomerAcceptancePending"), (waiting.Outcome, waiting.ApprovalState));
        var before = await run.JsonAsync(client, $"{draftPath}/status");
        Assert.Equal("AwaitingCustomerAcceptance", before.GetProperty("invoicing").GetProperty("gate").GetString());

        // A missing reason: 422, nothing recorded.
        var effects = await run.EffectsAsync(draft.Id);
        using (var blank = await client.SendAsync(Post($"{draftPath}/customer-rejection", "SENTETIK-reject-1", new SalesDraftStateCommandRequest("8", " "))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blank.StatusCode);
            var problem = await blank.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(("validation_failed", "reason", SalesRuleCodes.RevisionReasonRequired), (problem.GetProperty("code").GetString(),
                problem.GetProperty("errors")[0].GetProperty("field").GetString(), problem.GetProperty("errors")[0].GetProperty("code").GetString()));
        }

        Assert.Equal(effects, await run.EffectsAsync(draft.Id));

        var body = new SalesDraftStateCommandRequest("8", "  SENTETIK müşteri 15 kg istemedi ");
        using (var rejected = await client.SendAsync(Post($"{draftPath}/customer-rejection", "SENTETIK-reject-1", body)))
        {
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            var detail = (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail");
            Assert.Equal(("Approved", "9", "Rev.1", "15"), (detail.GetProperty("draft").GetProperty("approvalState").GetString(),
                detail.GetProperty("draft").GetProperty("version").GetString(), detail.GetProperty("draft").GetProperty("revision").GetString(),
                detail.GetProperty("lines")[0].GetProperty("quantity").GetString()));
            var rejection = Assert.Single(detail.GetProperty("customerAcceptance").GetProperty("rejections").EnumerateArray());
            Assert.Equal(("Rev.1", "SENTETIK müşteri 15 kg istemedi"), (rejection.GetProperty("revision").GetString(),
                rejection.GetProperty("reason").GetString()));
        }

        // ONE save, as SQL Server holds it: the rejection row, the header (still Rev.1, its 15 kg kept), the attempt, the audit event.
        Assert.Equal((1, 9L, "SENTETIK müşteri 15 kg istemedi"), (
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[DraftCustomerRejections] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [DraftVersion] FROM [sales].[DraftCustomerRejections] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [Reason] FROM [sales].[DraftCustomerRejections] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal((1, 9L, 0, "15"), (
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [CustomerAcceptedRevision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [QuantityText] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal((1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.customer-rejection-recorded.v1' "
                + "AND [EntityId] = @id AND [EntityVersionBefore] = 8 AND [EntityVersionAfter] = 9", ("@id", draft.Id.ToString("D"))),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.customer-rejection.record.v1' "
                + "AND [Outcome] = 1 AND [DraftId] = @id", ("@id", draft.Id))));

        // SAT-D115: shipping goes on under Rev.0 — 10 kg, the cap — rebuilt from SQL through the domain.
        var line = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, 0, "10", "10", SalesOrderLineRevisionBasisV1.LastApprovedRevision, "CustomerRejected"),
            (line.Outcome, line.Line!.Revision, line.Line.QuantityText, line.Line.ShippingCapQuantityText, line.Line.RevisionBasis,
                line.Line.PendingApprovalState));
        Assert.Contains(await run.ApprovedLinesAsync(CompanyA), item => item.OrderId == draft.Id && item.Revision == 0 && item.QuantityText == "10");
        // The invoicing gate opens at Rev.0's prices; the acceptance axis says Rev.1 was rejected; realization measures on Rev.0.
        var status = await run.JsonAsync(client, $"{draftPath}/status");
        Assert.Equal(("Open", "Rev.0", "Rejected", "Rev.0", "10"), (status.GetProperty("invoicing").GetProperty("gate").GetString(),
            status.GetProperty("invoicing").GetProperty("priceBasisRevision").GetString(),
            status.GetProperty("customerAcceptance").GetProperty("state").GetString(),
            status.GetProperty("realization").GetProperty("basisRevision").GetString(),
            status.GetProperty("realization").GetProperty("lines")[0].GetProperty("ordered").GetString()));

        // The same key and content: replayed, nothing written twice.
        effects = await run.EffectsAsync(draft.Id);
        using (var replayed = await client.SendAsync(Post($"{draftPath}/customer-rejection", "SENTETIK-reject-1", body)))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(effects, await run.EffectsAsync(draft.Id));

        // A stale version under a new key: the key's recorded 409, nothing else moves.
        using (var stale = await client.SendAsync(Post($"{draftPath}/customer-rejection", "SENTETIK-reject-stale", body)))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("version_conflict", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal(effects with { SalesAttempts = effects.SalesAttempts + 1 }, await run.EffectsAsync(draft.Id));
        // A rejected revision is not accepted afterwards (409).
        using (var accept = await client.SendAsync(Post($"{draftPath}/customer-acceptance", "SENTETIK-accept-after", new SalesDraftStateCommandRequest("9"))))
        {
            Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);
        }
    }

    [SqlServerFact]
    public async Task SAT_D115_another_company_cannot_reject_and_an_actor_without_the_acceptance_name_is_refused_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.AwaitingCustomerAsync(CompanyA, "k");
        var draftPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}";
        var body = new SalesDraftStateCommandRequest("8", "SENTETIK ret");

        var (other, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord);
        using (var client = run.Client(other, CompanyB))
        using (var response = await client.SendAsync(Post($"{draftPath}/customer-rejection", "SENTETIK-b", body)))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var (reader, _) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersApprove);
        using (var client = run.Client(reader, CompanyA))
        using (var response = await client.SendAsync(Post($"{draftPath}/customer-rejection", "SENTETIK-a", body)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal((0, 8L), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftCustomerRejections]"),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
    }

    [SqlServerFact]
    public async Task The_customer_rejection_migration_refuses_to_roll_back_over_a_recorded_rejection_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.AwaitingCustomerAsync(CompanyA, "k");
        await using (var scope = run.SalesStore(CompanyA))
        {
            var result = await scope.Store.UpdateDraftStateAsync(run.SalesEnvelope("k-reject"), Hash("k-reject"),
                SalesDraftCommands.RecordCustomerRejectionV1, draft.Id,
                (d, now) => d.RecordCustomerRejection(8, "SENTETIK-recorder", "SENTETIK ret", now), Rejected);
            Assert.Equal((9L, 0), (result!.Draft.Version, result.Draft.CustomerAcceptedFallback!.Revision));
        }

        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var rejections = Array.FindIndex(applied, migration => migration.EndsWith("_AddDraftCustomerRejections", StringComparison.Ordinal));
        Assert.True(rejections > 0);
        var migrator = context.GetService<IMigrator>();
        var refused = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[rejections - 1]));
        Assert.Equal(51134, refused.Number);
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftCustomerRejections]"));
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static HttpRequestMessage Post<T>(string path, string key, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"SENTETIK-{Guid.NewGuid():N}");
        return request;
    }

    private static HttpRequestMessage State(string path, string key, string expectedVersion) =>
        Post(path, key, new SalesDraftStateCommandRequest(expectedVersion));

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line 10 kg draft (Q9: customer acceptance required) from SYNTHETIC owner snapshots in the kg unit: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30);
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0, customerAcceptanceRequired: true);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "10", 10m, Day.AddDays(3)), T0);
        return draft;
    }

    /// <summary>(sales attempts, audit events, archived revisions, customer rejections, draft version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, int Revisions, int Rejections, long DraftVersion);

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
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
        public string Actor { get; } = "SENTETIK-preparer-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"sales-sat-d115-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>
        /// A stored draft of the company through the real Sales store: Rev.0 (10 kg) approved and ACCEPTED by the customer, then Rev.1
        /// (15 kg, a quantity revision) submitted and approved inside, awaiting the customer's acceptance — version 8.
        /// </summary>
        public async Task<SalesOrderDraft> AwaitingCustomerAsync(Guid company, string keyPrefix)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo))).Draft;
            var line = Assert.Single(draft.Lines).Id;
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, Actor, now), Submitted);
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"), Hash($"{keyPrefix}-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-accept"), Hash($"{keyPrefix}-accept"),
                SalesDraftCommands.RecordCustomerAcceptanceV1, draft.Id, (d, now) => d.RecordCustomerAcceptance(4, Actor, now), Accepted);
            await scope.Store.ReviseDraftQuantityAsync(SalesEnvelope($"{keyPrefix}-revise"), Hash($"{keyPrefix}-revise"), SalesDraftCommands.ReviseQuantityV1,
                draft.Id, (d, now) => d.ReviseQuantities(5, [new LineQuantityInput(line, "15", 15m)], Actor, "SENTETIK müşteri 15 kg istedi", now),
                QuantityRevised, (_, _) => Task.CompletedTask);
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit-1"), Hash($"{keyPrefix}-submit-1"), SalesDraftCommands.SubmitV1,
                draft.Id, (d, now) => d.Submit(6, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve-1", actor: "SENTETIK-approver"),
                Hash($"{keyPrefix}-approve-1"), SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(7, "SENTETIK-approver", now), Approved);
            Assert.Equal((8L, 1, false), (approved!.Draft.Version, approved.Draft.Revision, approved.Draft.IsCustomerAccepted));
            return approved.Draft;
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync(Guid draftId) => new Effects(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisions]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftCustomerRejections]"),
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)));

        public async Task<SalesOrderLineReferenceAnswerV1> LineAsync(Guid company, Guid orderId, Guid lineId)
        {
            await using var scope = SalesStore(company);
            var context = new CompanyContext();
            context.Set(company);
            return await new SalesOrderLineReferencePort(scope.Store, context).GetApprovedLineAsync(orderId, lineId);
        }

        public async Task<IReadOnlyList<SalesApprovedLineReferenceV1>> ApprovedLinesAsync(Guid company)
        {
            await using var scope = SalesStore(company);
            var context = new CompanyContext();
            context.Set(company);
            return (await new SalesOrderLineReferencePort(scope.Store, context).ListApprovedLinesAsync(100, null)).Items;
        }

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new SalesDraftWriteEnvelope(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

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

        /// <summary>A fresh synthetic role of the given names for the bootstrap subject in one company; returns the subject and its user id.</summary>
        public async Task<(string Subject, Guid UserId)> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales customer rejection user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SAT-D115-{roleId:N}", "Synthetic Sales customer rejection role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return (developmentUser, actor);
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
}
