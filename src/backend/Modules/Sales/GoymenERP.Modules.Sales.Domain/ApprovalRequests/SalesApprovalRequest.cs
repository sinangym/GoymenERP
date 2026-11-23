using System.Globalization;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Domain.ApprovalRequests;

/// <summary>
/// What an approval request asks for (WP4; SINAN_KARARLARI_2026-11-22_EK_SORULAR):
/// <see cref="QuoteExpiredConversion"/> is SAT-D476 (the salesperson asks that an EXPIRED open quote's current revision may be
/// converted), <see cref="OverDelivery"/> is SAT-D475 (a shipment above the order's approval-band tolerance was attempted; the
/// shipment does not become definite until the request is approved).
/// </summary>
public enum SalesApprovalRequestKind
{
    QuoteExpiredConversion = 1,
    OverDelivery = 2,
}

/// <summary>
/// The decision axis of an approval request: born <see cref="Requested"/> and moves once to <see cref="Approved"/> or
/// <see cref="Rejected"/>; nothing is deleted. A quote conversion request is never moved to <see cref="Approved"/> itself: the
/// quote's own expired-conversion approval (SAT-D24 / SAT-D44) answers it, so its "approved" is read from the quote.
/// </summary>
public enum SalesApprovalRequestState
{
    Requested = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>Who decided the request and when; the reason is a rejection's (required there, absent on an approval).</summary>
public sealed record SalesApprovalRequestDecision(string DecidedBy, DateTimeOffset DecidedAtUtc, string? Reason);

/// <summary>
/// One recorded approval request of a Sales record (SAT-D475 / SAT-D476). It is its own record: it never changes the quote or
/// the order it names (an over-delivery approval appends the order's own over-delivery approval fact in the same save; the
/// order's revision and approval axis never move).
/// <list type="bullet">
/// <item><b>Quote conversion (SAT-D476):</b> names the quote and the revision that was expired when asked. Opened by a holder of
/// the quote write; no maker ≠ checker is decided for it (SAT-D44 (2) lets the quote's own creator approve the conversion).</item>
/// <item><b>Over-delivery (SAT-D475):</b> names the order, the line, the measured (ordered) quantity it was judged against, the
/// attempted shipment quantity and the shipped total it would make, and the Logistics loading instruction and shipment as opaque
/// references (Sales never reads Logistics tables). Opened by the Logistics shipment gate through the Sales port in the
/// shipper's name; SAT-D216: the shipper never decides it.</item>
/// </list>
/// </summary>
public sealed class SalesApprovalRequest
{
    public const int MaximumTextLength = SalesOrderDraft.MaximumNoteLength;
    public const int MaximumActorLength = SalesOrderDraft.MaximumActorLength;
    public const int MaximumQuantityTextLength = 32;

    private bool restored;

    private SalesApprovalRequest(Guid id, Guid companyId, SalesApprovalRequestKind kind, Guid subjectId, int requestNo,
        int subjectRevision, Guid? lineId, string? ordered, string? quantity, string? requestedTotal, Guid? sourceInstructionId,
        Guid? sourceShipmentId, string? note, string requestedBy, DateTimeOffset now)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        Kind = Enum.IsDefined(kind) ? kind : throw new ArgumentOutOfRangeException(nameof(kind));
        SubjectId = Identity.Require(subjectId, nameof(subjectId));
        RequestNo = requestNo >= 1 ? requestNo : throw new ArgumentOutOfRangeException(nameof(requestNo));
        SubjectRevision = subjectRevision >= 0 ? subjectRevision : throw new ArgumentOutOfRangeException(nameof(subjectRevision));
        LineId = lineId;
        Ordered = ordered;
        Quantity = quantity;
        RequestedTotal = requestedTotal;
        SourceInstructionId = sourceInstructionId;
        SourceShipmentId = sourceShipmentId;
        Note = note;
        RequestedBy = RequireActor(requestedBy);
        RequestedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = RequestedAtUtc;
    }

    public Guid Id { get; }

    /// <summary>The server-resolved company; never taken from the request and never changed.</summary>
    public Guid CompanyId { get; }

    public SalesApprovalRequestKind Kind { get; }

    /// <summary>The quote (<see cref="SalesApprovalRequestKind.QuoteExpiredConversion"/>) or the order (<see cref="SalesApprovalRequestKind.OverDelivery"/>).</summary>
    public Guid SubjectId { get; }

    /// <summary>1-based number of the request within its kind and subject; the route key of the decisions.</summary>
    public int RequestNo { get; }

    /// <summary>The quote revision asked for, or the order revision whose quantity the over-delivery was measured against.</summary>
    public int SubjectRevision { get; }

    /// <summary>Over-delivery: the order line; null for a quote request.</summary>
    public Guid? LineId { get; }

    /// <summary>Over-delivery: the measured (ordered) quantity of the line, exact invariant text in the order unit.</summary>
    public string? Ordered { get; }

    /// <summary>Over-delivery: the attempted shipment's quantity, exact invariant text in the order unit.</summary>
    public string? Quantity { get; }

