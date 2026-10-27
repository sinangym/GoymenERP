namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D2 (Sinan, 2026-10-25): "Satış iskontosu limiti — Rol bazlı yüzde limiti, Ayarlar'dan; limiti aşan iskonto Müdür
/// onayına gider. Limiti tanımlı olmayan rol için her iskonto onaya düşer (fail-closed)." As applied by SAT-D34…D37
/// (<c>SINAN_KARARLARI_2026-10-27_SATIS_EK4.md</c>) and SAT-D42/D43. API-12 of the Sales plan: the price-floor exception's
/// maker-checker shape (AT-021), for the discount.
/// <list type="bullet">
/// <item><b>Which line (SAT-D35/D42/D43):</b> with a list-price reference, a line whose discounted unit price BEFORE the
/// maturity surcharge is below the list price — a manual price below list counts even with zero discount fields, and a
/// surcharge never hides a discount; without a reference, EVERY priced line, whatever its discount fields
/// (<see cref="SalesLinePriceCalculator.NeedsDiscountCheck"/>).</item>
/// <item><b>Check:</b> made at the create and at every commercial revision for the actor who set the price. With a
/// reference the discount is (list − discounted) / list, compared exactly with the limit the caller read from the Settings
/// owner (SAT-D34: the highest limit of the actor's groups); exactly AT the limit is within it. No limit defined (SAT-D36),
/// or no list reference (SAT-D43): approval required. The check is kept with the line
/// (<see cref="DiscountLimitCheckSnapshot"/>) and bound to its exact price, discount, discounted price and list reference.</item>
/// <item><b>Approval (SAT-D37, SAT-D45):</b> every current line needing it is approved at once with a reason, under the caller's
/// dedicated permission (Müdür's persona, a grant). On a line with a list price, whoever set the discount never approves it
/// (SAT-D37); a line without a list price may be approved by its own price writer (SAT-D45). Nothing is changed or
/// recomputed.</item>
/// <item><b>Gate:</b> a draft is not submitted or approved while such a line is unchecked or needs an approval it does not
/// have. Any commercial revision replaces the check (and with it any approval).</item>
/// </list>
/// </summary>
public sealed partial class SalesOrderDraft
{
    /// <summary>
    /// SAT-D2 / SAT-D35 / SAT-D43: the line's commercial fields with the discount-limit check for <paramref name="actorId"/>
    /// under <paramref name="limit"/> — a check when the line needs one, none otherwise. Used by the create (whose lines are
    /// added with their checks) and by the commercial revision; the fields are the line's validated texts. A text the
    /// calculator cannot read is left unchecked: the line's own rules refuse it before anything is stored.
    /// </summary>
    public static CommercialDraftFields WithDiscountLimitCheck(CommercialDraftFields commercial, SalesDiscountLimit limit,
        string actorId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(commercial);
        ArgumentNullException.ThrowIfNull(limit);
        var actor = RequireActor(actorId);
        RequireLimitShape(limit);
        if (!IsCheckable(commercial) || commercial.UnitPrice is not { } unitPrice || !SalesLinePriceCalculator.NeedsDiscountCheck(commercial))
        {
            return commercial with { DiscountCheck = null };
        }

        var reference = commercial.Reference;
        var requiresApproval = reference is null || limit.MaximumPercent is not { } maximum
            || SalesLinePriceCalculator.DiscountExceedsListPercent(commercial, maximum);
        return commercial with
        {
            DiscountCheck = new DiscountLimitCheckSnapshot(actor, now.ToUniversalTime(), unitPrice, commercial.DiscountPercent,
                commercial.DiscountAmount, SalesLinePriceCalculator.DiscountedUnitPriceText(commercial)!, reference?.PriceListId,
                reference?.VersionNo, reference?.UnitPrice,
                limit.MaximumPercent is { } percent ? SalesLinePriceCalculator.Format(percent) : null,
                limit.ProfileId, limit.ProfileVersion, requiresApproval),
        };
    }

