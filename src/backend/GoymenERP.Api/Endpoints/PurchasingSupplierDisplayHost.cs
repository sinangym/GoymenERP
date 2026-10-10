using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// Central record-bound adapter from authorized Purchasing orders to Cari's dedicated projection.
/// It exposes no endpoint, accepts no client Party id and grants no generic Cari read capability.
/// </summary>
public sealed class PurchasingSupplierDisplayHost(
    ICounterpartyPurchasingRecordPartyDisplaySourceV1 source) : IPurchasingSupplierDisplaySourceV1
{
    public async Task<PurchasingSupplierDisplayBatchAnswerV1> GetAuthorizedBatchAsync(
        PurchasingSupplierDisplayBatchRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var answer = await source.ReadForAuthorizedPurchasingRecordsAsync(
            new CounterpartyPurchasingRecordPartyDisplayRequestV1(
                request.CompanyId,
                request.ActorId,
                request.PartyIds),
            cancellationToken);

        if (answer is null || answer.Items is null)
        {
            return Closed(PurchasingSupplierDisplayBatchOutcomeV1.Unavailable);
        }

        return answer.Outcome switch
        {
            CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Found =>
                new PurchasingSupplierDisplayBatchAnswerV1(
                    PurchasingSupplierDisplayBatchOutcomeV1.Success,
                    [.. answer.Items.Select(item => new PurchasingSupplierDisplayV1(
                        item.PartyId,
                        item.Code,
                        item.LegalName,
                        item.TradeName))]),
            CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Denied =>
                Closed(PurchasingSupplierDisplayBatchOutcomeV1.Denied),
            _ => Closed(PurchasingSupplierDisplayBatchOutcomeV1.Unavailable),
        };
    }

    private static PurchasingSupplierDisplayBatchAnswerV1 Closed(
        PurchasingSupplierDisplayBatchOutcomeV1 outcome) => new(outcome, []);
}
