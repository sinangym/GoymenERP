using System.Globalization;
using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// Partial delivery (user decision 2026-09-30, binding): "on a partially delivered order line the delivered part is kept; the
/// remaining (undelivered) quantity can be cancelled or its quantity revised, and this requires approval." AT-024: "Kısmi
/// teslimat sipariş kalanını doğru tutar." DEC-SAL-005: "Etki önizlemesi olmadan iptal yok" — the request carries the delivery
/// owner's recorded totals it was decided on.
/// <list type="bullet">
/// <item><b>Two steps:</b> <see cref="RequestRemainderChange"/> records the request (nothing on the line moves);
/// <see cref="ApproveRemainderChange"/> applies it, <see cref="RejectRemainderChange"/> closes it. At most one request is
/// pending per order. Each step is one version step.</item>
/// <item><b>The order stays approved:</b> the approval axis, its facts and the commercial revision do not move — the
/// remainder change's own approval is the approval the decision asks for. Logistics keys its loading and shipments on the
/// approved revision (a shipment re-reads the line at the SAME revision), so moving the revision would strand the loaded,
/// not yet shipped part the decision says to keep.</item>
/// <item><b>The floor:</b> the owner's LOADED total of the line (loaded ≥ shipped ≥ delivered) — never reduced below what is
/// delivered or already on a vehicle. A cancel ends the line at the floor read afresh at the APPROVAL; a revise is checked
/// against that fresh floor again. A line without a recorded shipment is not partly delivered: its change is a quantity
/// revision (DEC-SAL-002), not this command.</item>
/// <item><b>No money or stock effect:</b> the line's ordered quantity (and, for a cancel, its remainder state) is all that
/// changes; the owners' records stay theirs. The delivery plan is not re-split: a plan above the new quantity is refused
/// (<see cref="SalesRuleCodes.SlicesExceedQuantity"/>), the quantity revision's technical limit.</item>
/// </list>
/// </summary>
public sealed partial class SalesOrderDraft
{
    private readonly List<RemainderChangeFacts> remainderChanges = [];

    /// <summary>The recorded remainder changes of the order, oldest first (change 1, 2, …).</summary>
    public IReadOnlyList<RemainderChangeFacts> RemainderChanges => remainderChanges.AsReadOnly();

    /// <summary>The one pending remainder change, if any.</summary>
    public RemainderChangeFacts? PendingRemainderChange =>
        remainderChanges.Count > 0 && remainderChanges[^1].State == RemainderChangeState.Pending ? remainderChanges[^1] : null;

    /// <summary>The technical bound of a remainder change's reasons: the note's, as every other reason.</summary>
    public const int MaximumRemainderReasonLength = MaximumNoteLength;

    /// <summary>One version step per recorded request and one per recorded decision (the approval restore's floor).</summary>
    private long RemainderTransitions => remainderChanges.Count + remainderChanges.Count(change => change.Decision is not null);

    /// <summary>
    /// The request's own preconditions WITHOUT the change — the expected version, the lifecycle and approval state, no pending
    /// request, the line and its remainder state, and the reason — so the caller refuses a stale or wrong-state command before it
    /// asks the delivery owner anything. Returns the trimmed reason. <see cref="RequestRemainderChange"/> runs them again.
    /// </summary>
    public string RequireRemainderChangeable(long expectedVersion, Guid lineId, string? reason)
    {
        RequireVersion(expectedVersion);
        RequireNotCancelled("İptal edilmiş siparişin kalanı değiştirilmez.");
        if (ApprovalState != InternalApprovalState.Approved)
        {
            throw StateConflict("Kalan değişikliği yalnız onaylı siparişte istenir; onaylanmamış taslak revize edilir.");
        }

        if (PendingRemainderChange is not null)
        {
            throw StateConflict("Siparişte karar bekleyen bir kalan değişikliği var; önce o karara bağlanır.");
        }

        var line = lines.SingleOrDefault(candidate => candidate.Id == lineId)
            ?? throw new SalesRuleException(SalesRuleCodes.RemainderLineUnknown, "Siparişte böyle bir satır yok.");
        if (line.Remainder == LineRemainderState.Cancelled)
        {
            throw StateConflict("Bu satırın kalanı iptal edildi; yeniden açılmaz.");
        }

        return RequireRemainderReason(reason);
    }

