using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseAcceptanceSourceContractTests
{
    [Fact]
    public void Normal_reopen_exposes_only_original_acceptance_facts_never_idempotency_or_actor_secrets()
    {
        Assert.Equal(
            ["AcceptanceId", "AcceptedQuantity", "CompanyId", "OccurredAtUtc", "PurchaseOrderId", "PurchaseOrderLineId", "PurchaseOrderVersionAfter", "RemainingQuantity"],
            typeof(PurchaseAcceptanceSourceV1).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
    }
}
