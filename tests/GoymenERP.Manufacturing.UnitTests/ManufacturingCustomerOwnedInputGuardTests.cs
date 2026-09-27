using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// The fail-closed input-issue guard (NEEDS-SINAN #66/5830378162 undecided): only a position owned AND custodied
/// by the context company is issued; customer-owned (fason), third-party-custodied, foreign-company and malformed
/// snapshots are refused with the route's existing 409 code. The host applies it inside the owner transaction;
/// the hosted SQL case pins the rollback (`ManufacturingInventoryInputIssueHostSqlServerRelationalAcceptanceTests`).
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

    [Theory]
    [InlineData("customer-owned")]
    [InlineData("customer-owned-and-custodied")]
    [InlineData("third-party-custodied")]
    [InlineData("another-companys-stock")]
    [InlineData("custodied-by-another-company")]
    [InlineData("unknown-owner-kind")]
    [InlineData("unknown-custodian-kind")]
    [InlineData("kind-in-another-case")]
    public void Any_other_owner_or_custodian_is_refused(string shape)
    {
        var position = shape switch
        {
            "customer-owned" => Position(ownerKind: "Counterparty", ownerId: Customer),
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
    }

    private static ManufacturingInventoryPositionSnapshotV1 Position(
        string ownerKind = "Company",
        Guid? ownerId = null,
        string custodianKind = "Company",
        Guid? custodianId = null) =>
        new(ownerKind, ownerId ?? Company, custodianKind, custodianId ?? Company, Location);
}
