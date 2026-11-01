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
/// <para>
/// <b>Contract delta SAT-D103 / SAT-D104 / SAT-D111 (EK13, K3).</b> AT-023 is narrowed by Sinan's decision: while a newer
/// commercial revision of an approved order waits for re-approval (<c>Preparing</c> / <c>PendingApproval</c>) or after it
/// was <c>Rejected</c>, the port no longer answers <see cref="SalesOrderLineReferenceOutcomeV1.NotApproved"/> for a line the
/// LAST APPROVED revision holds: it answers <see cref="SalesOrderLineReferenceOutcomeV1.Approved"/> with that revision's
/// identity (<c>Revision</c> = the archived revision's number, <c>QuantityText</c> = its approved quantity), marked
/// <see cref="SalesOrderLineRevisionBasisV1.LastApprovedRevision"/> with the pending revision's approval state, and with the
/// shipping cap (<c>ShippingCapQuantityText</c>, and the loading limit's maximum): the last approved quantity, or while a
/// DECREASE waits for approval min(last approved, requested) (SAT-D111); after a rejection the last approved quantity again.
/// An INCREASE is never shipped before its approval (SAT-D104). Nothing else of the newer revision — price, discount, rate,
/// the increased quantity — reaches a consumer before its approval. A line the last approved revision does not hold (added
/// in the newer revision), a cancelled or closed order, and a customer acceptance missing for the last approved revision
/// stay <see cref="SalesOrderLineReferenceOutcomeV1.NotApproved"/> (fail-closed). Physical shipping only: invoicing waits
/// during re-approval (the order status axis carries that gate; no invoicing consumer reads this port).
/// </para>
/// <para>
/// <b>API-04 (header revision, fail-closed).</b> SAT-D103 does not cover a currency change and no decision lets a customer change
/// ship under the old customer: while the newer revision names another customer (party or account) or another currency than the
/// last approved revision, every line answers <see cref="SalesOrderLineReferenceOutcomeV1.NotApproved"/> with the state
/// <c>HeaderRevisionPending</c>. A due-terms, sales-owner or delivery-reference revision keeps the SAT-D103 answer above.
/// </para>
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
/// No price, discount, currency or payment member — a contract test pins that. SAT-D103 (contract delta): during a
/// re-approval the line of the LAST APPROVED revision is listed (<c>RevisionBasis</c> <c>LastApprovedRevision</c>, its
/// revision and quantity, the pending revision's approval state and the SAT-D104/D111 shipping cap).
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
    string RemainderState = SalesRemainderChangeNamesV1.Open,
    string RevisionBasis = SalesOrderLineRevisionBasisV1.CurrentRevision,
    string? PendingApprovalState = null,
    string? ShippingCapQuantityText = null);

/// <summary>
/// SAT-D103 (EK13, contract delta): which commercial revision a line answer stands for. The Sales owner always sets it. It is
/// informational for the consumers of today (Logistics, Inventory): they do not read it and need not, because
/// <c>Revision</c>, <c>QuantityText</c> and the loading limit already carry the last approved identity and the shipping cap.
/// </summary>
public static class SalesOrderLineRevisionBasisV1
{
    /// <summary>The order's current revision is approved; the answer is that revision.</summary>
    public const string CurrentRevision = "CurrentRevision";

    /// <summary>
    /// A newer revision waits for re-approval or was rejected: the answer is the LAST APPROVED revision, valid for physical
    /// shipping only, capped by SAT-D104 / SAT-D111. <c>PendingApprovalState</c> names the newer revision's approval state.
    /// </summary>
    public const string LastApprovedRevision = "LastApprovedRevision";
}

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
/// source is unavailable; it is never repaired into zero. During a re-approval (SAT-D103) the maximum is the SAT-D104 /
/// SAT-D111 shipping cap under the source <see cref="SalesOrderLineLoadingLimitSourcesV1.ReapprovalShippingCap"/>.
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

    /// <summary>
    /// SAT-D104 / SAT-D111 (EK13): a newer revision waits for re-approval or was rejected — the cap is the last approved
    /// quantity, or min(last approved, requested) while a decrease waits for approval.
    /// </summary>
    public const string ReapprovalShippingCap = "SalesOrderReapprovalShippingCap";
}

/// <summary>
/// The approved line's identity facts as stored: the company, the order and the line, the
/// customer (party + company account), the product and its version as the Product owner answered
/// it, the order unit, the exact quantity text (Ö2: kept verbatim), the commercial revision and the
/// draft's concurrency version. Identities and text only — no price, no stock, no availability. SAT-D103 (contract delta):
/// <c>RevisionBasis</c> says whether this is the current revision or the last approved one during a re-approval;
/// <c>PendingApprovalState</c> is the newer revision's approval state (null for the current revision);
/// <c>ShippingCapQuantityText</c> is the most that may be shipped for the line under this answer (the quantity itself for
/// the current revision; SAT-D104/D111 during a re-approval).
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
    SalesOrderLineLoadingLimitV1? LoadingLimit = null,
    string RevisionBasis = SalesOrderLineRevisionBasisV1.CurrentRevision,
    string? PendingApprovalState = null,
    string? ShippingCapQuantityText = null);
