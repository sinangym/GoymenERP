namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// STK-S33 (KLN-D44): the Logistics-owned read of the customer behind loading-instruction lines, for the label trace's shipment
/// step (the definite-shipment Issue names the instruction line as its source line). Company-filtered, untracked and side-effect
/// free; it carries no authority (LOG-4 D6): the consumer measures its own permission around the call. Without a resolved
/// company it throws <see cref="InvalidOperationException"/> (never reads unscoped). A line of another company, or an unknown
/// line, is simply absent from the answer, never that company's data and never a 403.
/// </summary>
public interface ILogisticsShipmentCustomerReadPortV1
{
    /// <summary>Technical read bound: the most instruction lines one call answers.</summary>
    const int MaximumBatchSize = 200;

    Task<IReadOnlyList<LogisticsShipmentCustomerV1>> ReadByInstructionLinesAsync(
        IReadOnlyCollection<Guid> instructionLineIds,
        CancellationToken cancellationToken = default);
}

public sealed record LogisticsShipmentCustomerV1(
    Guid InstructionLineId,
    Guid InstructionId,
    Guid CustomerPartyId,
    Guid SourceOrderId,
    Guid SourceOrderLineId);
