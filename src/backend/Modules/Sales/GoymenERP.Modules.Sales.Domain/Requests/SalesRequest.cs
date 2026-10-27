using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Modules.Sales.Domain.Requests;

/// <summary>
/// The approval axis of a sales request (plan §3.2 API-20/21; delegation No 160, 161). A request is recorded as
/// <see cref="Submitted"/> — it waits for its approval from the moment it exists — and moves once:
/// <c>Submitted → Approved</c> (<see cref="SalesRequest.Approve"/>) or <c>Submitted | Approved → Rejected</c>
/// (<see cref="SalesRequest.Reject"/>, the two source states SINAN_KARARLARI_2026-10-27 §1 names for the same transition).
/// <see cref="Rejected"/> is terminal; nothing is deleted.
/// </summary>
public enum SalesRequestState
{
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>The header facts of a request as the client sent them; the customer is validated by the Cari owner at command time.</summary>
public sealed record SalesRequestHeader(
    Guid PartyId,
    Guid CompanyAccountId,
    DateOnly BusinessDate,
    DateOnly? RequestedDeliveryDate = null,
    string? Note = null);

/// <summary>One requested line: the owner snapshots come from the Product and Settings ports, the rest from the client.</summary>
public sealed record SalesRequestLineInput(
    Guid LineId,
    ProductSnapshot Product,
    UnitProfileSnapshot OrderUnit,
    string QuantityText,
    decimal Quantity,
    DateOnly? RequestedDeliveryDate = null,
    string? Note = null);

/// <summary>Who decided the request and when; the reason is a rejection's (required there, absent on an approval).</summary>
public sealed record SalesRequestDecision(string DecidedBy, DateTimeOffset DecidedAtUtc, string? RejectionReason);

/// <summary>One line of a sales request: the owners' snapshots and the exact requested quantity (Ö2, the draft's rules).</summary>
public sealed class SalesRequestLine
{
    internal SalesRequestLine(Guid id, int lineNo, ProductSnapshot product, UnitProfileSnapshot orderUnit, Quantity quantity,
        string quantityText, DateOnly? requestedDeliveryDate, string? note)
    {
        Id = Identity.Require(id, nameof(id));
        LineNo = lineNo >= 1 ? lineNo : throw new ArgumentOutOfRangeException(nameof(lineNo));
        Product = product;
        OrderUnit = orderUnit;
        Quantity = quantity;
        QuantityText = quantityText;
        RequestedDeliveryDate = requestedDeliveryDate;
        Note = note;
    }

    public Guid Id { get; }

    public int LineNo { get; }

    public ProductSnapshot Product { get; }

    public UnitProfileSnapshot OrderUnit { get; }

    public Quantity Quantity { get; }

    /// <summary>The quantity text exactly as the client sent it (lossless, the draft's shape).</summary>
    public string QuantityText { get; }

    public DateOnly? RequestedDeliveryDate { get; }

    public string? Note { get; }
}

/// <summary>
/// A sales request (S-01 "Satış Talebi"; plan §3.2 API-20/21): one company (server-resolved, immutable), one customer
/// (Party + CompanyPartyAccount), one business date, 1–500 product lines carrying the Product and Settings owners'
/// snapshots under exactly the order draft's line rules (Ö2, snapshot company/date/unit). It is a customer's demand, not
/// an order: it carries no price, no money profile and commits no stock or money.
/// <list type="bullet">
/// <item><b>Approval (No 160, 161):</b> born <see cref="SalesRequestState.Submitted"/>; one approver decides
/// (<see cref="Approve"/>); a rejection (<see cref="Reject"/>) carries a reason and is allowed from Submitted or Approved.
/// No maker≠checker, threshold or second approver is chosen here: none is decided for a sales request.</item>
/// <item><b>Version:</b> the optimistic-concurrency token every decision names; 1 at creation, +1 per decision.</item>
/// </list>
/// The document number is a technical, company-scoped display identifier assigned by the store, not an official
/// document number (Ö8).
/// </summary>
public sealed class SalesRequest
{
    public const int MaximumLines = SalesOrderDraft.MaximumLines;
    public const int MaximumNoteLength = SalesOrderDraft.MaximumNoteLength;
    public const int MaximumReasonLength = SalesOrderDraft.MaximumNoteLength;
    public const int MaximumDocumentNoLength = SalesOrderDraft.MaximumDocumentNoLength;
    public const int MaximumActorLength = SalesOrderDraft.MaximumActorLength;

