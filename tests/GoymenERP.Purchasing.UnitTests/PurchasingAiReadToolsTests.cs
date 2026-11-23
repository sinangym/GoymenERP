using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Ai;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// WP-A3b Purchasing READ tools over SENTETIK fakes of the Purchasing owner reads: order counts without price or supplier
/// display, pending receipts totalled per unit (kg and pieces never added), CONFIDENTIAL invoice variances totalled per
/// currency without approver or reason; a refused, site-scoped or revoked grant, another company's row or an unreadable
/// number releases nothing, and the scan cap withholds the counts.
/// </summary>
public sealed class PurchasingAiReadToolsTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000005a1");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000005b2");
    private static readonly Guid Supplier = Guid.Parse("cccccccc-0000-4000-8000-0000000005c3");
    private static readonly Guid KgUnit = Guid.Parse("dddddddd-0000-4000-8000-0000000005d4");
    private static readonly Guid PieceUnit = Guid.Parse("eeeeeeee-0000-4000-8000-0000000005e5");
    private static readonly Guid Try = Guid.Parse("ffffffff-0000-4000-8000-0000000005f6");
    private static readonly Guid Usd = Guid.Parse("12121212-0000-4000-8000-000000000507");
    private static readonly Guid Site = Guid.Parse("34343434-0000-4000-8000-000000000508");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] InvoicePermissions =
        [PurchaseIncomingInvoicePermissionNamesV1.Read, PurchasingPermissionManifestV1.OrdersPriceRead];

    [Fact]
    public void The_descriptors_are_purchasing_read_tools_and_only_the_invoice_variance_tool_is_confidential()
    {
        var authority = new FakeAuthority();
        var orders = new PurchasingOrderStatusAiTool(new FakeOrders(), authority, new FixedTime(Now)).Descriptor;
        var receipts = new PurchasingPendingReceiptsAiTool(new FakeLines(), authority, new FixedTime(Now)).Descriptor;
        var variances = new PurchasingInvoiceVariancesAiTool(new FakeInvoices(), authority, new FixedTime(Now)).Descriptor;

        foreach (var descriptor in new[] { orders, receipts, variances })
        {
            Assert.Equal("Purchasing", descriptor.OwnerModule);
            Assert.Equal(AiToolRiskClassV1.Read, descriptor.Risk);
            Assert.Null(descriptor.OwnerCommandName);
        }

        Assert.Equal(AiDataClassV1.Internal, orders.DataClass);
        Assert.Equal(AiDataClassV1.Internal, receipts.DataClass);
        Assert.Equal(AiDataClassV1.Confidential, variances.DataClass);
        Assert.Equal(new[] { "purchasing.orders.read" }, orders.RequiredPermissions);
        Assert.Equal(new[] { "limit", "status", "supplierPartyId" }, orders.FieldAllowlist);
        Assert.Equal(new[] { "purchasing.orders.read" }, receipts.RequiredPermissions);
        Assert.Equal(new[] { "limit", "productId", "supplierPartyId" }, receipts.FieldAllowlist);
        Assert.Equal(new[] { "purchasing.invoices.read", "purchasing.orders.price.read" }, variances.RequiredPermissions);
        Assert.Equal(new[] { "decision", "limit" }, variances.FieldAllowlist);
    }

    [Fact]
    public async Task Orders_are_counted_per_status_with_price_pending_and_no_supplier_display_is_released()
    {
        var owner = new FakeOrders();
        owner.Add(Order(Company, "Draft", pricePending: true));
        owner.Add(Order(Company, "Draft", pricePending: false));
        owner.Add(Order(Company, "Approved", pricePending: null));
        var tool = new PurchasingOrderStatusAiTool(owner, new FakeAuthority(PurchasingPermissionManifestV1.OrdersRead), new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1($"{{\"supplierPartyId\":\"{Supplier:D}\"}}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal(3, root.GetProperty("orderCount").GetInt32());
        Assert.Equal(1, root.GetProperty("pricePendingCount").GetInt32());
        var byStatus = root.GetProperty("byStatus").EnumerateArray()
            .ToDictionary(item => item.GetProperty("status").GetString()!, item => item.GetProperty("count").GetInt32());
        Assert.Equal(2, byStatus["Draft"]);
        Assert.Equal(1, byStatus["Approved"]);
        Assert.Equal(new[] { "Draft", "Approved" }, owner.Queries.Select(query => query.Status).ToArray());
        Assert.All(owner.Queries, query => Assert.Equal(Supplier, query.SupplierPartyId));
        Assert.DoesNotContain("SENTETIK Tedarikçi", result.ContentJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("legalName", result.ContentJson!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_order_owner_refusal_a_foreign_row_and_a_site_scoped_grant_release_nothing()
    {
        var refusing = new FakeOrders { Refusal = new PurchasingServiceException(403, "forbidden") };
        var foreign = new FakeOrders();
        foreign.Add(Order(OtherCompany, "Draft", null));
        var scoped = new FakeOrders();
        var granted = new FakeAuthority(PurchasingPermissionManifestV1.OrdersRead);

        Assert.Equal(AiToolOutcomeV1.Denied, (await new PurchasingOrderStatusAiTool(refusing, granted, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new PurchasingOrderStatusAiTool(foreign, granted, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.Denied, (await new PurchasingOrderStatusAiTool(scoped,
                new FakeAuthority(PurchasingPermissionManifestV1.OrdersRead) { ScopedToSite = Site }, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Empty(scoped.Queries);
    }

    [Fact]
    public async Task Pending_receipts_are_totalled_per_unit_and_kilograms_and_pieces_are_never_added()
    {
        var owner = new FakeLines();
        owner.Items.Add(Line(Company, KgUnit, "1000", "250.5", "749.5"));
        owner.Items.Add(Line(Company, KgUnit, "100", "0", "100"));
        owner.Items.Add(Line(Company, PieceUnit, "12", "2", "10"));
        var tool = new PurchasingPendingReceiptsAiTool(owner, new FakeAuthority(PurchasingPermissionManifestV1.OrdersRead), new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal(3, root.GetProperty("lineCount").GetInt32());
        Assert.Equal("KLN-D42", root.GetProperty("definition").GetString());
        Assert.Equal(3, root.GetProperty("orderCount").GetInt32());
        var perUnit = root.GetProperty("remainingByUnit").EnumerateArray()
            .ToDictionary(item => item.GetProperty("unitId").GetGuid(), item => item.GetProperty("remainingTotal").GetString());
        Assert.Equal(2, perUnit.Count);
        Assert.Equal("849.5", perUnit[KgUnit]);
        Assert.Equal("10", perUnit[PieceUnit]);
        Assert.DoesNotContain("859.5", result.ContentJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pending_receipts_are_measured_by_the_tool_because_the_port_carries_no_authority()
    {
        var owner = new FakeLines();
        owner.Items.Add(Line(Company, KgUnit, "1", "0", "1"));

        Assert.Equal(AiToolOutcomeV1.Denied, (await new PurchasingPendingReceiptsAiTool(owner, new FakeAuthority(), new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Equal(0, owner.Reads);
        var revoked = new FakeAuthority(PurchasingPermissionManifestV1.OrdersRead) { RevokeAfterCalls = 1 };
        Assert.Equal(AiToolOutcomeV1.Denied, (await new PurchasingPendingReceiptsAiTool(owner, revoked, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Equal(1, owner.Reads);
    }

    [Fact]
    public async Task A_pending_receipt_line_of_another_company_or_with_no_remaining_releases_nothing()
    {
        var foreign = new FakeLines();
        foreign.Items.Add(Line(OtherCompany, KgUnit, "1", "0", "1"));
        var settled = new FakeLines();
        settled.Items.Add(Line(Company, KgUnit, "1", "1", "0"));
        var authority = new FakeAuthority(PurchasingPermissionManifestV1.OrdersRead);

        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new PurchasingPendingReceiptsAiTool(foreign, authority, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new PurchasingPendingReceiptsAiTool(settled, authority, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
    }

    [Fact]
    public async Task Pending_receipts_over_the_scan_cap_withhold_the_counts_and_per_unit_totals()
    {
        var owner = new FakeLines();
        for (var i = 0; i < PurchasingPendingReceiptsAiTool.ScanLimit + 1; i++)
        {
            owner.Items.Add(Line(Company, KgUnit, "1", "0", "1"));
        }

        var result = await new PurchasingPendingReceiptsAiTool(owner, new FakeAuthority(PurchasingPermissionManifestV1.OrdersRead),
            new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.False(content.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("lineCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("remainingByUnit").ValueKind);
    }

    [Fact]
    public async Task Pending_invoice_variances_are_confidential_totalled_per_currency_and_carry_no_approver_or_reason()
    {
        var owner = new FakeInvoices();
        owner.Items.Add(Invoice(Company, Try, decided: false, ("Price", "1000.00", "1012.50"), ("Quantity", "10", "9")));
        owner.Items.Add(Invoice(Company, Usd, decided: false, ("Price", "50", "45.25")));
        owner.Items.Add(Invoice(Company, Try, decided: true, ("Price", "1", "2")));
        owner.Items.Add(Invoice(Company, Try, decided: false));
        var tool = new PurchasingInvoiceVariancesAiTool(owner, new FakeAuthority(InvoicePermissions), new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        Assert.Equal(AiDataClassV1.Confidential, result.Source!.DataClass);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal("pending", root.GetProperty("decision").GetString());
        Assert.Equal(2, root.GetProperty("invoiceCount").GetInt32());
        Assert.Equal(1, root.GetProperty("quantityVarianceCount").GetInt32());
        Assert.Equal(2, root.GetProperty("priceVarianceCount").GetInt32());
        var perCurrency = root.GetProperty("priceDifferenceByCurrency").EnumerateArray()
            .ToDictionary(item => item.GetProperty("currencyId").GetGuid(), item => item.GetProperty("priceDifferenceTotal").GetString());
        Assert.Equal("12.5", perCurrency[Try]);
        Assert.Equal("-4.75", perCurrency[Usd]);
        Assert.DoesNotContain("approvedBy", result.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENTETIK onay nedeni", result.ContentJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invoice_variances_need_the_price_grant_and_an_unknown_kind_or_foreign_invoice_releases_nothing()
    {
        var owner = new FakeInvoices();
        owner.Items.Add(Invoice(Company, Try, decided: false, ("Price", "1", "2")));
        var unknown = new FakeInvoices();
        unknown.Items.Add(Invoice(Company, Try, decided: false, ("Discount", "1", "2")));
        var foreign = new FakeInvoices();
        foreign.Items.Add(Invoice(OtherCompany, Try, decided: false, ("Price", "1", "2")));
        var granted = new FakeAuthority(InvoicePermissions);

        Assert.Equal(AiToolOutcomeV1.Denied, (await new PurchasingInvoiceVariancesAiTool(owner,
            new FakeAuthority(PurchaseIncomingInvoicePermissionNamesV1.Read), new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Empty(owner.Pages);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new PurchasingInvoiceVariancesAiTool(unknown, granted, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new PurchasingInvoiceVariancesAiTool(foreign, granted, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
    }

    [Fact]
    public async Task Invoice_variances_over_the_page_cap_withhold_the_counts()
    {
        var owner = new FakeInvoices { AlwaysHasNext = true };
        owner.Items.Add(Invoice(Company, Try, decided: false, ("Price", "1", "2")));

        var result = await new PurchasingInvoiceVariancesAiTool(owner, new FakeAuthority(InvoicePermissions), new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{\"decision\":\"any\"}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.False(content.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("invoiceCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("priceDifferenceByCurrency").ValueKind);
        Assert.Equal(PurchasingInvoiceVariancesAiTool.MaximumPages, owner.Pages.Count);
    }

    [Theory]
    [InlineData("{\"decision\":\"open\"}", "decision")]
    [InlineData("{\"limit\":26}", "limit")]
    [InlineData("{\"limit\":\"5\"}", "limit")]
    [InlineData("{\"companyId\":\"aaaaaaaa-0000-4000-8000-0000000005a1\"}", "arguments")]
    [InlineData("not json", "arguments")]
    public async Task Invoice_variance_arguments_outside_the_closed_schema_are_invalid_before_any_read(string arguments, string field)
    {
        var owner = new FakeInvoices();
        var authority = new FakeAuthority(InvoicePermissions);

        var result = await new PurchasingInvoiceVariancesAiTool(owner, authority, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1(arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Equal(new[] { field }, result.InvalidFields);
        Assert.Empty(owner.Pages);
        Assert.Empty(authority.Measured);
    }

    private static AiServerContextV1 Context() => new(Company, "actor-sentetik", "v1.session-sentetik",
        $"company:{Company:D}", "roles:sentetik", "corr-wpa3b-sentetik", null, null, null);

    private static PurchaseOrderReadSummaryViewV1 Order(Guid company, string status, bool? pricePending) => new(Guid.NewGuid(), company,
        status, "3", Supplier, Guid.NewGuid(), null, new DateOnly(2026, 11, 20), Now, Now, 2,
        new PurchasingSupplierDisplayV1(Supplier, "T-001", "SENTETIK Tedarikçi A.Ş.", null)) { PricePending = pricePending };

    private static PurchaseOpenLineReferenceV1 Line(Guid company, Guid unit, string committed, string accepted, string remaining) => new(
        company, Guid.NewGuid(), new DateOnly(2026, 11, 20), Guid.NewGuid(), 1, "Goods", Supplier, Guid.NewGuid(), Guid.NewGuid(), "2",
        unit, committed, accepted, remaining);

    private static PurchaseIncomingInvoiceViewV1 Invoice(Guid company, Guid currency, bool decided,
        params (string Kind, string Expected, string Actual)[] variances)
    {
        var lineId = Guid.NewGuid();
        var amounts = new PurchaseInvoiceAmountsViewV1("100", "20", "0", "120");
        var line = new PurchaseInvoiceLineViewV1(lineId, 1, Guid.NewGuid(), KgUnit, "10", "Goods", "Goods", "20", amounts, null);
        return new PurchaseIncomingInvoiceViewV1(Guid.NewGuid(), company, "5", "Matched", Supplier, Guid.NewGuid(), currency, Try,
            2, 2, null, "SNT2026000000001", Guid.NewGuid(), new DateOnly(2026, 11, 21), Guid.NewGuid(), "1", amounts, null, [line],
            variances.Select(item => new PurchaseInvoiceVarianceViewV1(lineId, item.Kind, item.Expected, item.Actual)).ToArray(),
            decided ? new GoymenERP.Modules.Purchasing.Domain.Invoices.PurchaseInvoiceVarianceDecision("approver-sentetik",
                "SENTETIK onay nedeni", Now) : null, null);
    }

    private sealed class FakeOrders : IPurchasingOrderReadServiceV1
    {
        private readonly List<PurchaseOrderReadSummaryViewV1> rows = [];
        public List<PurchaseOrderListQuery> Queries { get; } = [];
        public Exception? Refusal { get; init; }

        public void Add(PurchaseOrderReadSummaryViewV1 row) => rows.Add(row);

        public Task<PurchaseOrderReadDetailViewV1> GetOrderDetailAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PurchaseOrderReadListResponseV1> GetOrderListAsync(PurchaseOrderListQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            if (Refusal is not null)
            {
                throw Refusal;
            }

            var items = rows.Where(row => query.Status is null || row.Status == query.Status || row.CompanyId != Company).ToArray();
            return Task.FromResult(new PurchaseOrderReadListResponseV1(items, null));
        }
    }

    private sealed class FakeLines : IPurchaseOpenLineReadPortV1
    {
        public List<PurchaseOpenLineReferenceV1> Items { get; } = [];
        public int Reads { get; private set; }

        public Task<PurchaseOpenLinePageV1> ListOpenApprovedLinesAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default)
        {
            Reads++;
            var start = cursor is null ? 0 : int.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture);
            var page = Items.Skip(start).Take(pageSize).ToArray();
            var end = start + page.Length;
            return Task.FromResult(new PurchaseOpenLinePageV1(page,
                end < Items.Count ? end.ToString(System.Globalization.CultureInfo.InvariantCulture) : null));
        }
    }

    private sealed class FakeInvoices : IPurchaseIncomingInvoiceWorkspaceV1
    {
        public List<PurchaseIncomingInvoiceViewV1> Items { get; } = [];
        public List<int> Pages { get; } = [];
        public bool AlwaysHasNext { get; init; }

        public Task<PurchaseIncomingInvoicePageV1> ListAsync(string? officialNumber, int pageIndex, CancellationToken cancellationToken = default)
        {
            Pages.Add(pageIndex);
            var items = AlwaysHasNext ? (pageIndex == 0 ? Items.ToArray() : []) : Items.Skip(pageIndex * 25).Take(25).ToArray();
            return Task.FromResult(new PurchaseIncomingInvoicePageV1(pageIndex, AlwaysHasNext || (pageIndex + 1) * 25 < Items.Count, items));
        }

        public Task<PurchaseInvoiceReceiptCandidatePageV1> CandidatesAsync(Guid invoiceId, Guid invoiceLineId, int pageIndex,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeAuthority(params string[] granted) : IErpAuthorityRevalidationService
    {
        public List<string> Measured { get; } = [];
        public Guid? ScopedToSite { get; init; }
        public int? RevokeAfterCalls { get; init; }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Measured.Add(permission);
            if (companyId != Company || !granted.Contains(permission, StringComparer.Ordinal)
                || (RevokeAfterCalls is { } limit && Measured.Count > limit))
            {
                return Task.FromResult(PermissionScope.Denied);
            }

            var sites = ScopedToSite is { } site ? new HashSet<Guid> { site } : new HashSet<Guid>();
            return Task.FromResult(new PermissionScope(true, ScopedToSite is null, new HashSet<string>(StringComparer.Ordinal),
                sites, new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
