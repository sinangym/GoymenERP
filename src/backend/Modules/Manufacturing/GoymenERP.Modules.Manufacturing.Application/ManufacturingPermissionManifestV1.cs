namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>Registered Manufacturing names; this manifest grants nothing and selects no Critical classification.</summary>
public static class ManufacturingPermissionManifestV1
{
    public const string PlansWrite = "manufacturing.plans.write";
    public const string PlansApprove = "manufacturing.plans.approve";
    public const string OrdersCreate = "manufacturing.orders.create";
    public const string FactsRecord = "manufacturing.facts.record";
    public const string OrdersRead = "manufacturing.orders.read";
    public const string FactsCorrect = "manufacturing.facts.correct";
    public const string OrdersClose = "manufacturing.orders.close";
    public const string OrdersCloseExceptionApprove = "manufacturing.orders.close-exception.approve";
    public const string OrdersWrite = "manufacturing.orders.write";
    public const string OrdersCancel = "manufacturing.orders.cancel";
    public const string PlansCancel = "manufacturing.plans.cancel";
    public const string PlansReorder = "manufacturing.plans.reorder";
    public const string ScrapPricesRead = "manufacturing.scrap-prices.read";
    public const string ScrapPricesWrite = "manufacturing.scrap-prices.write";
    public const string ReportsRead = "manufacturing.reports.read";
    public const string PlansRead = "manufacturing.plans.read";

    public static IReadOnlyList<string> Names { get; } =
    [
        PlansWrite,
        PlansApprove,
        OrdersCreate,
        FactsRecord,
        OrdersRead,
        FactsCorrect,
        OrdersClose,
        OrdersCloseExceptionApprove,
        OrdersWrite,
        OrdersCancel,
        PlansCancel,
        PlansReorder,
        ScrapPricesRead,
        ScrapPricesWrite,
        ReportsRead,
        PlansRead,
    ];
}