    /// <summary>Over-delivery: the shipped total the attempted shipment would make (what an approval covers, SAT-D213).</summary>
    public string? RequestedTotal { get; }

    /// <summary>Over-delivery: the Logistics loading instruction of the attempt (opaque reference).</summary>
    public Guid? SourceInstructionId { get; }

    /// <summary>Over-delivery: the Logistics shipment id of the attempt (opaque reference).</summary>
    public Guid? SourceShipmentId { get; }

    /// <summary>Quote request: the salesperson's optional note.</summary>
    public string? Note { get; }

    public SalesApprovalRequestState State { get; private set; } = SalesApprovalRequestState.Requested;

    public string RequestedBy { get; }

    public DateTimeOffset RequestedAtUtc { get; }

    /// <summary>The decision; null while <see cref="SalesApprovalRequestState.Requested"/>.</summary>
    public SalesApprovalRequestDecision? Decision { get; private set; }

    /// <summary>Optimistic concurrency: 1 at creation, +1 on the decision; a refused command moves nothing.</summary>
    public long Version { get; private set; } = 1;

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>SAT-D476: the salesperson asks for the conversion of the quote's current (expired) revision.</summary>
    public static SalesApprovalRequest RequestQuoteConversion(Guid id, Guid companyId, Guid quoteId, int requestNo, int quoteRevision,
        string? note, string requestedBy, DateTimeOffset now) =>
        new(id, companyId, SalesApprovalRequestKind.QuoteExpiredConversion, quoteId, requestNo, quoteRevision, null, null, null, null,
            null, null, OptionalText(note), requestedBy, now);

    /// <summary>
    /// SAT-D475: the shipment gate's request for an approval-band over-delivery of one order line. The shipped total must be above
    /// the measured quantity and equal to the shipped-before total plus the attempted quantity, both positive, in exact text.
    /// </summary>
    public static SalesApprovalRequest RequestOverDelivery(Guid id, Guid companyId, Guid orderId, int requestNo, Guid lineId,
        int basisRevision, decimal ordered, decimal quantity, decimal requestedTotal, Guid loadingInstructionId, Guid shipmentId,
        string requestedBy, DateTimeOffset now)
    {
        Identity.Require(lineId, nameof(lineId));
        Identity.Require(loadingInstructionId, nameof(loadingInstructionId));
        Identity.Require(shipmentId, nameof(shipmentId));
        if (ordered <= 0m || quantity <= 0m || requestedTotal <= ordered || requestedTotal < quantity)
        {
            throw new ArgumentException("Fazla teslim talebi, ölçülen miktarın üzerindeki bir sevk toplamını adlandırır.", nameof(requestedTotal));
        }

        return new(id, companyId, SalesApprovalRequestKind.OverDelivery, orderId, requestNo, basisRevision, lineId, Text(ordered),
            Text(quantity), Text(requestedTotal), loadingInstructionId, shipmentId, null, requestedBy, now);
    }

    /// <summary>Over-delivery requested total as an exact decimal (null for a quote request).</summary>
    public decimal? RequestedTotalValue => Exact(RequestedTotal);

    /// <summary>Over-delivery measured quantity as an exact decimal (null for a quote request).</summary>
    public decimal? OrderedValue => Exact(Ordered);

    /// <summary>
    /// SAT-D475: one approver who is not the shipper who asked (SAT-D216). Only an over-delivery request is approved here; a
    /// quote request is answered by the quote's own expired-conversion approval.
    /// </summary>
    public void Approve(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        if (Kind != SalesApprovalRequestKind.OverDelivery)
        {
            throw new SalesRuleException(SalesRuleCodes.ApprovalRequestKindMismatch,
                "Teklif dönüşüm talebi teklifin kendi süre onayıyla cevaplanır.");
        }

        var actor = RequireDecider(actorId);
        Decide(SalesApprovalRequestState.Approved, new SalesApprovalRequestDecision(actor, now.ToUniversalTime(), null), now);
    }

    /// <summary>A rejection carries its reason. An over-delivery request is never rejected by its shipper (SAT-D216).</summary>
    public void Reject(long expectedVersion, string actorId, string? reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireDecider(actorId);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new SalesRuleException(SalesRuleCodes.ApprovalRequestReasonRequired, "Talebin reddi gerekçe ister.");
        }

