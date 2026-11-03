using System.Globalization;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Application;

// Üretim U2-a (URT-D1, URT-D2, API-10, API-11; plan v1 §5.2): the plan's rejection and cancellation, and the approval's
// read of each parent roll's current Inventory label state for the slitting gate. Every command runs the ADR-011 frame of
// the other plan commands: the fresh company-wide authority check, the key's stored answer, the domain's decision on the
// stored plan, and one save of the decision, its audit event and the command record.

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// <c>POST /plans/{planId}/rejection</c> (URT-D2, API-10) under <c>manufacturing.plans.approve</c>: instead of approving
    /// a Draft, its approver rejects it with a reason. The plan stays a Draft (no "Kontrolde" step), one version on, with
    /// the rejection recorded. ADR-011 as the approval: a success, a not-found and a stale version are the key's answer
    /// from then on; a rule refusal (not a draft, cancelled) is evaluated again on retry.
    /// </summary>
    public Task<ManufacturingApiResult<PlanViewV1>> RejectPlanAsync(Guid planId, RejectPlanRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken)
    {
        if (planId == Guid.Empty || request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<PlanViewV1>("expectedVersion"));
        }

        if (DecisionReason(request.Reason) is not { } reason)
        {
            return Task.FromResult(Invalid<PlanViewV1>("reason"));
        }

        // The Draft's answer (returned AND recorded for replay) uses the profile in force today, read once before the save.
        PlanParameterSnapshotV1? live = null;
        return RunAsync<PlanViewV1>(permission, idempotencyKey, ManufacturingCommandNames.RejectPlan,
            new { planId, expectedVersion = request.ExpectedVersion, reason }, ManufacturingApiStatus.Ok,
            async (metadata, actor, companyId) =>
            {
                if (planLifecycle is null)
                {
                    return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
                }

                if (await plans.FindPlanAsync(planId, cancellationToken) is { } current)
                {
                    live = await ReadLiveParameterProfileAsync(current, cancellationToken);
                    if (await FenceCapturedAsync<PlanViewV1>(permission, companyId, actor, cancellationToken) is { } fenced)
                    {
                        return fenced;
                    }
                }

                var rejected = await planLifecycle.RejectPlanAsync(planId, request.ExpectedVersion, actor,
                    timeProvider.GetUtcNow(), reason, metadata, cancellationToken);
                return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Ok, PlanView(rejected, live), false,
                    null);
            }, stored => PlanView((ProductionPlan)stored, live), cancellationToken);
    }

    /// <summary>
    /// <c>POST /plans/{planId}/cancellation</c> (API-11) under <c>manufacturing.plans.cancel</c> (registered centrally,
    /// granted to no role — a human gate): a Draft, or an approved plan that opened no order, is cancelled with a reason
    /// and kept (never deleted). A plan with an order is refused (<c>manufacturing_plan_has_order</c>); a cancelled plan
    /// again is <c>manufacturing_plan_cancelled</c>. Stock allocation does not exist yet (U3), so nothing is released.
    /// </summary>
    public Task<ManufacturingApiResult<PlanViewV1>> CancelPlanAsync(Guid planId, CancelPlanRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken)
    {
        if (planId == Guid.Empty || request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<PlanViewV1>("expectedVersion"));
        }

        if (DecisionReason(request.Reason) is not { } reason)
        {
            return Task.FromResult(Invalid<PlanViewV1>("reason"));
        }

        // A cancelled plan that was never approved is shown with the profile in force today, like the plan read.
        PlanParameterSnapshotV1? live = null;
        return RunAsync<PlanViewV1>(permission, idempotencyKey, ManufacturingCommandNames.CancelPlan,
            new { planId, expectedVersion = request.ExpectedVersion, reason }, ManufacturingApiStatus.Ok,
            async (metadata, actor, companyId) =>
            {
                if (planLifecycle is null)
                {
                    return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
                }

                if (await plans.FindPlanAsync(planId, cancellationToken) is { ApprovedAtUtc: null } current)
                {
                    live = await ReadLiveParameterProfileAsync(current, cancellationToken);
                    if (await FenceCapturedAsync<PlanViewV1>(permission, companyId, actor, cancellationToken) is { } fenced)
                    {
                        return fenced;
                    }
                }

                var cancelled = await planLifecycle.CancelPlanAsync(planId, request.ExpectedVersion, actor,
                    timeProvider.GetUtcNow(), reason, metadata, cancellationToken);
                return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Ok, PlanView(cancelled, live), false,
                    null);
            }, stored => PlanView((ProductionPlan)stored, live), cancellationToken);
    }

    /// <summary>The trimmed reason of a rejection or cancellation, or null when it is empty, too long or has control characters.</summary>
    private static string? DecisionReason(string? reason)
    {
        var text = reason?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > ProductionPlan.DecisionReasonMaximumLength
               || text.Any(char.IsControl)
            ? null
            : text;
    }

    /// <summary>
    /// Üretim U2-a (the slitting gate's <c>labelState</c>, #905 open point b): each planned parent roll's CURRENT state, read
    /// through the existing Inventory parent-roll ports at the label version the plan accepted (the contextual port for a
    /// customer-owned input). Inventory is not changed and nothing is reserved. Per roll:
    /// <list type="bullet">
    /// <item>usable (true) when Inventory still offers the exact label for production use and its unblocked physical kg
    /// covers the roll's planned kg;</item>
    /// <item>not usable (false) when Inventory no longer offers it (not selectable, a refusal code, no physical position,
    /// a context it requires, an unknown label) or its unblocked kg is below the planned kg (blocked stock);</item>
    /// <item>unknown (no entry) for a legacy input without an accepted label version: the gate names <c>labelState</c>.</item>
    /// </list>
    /// A label that moved since the plan was saved is Inventory's version conflict (409, terminal for the key: the plan must
    /// be re-planned); a read conflict is a retryable 409; a denial 403; an unavailable owner 503. The fence runs after
    /// every owner await.
    /// </summary>
    private async Task<(IReadOnlyDictionary<Guid, bool>? Usable, ManufacturingApiResult<object>? Refusal)>
        ReadLabelUsabilityAsync(ProductionPlan plan, string permission, Guid companyId, Guid actorId,
            CancellationToken cancellationToken)
    {
        var usable = new Dictionary<Guid, bool>();
        foreach (var input in plan.Inputs)
        {
            if (input.ExpectedLabelVersion is not { } version)
            {
                continue;
            }

            var expected = version.ToString(CultureInfo.InvariantCulture);
            var commercial = input.CommercialContext;
            if ((commercial is null && parentRolls is null) || (commercial is not null && contextualParentRolls is null))
            {
                return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                    InventoryParentRollCodesV1.Unavailable));
            }

            InventoryParentRollReferenceV1 reference;
            try
            {
                reference = commercial is null
                    ? await parentRolls!.ValidateAsync(new InventoryParentRollSelectionV1(input.ParentRollId, expected),
                        cancellationToken)
                    : await contextualParentRolls!.ValidateAsync(new InventoryContextualParentRollSelectionV1(
                        input.ParentRollId, expected, new InventoryParentRollPositionContextV1(StockPartyKind.Counterparty,
                            commercial.PartyId, StockPartyKind.Company, companyId)), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InventoryParentRollException exception)
            {
                if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced)
                {
                    return (null, fenced);
                }

                switch (exception)
                {
                    case { StatusCode: 401 or 403 }:
                        return (null, Denied<object>());
                    case { StatusCode: 404 }:
                    case { StatusCode: 422, Code: InventoryParentRollCodesV1.ContextRequired
                        or InventoryParentRollCodesV1.NoPhysicalPosition }:
                        usable[input.ParentRollId] = false;
                        continue;
                    case { StatusCode: 409, Code: InventoryParentRollCodesV1.VersionConflict
                        or InventoryParentRollCodesV1.ReadConflict }:
                        return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Conflict, null, false,
                            exception.Code));
                    default:
                        return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                            InventoryParentRollCodesV1.Unavailable));
                }
            }
            catch (Exception)
            {
                if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced)
                {
                    return (null, fenced);
                }

                return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                    InventoryParentRollCodesV1.Unavailable));
            }

            if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } afterRead)
            {
                return (null, afterRead);
            }

            // The answer must be this company's exact label at the accepted version; anything else fails closed.
            if (reference.CompanyId != companyId || reference.LabelId != input.ParentRollId
                || !string.Equals(reference.LabelVersion, expected, StringComparison.Ordinal)
                || !decimal.TryParse(reference.UnblockedPhysicalKg, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var unblockedKg))
            {
                return (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                    InventoryParentRollCodesV1.Unavailable));
            }

            usable[input.ParentRollId] = reference.DraftSelectable && reference.DraftRefusalCode is null
                && unblockedKg >= input.PlannedMass.Value;
        }

        return (usable, null);
    }
}
