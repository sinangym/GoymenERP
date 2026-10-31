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
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LogisticsQuantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals>;
using LogisticsQuantityUnit = GoymenERP.BuildingBlocks.Quantities.QuantityUnit;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// API-41 / API-42 (plan §3.4, UI-80; KPI No 124, No 25, SAT-D25) against a REAL SQL Server and the REAL host composition: the open
/// order report groups the approved, not closed or cancelled orders' lines per customer account, product and unit with the
/// approved − shipped quantity (the shipped total from the Logistics owner's port), filters by order date and customer on the server
/// and pages by a company- and filter-bound cursor; the lost opportunities carry the SAT-D25 reason snapshot and the calculator's net
/// amount, withheld without the price read; the price variance answers NoSource. Every report writes nothing in any module, needs
/// its own <c>sales.reports.read</c>, and never shows another company's records.
/// <para>
/// Data is set up through the owners only: customers through the real Cari route, orders and quotes through the real Sales store from
/// ISOLATED SYNTHETIC owner snapshots (the API-30 precedent), instructions and shipments through the real Logistics store. The unit is
/// the approved GDC kilogram (Ö1/Ö2); the currency Turkish lira. SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed class SalesReportSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    // SAT-D130: on or after the approved SAT-D5 tolerance of the synthetic companies' Settings record (GDC from 2026-10-25, GSP
    // from 2026-10-26), so the real Settings owner answers the 1 / 2 / 3 % ladder for the orders of this day.
    private static readonly DateOnly Day = new(2026, 10, 26);
    // Before the tolerance record's window: the real owner answers no policy for an order of this day (fail-closed).
    private static readonly DateOnly BeforePolicy = new(2026, 10, 20);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly Guid MoneyProfile = Guid.Parse("7c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d32");
    private static readonly Guid KgProfile = Guid.Parse("7c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d33");
    private static readonly Guid Product = Guid.Parse("7c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d34");
    private static readonly Guid PriceReason = Guid.Parse("7c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d35");
    private static readonly Guid LossListProfile = Guid.Parse("7c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d36");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit ApprovedAudit = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit Lost = new("sales.quote.lost.v1", "Lose", "SENTETIK kaybedildi");
    private static readonly SalesDraftStateAudit AcceptedAudit =
        new("sales.order-draft.customer-acceptance-recorded.v1", "RecordCustomerAcceptance", "SENTETIK müşteri kabul etti");
    private static readonly SalesDraftStateAudit RevisedAudit = new("sales.order-draft.quantity-revised.v1", "ReviseQuantity", "SENTETIK revizyon");
    private static readonly LogisticsMutationAudit Issued = new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit Loaded = new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");
    private static readonly LogisticsMutationAudit Shipped = new("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi");

    [SqlServerFact]
    public async Task The_reports_answer_open_quantities_lost_quotes_and_no_source_variance_from_the_owners_and_write_nothing_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var first = await run.CustomerAsync(CompanyA, "SENTETIK Rapor Müşterisi A.Ş.");
        var second = await run.CustomerAsync(CompanyA, "SENTETIK İkinci Müşteri A.Ş.");
        // Orders as the current service path builds them (Q9: customer acceptance required), approved and ACCEPTED: two of the first
        // customer (6 + 3 kg and 2 kg) and one of the second (5 kg); a draft never approved is not open and never appears.
        var big = await run.AcceptedDraftAsync(CompanyA, first, "o1", "6", "3");
        var small = await run.AcceptedDraftAsync(CompanyA, first, "o2", "2");
        var other = await run.AcceptedDraftAsync(CompanyA, second, "o3", "5");
        await run.DraftAsync(CompanyA, first, "o4", "100");
        // Approved inside, never accepted by the customer (it cannot ship or invoice): not open tonnage, 4 kg awaiting acceptance.
        var pending = await run.ApprovedDraftAsync(CompanyA, first, "o6", "4");
        // Rev.0 10 kg accepted, Rev.1 15 kg approved inside and awaiting acceptance, 3 kg shipped (30 % short of the accepted 10):
        // 10 − 3 open, the +5 awaiting acceptance.
        var revised = await run.RevisedAwaitingAcceptanceAsync(CompanyA, first, "o7", "10", "15");
        // 4 kg of the 6 kg line shipped through Logistics: 33 % short (beyond SAT-D5's 1 %), 2 kg of that line stays open.
        await run.ShipAsync(CompanyA, big, big.Lines.Single(line => line.LineNo == 1), "s1", 4m);
        // SAT-D130: 2.99 kg of the 3 kg line (0.33 % short, within the 1 % tolerance) is "Tamamlandı" — nothing of it is open.
        await run.ShipAsync(CompanyA, big, big.Lines.Single(line => line.LineNo == 2), "s2", 2.99m);
        // An order of a day before the tolerance record, shipped short: no readable policy — unmeasured, never open or closed.
        var early = await run.AcceptedDraftAsync(CompanyA, first, "o5", BeforePolicy, "5");
        await run.ShipAsync(CompanyA, early, Assert.Single(early.Lines), "s3", 1m);
        await run.ShipAsync(CompanyA, revised, Assert.Single(revised.Lines), "s4", 3m, planned: 10m);
        var quote = await run.LostQuoteAsync(CompanyA, first, "q1", "1.25", "12.50", "5", "SENTETIK rakip daha ucuz");

        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.ReportsRead,
            SalesPermissionManifestV1.OrdersPriceRead);
        using (var client = run.Client(reader, CompanyA))
        {
            var before = await run.EffectsAsync();
            var report = await run.ReadAsync(client, SalesEndpoints.ReportOpenOrdersPath);
            Assert.Equal("ApprovedMinusShipped", report.GetProperty("measure").GetString());
            var groups = report.GetProperty("groups").EnumerateArray().ToArray();
            Assert.Equal(2, groups.Length);
            var mine = Assert.Single(groups, group => group.GetProperty("customer").GetProperty("companyAccountId").GetGuid() == first.AccountId);
            // Listed: big/1 (6 − 4 = 2), small/1 (2), pending (0 open, 4 awaiting), revised (10 − 3 = 7 open, 5 awaiting). big/2 (2.99
            // of 3, within SAT-D5) is completed — closed history, not listed (SAT-D130). The early order is unmeasured (no policy).
            Assert.Equal((first.Code, Product, KgUnit, "kg", "Measured", 5, 4, "18", "7", "11", "9", false),
                (mine.GetProperty("customer").GetProperty("code").GetString(), mine.GetProperty("productId").GetGuid(),
                    mine.GetProperty("unitId").GetGuid(), mine.GetProperty("unitCode").GetString(), mine.GetProperty("state").GetString(),
                    mine.GetProperty("scopeOrderCount").GetInt32(), mine.GetProperty("orderCount").GetInt32(),
                    mine.GetProperty("approvedQuantity").GetString(), mine.GetProperty("shippedQuantity").GetString(),
                    mine.GetProperty("remainingQuantity").GetString(), mine.GetProperty("awaitingAcceptanceQuantity").GetString(),
                    mine.GetProperty("complete").GetBoolean()));
            Assert.Equal(
                [
                    (big.DocumentNo, 1, "6", "4", "2", "0", (string?)"Rev.0", (string?)null),
                    (small.DocumentNo, 1, "2", "0", "2", "0", "Rev.0", null),
                    (pending.DocumentNo, 1, "0", "0", "0", "4", null, "Rev.0"),
                    (revised.DocumentNo, 1, "10", "3", "7", "5", "Rev.0", "Rev.1"),
                ],
                mine.GetProperty("lines").EnumerateArray().Select(line => (line.GetProperty("documentNo").GetString(),
                    line.GetProperty("lineNo").GetInt32(), line.GetProperty("approvedQuantity").GetString(),
                    line.GetProperty("shippedQuantity").GetString(), line.GetProperty("remainingQuantity").GetString(),
                    line.GetProperty("awaitingAcceptanceQuantity").GetString(), line.GetProperty("basisRevision").GetString(),
                    line.GetProperty("awaitingAcceptanceRevision").GetString()))
                    .OrderBy(line => line.Item1, StringComparer.Ordinal).ThenBy(line => line.Item2));
            var unmeasured = Assert.Single(mine.GetProperty("unmeasured").EnumerateArray());
            Assert.Equal((early.Id, "TolerancePolicyUnavailable"), (unmeasured.GetProperty("orderId").GetGuid(),
                unmeasured.GetProperty("reason").GetString()));
            var theirs = Assert.Single(groups, group => group.GetProperty("customer").GetProperty("companyAccountId").GetGuid() == second.AccountId);
            Assert.Equal(("5", other.DocumentNo), (theirs.GetProperty("remainingQuantity").GetString(),
                Assert.Single(theirs.GetProperty("lines").EnumerateArray()).GetProperty("documentNo").GetString()));

            // Paged by the company- and filter-bound cursor: two pages of one group, nothing skipped or repeated.
            var head = await run.ReadAsync(client, $"{SalesEndpoints.ReportOpenOrdersPath}?pageSize=1");
            var cursor = head.GetProperty("nextCursor").GetString()!;
            var tail = await run.ReadAsync(client, $"{SalesEndpoints.ReportOpenOrdersPath}?pageSize=1&cursor={Uri.EscapeDataString(cursor)}");
            Assert.Equal(JsonValueKind.Null, tail.GetProperty("nextCursor").ValueKind);
            Assert.Equal(groups.Select(Key).Order(), head.GetProperty("groups").EnumerateArray().Concat(tail.GetProperty("groups").EnumerateArray())
                .Select(Key).Order());
            // The same cursor under another filter is refused, never read as a position.
            using var otherFilter = await client.GetAsync(
                $"{SalesEndpoints.ReportOpenOrdersPath}?pageSize=1&from=2026-01-01&cursor={Uri.EscapeDataString(cursor)}");
            Assert.Equal(HttpStatusCode.BadRequest, otherFilter.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.InvalidCursor,
                (await otherFilter.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

            // The customer and the order date narrow on the server.
            var narrowed = await run.ReadAsync(client, $"{SalesEndpoints.ReportOpenOrdersPath}?companyAccountId={second.AccountId:D}");
            Assert.Equal(second.AccountId, Assert.Single(narrowed.GetProperty("groups").EnumerateArray())
                .GetProperty("customer").GetProperty("companyAccountId").GetGuid());
            Assert.Empty((await run.ReadAsync(client, $"{SalesEndpoints.ReportOpenOrdersPath}?from={Day.AddDays(1):yyyy-MM-dd}"))
                .GetProperty("groups").EnumerateArray());
            var today = (await run.ReadAsync(client, $"{SalesEndpoints.ReportOpenOrdersPath}?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}"))
                .GetProperty("groups").EnumerateArray().ToArray();
            Assert.Equal(2, today.Length);
            // Without the early order the first customer's group is complete again.
            Assert.True(Assert.Single(today, group => group.GetProperty("customer").GetProperty("companyAccountId").GetGuid() == first.AccountId)
                .GetProperty("complete").GetBoolean());
            foreach (var malformed in new[] { "?from=2026-10-21&to=2026-10-20", "?from=20.10.2026", "?pageSize=101", "?unknown=1",
                         $"?companyAccountId={Guid.Empty:D}" })
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(SalesEndpoints.ReportOpenOrdersPath + malformed)).StatusCode);
            }

            // SAT-D25: the lost quote with its list reason snapshot, the explanation and the calculator's net amount (1.25 kg ×
            // (12.50 − 5 %) = 14.84375 → 14.84 at the TL money profile scale, SAT-D11), in the quote's own currency.
            var lost = await run.ReadAsync(client, $"{SalesEndpoints.ReportLostOpportunitiesPath}?from={Day:yyyy-MM-dd}&reasonId={PriceReason:D}");
            Assert.Equal("Visible", lost.GetProperty("amountDisclosure").GetString());
            var item = Assert.Single(lost.GetProperty("items").EnumerateArray());
            Assert.Equal((quote.Id, quote.DocumentNo, "SENTETIK-FIYAT", "SENTETIK rakip daha ucuz", Currency),
                (item.GetProperty("quoteId").GetGuid(), item.GetProperty("documentNo").GetString(),
                    item.GetProperty("reason").GetProperty("code").GetString(), item.GetProperty("explanation").GetString(),
                    item.GetProperty("currencyId").GetGuid()));
            Assert.Equal(("Computed", "14.84", 2), (item.GetProperty("amount").GetProperty("state").GetString(),
                item.GetProperty("amount").GetProperty("netAmount").GetString(), item.GetProperty("amount").GetProperty("amountScale").GetInt32()));
            Assert.Empty((await run.ReadAsync(client, $"{SalesEndpoints.ReportLostOpportunitiesPath}?reasonId={Guid.NewGuid():D}"))
                .GetProperty("items").EnumerateArray());

            // No 25: no invoice owner — "kaynak yok", never an empty list.
            var variance = await run.ReadAsync(client, SalesEndpoints.ReportPriceVariancePath);
            Assert.Equal(("NoSource", JsonValueKind.Null, JsonValueKind.Null), (variance.GetProperty("state").GetString(),
                variance.GetProperty("items").ValueKind, variance.GetProperty("nextCursor").ValueKind));
            // No page was ever issued while there is no source: a cursor is refused, never ignored.
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{SalesEndpoints.ReportPriceVariancePath}?cursor=abc")).StatusCode);

            // The reads wrote nothing: no audit event, attempt or version moved in Sales or Logistics.
            Assert.Equal(before, await run.EffectsAsync());
        }

        // The report read alone: the amounts are withheld and the price variance is closed (it is prices).
        var plain = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SalesPermissionManifestV1.ReportsRead);
        using (var client = run.Client(plain, CompanyA))
        {
            var lost = await run.ReadAsync(client, SalesEndpoints.ReportLostOpportunitiesPath);
            Assert.Equal("Withheld", lost.GetProperty("amountDisclosure").GetString());
            Assert.Equal(JsonValueKind.Null, Assert.Single(lost.GetProperty("items").EnumerateArray()).GetProperty("amount").ValueKind);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(SalesEndpoints.ReportPriceVariancePath)).StatusCode);
            Assert.Equal(2, (await run.ReadAsync(client, SalesEndpoints.ReportOpenOrdersPath)).GetProperty("groups").GetArrayLength());
        }
    }

    [SqlServerFact]
    public async Task Another_companys_orders_and_quotes_never_appear_and_an_ungranted_user_reads_no_report_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var customerOfA = await run.CustomerAsync(CompanyA, "SENTETIK A Rapor A.Ş.");
        var customerOfB = await run.CustomerAsync(CompanyB, "SENTETIK B Rapor A.Ş.");
        var orderOfA = await run.ApprovedDraftAsync(CompanyA, customerOfA, "a", "5");
        var orderOfB = await run.ApprovedDraftAsync(CompanyB, customerOfB, "b", "7");
        await run.LostQuoteAsync(CompanyA, customerOfA, "qa", "1", "10", null, null);
        var quoteOfB = await run.LostQuoteAsync(CompanyB, customerOfB, "qb", "2", "10", null, null);

        // The store under each company: each sees only its own open order and lost quote.
        await using (var b = run.SalesStore(CompanyB))
        {
            var all = new SalesOpenOrderReportFilter(null, null, null);
            var scope = Assert.Single(await b.Reports.ListOpenOrderGroupsAsync(all, null, 50));
            Assert.Equal((customerOfB.AccountId, 1), (scope.Key.CompanyAccountId, scope.OrderCount));
            Assert.Equal(orderOfB.Id, Assert.Single(await b.Reports.ListOpenOrdersOfGroupAsync(all, scope.Key, null, 10)).Id);
            Assert.Equal(quoteOfB.Id, Assert.Single((await b.Reports.ListLostQuotesAsync(new SalesLostQuoteReportFilter(null, null, null, null), 50, null)).Items).Id);
            // A filter naming A's customer finds nothing in B.
            Assert.Empty(await b.Reports.ListOpenOrderGroupsAsync(new SalesOpenOrderReportFilter(null, null, customerOfA.AccountId), null, 50));
        }

        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.ReportsRead);
        using (var clientOfB = run.Client(readerOfB, CompanyB))
        {
            var report = await run.ReadAsync(clientOfB, SalesEndpoints.ReportOpenOrdersPath);
            var group = Assert.Single(report.GetProperty("groups").EnumerateArray());
            Assert.Equal((customerOfB.AccountId, orderOfB.Id), (group.GetProperty("customer").GetProperty("companyAccountId").GetGuid(),
                Assert.Single(group.GetProperty("lines").EnumerateArray()).GetProperty("orderId").GetGuid()));
            Assert.Equal(quoteOfB.Id, Assert.Single((await run.ReadAsync(clientOfB, SalesEndpoints.ReportLostOpportunitiesPath))
                .GetProperty("items").EnumerateArray()).GetProperty("quoteId").GetGuid());
        }

        // Company A's cursor (read by A's reader from A's two groups) is refused in B.
        await run.ApprovedDraftAsync(CompanyA, await run.CustomerAsync(CompanyA, "SENTETIK A İkinci A.Ş."), "a2", "1");
        var readerOfA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.ReportsRead);
        string cursorOfA;
        using (var clientOfA = run.Client(readerOfA, CompanyA))
        {
            cursorOfA = (await run.ReadAsync(clientOfA, $"{SalesEndpoints.ReportOpenOrdersPath}?pageSize=1")).GetProperty("nextCursor").GetString()!;
        }

        using (var clientOfB = run.Client(readerOfB, CompanyB))
        {
            using var foreign = await clientOfB.GetAsync($"{SalesEndpoints.ReportOpenOrdersPath}?pageSize=1&cursor={Uri.EscapeDataString(cursorOfA)}");
            Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        }

        // A user holding another Sales name (the order read) reads no report: 403 on all three, before anything is read.
        var orderReader = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersPriceRead);
        using (var ungranted = run.Client(orderReader, CompanyA))
        {
            foreach (var path in new[] { SalesEndpoints.ReportOpenOrdersPath, SalesEndpoints.ReportLostOpportunitiesPath,
                         SalesEndpoints.ReportPriceVariancePath })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await ungranted.GetAsync(path)).StatusCode);
            }
        }

        // A reader of A whose request names company B is not let into B.
        using (var crossing = run.Client(readerOfA, CompanyB))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await crossing.GetAsync(SalesEndpoints.ReportOpenOrdersPath)).StatusCode);
        }

        _ = orderOfA;
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static string Key(JsonElement group) =>
        $"{group.GetProperty("customer").GetProperty("companyAccountId").GetGuid():D}|{group.GetProperty("productId").GetGuid():D}|{group.GetProperty("unitId").GetGuid():D}";

    private static LogisticsQuantity Kg(decimal value) => LogisticsQuantity.Of(value, LogisticsQuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    private static MoneyProfileSnapshot Money(Guid company, DateOnly? on = null) =>
        new(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", on ?? Day, true, From, null, 2);

    private static UnitProfileSnapshot Unit(Guid company) => new(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);

    private static ProductSnapshot ProductOf(Guid company, DateOnly? on = null)
    {
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        return new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", on ?? Day, binding, binding, binding);
    }

    /// <summary>A draft of one line per given kg quantity for the Cari customer, from SYNTHETIC Product/Settings snapshots.</summary>
    private static SalesOrderDraft Draft(Guid company, Customer customer, string documentNo, DateOnly businessDate,
        IReadOnlyList<string> quantities)
    {
        var header = new SalesDraftHeader(customer.PartyId, customer.AccountId, Currency, businessDate, businessDate.AddDays(10));
        // Q9: the current service path creates every order with customer acceptance required (not the pre-Q9 default).
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, Money(company, businessDate), T0,
            customerAcceptanceRequired: true);
        foreach (var quantity in quantities)
        {
            draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), ProductOf(company, businessDate), Unit(company), quantity,
                decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), businessDate.AddDays(3)), T0);
        }

        return draft;
    }

    private sealed record Customer(Guid PartyId, Guid AccountId, string Code, string LegalName);

    /// <summary>(audit events, sales draft and quote attempts, logistics attempts, the sum of every instruction, draft and quote version).</summary>
    private sealed record Effects(int AuditEvents, int SalesAttempts, int QuoteAttempts, int LogisticsAttempts, long InstructionVersions,
        long DraftVersions, long QuoteVersions);

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ISalesQuoteStore Quotes => store;

        public ISalesReportReadStore Reports => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class LogisticsScope(LogisticsDbContext dbContext, LogisticsStore store) : IAsyncDisposable
    {
        public ILogisticsInstructionStore Store => store;

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
        public string Correlation { get; } = $"sales-api-41-{Guid.NewGuid():N}";

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

        /// <summary>A customer of <paramref name="company"/> created through the real Cari route (party + its company account).</summary>
        public async Task<Customer> CustomerAsync(Guid company, string legalName)
        {
            using var client = Factory.CreateClientFor(TestUsers.GroupAdmin, company);
            using var response = await client.PostAsJsonAsync("/api/v1/counterparties",
                new CreateCounterparty(PartyKind.LegalEntity, legalName, null, PartyRole.Customer));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<JsonElement>();
            var account = Assert.Single(created.GetProperty("companyAccounts").EnumerateArray());
            Assert.Equal(company, account.GetProperty("companyId").GetGuid());
            return new Customer(created.GetProperty("id").GetGuid(), account.GetProperty("id").GetGuid(),
                created.GetProperty("code").GetString()!, legalName);
        }

        /// <summary>An approved order the customer accepted (Q9) through the real Sales store.</summary>
        public Task<SalesOrderDraft> AcceptedDraftAsync(Guid company, Customer customer, string keyPrefix, params string[] quantities) =>
            AcceptedDraftAsync(company, customer, keyPrefix, Day, quantities);

        public async Task<SalesOrderDraft> AcceptedDraftAsync(Guid company, Customer customer, string keyPrefix, DateOnly businessDate,
            params string[] quantities)
        {
            var approved = await ApprovedDraftAsync(company, customer, keyPrefix, businessDate, quantities);
            return await AcceptAsync(company, approved, $"{keyPrefix}-accept");
        }

        public async Task<SalesOrderDraft> AcceptAsync(Guid company, SalesOrderDraft draft, string key)
        {
            await using var scope = SalesStore(company);
            return (await scope.Store.UpdateDraftStateAsync(SalesEnvelope(key), Hash(key), SalesDraftCommands.RecordCustomerAcceptanceV1, draft.Id,
                (d, now) => d.RecordCustomerAcceptance(draft.Version, Actor, now), AcceptedAudit))!.Draft;
        }

        /// <summary>
        /// Rev.0 of <paramref name="accepted"/> kg approved and accepted, then a quantity revision to <paramref name="revised"/> kg
        /// (Rev.1) approved inside — awaiting the customer's acceptance — through the real Sales store.
        /// </summary>
        public async Task<SalesOrderDraft> RevisedAwaitingAcceptanceAsync(Guid company, Customer customer, string keyPrefix, string accepted,
            string revised)
        {
            var rev0 = await AcceptedDraftAsync(company, customer, keyPrefix, accepted);
            await using var scope = SalesStore(company);
            var line = Assert.Single(rev0.Lines);
            var value = decimal.Parse(revised, System.Globalization.CultureInfo.InvariantCulture);
            var revisedDraft = (await scope.Store.ReviseDraftQuantityAsync(SalesEnvelope($"{keyPrefix}-revise"), Hash($"{keyPrefix}-revise"),
                SalesDraftCommands.ReviseQuantityV1, rev0.Id,
                (d, now) => d.ReviseQuantities(rev0.Version, [new LineQuantityInput(line.Id, revised, value)], Actor, "SENTETIK revizyon", now),
                RevisedAudit, (_, _) => Task.CompletedTask))!.Draft;
            var submitted = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit-1"), Hash($"{keyPrefix}-submit-1"),
                SalesDraftCommands.SubmitV1, rev0.Id, (d, now) => d.Submit(revisedDraft.Version, Actor, now), Submitted);
            return (await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve-1", actor: "SENTETIK-approver"),
                Hash($"{keyPrefix}-approve-1"), SalesDraftCommands.ApproveV1, rev0.Id,
                (d, now) => d.Approve(submitted!.Draft.Version, "SENTETIK-approver", now), ApprovedAudit))!.Draft;
        }

        /// <summary>A stored, never submitted draft of the company (not an open order).</summary>
        public Task<SalesOrderDraft> DraftAsync(Guid company, Customer customer, string keyPrefix, params string[] quantities) =>
            DraftAsync(company, customer, keyPrefix, Day, quantities);

        public async Task<SalesOrderDraft> DraftAsync(Guid company, Customer customer, string keyPrefix, DateOnly businessDate,
            params string[] quantities)
        {
            await using var scope = SalesStore(company);
            return (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, customer, documentNo, businessDate, quantities))).Draft;
        }

        /// <summary>A stored draft of the company for the customer, submitted and approved through the real Sales store.</summary>
        public Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, Customer customer, string keyPrefix, params string[] quantities) =>
            ApprovedDraftAsync(company, customer, keyPrefix, Day, quantities);

        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, Customer customer, string keyPrefix, DateOnly businessDate,
            params string[] quantities)
        {
            var draft = await DraftAsync(company, customer, keyPrefix, businessDate, quantities);
            await using var scope = SalesStore(company);
            var submitted = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"),
                SalesDraftCommands.SubmitV1, draft.Id, (d, now) => d.Submit(draft.Version, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"),
                Hash($"{keyPrefix}-approve"), SalesDraftCommands.ApproveV1, draft.Id,
                (d, now) => d.Approve(submitted!.Draft.Version, "SENTETIK-approver", now), ApprovedAudit);
            return approved!.Draft;
        }

        /// <summary>A quote of one priced kg line stored and lost with a SENTETIK SAT-D25 list reason through the real Sales store.</summary>
        public async Task<SalesQuote> LostQuoteAsync(Guid company, Customer customer, string keyPrefix, string quantity, string price,
            string? discount, string? explanation)
        {
            await using var scope = SalesStore(company);
            var stored = (await scope.Quotes.StoreQuoteAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => SalesQuote.Create(Guid.NewGuid(), company, documentNo,
                    new SalesQuoteHeader(customer.PartyId, customer.AccountId, Currency, Day), Money(company), [],
                    new SalesQuoteTerms(Day.AddDays(10)),
                    [new SalesQuoteLineInput(Guid.NewGuid(), ProductOf(company), Unit(company), quantity,
                        decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), price, KgUnit, discount)],
                    Actor, T0))).Quote;
            var lost = await scope.Quotes.UpdateQuoteAsync(SalesEnvelope($"{keyPrefix}-lose"), Hash($"{keyPrefix}-lose"),
                SalesQuoteCommands.LoseV2, stored.Id,
                (quote, now) => quote.RecordLoss(stored.Version, Actor,
                    new SalesQuoteLossReason(PriceReason, "SENTETIK-FIYAT", "Sentetik fiyat", LossListProfile, 1), false, explanation, now),
                Lost);
            return lost!.Quote;
        }

        /// <summary>An instruction for one order line, issued, loaded in full and shipped <paramref name="shipped"/> kg.</summary>
        public async Task ShipAsync(Guid company, SalesOrderDraft draft, SalesOrderDraftLine orderLine, string keyPrefix, decimal shipped,
            decimal? planned = null)
        {
            var plannedQuantity = planned ?? orderLine.Quantity.Value;
            await using var scope = LogisticsStore(company);
            var stored = (await scope.Store.StoreInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-create"), Hash($"{keyPrefix}-l-create"), Day, () =>
            {
                var instruction = LoadingInstruction.Create(Guid.NewGuid(), company, draft.Header.PartyId, draft.Header.CompanyAccountId, T0);
                instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(draft.Id, orderLine.Id, draft.Revision),
                    orderLine.Product.ProductId, KgUnit, null, Kg(plannedQuantity), T0);
                return instruction;
            })).Instruction.Instruction;
            var lineId = Assert.Single(stored.Lines).Id;
            var id = stored.Id;
            var issued = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-issue"), Hash($"{keyPrefix}-l-issue"),
                LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(stored.Version, now), Issued))!.Instruction.Instruction;
            var loaded = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-load"), Hash($"{keyPrefix}-l-load"),
                LogisticsCommands.RecordLoadV1, id, (instruction, now) => instruction.RecordLoad(issued.Version, Guid.NewGuid(), lineId, Kg(plannedQuantity),
                    MeasurementKind.Measured, now), Loaded))!.Instruction.Instruction;
            await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-ship"), Hash($"{keyPrefix}-l-ship"),
                LogisticsCommands.RecordShipmentV1, id, (instruction, now) => instruction.MarkShipped(loaded.Version, Guid.NewGuid(), lineId,
                    Kg(shipped), now), Shipped);
        }

        public async Task<JsonElement> ReadAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync() => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[QuoteAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [logistics].[LoadingAttempts]"),
            await ScalarAsync<long>("SELECT ISNULL(SUM([Version]), 0) FROM [logistics].[LoadingInstructions]"),
            await ScalarAsync<long>("SELECT ISNULL(SUM([Version]), 0) FROM [sales].[Drafts]"),
            await ScalarAsync<long>("SELECT ISNULL(SUM([Version]), 0) FROM [sales].[Quotes]"));

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public LogisticsWriteEnvelope LogisticsEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-logistics-permission", ["SENTETIK-LOGISTICS"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales report user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"API-41-{roleId:N}", "Synthetic Sales report role", true);
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
