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

        var removals = new List<(SalesOrderDraftLine Line, Guid? ReplacedBy)>(removed.Count);
        foreach (var removal in removed)
        {
            var line = lines.SingleOrDefault(candidate => candidate.Id == removal.LineId)
                ?? throw new SalesRuleException(SalesRuleCodes.RevisionLinesMismatch,
                    "Çıkarılan satır siparişin etkin bir satırı değil.", removal.LineId);
            if (line.Remainder != LineRemainderState.Open)
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
            archivedRevisions.Add(archived);
            Revision += 1;
            ApprovalState = InternalApprovalState.Preparing;
            Approval = ApprovalFacts.None;
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
