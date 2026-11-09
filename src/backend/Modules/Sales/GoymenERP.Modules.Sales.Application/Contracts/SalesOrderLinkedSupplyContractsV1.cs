namespace GoymenERP.Modules.Sales.Application.Contracts;

// ---- S-1 / MOD-05 (URETIM_SATIS_DEVIR_v1 S-1; ALIS_HAZIRLIK_PLANI_v1 MOD-03): "bağlı üretim / bağlı satın alma" ----------------
//
// DEC-SAL-005 shows "bağlı satın alma/üretim" in the cancellation preview; No 30 as narrowed by SAT-D12 keeps "bağlı satın
// alma/üretim" and an unreadable source BLOCKING a force-close. Sales owns this read contract; each SUPPLY OWNER answers it with
// its own adapter in its own module (Production: U5-a over IManufacturingSalesOrderLinkReadPortV1; Purchasing: MOD-03). Sales
// never reads another module's tables, and changing this file is a Sales-lane change (owners adapt to it).

/// <summary>The supply kinds an owner answers for (each maps to one DEC-SAL-005 cancellation category).</summary>
public static class SalesLinkedSupplyKindsV1
{
    /// <summary>Linked production plans / orders (category <see cref="SalesCancellationImpactSourcesV1.LinkedProduction"/>).</summary>
    public const string Production = "Production";

    /// <summary>Linked purchase orders (category <see cref="SalesCancellationImpactSourcesV1.LinkedPurchasing"/>).</summary>
    public const string Purchasing = "Purchasing";

    public static IReadOnlyList<string> All { get; } = [Production, Purchasing];
}

/// <summary>The document types an open linked supply item can be.</summary>
public static class SalesLinkedSupplyDocumentTypesV1
{
    public const string ProductionPlan = "ProductionPlan";
    public const string ProductionOrder = "ProductionOrder";
    public const string PurchaseOrder = "PurchaseOrder";

    public static bool IsKnown(string? type) => type is ProductionPlan or ProductionOrder or PurchaseOrder;
}

/// <summary>The contract's version name (pinned by a contract test; a shape change is a new version).</summary>
public static class SalesOrderLinkedSupplyContractV1
{
    public const string Name = "sales.order-linked-supply.v1";
}

/// <summary>
/// What Sales asks: the OPEN (not closed, not cancelled) supply documents an owner links to the order in the company — all lines,
/// or only <paramref name="OrderLineIds"/> when given. The company is the caller's server-resolved company.
/// <para>
/// <paramref name="OrderId"/> is ALWAYS sent (the owner's index is on the order: Purchasing's request-line source
/// <c>{ kind: "Sales", documentId, documentLineId }</c>, <c>IX_PurchaseRequestLines_Source (CompanyId, SourceKind,
/// SourceDocumentId)</c>, #928); line ids are only a narrowing filter. The ids are opaque to the owner, which stores what the
/// requester sent and does not validate it against Sales; Sales checks every answered line id against the order and reads an
/// unknown one as Unreadable (fail-closed), never as a validated link.
/// </para>
/// </summary>
public sealed record SalesOrderLinkedSupplyQueryV1(Guid CompanyId, Guid OrderId, IReadOnlyList<Guid>? OrderLineIds);

/// <summary>
/// One open linked supply document line.
/// <list type="bullet">
/// <item><paramref name="SupplyId"/> / <paramref name="SupplyLineId"/>: the owner's document and (when it has lines) line — a
/// purchase order and its line (Purchasing: the approved, not cancelled / closed line linked through
/// <c>SourceRequestId</c> / <c>SourceRequestLineId</c>); a production plan or order (Production). (document, line) is unique.</item>
/// <item><paramref name="OrderLineId"/>: the Sales order line it serves (null when the owner links the whole order).</item>
/// <item><paramref name="OpenQuantity"/> in <paramref name="OpenQuantityUnitId"/> (a Settings unit identity): what is still open
/// as exact decimal text (Purchasing: the line's <c>RemainingQuantity</c>, a general line and its technical breakdown never
/// summed twice); null when the owner does not state it.</item>
/// <item><paramref name="Status"/>, <paramref name="PlannedKg"/>, <paramref name="ActualKg"/>: the owner's status summary
/// (Production's 2f answer: plan / order status, planned / actual kilograms as exact decimal text).</item>
/// </list>
/// Sales shows these and derives nothing from them beyond "an open link exists".
/// </summary>
public sealed record SalesOrderLinkedSupplyItemV1(
    Guid SupplyId,
    Guid? SupplyLineId,
    string DocumentType,
    string? DocumentNumber,
    Guid? OrderLineId,
    string? Status,
    string? OpenQuantity,
    Guid? OpenQuantityUnitId,
    string? PlannedKg,
    string? ActualKg);

