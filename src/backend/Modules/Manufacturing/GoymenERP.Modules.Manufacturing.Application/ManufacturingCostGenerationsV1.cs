namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>URT-D56/58/67: immutable results of a particular physical close, including unavailable and reversed results.</summary>
public enum ManufacturingCostGenerationStateV1 { Pending = 1, Unreadable = 2, Recognized = 3, Reversed = 4 }

public sealed record ManufacturingCostGenerationV1(Guid CompanyId, Guid OrderId, Guid GenerationId,
    long ClosedOrderVersion, long Revision, string InventoryVersion, ManufacturingCostGenerationStateV1 State,
    string InputFingerprint, string CalculatorVersion, string EvidenceJson, string ResultJson,
    DateTimeOffset RecordedAtUtc, string Reason, string CorrelationId);

public interface IManufacturingCostGenerationStoreV1
{
    Task<IReadOnlyList<ManufacturingCostGenerationV1>> ReadCostGenerationsAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<ManufacturingCostGenerationV1> AppendCostGenerationAsync(ManufacturingCostGenerationV1 generation,
        long expectedRevision, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default);
}

/// <summary>The close host stages this in its existing SQL transaction; the Inventory owner never commits independently.</summary>
public interface IManufacturingClosedOrderCostRecognitionPortV1
{
    Task<ManufacturingCostGenerationV1> RecognizeAsync(Guid orderId, long expectedOrderVersion, long expectedGenerationRevision,
        string reason, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken);
}

public sealed record RecognizeManufacturingOrderCostRequestV1(long ExpectedVersion, long ExpectedGenerationRevision, string? Reason);
