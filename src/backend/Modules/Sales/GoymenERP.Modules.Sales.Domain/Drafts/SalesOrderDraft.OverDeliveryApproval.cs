using System.Globalization;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D30 (EK3): an over-delivery in the approval band ("onay eşiği ile blok eşiği arasında") awaits an approval; once approved
/// the line is "Tamamlandı + onaylı fazla teslim". SAT-D168 (EK24) / SAT-D187–D190 (EK28): Satış Uzmanı, Satış Müdürü, Genel
/// Müdür and Admin approve it — a permission (<c>sales.orders.over-delivery.approve</c>), never a role name here. SAT-D213 (EK37):
/// the approval covers the SHIPPED total at the approval; a larger shipped total needs a new approval. SAT-D216 (EK37): the person
/// who made the shipment does not approve their own over-delivery. The domain half:
/// <list type="bullet">
/// <item><b>The band is the caller's judgement:</b> the shipped total (Logistics) and the tolerance (Settings) are not this
/// aggregate's knowledge; the caller reads them and judges the line with <see cref="SalesRealizationCalculator"/> BEFORE this
/// runs. The domain refuses what it can see: a terminal order, an unknown line, a shipped total that is not above the measured
/// quantity, one an earlier approval already covers, and an approver the delivery owner names as a shipper.</item>
/// <item><b>Append-only:</b> an approval is one fact with one version step; nothing on the line, the approval axis or the revision
/// moves, and no stock or money event exists.</item>
/// </list>
/// </summary>
public sealed partial class SalesOrderDraft
{
    private readonly List<OverDeliveryApprovalFacts> overDeliveryApprovals = [];

    /// <summary>SAT-D30: every recorded over-delivery approval, in the order they were recorded; empty until one is approved.</summary>
    public IReadOnlyList<OverDeliveryApprovalFacts> OverDeliveryApprovals => overDeliveryApprovals.AsReadOnly();

