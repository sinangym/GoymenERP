using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// The fail-closed input-issue guard: legacy/company input stays company-owned and company-custodied; a commercial
/// input is owned by its exact captured Counterparty party and remains custodied by the context company.
/// </summary>
public sealed class ManufacturingCustomerOwnedInputGuardTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Customer = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid Location = Guid.Parse("40000000-0000-0000-0000-000000000004");

    [Fact]
    public void The_companys_own_company_custodied_position_is_admitted()
    {
        Assert.True(ManufacturingCustomerOwnedInputGuardV1.Admits(Company, Position()));
    }

    [Fact]
    public void A_customer_owned_company_custodied_position_is_admitted_only_with_its_exact_snapshot()
    {
        var position = Position(ownerKind: "Counterparty", ownerId: Customer);

        Assert.True(ManufacturingCustomerOwnedInputGuardV1.Admits(Company, position, Commercial()));
        Assert.False(ManufacturingCustomerOwnedInputGuardV1.Admits(Company, position));
        Assert.False(ManufacturingCustomerOwnedInputGuardV1.Admits(Company, position,
            Commercial(partyId: Guid.Parse("30000000-0000-0000-0000-000000000099"))));
    }

    [Theory]
    [InlineData("customer-owned-and-custodied")]
    [InlineData("third-party-custodied")]
    [InlineData("another-companys-stock")]
    [InlineData("custodied-by-another-company")]
    [InlineData("unknown-owner-kind")]
    [InlineData("unknown-custodian-kind")]
    [InlineData("kind-in-another-case")]
    public void Any_other_owner_or_custodian_is_refused_for_a_company_input(string shape)
    {
        var position = shape switch
        {
            "customer-owned-and-custodied" => Position(ownerKind: "Counterparty", ownerId: Customer,
                custodianKind: "Counterparty", custodianId: Customer),
            "third-party-custodied" => Position(custodianKind: "Counterparty", custodianId: Customer),
            "another-companys-stock" => Position(ownerId: OtherCompany),
            "custodied-by-another-company" => Position(custodianId: OtherCompany),
            "unknown-owner-kind" => Position(ownerKind: "Supplier"),
            "unknown-custodian-kind" => Position(custodianKind: ""),
            _ => Position(ownerKind: "company"),
        };

        Assert.False(ManufacturingCustomerOwnedInputGuardV1.Admits(Company, position));
    }

    [Theory]
    [InlineData("another-customer")]
    [InlineData("counterparty-custodian")]
    [InlineData("foreign-company-custodian")]
    [InlineData("company-owner")]
    [InlineData("unknown-owner")]
    public void A_commercial_input_refuses_any_position_outside_its_captured_party_and_company_custody(string shape)
    {
        var position = shape switch
        {
            "another-customer" => Position(ownerKind: "Counterparty", ownerId: Guid.NewGuid()),
            "counterparty-custodian" => Position(ownerKind: "Counterparty", ownerId: Customer,
                custodianKind: "Counterparty", custodianId: Customer),
            "foreign-company-custodian" => Position(ownerKind: "Counterparty", ownerId: Customer,
                custodianId: OtherCompany),
            "company-owner" => Position(),
            _ => Position(ownerKind: "Supplier", ownerId: Customer),
        };

        Assert.False(ManufacturingCustomerOwnedInputGuardV1.Admits(Company, position, Commercial()));
    }

    [Fact]
    public void A_missing_snapshot_or_an_unresolved_company_is_refused()
    {
        Assert.False(ManufacturingCustomerOwnedInputGuardV1.Admits(Company, null));
        Assert.False(ManufacturingCustomerOwnedInputGuardV1.Admits(Guid.Empty,
            Position(ownerId: Guid.Empty, custodianId: Guid.Empty)));
    }

    [Fact]
    public void The_refusal_is_the_routes_existing_declared_context_conflict_code()
    {
        // No new public code: the issue answers exactly what it answered for non-company stock before the
        // customer-context aggregate, so the client text and the declared 409 set stay unchanged.
        Assert.Equal(InventoryManufacturingRealizationCodesV1.ParentLabelContextConflict,
            ManufacturingCustomerOwnedInputGuardV1.RefusalCode);
        Assert.Equal("Company", ManufacturingCustomerOwnedInputGuardV1.CompanyPartyKind);
        Assert.Equal("Counterparty", ManufacturingCustomerOwnedInputGuardV1.CounterpartyPartyKind);
    }

    private static PlanCommercialContextSnapshotV1 Commercial(Guid? partyId = null) => new(
        partyId ?? Customer,
        Guid.Parse("50000000-0000-0000-0000-000000000005"),
        "party-v1",
        "account-v1");

    private static ManufacturingInventoryPositionSnapshotV1 Position(
        string ownerKind = "Company",
        Guid? ownerId = null,
        string custodianKind = "Company",
        Guid? custodianId = null) =>
        new(ownerKind, ownerId ?? Company, custodianKind, custodianId ?? Company, Location);
}