    /// <summary>
    /// API-12: approves every current over-limit discount line as one keyed mutation. The approver must differ from every
    /// actor who set those discounts; reasons are required and bounded by the Sales approval-reason limit. No price or
    /// discount is changed or recomputed here; only the lines' checks gain the approval facts.
    /// </summary>
    public void ApproveDiscountExceptions(long expectedVersion, IReadOnlyList<LineDiscountExceptionApprovalInput> inputs,
        string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        ArgumentNullException.ThrowIfNull(inputs);
        RequireNotCancelled("İptal edilmiş siparişin iskonto istisnası onaylanmaz.");
        if (!LinesAreOpen)
        {
            throw StateConflict("İskonto istisnası yalnız hazırlanan veya reddedilmiş taslakta onaylanır.");
        }

        var overLimit = lines.Where(line => NeedsDiscountApproval(line.Commercial)).ToArray();
        if (overLimit.Length == 0 || inputs.Count != overLimit.Length
            || inputs.Any(input => input is null)
            || inputs.Select(input => input.LineId).Distinct().Count() != inputs.Count
            || inputs.Any(input => overLimit.All(line => line.Id != input.LineId)))
        {
            throw new SalesRuleException(SalesRuleCodes.DiscountExceptionLinesMismatch,
                "İskonto istisnası onayı limiti aşan her güncel satırı tam bir kez adlandırır.");
        }

        var approvals = new List<(SalesOrderDraftLine Line, DiscountLimitCheckSnapshot Check, string Reason)>(overLimit.Length);
        foreach (var input in inputs)
        {
            var line = overLimit.Single(candidate => candidate.Id == input.LineId);
            var check = line.Commercial.DiscountCheck
                ?? throw new SalesRuleException(SalesRuleCodes.DiscountExceptionPending,
                    "Satırın iskontosu limit kontrolünden geçmemiş; önce fiyat revizyonu ile yeniden verilir.", line.Id);
            if (check.IsApproved)
            {
                throw new SalesRuleException(SalesRuleCodes.RevisionNoChange, "İskonto istisnası zaten onaylı.", line.Id);
            }

            if (string.Equals(check.CheckedBy, actor, StringComparison.Ordinal) && !IsSelfApprovable(line.Commercial))
            {
                throw new SalesRuleException(SalesRuleCodes.DiscountExceptionSameActor,
                    "İskontoyu veren aktör aynı iskonto istisnasını onaylayamaz.", line.Id);
            }

            var reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
            if (reason is null)
            {
                throw new SalesRuleException(SalesRuleCodes.DiscountExceptionReasonRequired, "İskonto istisnası onay gerekçesi zorunludur.", line.Id);
            }

            if (reason.Length > MaximumRejectionReasonLength)
            {
                throw new SalesRuleException(SalesRuleCodes.ApprovalReasonTooLong,
                    $"İskonto istisnası onay gerekçesi en fazla {MaximumRejectionReasonLength} karakterdir.", line.Id);
            }

            approvals.Add((line, check, reason));
        }

        foreach (var (line, check, reason) in approvals)
        {
            line.ReplaceCommercial(line.Commercial with
            {
                DiscountCheck = check with { ApprovedBy = actor, ApprovedAtUtc = now.ToUniversalTime(), Reason = reason },
            });
        }

        Touch(now);
    }

    /// <summary>
    /// SAT-D45 (Sinan, 2026-10-27): a line WITHOUT a list price has no measurable discount; its only approval reason is SAT-D43,
    /// and a holder of the approval permission may approve the whole line themselves, discount fields included. The SAT-D37
    /// ban (whoever set the discount cannot approve it) applies to lines WITH a list price, measured below it.
    /// </summary>
    private static bool IsSelfApprovable(CommercialDraftFields commercial) => commercial.Reference is null;

    /// <summary>Whether a line that needs the check still waits for an approval: unchecked, or its check requires one.</summary>
    private static bool NeedsDiscountApproval(CommercialDraftFields commercial) =>
        SalesLinePriceCalculator.NeedsDiscountCheck(commercial)
        && (commercial.DiscountCheck is not { } check || check.RequiresApproval);

    private void RequireDiscountExceptionsApproved()
    {
        if (lines.Any(line => NeedsDiscountApproval(line.Commercial) && line.Commercial.DiscountCheck?.IsApproved != true))
        {
            throw new SalesRuleException(SalesRuleCodes.DiscountExceptionPending,
                "Limiti aşan (ya da limiti tanımlı olmayan) her iskonto farklı yetkili tarafından gerekçeli onaylanmalıdır.");
        }
    }

