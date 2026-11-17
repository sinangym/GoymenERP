using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// F1 → F2 (SAT-D309/D312, LOJ-D9/D12): the host adapter between the Logistics-owned delivery net-weight read and the Sales
/// consumer seam of the subcontract service invoice. It exposes no endpoint, carries no authority (the Sales service fences
/// around it), writes nothing and maps only facts: Logistics's kilogram text stays verbatim, nothing is split, rounded or priced
/// here. Anything the Sales seam cannot state consistently (another unit, a missing value, a key that is not echoed) is
/// <see cref="SalesSubcontractDeliveryWeighingStatusV1.Unavailable"/> — fail closed, never a silent zero.
/// </summary>
public sealed class SalesSubcontractDeliveryWeighingHost(
    ILogisticsSubcontractDeliveryNetWeightReadPortV1 source) : ISalesSubcontractDeliveryWeighingReadPortV1
{
    public async Task<SalesSubcontractDeliveryWeighingReadV1> ReadAsync(Guid companyId, Guid loadingInstructionId, Guid weighingId,
        CancellationToken cancellationToken = default)
    {
        var answer = await source.GetAsync(companyId, loadingInstructionId, weighingId, cancellationToken);
        if (answer is null
            || answer.CompanyId != companyId
            || answer.LoadingInstructionId != loadingInstructionId
            || answer.WeighingId != weighingId)
        {
            return Closed(SalesSubcontractDeliveryWeighingStatusV1.Unavailable, companyId, loadingInstructionId, weighingId);
        }

        return answer.Status switch
        {
            LogisticsSubcontractDeliveryNetWeightStatusV1.Available when answer.Value is { } value
                && string.Equals(value.Unit, LogisticsSubcontractDeliveryNetWeightContractV1.KilogramUnit, StringComparison.Ordinal)
                && value.Lines is not null =>
                new SalesSubcontractDeliveryWeighingReadV1(
                    SalesSubcontractDeliveryWeighingStatusV1.Available,
                    companyId,
                    loadingInstructionId,
                    weighingId,
                    new SalesSubcontractDeliveryWeighingV1(
                        value.NetKg,
                        value.WeighingRevision,
                        value.InstructionVersion,
                        [.. value.Lines.Select(line => new SalesSubcontractDeliveryWeighingLineV1(
                            line.SourceOrderId,
                            line.SourceOrderLineId,
                            line.ProductId,
                            line.UnitId,
                            line.LoadedKgAtWeighing,
                            value.Shipments is null ? null : [.. value.Shipments.Where(shipment => shipment.LineId == line.LineId)
                                .Select(shipment => new SalesSubcontractShipmentFactV1(shipment.ShipmentId, shipment.RecordedAtUtc))]))])),
            LogisticsSubcontractDeliveryNetWeightStatusV1.NoSource =>
                Closed(SalesSubcontractDeliveryWeighingStatusV1.NoSource, companyId, loadingInstructionId, weighingId),
            _ => Closed(SalesSubcontractDeliveryWeighingStatusV1.Unavailable, companyId, loadingInstructionId, weighingId),
        };
    }

    private static SalesSubcontractDeliveryWeighingReadV1 Closed(SalesSubcontractDeliveryWeighingStatusV1 status, Guid companyId,
        Guid loadingInstructionId, Guid weighingId) =>
        new(status, companyId, loadingInstructionId, weighingId, null);
}
