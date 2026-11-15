namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// F-017 F17-1 (ALIS_F017 v1 §3.1, ALS-D82; STOK_F017 §10 F017-a): the Purchasing-owned read port Inventory asks before it
/// records a future-supply allocation of a sales order line against a purchase line (A3-0's "v2" need; the v1
/// <see cref="IPurchaseSupplyReferencePortV1"/> is unchanged). It answers how much of an APPROVED purchase GENERAL line is still
/// allocatable — committed − accepted − cancelled − closed (§2: the INV-006 extension; the API-11 <c>closed</c> term entered with the
/// line closure slice, ALS-D87; the <c>kesinRed/iadeEtkisi</c> term does not exist in code yet and enters this port with the slice
/// that first writes it, API-44/API-60) — with the
/// technical slices of the line folded into it and never counted a second time (the existing "open" rule of
/// <c>PurchaseSalesLinkedSupplyComposer</c>): allocation is made on the general line only.
/// <para>
/// The company is NEVER a parameter: it comes from the server-resolved company context and the tenant filter. Without a
/// resolved company the single-line answer is <see cref="PurchaseFutureSupplyOutcomeV1.NotFound"/> and the list is empty
/// (fail-closed); another company's order is NotFound, never 403. <see cref="PurchaseFutureSupplyLineV1.CompanyId"/> only says
/// which context answered. Every quantity is the purchase line's own unit (<see cref="PurchaseFutureSupplyLineV1.UnitId"/>,
/// SAT-D298): Purchasing never converts; the consumer converts its input to the line's unit through the central unit conversion
/// service. <see cref="PurchaseFutureSupplyLineV1.ObservedOrderVersion"/> is an observation only — never a cross-module lock or
/// expected version; the cross-module race is closed by <c>IPurchaseLineSerializationLockV1</c> (§3.4), which the consumer takes
/// BEFORE it asks. No price, discount, currency or payment member exists (T28). The port carries no authority (LOG-4 D6): the
/// consumer measures its own permission. Reading here creates nothing (no attempt, audit or version effect).
/// </para>
/// </summary>
public interface IPurchaseFutureSupplyReadPortV1
{
    /// <summary>
    /// The one purchase line Inventory reads in its allocation transaction, after the §3.4 lock. A technical slice answers
    /// <see cref="PurchaseFutureSupplyOutcomeV1.NotAllocatable"/> with <see cref="PurchaseFutureSupplyStatusesV1.TechnicalLine"/> and
    /// its general line in <see cref="PurchaseFutureSupplyAnswerV1.ParentLineId"/>; a Draft or Cancelled order's line answers
    /// NotAllocatable with the order status name; an Approved general line answers <see cref="PurchaseFutureSupplyOutcomeV1.Allocatable"/>
    /// with its allocatable text (zero when fully accepted or closed).
    /// </summary>
    Task<PurchaseFutureSupplyAnswerV1> GetAllocatableLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The allocation screen's choices: the context company's Approved general lines of one product whose allocatable amount is
    /// above zero, ordered by the order's business date, then the order id, then the line number. The cursor is opaque and bound to
    /// the company and the product that read it; a malformed or foreign cursor is <see cref="ArgumentException"/>; a page size outside
    /// 1–<see cref="PurchaseFutureSupplyPageV1.MaximumPageSize"/> is <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    Task<PurchaseFutureSupplyPageV1> ListAllocatableLinesAsync(Guid productId, int pageSize, string? cursor, CancellationToken cancellationToken = default);
}

public enum PurchaseFutureSupplyOutcomeV1
{
    /// <summary>An Approved order's general line; <see cref="PurchaseFutureSupplyAnswerV1.Line"/> carries its allocatable amount.</summary>
    Allocatable = 1,

    /// <summary>
    /// The line exists in the context company but cannot carry an allocation: a technical slice, or a Draft/Cancelled order's
    /// line. <see cref="PurchaseFutureSupplyAnswerV1.Status"/> says why; fail-closed, the allocatable amount is zero.
    /// </summary>
    NotAllocatable = 2,

    /// <summary>No resolved company, or no such order or line in the context company.</summary>
    NotFound = 3,
}

/// <summary>The fixed <see cref="PurchaseFutureSupplyAnswerV1.Status"/> texts besides the order status names (<c>Draft</c>, <c>Cancelled</c>).</summary>
public static class PurchaseFutureSupplyStatusesV1
{
    /// <summary>The asked line is a technical slice; allocation is made on its general line (<see cref="PurchaseFutureSupplyAnswerV1.ParentLineId"/>).</summary>
    public const string TechnicalLine = "technical-line";
}

public sealed record PurchaseFutureSupplyAnswerV1(
    PurchaseFutureSupplyOutcomeV1 Outcome, PurchaseFutureSupplyLineV1? Line, string? Status, Guid? ParentLineId);

/// <summary>
/// One Approved purchase general line as Purchasing answers it for future-supply allocation: the company, the order and the line,
/// the supplier (party + company account), the product and its answered version, the Settings unit, the kind (always
/// <c>General</c>), and the exact quantity texts (Ö2, the line's own unit, technical slices folded in): committed, accepted,
/// cancelled, closed (API-11 short-delivery closure; zero while the line is open) and allocatable = committed − accepted − cancelled
/// − closed. The sales intent (§2: the line's request line came from a Sales
/// order) is the Sales order and line identity or null — an intent, never an allocation. Identities and text only — no price.
/// </summary>
public sealed record PurchaseFutureSupplyLineV1(
    Guid CompanyId,
    Guid OrderId,
    Guid LineId,
    int LineNo,
    Guid SupplierPartyId,
    Guid CompanyAccountId,
    Guid ProductId,
    string ProductVersion,
    Guid UnitId,
    string Kind,
    string CommittedText,
    string AcceptedText,
    string CancelledText,
    string ClosedText,
    string AllocatableText,
    Guid? SalesSourceOrderId,
    Guid? SalesSourceOrderLineId,
    long ObservedOrderVersion);

public sealed record PurchaseFutureSupplyPageV1(IReadOnlyList<PurchaseFutureSupplyLineV1> Items, string? NextCursor)
{
    /// <summary>The list page cap (the consumer validates the same 1–100).</summary>
    public const int MaximumPageSize = 100;
}
