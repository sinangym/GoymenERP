using System.Net;
using System.Reflection;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UglyToad.PdfPig;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP13 (DALGA_PLANI_v2 1.14): <c>GET /api/v1/sales/quotes/{id}/pdf</c> over SENTETIK Sales, Cari and Settings reads (no database):
/// the PDF text is read back from the bytes (PdfPig) and is the same for the same answers; the quoted prices are the quote's own
/// texts and appear only when the quote read disclosed them; no total is computed; a lost quote carries its watermark; two versions
/// are a conflict; a refusal is code-less; an anonymous caller or another company reaches no owner. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesQuotePdfHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid Kg = Guid.Parse("6a1f0c3e-0d5b-4e7c-9f21-3b4c5d6e7f80");
    private static readonly Guid Party = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly Guid Currency = Guid.Parse("9e60dcf0-f4bd-5bca-b464-f9a2db98e31a");

    [Fact]
    public async Task The_priced_quote_prints_the_customer_lines_and_quoted_prices_verbatim_and_no_total()
    {
        var quoteId = Guid.NewGuid();
        using var host = CreateHost(Quotes(_ => Quote(quoteId, SalesPriceDisclosure.Visible)));
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PdfPath(quoteId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal("Satış_Teklifi_SQ-2026-000005_SENTETIK_Gıda_2026-11-20_v02_Açık.pdf",
            response.Content.Headers.ContentDisposition!.FileNameStar);
        var text = await TextAsync(response);
        Assert.Contains("Satış Teklifi SQ-2026-000005", text, StringComparison.Ordinal);
        Assert.Contains("CAR-000042 — SENTETIK Gıda Sanayi A.Ş. (SENTETIK Gıda)", text, StringComparison.Ordinal);
        Assert.Contains("Geçerlilik05.12.2026", text, StringComparison.Ordinal);
        Assert.Contains("Para birimiTRY", text, StringComparison.Ordinal);
        Assert.Contains("GDC-001", text, StringComparison.Ordinal);
        Assert.Contains("Sentetik bant", text, StringComparison.Ordinal);
        Assert.Contains("12.50 TRY / kg", text, StringComparison.Ordinal);
        Assert.Contains("%5", text, StringComparison.Ordinal);
        Assert.Contains("Teklif toplamı bu belgede yer almaz", text, StringComparison.Ordinal);
        Assert.Contains("SQ-2026-000005 · Rev.1 · Bilgi amaçlı teknik çıktıdır; resmî belge değildir.", text, StringComparison.Ordinal);
        // Nothing is computed: 4 kg × 12.50 is never printed as an amount.
        Assert.DoesNotContain("50.00", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_withheld_quote_draws_no_price_or_currency_and_the_text_is_deterministic()
    {
        var quoteId = Guid.NewGuid();
        using var host = CreateHost(Quotes(_ => Quote(quoteId, SalesPriceDisclosure.Withheld)));
        using var client = CreateClient(host);

        using var first = await client.GetAsync(PdfPath(quoteId));
        using var second = await client.GetAsync(PdfPath(quoteId));

        var text = await TextAsync(first);
        Assert.Equal(text, await TextAsync(second));
        Assert.Contains("Fiyat bilgileri bu belgede yer almaz.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TRY", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Birim fiyat", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lost_quote_carries_its_watermark_and_two_versions_are_a_conflict()
    {
        var quoteId = Guid.NewGuid();
        var lost = Quote(quoteId, SalesPriceDisclosure.Withheld);
        lost = lost with { Quote = lost.Quote with { State = "Lost" } };
        using (var host = CreateHost(Quotes(_ => lost)))
        using (var client = CreateClient(host))
        using (var response = await client.GetAsync(PdfPath(quoteId)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
            Assert.All(pdf.GetPages(), page => Assert.Equal("KAYBEDİLDİ",
                string.Concat(page.Letters.Where(letter => letter.PointSize > 100).Select(letter => letter.Value))));
        }

        var calls = 0;
        using (var host = CreateHost(Quotes(_ =>
               {
                   var quote = Quote(quoteId, SalesPriceDisclosure.Withheld);
                   return ++calls == 1 ? quote : quote with { Quote = quote.Quote with { Version = "9" } };
               })))
        using (var client = CreateClient(host))
        using (var response = await client.GetAsync(PdfPath(quoteId)))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(403, HttpStatusCode.Forbidden)]
    [InlineData(404, HttpStatusCode.NotFound)]
    [InlineData(503, HttpStatusCode.ServiceUnavailable)]
    public async Task A_quote_refusal_is_code_less_and_never_a_pdf(int status, HttpStatusCode expected)
    {
        using var host = CreateHost(Quotes(_ => throw new SalesDraftServiceException(status, "SENTETIK-source")));
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PdfPath(Guid.NewGuid()));

        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("SENTETIK-source", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_anonymous_caller_or_another_company_reaches_no_owner()
    {
        var calls = 0;
        using var host = CreateHost(Quotes(_ =>
        {
            calls++;
            throw new InvalidOperationException("The quote must not be read.");
        }));

        using (var anonymous = host.CreateClient())
        {
            anonymous.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
            using var response = await anonymous.GetAsync(PdfPath(Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var foreign = host.CreateClient())
        {
            foreign.DefaultRequestHeaders.Add("X-Development-User", TestUsers.CompanyB);
            foreign.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
            using var response = await foreign.GetAsync(PdfPath(Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(0, calls);
    }

    private static string PdfPath(Guid quoteId) => $"{SalesQuoteApiV1.QuotePath}/{quoteId:D}/pdf";

    private static async Task<string> TextAsync(HttpResponseMessage response)
    {
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return string.Concat(pdf.GetPages().Select(page => page.Text));
    }

    private static SalesQuoteDetailView Quote(Guid quoteId, SalesPriceDisclosure disclosure)
    {
        var visible = disclosure == SalesPriceDisclosure.Visible;
        var summary = new SalesQuoteSummaryView(quoteId, CompanyA, "SQ-2026-000005", "Open", "3", "Rev.1", Party, Guid.NewGuid(), Currency,
            new DateOnly(2026, 11, 20), new DateOnly(2026, 12, 5), DateTimeOffset.UnixEpoch, 1, null);
        var line = new SalesQuoteLineView(Guid.NewGuid(), 1, Guid.NewGuid(), "7", "GDC-001", "Sentetik bant", Kg, "kg", "4",
            visible ? "12.50" : null, visible ? Kg : null, visible ? "5" : null, disclosure, new DateOnly(2026, 12, 1), null);
        return new SalesQuoteDetailView(summary, [], new DateOnly(2026, 12, 1), null, [line], null, null, "SENTETIK-seller", [], [],
            new SalesQuoteExpiryV1(new DateOnly(2026, 11, 22), false, false));
    }

    private WebApplicationFactory<Program> CreateHost(ISalesQuoteService quotes) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesQuoteService>();
            services.AddSingleton(quotes);
            services.RemoveAll<ISalesPartyDisplaySourceV1>();
            services.AddSingleton<ISalesPartyDisplaySourceV1, Parties>();
            services.RemoveAll<ISettingsPrerequisiteProvider>();
            services.AddSingleton(Proxy<ISettingsPrerequisiteProvider>(nameof(ISettingsPrerequisiteProvider.GetMoneyProfileAsync),
                args => Task.FromResult(new MoneyProfileV1(CompanyA, (Guid)args[0]!, "TRY", (string)args[1]!,
                    new ProfileVersionV1(Guid.NewGuid(), 1), 2, true, new DateOnly(2026, 1, 1), null))));
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

    private static ISalesQuoteService Quotes(Func<Guid, SalesQuoteDetailView> detail) =>
        Proxy<ISalesQuoteService>(nameof(ISalesQuoteService.GetQuoteDetailAsync), args => Task.FromResult(detail((Guid)args[0]!)));

    private static T Proxy<T>(string method, Func<object?[], object> answer) where T : class
    {
        var proxy = DispatchProxy.Create<T, OneMethodProxy>();
        var handler = (OneMethodProxy)(object)proxy;
        handler.Method = method;
        handler.Answer = answer;
        return proxy;
    }

    public class OneMethodProxy : DispatchProxy
    {
        public string Method { get; set; } = string.Empty;
        public Func<object?[], object> Answer { get; set; } = _ => throw new InvalidOperationException();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == Method ? Answer(args ?? []) : throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class Parties : ISalesPartyDisplaySourceV1
    {
        public Task<SalesPartyDisplayBatchAnswerV1> GetAuthorizedBatchAsync(SalesPartyDisplayBatchRequestV1 request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesPartyDisplayBatchAnswerV1(SalesPartyDisplayBatchOutcomeV1.Success,
                [.. request.PartyIds.Select(id => new SalesPartyDisplayV1(id, "CAR-000042", "SENTETIK Gıda Sanayi A.Ş.", "SENTETIK Gıda"))]));
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
