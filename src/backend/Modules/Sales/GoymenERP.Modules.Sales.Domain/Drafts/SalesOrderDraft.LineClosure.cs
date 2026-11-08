namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D16 / SAT-D17 (EK3, EK25): the shortfall of a line is measured only when the line is closed; in the warn band the line
/// closes directly, in the approval band after the authorised user's action, and the remainder counts as cancelled. SAT-D168
/// (EK24) / SAT-D214 (EK37): the closure is a user action under its own permission (<c>sales.orders.lines.close</c>), never a
/// role name here, with no extra approval. SAT-D215: no reason; a closed line cannot ship. SAT-D216: the person who made a
/// shipment of the line does not close it. The domain half:
/// <list type="bullet">
/// <item><b>The band is the caller's judgement:</b> the shipped total (Logistics) and the tolerance (Settings) are not this
/// aggregate's knowledge; the caller reads them and judges the line with <see cref="SalesRealizationCalculator"/> as a CLOSED line
/// BEFORE this runs. The domain refuses what it can see: a terminal order, an unknown or already closed line, a shipped total that
/// is zero or not below the measured quantity (no shortfall) and a closer the delivery owner names as a shipper.</item>
/// <item><b>Append-only:</b> a closure is one fact with one version step; the quantities, the approval axis and the revision do
/// not move, no stock or money event exists, and the remainder is never a movement (it is only counted as cancelled).</item>
/// </list>
/// </summary>
public sealed partial class SalesOrderDraft
{
    private readonly List<LineClosureFacts> lineClosures = [];

    /// <summary>SAT-D17: every recorded line closure, in the order they were recorded; empty until one line is closed.</summary>
    public IReadOnlyList<LineClosureFacts> LineClosures => lineClosures.AsReadOnly();

    /// <summary>SAT-D215: whether the line was closed with a shortfall (a closed line cannot ship).</summary>
    public bool IsLineClosed(Guid lineId) => lineClosures.Any(closure => closure.LineId == lineId);

    /// <summary>
    /// The closure's own preconditions WITHOUT the fact — the expected version, the lifecycle (neither cancelled nor closed), an
    /// order that is Approved now (no pending revision, no customer-rejection fallback) and customer-accepted (ASSUMPTION-LC-3), the line and its not being closed yet — so the caller
    /// refuses a stale, terminal, never-approved, unknown-line or already closed command before it asks any owner.
    /// </summary>
    public SalesOrderDraftLine RequireLineClosable(long expectedVersion, Guid lineId)
    {
        RequireVersion(expectedVersion);
        RequireNotCancelled("İptal edilmiş siparişin satırı kapatılmaz.");
        if (State == SalesDraftState.Closed)
        {
            throw StateConflict("Kapalı siparişin satırı ayrıca kapatılmaz.");
        }

        if (ApprovalState != InternalApprovalState.Approved || CustomerAcceptedFallback is not null)
        {
            // Fail closed: the line's measured quantity is the CURRENT approved one only while the order is Approved with no pending
            // revision and no customer-rejection fallback; a closure over a revision still to be decided (or over the older accepted
            // one) could be changed by the approval afterwards, so it is refused until the order is settled.
            throw StateConflict("Satır yalnız onaylı ve karar bekleyen revizyonu olmayan siparişte kapatılır.");
        }

        if (!IsCustomerAccepted)
        {
            // ASSUMPTION-LC-3 (fail closed): while the current revision awaits the customer's acceptance a rejection could still send
            // the order back to the older accepted revision, so a closure only ever happens on a revision the customer accepted
            // (or on an order without the acceptance gate). This is what lets a customer rejection stay possible after a closure.
            throw StateConflict("Revizyon müşteri kabulü beklerken satır eksikle kapatılamaz.");
        }

        var line = lines.SingleOrDefault(candidate => candidate.Id == lineId)
            ?? throw new SalesRuleException(SalesRuleCodes.LineClosureLineUnknown, "Satır kapatma siparişin bir satırını adlandırır.");
        if (IsLineClosed(lineId))
        {
            throw StateConflict("Satır zaten kapatılmış.");
        }

        return line;
    }

