using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-D2 / API-12 (<c>docs/decisions/SINAN_KARARLARI_2026-10-25_SATIS.md</c>): "Rol bazlı yüzde limiti, Ayarlar'dan; limiti
/// aşan iskonto Müdür onayına gider. Limiti tanımlı olmayan rol için her iskonto onaya düşer (fail-closed)", as applied by
/// SAT-D34…D37 (<c>SINAN_KARARLARI_2026-10-27_SATIS_EK4.md</c>).
/// <list type="bullet">
/// <item><b>The limit</b> of the actor who discounts a line against its list price is read only from the Settings owner
/// (<see cref="ISettingsSalesPolicyProviderV1"/>) for each of the actor's limit groups
/// (<see cref="ISalesDiscountLimitGroupSourceV1"/>: the person → group bindings, SAT-D63/D66, the Settings owner's record)
/// on the day the check runs: SAT-D81 (<c>SINAN_KARARLARI_2026-10-28_SATIS_EK12.md</c>) — the limit of the day the order is
/// entered or commercially revised (the server's Europe/Istanbul business day, SAT-D10), never of the order's business
/// date, so a limit scheduled for a later day (SAT-D78) does not apply to an order entered today. SAT-D34: the
/// HIGHEST limit among the groups that have one applies; a group without an approved limit ("not found"), an inactive policy
/// or one outside its window contributes nothing; no group with a limit (or no group, or no group source) means no limit
/// defined — every discount goes to approval (SAT-D36). A Settings source that cannot answer (not composed, not served,
/// malformed) refuses the command (503) — never a guessed limit.</item>
/// <item><b>The approval</b> is the price-floor exception's shape: its own permission (Müdür's, SAT-D37 — a grant, not a role
/// created here), every current over-limit line once with a reason, never the actor who set the discount, one keyed save with
/// the attempt, the audit event and both price visibility variants of the first answer.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    /// <summary>
    /// API-12: reasoned maker-checker approval of every current over-limit discount line. This command changes no price or
    /// discount; it binds the approving actor and reason to each line's current check and is invalidated by the next
    /// commercial revision. Its dedicated permission confers neither price write nor order approval.
    /// </summary>
    public async Task<SalesOrderDraftResponse> ApproveDiscountExceptionsAsync(Guid draftId,
        SalesDraftDiscountExceptionApprovalRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.DiscountExceptionApprovePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var validationErrors = new List<SalesDraftFieldError>();
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion)
            || expectedVersion < 1)
        {
            validationErrors.Add(new("expectedVersion", "validation_failed"));
        }

        if (request.Lines is null || request.Lines.Any(static line => line is null))
        {
            validationErrors.Add(new("lines", "validation_failed"));
        }

        if (validationErrors.Count > 0)
        {
            throw new SalesDraftServiceException(400, "validation_failed", validationErrors);
        }

        var lines = request.Lines!;
        const string commandName = SalesDraftCommands.ApproveDiscountExceptionsV1;
        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var inputs = lines.Select(line => new LineDiscountExceptionApprovalInput(line!.LineId, line.Reason)).ToArray();
        var audit = new SalesDraftStateAudit("sales.order-draft.discount-exceptions-approved.v1", "ApproveDiscountExceptions",
            "Satış siparişinde rol limitini aşan iskontolar farklı yetkili tarafından gerekçeli onaylandı");
        SalesDraftStateResult? result;
        try
        {
            result = await store.ReviseDraftCommercialAsync(envelope, requestHash, commandName, draftId,
                (draft, now) =>
                {
                    draft.ApproveDiscountExceptions(expectedVersion, inputs, scope.ActorId, now);
                    return null;
                }, audit, async writeCancellationToken =>
                {
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                }, cancellationToken,
                draft => new SalesDraftReplayResult(
                    Detail(draft, SalesPriceDisclosure.Visible, IsOwn(scope, draft), IsOwn(scope, draft) && scope.IsSalesSupport),
                    Detail(draft, SalesPriceDisclosure.Withheld, IsOwn(scope, draft), IsOwn(scope, draft) && scope.IsSalesSupport)));
        }
        catch (SalesVersionConflictException exception)
        {
            try
            {
                throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409,
                    SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]), cancellationToken,
                    commandName, draftId);
            }
            catch (RaceWon won)
            {
                return won.Response;
            }
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            try
            {
                throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409,
                    SalesDraftProblemCodesV1.StateConflict, [new("approvalState", exception.Code)]), cancellationToken,
                    commandName, draftId);
            }
            catch (RaceWon won)
            {
                return won.Response;
            }
        }
        catch (SalesRuleException exception)
        {
            var index = exception.LineId is { } lineId
                ? lines.ToList().FindIndex(line => line.LineId == lineId)
                : -1;
            var field = index < 0 ? "lines" : $"lines[{index}].reason";
            throw new SalesDraftServiceException(422, "validation_failed", [new(field, exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            if (won is null)
            {
                throw Store(exception);
            }

            return await ReplayedAsync(scope, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (result is null)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        return result.Outcome == SalesDraftWriteOutcome.Replayed && result.Result is { } replayed
            ? await ReplayedResultAsync(scope, replayed, result.OriginalCorrelationId, cancellationToken)
            : Response(scope, result.Draft, false, result.OriginalCorrelationId, await DiscloseAsync(scope, cancellationToken));
    }

    /// <summary>
    /// SAT-D81 / SAT-D10: the server's business day in Europe/Istanbul now — the day whose discount limit a create or a
    /// commercial revision is checked against. Unknown time zone rules refuse the check (503), never another zone's day.
    /// </summary>
    private DateOnly DiscountLimitCheckDay()
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(DiscountLimitBusinessTimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DiscountLimitUnavailable);
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
    }

    /// <summary>SAT-D10: the business time zone.</summary>
    private const string DiscountLimitBusinessTimeZoneId = "Europe/Istanbul";

    /// <summary>
    /// SAT-D2: the limit that applies to the fenced actor on <paramref name="effectiveOn"/>, read under
    /// <paramref name="permission"/> (the fence again after every awaited read). See the class summary for the rules.
    /// </summary>
    private async Task<(SalesDiscountLimit Limit, Scope Scope)> ResolveDiscountLimitAsync(Scope scope, string? permission,
        DateOnly effectiveOn, CancellationToken cancellationToken)
    {
        if (discountLimitGroups is null)
        {
            // No host composes the person → group bindings: no limit is defined for anyone (SAT-D2, fail-closed).
            return (SalesDiscountLimit.Undefined, scope);
        }

        IReadOnlyList<Guid>? groups;
        try
        {
            (groups, scope) = await GuardedWithScopeAsync(scope, permission,
                () => discountLimitGroups.GetCurrentActorLimitGroupsAsync(cancellationToken), cancellationToken);
        }
        catch (SalesDiscountLimitGroupSourceException exception)
        {
            // The binding owner could not answer: never read as "no group" (that would only look fail-closed by accident).
            throw exception.IsDenied
                ? new SalesDraftServiceException(403, "settings_access_denied")
                : new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DiscountLimitUnavailable);
        }

        if (groups is null || groups.Count == 0)
        {
            return (SalesDiscountLimit.Undefined, scope);
        }

        if (groups.Any(group => group == Guid.Empty))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DiscountLimitUnavailable);
        }

        var policies = salesPolicies
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DiscountLimitUnavailable);
        SalesDiscountLimit? highest = null;
        foreach (var group in groups.Distinct())
        {
            SalesDiscountLimitPolicyV1 policy;
            try
            {
                (policy, scope) = await GuardedWithScopeAsync(scope, permission,
                    () => policies.GetSalesDiscountLimitAsync(group, effectiveOn, cancellationToken), cancellationToken);
            }
            catch (SettingsPrerequisiteException exception) when (exception.Code == SettingsPrerequisiteCodesV1.SalesPolicyNotFound)
            {
                // SAT-D34: a group without a defined limit contributes nothing.
                continue;
            }
            catch (SettingsPrerequisiteException exception)
            {
                throw exception.Code == "settings_access_denied"
                    ? new SalesDraftServiceException(403, "settings_access_denied")
                    : new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DiscountLimitUnavailable);
            }

            if (policy is null || policy.CompanyId != scope.CompanyId || policy.LimitGroupId != group || policy.Profile is null
                || policy.Profile.ProfileId == Guid.Empty || policy.Profile.Version < 1
                || policy.MaximumDiscountPercent < 0m || policy.MaximumDiscountPercent > 100m)
            {
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DiscountLimitUnavailable);
            }

            if (!policy.IsActive || policy.EffectiveFrom > effectiveOn
                || (policy.EffectiveThrough is { } through && through < effectiveOn))
            {
                // Not in force on the date: no approved limit defined for this group; it contributes nothing (SAT-D34).
                continue;
            }

            // SAT-D34: the highest defined limit of the actor's groups applies (the first one read wins a tie).
            if (highest?.MaximumPercent is not { } current || policy.MaximumDiscountPercent > current)
            {
                highest = SalesDiscountLimit.Defined(policy.MaximumDiscountPercent, policy.Profile.ProfileId, policy.Profile.Version);
            }
        }

        return (highest ?? SalesDiscountLimit.Undefined, scope);
    }
}
