namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    // A fresh consumer callback may run after the owning TX was disposed. TX/dirty-state
    // checks alone cannot distinguish that callback from an ordinary native mutation.
    // Return's own writes keep their exact command guard and do not call this helper.
    private void RequireNoForeignReturnOrSelectedReadMutation()
    {
        if (ownedCustomerReturnDecision is not null || quoteDecisionReadFence is not null
            || overDeliveryRequestDecisionReadFence is not null || riskExceptionDecisionReadFence is not null
            || contractDiscountDecisionReadFence is not null || remainderDecisionReadFence is not null
            || customerReturnDecisionReadFence is not null)
            throw new InvalidOperationException("Selected decision or read cannot dispatch a foreign Sales mutation.");
    }
}
