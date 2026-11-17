using GoymenERP.Modules.Purchasing.Domain.Expenses;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public sealed record RecordPurchaseAdditionalCostV1(Guid CostId, string ExpectedVersion, Guid OrderId, PurchaseAdditionalCostSourceKind SourceKind, string ExpectedOrderVersion,
    Guid? AcceptanceId, Guid SupplierPartyId, Guid CompanyAccountId, DateOnly BusinessDate, Guid? ExpenseTypeId,
    string? ExpectedExpenseTypeVersion, string Description, string NetAmount, string VatRateCode, string ExpectedVatListVersion, string Reason);
public sealed record PurchaseAdditionalCostViewV1(Guid CompanyId, Guid CostId, string Version, Guid OrderId, string SourceOrderVersion,
    string? SourceOrderNumber, string SourceKind, Guid? AcceptanceId, Guid SupplierPartyId, Guid CompanyAccountId, DateOnly BusinessDate,
    Guid CurrencyId, string CurrencyCode, string Description, Guid? ExpenseTypeId, string? ExpenseTypeVersion, string? ExpenseTypeName,
    string NetAmount, string VatPercent, string VatAmount, string GrossAmount, string RecordedBy, string Reason, DateTimeOffset RecordedAtUtc);
public sealed record PurchaseAdditionalCostResponseV1(PurchaseAdditionalCostViewV1 Cost, bool IsReplay, string OriginalCorrelationId);
public sealed record PurchaseAdditionalCostReferencesV1(Guid CompanyId, bool TypeRequired,
    IReadOnlyList<PurchaseServiceTypeOptionV1> ExpenseTypes, DateOnly BusinessDate, string VatListVersion,
    IReadOnlyList<PurchaseAdditionalCostVatOptionV1> VatRates);
public sealed record PurchaseAdditionalCostVatOptionV1(string Code, string Name, string Percent);
public sealed record PurchaseAdditionalCostSourceV1(Guid CompanyId, Guid OrderId, long OrderVersion, string? OrderNumber,
    PurchaseAdditionalCostSourceKind Kind, Guid? AcceptanceId, Guid? AcceptanceLineId, PurchaseServiceMoney Money)
{
    public IReadOnlyList<PurchaseAdditionalCostAcceptanceOptionV1> Acceptances { get; init; } = [];
}
public sealed record PurchaseAdditionalCostAcceptanceOptionV1(Guid AcceptanceId, Guid LineId, string Quantity, DateTimeOffset RecordedAtUtc);
public sealed record PurchaseAdditionalCostSourceViewV1(Guid CompanyId, Guid OrderId, string OrderVersion, string? OrderNumber,
    string SourceKind, Guid CurrencyId, string CurrencyCode, IReadOnlyList<PurchaseAdditionalCostAcceptanceOptionV1> Acceptances);
public interface IPurchaseAdditionalCostServiceV1
{
    Task<PurchaseAdditionalCostSourceViewV1> SourceAsync(PurchaseAdditionalCostSourceKind kind, Guid orderId, CancellationToken token = default);
    Task<PurchaseAdditionalCostReferencesV1> ReferencesAsync(DateOnly on, CancellationToken token = default);
    Task<IReadOnlyList<PurchaseAdditionalCostViewV1>> ListAsync(PurchaseAdditionalCostSourceKind kind, Guid orderId, CancellationToken token = default);
    Task<PurchaseAdditionalCostResponseV1> RecordAsync(RecordPurchaseAdditionalCostV1 request, PurchasingCommandMetadata metadata, CancellationToken token = default);
}
public interface IPurchaseAdditionalCostStoreV1
{
    Task<PurchaseAdditionalCostSourceV1?> ReadSourceAsync(PurchaseAdditionalCostSourceKind kind, Guid orderId, Guid? acceptanceId, CancellationToken token);
    Task<IReadOnlyList<PurchaseAdditionalCostState>> ListAsync(PurchaseAdditionalCostSourceKind kind, Guid orderId, CancellationToken token);
    Task<PurchaseAdditionalCostWriteV1?> ReplayAsync(PurchasingWriteEnvelope envelope, string hash, CancellationToken token);
    Task<PurchaseAdditionalCostWriteV1> RecordAsync(Guid costId, PurchasingWriteEnvelope envelope, string hash,
        Func<CancellationToken, Task<PurchaseAdditionalCostState>> create, Func<CancellationToken, Task> requireStill, CancellationToken token);
}
public sealed record PurchaseAdditionalCostWriteV1(PurchaseAdditionalCostState? Cost, PurchasingTerminalRefusal? Refusal, bool IsReplay, string OriginalCorrelationId);
public static class PurchaseAdditionalCostCommandsV1 { public const string Record = "purchasing.additional-cost.record.v1"; }
/// <summary>Server composition only after the existing-permission decision is explicit. Absence keeps writes closed; this is no grant.</summary>
public sealed record PurchaseAdditionalCostWriteAdmissionV1(string Permission, string DecisionReference);
