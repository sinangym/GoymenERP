using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Modules.Purchasing.Domain.Requests;

public enum PurchaseRequestStatus
{
    Draft = 1,
    Submitted = 2,
}

/// <summary>
/// The demand side of talep → teklif → sipariş (#275 §7.2). A request names what a company
/// needs; it is not a commitment, not future supply and not an order. Offers (teklif) are a
/// later slice and no offer requirement is chosen here.
/// <para>
/// A submitted request does not record that it was sourced: today the same request can source
/// more than one order, each with the full quantities. Whether that is a supplier split with
/// partial quantities or a mistake is a Business decision still open (#66); until it is taken
/// the behaviour is kept as it is and named in the README.
/// </para>
/// </summary>
public sealed class PurchaseRequest
{
    private readonly List<PurchaseRequestLine> lines = [];

    private PurchaseRequest(Guid id, Guid companyId, DateTimeOffset now)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        CreatedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public PurchaseRequestStatus Status { get; private set; } = PurchaseRequestStatus.Draft;
    public long Version { get; private set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>A read-only view; the list behind it never leaves this record.</summary>
    public IReadOnlyList<PurchaseRequestLine> Lines => lines.AsReadOnly();

    public static PurchaseRequest Create(Guid id, Guid companyId, DateTimeOffset now) => new(id, companyId, now);

    /// <summary>
    /// Adds a line for a product in a Settings unit (identity only; the unit's profile and the
    /// quantity's scale are verified by the Application layer before the line is added).
    /// </summary>
    public PurchaseRequestLine AddLine(long expectedVersion, Guid lineId, Guid productId, Guid unitId, Quantity quantity, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireDraft();
        Identity.Require(lineId, nameof(lineId));
        Identity.Require(productId, nameof(productId));
        Identity.Require(unitId, nameof(unitId));
        if (quantity.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Talep miktarı sıfırdan büyük olmalıdır.");
        }

        if (lines.Any(line => line.Id == lineId))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.DuplicateLineId, "Aynı talep satırı kimliği ikinci kez eklenemez.");
        }

        var line = new PurchaseRequestLine(lineId, productId, unitId, quantity);
        lines.Add(line);
        Touch(now);
        return line;
    }

    public void Submit(long expectedVersion, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireDraft();
        if (lines.Count == 0)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.RequestWithoutLines, "Satırsız talep gönderilemez.");
        }

        Status = PurchaseRequestStatus.Submitted;
        Touch(now);
    }

    private void RequireDraft()
    {
        if (Status != PurchaseRequestStatus.Draft)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.RequestNotDraft, "Yalnız taslak talep değiştirilebilir.");
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

public sealed class PurchaseRequestLine
{
    internal PurchaseRequestLine(Guid id, Guid productId, Guid unitId, Quantity quantity)
    {
        Id = id;
        ProductId = productId;
        UnitId = unitId;
        Quantity = quantity;
    }

    public Guid Id { get; }
    public Guid ProductId { get; }

    /// <summary>The Settings unit this line is in (identity only).</summary>
    public Guid UnitId { get; }

    public Quantity Quantity { get; }
}
