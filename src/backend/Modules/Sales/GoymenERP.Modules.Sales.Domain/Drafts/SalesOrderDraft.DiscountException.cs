namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D2 (Sinan, 2026-10-25): "Satış iskontosu limiti — Rol bazlı yüzde limiti, Ayarlar'dan; limiti aşan iskonto Müdür
/// onayına gider. Limiti tanımlı olmayan rol için her iskonto onaya düşer (fail-closed)." API-12 of the Sales plan: the
/// price-floor exception's maker-checker shape (AT-021), for the discount.
/// <list type="bullet">
/// <item><b>Check:</b> every line given a POSITIVE discount (a percent or an amount above zero) is checked against the limit
/// that applies to the actor who set it — at the create and at every commercial revision. The caller reads the limit from the
/// Settings owner; the domain decides: a discount above the limit, or any positive discount when no limit is defined for the
/// actor, requires approval. Exactly AT the limit is within it ("limiti aşan"). The base of the percent is the line's unit
/// price (<see cref="SalesLinePriceCalculator.DiscountExceedsPercent"/>). The check is kept with the line
/// (<see cref="DiscountLimitCheckSnapshot"/>) and bound to its exact unit price and discount.</item>
/// <item><b>Approval:</b> another person (never the one who set the discount) approves every current over-limit line at once
/// with a reason, under the caller's dedicated permission. No price or discount is changed or recomputed.</item>
/// <item><b>Gate:</b> a draft is not submitted or approved while a positive-discount line is unchecked or its over-limit
/// discount is unapproved. Any commercial revision replaces the check (and with it any approval).</item>
/// </list>
/// </summary>
public sealed partial class SalesOrderDraft
{
    /// <summary>
    /// SAT-D2: the line's commercial fields with the discount-limit check for <paramref name="actorId"/> under
    /// <paramref name="limit"/> — a check when the discount is positive, none otherwise. Used by the create (whose lines are
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
        if (!IsReadable(commercial.UnitPrice) || !IsReadable(commercial.DiscountPercent) || !IsReadable(commercial.DiscountAmount)
            || commercial.UnitPrice is not { } unitPrice || !SalesLinePriceCalculator.HasPositiveDiscount(commercial))
        {
            return commercial with { DiscountCheck = null };
        }

        var requiresApproval = limit.MaximumPercent is not { } maximum
            || SalesLinePriceCalculator.DiscountExceedsPercent(commercial, maximum);
        return commercial with
        {
            DiscountCheck = new DiscountLimitCheckSnapshot(actor, now.ToUniversalTime(), unitPrice, commercial.DiscountPercent,
                commercial.DiscountAmount, limit.MaximumPercent is { } percent ? SalesLinePriceCalculator.Format(percent) : null,
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

            if (string.Equals(check.CheckedBy, actor, StringComparison.Ordinal))
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

    /// <summary>Whether a line's positive discount still waits for an approval: unchecked, or over the limit and unapproved.</summary>
    private static bool NeedsDiscountApproval(CommercialDraftFields commercial) =>
        SalesLinePriceCalculator.HasPositiveDiscount(commercial)
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
    /// The line's check, when present, is bound to its exact unit price and discount and agrees with the limit it records — a
    /// stored or supplied check that says otherwise is an internal invariant, never repaired. A line without a positive
    /// discount carries no check; a positive discount without one is accepted here (stored before SAT-D2) and blocks the
    /// submission until a commercial revision checks it.
    /// </summary>
    private static void ValidateDiscountCheck(CommercialDraftFields commercial, Guid lineId)
    {
        if (commercial.DiscountCheck is not { } check)
        {
            return;
        }

        if (!SalesLinePriceCalculator.HasPositiveDiscount(commercial) || commercial.UnitPrice is null)
        {
            throw new SalesRuleException(SalesRuleCodes.InternalInvariant, "İskontosu olmayan satır iskonto limit kontrolü taşıyamaz.", lineId);
        }

        var limitShaped = check.LimitPercent is null
            ? check.LimitProfileId is null && check.LimitProfileVersion is null
            : check.LimitProfileId is { } profile && profile != Guid.Empty && check.LimitProfileVersion is >= 1
                && SalesLinePriceCalculator.TryParseExact(check.LimitPercent, out _) && IsPercent(check.LimitPercent);
        var approvalShaped = check.ApprovedBy is null
            ? check.ApprovedAtUtc is null && check.Reason is null
            : check.ApprovedAtUtc is not null && !string.IsNullOrWhiteSpace(check.ApprovedBy)
                && !string.IsNullOrWhiteSpace(check.Reason) && check.Reason.Length <= MaximumRejectionReasonLength
                && check.RequiresApproval && !string.Equals(check.CheckedBy, check.ApprovedBy, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(check.CheckedBy) || !limitShaped || !approvalShaped
            || !string.Equals(check.UnitPrice, commercial.UnitPrice, StringComparison.Ordinal)
            || !string.Equals(check.DiscountPercent, commercial.DiscountPercent, StringComparison.Ordinal)
            || !string.Equals(check.DiscountAmount, commercial.DiscountAmount, StringComparison.Ordinal)
            || check.RequiresApproval != (check.LimitPercent is not { } limit
                || SalesLinePriceCalculator.DiscountExceedsPercent(commercial, SalesLinePriceCalculator.Parse(limit))))
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
}
