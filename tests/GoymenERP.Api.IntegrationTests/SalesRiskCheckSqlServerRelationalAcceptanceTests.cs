using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LogisticsQuantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals>;
using LogisticsQuantityUnit = GoymenERP.BuildingBlocks.Quantities.QuantityUnit;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// MOD-01 / API-06 against a REAL SQL Server and the REAL host composition: <c>GET /api/v1/sales/drafts/{id}/risk-check</c>.
/// Open receivables come from Finance open items ADMITTED through the real <see cref="FinanceOpenItemSourceAdmissionService"/>
/// (approved official invoice installments) and are read by Sales only through the Finance-owned
/// <see cref="ISalesPartyRiskReadPortV1"/>; the approved-unshipped remainder from approved priced orders stored through the real
/// Sales store and a shipment recorded through the real Logistics store (AT-024 port). "Shipped but not invoiced" (S5), the credit
/// limit and collateral have no owner record on main: the result is "Unavailable" with every reason — never "Within". The read
/// writes nothing in any module; another company's receivables never reach the check and another company cannot read it (404);
/// without the draft read it is 403; without the price read every amount is withheld.
/// <para>
/// Owner inputs (the approved source batch, the party snapshot, the Sales owner snapshots) are ISOLATED SYNTHETIC fixtures; no row
/// of another module is inserted directly. SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed class SalesRiskCheckSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 10, 26);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("6b7e0c11-5d2b-4c8e-9f10-2b3c4d5e6f01");
    private static readonly Guid MoneyProfile = Guid.Parse("6b7e0c11-5d2b-4c8e-9f10-2b3c4d5e6f02");
    private static readonly Guid KgProfile = Guid.Parse("6b7e0c11-5d2b-4c8e-9f10-2b3c4d5e6f03");
    private static readonly Guid Product = Guid.Parse("6b7e0c11-5d2b-4c8e-9f10-2b3c4d5e6f04");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly LogisticsMutationAudit Issued = new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit Loaded = new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");
    private static readonly LogisticsMutationAudit Shipped = new("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi");

    [SqlServerFact]
    public async Task Open_receivables_and_the_approved_remainder_come_from_their_real_owners_and_the_check_stays_unavailable_without_a_source_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var customer = new Customer(Guid.NewGuid(), Guid.NewGuid());
        // Two approved invoice installments of the customer in company A (1000.00 + 1001.00) through the real admission.
        await run.AdmitOpenItemsAsync(CompanyA, customer, "1000.00", "1001.00");
        // Two approved priced orders of the customer: 10 kg and 4 kg at 12.50 − 5 % = 11.875 net per kg.
        var first = await run.ApprovedDraftAsync(CompanyA, customer, "a", "10");
        var second = await run.ApprovedDraftAsync(CompanyA, customer, "b", "4");
        // 6 of the first order's 10 kg shipped through the real Logistics store: 4 kg left → 47.50; the second untouched → 47.50.
        var instructionId = await run.ShippedInstructionAsync(CompanyA, first, "a", instructed: 10m, shipped: 6m);

        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersPriceRead);
        using var client = run.Client(reader, CompanyA);
        var path = $"{SalesEndpoints.DraftsPath}/{second.Id:D}/risk-check";
        var before = await run.EffectsAsync();

        var check = await run.RiskCheckAsync(client, path);

        Assert.Equal(["draftVersion", "partyId", "companyAccountId", "currencyId", "result", "reasons", "amountDisclosure", "grossRisk",
            "components", "creditLimit", "collateral"], check.EnumerateObject().Select(member => member.Name));
        Assert.Equal(("4", customer.PartyId.ToString("D"), customer.AccountId.ToString("D"), Currency.ToString("D"), "Unavailable", "Visible"),
            (check.GetProperty("draftVersion").GetString(), check.GetProperty("partyId").GetString(),
                check.GetProperty("companyAccountId").GetString(), check.GetProperty("currencyId").GetString(),
                check.GetProperty("result").GetString(), check.GetProperty("amountDisclosure").GetString()));
        // Fail-closed: no gross risk while a stage has no source and the stages are on different tax bases.
        Assert.Equal(JsonValueKind.Null, check.GetProperty("grossRisk").ValueKind);
        Assert.Equal(
            [("ShippedNotInvoiced", "invoicing_owner_missing"), ("Check", "tax_basis_mismatch"), ("CreditLimit", "finance_credit_limit_owner_missing")],
            check.GetProperty("reasons").EnumerateArray().Select(reason => (reason.GetProperty("component").GetString(), reason.GetProperty("code").GetString())));
        Assert.Equal(
            [
                ("OpenReceivables", "finance.open-items", "Available", "IncludingTax", "2001.00", null),
                ("ShippedNotInvoiced", "sales.invoicing", "NoSource", null, null, "invoicing_owner_missing"),
                ("ApprovedUnshippedRemainder", "sales.approved-order-remainders", "Available", "ExcludingTax", "95.00", null),
            ],
            check.GetProperty("components").EnumerateArray().Select(Figure));
        Assert.Equal(("CreditLimit", "finance.party-credit-limit", "NoSource", null, null, "finance_credit_limit_owner_missing"),
            Figure(check.GetProperty("creditLimit")));
        Assert.Equal(("Collateral", "finance.party-collateral", "NoSource", null, null, "finance_collateral_owner_missing"),
            Figure(check.GetProperty("collateral")));

        // The read writes nothing in any module (no attempt, audit, command, share or version effect).
        Assert.Equal(before, await run.EffectsAsync());
        Assert.Equal(1L, await run.ScalarAsync<long>(
            "SELECT [Version] FROM [logistics].[LoadingInstructions] WHERE [Id] = @id", ("@id", instructionId)) > 0 ? 1L : 0L);

        // Without the price read: every amount withheld; the states, sources, reasons and the result stay.
        var withoutPrice = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using var priceless = run.Client(withoutPrice, CompanyA);
        var withheld = await run.RiskCheckAsync(priceless, path);
        Assert.Equal(("Withheld", "Unavailable", JsonValueKind.Null), (withheld.GetProperty("amountDisclosure").GetString(),
            withheld.GetProperty("result").GetString(), withheld.GetProperty("grossRisk").ValueKind));
        Assert.All(withheld.GetProperty("components").EnumerateArray(), figure =>
            Assert.Equal(JsonValueKind.Null, figure.GetProperty("amounts").ValueKind));
        Assert.Equal(("Available", "Available"), (withheld.GetProperty("components")[0].GetProperty("state").GetString(),
            withheld.GetProperty("components")[2].GetProperty("state").GetString()));
    }

    [SqlServerFact]
    public async Task Another_companys_receivables_never_reach_the_check_and_only_a_draft_read_of_its_own_company_reads_it_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var customer = new Customer(Guid.NewGuid(), Guid.NewGuid());
        // Company B admits open items under the SAME party and account identities: B's rows only.
        await run.AdmitOpenItemsAsync(CompanyB, customer, "5000.00");
        var draft = await run.ApprovedDraftAsync(CompanyA, customer, "k", "2");

        // The Finance owner port under company A sees nothing of B's items; under B it sees them.
        await using (var a = run.FinanceRead(CompanyA))
        {
            var answer = await new FinanceSalesPartyRiskReadPortV1(a.Context, a.Company).ReadPartyRiskAsync(customer.PartyId, customer.AccountId);
            Assert.Equal((CompanyA, SalesPartyRiskFigureStateV1.Available), (answer.CompanyId, answer.OpenReceivables.State));
            Assert.Empty(answer.OpenReceivables.Amounts);
        }

        await using (var b = run.FinanceRead(CompanyB))
        {
            var answer = await new FinanceSalesPartyRiskReadPortV1(b.Context, b.Company).ReadPartyRiskAsync(customer.PartyId, customer.AccountId);
            Assert.Equal([new SalesPartyRiskAmountV1(Currency, 5000m)], answer.OpenReceivables.Amounts);
        }

        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/risk-check";
        // Company A's read-only user holds no Sales permission (none is seeded): 403 before anything is read.
        using (var ungranted = run.Client(TestUsers.CompanyAViewer, CompanyA))
        using (var forbidden = await ungranted.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.True(forbidden.Headers.CacheControl is { Private: true, NoStore: true });
        }

        // Company B with its own draft read: A's draft does not exist for it (404, never 403).
        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersPriceRead);
        using (var clientOfB = run.Client(readerOfB, CompanyB))
        using (var missing = await clientOfB.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.True(missing.Headers.CacheControl is { Private: true, NoStore: true });
        }

        // Company A: B's 5000.00 is invisible — a recorded zero; the order's 2 kg is unshipped → 23.75.
        var readerOfA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersPriceRead);
        using var clientOfA = run.Client(readerOfA, CompanyA);
        var check = await run.RiskCheckAsync(clientOfA, path);
        var components = check.GetProperty("components").EnumerateArray().Select(Figure).ToArray();
        Assert.Equal(("OpenReceivables", "finance.open-items", "Available", "IncludingTax", "", null), components[0]);
        Assert.Equal(("ApprovedUnshippedRemainder", "sales.approved-order-remainders", "Available", "ExcludingTax", "23.75", null), components[2]);
        Assert.Equal("Unavailable", check.GetProperty("result").GetString());
    }

    // ---- helpers --------------------------------------------------------------------------------

    private sealed record Customer(Guid PartyId, Guid AccountId);

    private static (string? Kind, string? Source, string? State, string? TaxBasis, string? Amounts, string? Reason) Figure(JsonElement figure) =>
        (figure.GetProperty("kind").GetString(), figure.GetProperty("source").GetString(), figure.GetProperty("state").GetString(),
            figure.GetProperty("taxBasis").GetString(),
            figure.GetProperty("amounts").ValueKind == JsonValueKind.Null
                ? null
                : string.Join("|", figure.GetProperty("amounts").EnumerateArray().Select(amount =>
                {
                    Assert.Equal(Currency.ToString("D"), amount.GetProperty("currencyId").GetString());
                    return amount.GetProperty("amount").GetString();
                })),
            figure.GetProperty("reasonCode").GetString());

    private static LogisticsQuantity Kg(decimal value) => LogisticsQuantity.Of(value, LogisticsQuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line priced draft (kg, 12.50 per kg, 5 % discount) of the customer from SYNTHETIC owner snapshots: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, Customer customer, string documentNo, string quantity)
    {
        var header = new SalesDraftHeader(customer.PartyId, customer.AccountId, Currency, Day, Day.AddDays(10));
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3),
            Commercial: new CommercialDraftFields("12.50", KgUnit, "5")), T0);
        return draft;
    }

    /// <summary>(sales attempts, audit events, finance commands, open items, logistics attempts, sales draft versions).</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, long FinanceCommands, long OpenItems, int LogisticsAttempts, long DraftVersions);

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class LogisticsScope(LogisticsDbContext dbContext, LogisticsStore store) : IAsyncDisposable
    {
        public ILogisticsInstructionStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class FinanceScope(FinanceDbContext context, CompanyContext company) : IAsyncDisposable
    {
        public FinanceDbContext Context => context;

        public CompanyContext Company => company;

        public ValueTask DisposeAsync() => context.DisposeAsync();
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
        public string Correlation { get; } = $"sales-api-06-{Guid.NewGuid():N}";

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

        /// <summary>Approved official invoice installments of the customer, admitted through the real Finance admission service.</summary>
        public async Task AdmitOpenItemsAsync(Guid company, Customer customer, params string[] amounts)
        {
            var batchId = Guid.NewGuid();
            var batchVersion = $"approved-file-{batchId:N}";
            var checksum = Convert.ToHexStringLower(SHA256.HashData(batchId.ToByteArray()));
            var rows = amounts.Select((amount, index) => new ApprovedFinanceOpenItemSourceRow(
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), $"invoice-v1-{index}",
                    FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument, customer.PartyId, customer.AccountId, Currency, amount,
                    Day.AddDays(30 + index)))
                .ToArray();
            var batch = new ApprovedFinanceOpenItemSourceBatch(batchId, company, batchVersion, checksum, rows);
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            await using var db = FinanceContext(companyContext, collector);
            const string admitter = "SENTETIK-approved-file-admitter";
            var service = new FinanceOpenItemSourceAdmissionService(db, companyContext, new FixedCurrentUser(admitter, company),
                new AllowedAdmissionAuthority(), new FixedApprovedSource(batch), new ActiveParty(company, customer), collector,
                new FinanceAuditEnvelope("synthetic.finance.open-item-source.admit", ["SYNTHETIC-FINANCE"], "Web"), TimeProvider.System);
            var result = await service.AdmitAsync(new FinanceContext(company, admitter, $"api-06-admit-{Guid.NewGuid():N}", null),
                new CommandMetadata($"api-06-approved-file-{batchId:N}"), new FinanceOpenItemSourceReference(batchId, batchVersion, checksum),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            Assert.Equal(rows.Length, result.OpenItemIds.Count);
        }

        /// <summary>A stored priced draft of the customer, submitted and approved (version 4), through the real Sales store.</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, Customer customer, string keyPrefix, string quantity)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{Correlation}-{keyPrefix}-create"),
                documentNo => Draft(company, customer, documentNo, quantity))).Draft;
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{Correlation}-{keyPrefix}-submit"),
                SalesDraftCommands.SubmitV1, draft.Id, (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"),
                Hash($"{Correlation}-{keyPrefix}-approve"), SalesDraftCommands.ApproveV1, draft.Id,
                (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        /// <summary>An instruction for the draft's one line, created, issued, loaded in full and shipped, through the real Logistics store.</summary>
        public async Task<Guid> ShippedInstructionAsync(Guid company, SalesOrderDraft draft, string keyPrefix, decimal instructed, decimal shipped)
        {
            var orderLine = Assert.Single(draft.Lines);
            await using var scope = LogisticsStore(company);
            var stored = (await scope.Store.StoreInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-create"), Hash($"{keyPrefix}-l-create"), Day, () =>
            {
                var instruction = LoadingInstruction.Create(Guid.NewGuid(), company, draft.Header.PartyId, draft.Header.CompanyAccountId, T0);
                instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(draft.Id, orderLine.Id, draft.Revision),
                    orderLine.Product.ProductId, KgUnit, null, Kg(instructed), T0);
                return instruction;
            })).Instruction.Instruction;
            var lineId = Assert.Single(stored.Lines).Id;
            var id = stored.Id;
            var issued = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-issue"), Hash($"{keyPrefix}-l-issue"),
                LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(stored.Version, now), Issued))!.Instruction.Instruction;
            var loaded = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-load"), Hash($"{keyPrefix}-l-load"),
                LogisticsCommands.RecordLoadV1, id, (instruction, now) => instruction.RecordLoad(issued.Version, Guid.NewGuid(), lineId, Kg(instructed),
                    MeasurementKind.Measured, now), Loaded))!.Instruction.Instruction;
            await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-ship"), Hash($"{keyPrefix}-l-ship"),
                LogisticsCommands.RecordShipmentV1, id, (instruction, now) => instruction.MarkShipped(loaded.Version, Guid.NewGuid(), lineId, Kg(shipped), now),
                Shipped);
            return id;
        }

        public async Task<JsonElement> RiskCheckAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync() => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<long>("SELECT COUNT_BIG(*) FROM [finance].[Commands]"),
            await ScalarAsync<long>("SELECT COUNT_BIG(*) FROM [finance].[OpenItems]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [logistics].[LoadingAttempts]"),
            await ScalarAsync<long>("SELECT ISNULL(SUM([Version]), 0) FROM [sales].[Drafts]"));

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public LogisticsWriteEnvelope LogisticsEnvelope(string key) =>
            new(Actor, "SENTETIK-logistics-permission", ["SENTETIK-LOGISTICS"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

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

        public LogisticsScope LogisticsStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new LogisticsScope(dbContext, new LogisticsStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public FinanceScope FinanceRead(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceScope(FinanceContext(companyContext, null), companyContext);
        }

        private FinanceDbContext FinanceContext(CompanyContext company, AuditEventCollector? collector)
        {
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            if (collector is not null)
            {
                options.AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
            }

            return new FinanceDbContext(options.Options, company);
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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales risk-check user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"API-06-{roleId:N}", "Synthetic Sales risk-check role", true);
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

    private sealed class AllowedAdmissionAuthority : IFinanceOpenItemSourceAuthority
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class FixedApprovedSource(ApprovedFinanceOpenItemSourceBatch batch) : IApprovedFinanceOpenItemSourcePort
    {
        public Task<ReadResult<ApprovedFinanceOpenItemSourceBatch>> GetApprovedAsync(FinanceContext context,
            FinanceOpenItemSourceReference source, CancellationToken cancellationToken) =>
            Task.FromResult(new ReadResult<ApprovedFinanceOpenItemSourceBatch>(ReadOutcome.Found, batch));
    }

    private sealed class ActiveParty(Guid company, Customer customer) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId, Guid companyAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(context.CompanyId == company && partyId == customer.PartyId && companyAccountId == customer.AccountId
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(customer.PartyId, customer.AccountId, company, true, IsPartyActive: true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    private sealed class FixedCurrentUser(string userId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid company) => company == companyId;
    }
}