    private readonly List<SalesRequestLine> lines = [];
    private bool restored;

    private SalesRequest(Guid id, Guid companyId, string documentNo, SalesRequestHeader header, string createdBy, DateTimeOffset now)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        DocumentNo = RequireText(documentNo, MaximumDocumentNoLength, nameof(documentNo));
        Header = header;
        CreatedBy = RequireText(createdBy, MaximumActorLength, nameof(createdBy));
        CreatedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }

    /// <summary>The server-resolved company; never taken from the request and never changed.</summary>
    public Guid CompanyId { get; }

    /// <summary>Technical, company-scoped display identifier; not an official document number (Ö8); no gap-free guarantee.</summary>
    public string DocumentNo { get; }

    public SalesRequestHeader Header { get; }

    public SalesRequestState State { get; private set; } = SalesRequestState.Submitted;

    /// <summary>The decision; null while the request is <see cref="SalesRequestState.Submitted"/>.</summary>
    public SalesRequestDecision? Decision { get; private set; }

    /// <summary>Optimistic concurrency: 1 at creation, +1 per recorded decision; a refused command moves nothing.</summary>
    public long Version { get; private set; } = 1;

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<SalesRequestLine> Lines => lines.AsReadOnly();

    /// <summary>
    /// Records a request with its lines in one step. Every line is checked with the order draft's own rules (snapshot
    /// company, business date, order-unit binding, unit profile in effect, Ö2 scale, lossless quantity text); line ids are
    /// unique; notes keep the note bound. A request without lines is refused.
    /// </summary>
    public static SalesRequest Create(Guid id, Guid companyId, string documentNo, SalesRequestHeader header,
        IReadOnlyList<SalesRequestLineInput> inputs, string createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(inputs);
        Identity.Require(header.PartyId, nameof(header.PartyId));
        Identity.Require(header.CompanyAccountId, nameof(header.CompanyAccountId));
        RequireNote(header.Note);
        if (inputs.Count == 0)
        {
            throw new SalesRuleException(SalesRuleCodes.RequestWithoutLines, "Satırsız satış talebi kaydedilmez.");
        }

        if (inputs.Count > MaximumLines)
        {
            throw new SalesRuleException(SalesRuleCodes.TooManyLines, $"Bir satış talebi en fazla {MaximumLines} satır taşır.");
        }

        var request = new SalesRequest(id, companyId, documentNo, header, createdBy, now);
        var ids = new HashSet<Guid>();
        foreach (var input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(input.Product);
            ArgumentNullException.ThrowIfNull(input.OrderUnit);
            Identity.Require(input.LineId, nameof(input.LineId));
            if (!ids.Add(input.LineId))
            {
                throw new SalesRuleException(SalesRuleCodes.DuplicateLineId, "Aynı satır kimliği ikinci kez verilemez.");
            }

            RequireNote(input.Note);
            SalesOrderDraft.RequireLineSnapshots(companyId, header.BusinessDate, input.Product, input.OrderUnit);
            var quantity = SalesOrderDraft.RequireQuantity(input.Quantity, input.OrderUnit);
            SalesOrderDraft.RequireQuantityText(input.QuantityText, input.Quantity);
            request.lines.Add(new SalesRequestLine(input.LineId, request.lines.Count + 1, input.Product, input.OrderUnit, quantity,
                input.QuantityText, input.RequestedDeliveryDate, input.Note));
        }

        return request;
    }

    /// <summary>No 160: the approval of a submitted request — one approver, no reason. Any other state is a state conflict.</summary>
    public void Approve(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        if (State != SalesRequestState.Submitted)
        {
            throw StateConflict("Yalnız onay bekleyen satış talebi onaylanır");
        }

        Decide(SalesRequestState.Approved, new SalesRequestDecision(actor, now.ToUniversalTime(), null), now);
    }