    /// <summary>
    /// SAT-D30 / SAT-D213: whether a recorded approval of <paramref name="lineId"/> covers the over-delivery of
    /// <paramref name="shipped"/> against <paramref name="ordered"/> — an approval judged against the same measured quantity whose
    /// approved shipped total is at least <paramref name="shipped"/> ("onay anındaki sevk miktarını kapsar"). A shipped total above
    /// every approved one, or an approval judged against another measured quantity (a later revision changed the line), is not
    /// covered: the line awaits a new approval (fail-closed). Not an over-delivery (shipped ≤ ordered) is never "approved".
    /// </summary>
    public bool IsOverDeliveryApproved(Guid lineId, Quantity ordered, Quantity shipped)
    {
        if (!shipped.IsGreaterThan(ordered))
        {
            return false;
        }

        foreach (var approval in overDeliveryApprovals)
        {
            if (approval.LineId == lineId
                && TryExact(approval.Ordered, out var approvedOrdered) && approvedOrdered == ordered.Value
                && TryExact(approval.ApprovedShipped, out var approvedShipped) && shipped.Value <= approvedShipped)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The approval's own preconditions WITHOUT the fact — the expected version, the lifecycle (neither cancelled nor closed), an
    /// order approved at least once (its current revision, or an earlier one archived by a revision awaiting re-approval: nothing
    /// ships on an order never approved, a withdrawn one included) and the line — so the caller refuses a stale, terminal,
    /// never-approved or unknown-line command before it asks any owner.
    /// </summary>
    public SalesOrderDraftLine RequireOverDeliveryApprovable(long expectedVersion, Guid lineId)
    {
        RequireVersion(expectedVersion);
        RequireNotCancelled("İptal edilmiş siparişte fazla teslim onaylanmaz.");
        if (ApprovalState != InternalApprovalState.Approved && archivedRevisions.Count == 0)
        {
            throw StateConflict("Hiç onaylanmamış siparişte fazla teslim onaylanmaz.");
        }

        return lines.SingleOrDefault(line => line.Id == lineId)
            ?? throw new SalesRuleException(SalesRuleCodes.OverDeliveryLineUnknown, "Fazla teslim onayı siparişin bir satırını adlandırır.");
    }

    /// <summary>
    /// Records the approval of one line's approval-band over-delivery the caller judged (<see cref="OverDeliveryApprovalInput"/>):
    /// the preconditions (<see cref="RequireOverDeliveryApprovable"/>), SAT-D216 (the approver is none of the shippers the delivery
    /// owner names), a shipped total above the measured quantity that no earlier approval covers (SAT-D213). One version step.
    /// </summary>
    public OverDeliveryApprovalFacts ApproveOverDelivery(long expectedVersion, string actorId, OverDeliveryApprovalInput input,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actor = RequireActor(actorId);
        var line = RequireOverDeliveryApprovable(expectedVersion, input.LineId);
        if (input.ShippedBy is null || input.ShippedBy.Count == 0
            || input.ShippedBy.Any(shipper => string.IsNullOrWhiteSpace(shipper) || shipper.Length > MaximumActorLength))
        {
            throw new ArgumentException("Fazla teslim onayı, sevki yapan kişileri bilinen bir sevk toplamıyla kaydedilir.", nameof(input));
        }

        if (input.ShippedBy.Any(shipper => string.Equals(shipper.Trim(), actor, StringComparison.Ordinal)))
        {
            // SAT-D216: the person who made the shipment does not approve their own over-delivery ("başkası onaylar").
            throw new SalesRuleException(SalesRuleCodes.OverDeliverySelfApproval,
                "Sevki yapan kişi kendi fazla tesliminin onayını veremez; başkası onaylar.");
        }

        if (input.BasisRevision < 0 || input.BasisRevision > Revision
            || !TryLineQuantity(line, input.Ordered, out var ordered) || !TryLineQuantity(line, input.Shipped, out var shipped))
        {
            throw new ArgumentException("Ölçülen miktar ya da sevk toplamı satırın biriminde geçerli değil.", nameof(input));
        }

        if (!shipped.IsGreaterThan(ordered))
        {
            throw new SalesRuleException(SalesRuleCodes.StateConflict, "Satırda onaylanacak bir fazla teslim yok.");
        }

        if (IsOverDeliveryApproved(line.Id, ordered, shipped))
        {
            throw new SalesRuleException(SalesRuleCodes.StateConflict, "Satırın bu sevk toplamındaki fazla teslimi zaten onaylı.");
        }

        var facts = new OverDeliveryApprovalFacts(line.Id, NextOverDeliveryApprovalNo(line.Id), input.BasisRevision, Text(ordered),
            Text(shipped), actor, now.ToUniversalTime());
        overDeliveryApprovals.Add(facts);
        Touch(now);
        return facts;
    }

    /// <summary>
    /// The store's rebuild of one recorded approval, in recorded order, after the lines and archived revisions and BEFORE the
    /// approval axis (its version floor counts one step per approval). A stored approval that does not fit (an unknown line, a
    /// number out of sequence, a basis revision above the current one, quantities not of the line's unit, a shipped total not above
    /// the measured quantity, blank facts) is refused, not repaired.
    /// </summary>
    public void RestoreOverDeliveryApproval(OverDeliveryApprovalFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        RequireApprovalNotRestored();
        var line = lines.SingleOrDefault(candidate => candidate.Id == facts.LineId);
        if (line is null || facts.ApprovalNo != NextOverDeliveryApprovalNo(facts.LineId)
            || facts.BasisRevision < 0 || facts.BasisRevision > Revision
            || !TryExact(facts.Ordered, out var orderedValue) || !TryLineQuantity(line, orderedValue, out var ordered)
            || !TryExact(facts.ApprovedShipped, out var shippedValue) || !TryLineQuantity(line, shippedValue, out var shipped)
            || !string.Equals(facts.Ordered, Text(ordered), StringComparison.Ordinal)
            || !string.Equals(facts.ApprovedShipped, Text(shipped), StringComparison.Ordinal)
            || !shipped.IsGreaterThan(ordered)
            || string.IsNullOrWhiteSpace(facts.ApprovedBy) || facts.ApprovedBy.Length > MaximumActorLength)
        {
            throw new InvalidOperationException("Kayıtlı fazla teslim onayı siparişin satırlarıyla uyuşmuyor.");
        }

        overDeliveryApprovals.Add(facts with { ApprovedBy = facts.ApprovedBy.Trim(), ApprovedAtUtc = facts.ApprovedAtUtc.ToUniversalTime() });
    }

    private int NextOverDeliveryApprovalNo(Guid lineId) => overDeliveryApprovals.Count(approval => approval.LineId == lineId) + 1;

    private static bool TryLineQuantity(SalesOrderDraftLine line, string text, out Quantity quantity)
    {
        quantity = default;
        return TryExact(text, out var value) && TryLineQuantity(line, value, out quantity);
    }

    private static bool TryLineQuantity(SalesOrderDraftLine line, decimal value, out Quantity quantity)
    {
        quantity = default;
        try
        {
            quantity = Quantity.Of(value, line.Quantity.Unit);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (SalesRuleException)
        {
            return false;
        }
    }

    private static bool TryExact(string? text, out decimal value)
    {
        value = default;
        return !string.IsNullOrEmpty(text) && text.Length <= 32
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
