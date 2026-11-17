using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Logistics.Application.Contracts;

// Only expected identities/versions and entered quantities are public input. Every source fact is rebuilt by the host.
public sealed record CreateSubcontractDeliveryV1(Guid InstructionId, DateOnly EffectiveOn, IReadOnlyList<SubcontractDeliveryLineRequestV1> Lines);
public sealed record SubcontractDeliveryLineRequestV1(Guid LineId, Guid SalesOrderId, Guid SalesOrderLineId, string ExpectedSalesOrderVersion,
    int SalesLineRevision, Guid ManufacturingOrderId, string ExpectedManufacturingOrderVersion, string ExpectedPlanVersion,
    string ExpectedSalesLinkRevision, Guid OutputFactId, Guid LabelId, string ExpectedLabelVersion, Guid AllocationId, string QuantityKg, string Reason);
public sealed record ShipSubcontractDeliveryV1(string ExpectedVersion, DateTimeOffset OccurredAtUtc, IReadOnlyList<SubcontractDeliveryShipmentLineV1> Lines);
public sealed record SubcontractDeliveryShipmentLineV1(Guid LineId, Guid ShipmentId, Guid MovementId, string ExpectedAllocationVersion, string ExpectedLabelVersion,
    Guid SalesOrderId, Guid SalesOrderLineId, Guid ManufacturingOrderId, Guid OutputFactId, Guid LabelId);
public sealed record CorrectSubcontractWeighingV1(string ExpectedVersion, Guid WeighingId, int ExpectedWeighingRevision, Guid CorrectionId,
    string Direction, string FirstWeighingKg, string SecondWeighingKg, string Reason);

/// <summary>Immutable, price-free owner provenance stored with a subcontract instruction line. ProductId is physical material; SalesServiceProductId is not.</summary>
public sealed record LogisticsSubcontractSourceV1(Guid CompanyId, Guid CustomerPartyId, Guid CustomerAccountId,
    Guid SalesOrderId, Guid SalesOrderLineId, [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long SalesOrderVersion, int SalesLineRevision,
    Guid ProductId, string ProductVersion, Guid SalesServiceProductId, Guid ManufacturingOrderId,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long ManufacturingOrderVersion,
    Guid ManufacturingPlanId, [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long ManufacturingPlanVersion,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long ManufacturingSalesLinkRevision, Guid OutputFactId, Guid LabelId,
    string OutputNetKg, string ManufacturingSourceVersion, string LabelCode, string SalesDocumentNo, string ServiceCode, string ServiceName);

public sealed record LogisticsSubcontractCandidateV1(Guid SalesOrderId, Guid SalesOrderLineId, string DocumentNo, string ServiceCode, string ServiceName,
    Guid ManufacturingOrderId, string ManufacturingOrderVersion, string PlanVersion, string SalesLinkRevision, int SalesLineRevision,
    string SalesOrderVersion, Guid CustomerPartyId, Guid CustomerAccountId, Guid OutputFactId, Guid LabelId, string LabelCode, string LabelVersion,
    Guid ProductId, string ProductVersion, string DeclaredKg, string PhysicalKg, string ReservedKg, string AvailableKg, bool IsReleasable);
public sealed record LogisticsSubcontractCandidatesV1(Guid CompanyId, IReadOnlyList<LogisticsSubcontractCandidateV1> Items, int PageIndex, bool HasNext);
public sealed record LogisticsSubcontractSalesLineV1(Guid SalesOrderId, Guid SalesOrderLineId, string DocumentNo, string ServiceCode, string ServiceName);
public sealed record LogisticsSubcontractSalesLinesV1(Guid CompanyId, IReadOnlyList<LogisticsSubcontractSalesLineV1> Items, string? NextCursor);
public interface ILogisticsSubcontractDeliverySourceStoreV1
{
    Task<IReadOnlyDictionary<Guid, LogisticsSubcontractSourceV1>> ReadSubcontractSourcesAsync(Guid instructionId, CancellationToken token = default);
}
public sealed record LogisticsSubcontractListItemV1(Guid InstructionId, string Version, string Status, DateTimeOffset CreatedAtUtc, IReadOnlyList<string> LabelCodes);
public sealed record LogisticsSubcontractListV1(Guid CompanyId, IReadOnlyList<LogisticsSubcontractListItemV1> Items, string? NextCursor);
public sealed record LogisticsSubcontractInstructionV1(Guid CompanyId, Guid InstructionId, string Version, string Status, Guid CustomerPartyId,
    Guid CustomerAccountId, IReadOnlyList<LogisticsSubcontractLineV1> Lines, LogisticsSubcontractWeighingV1? Weighing,
    IReadOnlyList<LogisticsSubcontractCorrectionV1> Corrections, bool IsReplay, string OriginalCorrelationId,
    LogisticsSubcontractWeighingV1? OriginalWeighing = null, Guid? RecordedLoadId = null);
public sealed record LogisticsSubcontractLineV1(Guid LineId, LogisticsSubcontractSourceV1 Source, Guid AllocationId,
    string InstructedKg, string LoadedKg, string ShippedKg, string DeliveredKg, string? AllocationVersion, string? LabelVersion);
public sealed record LogisticsSubcontractWeighingV1(Guid WeighingId, int Revision, string Direction, string FirstKg, string SecondKg, string NetKg,
    string LoadedKg, string DifferenceKg);
public sealed record LogisticsSubcontractCorrectionV1(Guid CorrectionId, int Revision, string Direction, string FirstKg, string SecondKg,
    string NetKg, string PreviousNetKg, string DifferenceKg, string Reason, string CorrectedBy, DateTimeOffset CorrectedAtUtc);

public interface ILogisticsSubcontractDeliveryHostV1
{
    Task<LogisticsSubcontractListV1> ListAsync(string? cursor, CancellationToken token = default);
    Task<LogisticsSubcontractSalesLinesV1> SalesLinesAsync(string? cursor, CancellationToken token = default);
    Task<LogisticsSubcontractCandidatesV1> CandidatesAsync(Guid salesOrderId, Guid salesOrderLineId, int pageIndex, CancellationToken token = default);
    Task<LogisticsSubcontractInstructionV1> ReadAsync(Guid instructionId, CancellationToken token = default);
    Task<LogisticsSubcontractInstructionV1> CreateAsync(CreateSubcontractDeliveryV1 request, LogisticsCommandMetadata metadata, CancellationToken token = default);
    Task<LogisticsSubcontractInstructionV1> LoadAsync(Guid instructionId, Guid lineId, RecordLoadRequest request, LogisticsCommandMetadata metadata, CancellationToken token = default);
    Task<LogisticsSubcontractInstructionV1> ShipAsync(Guid instructionId, ShipSubcontractDeliveryV1 request, LogisticsCommandMetadata metadata, CancellationToken token = default);
    Task<LogisticsSubcontractInstructionV1> CorrectWeighingAsync(Guid instructionId, CorrectSubcontractWeighingV1 request, LogisticsCommandMetadata metadata, CancellationToken token = default);
}

public static class LogisticsSubcontractDeliveryV1
{
    public const string Path = "/api/v1/logistics/subcontract-deliveries";
    public const string ShipCommand = "logistics.loading-instruction.ship-subcontract.v1";
    public const string Unavailable = "logistics_subcontract_delivery_unavailable";
    public const string Invalid = "logistics_subcontract_delivery_invalid";
    public const string SourceConflict = "logistics_subcontract_delivery_source_conflict";
    public const string ActiveInvoice = "logistics_subcontract_delivery_active_invoice";
}
