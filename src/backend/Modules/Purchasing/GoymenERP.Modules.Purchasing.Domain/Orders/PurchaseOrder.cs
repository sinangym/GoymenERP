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
    public IReadOnlyList<PurchaseOrderLine> Lines => lines;
    public IReadOnlyList<PurchaseAcceptanceShare> Acceptances => acceptances;

    public static PurchaseOrder Create(Guid id, Guid companyId, Guid supplierPartyId, DateTimeOffset now) =>
        new(id, companyId, supplierPartyId, null, now);

    /// <summary>
    /// Sources an order from a submitted request of the SAME company, copying its lines as
    /// general lines. The request stays what it is; the order is the commitment.
    /// </summary>
    public static PurchaseOrder FromRequest(Guid id, PurchaseRequest request, Guid supplierPartyId,
        Func<Guid> lineIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lineIds);
        if (request.Status != PurchaseRequestStatus.Submitted)
        {
            throw new InvalidOperationException("Yalnız gönderilmiş talepten sipariş açılabilir.");
        }

        var order = new PurchaseOrder(id, request.CompanyId, supplierPartyId, request.Id, now);
        foreach (var requestLine in request.Lines)
        {
            order.lines.Add(new PurchaseOrderLine(Identity.Require(lineIds(), nameof(lineIds)),
                requestLine.ProductId, requestLine.Quantity, PurchaseOrderLineKind.General, null, requestLine.Id));
        }

        return order;
    }

    public PurchaseOrderLine AddLine(long expectedVersion, Guid lineId, Guid productId, Quantity ordered, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(PurchaseOrderStatus.Draft, "Yalnız taslak siparişe satır eklenebilir.");
        Identity.Require(lineId, nameof(lineId));
        Identity.Require(productId, nameof(productId));
        if (ordered.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(ordered), "Sipariş miktarı sıfırdan büyük olmalıdır.");
        }

        RequireNewLineId(lineId);
        var line = new PurchaseOrderLine(lineId, productId, ordered, PurchaseOrderLineKind.General, null, null);
        lines.Add(line);
        Touch(now);
        return line;
    }

    public void Approve(long expectedVersion, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(PurchaseOrderStatus.Draft, "Yalnız taslak sipariş onaylanabilir.");
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Satırsız sipariş onaylanamaz.");
        }

        Status = PurchaseOrderStatus.Approved;
        Touch(now);
    }

    /// <summary>
    /// Carves a technical line out of a general line (§7.2, §13: 1.000 general → 925 general +
    /// 75 technical). The general remainder plus its technical slices never exceed the first
    /// commitment, an already accepted general share cannot be sliced again, and a technical
    /// line cannot be broken down further.
    /// </summary>
    public PurchaseOrderLine BreakDown(long expectedVersion, Guid generalLineId, Guid technicalLineId,
        Guid technicalProductId, Quantity slice, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var general = FindLine(generalLineId);
        if (general.Kind != PurchaseOrderLineKind.General)
        {
            throw new InvalidOperationException("Teknik satır yeniden kırılamaz; kırılım yalnız genel satırdan yapılır.");
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
            throw new InvalidOperationException("Kırılım genel satırın kabul edilmemiş kalanını aşamaz; aynı pay ikinci kez kırılamaz.");
        }

        var technical = new PurchaseOrderLine(technicalLineId, technicalProductId, slice,
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
        RequireStatus(PurchaseOrderStatus.Approved, "Kabul payı yalnız onaylı siparişe kaydedilir.");
        Identity.Require(acceptanceId, nameof(acceptanceId));
        if (acceptances.Any(share => share.AcceptanceId == acceptanceId))
        {
            throw new InvalidOperationException("Aynı kabul kaydı ikinci kez işlenemez.");
        }

        if (accepted.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(accepted), "Kabul miktarı sıfırdan büyük olmalıdır.");
        }

        var line = FindLine(lineId);
        if (accepted.IsGreaterThan(line.RemainingQuantity))
        {
            throw new InvalidOperationException("Kabul payı satırın kalan taahhüdünü aşamaz.");
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
        ?? throw new InvalidOperationException("Sipariş satırı bulunamadı.");

    private void RequireNewLineId(Guid lineId)
    {
        if (lines.Any(line => line.Id == lineId))
        {
            throw new InvalidOperationException("Aynı sipariş satırı kimliği ikinci kez eklenemez.");
        }
    }

    private void RequireStatus(PurchaseOrderStatus required, string message)
    {
        if (Status != required)
        {
            throw new InvalidOperationException(message);
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
