namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D251..SAT-D255 (EK50) / API-11: the order source plan, the domain half.
/// <list type="bullet">
/// <item><b>Not line content (SAT-D253 (2)):</b> a plan save changes no customer, product, quantity, price, discount, money, due
/// term, tax or risk exception; it is NOT a revision (the revision number and the approval axis never move) and needs no
/// re-approval or customer acceptance. It is one version step of the order (the expected-version fence of every critical
/// command).</item>
/// <item><b>Until delivery (SAT-D253 (1)/(3)):</b> refused on a cancelled or closed order, a closed line and a fully delivered line
/// (locked part at or above the line quantity). Fail-closed while SAT-D219's NEEDS-SINAN on a line whose removal/product change
/// awaits a decision is open: a revision awaiting its decision, a customer-rejection fallback and a withdrawn order refuse.</item>
/// <item><b>Facts are the caller's reads:</b> the locked part (the delivery owner's shipped total), the stock part (the active
/// allocation; SAT-D255) and the linked-supply warning are read by the caller BEFORE this runs; the domain checks the shape and
/// the ONE equality of <see cref="SourcePlanCalculator.IsBalanced"/>.</item>
/// <item><b>Append-only:</b> the recorded plan lines are stored as new rows by the store; they are not part of the restored
/// aggregate (no other rule of the order depends on them).</item>
/// </list>
/// </summary>
public sealed partial class SalesOrderDraft
{
    /// <summary>
    /// The plan's order-level preconditions WITHOUT the facts — expected version, lifecycle and the fail-closed approval states —
    /// so the caller refuses a stale or terminal command before it asks any owner.
    /// </summary>
    public void RequireSourcePlanWritable(long expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireNotCancelled("İptal edilmiş siparişin kaynak planı değiştirilmez.");
        if (ApprovalState == InternalApprovalState.Withdrawn)
        {
            throw StateConflict("Geri çekilmiş siparişin kaynak planı değiştirilmez.");
        }

        if ((Revision > 0 && ApprovalState != InternalApprovalState.Approved) || CustomerAcceptedFallback is not null)
        {
            // Fail closed (SAT-D219's open NEEDS-SINAN, kept open by EK50): a revision still to be decided may remove a line or
            // change its product, and a customer-rejection fallback shows an older accepted revision; no plan is saved over either.
            throw StateConflict("Karar bekleyen revizyonu ya da müşteri reddi geri dönüşü olan siparişin kaynak planı değiştirilmez.");
        }
    }

    /// <summary>
    /// One planned line's own preconditions: an active line of the order (else <see cref="SalesRuleCodes.SourcePlanLineUnknown"/>),
    /// not closed with a shortfall (state conflict) and in kilograms (else <see cref="SalesRuleCodes.SourcePlanUnitUnsupported"/>:
    /// the allocation answers kilograms and no conversion is decided — NEEDS-SINAN).
    /// </summary>
    public SalesOrderDraftLine RequireSourcePlanLine(Guid lineId)
    {
        var line = lines.SingleOrDefault(candidate => candidate.Id == lineId)
            ?? throw new SalesRuleException(SalesRuleCodes.SourcePlanLineUnknown, "Kaynak planı siparişin etkin bir satırını adlandırır.");
        if (IsLineClosed(lineId))
        {
            throw StateConflict("Kapatılmış satırın kaynak planı değiştirilmez.");
        }

        if (!line.OrderUnit.IsKilogram || line.Quantity.Unit != QuantityUnit.Kilogram)
        {
            throw new SalesRuleException(SalesRuleCodes.SourcePlanUnitUnsupported,
                "Kilogram dışındaki birimde tahsis miktarı satır birimine çevrilmez; kaynak planı kaydedilmez.", lineId);
        }

        return line;
    }

    /// <summary>
    /// Records the plan of the listed lines (<paramref name="input"/>): the preconditions, distinct lines, every part a valid
    /// non-negative kilogram, a line that is not fully delivered (<see cref="SalesRuleCodes.SourcePlanLineLocked"/>) and the ONE
    /// equality per line (<see cref="SalesRuleCodes.SourcePlanSumMismatch"/>). One version step for the whole save; the facts carry
    /// the new version.
    /// </summary>
    public SourcePlanFacts RecordSourcePlan(long expectedVersion, string actorId, SourcePlanInput input, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actor = RequireActor(actorId);
        RequireSourcePlanWritable(expectedVersion);
        if (input.Lines is null || input.Lines.Count == 0 || input.Lines.Any(line => line is null)
            || input.Lines.Select(line => line.LineId).Distinct().Count() != input.Lines.Count)
        {
            throw new ArgumentException("Kaynak planı en az bir satırı birer kez adlandırır.", nameof(input));
        }

        var planned = new List<(SalesOrderDraftLine Line, SourcePlanLineInput Input)>(input.Lines.Count);
        foreach (var entry in input.Lines)
        {
            var line = RequireSourcePlanLine(entry.LineId);
            if (!IsPart(entry.Locked) || !IsPart(entry.Stock) || !IsPart(entry.Production) || !IsPart(entry.Purchasing)
                || !IsPart(entry.Subcontract))
            {
                throw new ArgumentException("Kaynak planı parçaları satırın biriminde negatif olmayan tam miktarlardır.", nameof(input));
            }

            if (entry.Locked >= line.Quantity.Value)
            {
                // SAT-D253 (3): a fully delivered line's plan no longer changes.
                throw new SalesRuleException(SalesRuleCodes.SourcePlanLineLocked, "Tamamen teslim edilmiş satırın kaynak planı değiştirilmez.", line.Id);
            }

            if (!SourcePlanCalculator.IsBalanced(line.Quantity.Value, entry.Locked, entry.Stock, entry.Production, entry.Purchasing,
                    entry.Subcontract))
            {
                throw new SalesRuleException(SalesRuleCodes.SourcePlanSumMismatch,
                    "Kaynak planı parçalarının toplamı satır miktarına eşit olmalıdır.", line.Id);
            }

            planned.Add((line, entry));
        }

        Touch(now);
        var savedAt = now.ToUniversalTime();
        return new SourcePlanFacts([.. planned.Select(entry => new SourcePlanLineFacts(entry.Line.Id, entry.Line.LineNo,
            entry.Line.Quantity.Value, entry.Input.Locked, entry.Input.Stock, entry.Input.Production, entry.Input.Purchasing,
            entry.Input.Subcontract, input.LinkedSupplyConfirmed, actor, savedAt, Version))]);
    }

    private static bool IsPart(decimal value)
    {
        if (value < 0m)
        {
            return false;
        }

        try
        {
            _ = Quantity.Of(value, QuantityUnit.Kilogram);
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
}
