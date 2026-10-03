using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Domain.Returns;

namespace GoymenERP.Modules.Logistics.Application.Contracts;

public static class LogisticsCustomerReturnCommandsV1
{
    public const string Request = "logistics.customer-return.request.v1";
    public const string Approve = "logistics.customer-return.approve.v1";
    public const string Reject = "logistics.customer-return.reject.v1";
}

public static class LogisticsCustomerReturnCodesV1
{
    public const string SourceInvalid = "logistics_customer_return_source_invalid";
    public const string QuantityExceeded = "logistics_customer_return_quantity_exceeded";
    public const string InventoryUnavailable = "logistics_customer_return_inventory_unavailable";
    public const string InventoryInvalid = "logistics_customer_return_inventory_invalid";
    public const string VersionConflict = "logistics_customer_return_version_conflict";
}

public sealed record LogisticsCustomerReturnBindingsV1(
    string RequestPermission,
    string DecisionPermission,
    string ReadPermission);

public sealed record RequestCustomerReturnV1(
    Guid CustomerReturnId,
    Guid SourceDeliveryId,
    string ExpectedInstructionVersion,
    string Quantity,
    string Reason,
    DateTimeOffset OccurredAtUtc);

public sealed record ApproveCustomerReturnV1(string ExpectedVersion, Guid InventoryMovementId, string? Note = null);

public sealed record RejectCustomerReturnV1(string ExpectedVersion, string? Note = null);

public sealed record CustomerReturnViewV1(
    Guid CustomerReturnId,
    Guid CompanyId,
    Guid LoadingInstructionId,
    Guid LoadingInstructionLineId,
    Guid SourceDeliveryId,
    Guid SourceShipmentId,
    Guid SourceOrderId,
    Guid SourceOrderLineId,
    Guid UnitId,
    string Quantity,
    string SourceDeliveredQuantity,
    string Status,
    string Version,
    string Reason,
    string RequestedBy,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset RequestedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionNote,
    Guid? InventoryMovementId,
    Guid? OriginalLabelId,
    Guid? OriginalShipmentMovementId);

public sealed record CustomerReturnResponseV1(CustomerReturnViewV1 Return, bool IsReplay, string OriginalCorrelationId);

public interface ILogisticsCustomerReturnServiceV1
{
    Task<CustomerReturnResponseV1> RequestAsync(Guid instructionId, Guid lineId, RequestCustomerReturnV1 request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<CustomerReturnResponseV1> ApproveAsync(Guid customerReturnId, ApproveCustomerReturnV1 request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<CustomerReturnResponseV1> RejectAsync(Guid customerReturnId, RejectCustomerReturnV1 request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<CustomerReturnViewV1?> GetAsync(Guid customerReturnId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerReturnViewV1>> ListOrderReturnsAsync(Guid salesOrderId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Server-owned, fresh authority scope. The central adapter resolves company and actor itself and
/// revalidates the named permission; request payloads never supply either identity.
/// </summary>
public interface ILogisticsCustomerReturnScopeV1
{
    Task<LogisticsCustomerReturnScopeV1> RequireAsync(string permission, CancellationToken cancellationToken = default);
    Task RequireStillAsync(LogisticsCustomerReturnScopeV1 scope, string permission, CancellationToken cancellationToken = default);
}

public sealed record LogisticsCustomerReturnScopeV1(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);

/// <summary>The exact immutable Logistics delivery chain. No client-supplied label, location, owner or custodian.</summary>
public sealed record LogisticsCustomerReturnSourceV1(
    Guid CompanyId,
    Guid LoadingInstructionId,
    long LoadingInstructionVersion,
    Guid LoadingInstructionLineId,
    Guid SourceDeliveryId,
    Guid SourceShipmentId,
    Guid SourceOrderId,
    Guid SourceOrderLineId,
    Guid UnitId,
    Quantity DeliveredQuantity);

public interface ILogisticsCustomerReturnSourceReadV1
{
    Task<LogisticsCustomerReturnSourceV1?> FindDeliveryAsync(Guid instructionId, Guid lineId, Guid deliveryId,
        CancellationToken cancellationToken = default);
}

public sealed record CustomerReturnStoreResultV1(CustomerReturn Return, LogisticsWriteOutcome Outcome, string OriginalCorrelationId);
public sealed record CustomerReturnReplayV1(CustomerReturn Return, string OriginalCorrelationId);

public interface ILogisticsCustomerReturnStoreV1
{
    Task<CustomerReturnReplayV1?> FindReplayAsync(LogisticsWriteEnvelope envelope, string requestHash, string command,
        CancellationToken cancellationToken = default);
    Task<CustomerReturn?> FindAsync(Guid customerReturnId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerReturn>> ListOrderReturnsAsync(Guid salesOrderId, CancellationToken cancellationToken = default);
    Task<Quantity> SumApprovedForDeliveryAsync(Guid sourceDeliveryId, QuantityUnit unit,
        CancellationToken cancellationToken = default);
    Task<CustomerReturnStoreResultV1> StoreRequestAsync(LogisticsWriteEnvelope envelope, string requestHash,
        CustomerReturn customerReturn, CancellationToken cancellationToken = default);
    Task<CustomerReturnStoreResultV1> StoreDecisionAsync(LogisticsWriteEnvelope envelope, string requestHash, string command,
        CustomerReturn customerReturn, CancellationToken cancellationToken = default);
}

/// <summary>
/// Inventory resolves the immutable definite-shipment trace and writes one Return IN movement on its
/// original label/position. It must preserve owner, custodian and location from that trace and reject
/// a stale/mismatching company, source or quantity. Same return id replays; changed payload conflicts.
/// </summary>
public sealed record LogisticsCustomerReturnStockRequestV1(
    Guid CompanyId,
    Guid CustomerReturnId,
    Guid LoadingInstructionId,
    Guid LoadingInstructionLineId,
    Guid SourceDeliveryId,
    Guid SourceShipmentId,
    Guid SourceOrderId,
    Guid SourceOrderLineId,
    Guid UnitId,
    decimal Quantity,
    Guid InventoryMovementId,
    DateTimeOffset OccurredAtUtc);

public sealed record LogisticsCustomerReturnStockResultV1(
    Guid CompanyId,
    Guid CustomerReturnId,
    Guid SourceShipmentId,
    Guid InventoryMovementId,
    Guid OriginalShipmentMovementId,
    Guid OriginalLabelId,
    decimal Quantity,
    bool IsReplay,
    string OriginalCorrelationId);

public interface ILogisticsCustomerReturnStockV1
{
    Task<LogisticsCustomerReturnStockResultV1> StageAsync(LogisticsCustomerReturnStockRequestV1 request,
        LogisticsWriteEnvelope envelope, CancellationToken cancellationToken = default);
    Task RequireStillAsync(CancellationToken cancellationToken = default);
}

public interface ILogisticsCustomerReturnTransactionRunnerV1
{
    Task<T> ExecuteLogisticsAsync<T>(Guid companyId, Func<CancellationToken, Task<T>> work,
        Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default);
    Task<T> ExecuteApprovalAsync<T>(Guid companyId,
        Func<ILogisticsCustomerReturnStockV1, CancellationToken, Task<T>> work,
        Func<ILogisticsCustomerReturnStockV1, CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default);
}

public sealed class LogisticsCustomerReturnServiceException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
