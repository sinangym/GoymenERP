namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// DEC-SAL-005: one Sales order line's current loading-instruction facts, read from the Logistics owner in the line's
/// order unit. The values are exact stored quantities (Ö2), never converted or rounded. A line without a Logistics
/// loading instruction is represented by zero facts only after the owner answered successfully.
/// </summary>
public sealed record SalesDraftLineLoadingProgressV1(
    Guid LineId,
    int LineNo,
    Guid OrderUnitId,
    string Ordered,
    int LoadingInstructionLines,
    string Instructed,
    string Loaded,
    string Shipped,
    string Delivered);

/// <summary>
/// DEC-SAL-005: the loading facts of every line of one Sales order, in line order. The answer is read afresh from
/// Logistics on every request and nothing is cached or copied into Sales. <c>draftVersion</c> identifies the Sales
/// draft whose lines were read.
/// </summary>
public sealed record SalesOrderDraftLoadingProgressResponse(
    string DraftVersion,
    IReadOnlyList<SalesDraftLineLoadingProgressV1> Lines);