    /// <summary>
    /// Records a remainder change request for one line with the owner's floor it was asked on. Cancel: something must remain
    /// above the floor. Revise: the new quantity is Ö2 in the line's own unit (never rounded, never converted), not below the
    /// floor, not the current quantity, and the line's delivery plan still fits it. Nothing on the line changes here.
    /// </summary>
    public RemainderChangeFacts RequestRemainderChange(long expectedVersion, Guid lineId, RemainderChangeKind kind, string? quantityText,
        decimal? quantity, string? reason, RemainderFloor floor, string actorId, DateTimeOffset now)
    {
        var actor = RequireActor(actorId);
        var trimmedReason = RequireRemainderChangeable(expectedVersion, lineId, reason);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var line = lines.Single(candidate => candidate.Id == lineId);
        var loaded = RequireFloor(line, floor);
        decimal? requested = null;
        string? requestedText = null;
        if (kind == RemainderChangeKind.Cancel)
        {
            if (quantity is not null || quantityText is not null)
            {
                throw new ArgumentException("Kalan iptali miktar taşımaz; satır teslim/yükleme tabanında kapanır.", nameof(quantity));
            }

            if (!line.Quantity.IsGreaterThan(loaded))
            {
                throw new SalesRuleException(SalesRuleCodes.RemainderNone,
                    "Satırın yüklenen/teslim edilen kısmının üstünde iptal edilecek kalan yok.", line.Id);
            }

            RequirePlanFits(line, loaded);
        }
        else
        {
            if (quantity is not { } value || quantityText is null)
            {
                throw new ArgumentException("Kalan revizyonu yeni miktarı ister.", nameof(quantity));
            }

            var revised = RequireRevisedRemainder(line, value, quantityText, loaded);
            requested = revised.Value;
            requestedText = quantityText;
        }

        var facts = new RemainderChangeFacts(new RemainderChangeRequest(remainderChanges.Count + 1, Revision, line.Id, line.LineNo, kind,
            line.QuantityText, requested, requestedText, trimmedReason, actor, now.ToUniversalTime(), floor), null);
        remainderChanges.Add(facts);
        Touch(now);
        return facts;
    }

    /// <summary>
    /// The decision's own preconditions WITHOUT the decision: the expected version, a live (not cancelled, not withdrawn) order,
    /// and that <paramref name="changeNo"/> is THE pending change; for an approval also that the order is approved at the
    /// revision the change was asked against. Returns the pending change.
    /// </summary>
    public RemainderChangeFacts RequireRemainderDecidable(long expectedVersion, int changeNo, bool approving)
    {
        RequireVersion(expectedVersion);
        RequireNotCancelled("İptal edilmiş siparişin kalan değişikliğine karar verilmez.");
        if (ApprovalState == InternalApprovalState.Withdrawn)
        {
            throw StateConflict("Geri çekilmiş taslakta kalan değişikliği yok.");
        }

        var pending = PendingRemainderChange;
        if (pending is null || pending.Request.ChangeNo != changeNo)
        {
            throw StateConflict("Bu numarada karar bekleyen bir kalan değişikliği yok.");
        }

        if (approving && (ApprovalState != InternalApprovalState.Approved || pending.Request.Revision != Revision))
        {
            throw StateConflict("Kalan değişikliği, istendiği onaylı revizyon yürürlükteyken onaylanır.");
        }

        return pending;
    }

