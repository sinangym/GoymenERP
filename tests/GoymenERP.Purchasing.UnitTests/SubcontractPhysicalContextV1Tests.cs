using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Subcontracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class SubcontractPhysicalContextV1Tests
{
    private static readonly Guid CompanyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SupplierPartyId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid SupplierAccountId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid OrderId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid LineId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid ServiceId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PhysicalRequestId = Guid.Parse("80000000-0000-0000-0000-000000000001");
    private static readonly Guid ExternalLocationId = Guid.Parse("90000000-0000-0000-0000-000000000001");
    private static readonly Guid OperatorPartyId = Guid.Parse("a0000000-0000-0000-0000-000000000001");

    [Fact]
    public void Contract_keeps_supplier_service_quantity_unit_and_physical_source_as_separate_facts()
    {
        var line = ValidLine();
        var order = new PurchasingSubcontractOrderV1(
            OrderId,
            CompanyId,
            SupplierPartyId,
            SupplierAccountId,
            "order-v3",
            [line]);

        Assert.Equal(SupplierPartyId, order.SupplierPartyId);
        Assert.Equal(SupplierAccountId, order.CompanyAccountId);
        Assert.Equal(ServiceId, line.ServiceId);
        Assert.Equal(UnitId, line.ServiceUnitId);
        Assert.Equal("25000.000", line.ServiceQuantity);
        Assert.Equal(PhysicalRequestId, line.SourcePhysicalRequestId);
    }

    [Fact]
    public void Physical_context_keeps_owner_location_operator_and_custodian_without_stock_effect()
    {
        var context = ValidPhysicalContext(SubcontractMaterialOwnerKindV1.Company, CompanyId);

        Assert.Equal(CompanyId, context.CompanyId);
        Assert.Equal(SubcontractMaterialOwnerKindV1.Company, context.MaterialOwnerKind);
        Assert.Equal(CompanyId, context.MaterialOwnerId);
        Assert.Equal(ExternalLocationId, context.ExternalLocationId);
        Assert.Equal(OperatorPartyId, context.OperatorPartyId);
        Assert.Equal(OperatorPartyId, context.CustodianPartyId);
        Assert.Equal(PhysicalRequestId, context.SourcePhysicalRequestId);

        var propertyNames = typeof(SubcontractPhysicalContextV1).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Quantity", propertyNames);
        Assert.DoesNotContain("StockEffect", propertyNames);
        Assert.DoesNotContain("LabelId", propertyNames);
    }

    [Fact]
    public void Service_quantity_does_not_change_the_physical_context_or_create_a_second_quantity()
    {
        var context = ValidPhysicalContext(SubcontractMaterialOwnerKindV1.Counterparty, SupplierPartyId);
        var line = ValidLine();

        Assert.Equal("25000.000", line.ServiceQuantity);
        Assert.Equal(SupplierPartyId, context.MaterialOwnerId);
        Assert.Equal(OperatorPartyId, context.CustodianPartyId);
        Assert.Empty(typeof(PurchasingSubcontractOrderLineV1).GetProperties()
            .Where(property => property.Name.Contains("Stock", StringComparison.Ordinal)));
    }

    [Fact]
    public void Contract_refuses_empty_identities_blank_versions_nonpositive_quantity_and_duplicate_lines()
    {
        Assert.Throws<ArgumentException>(() => new PurchasingSubcontractOrderV1(
            Guid.Empty, CompanyId, SupplierPartyId, SupplierAccountId, "order-v3", [ValidLine()]));
        Assert.Throws<ArgumentException>(() => new PurchasingSubcontractOrderV1(
            OrderId, CompanyId, SupplierPartyId, SupplierAccountId, " ", [ValidLine()]));
        Assert.Throws<ArgumentException>(() => ValidLine(serviceQuantity: "0"));
        Assert.Throws<ArgumentException>(() => new PurchasingSubcontractOrderV1(
            OrderId, CompanyId, SupplierPartyId, SupplierAccountId, "order-v3", [ValidLine(), ValidLine()]));
    }

    [Fact]
    public void Physical_context_refuses_missing_or_undefined_owner_location_operator_and_source_facts()
    {
        Assert.Throws<ArgumentException>(() => SubcontractPhysicalContextV1.Create(
            Guid.Empty, SubcontractMaterialOwnerKindV1.Company, CompanyId, ExternalLocationId, "location-v2",
            OperatorPartyId, "party-v4", PhysicalRequestId, "request-v5"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidPhysicalContext((SubcontractMaterialOwnerKindV1)999, CompanyId));
        Assert.Throws<ArgumentException>(() => SubcontractPhysicalContextV1.Create(
            CompanyId, SubcontractMaterialOwnerKindV1.Company, CompanyId, ExternalLocationId, " location-v2",
            OperatorPartyId, "party-v4", PhysicalRequestId, "request-v5"));
        Assert.Throws<ArgumentException>(() => SubcontractPhysicalContextV1.Create(
            CompanyId, SubcontractMaterialOwnerKindV1.Company, CompanyId, ExternalLocationId, "location-v2",
            Guid.Empty, "party-v4", PhysicalRequestId, "request-v5"));
        Assert.Throws<ArgumentException>(() => SubcontractPhysicalContextV1.Create(
            CompanyId, SubcontractMaterialOwnerKindV1.Company, CompanyId, ExternalLocationId, "location-v2",
            OperatorPartyId, "party-v4", Guid.Empty, "request-v5"));
    }

    [Fact]
    public void Commercial_contract_has_one_service_identity_and_no_parallel_product_or_consumable_ids()
    {
        var properties = typeof(PurchasingSubcontractOrderLineV1).GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("ServiceId", properties);
        Assert.DoesNotContain("ProductId", properties);
        Assert.DoesNotContain("ConsumableId", properties);
        Assert.DoesNotContain("Price", properties);
        Assert.DoesNotContain("Tax", properties);
    }

    private static PurchasingSubcontractOrderLineV1 ValidLine(string serviceQuantity = "25000.000") => new(
        LineId,
        ServiceId,
        "service-v7",
        UnitId,
        serviceQuantity,
        PhysicalRequestId,
        "request-v5");

    private static SubcontractPhysicalContextV1 ValidPhysicalContext(
        SubcontractMaterialOwnerKindV1 ownerKind,
        Guid ownerId) => SubcontractPhysicalContextV1.Create(
            CompanyId,
            ownerKind,
            ownerId,
            ExternalLocationId,
            "location-v2",
            OperatorPartyId,
            "party-v4",
            PhysicalRequestId,
            "request-v5");
}
