using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-D2 / API-12 (<c>docs/decisions/SINAN_KARARLARI_2026-10-25_SATIS.md</c>): "Rol bazlı yüzde limiti, Ayarlar'dan; limiti
/// aşan iskonto Müdür onayına gider. Limiti tanımlı olmayan rol için her iskonto onaya düşer (fail-closed)."
/// <list type="bullet">
/// <item><b>The limit</b> of the actor who sets a positive discount (create, commercial revision) is read only from the Settings
/// owner (<see cref="ISettingsSalesPolicyProviderV1"/>) for each of the actor's limit groups
/// (<see cref="ISalesDiscountLimitGroupSourceV1"/>, the Identity owner's mapping) on the draft's business date. Several groups:
/// the STRICTEST (lowest) limit applies, and a group without an approved limit makes the actor's limit undefined (the
/// fail-closed reading; NEEDS-SINAN on which limit of a multi-role user applies). No group, no group source, "not found", an
/// inactive policy or one outside its window: no limit defined — every positive discount goes to approval. A Settings source
/// that cannot answer (not composed, not served, malformed) refuses the command (503) — never a guessed limit.</item>
/// <item><b>The approval</b> is the price-floor exception's shape: its own permission, every current over-limit line once
/// with a reason, never the actor who set the discount, one keyed save with the attempt, the audit event and both price
/// visibility variants of the first answer.</item>
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
    /// Whether a discount text may be above zero: a readable positive value, or a text the calculator cannot read (the domain
    /// refuses that line; reading the limit for it costs nothing and decides nothing).
    /// </summary>
    private static bool MayBePositiveDiscount(string? text) =>
        text is not null && (!SalesLinePriceCalculator.TryParseExact(text, out var value) || value > 0m);

    /// <summary>
    /// SAT-D2: the limit that applies to the fenced actor on <paramref name="effectiveOn"/>, read under
    /// <paramref name="permission"/> (the fence again after every awaited read). See the class summary for the rules.
    /// </summary>
    private async Task<(SalesDiscountLimit Limit, Scope Scope)> ResolveDiscountLimitAsync(Scope scope, string? permission,
        DateOnly effectiveOn, CancellationToken cancellationToken)
    {
        if (discountLimitGroups is null)
        {
            // No owner maps roles to limit groups yet: no limit is defined for anyone (SAT-D2, fail-closed).
            return (SalesDiscountLimit.Undefined, scope);
        }

        IReadOnlyList<Guid>? groups;
        (groups, scope) = await GuardedWithScopeAsync(scope, permission,
            () => discountLimitGroups.GetCurrentActorLimitGroupsAsync(cancellationToken), cancellationToken);
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
        SalesDiscountLimit? strictest = null;
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
                // "Limiti tanımlı olmayan rol için her iskonto onaya düşer": one undefined group makes the actor's limit undefined.
                return (SalesDiscountLimit.Undefined, scope);
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
                // Not in force on the date: no approved limit defined for this group.
                return (SalesDiscountLimit.Undefined, scope);
            }

            if (strictest?.MaximumPercent is not { } current || policy.MaximumDiscountPercent < current)
            {
                strictest = SalesDiscountLimit.Defined(policy.MaximumDiscountPercent, policy.Profile.ProfileId, policy.Profile.Version);
            }
        }

        return (strictest ?? SalesDiscountLimit.Undefined, scope);
    }
}
