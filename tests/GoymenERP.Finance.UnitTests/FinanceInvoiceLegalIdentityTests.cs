using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceInvoiceLegalIdentityTests
{
    [Theory]
    [InlineData(null, "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")]
    [InlineData("SNT1", null)]
    [InlineData(" ", "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")]
    [InlineData(" SNT1", "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")]
    [InlineData("SNT\n1", "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")]
    [InlineData("SNT1", "not-ettn")]
    [InlineData("SNT1", "00000000-0000-0000-0000-000000000000")]
    public void Incomplete_or_malformed_owner_identity_is_not_inferred(string? number, string? ettn)
    {
        Assert.NotEmpty(FinanceInvoiceOpenItemAdmissionService.OfficialIdentity(Invoice() with { OfficialInvoiceNo = number, Ettn = ettn }, out var document));
        Assert.Null(document);
    }

    [Fact]
    public void Native_identity_uses_the_existing_exact_official_number_and_canonical_ETTN_rules_only_for_sales()
    {
        var invoice = Invoice() with { OfficialInvoiceNo = "SNT1", Ettn = "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA" };
        Assert.Empty(FinanceInvoiceOpenItemAdmissionService.OfficialIdentity(invoice, out var document));
        Assert.Equal((invoice.InvoiceId, "SNT1", "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            (document!.SourceDocumentId, document.OfficialNo, document.Ettn));
        Assert.NotEmpty(FinanceInvoiceOpenItemAdmissionService.OfficialIdentity(invoice with { Producer = FinanceInvoiceProducerV1.Purchasing }, out _));
    }

    [Fact]
    public void Legacy_absent_proof_remains_absent_on_the_wire_and_never_fabricates_a_legal_identity()
    {
        var invoice = Invoice();
        Assert.Empty(FinanceInvoiceOpenItemAdmissionService.OfficialIdentity(invoice, out var document));
        Assert.Null(document);
        var json = JsonSerializer.Serialize(invoice, FinanceJson.CreateOptions());
        Assert.DoesNotContain("officialInvoiceNo", json); Assert.DoesNotContain("ettn", json);
    }

    private static FinanceInvoiceOpenItemAdmissionV1 Invoice() => new(Guid.NewGuid(), FinanceInvoiceProducerV1.Sales,
        Guid.NewGuid(), "1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
        [new(Guid.NewGuid(), Guid.NewGuid(), 1, new(2026, 11, 30), "100.00", null)]);
}
