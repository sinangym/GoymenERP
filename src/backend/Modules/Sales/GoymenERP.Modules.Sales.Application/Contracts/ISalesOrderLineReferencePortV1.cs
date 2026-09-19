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
    long Version);
