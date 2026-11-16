using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// URT-D34/D35: advisory quantities for a stored plan's own inputs, never a reservation or an AT-044 draft limit.
/// Physical, blocked, allocated and free quantities remain distinct; only Inventory's enlisted command decides allocation.
/// </summary>
public sealed record PlanInputAvailabilityLineV1(Guid LabelId, Guid ProductId, string LabelVersion,
    string RequestedKg, string PhysicalKg, string BlockedKg, string AllocatedKg, string FreeKg,
    string MissingKg, int FreePositionCount);

/// <summary>The cancellation generation remains visible even after another order is opened.</summary>
public sealed record PlanCancelledOrderReferenceV1(Guid OrderId, long PlanVersionAfterCancel);

public sealed record PlanInputAvailabilityViewV1(int ContractVersion, Guid CompanyId, Guid PlanId, long PlanVersion,
    IReadOnlyList<PlanInputAvailabilityLineV1> Inputs,
    IReadOnlyList<PlanCancelledOrderReferenceV1> CancelledOrders, DateTimeOffset ReadAtUtc);

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// A bounded read under PlansRead: the caller supplies only the plan id, never arbitrary Inventory label ids.
    /// Both owner answers must reconcile at the same label version. A concurrent owner/plan read is retryable 503;
    /// unreadable owners disclose no partial quantities. The authority and captured company/actor are checked around reads.
    /// </summary>
    public async Task<ManufacturingApiResult<PlanInputAvailabilityViewV1>> GetPlanInputAvailabilityAsync(Guid planId,
        CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId) return Denied<PlanInputAvailabilityViewV1>();
        var companyId = companyContext.CompanyId;
        async Task<bool> CanReadAsync()
        {
            var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.ManufacturingPlansRead,
                companyId, cancellationToken);
            return scope.IsGranted && HasCapturedContext(companyId, actorId);
        }
        if (!await CanReadAsync()) return Denied<PlanInputAvailabilityViewV1>();
        if (planId == Guid.Empty) return NotFoundResult<PlanInputAvailabilityViewV1>();
        ManufacturingApiResult<PlanInputAvailabilityViewV1> answer;
        try
        {
            var plan = await plans.FindPlanAsync(planId, cancellationToken);
            if (plan is null || plan.CompanyId != companyId) answer = NotFoundResult<PlanInputAvailabilityViewV1>();
            else if (freeQuantity is null || inputDisposition is null) answer = AvailabilityUnavailable();
            else
            {
                var planVersion = plan.Version;
                var lines = new List<PlanInputAvailabilityLineV1>(plan.Inputs.Count);
                foreach (var input in plan.Inputs)
                {
                    var free = await freeQuantity.GetAsync(companyId, input.ParentRollId, cancellationToken);
                    if (!await CanReadAsync()) return Denied<PlanInputAvailabilityViewV1>();
                    var disposition = await inputDisposition.GetAsync(companyId, input.ParentRollId, cancellationToken);
                    if (!await CanReadAsync()) return Denied<PlanInputAvailabilityViewV1>();
                    if (free.Status != InventoryLabelDispositionStatusV1.Value || free.Value is not { } available
                        || disposition.Status != InventoryLabelDispositionStatusV1.Value
                        || disposition.Disposition is not { } physical
                        || available.CompanyId != companyId || physical.CompanyId != companyId
                        || available.LabelId != input.ParentRollId || physical.LabelId != input.ParentRollId
                        || available.ProductId == Guid.Empty || available.ProductId != physical.ProductId
                        || available.LabelVersion != physical.LabelVersion
                        || !long.TryParse(available.LabelVersion, NumberStyles.None, CultureInfo.InvariantCulture,
                            out var labelVersion) || labelVersion <= 0
                        || !AvailabilityKg(available.FreeKg, out var freeKg)
                        || !AvailabilityKg(physical.PhysicalKg, out var physicalKg)
                        || !AvailabilityKg(physical.BlockedKg, out var blockedKg)
                        || !AvailabilityKg(physical.AllocatedKg, out var allocatedKg)
                        || blockedKg > physicalKg || freeKg > physicalKg - blockedKg
                        || available.FreePositionCount < 0)
                        return await CanReadAsync() ? AvailabilityUnavailable() : Denied<PlanInputAvailabilityViewV1>();
                    // This is an advisory missing quantity only; no client subtracts figures or decides an allocation.
                    var missing = Math.Max(0m, input.PlannedMass.Value - freeKg);
                    lines.Add(new(input.ParentRollId, available.ProductId, available.LabelVersion,
                        Text(input.PlannedMass.Value), Text(physicalKg), Text(blockedKg), Text(allocatedKg),
                        Text(freeKg), Text(missing), available.FreePositionCount));
                }
                var current = await plans.FindPlanAsync(planId, cancellationToken);
                answer = current is null || current.CompanyId != companyId || current.Version != planVersion
                    ? AvailabilityUnavailable()
                    : new(ManufacturingApiStatus.Ok, new(1, companyId, plan.Id, planVersion, lines,
                        [.. plan.OrderReleases.Select(release => new PlanCancelledOrderReferenceV1(release.OrderId,
                            release.PlanVersion))], timeProvider.GetUtcNow()), false, null);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { answer = AvailabilityUnavailable(); }
        return await CanReadAsync() ? answer : Denied<PlanInputAvailabilityViewV1>();
    }

    private static bool AvailabilityKg(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && value >= 0m;

    private static ManufacturingApiResult<PlanInputAvailabilityViewV1> AvailabilityUnavailable() =>
        new(ManufacturingApiStatus.Unavailable, null, false, InventoryParentRollCodesV1.Unavailable);
}
