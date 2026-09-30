namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// SALES-A1, Business #66 5737989917 (= B: only an APPROVED order line is a need): the Sales-owned
/// read port the consumers — Inventory allocation (INV-AL-1) and Logistics loading instructions
/// (LOG-4) — ask before they create their own records. It answers the line's identity facts as the
/// Sales owner stored them, and only while the draft is internally approved; a draft that is being
/// prepared, waiting for approval or rejected is <see cref="SalesOrderLineReferenceOutcomeV1.NotApproved"/>
/// with its state, and an unknown or another company's draft or line is
/// <see cref="SalesOrderLineReferenceOutcomeV1.NotFound"/> (never 403). Reading here creates nothing:
/// no allocation, loading, stock or money record — the consumer's own command does that under its
/// own authority, idempotency and audit, and a replay of that command creates no second need.
/// </summary>
public interface ISalesOrderLineReferencePortV1
{
    Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logistics-specific read for an effective-dated loading cap. Existing consumers keep the identity-only read above;
    /// implementations that have not yet published a loading source remain compatible and answer their normal line.
    /// </summary>
    Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineForLoadingAsync(Guid orderId, Guid lineId, DateOnly effectiveOn,
        CancellationToken cancellationToken = default) => GetApprovedLineAsync(orderId, lineId, cancellationToken);

    /// <summary>
    /// LOG-4b: the company's APPROVED order lines for loading as a LINE page — <paramref name="pageSize"/>
    /// (1–100) caps the lines actually returned, whatever the drafts' line counts (FİNANS #358 P2) — newest
    /// draft first, by the Sales owner's opaque, company-bound COMPOSITE cursor (draft + line position, so a
    /// draft's lines are neither skipped nor repeated at a page boundary; the last page answers null; a
    /// malformed or foreign cursor is <see cref="ArgumentException"/>). PRICE-FREE by construction (Sinan §4,
    /// #66 5738113623: the Sevkiyatçı sees sales orders but never prices; deny-first, so no price, discount or
    /// payment member exists on <see cref="SalesApprovedLineReferenceV1"/>). Identities, names, unit and the
    /// exact quantity text only. The consumer (Logistics) measures its own authority before asking; reading
    /// here creates nothing.
    /// </summary>
    Task<SalesApprovedLinePageV1> ListApprovedLinesAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default);
}

public sealed record SalesApprovedLinePageV1(IReadOnlyList<SalesApprovedLineReferenceV1> Items, string? NextCursor);

/// <summary>
/// One APPROVED line as the Sales owner stored it, for a loading picker: the order (id + document no), the
/// line (id, no), the customer (party + company account), the product (id, version, code, name), the order
/// unit (id + code), the exact quantity text (Ö2), the requested delivery date, the commercial revision.
/// No price, discount, currency or payment member — a contract test pins that.
/// </summary>
public sealed record SalesApprovedLineReferenceV1(
    Guid CompanyId,
    Guid OrderId,
    string DocumentNo,
    Guid LineId,
    int LineNo,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid ProductId,
    string ProductVersion,
    string ProductCode,
    string ProductName,
    Guid OrderUnitId,
    string UnitCode,
    string QuantityText,
    DateOnly? RequestedDeliveryDate,
    int Revision,
    string RemainderState = SalesRemainderChangeNamesV1.Open);

public enum SalesOrderLineReferenceOutcomeV1
{
    /// <summary>The draft is internally approved; <see cref="SalesOrderLineReferenceAnswerV1.Line"/> carries the line.</summary>
    Approved = 1,

    /// <summary>The draft exists in the context company but is not approved; <see cref="SalesOrderLineReferenceAnswerV1.ApprovalState"/> says which state it is in. Fail-closed: no need.</summary>
    NotApproved = 2,

    /// <summary>No such draft or line in the context company.</summary>
    NotFound = 3,
}

/// <summary>The answer: exactly one of the line (approved), the state (not approved), or neither (not found).</summary>
public sealed record SalesOrderLineReferenceAnswerV1(
    SalesOrderLineReferenceOutcomeV1 Outcome,
    SalesOrderLineReferenceV1? Line,
    string? ApprovalState);

/// <summary>
/// OPEN-004 consumer boundary: Sales has already resolved the line override or the versioned Settings
/// product-group default into one maximum total loading-instruction quantity in the order unit. Logistics
/// never calculates a percentage, chooses a default or converts a unit. Null on the line means that the
/// source is unavailable; it is never repaired into zero.
/// </summary>
public sealed record SalesOrderLineLoadingLimitV1(
    Guid CompanyId,
    Guid OrderId,
    Guid LineId,
    Guid UnitId,
    long SalesOrderVersion,
    string MaximumAggregateQuantityText,
    string SourceOwner,
    string SourceId,
    string SourceVersion,
    DateOnly EffectiveOn);

public static class SalesOrderLineLoadingLimitSourcesV1
{
    /// <summary>User decision 5917438293 option 7B: without a defined profile, the approved order quantity is the cap.</summary>
    public const string ApprovedOrderQuantity = "SalesOrderQuantity";
}

/// <summary>
/// The approved line's identity facts as stored: the company, the order and the line, the
/// customer (party + company account), the product and its version as the Product owner answered
/// it, the order unit, the exact quantity text (Ö2: kept verbatim), the commercial revision and the
/// draft's concurrency version. Identities and text only — no price, no stock, no availability.
/// </summary>
public sealed record SalesOrderLineReferenceV1(
    Guid CompanyId,
    Guid OrderId,
    Guid LineId,
    int LineNo,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid ProductId,
    string ProductVersion,
    Guid OrderUnitId,
    string QuantityText,
    int Revision,
    long Version,
    string RemainderState = SalesRemainderChangeNamesV1.Open,
    SalesOrderLineLoadingLimitV1? LoadingLimit = null);