    /// <summary>
    /// Approves THE pending change on the owner's floor read afresh: a cancel ends the line at that floor (nothing loadable
    /// remains — the remainder is cancelled), a revise sets the requested quantity if it is still not below the floor. The
    /// approval axis and the revision do not move; the line's quantity and remainder state do.
    /// </summary>
    public RemainderChangeFacts ApproveRemainderChange(long expectedVersion, int changeNo, RemainderFloor floor, string actorId, DateTimeOffset now)
    {
        var actor = RequireActor(actorId);
        var pending = RequireRemainderDecidable(expectedVersion, changeNo, approving: true);
        var line = lines.Single(candidate => candidate.Id == pending.Request.LineId);
        var loaded = RequireFloor(line, floor);
        Quantity result;
        string resultText;
        if (pending.Request.Kind == RemainderChangeKind.Cancel)
        {
            if (!line.Quantity.IsGreaterThan(loaded))
            {
                throw new SalesRuleException(SalesRuleCodes.RemainderNone,
                    "Onay anında satırın yüklenen/teslim edilen kısmının üstünde iptal edilecek kalan kalmadı.", line.Id);
            }

            result = RequireQuantity(loaded.Value, line.OrderUnit);
            RequirePlanFits(line, result);
            resultText = result.Value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            result = RequireRevisedRemainder(line, pending.Request.RequestedQuantity!.Value, pending.Request.RequestedQuantityText!, loaded);
            resultText = pending.Request.RequestedQuantityText!;
        }

        line.ReplaceQuantity(result, resultText);
        line.MarkRemainder(pending.Request.Kind == RemainderChangeKind.Cancel ? LineRemainderState.Cancelled : LineRemainderState.Revised);
        var decided = pending with
        {
            Decision = new RemainderChangeDecision(RemainderChangeState.Approved, actor, now.ToUniversalTime(), null, result.Value, resultText, floor),
        };
        remainderChanges[^1] = decided;
        Touch(now);
        return decided;
    }

    /// <summary>Rejects THE pending change with an optional reason (bounded like every reason); nothing on the line changes.</summary>
    public RemainderChangeFacts RejectRemainderChange(long expectedVersion, int changeNo, string actorId, string? reason, DateTimeOffset now)
    {
        var actor = RequireActor(actorId);
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is not null && trimmedReason.Length > MaximumRemainderReasonLength)
        {
            throw new SalesRuleException(SalesRuleCodes.ApprovalReasonTooLong,
                $"Ret nedeni en fazla {MaximumRemainderReasonLength} karakterdir.");
        }

