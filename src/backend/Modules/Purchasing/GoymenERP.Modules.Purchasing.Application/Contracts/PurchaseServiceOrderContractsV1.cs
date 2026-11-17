using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public sealed record CreatePurchaseServiceOrderV1(Guid OrderId, string ExpectedVersion, PurchaseServiceOrderKind Kind,
    Guid SupplierPartyId, Guid CompanyAccountId, DateOnly BusinessDate, Guid CurrencyId,
    IReadOnlyList<CreatePurchaseServiceLineV1> Lines, IReadOnlyList<PurchaseServiceMaterialInputV1> Materials,
    IReadOnlyList<PurchaseServiceOutputInputV1> ExpectedOutputs, string Reason);
public sealed record PurchaseServiceMaterialInputV1(Guid LabelId, string LabelVersion, Guid ProductId, string ProductVersion, string QuantityKg);
public sealed record PurchaseServiceOutputInputV1(Guid ProductId, string ProductVersion, string ExpectedQuantityKg);
public sealed record CreatePurchaseServiceLineV1(Guid LineId, Guid ServiceTypeId, string ExpectedServiceTypeVersion,
    Guid UnitId, string Quantity, string? UnitPrice);
public sealed record ApprovePurchaseServiceOrderV1(string ExpectedVersion, string Reason);
public sealed record AcceptPurchaseServiceV1(string ExpectedVersion, Guid AcceptanceId, Guid LineId, string Quantity,
    DateOnly AcceptedOn, string Reason);
public sealed record PurchaseServiceOrderResponseV1(PurchaseServiceOrderViewV1 Order, bool IsReplay, string OriginalCorrelationId);
public sealed record PurchaseServiceLineViewV1(Guid LineId, Guid ServiceTypeId, string ServiceTypeVersion, string ServiceName,
    Guid UnitId, string UnitCode, string Quantity, string AcceptedQuantity, string? UnitPrice, string? Amount);
public sealed record PurchaseServiceOrderViewV1(Guid CompanyId, Guid OrderId, string DocumentNumber, string Version, string Kind, string Status,
    Guid SupplierPartyId, Guid CompanyAccountId, DateOnly BusinessDate, Guid CurrencyId, string CurrencyCode, bool IsOpen,
    string PriceDisclosure, IReadOnlyList<PurchaseServiceLineViewV1> Lines, IReadOnlyList<PurchaseServiceMaterialInputV1> Materials,
    IReadOnlyList<PurchaseServiceOutputInputV1> ExpectedOutputs, IReadOnlyList<PurchaseServiceAcceptanceViewV1> Acceptances);
public sealed record PurchaseServiceAcceptanceViewV1(Guid AcceptanceId, Guid LineId, string Quantity, DateOnly AcceptedOn,
    string RecordedBy, string Reason, DateTimeOffset RecordedAtUtc);
public sealed record PurchaseServiceTypeOptionV1(Guid Id, string Version, string Code, string Name);
public sealed record PurchaseServiceUnitOptionV1(Guid UnitId, string Code);
public sealed record PurchaseServiceOrderReferencesV1(Guid CompanyId, IReadOnlyList<PurchaseServiceTypeOptionV1> ServiceTypes,
    IReadOnlyList<PurchaseServiceUnitOptionV1> Units);
public interface IPurchaseServiceOrderServiceV1
{
    Task<PurchaseServiceOrderReferencesV1> ReferencesAsync(CancellationToken token = default);
    Task<IReadOnlyList<PurchaseServiceOrderViewV1>> ListAsync(CancellationToken token = default);
    Task<PurchaseServiceOrderViewV1> ReadAsync(Guid orderId, CancellationToken token = default);
    Task<PurchaseServiceOrderResponseV1> CreateAsync(CreatePurchaseServiceOrderV1 request, PurchasingCommandMetadata metadata, CancellationToken token = default);
    Task<PurchaseServiceOrderResponseV1> ApproveAsync(Guid orderId, ApprovePurchaseServiceOrderV1 request, PurchasingCommandMetadata metadata, CancellationToken token = default);
    Task<PurchaseServiceOrderResponseV1> AcceptAsync(Guid orderId, AcceptPurchaseServiceV1 request, PurchasingCommandMetadata metadata, CancellationToken token = default);
}

