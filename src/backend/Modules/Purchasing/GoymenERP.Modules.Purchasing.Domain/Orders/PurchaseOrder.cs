using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Requests;

namespace GoymenERP.Modules.Purchasing.Domain.Orders;

public enum PurchaseOrderStatus
{
    Draft = 1,
    Approved = 2,
}

/// <summary>
/// A purchase commitment towards one supplier Party for one company (#275 §7.2). An approved
/// order is FUTURE supply: it is not physical stock and it books nothing in Inventory. What this
/// record tracks is shares only — ordered, broken down into technical lines, accepted, remaining.
/// The physical receipt, its lot/label and the stock movement belong to Inventory (T09), and a
/// second allocation engine is deliberately not built here. Price, tax and currency are T28.
/// Approval here is a state transition only; which approvals a purchase needs is policy (§8)
/// and is not chosen by this type.
/// <para>
/// This aggregate is the single authority for every share bound; each refusal carries a
/// <see cref="PurchasingRuleCodes"/> code. Line units are Settings identities; the Application
/// layer's verifier checks them against the Settings profile before a line is added.
/// </para>
/// </summary>
public sealed class PurchaseOrder
{
    private readonly List<PurchaseOrderLine> lines = [];
    private readonly List<PurchaseAcceptanceShare> acceptances = [];

    private PurchaseOrder(Guid id, Guid companyId, Guid supplierPartyId, Guid? sourceRequestId, DateTimeOffset now)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        SupplierPartyId = Identity.Require(supplierPartyId, nameof(supplierPartyId));
        if (sourceRequestId == Guid.Empty)
        {
            throw new ArgumentException("Kimlik boş olamaz.", nameof(sourceRequestId));
        }

        SourceRequestId = sourceRequestId;
        CreatedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public Guid SupplierPartyId { get; }
    public Guid? SourceRequestId { get; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;
    public long Version { get; private set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Read-only views; the lists behind them never leave this record.</summary>
    public IReadOnlyList<PurchaseOrderLine> Lines => lines.AsReadOnly();

    public IReadOnlyList<PurchaseAcceptanceShare> Acceptances => acceptances.AsReadOnly();

    public static PurchaseOrder Create(Guid id, Guid companyId, Guid supplierPartyId, DateTimeOffset now) =>
        new(id, companyId, supplierPartyId, null, now);

    /// <summary>
    /// PUR-4a: an order opened towards a supplier for ONE submitted request, whose lines bind to that request's
    /// lines at the quantity this supplier takes (multi-sourcing, Sinan #66 5738113623 §1: a request line may
    /// split across several orders; the sum of the shares never exceeds the line's need — the store enforces the
    /// sum across orders at approval, this record enforces the binding shape).
    /// </summary>
    public static PurchaseOrder CreateForRequest(Guid id, Guid companyId, Guid supplierPartyId, Guid sourceRequestId, DateTimeOffset now) =>
        new(id, companyId, supplierPartyId, Identity.Require(sourceRequestId, nameof(sourceRequestId)), now);

    /// <summary>
    /// Sources an order from a SUBMITTED request of the SAME company, copying its lines as
    /// general lines with their units. The request stays what it is; the order is the commitment.
    /// Sourcing the same request again is not refused today (open Business question, README).
    /// </summary>
    public static PurchaseOrder FromRequest(Guid id, PurchaseRequest request, Guid supplierPartyId,
        Func<Guid> lineIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lineIds);
        if (request.Status != PurchaseRequestStatus.Submitted)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.RequestNotSubmitted,
                "Yalnız gönderilmiş talepten sipariş açılabilir.");
        }

        var order = new PurchaseOrder(id, request.CompanyId, supplierPartyId, request.Id, now);
        foreach (var requestLine in request.Lines)
        {
            order.lines.Add(new PurchaseOrderLine(Identity.Require(lineIds(), nameof(lineIds)), requestLine.ProductId,
                requestLine.UnitId, requestLine.Quantity, PurchaseOrderLineKind.General, null, requestLine.Id));
        }

