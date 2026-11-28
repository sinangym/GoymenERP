using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>Pure binding seam only. This is not a create/approve endpoint or persistence participant.</summary>
public static class PurchaseMixedOrderBindingV1
{
    public static void Bind(PurchaseMixedOrderBindingContextV1 context, long expectedParentVersion, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(context.Parent);
        var parent = context.Parent;
        parent.Order.BindServiceOrders(expectedParentVersion, parent.CompanyAccountId, context.Services, context.Selections, now);
    }
}
