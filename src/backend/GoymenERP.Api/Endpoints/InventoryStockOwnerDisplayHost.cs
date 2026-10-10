using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// Central record-bound adapter from an authorized Inventory stock page to Cari's dedicated owner
/// projection. It exposes no endpoint and accepts no client-supplied Party identifiers.
/// </summary>
public sealed class InventoryStockOwnerDisplayHost(
    ICounterpartyInventoryStockOwnerDisplaySourceV1 source) : IInventoryStockOwnerDisplaySourceV1
{
    public async Task<InventoryStockOwnerDisplayBatchAnswerV1> GetAuthorizedBatchAsync(
        InventoryStockOwnerDisplayBatchRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var answer = await source.ReadForAuthorizedInventoryStockOwnersAsync(
            new CounterpartyInventoryStockOwnerDisplayRequestV1(
                request.CompanyId,
                request.ActorId,
                request.PartyIds),
            cancellationToken);

        return new(
            answer.Outcome switch
            {
                CounterpartyInventoryStockOwnerDisplayOutcomeV1.Found =>
                    InventoryStockOwnerDisplayBatchOutcomeV1.Found,
                CounterpartyInventoryStockOwnerDisplayOutcomeV1.Denied =>
                    InventoryStockOwnerDisplayBatchOutcomeV1.Denied,
                _ => InventoryStockOwnerDisplayBatchOutcomeV1.Unavailable,
            },
            [.. answer.Items.Select(item => new InventoryStockOwnerDisplayV1(
                item.PartyId,
                item.Code,
                item.LegalName,
                item.TradeName))]);
    }
}
