using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Modules.Purchasing.Domain.Requests;

public enum PurchaseRequestStatus
{
    Draft = 1,
    Submitted = 2,

    /// <summary>A1-1 (ALS-D2): the purchase manager approved the submitted need; only now may an order be opened for it (ALS-D16).</summary>
    Approved = 3,

    /// <summary>A1-1 (ALS-D2, No.161 §1): rejected from Submitted or Approved with a reason; terminal, nothing is deleted.</summary>
    Rejected = 4,
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
/// <para>
/// A1-1 (ALS-D2, ALS-D16, No.161 §1): <c>Submitted → Approved</c> (<see cref="Approve"/>) and
/// <c>Submitted | Approved → Rejected</c> (<see cref="Reject"/>, reason required). The preparer (the creator) and the
/// submitter are the makers; neither may approve or reject (hazırlayan ≠ onaylayan). A maker that is not known is
/// refused, never assumed (fail-closed). A rejection is refused while an APPROVED order is bound to the request; the
/// caller measures that fact inside the same save.
/// </para>
/// </summary>
public sealed class PurchaseRequest
{
    /// <summary>The longest rejection reason; a longer one is refused, never cut (the Sales request's bound).</summary>
    public const int MaximumReasonLength = 4000;

    /// <summary>The longest actor identity (the stored actor columns).</summary>
    public const int MaximumActorLength = 128;

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

    /// <summary>The actor who created the request (a maker); null only for a request built without one, which cannot be decided.</summary>
    public string? PreparedBy { get; private set; }

    /// <summary>The actor who submitted the request (a maker); null for a Draft, or for a submission recorded without its actor.</summary>
    public string? SubmittedBy { get; private set; }

    /// <summary>The approval (ALS-D2), if any; it stays after a later rejection.</summary>
    public PurchaseRequestDecision? Approval { get; private set; }

    /// <summary>The rejection (No.161 §1), if any, with its reason.</summary>
    public PurchaseRequestDecision? Rejection { get; private set; }

    /// <summary>A read-only view; the list behind it never leaves this record.</summary>
    public IReadOnlyList<PurchaseRequestLine> Lines => lines.AsReadOnly();

    public static PurchaseRequest Create(Guid id, Guid companyId, DateTimeOffset now) => new(id, companyId, now);

    /// <summary>A1-1: a request with its preparer, the first maker of the maker ≠ checker rule.</summary>
    public static PurchaseRequest Create(Guid id, Guid companyId, string preparedBy, DateTimeOffset now) =>
        new(id, companyId, now) { PreparedBy = RequireActor(preparedBy, nameof(preparedBy)) };

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

    public void Submit(long expectedVersion, DateTimeOffset now) => SubmitCore(expectedVersion, null, now);

    /// <summary>A1-1: the submission with its actor, the second maker of the maker ≠ checker rule.</summary>
    public void Submit(long expectedVersion, string submittedBy, DateTimeOffset now) =>
        SubmitCore(expectedVersion, RequireActor(submittedBy, nameof(submittedBy)), now);

    /// <summary>
    /// ALS-D2: the purchase manager approves a SUBMITTED request; the approver is neither its preparer nor its submitter.
    /// Any other state is <see cref="PurchasingRuleCodes.RequestNotSubmitted"/>.
    /// </summary>
    public void Approve(long expectedVersion, string approvedBy, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(approvedBy, nameof(approvedBy));
        if (Status != PurchaseRequestStatus.Submitted)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.RequestNotSubmitted, "Yalnız gönderilmiş talep onaylanır.");
        }

        RequireChecker(actor);
        Approval = new PurchaseRequestDecision(actor, now.ToUniversalTime(), null);
        Status = PurchaseRequestStatus.Approved;
        Touch(now);
    }

    /// <summary>
    /// No.161 §1 / ALS-D2: the rejection of a Submitted or Approved request with its reason (required, never defaulted or
    /// cut), by someone other than its makers, refused while an APPROVED order is bound to the request
    /// (<paramref name="hasApprovedOrder"/>, measured by the caller inside the same save).
    /// </summary>
    public void Reject(long expectedVersion, string rejectedBy, string? reason, bool hasApprovedOrder, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(rejectedBy, nameof(rejectedBy));
        if (Status is not (PurchaseRequestStatus.Submitted or PurchaseRequestStatus.Approved))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.RequestNotRejectable, "Yalnız gönderilmiş ya da onaylı talep reddedilir.");
        }

        var text = RequireReason(reason);
        RequireChecker(actor);
        if (hasApprovedOrder)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.RequestHasApprovedOrder,
                "Talebe bağlı onaylı sipariş varken talep reddedilemez; önce bağlı sipariş iptal edilmelidir.");
        }

        Rejection = new PurchaseRequestDecision(actor, now.ToUniversalTime(), text);
        Status = PurchaseRequestStatus.Rejected;
        Touch(now);
    }

    private void SubmitCore(long expectedVersion, string? submittedBy, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireDraft();
        if (lines.Count == 0)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.RequestWithoutLines, "Satırsız talep gönderilemez.");
        }

        SubmittedBy = submittedBy;
        Status = PurchaseRequestStatus.Submitted;
        Touch(now);
    }

    /// <summary>Hazırlayan ≠ onaylayan (ALS-D2): both makers must be known and neither may decide; ordinal comparison.</summary>
    private void RequireChecker(string actor)
    {
        if (PreparedBy is null || SubmittedBy is null)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.RequestMakerUnknown,
                "Talebi hazırlayan veya gönderen bilinmiyor; karar verilemez.");
        }

        if (string.Equals(actor, PreparedBy, StringComparison.Ordinal) || string.Equals(actor, SubmittedBy, StringComparison.Ordinal))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.RequestSameActor,
                "Talebi hazırlayan veya gönderen aynı talep için karar veremez.");
        }
    }

    private static string RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.RequestRejectionReasonRequired, "Talebin reddi gerekçe ister.");
        }

        return reason.Length <= MaximumReasonLength
            ? reason
            : throw new PurchasingRuleException(PurchasingRuleCodes.RequestRejectionReasonTooLong,
                $"Ret gerekçesi en fazla {MaximumReasonLength} karakterdir; fazlası kesilmez, reddedilir.");
    }

    private static string RequireActor(string actor, string parameterName) =>
        string.IsNullOrWhiteSpace(actor) || actor.Length > MaximumActorLength || !string.Equals(actor, actor.Trim(), StringComparison.Ordinal)
            ? throw new ArgumentException("Aktör kimliği boş, kenar boşluklu veya çok uzun olamaz.", parameterName)
            : actor;

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

/// <summary>One decision on a request: who, when and (for a rejection) why.</summary>
public sealed record PurchaseRequestDecision(string DecidedBy, DateTimeOffset DecidedAtUtc, string? Reason);

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
