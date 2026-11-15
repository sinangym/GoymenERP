namespace GoymenERP.Modules.Sales.Application.Contracts;

// ---- F-017 STOK §5.1 (F017-s) / KLN-D28..D32: the allocation read with its KIND ---------------------------------------------
//
// V1 (ISalesOrderAllocationReadPortV1) answers Inventory's PHYSICAL allocations only, in kilograms, and keeps that meaning for
// every existing caller (SAT-D167 line revision, source plan, closures, remainder change). V2 adds the second kind Inventory
// records against a Sales order line: a FUTURE SUPPLY allocation (a share of an approved purchase line's not-yet-received
// quantity). The two kinds are never added together: a future allocation is reported in the PURCHASE line's own unit with that
// unit's id, never converted to kilograms, never part of the physical kilograms and never a source-plan stock part (§5.1 a).

/// <summary>
/// F-017 STOK §5.1: the Sales-declared read the ALLOCATION OWNER (Inventory) answers with both kinds of ACTIVE allocation it
/// records against the lines of a Sales order. Same rules as <see cref="ISalesOrderAllocationReadPortV1"/>: registered by the
/// owner in its own module, no authority (the Sales caller fences its own name before and after asking), the caller's
/// server-resolved company only, creates nothing. V1 stays registered and unchanged; V2 does not replace it.
/// </summary>
public interface ISalesOrderAllocationReadPortV2
{
    /// <summary>
    /// The owner's ACTIVE allocations of every line of <paramref name="orderId"/> in the context company, one entry per
    /// (order line, kind, unit): <see cref="SalesOrderAllocationKindsV2.Physical"/> entries in kilograms with no unit id (the V1
    /// figure), <see cref="SalesOrderAllocationKindsV2.FutureSupply"/> entries per purchase unit with that unit's id. A line the
    /// owner holds none of a kind for has no entry of that kind. An unknown order answers an empty list.
    /// </summary>
    Task<SalesOrderAllocationReadAnswerV2> ReadOrderAllocationsAsync(Guid orderId, CancellationToken cancellationToken = default);
}

/// <summary>The allocation owner's V2 answer for one Sales order in the context company.</summary>
public sealed record SalesOrderAllocationReadAnswerV2(Guid CompanyId, Guid OrderId, IReadOnlyList<SalesOrderLineAllocationV2> Lines);

/// <summary>
/// One Sales order line's ACTIVE allocations of one kind in one unit: their count and the exact quantity they still hold (Ö2,
/// never rounded). <paramref name="UnitId"/> is null for <see cref="SalesOrderAllocationKindsV2.Physical"/> (kilograms) and the
/// purchase line's unit for <see cref="SalesOrderAllocationKindsV2.FutureSupply"/>. A zero count has a zero quantity.
/// </summary>
public sealed record SalesOrderLineAllocationV2(Guid OrderLineId, string Kind, int ActiveCount, decimal ActiveQuantity, Guid? UnitId);

/// <summary>The allocation kinds of <see cref="ISalesOrderAllocationReadPortV2"/> (stable names).</summary>
public static class SalesOrderAllocationKindsV2
{
    /// <summary>A physical stock allocation (INV-AL-1), in kilograms — the V1 meaning.</summary>
    public const string Physical = "Physical";

    /// <summary>A future supply allocation on an approved purchase line (F-017 STOK §3), in that purchase line's unit.</summary>
    public const string FutureSupply = "FutureSupply";

    /// <summary>
    /// The exact decimals a future supply quantity is held with by its owner (the purchase line's stored precision). A value
    /// with more decimals is a malformed answer, never rounded. Inventory's own value object is pinned to this by a test.
    /// </summary>
    public const int FutureSupplyQuantityMaximumDecimals = 3;

    /// <summary>Whether <paramref name="kind"/> is one of the two names (ordinal).</summary>
    public static bool IsKnown(string? kind) =>
        string.Equals(kind, Physical, StringComparison.Ordinal) || string.Equals(kind, FutureSupply, StringComparison.Ordinal);
}
