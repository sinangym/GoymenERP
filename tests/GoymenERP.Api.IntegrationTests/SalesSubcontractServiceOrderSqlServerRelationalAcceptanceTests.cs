using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
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
/// SAT-D175…D178 (EK25; handoff S-3) against a REAL SQL Server and the REAL host composition: a subcontract service ("fason") order
/// keeps its flag (<c>sales.Drafts.IsSubcontractService</c>, and on every archived header), its lines' confirmed item type
/// (<c>DraftLines.ItemType</c>, only "Service" — <c>CK_DraftLines_ItemType</c>) and a free price's price-list absence (in
/// <c>PricingJson</c>); the write guard never moves the flag; the Sales-owned read port answers the order to the Production consumer
/// for its own company only (another company: not found; a company other than the context: unreadable); the detail and line-pricing
/// routes show the flag and the absence; the migration refuses to roll back over a subcontract order (51141).
/// <para>
/// The drafts are stored through the real Sales store from ISOLATED SYNTHETIC owner snapshots (the API-03 / MOD-03 precedent); no
/// price here is a policy. SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed class SalesSubcontractServiceOrderSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 11, 2);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("5f3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c01");
    private static readonly Guid MoneyProfile = Guid.Parse("5f3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c02");
    private static readonly Guid KgProfile = Guid.Parse("5f3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c03");
    private static readonly Guid Product = Guid.Parse("5f3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c04");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit Revised = new("sales.order-draft.commercial-revised.v1", "ReviseCommercial", "SENTETIK revize");

    [SqlServerFact]
    public async Task A_subcontract_order_keeps_its_flag_item_type_and_free_price_and_the_read_port_answers_it_for_its_company_only_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.StoredDraftAsync(CompanyA, "f");
        var line = Assert.Single(draft.Lines);

        Assert.Equal((1, 1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[Drafts] WHERE [Id] = @id AND [IsSubcontractService] = 1", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLines] WHERE [DraftId] = @id AND [ItemType] = N'Service'", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLines] WHERE [DraftId] = @id "
                + "AND JSON_VALUE([PricingJson], '$.listAbsence.writtenBy') = N'SENTETIK-writer' AND JSON_VALUE([PricingJson], '$.listAbsence.unitPrice') = N'37.5'",
                ("@id", draft.Id))));
        // SAT-D178: the create's audit record names who wrote which free price.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityId] = @id "
            + "AND [AfterJson] LIKE N'%\"subcontractService\":true%' AND [AfterJson] LIKE N'%\"listAbsence\":{%SENTETIK-writer%'", ("@id", draft.Id.ToString("D"))));

        await using (var scope = run.SalesStore(CompanyA))
        {
            var stored = (await scope.Store.FindDraftAsync(draft.Id))!;
            Assert.True(stored.IsSubcontractService);
            Assert.Equal((SalesOrderDraft.ServiceItemType, line.Commercial), (Assert.Single(stored.Lines).ItemType, stored.Lines[0].Commercial));
            Assert.Null(stored.Lines[0].Commercial.DiscountCheck);

            // S-3: the read port — prepared, not yet approved; the customer is the order's (the goods' owner, URT-D22).
            var port = new SalesSubcontractServiceOrderReadPort(scope.Store, run.Company(CompanyA));
            var answer = await port.GetAsync(CompanyA, draft.Id);
            Assert.Equal((SalesSubcontractServiceOrderOutcomeV1.Found, ISalesSubcontractServiceOrderReadPortV1.ContractVersion),
                (answer.Outcome, answer.ContractVersion));
            Assert.Equal(("Preparing", false, SalesSubcontractCustomerAcceptanceStatesV1.NotRequired, "Draft", true, draft.Header.PartyId,
                draft.Header.CompanyAccountId), (answer.Order!.ApprovalState, answer.Order.IsApproved, answer.Order.CustomerAcceptanceState,
                answer.Order.LifecycleState, answer.Order.IsOpen, answer.Order.PartyId, answer.Order.CompanyAccountId));
            var service = Assert.Single(answer.Order.Lines);
            Assert.Equal((line.Id, Product, "250", 250m, KgUnit, "kg", "Service"), (service.LineId, service.ProductId, service.QuantityText,
                service.Quantity, service.OrderUnitId, service.OrderUnitCode, service.ItemType));

            // CompanyId enforced: another company than the server's is unreadable (never "not found").
            Assert.Equal((SalesSubcontractServiceOrderOutcomeV1.Unreadable, SalesSubcontractServiceOrderUnreadableReasonsV1.CompanyMismatch),
                ((await port.GetAsync(CompanyB, draft.Id)).Outcome, (await port.GetAsync(CompanyB, draft.Id)).Reason));
        }

        // Company B's own context cannot see company A's order.
        await using (var scope = run.SalesStore(CompanyB))
        {
            var port = new SalesSubcontractServiceOrderReadPort(scope.Store, run.Company(CompanyB));
            Assert.Equal(SalesSubcontractServiceOrderOutcomeV1.NotFound, (await port.GetAsync(CompanyB, draft.Id)).Outcome);
        }

        // A normal order is not a subcontract service order.
        var normal = await run.StoredDraftAsync(CompanyA, "n", subcontract: false);
        await using (var scope = run.SalesStore(CompanyA))
        {
            var port = new SalesSubcontractServiceOrderReadPort(scope.Store, run.Company(CompanyA));
            Assert.Equal(SalesSubcontractServiceOrderOutcomeV1.NotSubcontractService, (await port.GetAsync(CompanyA, normal.Id)).Outcome);
        }

        // The host registers the port; without a resolved company context it answers unreadable (fail-closed).
        await using (var hostScope = run.Factory.Services.CreateAsyncScope())
        {
            var hosted = hostScope.ServiceProvider.GetRequiredService<ISalesSubcontractServiceOrderReadPortV1>();
            Assert.Equal(SalesSubcontractServiceOrderOutcomeV1.Unreadable, (await hosted.GetAsync(CompanyA, draft.Id)).Outcome);
        }

        // Approved: the port says so; a free-price revision archives the approved header WITH the flag (SAT-D175: part of the snapshot).
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.UpdateDraftStateAsync(run.SalesEnvelope("f-submit"), Hash("f-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, run.Actor, now), Submitted);
            await scope.Store.UpdateDraftStateAsync(run.SalesEnvelope("f-approve", "SENTETIK-approver"), Hash("f-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            var port = new SalesSubcontractServiceOrderReadPort(scope.Store, run.Company(CompanyA));
            var approved = (await port.GetAsync(CompanyA, draft.Id)).Order!;
            Assert.Equal(("Approved", true, 4L), (approved.ApprovalState, approved.IsApproved, approved.Version));
        }

        await using (var scope = run.SalesStore(CompanyA))
        {
            var revised = await scope.Store.ReviseDraftCommercialAsync(run.SalesEnvelope("f-rev"), Hash("f-rev"),
                SalesDraftCommands.ReviseCommercialV1, draft.Id,
                (d, now) => d.ReviseCommercial(4, [new LineCommercialInput(line.Id, Free("41", now))], "SENTETIK-writer", now), Revised,
                _ => Task.CompletedTask);
            Assert.Equal((1, "Preparing"), (revised!.Draft.Revision, revised.Draft.ApprovalState.ToString()));
        }

        Assert.Equal((1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisionHeaders] WHERE [DraftId] = @id AND [IsSubcontractService] = 1",
                ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisionLines] WHERE [DraftId] = @id "
                + "AND JSON_VALUE([PricingJson], '$.listAbsence.unitPrice') = N'37.5'", ("@id", draft.Id))));
        await using (var scope = run.SalesStore(CompanyA))
        {
            var stored = (await scope.Store.FindDraftAsync(draft.Id))!;
            Assert.True(Assert.Single(stored.ArchivedRevisions).Header!.IsSubcontractService);
            Assert.Equal("41", stored.Lines[0].Commercial.ListAbsence!.UnitPrice);
        }

        // The routes: the detail carries the flag (draft read); the line pricing carries the absence (price read).
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}";
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceRead);
        using var client = run.Client(reader, CompanyA);
        using (var response = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(detail.GetProperty("draft").GetProperty("subcontractService").GetBoolean());
        }

        using (var response = await client.GetAsync($"{path}/line-pricing"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var pricing = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lines").EnumerateArray());
            Assert.Equal(("41", "SENTETIK-writer"), (pricing.GetProperty("listAbsence").GetProperty("unitPrice").GetString(),
                pricing.GetProperty("listAbsence").GetProperty("writtenBy").GetString()));
            Assert.Equal(JsonValueKind.Null, pricing.GetProperty("discountCheck").ValueKind);
        }

        using (var response = await client.GetAsync($"{SalesEndpoints.DraftsPath}/{normal.Id:D}"))
        {
            Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("draft").TryGetProperty("subcontractService", out _));
        }
    }

    [SqlServerFact]
    public async Task The_write_guard_never_moves_the_flag_and_the_database_refuses_another_item_type_and_the_rollback_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.StoredDraftAsync(CompanyA, "g");

        await using (var context = run.SalesContext(CompanyA))
        {
            var row = await context.Drafts.SingleAsync(candidate => candidate.Id == draft.Id);
            row.IsSubcontractService = false;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        var constraint = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[DraftLines] SET [ItemType] = N'Steel' WHERE [DraftId] = @id; SELECT @@ROWCOUNT", ("@id", draft.Id)));
        Assert.Equal(547, constraint.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[Drafts] WHERE [Id] = @id AND [IsSubcontractService] = 1",
            ("@id", draft.Id)));

        // The rollback refuses (51141) while a subcontract order exists; nothing of it is dropped (forward fix).
        await using (var context = run.SalesContext(CompanyA))
        {
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var index = Array.FindIndex(applied, migration => migration.EndsWith("_AddDraftSubcontractService", StringComparison.Ordinal));
            Assert.Equal(applied.Length - 1, index);
            var refused = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[index - 1]));
            Assert.Equal(51141, refused.Number);
            Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A SENTETIK free subcontract price: the list held neither a price nor a floor when SENTETIK-writer typed it.</summary>
    private static CommercialDraftFields Free(string price, DateTimeOffset at) =>
        new(price, KgUnit, null, ListAbsence: new PriceListAbsenceSnapshot(KgUnit, "TRY", Day, price, "SENTETIK-writer", at));

    /// <summary>A draft with one 250 kg service line (free price on a subcontract order, unpriced otherwise) from SYNTHETIC owner snapshots.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo, bool subcontract)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30,
            BuyerTax: SalesBuyerTax.Create(TaxBuyerSituationsV1.Normal), IsSubcontractService: subcontract);
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "HZM-001", "Sentetik işçilik", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "250", 250m, Day.AddDays(3),
            Commercial: subcontract ? Free("37.5", T0) : null, ItemType: subcontract ? SalesOrderDraft.ServiceItemType : null), T0);
        return draft;
    }


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
        public string Correlation { get; } = $"sales-fason-{Guid.NewGuid():N}";

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

        /// <summary>A stored draft of the company, submitted and approved (version 4), through the real Sales store.</summary>
        public async Task<SalesOrderDraft> StoredDraftAsync(Guid company, string keyPrefix, bool subcontract = true)
        {
            await using var scope = SalesStore(company);
            return (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo, subcontract))).Draft;
        }

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public SalesDbContext SalesContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

        public CompanyContext Company(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return companyContext;
        }

        public SalesScope SalesStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        /// <summary>A fresh synthetic role of the given names for the bootstrap subject in one company (company-wide scope).</summary>
        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales fason user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"FASON-{roleId:N}", "Synthetic Sales fason role", true);
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
