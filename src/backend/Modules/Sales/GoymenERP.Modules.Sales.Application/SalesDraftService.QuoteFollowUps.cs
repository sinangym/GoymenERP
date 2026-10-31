using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Quotes;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// S4 quote follow-ups (<c>docs/decisions/SINAN_KARARLARI_2026-10-27_SATIS_EK3.md</c>, <c>…_EK4.md</c>, <c>…_EK5.md</c>):
/// <list type="bullet">
/// <item><b>SAT-D24 / SAT-D44 — expired-quote approval.</b> A quote is expired when its validity date precedes the SERVER's
/// Europe/Istanbul business day (SAT-D10; never a client-sent date). Such a quote converts only after a manager approval was
/// recorded for its CURRENT revision under the approval's own permission (<c>sales.quotes.expired-conversion.approve</c>;
/// registered, granted to no role). SAT-D44 (2): the quote's creator or reviser may approve it — an explicit exception to P6
/// for this approval only. The command is a quote attempt (ADR-SAL-001): authority first, replay, the one save with the
/// approval row, the quote's version step, the attempt and the audit event; authority is measured again right before it.</item>
/// <item><b>SAT-D25 — loss reasons from Settings.</b> The loss (v2) names an active entry of the company's closed Settings list
/// in force on the server's business day; the entry's own flag decides whether the explanation is required. No list record,
/// a passive list or one outside its window, or an unreadable source: 503, nothing recorded (fail-closed). No reason value is
/// a code constant.</item>
/// <item><b>SAT-D39 — default validity.</b> The Settings day count in force on the quote's business date is SUGGESTED for a new
/// quote (business date + days); no value, no suggestion. The server never applies it: the validity date stays optional.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesQuoteResponse> ApproveExpiredQuoteConversionAsync(Guid quoteId, SalesQuoteExpiryApprovalRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Authority first (ADR-011 §5): the approval's own name — the quote write and the order names do not confer it.
        var permission = bindings.QuoteExpiredConversionApprovePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        QuoteStore();
        var expectedVersion = ExpectedVersion(request.ExpectedVersion);
        const string commandName = SalesQuoteCommands.ApproveExpiredConversionV1;
        var requestHash = SalesQuoteHash.Compute(quoteId, request);
        var envelope = Envelope(scope, metadata, permission!);

        // 2. Replay by key: the same key, content and actor answers the quote as it now stands (or the recorded refusal).
        var replay = await FindQuoteReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return await QuoteReplayedAsync(scope, replay, cancellationToken);
        }

        // 3. The server's business day must be resolvable before anything is written (no UTC fallback).
        _ = ServerBusinessDay();

        // 4. The one save: the domain decides on the business day of the save's own clock; authority is measured again first.
        return await UpdateQuoteAsync(scope, permission, envelope, requestHash, commandName, quoteId,
            (quote, now) => quote.ApproveExpiredConversion(expectedVersion, scope.ActorId, SalesBusinessDay.DateOf(now), request.Note, now),
            new SalesDraftStateAudit("sales.quote.expired-conversion-approved.v1", "ApproveExpiredConversion",
                "Süresi geçmiş satış teklifinin siparişe dönüştürülmesi onaylandı"), null, cancellationToken);
    }

    public async Task<SalesQuoteResponse> RecordQuoteLossV2Async(Guid quoteId, SalesQuoteLossRequestV2 request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.QuoteWritePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        QuoteStore();
        var expectedVersion = ExpectedVersion(request.ExpectedVersion);
        const string commandName = SalesQuoteCommands.LoseV2;
        var requestHash = SalesQuoteHash.Compute(quoteId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindQuoteReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return await QuoteReplayedAsync(scope, replay, cancellationToken);
        }

        // SAT-D25: the list in force on the server's business day; no selectable list → 503, nothing recorded (fail-closed).
        var businessDay = ServerBusinessDay();
        (var list, scope) = await ReadLossReasonListAsync(scope, permission, businessDay, cancellationToken);
        if (list is null)
        {
            throw new SalesDraftServiceException(503, SalesQuoteProblemCodesV1.LossReasonsUnavailable);
        }

        var entry = list.Reasons.FirstOrDefault(candidate => candidate.ReasonId == request.ReasonId && candidate.IsActive)
            ?? throw new SalesDraftServiceException(422, SalesQuoteProblemCodesV1.LossReasonUnknown,
                [new("reasonId", SalesQuoteProblemCodesV1.LossReasonUnknown)]);
        var reason = new SalesQuoteLossReason(entry.ReasonId, entry.Code, entry.Label, list.Profile.ProfileId, list.Profile.Version);
        return await UpdateQuoteAsync(scope, permission, envelope, requestHash, commandName, quoteId,
            (quote, now) => quote.RecordLoss(expectedVersion, scope.ActorId, reason, entry.RequiresExplanation, request.Explanation, now),
            new SalesDraftStateAudit("sales.quote.lost.v1", "Lose", "Satış teklifi kaybedildi"), null, cancellationToken);
    }

    public async Task<SalesQuoteLossReasonsViewV1> GetQuoteLossReasonsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.QuoteWritePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var businessDay = ServerBusinessDay();
        var (list, _) = await ReadLossReasonListAsync(scope, permission, businessDay, cancellationToken);
        return list is null
            ? new SalesQuoteLossReasonsViewV1(businessDay, false, null, null, [])
            : new SalesQuoteLossReasonsViewV1(businessDay, true, list.Profile.ProfileId,
                list.Profile.Version.ToString(CultureInfo.InvariantCulture),
                [.. list.Reasons.Where(reason => reason.IsActive)
                    .Select(reason => new SalesQuoteLossReasonOptionV1(reason.ReasonId, reason.Code, reason.Label, reason.RequiresExplanation))]);
    }

    public async Task<SalesQuoteValiditySuggestionV1> GetQuoteValiditySuggestionAsync(DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.QuoteWritePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var none = new SalesQuoteValiditySuggestionV1(businessDate, null, null, null, null);
        var policies = quotePolicies ?? throw new SalesDraftServiceException(503, "settings_provider_unavailable");
        SalesQuoteDefaultValidityPolicyV1 policy;
        try
        {
            (policy, scope) = await GuardedWithScopeAsync(scope, permission,
                () => policies.GetSalesQuoteDefaultValidityAsync(businessDate, cancellationToken), cancellationToken);
        }
        catch (SettingsPrerequisiteException exception) when (exception.Code == SettingsPrerequisiteCodesV1.SalesPolicyNotFound)
        {
            // SAT-D39: "Ayarlar'da değer yoksa varsayılan önerilmez".
            return none;
        }
        catch (SettingsPrerequisiteException exception)
        {
            throw exception.Code == "settings_access_denied"
                ? new SalesDraftServiceException(403, "settings_access_denied")
                : new SalesDraftServiceException(503, "settings_provider_unavailable");
        }

        if (policy is null || policy.CompanyId != scope.CompanyId || policy.Profile is null || policy.Profile.ProfileId == Guid.Empty
            || policy.Profile.Version < 1 || policy.ValidityDays < 1)
        {
            throw new SalesDraftServiceException(503, "settings_provider_unavailable");
        }

        if (!policy.IsActive || policy.EffectiveFrom > businessDate || (policy.EffectiveThrough is { } through && through < businessDate))
        {
            // Not in force on the quote's business date: no value, no suggestion.
            return none;
        }

        DateOnly suggested;
        try
        {
            suggested = businessDate.AddDays(policy.ValidityDays);
        }
        catch (ArgumentOutOfRangeException)
        {
            return none;
        }

        return new SalesQuoteValiditySuggestionV1(businessDate, policy.ValidityDays, suggested, policy.Profile.ProfileId,
            policy.Profile.Version.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// SAT-D25: the company's loss reason list in force on <paramref name="businessDay"/>, released under
    /// <paramref name="permission"/>. Null when Settings holds no list, or the list is passive or outside its window (a loss is
    /// then refused). Access denied is 403; an absent, unserved or malformed source — or a malformed answer — is 503.
    /// </summary>
    private async Task<(SalesQuoteLossReasonListV1? List, Scope Scope)> ReadLossReasonListAsync(Scope scope, string? permission,
        DateOnly businessDay, CancellationToken cancellationToken)
    {
        var policies = quotePolicies ?? throw new SalesDraftServiceException(503, SalesQuoteProblemCodesV1.LossReasonsUnavailable);
        SalesQuoteLossReasonListV1 list;
        try
        {
            (list, scope) = await GuardedWithScopeAsync(scope, permission,
                () => policies.GetSalesQuoteLossReasonsAsync(businessDay, cancellationToken), cancellationToken);
        }
        catch (SettingsPrerequisiteException exception) when (exception.Code == SettingsPrerequisiteCodesV1.SalesPolicyNotFound)
        {
            return (null, scope);
        }
        catch (SettingsPrerequisiteException exception)
        {
            throw exception.Code == "settings_access_denied"
                ? new SalesDraftServiceException(403, "settings_access_denied")
                : new SalesDraftServiceException(503, SalesQuoteProblemCodesV1.LossReasonsUnavailable);
        }

        if (list is null || list.CompanyId != scope.CompanyId || list.Profile is null || list.Profile.ProfileId == Guid.Empty
            || list.Profile.Version < 1 || list.Reasons is null || list.Reasons.Count == 0
            || list.Reasons.Any(reason => reason is null || reason.ReasonId == Guid.Empty
                || string.IsNullOrWhiteSpace(reason.Code) || reason.Code.Length > SalesQuote.MaximumLossReasonCodeLength
                || string.IsNullOrWhiteSpace(reason.Label) || reason.Label.Length > SalesQuote.MaximumLossReasonLabelLength)
            || list.Reasons.Select(reason => reason.ReasonId).Distinct().Count() != list.Reasons.Count)
        {
            throw new SalesDraftServiceException(503, SalesQuoteProblemCodesV1.LossReasonsUnavailable);
        }

        return !list.IsActive || list.EffectiveFrom > businessDay || (list.EffectiveThrough is { } through && through < businessDay)
            ? (null, scope)
            : (list, scope);
    }

    /// <summary>The server's Europe/Istanbul business day now (SAT-D10); an unresolvable zone is 503, never UTC.</summary>
    private DateOnly ServerBusinessDay()
    {
        try
        {
            return SalesBusinessDay.DateOf(timeProvider.GetUtcNow());
        }
        catch (SalesBusinessDayUnavailableException)
        {
            throw new SalesDraftServiceException(503, SalesQuoteProblemCodesV1.BusinessDayUnavailable);
        }
    }
}
