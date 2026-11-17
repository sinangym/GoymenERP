using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>Explicit attribution to approved exception capacity; never a plan or stock override.</summary>
public sealed record LogisticsPlanExcessUseV1(Guid ExceptionId, string ExpectedVersion, string Quantity);
public sealed record ProposeLogisticsPlanExcessV1(Guid ExceptionId, string ExpectedInstructionVersion, string Quantity, string Reason);
public sealed record ApproveLogisticsPlanExcessV1(string ExpectedVersion, string ExpectedInstructionVersion);
public sealed record ReserveLogisticsPlanExcessV1(string ExpectedInstructionVersion, string ExpectedExceptionVersion,
    Guid AllocationId, string ExpectedLabelVersion, string QuantityKg, string Reason);
public sealed record LogisticsPlanExcessReservationV1(Guid CompanyId, Guid ExceptionId, Guid InstructionId, Guid LineId,
    Guid AllocationId, Guid LabelId, string QuantityKg, string OriginalAllocationVersion, string OriginalLabelVersion);
public sealed record LogisticsPlanExcessAdditionalAllocationV1(Guid AllocationId, Guid ExceptionId, string Version, string RemainingKg, string AllocatedKg);
public sealed record LogisticsPlanExcessViewV1(Guid CompanyId, Guid ExceptionId, Guid InstructionId, Guid LineId,
    Guid SalesOrderId, Guid SalesOrderLineId, Guid UnitId, string Version, string InstructionVersion,
    string Status, string Instructed, string Quantity, string LoadedUsed, string ShippedUsed,
    string Reason, string ProposedBy, DateTimeOffset ProposedAtUtc, string? ApprovedBy, DateTimeOffset? ApprovedAtUtc,
    string AvailableToLoad, string AvailableToShip);
public sealed record LogisticsPlanExcessPageV1(IReadOnlyList<LogisticsPlanExcessViewV1> Items, string? NextCursor);
public sealed record LogisticsPlanExcessResultV1(LogisticsPlanExcessViewV1 Exception, bool IsReplay, string OriginalCorrelationId);
public static class LogisticsPlanExcessV1
{
    public const string ApprovePermission = "logistics.shipments.plan-excess.approve";
    public const string ProposeCommand = "logistics.plan-excess.propose.v1";
    public const string ApproveCommand = "logistics.plan-excess.approve.v1";
    public const string ReserveCommand = "logistics.plan-excess.reserve.v1";
    public const string ApprovalRequired = "logistics_plan_excess_approval_required";
    public const string Conflict = "logistics_plan_excess_conflict";
    public const string Unavailable = "logistics_plan_excess_unavailable";
}
public interface ILogisticsPlanExcessServiceV1
{
    Task<LogisticsPlanExcessPageV1> ListAsync(string purpose, Guid? instructionId, string? cursor, CancellationToken token = default);
    Task<LogisticsPlanExcessResultV1> ProposeAsync(Guid instructionId, Guid lineId, ProposeLogisticsPlanExcessV1 request,
        LogisticsCommandMetadata metadata, CancellationToken token = default);
    Task<LogisticsPlanExcessResultV1> ApproveAsync(Guid exceptionId, ApproveLogisticsPlanExcessV1 request,
        LogisticsCommandMetadata metadata, CancellationToken token = default);
}
public interface ILogisticsPlanExcessStoreV1
{
    Task<LogisticsPlanExcessPageV1> ListPlanExcessAsync(Guid? instructionId, string? cursor, CancellationToken token);
    Task<LogisticsPlanExcessResultV1?> FindPlanExcessReplayAsync(LogisticsWriteEnvelope envelope, string hash, string command, CancellationToken token);
    Task<LogisticsPlanExcessResultV1> ProposePlanExcessAsync(LogisticsWriteEnvelope envelope, string hash, Guid instructionId,
        Guid lineId, ProposeLogisticsPlanExcessV1 request, CancellationToken token);
    Task<LogisticsPlanExcessResultV1> ApprovePlanExcessAsync(LogisticsWriteEnvelope envelope, string hash, Guid exceptionId,
        ApproveLogisticsPlanExcessV1 request, CancellationToken token);
}
public interface ILogisticsPlanExcessMutationStoreV1
{
    Task<LogisticsStoreResult?> MutateWithPlanExcessAsync(LogisticsWriteEnvelope envelope, string hash, string command,
        Guid instructionId, Action<GoymenERP.Modules.Logistics.Domain.Loading.LoadingInstruction, DateTimeOffset> mutate,
        LogisticsMutationAudit audit, LogisticsOrderLineAggregateLoadLimit? loadLimit, LogisticsShipmentRiskGateFactsV1? risk,
        IReadOnlyDictionary<Guid, IReadOnlyList<LogisticsPlanExcessUseV1>?> selections, CancellationToken token);
}
