using System.Net;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP13 (YOL-D11): <c>GET /api/v1/sales/drafts/{id}/contract-pdf</c> — the order contract sent to the customer for a wet signature.
/// It is the order print's reads, authority and figures laid out with the contract title, file-name type, footer and the customer
/// approval block; the PDF text is read back from the bytes (PdfPig) and is the same on every rendering of the same answers.
/// </summary>
public sealed partial class SalesOrderPdfHttpAcceptanceTests
{
    [Fact]
    public async Task The_order_contract_prints_the_order_figures_with_the_contract_title_footer_and_customer_approval_block()
    {
        var draftId = Guid.NewGuid();
        var (detail, pricing) = Order(draftId, SalesPriceDisclosure.Visible);
        using var host = CreateHost(new StubDetail(_ => Task.FromResult(detail)), new StubPricing(_ => Task.FromResult(pricing)));
        using var client = CreateClient(host);

        using var response = await client.GetAsync(ContractPdfPath(draftId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("Sipariş_Sözleşmesi_SO-2026-000123_SENTETIK_Gıda_2026-10-27_v02_Onaylı.pdf",
            response.Content.Headers.ContentDisposition!.FileNameStar);

        var text = await TextAsync(response);
        AssertInOrder(text,
        [
            "Sipariş Sözleşmesi SO-2026-000123",
            "CAR-000042 — SENTETIK Gıda Sanayi A.Ş. (SENTETIK Gıda)",
            "GDC-001",
            "Ödenecek tutar53.35 TRY",
            "Müşteri Onayı",
            "Bu sözleşme siparişin Rev.1 revizyonu içindir. İmzalı onay belgesi siparişe yüklenir.",
            "Ad Soyad / Unvan",
            "Tarih",
            "İmza / Kaşe",
        ]);
        Assert.Contains("SO-2026-000123 · Rev.1 · Sipariş sözleşmesi; müşteri onayı için imzalanır.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("resmî belge değildir", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Satış Siparişi SO-2026-000123", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TASLAK", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_order_contract_text_is_deterministic_for_the_same_answers_and_the_order_print_is_unchanged()
    {
        var draftId = Guid.NewGuid();
        var (detail, pricing) = Order(draftId, SalesPriceDisclosure.Withheld);
        using var host = CreateHost(new StubDetail(_ => Task.FromResult(detail)), new StubPricing(_ => Task.FromResult(pricing)));
        using var client = CreateClient(host);

        using var first = await client.GetAsync(ContractPdfPath(draftId));
        using var second = await client.GetAsync(ContractPdfPath(draftId));
        using var order = await client.GetAsync(PdfPath(draftId));

        var firstText = await TextAsync(first);
        Assert.Equal(firstText, await TextAsync(second));
        Assert.Contains("Fiyat bilgileri bu belgede yer almaz.", firstText, StringComparison.Ordinal);
        Assert.Equal("Sipariş_Sözleşmesi_Fiyatsız_SO-2026-000123_SENTETIK_Gıda_2026-10-27_v02_Onaylı.pdf",
            first.Content.Headers.ContentDisposition!.FileNameStar);
        var orderText = await TextAsync(order);
        Assert.Contains("Satış Siparişi SO-2026-000123", orderText, StringComparison.Ordinal);
        Assert.DoesNotContain("Müşteri Onayı", orderText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_order_contract_reads_nothing_for_an_anonymous_caller_or_another_company_and_a_refusal_is_code_less()
    {
        var draftId = Guid.NewGuid();
        var detailService = new StubDetail(_ => Task.FromResult(Order(draftId, SalesPriceDisclosure.Visible).Detail));
        var pricing = new StubPricing(_ => Task.FromResult(Order(draftId, SalesPriceDisclosure.Visible).Pricing));
        using var host = CreateHost(detailService, pricing);

        using (var anonymous = host.CreateClient())
        {
            anonymous.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
            using var response = await anonymous.GetAsync(ContractPdfPath(draftId));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var foreign = host.CreateClient())
        {
            foreign.DefaultRequestHeaders.Add("X-Development-User", TestUsers.CompanyB);
            foreign.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
            using var response = await foreign.GetAsync(ContractPdfPath(draftId));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal((0, 0), (detailService.Calls, pricing.Calls));

        using var refusingHost = CreateHost(new StubDetail(_ => throw new SalesDraftServiceException(403, "SENTETIK-source")),
            new StubPricing(_ => throw new InvalidOperationException("The price read must not be asked.")));
        using var client = CreateClient(refusingHost);
        using var refused = await client.GetAsync(ContractPdfPath(draftId));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.DoesNotContain("SENTETIK-source", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static string ContractPdfPath(Guid draftId) => PdfPath(draftId)[..^"/pdf".Length] + "/contract-pdf";
}
