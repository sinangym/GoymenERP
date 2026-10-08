using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// Central, record-bound adapter between the authorized Sales read seam and Cari's dedicated
/// Sales-record projection. It exposes no endpoint, accepts no client Party id and grants no
/// general Cari read capability.
/// </summary>
public sealed class SalesPartyDisplayHost(
    ICounterpartySalesRecordPartyDisplaySourceV1 source) : ISalesPartyDisplaySourceV1
{
    public async Task<SalesPartyDisplayBatchAnswerV1> GetAuthorizedBatchAsync(
        SalesPartyDisplayBatchRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var answer = await source.ReadForAuthorizedSalesRecordsAsync(
            new CounterpartySalesRecordPartyDisplayRequestV1(
                request.CompanyId,
                request.ActorId,
                request.PartyIds),
            cancellationToken);

        if (answer is null || answer.Items is null)
        {
            return Closed(SalesPartyDisplayBatchOutcomeV1.Unavailable);
        }

        return answer.Outcome switch
        {
            CounterpartySalesRecordPartyDisplayOutcomeV1.Found =>
                new SalesPartyDisplayBatchAnswerV1(
                    SalesPartyDisplayBatchOutcomeV1.Success,
                    [.. answer.Items.Select(item => new SalesPartyDisplayV1(
                        item.PartyId,
                        item.Code,
                        item.LegalName,
                        item.TradeName))]),
            CounterpartySalesRecordPartyDisplayOutcomeV1.Denied =>
                Closed(SalesPartyDisplayBatchOutcomeV1.Denied),
            _ => Closed(SalesPartyDisplayBatchOutcomeV1.Unavailable),
        };
    }

    private static SalesPartyDisplayBatchAnswerV1 Closed(SalesPartyDisplayBatchOutcomeV1 outcome) =>
        new(outcome, []);
}
