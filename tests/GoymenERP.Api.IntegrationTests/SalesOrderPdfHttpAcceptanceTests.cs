using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UglyToad.PdfPig;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// MOD-07: <c>GET /api/v1/sales/drafts/{id}/pdf</c> over SENTETIK Sales reads (no database, no owner): what the wire and
/// the PDF carry for each answer of the two reads. The PDF text is read back from the bytes (PdfPig), never assumed. The
/// real host with the real store, grants and owners is <c>SalesOrderPdfSqlServerRelationalAcceptanceTests</c>.
/// </summary>
public sealed class SalesOrderPdfHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid Kg = Guid.Parse("6a1f0c3e-0d5b-4e7c-9f21-3b4c5d6e7f80");

    [Fact]
    public async Task The_priced_order_is_a_pdf_attachment_with_the_stored_line_prices_the_calculators_net_and_no_total()
    {
        var draftId = Guid.NewGuid();
        var (detail, pricing) = Order(draftId, SalesPriceDisclosure.Visible);
        using var host = CreateHost(new StubDetail(_ => Task.FromResult(detail)), new StubPricing(_ => Task.FromResult(pricing)));
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PdfPath(draftId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        // DEC-DOC-004: type, number, customer, business date, version (Rev.1 is the second approved state), status.
        Assert.Equal("Satış_Siparişi_SO-2026-000123_SENTETIK_Gıda_2026-10-27_v02_Onaylı.pdf", disposition.FileNameStar);

        var text = await TextAsync(response);
        Assert.Contains("Satış Siparişi SO-2026-000123", text, StringComparison.Ordinal);
        Assert.Contains("CAR-000042 — SENTETIK Gıda Sanayi A.Ş. (SENTETIK Gıda)", text, StringComparison.Ordinal);
        Assert.Contains("Taslak · Onaylı", text, StringComparison.Ordinal);
        Assert.Contains("SENTETIK Onaylayan · 27.10.2026 12:30 (İstanbul saati)", text, StringComparison.Ordinal);
        Assert.Contains("GDC-001", text, StringComparison.Ordinal);
        Assert.Contains("Sentetik bant", text, StringComparison.Ordinal);
        Assert.Contains("12.50 TRY / kg", text, StringComparison.Ordinal);
        Assert.Contains("%5", text, StringComparison.Ordinal);
        Assert.Contains("0.25 TRY", text, StringComparison.Ordinal);
        Assert.Contains("12.125 TRY / kg", text, StringComparison.Ordinal);
        Assert.Contains("Para birimiTRY", text, StringComparison.Ordinal);
        Assert.Contains("Sipariş toplamı, KDV ve genel toplam henüz hesaplanmıyor", text, StringComparison.Ordinal);
        Assert.Contains("SO-2026-000123 · Rev.1 · Bilgi amaçlı teknik çıktıdır; resmî belge değildir.", text, StringComparison.Ordinal);
        Assert.Contains("Sayfa 1 / 1", text, StringComparison.Ordinal);
        // Nothing is computed: 4 kg × 12.125 = 48.5 is no line amount or total of this document.
        Assert.DoesNotContain("48.5", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Fiyat bilgileri bu belgede yer almaz", text, StringComparison.Ordinal);
        // SAT-D56: an approved revision carries no draft watermark.
        Assert.DoesNotContain("TASLAK", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// SAT-D56 (Sinan, "Evet, ama 'TASLAK' filigranıyla"): an order whose current revision is not approved is downloadable
    /// but carries the large "TASLAK" watermark on EVERY page; the content still reads back.
    /// </summary>
    [Theory]
    [InlineData("Preparing")]
    [InlineData("PendingApproval")]
    [InlineData("Rejected")]
    [InlineData("Withdrawn")]
    public async Task An_unapproved_order_is_printed_with_the_draft_watermark_on_every_page(string approvalState)
    {
        var draftId = Guid.NewGuid();
        var (detail, pricing) = Order(draftId, SalesPriceDisclosure.Withheld);
        var template = Assert.Single(detail.Lines);
        var lines = Enumerable.Range(1, 60)
            .Select(lineNo => template with { LineId = Guid.NewGuid(), LineNo = lineNo, ProductCode = $"GDC-{lineNo:D3}" })
            .ToArray();
        var unapproved = detail with
        {
            Draft = detail.Draft with { ApprovalState = approvalState, LineCount = lines.Length },
            Lines = lines,
            Approval = detail.Approval with { DecidedBy = null, DecidedAtUtc = null },
        };
        using var host = CreateHost(new StubDetail(_ => Task.FromResult(unapproved)), new StubPricing(_ => Task.FromResult(pricing)));
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PdfPath(draftId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        var pages = pdf.GetPages().ToArray();
        Assert.True(pages.Length > 1, "60 lines span more than one page.");
        foreach (var page in pages)
        {
            Assert.Equal("TASLAK", string.Concat(page.Letters.Where(letter => letter.PointSize > 100).Select(letter => letter.Value)));
        }

        var text = string.Concat(pages.Select(page => page.Text));
        Assert.Contains("Satış Siparişi SO-2026-000123", text, StringComparison.Ordinal);
        foreach (var line in lines)
        {
            Assert.Contains(line.ProductCode, text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// SAT-D10: instants are stored in UTC and printed in Europe/Istanbul local time, labelled — a UTC evening is the next
    /// business day in Istanbul, and an instant given with another offset is the same moment.
    /// </summary>
    [Fact]
    public async Task Times_are_printed_in_istanbul_local_time_with_their_label_never_as_utc()
    {
        var draftId = Guid.NewGuid();
        var (detail, pricing) = Order(draftId, SalesPriceDisclosure.Withheld);
        // 09:30 UTC given as 14:30 at +05:00: the same moment.
        var shifted = detail with
        {
            Approval = detail.Approval with { DecidedAtUtc = new DateTimeOffset(2026, 10, 27, 14, 30, 0, TimeSpan.FromHours(5)) },
        };
        using var host = CreateHost(new StubDetail(_ => Task.FromResult(shifted)), new StubPricing(_ => Task.FromResult(pricing)));
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PdfPath(draftId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await TextAsync(response);
        // 2026-10-26 21:15 UTC is already 27.10.2026 00:15 in Istanbul.
        Assert.Contains("SENTETIK-preparer · 27.10.2026 00:15 (İstanbul saati)", text, StringComparison.Ordinal);
        Assert.Contains("SENTETIK Onaylayan · 27.10.2026 12:30 (İstanbul saati)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UTC", text, StringComparison.Ordinal);
        Assert.DoesNotContain("26.10.2026 21:15", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_price_read_the_price_less_variant_draws_no_price_column_currency_or_value()
    {
        var draftId = Guid.NewGuid();
        var (detail, pricing) = Order(draftId, SalesPriceDisclosure.Withheld);
        using var host = CreateHost(new StubDetail(_ => Task.FromResult(detail)), new StubPricing(_ => Task.FromResult(pricing)));
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PdfPath(draftId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Satış_Siparişi_Fiyatsız_SO-2026-000123_SENTETIK_Gıda_2026-10-27_v02_Onaylı.pdf",
            response.Content.Headers.ContentDisposition!.FileNameStar);
        var text = await TextAsync(response);
        Assert.Contains("Sentetik bant", text, StringComparison.Ordinal);
        Assert.Contains("Fiyat bilgileri bu belgede yer almaz.", text, StringComparison.Ordinal);
        foreach (var priceText in new[] { "Birim fiyat", "İskonto", "Vade farkı", "Net birim fiyat", "Para birimi", "TRY", "12.50", "%5" })
        {
            Assert.DoesNotContain(priceText, text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The two reads disagree about disclosure only when the price read changed between them: the detail's withheld marker
    /// wins, so a price the pricing read still carried is never printed.
    /// </summary>
    [Fact]
    public async Task A_withheld_detail_line_wins_over_a_visible_pricing_answer_and_no_price_leaks()
    {
        var draftId = Guid.NewGuid();
        var (detail, _) = Order(draftId, SalesPriceDisclosure.Withheld);
        var (_, visible) = Order(draftId, SalesPriceDisclosure.Visible, unitPrice: "99.97");
        using var host = CreateHost(new StubDetail(_ => Task.FromResult(detail)), new StubPricing(_ => Task.FromResult(visible)));
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PdfPath(draftId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await TextAsync(response);
        Assert.DoesNotContain("99.97", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TRY", text, StringComparison.Ordinal);
        Assert.Contains("Fiyat bilgileri bu belgede yer almaz.", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(404, HttpStatusCode.NotFound)]
    [InlineData(403, HttpStatusCode.Forbidden)]
    [InlineData(503, HttpStatusCode.ServiceUnavailable)]
    public async Task A_detail_refusal_is_answered_code_less_and_the_price_read_is_never_asked(int status, HttpStatusCode expected)
    {
        var pricing = new StubPricing(_ => throw new InvalidOperationException("The price read must not be asked."));
        using var host = CreateHost(new StubDetail(_ => throw new SalesDraftServiceException(status, "SENTETIK-source")), pricing);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PdfPath(Guid.NewGuid()));

        Assert.Equal(expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.DoesNotContain("SENTETIK-source", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, pricing.Calls);
    }

    [Theory]
    [InlineData(403, "forbidden", HttpStatusCode.Forbidden)]
    [InlineData(404, "not_found", HttpStatusCode.NotFound)]
    [InlineData(503, "settings_provider_unavailable", HttpStatusCode.ServiceUnavailable)]
    [InlineData(422, "unsupported_currency", HttpStatusCode.ServiceUnavailable)]
    public async Task A_price_read_refusal_after_the_detail_is_never_a_pdf(int status, string code, HttpStatusCode expected)
    {
        var draftId = Guid.NewGuid();
        var (detail, _) = Order(draftId, SalesPriceDisclosure.Visible);
        using var host = CreateHost(new StubDetail(_ => Task.FromResult(detail)),
            new StubPricing(_ => throw new SalesDraftServiceException(status, code)));
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PdfPath(draftId));

        Assert.Equal(expected, response.StatusCode);
        Assert.NotEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Reads_of_two_different_versions_are_a_concurrent_change_not_a_mixed_document()
    {
        var draftId = Guid.NewGuid();
        var (detail, pricing) = Order(draftId, SalesPriceDisclosure.Visible);
        using var host = CreateHost(new StubDetail(_ => Task.FromResult(detail)),
            new StubPricing(_ => Task.FromResult(pricing with { Version = "8" })));
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PdfPath(draftId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(SalesDraftProblemCodesV1.ConcurrentChange, problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_answer_that_cannot_be_printed_faithfully_is_503_never_a_partial_pdf()
    {
        var draftId = Guid.NewGuid();
        var (detail, pricing) = Order(draftId, SalesPriceDisclosure.Visible);
        // A visible answer missing the line it must price, and a detail of another record.
        var incomplete = pricing with { Lines = [] };
        var otherRecord = detail with { Draft = detail.Draft with { Id = Guid.NewGuid() } };
        foreach (var (detailAnswer, pricingAnswer) in new[] { (detail, incomplete), (otherRecord, pricing) })
        {
            using var host = CreateHost(new StubDetail(_ => Task.FromResult(detailAnswer)), new StubPricing(_ => Task.FromResult(pricingAnswer)));
            using var client = CreateClient(host);

            using var response = await client.GetAsync(PdfPath(draftId));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
    }

    [Fact]
    public async Task An_anonymous_caller_or_a_company_outside_the_callers_access_reads_nothing()
    {
        var draftId = Guid.NewGuid();
        var detailService = new StubDetail(_ => Task.FromResult(Order(draftId, SalesPriceDisclosure.Visible).Detail));
        var pricing = new StubPricing(_ => Task.FromResult(Order(draftId, SalesPriceDisclosure.Visible).Pricing));
        using var host = CreateHost(detailService, pricing);

        using (var anonymous = host.CreateClient())
        {
            anonymous.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
            using var response = await anonymous.GetAsync(PdfPath(draftId));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var foreign = host.CreateClient())
        {
            foreign.DefaultRequestHeaders.Add("X-Development-User", TestUsers.CompanyB);
            foreign.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
            using var response = await foreign.GetAsync(PdfPath(draftId));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal((0, 0), (detailService.Calls, pricing.Calls));
    }

    private static string PdfPath(Guid draftId) => SalesDraftApiV1.OrderPdfPath.Replace("{id}", draftId.ToString("D"), StringComparison.Ordinal);

    private static async Task<string> TextAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var pdf = PdfDocument.Open(bytes);
        return string.Concat(pdf.GetPages().Select(page => page.Text));
    }

    /// <summary>An approved Rev.1 order of one priced line (4 kg at 12.50, 5 % discount, 0.25 surcharge) and its print pricing.</summary>
    private static (SalesOrderDraftReadDetailViewV1 Detail, SalesOrderPrintPricingV1 Pricing) Order(Guid draftId,
        SalesPriceDisclosure disclosure, string unitPrice = "12.50")
    {
        var productId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var visible = disclosure == SalesPriceDisclosure.Visible;
        var summary = new SalesOrderDraftSummaryView(
            draftId, CompanyA, "SO-2026-000123", "Order", "Draft", "7", "Rev.1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 10, 27), new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero), 1, "Approved",
            SalesCommercialStageV1.Order, [new SalesOrderDraftProductSnapshotViewV1(productId, "3", "GDC-001", "Sentetik bant", Kg, "kg")]);
        var line = new SalesOrderDraftLineDetail(
            lineId, 1, productId, "3", "GDC-001", "Sentetik bant", Kg, "4",
            visible ? unitPrice : null, visible ? Kg : null, new DateOnly(2026, 11, 3), "SENTETIK satır notu", null,
            visible ? "5" : null, disclosure, "kg");
        var detail = new SalesOrderDraftReadDetailViewV1(
            summary, new DateOnly(2026, 11, 3), "SENTETIK sipariş notu", [line], null, null, null, null, null, null,
            new SalesDraftApprovalV1("SENTETIK-preparer", new DateTimeOffset(2026, 10, 26, 21, 15, 0, TimeSpan.Zero),
                approver.ToString("D"), new DateTimeOffset(2026, 10, 27, 9, 30, 0, TimeSpan.Zero), null),
            new SalesDraftCustomerAcceptanceV1(false, null, null, null, false), false, false,
            new SalesPartyDisplayV1(summary.PartyId, "CAR-000042", "SENTETIK Gıda Sanayi A.Ş.", "SENTETIK Gıda"),
            [new SalesUserDisplayV1(approver, "SENTETIK Onaylayan", true)]);
        var pricing = visible
            ? new SalesOrderPrintPricingV1("7", SalesPriceDisclosure.Visible, "TRY",
                [new SalesDraftLinePricingV1(lineId, 1, unitPrice, Kg, "Manual", null, "5", null, null, "0.25", "12.125", null)])
            : new SalesOrderPrintPricingV1("7", SalesPriceDisclosure.Withheld, null, []);
        return (detail, pricing);
    }

    private WebApplicationFactory<Program> CreateHost(ISalesDraftReadServiceV1 detail, ISalesOrderPrintReadServiceV1 pricing) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesDraftReadServiceV1>();
            services.AddSingleton(detail);
            services.RemoveAll<ISalesOrderPrintReadServiceV1>();
            services.AddSingleton(pricing);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        return client;
    }

    private sealed class StubDetail(Func<Guid, Task<SalesOrderDraftReadDetailViewV1>> detail) : ISalesDraftReadServiceV1
    {
        public int Calls { get; private set; }

        public Task<SalesOrderDraftReadDetailViewV1> GetDraftDetailAsync(Guid draftId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return detail(draftId);
        }

        public Task<SalesOrderDraftReadListResponseV1> GetDraftListAsync(SalesOrderDraftListQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubPricing(Func<Guid, Task<SalesOrderPrintPricingV1>> pricing) : ISalesOrderPrintReadServiceV1
    {
        public int Calls { get; private set; }

        public Task<SalesOrderPrintPricingV1> GetOrderPrintPricingAsync(Guid draftId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return pricing(draftId);
        }
    }
}