/// <summary>API72, trusted server CompanyId only. Price-free operational facts; this port grants no stock command.</summary>
public interface IPurchaseSubcontractOrderReadPortV1
{
    Task<PurchaseSubcontractOrderAnswerV1> GetAsync(Guid companyId, Guid orderId, CancellationToken token = default);
}
public enum PurchaseSubcontractOrderOutcomeV1 { Available = 1, NotFound = 2, NotSubcontract = 3, Unavailable = 4 }
public sealed record PurchaseSubcontractServiceLineV1(Guid LineId, Guid ServiceTypeId, string ServiceTypeVersion,
    Guid UnitId, string Quantity, string AcceptedQuantity);
public sealed record PurchaseSubcontractOrderV1(Guid CompanyId, Guid OrderId, string Version, Guid SupplierPartyId,
    Guid CompanyAccountId, bool IsApproved, bool IsOpen, IReadOnlyList<PurchaseSubcontractServiceLineV1> ServiceLines,
    IReadOnlyList<PurchaseServiceMaterial> Materials, IReadOnlyList<PurchaseServiceOutput> ExpectedOutputs);
public sealed record PurchaseSubcontractOrderAnswerV1(PurchaseSubcontractOrderOutcomeV1 Outcome, PurchaseSubcontractOrderV1? Order);

/// <summary>Actual performed-service facts for invoice matching. No movement or goods receipt is manufactured.</summary>
public interface IPurchaseServiceAcceptanceReadPortV1
{
    Task<PurchaseServiceAcceptanceAnswerV1> GetAsync(Guid companyId, Guid orderId, Guid acceptanceId, CancellationToken token = default);
}
public enum PurchaseServiceAcceptanceOutcomeV1 { Available = 1, NotFound = 2, Unavailable = 3 }
public sealed record PurchaseServiceAcceptanceSourceV1(Guid CompanyId, Guid OrderId, string OrderVersion, Guid SupplierPartyId,
    Guid CompanyAccountId, Guid AcceptanceId, Guid ServiceLineId, Guid ServiceTypeId, Guid UnitId, string Quantity,
    DateOnly AcceptedOn, Guid CurrencyId, decimal? UnitPrice, string SourceVersion);
public sealed record PurchaseServiceAcceptanceAnswerV1(PurchaseServiceAcceptanceOutcomeV1 Outcome, PurchaseServiceAcceptanceSourceV1? Source);

public interface IPurchaseServiceOrderReferenceProofV1
{
    Task ValidateAsync(Guid companyId, DateOnly businessDate, IReadOnlyList<PurchaseServiceMaterial> materials,
        IReadOnlyList<PurchaseServiceOutput> outputs, CancellationToken token);
}
public sealed record PurchaseServiceOrderWriteV1(PurchaseServiceOrderState? Order, PurchasingTerminalRefusal? Refusal, bool IsReplay, string OriginalCorrelationId);
public interface IPurchaseServiceOrderStoreV1
{
    Task<PurchaseServiceOrderState?> ReadAsync(Guid orderId, CancellationToken token);
    Task<IReadOnlyList<PurchaseServiceOrderState>> ListAsync(CancellationToken token);
    Task<PurchaseServiceOrderWriteV1?> ReplayAsync(string command, PurchasingWriteEnvelope envelope, string hash, CancellationToken token);
    Task<PurchaseServiceOrderWriteV1?> WriteAsync(Guid orderId, string command, PurchasingWriteEnvelope envelope, string hash, string reason,
        Func<PurchaseServiceOrderState?, CancellationToken, Task<PurchaseServiceOrderState>> mutate,
        Func<CancellationToken, Task> requireStill, CancellationToken token);
}
public static class PurchaseServiceOrderCommandsV1
{
    public const string Create = "purchasing.service-order.create.v1";
    public const string Approve = "purchasing.service-order.approve.v1";
    public const string Accept = "purchasing.service-order.accept.v1";
    public static bool IsKnown(string command) => command is Create or Approve or Accept;
}
