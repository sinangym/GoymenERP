using System.Data;
using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>
/// Üretim U3-a (URT-D21, URT-D3, URT-D22; API-20): owns the one SQL transaction in which a Manufacturing plan decision
/// and Inventory's plan-input allocation (Kalan Modüller B-01) commit together. The approval allocates every input of
/// the approved plan; a cancellation of an approved plan and an order's closure release what remains. A refusal from
/// either module rolls both back. The host decides no policy: the Manufacturing write is the caller's, the allocation
/// rules are Inventory's.
/// </summary>
internal sealed class ManufacturingPlanAllocationHost(
    ManufacturingDbContext manufacturing,
    ManufacturingStore plans,
    ICompanyContext companyContext,
    ISettingsPrerequisiteProvider settings,
    TimeProvider timeProvider)
    : IManufacturingPlanAllocationPortV1
{
    public const string AllocationReason = "manufacturing.plan-approved";

    public Task<T> ApproveAndAllocateAsync<T>(Guid planId, IReadOnlyDictionary<Guid, long> labelVersions,
        Func<CancellationToken, Task<T>> approve, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(labelVersions);
        return RunAsync(planId, approve, metadata, async (companyId, plan, inventory, inventoryMetadata, token) =>
        {
            if (plan.Status != Modules.Manufacturing.Domain.Plans.PlanStatus.Approved || plan.Inputs.Count == 0)
            {
                throw new ManufacturingPlanAllocationHostException(ManufacturingPlanAllocationCodesV1.Incomplete);
            }

            var lines = plan.Inputs.Select(input =>
            {
                long version = labelVersions.TryGetValue(input.ParentRollId, out var current)
                    ? current
                    : input.ExpectedLabelVersion
                      ?? throw new ManufacturingPlanAllocationHostException(
                          ManufacturingPlanAllocationCodesV1.MissingLabelVersion);
                var commercial = input.CommercialContext;
                return new InventoryManufacturingPlanInputLineV1(
                    input.ParentRollId,
                    version.ToString(CultureInfo.InvariantCulture),
                    input.PlannedMass.Value.ToString("0.###", CultureInfo.InvariantCulture),
                    commercial is null
                        ? InventoryManufacturingPlanInputOwnershipV1.Company
                        : InventoryManufacturingPlanInputOwnershipV1.CustomerOwned,
                    commercial?.PartyId);
            }).ToArray();
            var request = new InventoryManufacturingPlanAllocationRequestV1(companyId, plan.Id,
                plan.Version.ToString(CultureInfo.InvariantCulture), lines, AllocationReason,
                timeProvider.GetUtcNow().ToUniversalTime());
            var result = await inventory.StagePlanInputAllocationAsync(request, inventoryMetadata, token);
            if (result.IsReplay || result.CompanyId != companyId || result.PlanId != plan.Id
                || result.Allocations.Count != lines.Length)
            {
                // Both effects are born in this transaction; a replayed Inventory command here is an orphan.
                throw new ManufacturingPlanAllocationHostException(ManufacturingPlanAllocationCodesV1.Incomplete);
            }

            var recorded = new List<ManufacturingPlanInputAllocationV1>(result.Allocations.Count);
            foreach (var input in plan.Inputs)
            {
                var allocation = result.Allocations.SingleOrDefault(candidate => candidate.LabelId == input.ParentRollId);
                if (allocation is null || allocation.AllocationId == Guid.Empty
                    || !long.TryParse(allocation.LabelVersion, NumberStyles.None, CultureInfo.InvariantCulture,
                        out var labelVersionAfter)
                    || labelVersionAfter < 1
                    || !decimal.TryParse(allocation.QuantityKg, NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var allocatedKg)
                    || allocatedKg != input.PlannedMass.Value)
                {
                    throw new ManufacturingPlanAllocationHostException(ManufacturingPlanAllocationCodesV1.Incomplete);
                }

                recorded.Add(new ManufacturingPlanInputAllocationV1(input.ParentRollId, allocation.AllocationId,
                    labelVersionAfter, input.PlannedMass));
            }

            await plans.RecordPlanInputAllocationsAsync(plan.Id, plan.Version, recorded, timeProvider.GetUtcNow(), token);
        }, cancellationToken);
    }

    public Task<T> WriteAndReleaseAsync<T>(Guid planId, ManufacturingPlanAllocationReleaseReasonV1 reason,
        Func<CancellationToken, Task<T>> write, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Tanımsız serbest bırakma sebebi.");
        }

        return RunAsync(planId, write, metadata, async (companyId, plan, inventory, inventoryMetadata, token) =>
        {
            var result = await inventory.StagePlanRemainderReleaseAsync(
                new InventoryManufacturingPlanReleaseRequestV1(companyId, plan.Id,
                    plan.Version.ToString(CultureInfo.InvariantCulture),
                    reason == ManufacturingPlanAllocationReleaseReasonV1.PlanCancelled
                        ? InventoryManufacturingPlanReleaseReasonV1.PlanCancelled
                        : InventoryManufacturingPlanReleaseReasonV1.OrderClosed,
                    timeProvider.GetUtcNow().ToUniversalTime()),
                inventoryMetadata with
                {
                    IdempotencyKey = $"manufacturing-plan-release:{companyId:N}:{plan.Id:N}:{(int)reason}",
                },
                token);
            if (result.IsReplay || result.CompanyId != companyId || result.PlanId != plan.Id)
            {
                throw new ManufacturingPlanAllocationHostException(ManufacturingPlanAllocationCodesV1.Incomplete);
            }
        }, cancellationToken);
    }

    private async Task<T> RunAsync<T>(Guid planId, Func<CancellationToken, Task<T>> write,
        ManufacturingCommandMetadata metadata,
        Func<Guid, Modules.Manufacturing.Domain.Plans.ProductionPlan, IInventoryManufacturingPlanAllocationParticipantV1,
            InventoryCommandMetadata, CancellationToken, Task> stage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(metadata);
        if (planId == Guid.Empty)
        {
            throw new ArgumentException("Plan kimliği boş olamaz.", nameof(planId));
        }

        var companyId = companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
            ? companyContext.CompanyId
            : throw new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Denied,
                ManufacturingApiCodes.AuthorityDenied);

        await using var transaction = await manufacturing.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var committed = false;
        try
        {
            var answer = await write(cancellationToken);
            var plan = await plans.FindPlanAsync(planId, cancellationToken)
                ?? throw new ManufacturingPlanAllocationHostException(ManufacturingPlanAllocationCodesV1.Incomplete);
            var inventoryMetadata = new InventoryCommandMetadata(
                metadata.ActorId,
                metadata.PermissionCode,
                metadata.RoleCodes,
                metadata.CorrelationId,
                metadata.CausationId,
                // The Inventory command follows the plan's version, not one HTTP attempt.
                $"manufacturing-plan-allocation:{companyId:N}:{planId:N}:{plan.Version.ToString(CultureInfo.InvariantCulture)}",
                metadata.SourceChannel,
                metadata.ClientIpAddress,
                metadata.UserAgent);
            try
            {
                await using var inventory = await InventoryManufacturingRealizationParticipant.EnlistAsync(
                    manufacturing.Database.GetDbConnection(), transaction.GetDbTransaction(), companyId,
                    settings, cancellationToken);
                await stage(companyId, plan, inventory, inventoryMetadata, cancellationToken);
            }
            catch (ArgumentException)
            {
                // TAKIP-U3a (#1004): an argument fault inside the Inventory stage is this host's contract fault (503),
                // never the caller's 400; the caller's own write above keeps its ArgumentException (400).
                throw new ManufacturingPlanAllocationHostException(ManufacturingPlanAllocationCodesV1.Incomplete);
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return answer;
        }
        catch (InventoryManufacturingRealizationException exception)
        {
            await RollbackAsync(transaction, committed, exception);
            throw new ManufacturingInventoryRealizationPortException(exception.Status switch
            {
                400 => ManufacturingApiStatus.Invalid,
                404 => ManufacturingApiStatus.Conflict,
                409 => ManufacturingApiStatus.Conflict,
                _ => ManufacturingApiStatus.Unavailable,
            }, exception.Status == 404
                ? ManufacturingPlanAllocationCodesV1.LabelNotFound
                : exception.Code);
        }
        catch (ManufacturingPlanAllocationHostException exception)
        {
            await RollbackAsync(transaction, committed, exception);
            throw new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Unavailable,
                exception.Code);
        }
        catch (Exception exception)
        {
            await RollbackAsync(transaction, committed, exception);
            throw;
        }
        finally
        {
            manufacturing.ChangeTracker.Clear();
        }
    }

    private async Task RollbackAsync(IDbContextTransaction transaction, bool committed, Exception attempt)
    {
        if (committed)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollback) when (rollback is InvalidOperationException or DbException)
        {
            manufacturing.ChangeTracker.Clear();
            throw new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Unavailable,
                InventoryManufacturingRealizationCodesV1.Unavailable, field: null, expectedVersion: null,
                currentVersion: null);
        }
    }
}

internal sealed class ManufacturingPlanAllocationHostException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
