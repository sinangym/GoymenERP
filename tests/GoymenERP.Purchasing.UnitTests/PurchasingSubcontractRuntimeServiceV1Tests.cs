using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Subcontracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchasingSubcontractRuntimeServiceV1Tests
{
    [Fact]
    public void Draft_preserves_exact_service_quantity_and_admits_once_with_expected_version()
    {
        var now = new DateTimeOffset(2026, 10, 19, 20, 0, 0, TimeSpan.Zero);
        var line = new SubcontractOrderLineV1(Guid.NewGuid(), Guid.NewGuid(), "service-v4",
            Guid.NewGuid(), "0012.5000", Guid.NewGuid(), "physical-v3");
        var order = SubcontractOrderV1.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "party-v2", Guid.NewGuid(), "account-v5", [line], now);

        Assert.Equal("0012.5000", Assert.Single(order.Lines).ServiceQuantity);
        order.Admit(1, now.AddMinutes(1));
        Assert.Equal(SubcontractOrderStatusV1.Admitted, order.Status);
        Assert.Equal(2, order.Version);
        Assert.Throws<SubcontractOrderVersionConflictV1>(() => order.Admit(1, now.AddMinutes(2)));
        Assert.Throws<SubcontractOrderStateConflictV1>(() => order.Admit(2, now.AddMinutes(2)));
    }

    [Fact]
    public void Runtime_contract_has_no_stock_finance_or_acceptance_effect_surface()
    {
        var names = typeof(PurchasingSubcontractViewV1).Assembly.GetTypes()
            .Where(type => type.Name.Contains("PurchasingSubcontract", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties().Select(property => property.Name)).ToArray();

        Assert.DoesNotContain(names, name => name.Contains("Stock", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Invoice", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Payment", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("purchasing.subcontracts.admit", PurchasingPermissionManifestV1.SubcontractsAdmit);
    }
}