    /// <summary>
    /// Records the closure of one line with the shortfall the caller judged (<see cref="LineClosureInput"/>): the preconditions
    /// (<see cref="RequireLineClosable"/>), SAT-D216 (the closer is none of the shippers the delivery owner names) and a shipped
    /// total above zero and below the measured quantity. One version step.
    /// </summary>
    public LineClosureFacts CloseLine(long expectedVersion, string actorId, LineClosureInput input, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actor = RequireActor(actorId);
        var line = RequireLineClosable(expectedVersion, input.LineId);
        if (input.ShippedBy is null || input.ShippedBy.Count == 0
            || input.ShippedBy.Any(shipper => string.IsNullOrWhiteSpace(shipper) || shipper.Length > MaximumActorLength))
        {
            throw new ArgumentException("Satır kapatma, sevki yapan kişileri bilinen bir sevk toplamıyla kaydedilir.", nameof(input));
        }

        if (input.ShippedBy.Any(shipper => string.Equals(shipper.Trim(), actor, StringComparison.Ordinal)))
        {
            // SAT-D216: the person who made the shipment does not close their own line ("başkası kapatır").
            throw new SalesRuleException(SalesRuleCodes.LineClosureSelfClosure,
                "Sevki yapan kişi kendi satırını kapatamaz; başkası kapatır.");
        }

        if (input.Band == DeliveryToleranceBand.Block)
        {
            // SAT-D17: the block band never closes; the service answers 422 first, the domain refuses it too (defence in depth).
            throw new ArgumentException("Engel bandındaki satır bu yolla kapatılmaz.", nameof(input));
        }

        if (input.BasisRevision < 0 || input.BasisRevision > Revision || !Enum.IsDefined(input.Band)
            || !TryLineQuantity(line, input.Ordered, out var ordered) || !TryLineQuantity(line, input.Shipped, out var shipped))
        {
            throw new ArgumentException("Ölçülen miktar ya da sevk toplamı satırın biriminde geçerli değil.", nameof(input));
        }

        if (!string.Equals(Text(ordered), line.QuantityText, StringComparison.Ordinal))
        {
            // The measured quantity is the line's current approved one (the closure entry accepts only a settled order).
            throw new ArgumentException("Ölçülen miktar satırın onaylı miktarıyla aynı olmalıdır.", nameof(input));
        }

        if (shipped.IsZero || !ordered.IsGreaterThan(shipped))
        {
            throw new SalesRuleException(SalesRuleCodes.StateConflict, "Satırda kapatılacak bir eksik teslim yok.");
        }

        var facts = new LineClosureFacts(line.Id, input.BasisRevision, Text(ordered), Text(shipped), Text(ordered.Subtract(shipped)),
            input.Band, actor, now.ToUniversalTime());
        lineClosures.Add(facts);
        Touch(now);
        return facts;
    }

    /// <summary>
    /// The store's rebuild of one recorded line closure, in recorded order, after the lines and archived revisions and BEFORE the
    /// approval axis (its version floor counts one step per closure). A stored closure that does not fit (an unknown or twice
    /// closed line, a basis revision above the current one, quantities not of the line's unit, a shipped total that is zero or not
    /// below the measured quantity, a remainder that is not their difference, an undefined band or the block band that never
    /// closes (SAT-D17, as <see cref="CloseLine"/>), blank facts) is refused, not repaired.
    /// </summary>
    public void RestoreLineClosure(LineClosureFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        RequireApprovalNotRestored();
        var line = lines.SingleOrDefault(candidate => candidate.Id == facts.LineId);
        if (line is null || IsLineClosed(facts.LineId)
            || facts.BasisRevision < 0 || facts.BasisRevision > Revision || !Enum.IsDefined(facts.Band)
            || facts.Band == DeliveryToleranceBand.Block
            || !TryExact(facts.Ordered, out var orderedValue) || !TryLineQuantity(line, orderedValue, out var ordered)
            || !TryExact(facts.ShippedAtClosure, out var shippedValue) || !TryLineQuantity(line, shippedValue, out var shipped)
            || !TryExact(facts.CancelledRemainder, out var remainderValue) || !TryLineQuantity(line, remainderValue, out var remainder)
            || !string.Equals(facts.Ordered, Text(ordered), StringComparison.Ordinal)
            || !string.Equals(facts.Ordered, line.QuantityText, StringComparison.Ordinal)
            || !string.Equals(facts.ShippedAtClosure, Text(shipped), StringComparison.Ordinal)
            || !string.Equals(facts.CancelledRemainder, Text(remainder), StringComparison.Ordinal)
            || shipped.IsZero || !ordered.IsGreaterThan(shipped) || ordered.Subtract(shipped) != remainder
            || string.IsNullOrWhiteSpace(facts.ClosedBy) || facts.ClosedBy.Length > MaximumActorLength)
        {
            throw new InvalidOperationException("Kayıtlı satır kapatması siparişin satırlarıyla uyuşmuyor.");
        }

        lineClosures.Add(facts with { ClosedBy = facts.ClosedBy.Trim(), ClosedAtUtc = facts.ClosedAtUtc.ToUniversalTime() });
    }
}
