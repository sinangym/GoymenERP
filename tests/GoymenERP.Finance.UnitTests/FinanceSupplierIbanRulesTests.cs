using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// B-17 (FIN-S12): the one classification of a party account's supplier IBAN behind
/// <see cref="IFinancePartyAccountPortV2"/>. A non-supplier is never asked for one; a supplier whose owner cannot
/// answer is SourceUnavailable (never "no IBAN"); an approved value is normalized and checked for STRUCTURE only
/// (K2), and a malformed value is not released. The IBANs are published ISO 13616 examples: <b>SENTETIK</b>.
/// </summary>
public sealed class FinanceSupplierIbanRulesTests
{
    private const string ValidTr = "TR330006100519786457841326";
    private const string ValidGb = "GB82WEST12345698765432";

    [Theory]
    [InlineData(FinancePartyAccountRoles.None)]
    [InlineData(FinancePartyAccountRoles.Customer)]
    [InlineData(FinancePartyAccountRoles.Customer | FinancePartyAccountRoles.Tolling | FinancePartyAccountRoles.Service)]
    public void A_party_account_without_the_supplier_role_is_not_asked_for_a_supplier_iban(FinancePartyAccountRoles roles)
    {
        // Even a valid approved value is not released for a non-supplier account.
        Assert.Equal(new FinanceSupplierIbanView(FinanceSupplierIbanState.NotApplicable, null),
            FinanceSupplierIbanRules.Classify(roles, new FinanceSupplierIbanSourceAnswer(true, ValidTr)));
    }

    [Theory]
    [InlineData(FinancePartyAccountRoles.Supplier)]
    [InlineData(FinancePartyAccountRoles.Customer | FinancePartyAccountRoles.Supplier)]
    public void A_supplier_whose_owner_cannot_answer_is_source_unavailable_not_unrecorded(FinancePartyAccountRoles roles)
    {
        Assert.Equal(new FinanceSupplierIbanView(FinanceSupplierIbanState.SourceUnavailable, null),
            FinanceSupplierIbanRules.Classify(roles, FinanceSupplierIbanSourceAnswer.Unavailable));
        // An unavailable source carries no value even if one was handed with it.
        Assert.Equal(new FinanceSupplierIbanView(FinanceSupplierIbanState.SourceUnavailable, null),
            FinanceSupplierIbanRules.Classify(roles, new FinanceSupplierIbanSourceAnswer(false, ValidTr)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_supplier_the_owner_holds_no_approved_iban_for_is_not_recorded(string? approved)
    {
        Assert.Equal(new FinanceSupplierIbanView(FinanceSupplierIbanState.NotRecorded, null),
            FinanceSupplierIbanRules.Classify(FinancePartyAccountRoles.Supplier,
                new FinanceSupplierIbanSourceAnswer(true, approved)));
    }

    [Theory]
    [InlineData(ValidTr, ValidTr)]
    [InlineData("tr33 0006 1005 1978 6457 8413 26", ValidTr)]
    [InlineData("GB82-WEST-1234-5698-7654-32", ValidGb)]
    public void An_approved_structurally_valid_iban_is_released_in_its_normalized_form(string approved, string normalized)
    {
        Assert.Equal(new FinanceSupplierIbanView(FinanceSupplierIbanState.StructurallyValid, normalized),
            FinanceSupplierIbanRules.Classify(FinancePartyAccountRoles.Customer | FinancePartyAccountRoles.Supplier,
                new FinanceSupplierIbanSourceAnswer(true, approved)));
    }

    [Theory]
    [InlineData("TR330006100519786457841327")]
    [InlineData("GB82WEST1234")]
    [InlineData("33TR0006100519786457841326")]
    public void A_malformed_approved_value_is_structurally_invalid_and_not_released(string approved)
    {
        Assert.Equal(new FinanceSupplierIbanView(FinanceSupplierIbanState.StructurallyInvalid, null),
            FinanceSupplierIbanRules.Classify(FinancePartyAccountRoles.Supplier,
                new FinanceSupplierIbanSourceAnswer(true, approved)));
    }

    [Fact]
    public void A_missing_owner_answer_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FinanceSupplierIbanRules.Classify(FinancePartyAccountRoles.Supplier, null!));
    }

    [Theory]
    [InlineData(FinancePartyAccountRoles.None, false, false)]
    [InlineData(FinancePartyAccountRoles.Customer, true, false)]
    [InlineData(FinancePartyAccountRoles.Supplier, false, true)]
    [InlineData(FinancePartyAccountRoles.Customer | FinancePartyAccountRoles.Supplier, true, true)]
    [InlineData(FinancePartyAccountRoles.Tolling | FinancePartyAccountRoles.Service, false, false)]
    public void The_eligibility_reads_customer_supplier_and_dual_role_from_the_flags(FinancePartyAccountRoles roles,
        bool customer, bool supplier)
    {
        var party = new Guid("c0000000-0000-4000-8000-000000000001");
        var account = new Guid("c1000000-0000-4000-8000-000000000001");
        var company = new Guid("10000000-0000-0000-0000-000000000001");
        var eligibility = new PartyAccountEligibilityV2(party, account, company, true, true, roles,
            new DraftPartyLabel(party, account, "C-0001", "SENTETIK Cari A.Ş.", null, "1", "1"),
            FinanceSupplierIbanRules.Classify(roles, FinanceSupplierIbanSourceAnswer.Unavailable));

        Assert.Equal(customer, eligibility.IsCustomer);
        Assert.Equal(supplier, eligibility.IsSupplier);
        Assert.Equal(customer && supplier, eligibility.IsCustomerAndSupplier);
    }
}
