using System.Globalization;
using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// B-01 (URT-D3, URT-D21, URT-D22; Manufacturing API-20): Inventory's part of a Manufacturing plan's input allocation.
/// Like <see cref="IInventoryManufacturingRealizationParticipantV1"/> it is enlisted in Manufacturing's already-open
/// transaction, so the plan approval and its allocations — or the plan cancel / order close and its release — commit
/// or roll back together. Inventory never owns commit, rollback or Manufacturing state, and executes one command per
/// participant.
/// </summary>
public interface IInventoryManufacturingPlanAllocationParticipantV1 : IAsyncDisposable
{
    /// <summary>
    /// Allocates every input of one approved plan, all or nothing. Each input label is allocated once per plan; the
    /// sum of active allocations of every kind on a label never exceeds its Free kg.
    /// </summary>
    Task<InventoryManufacturingPlanAllocationResultV1> StagePlanInputAllocationAsync(
        InventoryManufacturingPlanAllocationRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases every active plan-input allocation of the plan. A release is not a stock movement. A plan with no
    /// active allocation releases nothing and succeeds.
    /// </summary>
    Task<InventoryManufacturingPlanReleaseResultV1> StagePlanRemainderReleaseAsync(
        InventoryManufacturingPlanReleaseRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>Who must own the input stock (URT-D22). Custody is always the company's.</summary>
public enum InventoryManufacturingPlanInputOwnershipV1
{
    /// <summary>The company's own stock.</summary>
    Company = 1,

    /// <summary>
    /// Customer-owned stock: allowed only on a plan bound to that customer's own fason service order. Manufacturing
    /// verifies the order and its customer; Inventory verifies that the stock owner is that customer.
    /// </summary>
    CustomerOwned = 2,
}

/// <summary>
/// One plan input. The plan keys its input by the parent label, so the label is also the allocation's demand line.
/// <see cref="QuantityKg"/> is the plan's exact planned kg (at most three decimals); Inventory does not round it.
/// </summary>
public sealed record InventoryManufacturingPlanInputLineV1(
    Guid LabelId,
    string ExpectedLabelVersion,
    string QuantityKg,
    InventoryManufacturingPlanInputOwnershipV1 Ownership,
    Guid? CustomerPartyId);

public sealed record InventoryManufacturingPlanAllocationRequestV1(
    Guid CompanyId,
    Guid PlanId,
    string PlanVersionAfter,
    IReadOnlyList<InventoryManufacturingPlanInputLineV1> Lines,
    string Reason,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// One allocation written for a plan input. <see cref="LabelVersion"/> is the label's version after this command: the
/// plan must keep it, because a later input issue revalidates the label version it supplies.
/// </summary>
public sealed record InventoryManufacturingPlanAllocationV1(
    Guid AllocationId,
    Guid LabelId,
    string LabelVersion,
    string AllocationVersion,
    string QuantityKg);

public sealed record InventoryManufacturingPlanAllocationResultV1(
    Guid CompanyId,
    Guid PlanId,
    string PlanVersion,
    IReadOnlyList<InventoryManufacturingPlanAllocationV1> Allocations,
    bool IsReplay,
    string OriginalCorrelationId);

public enum InventoryManufacturingPlanReleaseReasonV1
{
    PlanCancelled = 1,
    OrderClosed = 2,
}

public sealed record InventoryManufacturingPlanReleaseRequestV1(
    Guid CompanyId,
    Guid PlanId,
    string PlanVersionAfter,
    InventoryManufacturingPlanReleaseReasonV1 Reason,
    DateTimeOffset OccurredAtUtc);

public sealed record InventoryManufacturingPlanReleasedAllocationV1(
    Guid AllocationId,
    Guid LabelId,
    string LabelVersion,
    string ReleasedKg);

public sealed record InventoryManufacturingPlanReleaseResultV1(
    Guid CompanyId,
    Guid PlanId,
    string PlanVersion,
    IReadOnlyList<InventoryManufacturingPlanReleasedAllocationV1> Released,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>Read side of a plan's allocations (B-01 item 4): every status, so a closed plan shows consumed and released kg.</summary>
public interface IInventoryManufacturingPlanAllocationReadPortV1
{
    Task<IReadOnlyList<InventoryManufacturingPlanAllocationViewV1>> GetForPlanAsync(
        Guid planId,
        CancellationToken cancellationToken = default);
}

public enum InventoryManufacturingPlanAllocationStatusV1
{
    Active = 1,
    Consumed = 2,
    Released = 3,
}

public sealed record InventoryManufacturingPlanAllocationViewV1(
    Guid AllocationId,
    Guid PlanId,
    Guid LabelId,
    string AllocatedKg,
    string RemainingKg,
    string ConsumedKg,
    InventoryManufacturingPlanAllocationStatusV1 Status,
    string AllocationVersion,
    string? ReleaseReason);

public static class InventoryManufacturingPlanAllocationContractV1
{
    public const string AllocateCommandName = "inventory.manufacturing-plan-input-allocation.stage.v1";
    public const string ReleaseCommandName = "inventory.manufacturing-plan-input-release.stage.v1";
    public const int ReasonMaximumLength = 1000;

    /// <summary>
    /// The release reasons stored on the allocation row. They are codes, not user text: the screen localizes them.
    /// </summary>
    public static string ReleaseReasonCode(InventoryManufacturingPlanReleaseReasonV1 reason) => reason switch
    {
        InventoryManufacturingPlanReleaseReasonV1.PlanCancelled => "manufacturing.plan-cancelled",
        InventoryManufacturingPlanReleaseReasonV1.OrderClosed => "manufacturing.order-closed",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>Validates the request and returns each line's exact quantity, in request order.</summary>
    public static IReadOnlyList<Kilograms> Validate(InventoryManufacturingPlanAllocationRequestV1 request,
        InventoryCommandMetadata metadata, Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHeader(request.CompanyId, request.PlanId, request.PlanVersionAfter, request.OccurredAtUtc, metadata,
            enlistedCompanyId);
        if (request.Lines is null || request.Lines.Count == 0
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > ReasonMaximumLength
            || request.Lines.Select(line => line?.LabelId).Distinct().Count() != request.Lines.Count)
        {
            throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidRequest);
        }

        var quantities = new List<Kilograms>(request.Lines.Count);
        foreach (var line in request.Lines)
        {
            if (line is null || line.LabelId == Guid.Empty || !Enum.IsDefined(line.Ownership)
                || (line.Ownership == InventoryManufacturingPlanInputOwnershipV1.Company) != (line.CustomerPartyId is null)
                || line.CustomerPartyId == Guid.Empty)
            {
                throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidRequest);
            }

            InventoryManufacturingRealizationContractV1.RequireVersion(line.ExpectedLabelVersion);
            quantities.Add(InventoryManufacturingRealizationContractV1.Quantity(line.QuantityKg));
        }

        return quantities;
    }

    public static void Validate(InventoryManufacturingPlanReleaseRequestV1 request,
        InventoryCommandMetadata metadata, Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHeader(request.CompanyId, request.PlanId, request.PlanVersionAfter, request.OccurredAtUtc, metadata,
            enlistedCompanyId);
        if (!Enum.IsDefined(request.Reason))
        {
            throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidRequest);
        }
    }

    public static string Format(decimal kilograms) => kilograms.ToString("0.###", CultureInfo.InvariantCulture);

    private static void ValidateHeader(Guid companyId, Guid planId, string planVersionAfter,
        DateTimeOffset occurredAtUtc, InventoryCommandMetadata metadata, Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (enlistedCompanyId == Guid.Empty || companyId == Guid.Empty || planId == Guid.Empty
            || occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidRequest);
        }

        if (companyId != enlistedCompanyId)
        {
            throw Refused(404, InventoryManufacturingRealizationCodesV1.InvalidReference);
        }

        InventoryManufacturingRealizationContractV1.RequireVersion(planVersionAfter);
        InventoryManufacturingRealizationContractV1.RequireMetadata(metadata);
    }

    private static InventoryManufacturingRealizationException Refused(int status, string code) =>
        InventoryManufacturingRealizationException.Refused(status, code);
}