    /// <summary>
    /// No 161 / SINAN_KARARLARI_2026-10-27 §1: the rejection of a Submitted or Approved request, with its reason (required,
    /// never defaulted or cut). Whether a linked order blocks it is the caller's question, asked inside the same save.
    /// </summary>
    public void Reject(long expectedVersion, string actorId, string? reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        if (State is not (SalesRequestState.Submitted or SalesRequestState.Approved))
        {
            throw StateConflict("Yalnız onay bekleyen ya da onaylı satış talebi reddedilir");
        }

        var text = RequireReason(reason);
        Decide(SalesRequestState.Rejected, new SalesRequestDecision(actor, now.ToUniversalTime(), text), now);
    }

    /// <summary>
    /// Restores the stored decision onto a request rebuilt through <see cref="Create"/>: the facts must match the state
    /// (Submitted: none; Approved: a decision without a reason; Rejected: a decision with one) and the stored version must
    /// be at least the transitions it implies. A corrupt row is refused, never repaired. Runs once.
    /// </summary>
    public void Restore(SalesRequestState state, SalesRequestDecision? decision, long storedVersion, DateTimeOffset updatedAtUtc)
    {
        if (restored || Version != 1 || State != SalesRequestState.Submitted)
        {
            throw new InvalidOperationException("Satış talebinin kayıtlı durumu yalnız bir kez ve yeni kurulmuş talebe geri yüklenir.");
        }

        var consistent = state switch
        {
            SalesRequestState.Submitted => decision is null,
            SalesRequestState.Approved => decision is { RejectionReason: null },
            SalesRequestState.Rejected => decision is { RejectionReason: { Length: > 0 and <= MaximumReasonLength } },
            _ => false,
        };
        if (!consistent || decision is { DecidedBy: { } by } && (string.IsNullOrWhiteSpace(by) || by.Length > MaximumActorLength))
        {
            throw new InvalidOperationException("Kayıtlı satış talebi kararı kayıtlı durumuyla uyuşmuyor.");
        }

        var minimumVersion = state == SalesRequestState.Submitted ? 1L : 2L;
        if (storedVersion < minimumVersion)
        {
            throw new InvalidOperationException("Kayıtlı satış talebi sürümü, kararların hesabıyla uyuşmuyor.");
        }

        State = state;
        Decision = decision;
        Version = storedVersion;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        restored = true;
    }

    private void Decide(SalesRequestState state, SalesRequestDecision decision, DateTimeOffset now)
    {
        State = state;
        Decision = decision;
        UpdatedAtUtc = now.ToUniversalTime();
        Version = checked(Version + 1);
    }

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new SalesVersionConflictException(expectedVersion, Version);
        }
    }

    private SalesRuleException StateConflict(string message) =>
        new(SalesRuleCodes.StateConflict, $"{message} (durum: {State}).");

    private static string RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new SalesRuleException(SalesRuleCodes.RequestRejectionReasonRequired, "Satış talebinin reddi gerekçe ister.");
        }

        return reason.Length <= MaximumReasonLength
            ? reason
            : throw new SalesRuleException(SalesRuleCodes.RequestRejectionReasonTooLong,
                $"Ret gerekçesi en fazla {MaximumReasonLength} karakterdir; fazlası kesilmez, reddedilir.");
    }

    private static void RequireNote(string? note)
    {
        if (note is not null && note.Length > MaximumNoteLength)
        {
            throw new SalesRuleException(SalesRuleCodes.NoteTooLong, $"Not en fazla {MaximumNoteLength} karakterdir.");
        }
    }

    private static string RequireActor(string actorId) => RequireText(actorId, MaximumActorLength, nameof(actorId));

    private static string RequireText(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        var text = value.Trim();
        return text.Length <= maximumLength
            ? text
            : throw new ArgumentException($"Değer en fazla {maximumLength} karakterdir.", parameterName);
    }
}
