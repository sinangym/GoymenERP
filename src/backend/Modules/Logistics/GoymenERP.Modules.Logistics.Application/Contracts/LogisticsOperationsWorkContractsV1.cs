namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// AT-025 ("Satış ve satın alma belgeleri operasyon listelerinde karışmaz"): the operations work list, owned by Logistics
/// because both sections are the gate's work (master v3 §27.8: L-01 "Sevk Edilebilir Siparişler" goes out, L-09 "Beklenen
/// Alış / Kapı Kabulü" comes in). The two sections are SEPARATE by construction, not by a filter over one mixed list: two
/// routes, two permission names, two wire types (a sales row cannot be decoded as a purchase row and vice versa), and each
/// page names its section. Each section is fed by its owner's read port only (Sales: <c>ISalesOrderLineReferencePortV1</c>;
/// Purchasing: <c>IPurchaseOpenLineReadPortV1</c>); Logistics adds only its OWN recorded totals and the Cari owner's name.
/// Both are price-free (Sinan §4, #66 5738113623): no price, discount, currency or payment member exists on either type.
/// </summary>
public interface ILogisticsOperationsWorkService
{
    /// <summary>The SALES section: approved (not cancelled) sales order lines, newest order first, under <see cref="LogisticsPermissionManifestV1.OperationsSalesWorkRead"/>.</summary>
    Task<LogisticsSalesWorkPage> GetSalesWorkAsync(LogisticsOperationsWorkQuery query, CancellationToken cancellationToken = default);

    /// <summary>The PURCHASE section: approved purchase order lines still waiting for goods receipt, under <see cref="LogisticsPermissionManifestV1.OperationsPurchaseWorkRead"/>.</summary>
    Task<LogisticsPurchaseWorkPage> GetPurchaseWorkAsync(LogisticsOperationsWorkQuery query, CancellationToken cancellationToken = default);
}

public sealed record LogisticsOperationsWorkQuery(int PageSize = 50, string? Cursor = null);

/// <summary>The two section names a page carries; a consumer refuses a page of the other section.</summary>
public static class LogisticsOperationsWorkSectionsV1
{
    public const string Sales = "Sales";
    public const string Purchase = "Purchase";
}

/// <summary>
/// One approved sales order line in the SALES section: the order (id + document no) and line, the customer by NAME (the
/// Cari owner's, or a labelled "ref. …" when the owner does not answer — the LOG-4b rule), the product (code + name as the
/// Sales owner stored them), the order unit, the ordered quantity (the Sales owner's exact text) and Logistics's OWN recorded
/// totals for that order line in the order unit — instructed, loaded, shipped, delivered (summed over the company's
/// instructions; zero when none) — and the requested delivery date. The remaining quantity is the Sales owner's AT-024
/// answer on its own screen and is not recomputed here.
/// </summary>
public sealed record LogisticsSalesWorkItemView(
    Guid OrderId, string DocumentNo, Guid LineId, int LineNo, Guid CustomerPartyId, string CustomerName,
    Guid ProductId, string ProductCode, string ProductName, Guid UnitId, string UnitCode,
    string Ordered, string Instructed, string Loaded, string Shipped, string Delivered, DateOnly? RequestedDeliveryDate);

public sealed record LogisticsSalesWorkPage(string Section, IReadOnlyList<LogisticsSalesWorkItemView> Items, string? NextCursor);

/// <summary>
/// One approved purchase order line in the PURCHASE section: the order (id + business date) and line (id, number, kind), the
/// supplier by NAME (the Cari owner's, or a labelled "ref. …"), the product and the Settings unit as identities (the
/// Purchasing owner stores no code or name for them — its own detail screen shows the same references), and the Purchasing
/// owner's exact committed / accepted / remaining texts (remaining above zero: that is why the line is here).
/// </summary>
public sealed record LogisticsPurchaseWorkItemView(
    Guid OrderId, DateOnly OrderEffectiveOn, Guid LineId, int LineNo, string Kind, Guid SupplierPartyId, string SupplierName,
    Guid ProductId, Guid UnitId, string Committed, string Accepted, string Remaining);

public sealed record LogisticsPurchaseWorkPage(string Section, IReadOnlyList<LogisticsPurchaseWorkItemView> Items, string? NextCursor);

/// <summary>The permission names the operations work service measures; null = not bound → 503 setup problem, never a grant.</summary>
public sealed record LogisticsOperationsWorkBindings(
    string? SalesWorkPermission = LogisticsPermissionManifestV1.OperationsSalesWorkRead,
    string? PurchaseWorkPermission = LogisticsPermissionManifestV1.OperationsPurchaseWorkRead);

public static class LogisticsOperationsWorkApiV1
{
    public const string BasePath = "/api/v1/logistics/operations-work";
    public const string SalesPath = "/api/v1/logistics/operations-work/sales";
    public const string PurchasePath = "/api/v1/logistics/operations-work/purchase";
}

/// <summary>
/// Logistics's OWN recorded totals per Sales order line (the D4 binding every instruction line carries) and unit, summed
/// over every instruction of the context company. Read-only, untracked, creates nothing; no company context → refused.
/// Implemented in Logistics Infrastructure over its own company-filtered lines (never another module's table).
/// </summary>
public interface ILogisticsOrderLineProgressReader
{
    Task<IReadOnlyList<LogisticsOrderLineProgress>> ReadAsync(IReadOnlyCollection<Guid> orderLineIds, CancellationToken cancellationToken = default);
}

public sealed record LogisticsOrderLineProgress(Guid OrderLineId, Guid UnitId, decimal Instructed, decimal Loaded, decimal Shipped, decimal Delivered);
