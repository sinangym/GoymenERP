namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D167 (EK24): one line to take out of the order in a line revision — the line goes passive (never deleted). For a product
/// change, <see cref="ReplacedByLineId"/> names the line added in the same revision that replaces it.
/// </summary>
public sealed record LineRemovalInput(Guid LineId, Guid? ReplacedByLineId = null);

public sealed partial class SalesOrderDraft
{
    /// <summary>
    /// SAT-D167 (EK24, Sinan): "Revizyonla yeni satır eklenebilir, sevk edilmemiş satır silinebilir (pasif olur), sevk edilmemiş
    /// satırın ürünü değiştirilebilir. Hepsi Rev+1 + onay + müşteri kabulü." One line revision, under DEC-SAL-002's rules:
    /// <list type="bullet">
    /// <item><b>Add:</b> each new line passes every rule of a create line (<see cref="BuildLine"/>: snapshots, Ö2, commercial
    /// pairing, plan, tax snapshot, and on a subcontract order the service / kg / no-price-list rules) and takes the next line
    /// number; it records the revision it was added in.</item>
    /// <item><b>Remove:</b> an ACTIVE line goes passive (out of <see cref="Lines"/>: totals, risk, tax, print, every consumer read),
    /// recording the revision. Whether the line is unshipped (no dispatch note, loading instruction or stock allocation) is the
    /// caller's read of the owners, made before this runs; a line with a recorded remainder change (it was delivered) is refused here
    /// too (<see cref="SalesRuleCodes.LineRevisionLineShipped"/>). The order keeps at least one active line.</item>
    /// <item><b>Product change:</b> a removal whose <see cref="LineRemovalInput.ReplacedByLineId"/> names a line added in the same
    /// revision for ANOTHER product — the replacement's price, discount and tax are its own (resolved for the new product).</item>
    /// <item><b>Revision:</b> from <c>Approved</c> the approved revision is archived first (with the reason) and the order goes on
    /// as the next revision in <c>Preparing</c> (re-approval and a new customer acceptance); from <c>Preparing</c> / <c>Rejected</c>
    /// the lines simply change. <c>PendingApproval</c>, <c>Withdrawn</c>, a cancelled or closed order are refused (the quantity
    /// revision's preconditions, <see cref="RequireQuantityRevisable"/>). One version step whatever the number of lines.</item>
    /// </list>
    /// SAT-D212 (EK36): an added line's product eligibility, unit and tax are read for <paramref name="revisionDay"/> — the server's
    /// business day the line is added — not the order's business date; its snapshots must be taken for that day. Existing lines
    /// keep theirs.
    /// Returns the archived snapshot when one was made.
    /// </summary>
    public ApprovedRevisionSnapshot? ReviseLines(long expectedVersion, IReadOnlyList<SalesDraftLineInput> added,
        IReadOnlyList<LineRemovalInput> removed, string actorId, string? reason, DateTimeOffset now, DateOnly revisionDay)
    {
        var actor = RequireActor(actorId);
        var trimmedReason = RequireQuantityRevisable(expectedVersion, reason);
        ArgumentNullException.ThrowIfNull(added);
        ArgumentNullException.ThrowIfNull(removed);
        if (added.Count == 0 && removed.Count == 0)
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionNoChange, "Satır revizyonu hiçbir satırı eklemiyor ya da çıkarmıyor.");
        }

        if (added.Any(input => input is null) || removed.Any(input => input is null)
            || added.Select(input => input.LineId).Distinct().Count() != added.Count
            || removed.Select(input => input.LineId).Distinct().Count() != removed.Count)
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionLinesMismatch, "Satır revizyonu her satırı en fazla bir kez adlandırır.");
        }

        // SAT-D211: opening the next revision of a customer-rejected one reverts its product changes first; a replacement that the
        // revert takes out is not the line this request may remove.
        var revertedReplacements = ApprovalState == InternalApprovalState.Approved && IsCustomerRejected(Revision)
            ? removedLines.Where(line => line.RemovedInRevision == Revision && line.AddedInRevision != Revision)
                .Select(line => line.ReplacedByLineId).OfType<Guid>().ToHashSet()
            : [];
        if (removed.Any(removal => revertedReplacements.Contains(removal.LineId)))
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionLinesMismatch,
                "Müşterinin reddettiği ürün değişikliğinin yeni satırı geri alınır; ayrıca çıkarılamaz.");
        }

        var removals = new List<(SalesOrderDraftLine Line, Guid? ReplacedBy)>(removed.Count);
        foreach (var removal in removed)
        {
            var line = lines.SingleOrDefault(candidate => candidate.Id == removal.LineId)
                ?? throw new SalesRuleException(SalesRuleCodes.RevisionLinesMismatch,
                    "Çıkarılan satır siparişin etkin bir satırı değil.", removal.LineId);
            // K3 review A P3-1: a line with any remainder-change history (pending, rejected or approved) has shipped; its history
            // is restored against the active lines, so it never goes passive.
            if (line.Remainder != LineRemainderState.Open || remainderChanges.Any(change => change.Request.LineId == line.Id))
            {
                throw new SalesRuleException(SalesRuleCodes.LineRevisionLineShipped,
                    "Sevki olan satır silinmez ve ürünü değişmez (SAT-D167).", line.Id);
            }

            if (removal.ReplacedByLineId is { } replacement)
            {
                var replacing = added.SingleOrDefault(input => input.LineId == replacement)
                    ?? throw new SalesRuleException(SalesRuleCodes.LineReplacementInvalid,
                        "Ürünü değişen satırın yerine aynı revizyonda eklenen bir satır gelir.", line.Id);
                if (removed.Count(other => other.ReplacedByLineId == replacement) != 1)
                {
                    throw new SalesRuleException(SalesRuleCodes.LineReplacementInvalid,
                        "Bir yeni satır yalnız bir satırın yerine geçer.", line.Id);
                }

                if (replacing.Product.ProductId == line.Product.ProductId)
                {
                    throw new SalesRuleException(SalesRuleCodes.LineReplacementSameProduct,
                        "Ürün değişikliği başka bir ürün ister; aynı ürünün miktarı ya da fiyatı kendi revizyonuyla değişir.", line.Id);
                }
            }

            removals.Add((line, removal.ReplacedByLineId));
        }

        if (lines.Count - removals.Count + added.Count < 1)
        {
            throw new SalesRuleException(SalesRuleCodes.LineRevisionNoLinesLeft, "Siparişte en az bir etkin satır kalır.");
        }

        // K3 review A P1-1: the order's line limit counts every line it will hold — active, passive and added — before anything
        // changes (BuildLine counts only the lines held now), so no revision writes an order the rebuild refuses.
        if (lines.Count + removedLines.Count + added.Count > MaximumLines)
        {
            throw new SalesRuleException(SalesRuleCodes.TooManyLines, $"Bir sipariş pasif satırlarıyla birlikte en fazla {MaximumLines} satır taşır.");
        }

        // Every added line passes the create's rules before anything changes (a refusal names the added line).
        var built = new List<SalesOrderDraftLine>(added.Count);
        var numbered = lines.Count + removedLines.Count;
        foreach (var input in added)
        {
            try
            {
                var line = BuildLine(input, revisionDay);
                if (built.Any(other => other.Id == line.Id))
                {
                    throw new SalesRuleException(SalesRuleCodes.DuplicateLineId, "Aynı satır kimliği ikinci kez eklenemez.");
                }

                // BuildLine numbers against the lines held now; the added lines take the following numbers in request order.
                built.Add(new SalesOrderDraftLine(line.Id, numbered + built.Count + 1, line.Product, line.OrderUnit, line.Quantity,
                    line.QuantityText, line.RequestedDeliveryDate, line.Note, line.Commercial, line.RequestedDeliverySlices, line.Tax,
                    line.ItemType));
            }
            catch (SalesRuleException exception) when (exception.LineId is null)
            {
                throw new SalesRuleException(exception.Code, exception.Message, input.LineId);
            }
        }

        ApprovedRevisionSnapshot? archived = null;
        if (ApprovalState == InternalApprovalState.Approved)
        {
            // The approved revision as it stands — its lines and header — archived BEFORE anything of it changes, with the reason.
            archived = new ApprovedRevisionSnapshot(Revision, Approval, Money.ProfileId, Money.ProfileVersion, Money.Scale,
                now.ToUniversalTime(), actor, ArchiveLines(), trimmedReason, CurrentHeaderSnapshot());
            AdvanceRevision(archived);
        }

        foreach (var (line, replacedBy) in removals)
        {
            line.MarkRemoved(Revision, replacedBy);
            lines.Remove(line);
            removedLines.Add(line);
        }

        foreach (var line in built)
        {
            line.MarkAdded(Revision);
            lines.Add(line);
        }

        lines.Sort((left, right) => left.LineNo.CompareTo(right.LineNo));
        Touch(now);
        return archived;
    }

    /// <summary>
    /// The approved revision is archived (<paramref name="archived"/>, taken BEFORE anything of it changes) and the order goes on as
    /// the next revision in <c>Preparing</c>. SAT-D211 (EK36) with SAT-D115: when the customer had rejected the archived revision,
    /// its removals are reverted now — after the archive, which keeps the revision exactly as it was approved — so the removed lines
    /// are active again in the next revision (they shipped meanwhile under the last accepted revision).
    /// </summary>
    private void AdvanceRevision(ApprovedRevisionSnapshot archived)
    {
        var customerRejected = IsCustomerRejected(Revision);
        archivedRevisions.Add(archived);
        Revision += 1;
        ApprovalState = InternalApprovalState.Preparing;
        Approval = ApprovalFacts.None;
        if (customerRejected)
        {
            RevertRemovalsOf(Revision - 1);
        }
    }

    /// <summary>
    /// SAT-D211 (EK36): the removals of a REJECTED <paramref name="revision"/> are reverted — every line it removed is active again
    /// (<see cref="SalesOrderDraftLine.ReinstatedInRevision"/>), and a line it added to replace one of them (a product change) goes
    /// passive in that revision, so the order never holds both products. Nothing is reverted for an order never approved (no
    /// approved revision to ship under) and a line added and removed in the same revision stays passive (it was never approved).
    /// The lines that only the revision added stay as they are (its own content, never shipped before an approval).
    /// </summary>
    private void RevertRemovalsOf(int revision)
    {
        if (archivedRevisions.Count == 0)
        {
            return;
        }

        var reverted = removedLines.Where(line => line.RemovedInRevision == revision && line.AddedInRevision != revision).ToList();
        if (reverted.Count == 0)
        {
            return;
        }

        var replacements = reverted.Select(line => line.ReplacedByLineId).OfType<Guid>().ToHashSet();
        foreach (var line in reverted)
        {
            line.Reinstate(revision);
            removedLines.Remove(line);
            lines.Add(line);
        }

        foreach (var line in lines.Where(line => replacements.Contains(line.Id)).ToList())
        {
            line.MarkRemoved(revision, null);
            lines.Remove(line);
            removedLines.Add(line);
        }

        lines.Sort((left, right) => left.LineNo.CompareTo(right.LineNo));
    }

    /// <summary>The store's rebuild of a recorded reinstatement (SAT-D211); no version step. Runs before the approval axis is restored.</summary>
    public void RestoreReinstatement(Guid lineId, int reinstatedInRevision)
    {
        RequireApprovalNotRestored();
        var line = lines.SingleOrDefault(candidate => candidate.Id == lineId)
            ?? throw new InvalidOperationException("Kayıtlı geri dönen satır taslağın etkin satırlarında yok.");
        line.RestoreReinstated(reinstatedInRevision);
    }

    /// <summary>
    /// The store's rebuild of a line a line revision ADDED (SAT-D167): the create's line rules again, the stored number and revision,
    /// and NO version step (the revision that added it was one step of its own, counted with its archive or not at all, like every
    /// line write on a Preparing order). Runs before the approval axis is restored.
    /// </summary>
    public SalesOrderDraftLine RestoreAddedLine(SalesDraftLineInput input, int addedInRevision)
    {
        RequireApprovalNotRestored();
        // The stored snapshot day (SAT-D212: the revision day it was read for); the unit profile is checked in effect on that day.
        var line = BuildLine(input, input.Product.EffectiveOn);
        line.MarkAdded(addedInRevision);
        lines.Add(line);
        return line;
    }

    /// <summary>
    /// The store's rebuild of a removed (passive) line (SAT-D167): it leaves the active lines, keeping its number and data. A
    /// replacement must name another line of the order. No version step. Runs before the approval axis is restored.
    /// </summary>
    public void RestoreLineRemoval(Guid lineId, int removedInRevision, Guid? replacedByLineId)
    {
        RequireApprovalNotRestored();
        var line = lines.SingleOrDefault(candidate => candidate.Id == lineId)
            ?? throw new InvalidOperationException("Kayıtlı pasif satır taslağın etkin satırlarında yok.");
        if (replacedByLineId is { } replacement && AllLines.All(candidate => candidate.Id != replacement))
        {
            throw new InvalidOperationException("Kayıtlı pasif satırın yerine geçen satır taslakta yok.");
        }

        line.MarkRemoved(removedInRevision, replacedByLineId);
        lines.Remove(line);
        removedLines.Add(line);
    }
}
