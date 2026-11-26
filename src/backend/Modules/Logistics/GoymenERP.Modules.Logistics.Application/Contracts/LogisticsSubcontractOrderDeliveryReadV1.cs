namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>Bounded, price-free owner evidence for this order's subcontract delivery obligations only.</summary>
public interface ILogisticsSubcontractOrderDeliveryReadPortV1
{
    Task<LogisticsSubcontractOrderDeliveryReadV1> ReadAsync(Guid companyId, Guid salesOrderId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken token = default);
}
public sealed record LogisticsSubcontractOrderDeliveryReadV1(Guid CompanyId, Guid SalesOrderId, bool Available,
    IReadOnlyList<LogisticsSubcontractOrderDeliveryV1> Instructions, string? SourceVersion);
public sealed record LogisticsSubcontractOrderDeliveryV1(Guid InstructionId, long InstructionVersion, bool Cancelled,
    Guid? WeighingId, LogisticsSubcontractDeliveryNetWeightV1? Weighing,
    IReadOnlyList<LogisticsSubcontractOrderDeliveryLineV1> Lines,
    IReadOnlyList<LogisticsSubcontractDeliveryShipmentV1> Shipments);
public sealed record LogisticsSubcontractOrderDeliveryLineV1(Guid InstructionLineId, Guid UnitId,
    decimal InstructedKg, decimal ShippedKg, decimal DeliveredKg, LogisticsSubcontractSourceV1 Source, decimal ApprovedExcessKg = 0m);
