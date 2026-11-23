namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// WP14 (YOL-D29, OTIF): the Sales-owned read of order lines by their PROMISED delivery day — the line's requested delivery
/// date, or the order's when the line has none. It answers lines of open AND closed orders (a delivered order still counts in
/// OTIF); cancelled orders, removed lines and lines whose approved quantity cannot be named are not answered (the last are
/// counted in <see cref="SalesOrderPromisePageV1.UnmeasuredLines"/>, never guessed). The quantity is the approved (and, where
/// acceptance is required, accepted) revision's, in the line's order unit, as exact invariant text. The port carries no authority
/// (the consumer measures its own before and after asking), reads only the context company's drafts and creates nothing.
/// </summary>
public interface ISalesOrderPromiseReadPortV1
{
    /// <summary>The technical draft page bound (not a business threshold).</summary>
    const int MaximumPageSize = 100;

    /// <summary>
    /// The lines whose promised day is in [<paramref name="from"/>, <paramref name="to"/>] of one page of drafts (newest first);
    /// <see cref="SalesOrderPromisePageV1.NextCursor"/> continues with the next drafts (opaque, company-bound), null at the end.
    /// </summary>
    Task<SalesOrderPromisePageV1> ListPromisedLinesAsync(DateOnly from, DateOnly to, int pageSize, string? cursor,
        CancellationToken cancellationToken = default);
}

public sealed record SalesOrderPromisedLineV1(
    Guid CompanyId,
    Guid OrderId,
    string DocumentNo,
    Guid LineId,
    int LineNo,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid UnitId,
    string UnitCode,
    DateOnly PromisedOn,
    string Quantity);

public sealed record SalesOrderPromisePageV1(
    Guid CompanyId,
    IReadOnlyList<SalesOrderPromisedLineV1> Lines,
    int UnmeasuredLines,
    string? NextCursor);