        var text = OptionalText(reason)!;
        Decide(SalesApprovalRequestState.Rejected, new SalesApprovalRequestDecision(actor, now.ToUniversalTime(), text), now);
    }

    /// <summary>
    /// Restores the stored decision onto a request rebuilt through its factory: Requested carries none, Approved a decision without
    /// a reason (an over-delivery request only), Rejected one with a reason; an over-delivery decider is never the shipper; the
    /// version is at least the transitions it implies. A corrupt row is refused, never repaired. Runs once.
    /// </summary>
    public void Restore(SalesApprovalRequestState state, SalesApprovalRequestDecision? decision, long storedVersion,
        DateTimeOffset updatedAtUtc)
    {
        if (restored || Version != 1 || State != SalesApprovalRequestState.Requested)
        {
            throw new InvalidOperationException("Onay talebinin kayıtlı durumu yalnız bir kez ve yeni kurulmuş kayda geri yüklenir.");
        }

        var consistent = state switch
        {
            SalesApprovalRequestState.Requested => decision is null,
            SalesApprovalRequestState.Approved => Kind == SalesApprovalRequestKind.OverDelivery && decision is { Reason: null },
            SalesApprovalRequestState.Rejected => decision is { Reason: { Length: > 0 and <= MaximumTextLength } },
            _ => false,
        };
        if (!consistent || decision is { DecidedBy: var by }
            && (string.IsNullOrWhiteSpace(by) || by.Length > MaximumActorLength
                || Kind == SalesApprovalRequestKind.OverDelivery && string.Equals(by, RequestedBy, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Kayıtlı onay talebi kararı kayıtlı durumuyla uyuşmuyor.");
        }

        if (storedVersion < (state == SalesApprovalRequestState.Requested ? 1L : 2L))
        {
            throw new InvalidOperationException("Kayıtlı onay talebi sürümü kararların hesabıyla uyuşmuyor.");
        }

        State = state;
        Decision = decision;
        Version = storedVersion;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        restored = true;
    }

    /// <summary>Rebuilds a stored request's facts (persistence only; the decision follows through <see cref="Restore"/>).</summary>
    public static SalesApprovalRequest Rebuild(Guid id, Guid companyId, SalesApprovalRequestKind kind, Guid subjectId, int requestNo,
        int subjectRevision, Guid? lineId, string? ordered, string? quantity, string? requestedTotal, Guid? sourceInstructionId,
        Guid? sourceShipmentId, string? note, string requestedBy, DateTimeOffset requestedAtUtc)
    {
        var overDelivery = kind == SalesApprovalRequestKind.OverDelivery;
        var factsConsistent = overDelivery
            ? lineId is { } line && line != Guid.Empty && Exact(ordered) is > 0m && Exact(quantity) is > 0m
                && Exact(requestedTotal) is { } total && total > Exact(ordered) && sourceInstructionId is { } instruction
                && instruction != Guid.Empty && sourceShipmentId is { } shipment && shipment != Guid.Empty && note is null
            : kind == SalesApprovalRequestKind.QuoteExpiredConversion && lineId is null && ordered is null && quantity is null
                && requestedTotal is null && sourceInstructionId is null && sourceShipmentId is null
                && (note is null || note.Length is > 0 and <= MaximumTextLength);
        if (!factsConsistent)
        {
            throw new InvalidOperationException("Kayıtlı onay talebi olguları türüyle uyuşmuyor.");
        }

        return new SalesApprovalRequest(id, companyId, kind, subjectId, requestNo, subjectRevision, lineId, ordered, quantity,
            requestedTotal, sourceInstructionId, sourceShipmentId, note, requestedBy, requestedAtUtc);
    }

    private string RequireDecider(string actorId)
    {
        var actor = RequireActor(actorId);
        if (State != SalesApprovalRequestState.Requested)
        {
            throw new SalesRuleException(SalesRuleCodes.StateConflict, $"Yalnız bekleyen talep karara bağlanır (durum: {State}).");
        }

        if (Kind == SalesApprovalRequestKind.OverDelivery && string.Equals(actor, RequestedBy, StringComparison.Ordinal))
        {
            // SAT-D216: the person who made (attempted) the shipment does not decide their own over-delivery.
            throw new SalesRuleException(SalesRuleCodes.ApprovalRequestSelfDecision,
                "Sevki deneyen kişi kendi fazla teslim talebini onaylayamaz ya da reddedemez.");
        }

        return actor;
    }

    private void Decide(SalesApprovalRequestState state, SalesApprovalRequestDecision decision, DateTimeOffset now)
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

    private static string? OptionalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= MaximumTextLength
            ? value
            : throw new SalesRuleException(SalesRuleCodes.ApprovalRequestTextTooLong,
                $"Metin en fazla {MaximumTextLength} karakterdir; fazlası kesilmez, reddedilir.");
    }

    private static string RequireActor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Değer boş olamaz.", nameof(value));
        }

        var text = value.Trim();
        return text.Length <= MaximumActorLength
            ? text
            : throw new ArgumentException($"Değer en fazla {MaximumActorLength} karakterdir.", nameof(value));
    }

    /// <summary>Exact invariant text without trailing zeros (the order line's own quantity text form).</summary>
    public static string Text(decimal value)
    {
        var text = value.ToString("0.############################", CultureInfo.InvariantCulture);
        return text.Length <= MaximumQuantityTextLength ? text : throw new ArgumentException("Miktar metni çok uzun.", nameof(value));
    }

    private static decimal? Exact(string? text) =>
        !string.IsNullOrEmpty(text) && text.Length <= MaximumQuantityTextLength
        && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