    /// <summary>
    /// The line's check, when present, is bound to its exact unit price, discount fields, discounted price and list reference
    /// and agrees with the limit it records — a stored or supplied check that says otherwise is an internal invariant, never
    /// repaired. A line that needs no check carries none; a line that needs one without one is accepted here (stored before
    /// SAT-D2) and blocks the submission until a commercial revision checks it.
    /// </summary>
    private static void ValidateDiscountCheck(CommercialDraftFields commercial, Guid lineId)
    {
        if (commercial.DiscountCheck is not { } check)
        {
            return;
        }

        if (commercial.UnitPrice is null || !SalesLinePriceCalculator.NeedsDiscountCheck(commercial))
        {
            throw new SalesRuleException(SalesRuleCodes.InternalInvariant, "İskonto kontrolü gerektirmeyen satır iskonto limit kontrolü taşıyamaz.", lineId);
        }

        var reference = commercial.Reference;
        var referenceBound = reference is null
            ? check.PriceListId is null && check.PriceListVersionNo is null && check.ListUnitPrice is null
            : check.PriceListId == reference.PriceListId && check.PriceListVersionNo == reference.VersionNo
                && string.Equals(check.ListUnitPrice, reference.UnitPrice, StringComparison.Ordinal);

        var limitShaped = check.LimitPercent is null
            ? check.LimitProfileId is null && check.LimitProfileVersion is null
            : check.LimitProfileId is { } profile && profile != Guid.Empty && check.LimitProfileVersion is >= 1
                && SalesLinePriceCalculator.TryParseExact(check.LimitPercent, out _) && IsPercent(check.LimitPercent);
        var approvalShaped = check.ApprovedBy is null
            ? check.ApprovedAtUtc is null && check.Reason is null
            : check.ApprovedAtUtc is not null && !string.IsNullOrWhiteSpace(check.ApprovedBy)
                && !string.IsNullOrWhiteSpace(check.Reason) && check.Reason.Length <= MaximumRejectionReasonLength
                && check.RequiresApproval
                && (IsSelfApprovable(commercial) || !string.Equals(check.CheckedBy, check.ApprovedBy, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(check.CheckedBy) || !limitShaped || !approvalShaped
            || !string.Equals(check.UnitPrice, commercial.UnitPrice, StringComparison.Ordinal)
            || !string.Equals(check.DiscountPercent, commercial.DiscountPercent, StringComparison.Ordinal)
            || !string.Equals(check.DiscountAmount, commercial.DiscountAmount, StringComparison.Ordinal)
            || !string.Equals(check.DiscountedUnitPrice, SalesLinePriceCalculator.DiscountedUnitPriceText(commercial), StringComparison.Ordinal)
            || !referenceBound
            || check.RequiresApproval != (reference is null || check.LimitPercent is not { } limit
                || SalesLinePriceCalculator.DiscountExceedsListPercent(commercial, SalesLinePriceCalculator.Parse(limit))))
        {
            throw new SalesRuleException(SalesRuleCodes.InternalInvariant, "İskonto limit kontrolü bağlı olduğu ticari değerlerle uyuşmuyor.", lineId);
        }
    }

    private static void RequireLimitShape(SalesDiscountLimit limit)
    {
        var consistent = limit.MaximumPercent is { } maximum
            ? maximum >= 0m && maximum <= 100m && limit.ProfileId is { } profile && profile != Guid.Empty && limit.ProfileVersion is >= 1
            : limit.ProfileId is null && limit.ProfileVersion is null;
        if (!consistent)
        {
            throw new ArgumentException("İskonto limiti kendiyle çelişiyor.", nameof(limit));
        }
    }

    private static bool IsReadable(string? text) => text is null || SalesLinePriceCalculator.TryParseExact(text, out _);

    /// <summary>Every text the measurement reads is exact (the line's own rules refuse it otherwise) and the net is calculable.</summary>
    private static bool IsCheckable(CommercialDraftFields commercial)
    {
        if (!IsReadable(commercial.UnitPrice) || !IsReadable(commercial.DiscountPercent) || !IsReadable(commercial.DiscountAmount)
            || !IsReadable(commercial.SurchargePercent) || !IsReadable(commercial.SurchargeAmount)
            || (commercial.Reference is { } reference && !IsReadable(reference.UnitPrice)))
        {
            return false;
        }

        try
        {
            _ = SalesLinePriceCalculator.NetUnitPrice(commercial);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// SAT-D2 / SAT-D35 / SAT-D42: whether a line's discounted price is below its list-price reference — the only case where
    /// the actor's limit decides. The caller reads the limit from the Settings owner only then; any unreadable text answers false.
    /// </summary>
    public static bool IsMeasurablyDiscounted(CommercialDraftFields commercial)
    {
        ArgumentNullException.ThrowIfNull(commercial);
        return commercial.Reference is not null && commercial.UnitPrice is not null && IsCheckable(commercial)
            && SalesLinePriceCalculator.NeedsDiscountCheck(commercial);
    }
}