/// <summary>
/// The owner's answer for one order, in the cancellation preview's own vocabulary (<see cref="SalesCancellationImpactStatusesV1"/>):
/// <c>Present</c> with every open linked line in <paramref name="OpenItems"/>; <c>None</c> with no item; <c>Unreadable</c> when
/// the owner could not read its records (its runtime off, a failure), <paramref name="ReasonCode"/> saying why and no item —
/// never "None". <c>NoSource</c> is Sales's own word for "no adapter registered"; an adapter that answers it, or anything else,
/// is read as Unreadable (fail-closed).
/// </summary>
public sealed record SalesOrderLinkedSupplyAnswerV1(
    string Status,
    Guid CompanyId,
    Guid OrderId,
    string SupplyKind,
    IReadOnlyList<SalesOrderLinkedSupplyItemV1> OpenItems,
    string? ReasonCode);

/// <summary>
/// The Sales-declared read a SUPPLY OWNER answers (S-1 Production U5-a; Purchasing MOD-03), registered by that owner's host
/// adapter. It carries NO authority (the allocation port's shape): the Sales caller measures its own name before and after
/// asking. It reads under the caller's company only and CREATES NOTHING. One adapter per <see cref="SupplyKind"/>; a kind with no
/// adapter and no <see cref="ISalesOrderLinkedSupplyRecorderV1"/> is "no source" (not a blocker). A registered adapter that throws,
/// answers Unreadable or answers a malformed value makes the category Unreadable (it blocks a cancellation and a force-close).
/// On main (#998): the host registers Purchasing's adapter (<c>PurchasingSalesOrderLinkedSupplyReadPort</c>, kind Purchasing);
/// Production has no adapter yet, so "linked production" stays "no source".
/// </summary>
public interface ISalesOrderLinkedSupplyReadPortV1
{
    /// <summary>One of <see cref="SalesLinkedSupplyKindsV1"/>.</summary>
    string SupplyKind { get; }

    Task<SalesOrderLinkedSupplyAnswerV1> ReadOrderLinkedSupplyAsync(SalesOrderLinkedSupplyQueryV1 query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// SAT-D219 (EK38): the declaration a SUPPLY OWNER registers, in its own module, when it RECORDS links to Sales order lines for a
/// <see cref="SupplyKind"/> (Purchasing: a purchase order line carrying a Sales order line as its source; Production: a plan or
/// order). Sales cannot see another module's tables, so this is how it knows "no module records a link" (the kind is
/// <c>NoSource</c> and blocks nothing) from "a module records links but its adapter of
/// <see cref="ISalesOrderLinkedSupplyReadPortV1"/> is not registered" (the kind is <c>Unreadable</c>: removing a line or changing
/// its product is refused, as are the cancellation and the force-close, fail-closed). A module that starts recording links delivers
/// this declaration with its adapter in the same change. On main (#998) the host declares Purchasing
/// (<c>PurchasingSalesOrderLinkedSupplyRecorder</c>) beside its adapter; Production declares nothing yet. It carries no authority
/// and reads nothing.
/// </summary>
public interface ISalesOrderLinkedSupplyRecorderV1
{
    /// <summary>One of <see cref="SalesLinkedSupplyKindsV1"/>.</summary>
    string SupplyKind { get; }
}
