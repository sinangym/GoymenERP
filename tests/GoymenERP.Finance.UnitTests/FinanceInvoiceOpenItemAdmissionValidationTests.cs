using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// F2a / MOD-04: the pure shape rules of the invoice admission port. The producer fixes the source kind and the
/// direction (MOD-15); installments are a numbered plan with positive exact amounts and due dates; a foreign-currency
/// invoice carries one rate and a functional amount on every installment, a TL invoice neither. SENTETIK values only.
/// </summary>
public sealed class FinanceInvoiceOpenItemAdmissionValidationTests
{
    [Theory]
    [InlineData(FinanceInvoiceProducerV1.Sales, FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
        FinanceOpenItemDirectionCodes.Receivable)]
    [InlineData(FinanceInvoiceProducerV1.Purchasing, FinanceOpenItemSourceKinds.ApprovedOfficialIncomingInvoiceInstallment,
        FinanceOpenItemDirectionCodes.Payable)]
    public void The_producer_fixes_the_kind_and_direction(FinanceInvoiceProducerV1 producer, string kind, int direction)
    {
        var issues = FinanceInvoiceOpenItemAdmissionService.Validate(Invoice(producer), out var actualKind,
            out var actualDirection);

        Assert.Empty(issues);
        Assert.Equal(kind, actualKind);
        Assert.Equal(direction, actualDirection);
    }

    [Fact]
    public void An_unknown_producer_is_refused()
    {
        var issues = FinanceInvoiceOpenItemAdmissionService.Validate(Invoice((FinanceInvoiceProducerV1)9), out _, out _);

        Assert.Contains(issues, issue => issue.Code == "finance_invoice_producer_invalid");
    }

    [Fact]
    public void A_foreign_currency_invoice_carries_a_rate_and_a_functional_amount_on_every_installment()
    {
        var invoice = Invoice(FinanceInvoiceProducerV1.Purchasing, "38.4512",
            Installment(1, "100.00", "3845.12"), Installment(2, "50.00", "1922.56"));

        Assert.Empty(FinanceInvoiceOpenItemAdmissionService.Validate(invoice, out _, out _));
    }

    [Theory]
    [InlineData("38.45", null)]
    [InlineData(null, "3845.12")]
    [InlineData("0", "3845.12")]
    [InlineData("-1", "3845.12")]
    [InlineData("38.45", "0")]
    [InlineData("abc", "3845.12")]
    [InlineData("1.55555555555555555555555555555555555555555555555555555555555555555", "3845.12")]
    public void Exchange_data_is_all_or_none_and_positive(string? rate, string? functional)
    {
        var issues = FinanceInvoiceOpenItemAdmissionService.Validate(
            Invoice(FinanceInvoiceProducerV1.Sales, rate, Installment(1, "100.00", functional)), out _, out _);

        Assert.Contains(issues, issue => issue.Code is "finance_invoice_exchange_rate_invalid"
            or "finance_invoice_functional_amount_invalid");
    }

    [Fact]
    public void Installments_are_required_numbered_unique_dated_and_positive()
    {
        Assert.Contains(FinanceInvoiceOpenItemAdmissionService.Validate(
                Invoice(FinanceInvoiceProducerV1.Sales, null, []), out _, out _),
            issue => issue.Code == "finance_invoice_installments_required");

        var first = Installment(1, "100.00");
        var issues = FinanceInvoiceOpenItemAdmissionService.Validate(Invoice(FinanceInvoiceProducerV1.Sales, null,
            first,
            first with { Sequence = 2 },
            Installment(4, "10.00"),
            Installment(4, "0") with { DueOn = default }), out _, out _);

        Assert.Contains(issues, issue => issue.Field == "installments[1]" && issue.Code == "finance_invoice_installment_duplicate");
        Assert.Contains(issues, issue => issue.Field == "installments[2].sequence");
        Assert.Contains(issues, issue => issue.Field == "installments[3].dueOn");
        Assert.Contains(issues, issue => issue.Field == "installments[3].amount");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" v1")]
    [InlineData("v 1")]
    public void The_invoice_version_is_a_token(string version)
    {
        var issues = FinanceInvoiceOpenItemAdmissionService.Validate(
            Invoice(FinanceInvoiceProducerV1.Sales) with { InvoiceVersion = version }, out _, out _);

        Assert.Contains(issues, issue => issue.Code == "finance_invoice_version_invalid");
    }

    [Fact]
    public void Every_identity_is_required()
    {
        var issues = FinanceInvoiceOpenItemAdmissionService.Validate(
            Invoice(FinanceInvoiceProducerV1.Sales) with { PartyId = Guid.Empty }, out _, out _);

        Assert.Contains(issues, issue => issue.Field == "invoice" && issue.Code == "identity_required");
    }

    private static FinanceInvoiceOpenItemAdmissionV1 Invoice(FinanceInvoiceProducerV1 producer) =>
        Invoice(producer, null, Installment(1, "100.00"));

    private static FinanceInvoiceOpenItemAdmissionV1 Invoice(FinanceInvoiceProducerV1 producer, string? rate,
        params FinanceInvoiceInstallmentV1[] installments) =>
        new(Guid.NewGuid(), producer, Guid.NewGuid(), "invoice-v1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), rate,
            installments);

    private static FinanceInvoiceInstallmentV1 Installment(int sequence, string amount, string? functional = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), sequence, new DateOnly(2026, 12, 1).AddMonths(sequence - 1), amount, functional);
}