        return order;
    }

    public PurchaseOrderLine AddLine(long expectedVersion, Guid lineId, Guid productId, Guid unitId, Quantity ordered, DateTimeOffset now) =>
        AddLine(expectedVersion, lineId, productId, unitId, ordered, sourceRequestLineId: null, now);

    /// <summary>
    /// PUR-4a: a general line, optionally bound to one line of the order's source request (the share this
    /// supplier takes of that need). A binding needs the order to have a source request; the same request line
    /// is bound at most once per order (<see cref="PurchasingRuleCodes.DuplicateSourceLine"/>).
    /// </summary>
    public PurchaseOrderLine AddLine(long expectedVersion, Guid lineId, Guid productId, Guid unitId, Quantity ordered, Guid? sourceRequestLineId,
        DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(PurchaseOrderStatus.Draft, PurchasingRuleCodes.OrderNotDraft, "Yalnız taslak siparişe satır eklenebilir.");
        Identity.Require(lineId, nameof(lineId));
        Identity.Require(productId, nameof(productId));
        Identity.Require(unitId, nameof(unitId));
        if (ordered.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(ordered), "Sipariş miktarı sıfırdan büyük olmalıdır.");
        }

        if (sourceRequestLineId is { } sourceLine)
        {
            Identity.Require(sourceLine, nameof(sourceRequestLineId));
            if (SourceRequestId is null)
            {
                throw new PurchasingRuleException(PurchasingRuleCodes.SourceLineWithoutRequest,
                    "Talep satırına bağlı satır yalnız bir talebe açılmış siparişe eklenir.");
            }

            if (lines.Any(line => line.SourceRequestLineId == sourceLine))
            {
                throw new PurchasingRuleException(PurchasingRuleCodes.DuplicateSourceLine,
                    "Aynı talep satırı bir siparişe ikinci kez bağlanamaz; pay tek satırda verilir.");
            }
        }

        RequireNewLineId(lineId);
        var line = new PurchaseOrderLine(lineId, productId, unitId, ordered, PurchaseOrderLineKind.General, null, sourceRequestLineId);
        lines.Add(line);
        Touch(now);
        return line;
    }

    public void Approve(long expectedVersion, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(PurchaseOrderStatus.Draft, PurchasingRuleCodes.OrderNotDraft, "Yalnız taslak sipariş onaylanabilir.");
        if (lines.Count == 0)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderWithoutLines, "Satırsız sipariş onaylanamaz.");
        }

        Status = PurchaseOrderStatus.Approved;
        Touch(now);
    }

    /// <summary>
    /// Carves a technical line out of a general line (§7.2, §13: 1.000 general → 925 general +
    /// 75 technical). Allowed while Draft and after approval — that is the §7.2 case, a general
    /// purchase whose technical breakdown becomes known later; the total commitment never changes
    /// and the technical line keeps the general line's unit, only its product may differ. The
    /// general remainder plus its technical slices never exceed the first commitment, an already
    /// accepted general share cannot be sliced again, and a technical line cannot be broken down
    /// further. Whether a breakdown after approval needs a re-approval is §8 policy, not chosen here.
    /// </summary>
    public PurchaseOrderLine BreakDown(long expectedVersion, Guid generalLineId, Guid technicalLineId,
        Guid technicalProductId, Quantity slice, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        // Breakdown is open in both statuses that exist (Draft and Approved; pinned by tests), so
        // there is no status guard here: one would be unreachable today and, the day a third
        // status arrives, would need its own code — it must not report "not approved" for an
        // order that was approved and then closed (ORG #288 P3-1). The slice adding that status
        // adds the guard, its code and its test.
        var general = FindLine(generalLineId);
        if (general.Kind != PurchaseOrderLineKind.General)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.TechnicalLineNotBreakable,
                "Teknik satır yeniden kırılamaz; kırılım yalnız genel satırdan yapılır.");
        }

        Identity.Require(technicalLineId, nameof(technicalLineId));
        Identity.Require(technicalProductId, nameof(technicalProductId));
        RequireNewLineId(technicalLineId);
        if (slice.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(slice), "Kırılım miktarı sıfırdan büyük olmalıdır.");
        }

        if (slice.IsGreaterThan(general.RemainingQuantity))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.SliceExceedsRemaining,
                "Kırılım genel satırın kabul edilmemiş kalanını aşamaz; aynı pay ikinci kez kırılamaz.");
        }

        var technical = new PurchaseOrderLine(technicalLineId, technicalProductId, general.UnitId, slice,
            PurchaseOrderLineKind.Technical, general.Id, general.SourceRequestLineId);
        general.AddTechnicalSlice(slice);
        lines.Add(technical);
        Touch(now);
        return technical;
    }

    /// <summary>
    /// Records an accepted SHARE against a line (§13: 100 ton ordered, 40 accepted → 40 accepted +
    /// 60 remaining; 140 never). Share only: no lot, label, location or stock movement is created
    /// here. A duplicate acceptance id is the same receipt twice and is refused.
    /// </summary>
    public PurchaseAcceptanceShare RecordAcceptance(long expectedVersion, Guid acceptanceId, Guid lineId,
        Quantity accepted, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(PurchaseOrderStatus.Approved, PurchasingRuleCodes.OrderNotApproved, "Kabul payı yalnız onaylı siparişe kaydedilir.");
        Identity.Require(acceptanceId, nameof(acceptanceId));
        if (acceptances.Any(share => share.AcceptanceId == acceptanceId))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.DuplicateAcceptanceId, "Aynı kabul kaydı ikinci kez işlenemez.");
        }

        if (accepted.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(accepted), "Kabul miktarı sıfırdan büyük olmalıdır.");
        }

        var line = FindLine(lineId);
        if (accepted.IsGreaterThan(line.RemainingQuantity))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.AcceptanceExceedsRemaining,
                "Kabul payı satırın kalan taahhüdünü aşamaz.");
        }

        line.AddAcceptedShare(accepted);
        var share = new PurchaseAcceptanceShare(acceptanceId, line.Id, accepted, now.ToUniversalTime());
        acceptances.Add(share);
        Touch(now);
        return share;
    }

    /// <summary>What is still expected from the supplier in one unit: future supply, never stock.</summary>
    public Quantity RemainingCommitment(QuantityUnit unit) =>
        lines.Where(line => line.Quantity.Unit == unit)
            .Aggregate(Quantity.Zero(unit), (sum, line) => sum.Add(line.RemainingQuantity));

    private PurchaseOrderLine FindLine(Guid lineId) =>
        lines.FirstOrDefault(line => line.Id == lineId)
        ?? throw new PurchasingRuleException(PurchasingRuleCodes.LineNotFound, "Sipariş satırı bulunamadı.");

    private void RequireNewLineId(Guid lineId)
    {
        if (lines.Any(line => line.Id == lineId))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.DuplicateLineId, "Aynı sipariş satırı kimliği ikinci kez eklenemez.");
        }
    }

    private void RequireStatus(PurchaseOrderStatus required, string code, string message)
    {
        if (Status != required)
        {
            throw new PurchasingRuleException(code, message);
        }
    }

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new PurchasingVersionConflictException(expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAtUtc = now.ToUniversalTime();
        Version = checked(Version + 1);
    }
}

/// <summary>One recorded acceptance share. Not a receipt document and not a stock movement.</summary>
public sealed record PurchaseAcceptanceShare(Guid AcceptanceId, Guid LineId, Quantity Quantity, DateTimeOffset RecordedAtUtc);
