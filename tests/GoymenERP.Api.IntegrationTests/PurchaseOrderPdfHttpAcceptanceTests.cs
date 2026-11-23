using System.Net;
using System.Reflection;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UglyToad.PdfPig;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP13 (DALGA_PLANI_v2 1.14): <c>GET /api/v1/purchasing/orders/{id}/pdf</c> over SENTETIK Purchasing, Product and Settings reads (no
/// database): the PDF text is read back from the bytes (PdfPig), is the same for the same answers, carries the owner's figures
/// verbatim (nothing is computed: the calculator's line amounts and totals as answered, a state as its reason), prints no price column
/// or total without the price read, prints only general lines, refuses two different versions (409) and an unprintable answer (503),
/// and reaches no owner for an anonymous caller or another company. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseOrderPdfHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid Kg = Guid.Parse("6a1f0c3e-0d5b-4e7c-9f21-3b4c5d6e7f80");
    private static readonly Guid ProductId = Guid.Parse("7b2f1d4e-1e6c-4f8d-8a32-4c5d6e7f8091");
    private static readonly Guid Currency = Guid.Parse("9e60dcf0-f4bd-5bca-b464-f9a2db98e31a");
    private static readonly Guid GeneralLineId = Guid.Parse("8c3f2e5f-2f7d-4a9e-9b43-5d6e7f8091a2");

    [Fact]
    public async Task The_priced_order_prints_the_supplier_products_schedule_and_the_calculators_amounts_verbatim()
    {
        var orderId = Guid.NewGuid();
        using var host = CreateHost(new Orders(_ => Order(orderId, visible: true)), Totals(_ => TotalsOf(orderId, visible: true)));
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PdfPath(orderId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("Satın_Alma_Siparişi_PO-2026-000007_SENTETIK_Metal_2026-11-20_v03_Onaylı.pdf",
            response.Content.Headers.ContentDisposition!.FileNameStar);

        var text = await TextAsync(response);
        Assert.Contains("Satın Alma Siparişi PO-2026-000007", text, StringComparison.Ordinal);
        Assert.Contains("SUP-0009 — SENTETIK Metal Sanayi A.Ş. (SENTETIK Metal)", text, StringComparison.Ordinal);
        Assert.Contains("HMD-001", text, StringComparison.Ordinal);
        Assert.Contains("Sentetik sac", text, StringComparison.Ordinal);
        Assert.Contains("25.12.2026: 60", text, StringComparison.Ordinal);
        Assert.Contains("10.50 TRY / kg", text, StringComparison.Ordinal);
        Assert.Contains("1050.00 TRY", text, StringComparison.Ordinal);
        Assert.Contains("Net toplam1050.00 TRY", text, StringComparison.Ordinal);
        Assert.Contains("KDV toplamı210.00 TRY", text, StringComparison.Ordinal);
        Assert.Contains("Ödenecek tutar1260.00 TRY", text, StringComparison.Ordinal);
        Assert.Contains("Tevkif edilen KDV toplamıTevkifat yok", text, StringComparison.Ordinal);
        // Only the general line is printed; the technical breakdown line is not a second commitment.
        Assert.DoesNotContain("TEKNIK-SATIR", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TASLAK", text, StringComparison.Ordinal);
        Assert.Contains("PO-2026-000007 · sürüm 3 · Bilgi amaçlı teknik çıktıdır; resmî belge değildir.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_price_read_no_price_column_currency_or_total_is_drawn_and_the_text_is_deterministic()
    {
        var orderId = Guid.NewGuid();
        using var host = CreateHost(new Orders(_ => Order(orderId, visible: false)), Totals(_ => TotalsOf(orderId, visible: false)));
        using var client = CreateClient(host);

        using var first = await client.GetAsync(PdfPath(orderId));
        using var second = await client.GetAsync(PdfPath(orderId));

        var text = await TextAsync(first);
        Assert.Equal(text, await TextAsync(second));
        Assert.Contains("Fiyat bilgileri bu belgede yer almaz.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TRY", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Birim fiyat", text, StringComparison.Ordinal);
        Assert.Equal("Satın_Alma_Siparişi_Fiyatsız_PO-2026-000007_SENTETIK_Metal_2026-11-20_v03_Onaylı.pdf",
            first.Content.Headers.ContentDisposition!.FileNameStar);
    }

    [Fact]
    public async Task Two_different_versions_are_a_conflict_and_an_unknown_unit_is_503_never_a_partial_pdf()
    {
        var orderId = Guid.NewGuid();
        using (var host = CreateHost(new Orders(_ => Order(orderId, visible: true)), Totals(_ => TotalsOf(orderId, visible: true) with { Version = "4" })))
        using (var client = CreateClient(host))
        using (var response = await client.GetAsync(PdfPath(orderId)))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        var unknownUnit = Order(orderId, visible: true);
        unknownUnit = unknownUnit with { Lines = [.. unknownUnit.Lines.Select(line => line with { UnitId = Guid.NewGuid() })] };
        using (var host = CreateHost(new Orders(_ => unknownUnit), Totals(_ => TotalsOf(orderId, visible: true))))
        using (var client = CreateClient(host))
        using (var response = await client.GetAsync(PdfPath(orderId)))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.NotEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Theory]
    [InlineData(403, HttpStatusCode.Forbidden)]
    [InlineData(404, HttpStatusCode.NotFound)]
    [InlineData(503, HttpStatusCode.ServiceUnavailable)]
    public async Task An_owner_refusal_is_code_less_and_never_a_pdf(int status, HttpStatusCode expected)
    {
        using var host = CreateHost(new Orders(_ => throw new PurchasingServiceException(status, "SENTETIK-source")),
            Totals(_ => throw new InvalidOperationException("The totals must not be asked.")));
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PdfPath(Guid.NewGuid()));

        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("SENTETIK-source", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_anonymous_caller_or_another_company_reaches_no_owner()
    {
        var orders = new Orders(_ => throw new InvalidOperationException("The order must not be read."));
        using var host = CreateHost(orders, Totals(_ => throw new InvalidOperationException("The totals must not be asked.")));
        var orderId = Guid.NewGuid();

        using (var anonymous = host.CreateClient())
        {
            anonymous.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
            using var response = await anonymous.GetAsync(PdfPath(orderId));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var foreign = host.CreateClient())
        {
            foreign.DefaultRequestHeaders.Add("X-Development-User", TestUsers.CompanyB);
            foreign.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
            using var response = await foreign.GetAsync(PdfPath(orderId));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(0, orders.Calls);
    }

    private static string PdfPath(Guid orderId) => $"{PurchasingApiV1.OrdersPath}/{orderId:D}/pdf";

    private static async Task<string> TextAsync(HttpResponseMessage response)
    {
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return string.Concat(pdf.GetPages().Select(page => page.Text));
    }

    private static PurchaseOrderReadDetailViewV1 Order(Guid orderId, bool visible)
    {
        var supplier = new PurchasingSupplierDisplayV1(Guid.NewGuid(), "SUP-0009", "SENTETIK Metal Sanayi A.Ş.", "SENTETIK Metal");
        var summary = new PurchaseOrderReadSummaryViewV1(orderId, CompanyA, "Approved", "3", supplier.PartyId, Guid.NewGuid(), null,
            new DateOnly(2026, 11, 20), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 2, supplier) { DocumentNumber = "PO-2026-000007" };
        var generalId = GeneralLineId;
        var general = new PurchaseOrderLineView(generalId, 1, "General", null, null, ProductId, "5", Kg, "100", "0", "100", "0", "100")
        {
            Price = visible ? new PurchaseOrderLinePriceView("10.50", Kg, null, null, "10.50", "1050.00") : null,
            DeliverySchedule = [new PurchaseOrderDeliverySliceView(Guid.NewGuid(), 1, "40", new DateOnly(2026, 12, 15)),
                new PurchaseOrderDeliverySliceView(Guid.NewGuid(), 2, "60", new DateOnly(2026, 12, 25))],
        };
        var technical = new PurchaseOrderLineView(Guid.NewGuid(), 2, "Technical", generalId, null, ProductId, "5", Kg, "TEKNIK-SATIR", "0",
            "0", "0", "0");
        return new PurchaseOrderReadDetailViewV1(summary, [general, technical])
        {
            PriceDisclosure = visible ? PurchasingPriceDisclosureV1.Visible : PurchasingPriceDisclosureV1.Withheld,
            Currency = new PurchaseOrderCurrencyView(Currency, "TRY", 2),
        };
    }

    private static PurchaseOrderTotalsView TotalsOf(Guid orderId, bool visible) => visible
        ? new PurchaseOrderTotalsView(orderId, "3", PurchasingPriceDisclosureV1.Visible, new PurchaseOrderCurrencyView(Currency, "TRY", 2),
            "Computed", 0, "1050.00", "Computed", 0, "210.00", "1260.00", "NotApplied", null, null, "1260.00",
            [new PurchaseOrderLineTotalView(GeneralLineId, 1, "Computed", "10.50", "1050.00", "Computed", null, "20", "210.00", "1260.00", "NotApplied")])
        : new PurchaseOrderTotalsView(orderId, "3", PurchasingPriceDisclosureV1.Withheld, null, "Withheld", 0, null, "Withheld", 0, null, null,
            "Withheld", null, null, null, []);

    private WebApplicationFactory<Program> CreateHost(Orders orders, IPurchasingService totals) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPurchasingOrderReadServiceV1>();
            services.AddSingleton<IPurchasingOrderReadServiceV1>(orders);
            services.RemoveAll<IPurchasingService>();
            services.AddSingleton(totals);
            services.RemoveAll<IProductPurchasingDocumentDisplayV1>();
            services.AddSingleton<IProductPurchasingDocumentDisplayV1, Products>();
            services.RemoveAll<ISettingsUnitIdentityCatalogV1>();
            services.AddSingleton<ISettingsUnitIdentityCatalogV1, Units>();
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        return client;
    }

    /// <summary>The totals read through a proxy of the Purchasing service: only <c>GetOrderTotalsAsync</c> answers.</summary>
    private static IPurchasingService Totals(Func<Guid, PurchaseOrderTotalsView> answer)
    {
        var proxy = DispatchProxy.Create<IPurchasingService, TotalsProxy>();
        ((TotalsProxy)(object)proxy).Answer = answer;
        return proxy;
    }

    public class TotalsProxy : DispatchProxy
    {
        public Func<Guid, PurchaseOrderTotalsView> Answer { get; set; } = _ => throw new InvalidOperationException();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IPurchasingService.GetOrderTotalsAsync) => Task.FromResult(Answer((Guid)args![0]!)),
            _ => throw new NotSupportedException(targetMethod?.Name),
        };
    }

    private sealed class Orders(Func<Guid, PurchaseOrderReadDetailViewV1> detail) : IPurchasingOrderReadServiceV1
    {
        public int Calls { get; private set; }

        public Task<PurchaseOrderReadDetailViewV1> GetOrderDetailAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(detail(orderId));
        }

        public Task<PurchaseOrderReadListResponseV1> GetOrderListAsync(PurchaseOrderListQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Products : IProductPurchasingDocumentDisplayV1
    {
        public Task<ProductPurchasingDocumentDisplayV1> ReadAsync(Guid companyId, Guid productId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductPurchasingDocumentDisplayV1(ProductPurchasingDocumentDisplayOutcomeV1.Available, companyId, productId, "5",
                "HMD-001", "Sentetik sac"));
    }

    private sealed class Units : ISettingsUnitIdentityCatalogV1
    {
        private static readonly UnitIdentityV1 Kilogram = new(Kg, "kg", "weight", true);

        public Task<UnitIdentityV1?> ReadAsync(Guid unitId, CancellationToken cancellationToken = default) =>
            Task.FromResult(unitId == Kg ? Kilogram : null);

        public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UnitIdentityV1>>([Kilogram]);
    }
}
