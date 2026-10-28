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
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LogisticsQuantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals>;
using LogisticsQuantityUnit = GoymenERP.BuildingBlocks.Quantities.QuantityUnit;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// API-30 / API-31 / MOD-06 (plan §3.3, §5; SAT-D7, DEC-SAL-006, AT-025, AT-055) against a REAL SQL Server and the REAL host
/// composition: the Sales dispatch list shows every dispatch note of the company (draft, approved, cancelled — newest first, paged
/// by the owner's company-bound cursor, narrowed by an exact state) with the Sales order composed from the Sales store and the
/// customer from Cari; the invoice candidates are only the lines of APPROVED, not cancelled notes, each shipment once (AT-055: a
/// cancelled note's released shipment that went onto a new approved note is one share), grouped by the decided key parts. Both
/// reads write nothing in any module; each route measures its own name; another company's notes never appear and its cursor is
/// refused.
/// <para>
/// Data is set up through the owners only: the customer through the real Cari route, the order through the real Sales store from
/// ISOLATED SYNTHETIC owner snapshots (the API-10 precedent), the instruction, shipments and dispatch notes through the real
/// Logistics store. The unit is the approved GDC kilogram (Ö1/Ö2). SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed class SalesDispatchSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 10, 20);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d21");
    private static readonly Guid MoneyProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d22");
    private static readonly Guid KgProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d23");
    private static readonly Guid Product = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d24");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit ApprovedAudit = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly LogisticsMutationAudit Issued = new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit Loaded = new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");
    private static readonly LogisticsMutationAudit Shipped = new("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi");
    private static readonly LogisticsMutationAudit Drafted =
        new("logistics.loading-instruction.dispatch-note-drafted.v1", "CreateDispatchNote", "SENTETIK irsaliye taslağı");
    private static readonly LogisticsMutationAudit NoteApproved =
        new("logistics.loading-instruction.dispatch-note-approved.v1", "ApproveDispatchNote", "SENTETIK irsaliye onaylandı");
    private static readonly LogisticsMutationAudit NoteCancelled =
        new("logistics.loading-instruction.dispatch-note-cancelled.v1", "CancelDispatchNote", "SENTETIK irsaliye iptal edildi");

    [SqlServerFact]
    public async Task The_dispatch_list_and_the_invoice_candidates_compose_the_logistics_notes_with_the_sales_order_and_write_nothing_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var customer = await run.CustomerAsync(CompanyA, "SENTETIK Sevk Müşterisi A.Ş.");
        var first = await run.ApprovedDraftAsync(CompanyA, customer, "d1", "6", "3");
        var second = await run.ApprovedDraftAsync(CompanyA, customer, "d2", "2");
        var (firstLine, secondLine) = (first.Lines.Single(line => line.LineNo == 1), first.Lines.Single(line => line.LineNo == 2));

        // N1: a DRAFT note on the first order's first line (4 kg shipped) — listed, never a candidate.
        var i1 = await run.ShippedAsync(CompanyA, first, firstLine, "i1", 4m);
        var n1 = await run.DraftNoteAsync(CompanyA, i1, "n1");
        // N2: an APPROVED note on the first order's second line (3 kg) — listed and a candidate.
        var i2 = await run.ShippedAsync(CompanyA, first, secondLine, "i2", 3m);
        var n2 = await run.DraftNoteAsync(CompanyA, i2, "n2");
        await run.ApproveNoteAsync(CompanyA, i2, n2, "n2");
        // N3 approved then CANCELLED (its line released), N4 the correction carrying the SAME shipment, approved (AT-055).
        var i3 = await run.ShippedAsync(CompanyA, second, Assert.Single(second.Lines), "i3", 1.5m);
        var n3 = await run.DraftNoteAsync(CompanyA, i3, "n3");
        await run.ApproveNoteAsync(CompanyA, i3, n3, "n3");
        await run.CancelNoteAsync(CompanyA, i3, n3, "n3");
        var n4 = await run.DraftNoteAsync(CompanyA, i3, "n4");
        await run.ApproveNoteAsync(CompanyA, i3, n4, "n4");

        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using (var client = run.Client(reader, CompanyA))
        {
            // Every note of the company, newest first, with its state, system number and the composed Sales order line.
            var page = await run.ReadAsync(client, SalesEndpoints.DispatchesPath);
            var items = page.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal([n4.NoteId, n3.NoteId, n2.NoteId, n1.NoteId], items.Select(item => item.GetProperty("id").GetGuid()));
            Assert.Equal(["Approved", "Cancelled", "Approved", "Draft"], items.Select(item => item.GetProperty("state").GetString()));
            Assert.Equal(["IRS-000003", "IRS-000002", "IRS-000001", null], items.Select(item => item.GetProperty("systemNumber").GetString()));
            Assert.All(items, item =>
            {
                Assert.Equal(CompanyA, item.GetProperty("companyId").GetGuid());
                Assert.Equal("NoSource", item.GetProperty("invoicing").GetString());
                Assert.Equal((customer.PartyId, customer.AccountId, customer.Code, customer.LegalName),
                    (item.GetProperty("customer").GetProperty("partyId").GetGuid(), item.GetProperty("customer").GetProperty("companyAccountId").GetGuid(),
                        item.GetProperty("customer").GetProperty("code").GetString(), item.GetProperty("customer").GetProperty("legalName").GetString()));
            });
            var draftLine = Assert.Single(items[3].GetProperty("lines").EnumerateArray());
            Assert.Equal((first.Id, first.DocumentNo, firstLine.Id, 1, i1.ShipmentId, "4", KgUnit, "GDC-001"),
                (draftLine.GetProperty("salesOrderId").GetGuid(), draftLine.GetProperty("salesOrderDocumentNo").GetString(),
                    draftLine.GetProperty("salesOrderLineId").GetGuid(), draftLine.GetProperty("salesOrderLineNo").GetInt32(),
                    draftLine.GetProperty("shipmentId").GetGuid(), draftLine.GetProperty("quantity").GetString(),
                    draftLine.GetProperty("unitId").GetGuid(), draftLine.GetProperty("productCode").GetString()));
            Assert.Equal(JsonValueKind.Null, items[3].GetProperty("approvedAtUtc").ValueKind);
            Assert.NotEqual(JsonValueKind.Null, items[1].GetProperty("cancelledAtUtc").ValueKind);
            // The cancelled note keeps its (released) line as history.
            Assert.Equal(i3.ShipmentId, Assert.Single(items[1].GetProperty("lines").EnumerateArray()).GetProperty("shipmentId").GetGuid());

            // Paged by the owner's company-bound cursor: two pages of two, nothing skipped or repeated.
            var head = await run.ReadAsync(client, $"{SalesEndpoints.DispatchesPath}?pageSize=2");
            var cursor = head.GetProperty("nextCursor").GetString()!;
            var tail = await run.ReadAsync(client, $"{SalesEndpoints.DispatchesPath}?pageSize=2&cursor={Uri.EscapeDataString(cursor)}");
            Assert.Equal([n4.NoteId, n3.NoteId, n2.NoteId, n1.NoteId],
                head.GetProperty("items").EnumerateArray().Concat(tail.GetProperty("items").EnumerateArray()).Select(item => item.GetProperty("id").GetGuid()));
            Assert.Equal(JsonValueKind.Null, tail.GetProperty("nextCursor").ValueKind);

            // The exact state narrows on the server.
            var approved = await run.ReadAsync(client, $"{SalesEndpoints.DispatchesPath}?state=Approved");
            Assert.Equal([n4.NoteId, n2.NoteId], approved.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{SalesEndpoints.DispatchesPath}?state=approved")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{SalesEndpoints.DispatchesPath}?pageSize=101")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{SalesEndpoints.DispatchesPath}?unknown=1")).StatusCode);

            // The order read does not open the invoice candidates (their own name).
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(SalesEndpoints.InvoiceCandidatesPath)).StatusCode);
        }

        var invoicer = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SalesPermissionManifestV1.InvoicesRead);
        using (var client = run.Client(invoicer, CompanyA))
        {
            var before = await run.EffectsAsync();
            var candidates = await run.ReadAsync(client, SalesEndpoints.InvoiceCandidatesPath);
            Assert.Equal(before, await run.EffectsAsync());

            Assert.False(candidates.GetProperty("truncated").GetBoolean());
            Assert.Equal("NoSource", candidates.GetProperty("invoicedState").GetString());
            // One group: one company, one customer, one currency, no payment terms on either order.
            var group = Assert.Single(candidates.GetProperty("groups").EnumerateArray());
            var key = group.GetProperty("key");
            Assert.Equal((CompanyA, customer.PartyId, customer.AccountId, Currency, JsonValueKind.Null),
                (key.GetProperty("companyId").GetGuid(), key.GetProperty("partyId").GetGuid(), key.GetProperty("companyAccountId").GetGuid(),
                    key.GetProperty("currencyId").GetGuid(), key.GetProperty("paymentTerms").ValueKind));
            Assert.Equal(["PaymentTerms", "Tax", "ExchangeRate", "EDocument"],
                group.GetProperty("unresolvedKeyParts").EnumerateArray().Select(part => part.GetString()));
            Assert.False(group.GetProperty("complete").GetBoolean());
            Assert.Equal(2, group.GetProperty("dispatchNoteCount").GetInt32());
            // Only the approved, not released lines, each shipment once: N2's 3 kg and N4's 1.5 kg (N3's released line is not a share).
            var shares = group.GetProperty("shares").EnumerateArray().ToArray();
            Assert.Equal([(n2.NoteId, i2.ShipmentId, first.DocumentNo, 2, "3"), (n4.NoteId, i3.ShipmentId, second.DocumentNo, 1, "1.5")],
                shares.Select(share => (share.GetProperty("dispatchNoteId").GetGuid(), share.GetProperty("shipmentId").GetGuid(),
                    share.GetProperty("salesOrderDocumentNo").GetString(), share.GetProperty("salesOrderLineNo").GetInt32(),
                    share.GetProperty("quantity").GetString())));
            Assert.Equal(["IRS-000001", "IRS-000003"], shares.Select(share => share.GetProperty("dispatchSystemNumber").GetString()));
            var total = Assert.Single(group.GetProperty("quantityTotals").EnumerateArray());
            Assert.Equal((KgUnit, "kg", "4.5"), (total.GetProperty("unitId").GetGuid(), total.GetProperty("unitCode").GetString(),
                total.GetProperty("quantity").GetString()));
            // No money of any kind on the wire.
            var json = candidates.GetRawText();
            Assert.DoesNotContain("price", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("amount", json, StringComparison.OrdinalIgnoreCase);

            // The customer filter narrows on the owner; an unknown customer has no share.
            var narrowed = await run.ReadAsync(client, $"{SalesEndpoints.InvoiceCandidatesPath}?companyAccountId={customer.AccountId:D}");
            Assert.Single(narrowed.GetProperty("groups").EnumerateArray());
            var unknown = await run.ReadAsync(client, $"{SalesEndpoints.InvoiceCandidatesPath}?companyAccountId={Guid.NewGuid():D}");
            Assert.Empty(unknown.GetProperty("groups").EnumerateArray());
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.GetAsync($"{SalesEndpoints.InvoiceCandidatesPath}?companyAccountId={Guid.Empty:D}")).StatusCode);

            // The invoice read does not open the dispatch list (the order read's).
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(SalesEndpoints.DispatchesPath)).StatusCode);

            // Both reads wrote nothing: the Logistics instruction and the Sales orders are as they were.
            await run.ReadAsync(client, SalesEndpoints.InvoiceCandidatesPath);
            Assert.Equal(before, await run.EffectsAsync());
        }
    }

    [SqlServerFact]
    public async Task Another_companys_notes_never_appear_its_cursor_is_refused_and_an_ungranted_user_reads_nothing_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var customerOfA = await run.CustomerAsync(CompanyA, "SENTETIK A Müşterisi A.Ş.");
        var customerOfB = await run.CustomerAsync(CompanyB, "SENTETIK B Müşterisi A.Ş.");
        var orderOfA = await run.ApprovedDraftAsync(CompanyA, customerOfA, "a", "5");
        var orderOfB = await run.ApprovedDraftAsync(CompanyB, customerOfB, "b", "7");
        var shippedOfA = await run.ShippedAsync(CompanyA, orderOfA, Assert.Single(orderOfA.Lines), "a", 5m);
        var noteOfA = await run.DraftNoteAsync(CompanyA, shippedOfA, "a");
        await run.ApproveNoteAsync(CompanyA, shippedOfA, noteOfA, "a");
        var shippedOfB = await run.ShippedAsync(CompanyB, orderOfB, Assert.Single(orderOfB.Lines), "b", 7m);
        var noteOfB = await run.DraftNoteAsync(CompanyB, shippedOfB, "b");
        await run.ApproveNoteAsync(CompanyB, shippedOfB, noteOfB, "b");

        // The owner ports under each company: each sees only its own note and its own share.
        await using (var a = run.LogisticsContext(CompanyA))
        {
            var ports = new LogisticsSalesDispatchReadPorts(a.Context, a.Company);
            var notes = await ports.ListSalesDispatchNotesAsync(new SalesDispatchNotePageQueryV1(10, null, null));
            Assert.Equal((SalesDispatchReadOutcomeV1.Found, CompanyA, noteOfA.NoteId),
                (notes.Outcome, notes.CompanyId, Assert.Single(notes.Notes).DispatchNoteId));
            var shares = await ports.ReadInvoiceableShipmentsAsync(new SalesInvoiceableShipmentQueryV1(null, 10));
            Assert.Equal((SalesDispatchReadOutcomeV1.Found, shippedOfA.ShipmentId, 5m, false),
                (shares.Outcome, Assert.Single(shares.Shares).ShipmentId, shares.Shares[0].Quantity, shares.HasMore));
            // A filter naming B's customer account finds nothing in A.
            Assert.Empty((await ports.ReadInvoiceableShipmentsAsync(new SalesInvoiceableShipmentQueryV1(customerOfB.AccountId, 10))).Shares);
        }

        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.InvoicesRead);
        using (var clientOfB = run.Client(readerOfB, CompanyB))
        {
            var list = await run.ReadAsync(clientOfB, SalesEndpoints.DispatchesPath);
            Assert.Equal(noteOfB.NoteId, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
            var candidates = await run.ReadAsync(clientOfB, SalesEndpoints.InvoiceCandidatesPath);
            var share = Assert.Single(Assert.Single(candidates.GetProperty("groups").EnumerateArray()).GetProperty("shares").EnumerateArray());
            Assert.Equal((noteOfB.NoteId, orderOfB.Id), (share.GetProperty("dispatchNoteId").GetGuid(), share.GetProperty("salesOrderId").GetGuid()));

            // Company A's cursor is refused in B (400 invalid_cursor), never read as a position of B's list.
            var cursorOfA = LogisticsStore.ListCursor.Encode(CompanyA, DateTimeOffset.UtcNow, Guid.NewGuid());
            using var foreign = await clientOfB.GetAsync($"{SalesEndpoints.DispatchesPath}?cursor={Uri.EscapeDataString(cursorOfA)}");
            Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.InvalidCursor, (await foreign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        // Company A's read-only user holds no Sales name (none is seeded): 403 on both, before anything is read.
        using (var ungranted = run.Client(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await ungranted.GetAsync(SalesEndpoints.DispatchesPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ungranted.GetAsync(SalesEndpoints.InvoiceCandidatesPath)).StatusCode);
        }

        // A reader of A whose request names company B is not let into B.
        var readerOfA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using (var crossing = run.Client(readerOfA, CompanyB))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await crossing.GetAsync(SalesEndpoints.DispatchesPath)).StatusCode);
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static LogisticsQuantity Kg(decimal value) => LogisticsQuantity.Of(value, LogisticsQuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A draft of one line per given kg quantity for the Cari customer, from SYNTHETIC Product/Settings snapshots.</summary>
    private static SalesOrderDraft Draft(Guid company, Customer customer, string documentNo, IReadOnlyList<string> quantities)
    {
        var header = new SalesDraftHeader(customer.PartyId, customer.AccountId, Currency, Day, Day.AddDays(10));
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        foreach (var quantity in quantities)
        {
            draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
                decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3)), T0);
        }

        return draft;
    }

    /// <summary>The Cari customer as the real Cari route created it.</summary>
    private sealed record Customer(Guid PartyId, Guid AccountId, string Code, string LegalName);

    /// <summary>A shipped instruction: its id, its current version and the shipment.</summary>
    private sealed record ShippedInstruction(Guid InstructionId, Guid ShipmentId)
    {
        public long Version { get; set; }
    }

    /// <summary>A dispatch note of an instruction.</summary>
    private sealed record Note(Guid NoteId);

    /// <summary>(audit events, sales attempts, logistics attempts, the sum of every instruction and draft version) on SQL Server.</summary>
    private sealed record Effects(int AuditEvents, int SalesAttempts, int LogisticsAttempts, long InstructionVersions, long DraftVersions);

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

    private sealed class LogisticsRead(LogisticsDbContext context, CompanyContext company) : IAsyncDisposable
    {
        public LogisticsDbContext Context => context;

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
        public string Correlation { get; } = $"sales-api-30-{Guid.NewGuid():N}";

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

        /// <summary>A stored draft of the company for the customer, submitted and approved through the real Sales store.</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, Customer customer, string keyPrefix, params string[] quantities)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, customer, documentNo, quantities))).Draft;
            var submitted = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"),
                SalesDraftCommands.SubmitV1, draft.Id, (d, now) => d.Submit(draft.Version, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"),
                Hash($"{keyPrefix}-approve"), SalesDraftCommands.ApproveV1, draft.Id,
                (d, now) => d.Approve(submitted!.Draft.Version, "SENTETIK-approver", now), ApprovedAudit);
            return approved!.Draft;
        }

        /// <summary>
        /// An instruction of the company for one order line (planned at the line's quantity), created, issued, loaded in full and
        /// shipped <paramref name="shipped"/> kg through the real Logistics store.
        /// </summary>
        public async Task<ShippedInstruction> ShippedAsync(Guid company, SalesOrderDraft draft, SalesOrderDraftLine orderLine, string keyPrefix,
            decimal shipped)
        {
            var planned = orderLine.Quantity.Value;
            var shipmentId = Guid.NewGuid();
            await using var scope = LogisticsStore(company);
            var stored = (await scope.Store.StoreInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-create"), Hash($"{keyPrefix}-l-create"), Day, () =>
            {
                var instruction = LoadingInstruction.Create(Guid.NewGuid(), company, draft.Header.PartyId, draft.Header.CompanyAccountId, T0);
                instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(draft.Id, orderLine.Id, draft.Revision),
                    orderLine.Product.ProductId, KgUnit, null, Kg(planned), T0);
                return instruction;
            })).Instruction.Instruction;
            var lineId = Assert.Single(stored.Lines).Id;
            var id = stored.Id;
            var issued = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-issue"), Hash($"{keyPrefix}-l-issue"),
                LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(stored.Version, now), Issued))!.Instruction.Instruction;
            var loaded = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-load"), Hash($"{keyPrefix}-l-load"),
                LogisticsCommands.RecordLoadV1, id, (instruction, now) => instruction.RecordLoad(issued.Version, Guid.NewGuid(), lineId, Kg(planned),
                    MeasurementKind.Measured, now), Loaded))!.Instruction.Instruction;
            var afterShipment = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-ship"), Hash($"{keyPrefix}-l-ship"),
                LogisticsCommands.RecordShipmentV1, id, (instruction, now) => instruction.MarkShipped(loaded.Version, shipmentId, lineId,
                    Kg(shipped), now), Shipped))!.Instruction.Instruction;
            return new ShippedInstruction(id, shipmentId) { Version = afterShipment.Version };
        }

        /// <summary>A draft dispatch note of the instruction's shipment through the real Logistics store.</summary>
        public async Task<Note> DraftNoteAsync(Guid company, ShippedInstruction shipped, string key)
        {
            var noteId = Guid.NewGuid();
            await using var scope = LogisticsStore(company);
            var result = await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{key}-note"), Hash($"{key}-note-{noteId:N}"),
                LogisticsCommands.CreateDispatchNoteV1, shipped.InstructionId,
                (instruction, now) => instruction.CreateDispatchNoteDraft(shipped.Version, noteId, [shipped.ShipmentId], Actor, now), Drafted);
            shipped.Version = result!.Instruction.Instruction.Version;
            return new Note(noteId);
        }

        /// <summary>The approval of the note (the company's next system number in the same save) through the real Logistics store.</summary>
        public async Task ApproveNoteAsync(Guid company, ShippedInstruction shipped, Note note, string key)
        {
            await using var scope = LogisticsStore(company);
            var result = await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{key}-approve"), Hash($"{key}-approve-{note.NoteId:N}"),
                LogisticsCommands.ApproveDispatchNoteV1, shipped.InstructionId,
                (instruction, now) => instruction.ApproveDispatchNote(shipped.Version, note.NoteId, Actor, now), NoteApproved);
            shipped.Version = result!.Instruction.Instruction.Version;
        }

        /// <summary>The cancellation of the note (its lines released) through the real Logistics store.</summary>
        public async Task CancelNoteAsync(Guid company, ShippedInstruction shipped, Note note, string key)
        {
            await using var scope = LogisticsStore(company);
            var result = await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{key}-cancel"), Hash($"{key}-cancel-{note.NoteId:N}"),
                LogisticsCommands.CancelDispatchNoteV1, shipped.InstructionId,
                (instruction, now) => instruction.CancelDispatchNote(shipped.Version, note.NoteId, Actor, "SENTETIK yanlış araç", now), NoteCancelled);
            shipped.Version = result!.Instruction.Instruction.Version;
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
            await ScalarAsync<int>("SELECT COUNT(*) FROM [logistics].[LoadingAttempts]"),
            await ScalarAsync<long>("SELECT ISNULL(SUM([Version]), 0) FROM [logistics].[LoadingInstructions]"),
            await ScalarAsync<long>("SELECT ISNULL(SUM([Version]), 0) FROM [sales].[Drafts]"));

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

        public LogisticsRead LogisticsContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new LogisticsRead(new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .Options, companyContext), companyContext);
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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales dispatch user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"API-30-{roleId:N}", "Synthetic Sales dispatch role", true);
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