        var pending = RequireRemainderDecidable(expectedVersion, changeNo, approving: false);
        var decided = pending with
        {
            Decision = new RemainderChangeDecision(RemainderChangeState.Rejected, actor, now.ToUniversalTime(), trimmedReason, null, null, null),
        };
        remainderChanges[^1] = decided;
        Touch(now);
        return decided;
    }

    /// <summary>
    /// The store's rebuild of one recorded remainder change, oldest first, BEFORE the approval axis (its version floor counts
    /// one step per request and one per decision). The change must be the next number, only the last may be pending, it must
    /// name a line of the order at its position, and its facts must fit its kind and outcome. The line's stored quantity is
    /// already the current one (the line row carries it); an approved change marks the line's remainder state. A history that
    /// does not fit is refused, not repaired.
    /// </summary>
    public void RestoreRemainderChange(RemainderChangeFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(facts.Request);
        RequireApprovalNotRestored();
        var request = facts.Request;
        var line = lines.SingleOrDefault(candidate => candidate.Id == request.LineId);
        var decision = facts.Decision;
        var consistent = request.ChangeNo == remainderChanges.Count + 1
            && PendingRemainderChange is null
            && line is not null && line.LineNo == request.LineNo
            && Enum.IsDefined(request.Kind) && request.Revision >= 0 && request.Revision <= Revision
            && !string.IsNullOrWhiteSpace(request.OrderedQuantityText)
            && !string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length <= MaximumRemainderReasonLength
            && !string.IsNullOrWhiteSpace(request.RequestedBy) && request.RequestedBy.Length <= MaximumActorLength
            && request.Floor is not null
            && (request.Kind == RemainderChangeKind.Cancel
                ? request.RequestedQuantity is null && request.RequestedQuantityText is null
                : request.RequestedQuantity is > 0m && !string.IsNullOrWhiteSpace(request.RequestedQuantityText))
            && (decision is null || IsDecision(request, decision));
        if (!consistent || line!.Remainder == LineRemainderState.Cancelled)
        {
            throw new InvalidOperationException("Kayıtlı kalan değişikliği geçmişi taslakla uyuşmuyor.");
        }

        if (decision?.Outcome == RemainderChangeState.Approved)
        {
            line.MarkRemainder(request.Kind == RemainderChangeKind.Cancel ? LineRemainderState.Cancelled : LineRemainderState.Revised);
        }

        remainderChanges.Add(facts);
    }

    private static bool IsDecision(RemainderChangeRequest request, RemainderChangeDecision decision) =>
        !string.IsNullOrWhiteSpace(decision.DecidedBy) && decision.DecidedBy.Length <= MaximumActorLength
        && (decision.Reason is null || (decision.Reason.Length > 0 && decision.Reason.Length <= MaximumRemainderReasonLength))
        && decision.Outcome switch
        {
            RemainderChangeState.Approved => decision.ResultQuantity is > 0m && !string.IsNullOrWhiteSpace(decision.ResultQuantityText)
                && decision.Floor is not null && decision.Reason is null
                && (request.Kind == RemainderChangeKind.Cancel || decision.ResultQuantity == request.RequestedQuantity),
            RemainderChangeState.Rejected => decision.ResultQuantity is null && decision.ResultQuantityText is null && decision.Floor is null,
            _ => false,
        };

    private static string RequireRemainderReason(string? reason)
    {
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is null)
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionReasonRequired, "Kalan değişikliğinin gerekçesi zorunludur.");
        }

        return trimmedReason.Length <= MaximumRemainderReasonLength
            ? trimmedReason
            : throw new SalesRuleException(SalesRuleCodes.RevisionReasonTooLong,
                $"Kalan değişikliğinin gerekçesi en fazla {MaximumRemainderReasonLength} karakterdir.");
    }

    /// <summary>
    /// The owner's floor as the line's own quantity: non-negative, delivered ≤ shipped ≤ loaded, Ö2 in the line's dimension
    /// (a malformed answer is the caller's error, never repaired), and a recorded shipment (else the line is not partly
    /// delivered). Returns the loaded total — the floor.
    /// </summary>
    private static Quantity RequireFloor(SalesOrderDraftLine line, RemainderFloor floor)
    {
        ArgumentNullException.ThrowIfNull(floor);
        if (floor.Delivered < 0m || floor.Delivered > floor.Shipped || floor.Shipped > floor.Loaded)
        {
            throw new ArgumentException("Teslim/sevk/yükleme toplamları kendi sırasıyla çelişiyor.", nameof(floor));
        }

        Quantity loaded;
        try
        {
            loaded = Quantity.Of(floor.Loaded, line.Quantity.Unit);
            _ = Quantity.Of(floor.Shipped, line.Quantity.Unit);
            _ = Quantity.Of(floor.Delivered, line.Quantity.Unit);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("Teslim/sevk/yükleme toplamı satırın biriminde tam değil.", nameof(floor), exception);
        }

        if (floor.Shipped == 0m)
        {
            throw new SalesRuleException(SalesRuleCodes.RemainderNotStarted,
                "Satırın kayıtlı bir sevkiyatı yok; kısmi teslim edilmemiş satırın miktarı revizyonla değişir.", line.Id);
        }

        return loaded;
    }

    private Quantity RequireRevisedRemainder(SalesOrderDraftLine line, decimal value, string text, Quantity loaded)
    {
        Quantity revised;
        try
        {
            revised = RequireQuantity(value, line.OrderUnit);
            RequireQuantityText(text, value);
        }
        catch (SalesRuleException exception) when (exception.LineId is null)
        {
            throw new SalesRuleException(exception.Code, exception.Message, line.Id);
        }

        if (loaded.IsGreaterThan(revised))
        {
            throw new SalesRuleException(SalesRuleCodes.RemainderBelowFloor,
                "Yeni miktar teslim edilen/yüklenen miktarın altına inemez.", line.Id);
        }

        if (revised.Value == line.Quantity.Value)
        {
            throw new SalesRuleException(SalesRuleCodes.RemainderNoChange, "Yeni miktar mevcut miktarla aynı.", line.Id);
        }

        RequirePlanFits(line, revised);
        return revised;
    }

    private static void RequirePlanFits(SalesOrderDraftLine line, Quantity quantity)
    {
        var planned = line.RequestedDeliverySlices.Aggregate(Quantity.Zero(quantity.Unit), (sum, slice) => sum.Add(slice.Quantity));
        if (planned.IsGreaterThan(quantity))
        {
            throw new SalesRuleException(SalesRuleCodes.SlicesExceedQuantity,
                "Yeni miktar satırın termin dilimlerinin toplamından küçük; dilimler bu değişiklikte yeniden bölünmez.", line.Id);
        }
    }
}
