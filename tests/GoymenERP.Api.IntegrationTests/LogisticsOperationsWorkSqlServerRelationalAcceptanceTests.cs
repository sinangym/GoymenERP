using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LogisticsQuantity = GoymenERP.Modules.Logistics.Domain.Quantities.Quantity;
using LogisticsQuantityUnit = GoymenERP.Modules.Logistics.Domain.Quantities.QuantityUnit;
using PurchaseQuantity = GoymenERP.Modules.Purchasing.Domain.Quantities.Quantity;
using PurchaseQuantityUnit = GoymenERP.Modules.Purchasing.Domain.Quantities.QuantityUnit;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AT-025 ("Satış ve satın alma belgeleri operasyon listelerinde karışmaz") against a REAL SQL Server and the REAL host
/// composition: the operations work list's SALES section answers only the context company's approved sales order lines (with
/// Logistics's own recorded totals) and its PURCHASE section only the context company's approved purchase order lines still
/// waiting for goods receipt; a row of one section never appears in the other, a draft / not-yet-approved / fully received
/// line appears in neither, another company's rows never appear, each section opens only with its own grant, neither wire
/// body carries a price member, and reading writes nothing in any module.
/// <para>
/// The owners' records are stored through their REAL stores from SYNTHETIC owner snapshots (the AT-024 precedent): the Sales
/// draft (created, submitted, approved), the Logistics instruction (created, issued, loaded, shipped), the purchase orders
/// (created, approved; a fully received line is set by SQL, the acceptance being PUR-4b's own evidence). The routes are
/// called with fresh synthetic grants. The customers and suppliers are synthetic, so the Cari owner answers them with the
/// labelled "ref. …" reference (LOG-4b). SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed class LogisticsOperationsWorkSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d01");
    private static readonly Guid MoneyProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d02");
    private static readonly Guid KgProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d03");
    private static readonly Guid Product = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d04");
    private const string SalesPath = LogisticsOperationsWorkApiV1.SalesPath;
    private const string PurchasePath = LogisticsOperationsWorkApiV1.PurchasePath;
    private static readonly string[] PriceWords = ["price", "amount", "discount", "currency", "payment", "unitprice", "total"];

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly LogisticsMutationAudit Issued = new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit Loaded = new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");
    private static readonly LogisticsMutationAudit Shipped = new("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi");
    private static readonly PurchasingMutationAudit PurchaseApproved = new("purchasing.purchase-order.approved.v1", "Approve", "SENTETIK onaylandı");

    [SqlServerFact]
    public async Task Each_section_lists_only_its_own_owners_open_lines_of_the_context_company_and_no_row_crosses_into_the_other_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        // Company A: one approved sales order (6 kg; loaded 6, shipped 4 by Logistics), one draft only submitted (never listed);
        // one approved purchase order waiting for 100 kg, one fully received, one still Draft (neither listed).
        var salesA = await run.ApprovedDraftAsync(CompanyA, "a", "6");
        var pendingA = await run.SubmittedDraftAsync(CompanyA, "a-pending", "3");
        await run.ShippedInstructionAsync(CompanyA, salesA, "a", loaded: 6m, shipped: 4m);
        var openA = await run.PurchaseOrderAsync(CompanyA, "a-open", 100m, approve: true);
        var receivedA = await run.PurchaseOrderAsync(CompanyA, "a-received", 50m, approve: true);
        await run.ExecuteAsync("UPDATE [purchasing].[PurchaseOrderLines] SET [Accepted] = [Ordered] WHERE [OrderId] = @id", ("@id", receivedA.OrderId));
        var draftA = await run.PurchaseOrderAsync(CompanyA, "a-draft", 30m, approve: false);
        // Company B: its own approved sales order and approved purchase order — never company A's work.
        var salesB = await run.ApprovedDraftAsync(CompanyB, "b", "9");
        var openB = await run.PurchaseOrderAsync(CompanyB, "b-open", 70m, approve: true);

        var subjectA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.OperationsSalesWorkRead,
            LogisticsPermissionManifestV1.OperationsPurchaseWorkRead, ErpPermissions.CounterpartyPartiesRead);
        using var clientA = run.Client(subjectA, CompanyA);
        var before = await run.EffectsAsync();

        var sales = await run.ReadAsync(clientA, SalesPath);
        Assert.Equal(LogisticsOperationsWorkSectionsV1.Sales, sales.GetProperty("section").GetString());
        var salesRow = Assert.Single(sales.GetProperty("items").EnumerateArray());
        var orderLine = Assert.Single(salesA.Lines);
        Assert.Equal((salesA.Id.ToString("D"), orderLine.Id.ToString("D"), "6", "6", "6", "4", "0"),
            (salesRow.GetProperty("orderId").GetString(), salesRow.GetProperty("lineId").GetString(), salesRow.GetProperty("ordered").GetString(),
                salesRow.GetProperty("instructed").GetString(), salesRow.GetProperty("loaded").GetString(), salesRow.GetProperty("shipped").GetString(),
                salesRow.GetProperty("delivered").GetString()));
        Assert.StartsWith("ref. …", salesRow.GetProperty("customerName").GetString(), StringComparison.Ordinal);

        var purchase = await run.ReadAsync(clientA, PurchasePath);
        Assert.Equal(LogisticsOperationsWorkSectionsV1.Purchase, purchase.GetProperty("section").GetString());
        var purchaseRow = Assert.Single(purchase.GetProperty("items").EnumerateArray());
        Assert.Equal((openA.OrderId.ToString("D"), openA.LineId.ToString("D"), "100", "0", "100", "General"),
            (purchaseRow.GetProperty("orderId").GetString(), purchaseRow.GetProperty("lineId").GetString(), purchaseRow.GetProperty("committed").GetString(),
                purchaseRow.GetProperty("accepted").GetString(), purchaseRow.GetProperty("remaining").GetString(), purchaseRow.GetProperty("kind").GetString()));
        Assert.StartsWith("ref. …", purchaseRow.GetProperty("supplierName").GetString(), StringComparison.Ordinal);

        // No mixing: no purchase order is in the sales body and no sales order in the purchase body; nothing of company B, of the
        // pending draft, of the received or the Draft purchase order is in either.
        var salesText = sales.GetRawText();
        var purchaseText = purchase.GetRawText();
        foreach (var id in new[] { openA.OrderId, receivedA.OrderId, draftA.OrderId, openB.OrderId, salesB.Id, pendingA.Id })
        {
            Assert.DoesNotContain(id.ToString("D"), salesText, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var id in new[] { salesA.Id, orderLine.Id, pendingA.Id, salesB.Id, receivedA.OrderId, draftA.OrderId, openB.OrderId })
        {
            Assert.DoesNotContain(id.ToString("D"), purchaseText, StringComparison.OrdinalIgnoreCase);
        }

        // Price-free (Sinan §4, #66 5738113623): neither wire row has a price member.
        foreach (var row in new[] { salesRow, purchaseRow })
        {
            Assert.DoesNotContain(row.EnumerateObject(), member => PriceWords.Any(word => member.Name.Contains(word, StringComparison.OrdinalIgnoreCase)));
        }

        // Reading wrote nothing in any module.
        Assert.Equal(before, await run.EffectsAsync());

        // Company B's reader sees only B's work, in each section.
        var subjectB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, LogisticsPermissionManifestV1.OperationsSalesWorkRead,
            LogisticsPermissionManifestV1.OperationsPurchaseWorkRead, ErpPermissions.CounterpartyPartiesRead);
        using var clientB = run.Client(subjectB, CompanyB);
        Assert.Equal(salesB.Id.ToString("D"),
            Assert.Single((await run.ReadAsync(clientB, SalesPath)).GetProperty("items").EnumerateArray()).GetProperty("orderId").GetString());
        Assert.Equal(openB.OrderId.ToString("D"),
            Assert.Single((await run.ReadAsync(clientB, PurchasePath)).GetProperty("items").EnumerateArray()).GetProperty("orderId").GetString());
    }

    [SqlServerFact]
    public async Task Each_section_opens_only_with_its_own_grant_and_a_foreign_cursor_or_query_member_is_refused_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        await run.ApprovedDraftAsync(CompanyA, "a", "6");
        await run.PurchaseOrderAsync(CompanyA, "a-1", 10m, approve: true);
        await run.PurchaseOrderAsync(CompanyA, "a-2", 20m, approve: true);
        await run.PurchaseOrderAsync(CompanyB, "b-1", 30m, approve: true);
        await run.PurchaseOrderAsync(CompanyB, "b-2", 40m, approve: true);

        // No operations grant at all: both sections refuse before anything is read.
        using (var ungranted = run.Client(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await ungranted.GetAsync(SalesPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ungranted.GetAsync(PurchasePath)).StatusCode);
        }

        // The sales name alone opens the sales section only.
        var salesOnly = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.OperationsSalesWorkRead,
            ErpPermissions.CounterpartyPartiesRead);
        using (var client = run.Client(salesOnly, CompanyA))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(SalesPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(PurchasePath)).StatusCode);
            // A query member the contract does not name is refused, never ignored.
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{SalesPath}?section=Purchase")).StatusCode);
        }

        // The purchase name alone opens the purchase section only.
        var purchaseOnly = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, LogisticsPermissionManifestV1.OperationsPurchaseWorkRead,
            ErpPermissions.CounterpartyPartiesRead);
        string? foreignCursor;
        var subjectB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, LogisticsPermissionManifestV1.OperationsPurchaseWorkRead,
            ErpPermissions.CounterpartyPartiesRead);
        using (var clientB = run.Client(subjectB, CompanyB))
        {
            foreignCursor = (await run.ReadAsync(clientB, $"{PurchasePath}?pageSize=1")).GetProperty("nextCursor").GetString();
        }

        Assert.False(string.IsNullOrEmpty(foreignCursor));
        using (var client = run.Client(purchaseOnly, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(SalesPath)).StatusCode);
            // Paging with page size 1 answers each open line exactly once, newest order first.
            var first = await run.ReadAsync(client, $"{PurchasePath}?pageSize=1");
            var next = first.GetProperty("nextCursor").GetString();
            Assert.False(string.IsNullOrEmpty(next));
            var second = await run.ReadAsync(client, $"{PurchasePath}?pageSize=1&cursor={Uri.EscapeDataString(next!)}");
            Assert.Null(second.GetProperty("nextCursor").GetString());
            Assert.Equal(["20", "10"],
                new[] { first, second }.Select(page => Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("remaining").GetString()));
            // Company B's cursor is refused for company A (400 invalid_cursor), never read as A's position.
            using var foreign = await client.GetAsync($"{PurchasePath}?pageSize=1&cursor={Uri.EscapeDataString(foreignCursor!)}");
            Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
            Assert.Contains("\"code\":\"invalid_cursor\"", await foreign.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [SqlServerFact]
    public async Task The_owner_ports_answer_only_their_own_open_lines_under_each_company_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var salesA = await run.ApprovedDraftAsync(CompanyA, "a", "6");
        await run.ShippedInstructionAsync(CompanyA, salesA, "a", loaded: 5m, shipped: 2m);
        // Company B records an instruction naming company A's order line (a foreign binding): B's rows only, never A's totals.
        await run.ShippedInstructionAsync(CompanyB, salesA, "b", loaded: 3m, shipped: 3m);
        var openA = await run.PurchaseOrderAsync(CompanyA, "a-open", 12.5m, approve: true);
        await run.PurchaseOrderAsync(CompanyB, "b-open", 8m, approve: true);

        await using (var a = run.LogisticsContext(CompanyA))
        {
            var total = Assert.Single(await new LogisticsOrderLineProgressReader(a.Context, a.Company).ReadAsync([Assert.Single(salesA.Lines).Id]));
            Assert.Equal((KgUnit, 6m, 5m, 2m, 0m), (total.UnitId, total.Instructed, total.Loaded, total.Shipped, total.Delivered));
        }

        await using (var b = run.LogisticsContext(CompanyB))
        {
            var total = Assert.Single(await new LogisticsOrderLineProgressReader(b.Context, b.Company).ReadAsync([Assert.Single(salesA.Lines).Id]));
            Assert.Equal((3m, 3m), (total.Loaded, total.Shipped));
        }

        await using (var a = run.PurchasingContext(CompanyA))
        {
            var page = await new PurchaseOpenLineReadPort(a.Context, a.Company).ListOpenApprovedLinesAsync(10, null);
            var line = Assert.Single(page.Items);
            Assert.Equal((CompanyA, openA.OrderId, openA.LineId, "12.5", "0", "12.5", "7"),
                (line.CompanyId, line.OrderId, line.LineId, line.CommittedText, line.AcceptedText, line.RemainingText, line.ProductVersion));
            Assert.Null(page.NextCursor);
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static LogisticsQuantity Kg(decimal value) => LogisticsQuantity.Of(value, LogisticsQuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line draft (the given kg quantity) from SYNTHETIC owner snapshots in the approved kg unit: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo, string quantity)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10));
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3)), T0);
        return draft;
    }

    private sealed record PurchaseLineRef(Guid OrderId, Guid LineId);

    /// <summary>(sales attempts, logistics attempts, purchasing attempts, audit events) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int LogisticsAttempts, int PurchaseAttempts, int AuditEvents);

    private sealed class LogisticsRead(LogisticsDbContext context, CompanyContext company) : IAsyncDisposable
    {
        public LogisticsDbContext Context => context;

        public CompanyContext Company => company;

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }

    private sealed class PurchasingRead(PurchasingDbContext context, CompanyContext company) : IAsyncDisposable
    {
        public PurchasingDbContext Context => context;

        public CompanyContext Company => company;

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;
        private int purchaseSequence;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = "SENTETIK-operations-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"at-025-{Guid.NewGuid():N}";

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
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix, string quantity)
        {
            var submitted = await SubmittedDraftAsync(company, keyPrefix, quantity);
            await using var scope = SalesStore(company);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"), Hash($"{keyPrefix}-approve"),
                SalesDraftCommands.ApproveV1, submitted.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        /// <summary>A stored draft of the company, submitted for approval only (version 3): not an approved demand.</summary>
        public async Task<SalesOrderDraft> SubmittedDraftAsync(Guid company, string keyPrefix, string quantity)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo, quantity))).Draft;
            var submitted = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1,
                draft.Id, (d, now) => d.Submit(2, Actor, now), Submitted);
            return submitted!.Draft;
        }

        /// <summary>An instruction of <paramref name="company"/> for the draft's one line (6 kg instructed), issued, loaded and shipped, through the real Logistics store.</summary>
        public async Task ShippedInstructionAsync(Guid company, SalesOrderDraft draft, string keyPrefix, decimal loaded, decimal shipped)
        {
            var orderLine = Assert.Single(draft.Lines);
            await using var scope = LogisticsStore(company);
            var stored = (await scope.Store.StoreInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-create"), Hash($"{keyPrefix}-l-create"), Day, () =>
            {
                var instruction = LoadingInstruction.Create(Guid.NewGuid(), company, draft.Header.PartyId, draft.Header.CompanyAccountId, T0);
                instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(draft.Id, orderLine.Id, draft.Revision),
                    orderLine.Product.ProductId, KgUnit, null, Kg(6m), T0);
                return instruction;
            })).Instruction.Instruction;
            var lineId = Assert.Single(stored.Lines).Id;
            var id = stored.Id;
            var issued = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-issue"), Hash($"{keyPrefix}-l-issue"),
                LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(stored.Version, now), Issued))!.Instruction.Instruction;
            var afterLoad = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-load"), Hash($"{keyPrefix}-l-load"),
                LogisticsCommands.RecordLoadV1, id, (instruction, now) => instruction.RecordLoad(issued.Version, Guid.NewGuid(), lineId, Kg(loaded),
                    MeasurementKind.Measured, now), Loaded))!.Instruction.Instruction;
            await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-ship"), Hash($"{keyPrefix}-l-ship"),
                LogisticsCommands.RecordShipmentV1, id, (instruction, now) => instruction.MarkShipped(afterLoad.Version, Guid.NewGuid(), lineId, Kg(shipped), now),
                Shipped);
        }

        /// <summary>A purchase order of the company towards a SYNTHETIC supplier with one general kg line, approved when asked, through the real Purchasing store.</summary>
        public async Task<PurchaseLineRef> PurchaseOrderAsync(Guid company, string keyPrefix, decimal quantity, bool approve)
        {
            await using var scope = PurchasingStore(company);
            // Each order one minute after the previous: "newest first" is then a fact of the data, never an id tie-break.
            var at = T0.AddMinutes(Interlocked.Increment(ref purchaseSequence));
            var order = PurchaseOrder.Create(Guid.NewGuid(), company, Guid.NewGuid(), at);
            order.AddLine(order.Version, Guid.NewGuid(), Product, KgUnit, PurchaseQuantity.Of(quantity, PurchaseQuantityUnit.Kilogram), null, at);
            var stored = (await scope.Store.StoreOrderAsync(PurchasingEnvelope($"{keyPrefix}-p-create"), Hash($"{keyPrefix}-p-create"), Guid.NewGuid(), Day,
                () => order, order.Lines.ToDictionary(line => line.Id, _ => "7"))).Order;
            if (approve)
            {
                await scope.Store.MutateOrderAsync(PurchasingEnvelope($"{keyPrefix}-p-approve"), Hash($"{keyPrefix}-p-approve"), PurchasingCommands.OrderApproveV1,
                    stored.Order.Id, (o, now) => o.Approve(stored.Order.Version, now), PurchaseApproved);
            }

            return new PurchaseLineRef(stored.Order.Id, Assert.Single(stored.Order.Lines).Id);
        }

        public async Task<JsonElement> ReadAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync() => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [logistics].[LoadingAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [purchasing].[PurchaseAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"));

        private SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        private LogisticsWriteEnvelope LogisticsEnvelope(string key) =>
            new(Actor, "SENTETIK-logistics-permission", ["SENTETIK-LOGISTICS"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        private PurchasingWriteEnvelope PurchasingEnvelope(string key) =>
            new(Actor, "SENTETIK-purchasing-permission", ["SENTETIK-PURCHASING"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        private (SalesDbContext Context, SalesStore Store) SalesParts(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return (dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        private StoreScope<ISalesOrderDraftStore> SalesStore(Guid company)
        {
            var (context, store) = SalesParts(company);
            return new StoreScope<ISalesOrderDraftStore>(context, store);
        }

        private StoreScope<ILogisticsInstructionStore> LogisticsStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new StoreScope<ILogisticsInstructionStore>(dbContext, new LogisticsStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        private StoreScope<IPurchasingStore> PurchasingStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new StoreScope<IPurchasingStore>(dbContext, new PurchasingStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public LogisticsRead LogisticsContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new LogisticsRead(new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .Options, companyContext), companyContext);
        }

        public PurchasingRead PurchasingContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new PurchasingRead(new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic operations work user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"AT-025-{roleId:N}", "Synthetic operations work role", true);
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

    private sealed class StoreScope<TStore>(DbContext dbContext, TStore store) : IAsyncDisposable
    {
        public TStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }
}
