using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FNS-D76 / KLN-D104: the approved manual official sales invoice and a CSV v2 import document with the same official
/// number or ETTN are compared field by field, exactly; the manual record wins and every disagreement is named in a
/// stable order for the audit warning. SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinanceOfficialInvoiceSourceComparisonTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Lira = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Dollar = Guid.Parse("23232323-2323-2323-2323-232323232323");
    private static readonly Guid Customer = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherCustomer = Guid.Parse("34343434-3434-3434-3434-343434343434");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Batch = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Document = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ManualInvoice = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private const string OfficialNo = "GYM2026000000001";
    private const string Ettn = "8a8a8a8a-8a8a-4a8a-8a8a-8a8a8a8a8a8a";

    [Fact]
    public void Missing_source_TL_is_a_named_difference_and_is_never_zero_or_the_manual_value()
    {
        var imported = Assert.Single(Imported(Installments(("1000", Customer, Lira)), OfficialNo, Ettn))
            with { PayableFunctionalAmount = null };
        var difference = Assert.Single(FinanceOfficialInvoiceSourceComparison.Compare([Manual()], [imported]));
        Assert.Equal(new[] { FinanceOfficialInvoiceSourceComparison.PayableFunctionalAmountField }, difference.Fields);
        Assert.Null(difference.Imported.PayableFunctionalAmount);
    }

    [Fact]
    public void A_matching_import_document_is_not_a_difference_even_with_another_decimal_scale()
    {
        var imported = Imported(Installments(("600.00", Customer, Lira), ("400", Customer, Lira)), OfficialNo, Ettn,
            withholding: "0.00");

        var differences = FinanceOfficialInvoiceSourceComparison.Compare(new[] { Manual() }, imported);

        Assert.Empty(differences);
        Assert.Equal("1000.00", Assert.Single(imported).PayableAmount);
    }

    [Fact]
    public void Every_disagreeing_field_is_named_in_a_stable_order()
    {
        var imported = Imported(Installments(("999.99", OtherCustomer, Dollar)), OfficialNo,
            "9b9b9b9b-9b9b-4b9b-9b9b-9b9b9b9b9b9b", taxBase: "800", vat: "160", withholding: "1");

        var difference = Assert.Single(FinanceOfficialInvoiceSourceComparison.Compare(new[] { Manual() }, imported));

        Assert.Equal(ManualInvoice, difference.ManualInvoiceId);
        Assert.Equal(
            new[]
            {
                FinanceOfficialInvoiceSourceComparison.EttnField,
                FinanceOfficialInvoiceSourceComparison.CustomerPartyField,
                FinanceOfficialInvoiceSourceComparison.CurrencyField,
                FinanceOfficialInvoiceSourceComparison.PayableAmountField,
                FinanceOfficialInvoiceSourceComparison.TaxBaseField,
                FinanceOfficialInvoiceSourceComparison.VatField,
                FinanceOfficialInvoiceSourceComparison.WithholdingField,
            },
            difference.Fields);
    }

    [Fact]
    public void A_record_sharing_only_the_ETTN_is_compared_and_its_number_is_a_difference()
    {
        var imported = Imported(Installments(("1000", Customer, Lira)), "GYM2026000000002", Ettn);

        var difference = Assert.Single(FinanceOfficialInvoiceSourceComparison.Compare(new[] { Manual() }, imported));

        Assert.Equal(new[] { FinanceOfficialInvoiceSourceComparison.OfficialNoField }, difference.Fields);
    }

    [Fact]
    public void Installments_that_disagree_on_customer_or_currency_or_cannot_be_read_never_compare_equal()
    {
        var mixedImport = Imported(Installments(("600", Customer, Lira), ("400", OtherCustomer, Dollar)),
            OfficialNo, Ettn);
        var unreadableImport = Imported(Installments(("600", Customer, Lira), ("4,00", Customer, Lira)),
            OfficialNo, Ettn);

        var mixed = Assert.Single(FinanceOfficialInvoiceSourceComparison.Compare(new[] { Manual() }, mixedImport));
        var broken = Assert.Single(FinanceOfficialInvoiceSourceComparison.Compare(new[] { Manual() }, unreadableImport));

        Assert.Equal(
            new[]
            {
                FinanceOfficialInvoiceSourceComparison.CustomerPartyField,
                FinanceOfficialInvoiceSourceComparison.CurrencyField,
            },
            mixed.Fields);
        Assert.Null(Assert.Single(unreadableImport).PayableAmount);
        Assert.Equal(new[] { FinanceOfficialInvoiceSourceComparison.PayableAmountField }, broken.Fields);
    }

    [Fact]
    public void An_import_document_without_a_manual_record_on_its_number_or_ETTN_is_not_compared()
    {
        var imported = Imported(Installments(("1", OtherCustomer, Dollar)), "GYM2026000000003",
            "9c9c9c9c-9c9c-4c9c-9c9c-9c9c9c9c9c9c");

        Assert.Empty(FinanceOfficialInvoiceSourceComparison.Compare(new[] { Manual() }, imported));
    }

    private static FinanceOfficialSalesInvoiceRow Manual() => new()
    {
        Id = ManualInvoice,
        CompanyId = Company,
        OfficialNo = OfficialNo,
        Ettn = Ettn,
        CustomerPartyId = Customer,
        CurrencyId = Lira,
        PayableAmountText = "1000",
        PayableFunctionalAmountText = "1000",
        TaxBaseFunctionalAmountText = "847.46",
        VatFunctionalAmountText = "152.54",
        WithholdingFunctionalAmountText = "0",
        Status = (int)FinanceOfficialSalesInvoiceStatusV1.Approved,
        Version = 2,
    };

    private static (string Amount, Guid Party, Guid Currency)[] Installments(
        params (string Amount, Guid Party, Guid Currency)[] installments) => installments;

    private static IReadOnlyList<FinanceOfficialInvoiceSourceComparison.ImportedDocument> Imported(
        (string Amount, Guid Party, Guid Currency)[] installments,
        string officialNo,
        string ettn,
        string taxBase = "847.46",
        string vat = "152.54",
        string withholding = "0")
    {
        var rows = installments
            .Select((installment, index) => new ApprovedFinanceOpenItemSourceRow(
                Guid.NewGuid(), Document, Guid.NewGuid(), "1", "sales-invoice", installment.Party, Account,
                installment.Currency, installment.Amount, new DateOnly(2026, 12, 1).AddDays(index)))
            .ToArray();
        var batch = new ApprovedFinanceOpenItemSourceBatch(Batch, Company, "1", "checksum", rows);
        return FinanceOfficialInvoiceSourceComparison.Imported(batch,
            new[] { new FinanceOfficialSalesInvoiceImportDocumentV2(Document, officialNo, ettn, taxBase, vat, withholding,
                PayableFunctionalAmount: "1000") });
    }
}
