using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// STK-S33 (KLN-D44): the host adapter from Manufacturing's <see cref="IManufacturingLabelLineageReadPortV1"/> (URT-S20) to the
/// trace's <see cref="IInventoryLabelTraceProductionSourceV1"/>; Inventory never references Manufacturing. Every input of each
/// output is answered with the owner's own share and status, never recomputed here. An output the owner recorded no edge for is
/// simply not answered (the trace then says Partial). Fail-closed: a call outside the resolved company, a lineage for another output, a
/// null input or more outputs than <see cref="InventoryLabelTraceApiV1.MaximumLabels"/> throws, which the trace answers as
/// Unavailable; cancellation is rethrown.
/// </summary>
public sealed class InventoryLabelTraceProductionHostSource(
    IManufacturingLabelLineageReadPortV1 manufacturing,
    ICompanyContext companyContext) : IInventoryLabelTraceProductionSourceV1
{
    public async Task<InventoryLabelTraceProductionAnswerV1> ReadSharesAsync(
        Guid companyId,
        IReadOnlyList<Guid> outputLabelIds,
        CancellationToken cancellationToken = default)
    {
        InventoryLabelTraceHostGuard.RequireCompany(companyContext, companyId);
        ArgumentNullException.ThrowIfNull(outputLabelIds);
        var outputs = outputLabelIds.Distinct().ToArray();
        if (outputs.Length > InventoryLabelTraceApiV1.MaximumLabels || outputs.Contains(Guid.Empty))
        {
            throw new InvalidOperationException("Üretim izi istenen etiket listesi geçersiz.");
        }

        var items = new List<InventoryLabelTraceProductionShareV1>();
        foreach (var outputLabelId in outputs)
        {
            var lineage = await manufacturing.ReadBackwardAsync(companyId, outputLabelId, cancellationToken);
            if (lineage is null)
            {
                continue;
            }

            if (lineage.CompanyId != companyId || lineage.OutputLabelId != outputLabelId || lineage.Inputs is null
                || string.IsNullOrWhiteSpace(lineage.ShareStatus))
            {
                throw new InvalidOperationException("Üretim sahibinin iz cevabı tutarsız.");
            }

            foreach (var input in lineage.Inputs)
            {
                if (input is null)
                {
                    throw new InvalidOperationException("Üretim sahibinin iz cevabı tutarsız.");
                }

                items.Add(new InventoryLabelTraceProductionShareV1(
                    outputLabelId,
                    input.InputLabelId,
                    lineage.OrderId,
                    lineage.GenealogyTransformationId,
                    input.ConsumedKg,
                    input.Share,
                    lineage.ShareStatus));
            }
        }

        return new InventoryLabelTraceProductionAnswerV1(true, items);
    }
}

/// <summary>
/// STK-S33 (KLN-D44, KLN-D41): the host adapter answering the party of each receipt and shipment source of the trace.
/// <list type="bullet">
/// <item>Receipt (<see cref="InventoryGoodsReceiptContractV1.SourceKind"/>): Inventory's own immutable receipt source names the
/// purchase order line; the supplier is Purchasing's answer for that line (<see cref="IPurchaseSupplyReferencePortV1"/>), which
/// today answers approved orders only, so a closed order's supplier stays unknown (a status-free supplier port is requested
/// from the Purchasing owner). The supplier's original label number is the receipt owner's own recorded text for the same label
/// identity (KLN-D41).</item>
/// <item>Shipment (<see cref="InventoryLabelTraceStepsV1.DefiniteShipmentSourceKind"/>): the loading-instruction line the movement
/// names is answered by Logistics (<see cref="ILogisticsShipmentCustomerReadPortV1"/>) with its instruction's customer.</item>
/// </list>
/// A source no owner knows is answered with a null party, never guessed. Fail-closed: another company, a repeated or foreign
/// answer throws, which the trace answers as Unavailable; cancellation is rethrown. Carries no authority.
/// </summary>
public sealed class InventoryLabelTracePartyHostSource(
    IInventoryGoodsReceiptSourceV1 receipts,
    IPurchaseSupplyReferencePortV1 purchaseLines,
    IPurchaseGoodsReceiptBatchStore receiptLabels,
    ILogisticsShipmentCustomerReadPortV1 shipments,
    ICompanyContext companyContext) : IInventoryLabelTracePartySourceV1
{
    /// <summary>Technical read bound of the receipt owner's label lookup by one label code.</summary>
    public const int ReceiptLabelLookupSize = 25;

    public async Task<InventoryLabelTracePartyAnswerV1> ResolveAsync(
        InventoryLabelTracePartyRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        InventoryLabelTraceHostGuard.RequireCompany(companyContext, request.CompanyId);
        if (request.Sources is null || request.Sources.Any(source => source is null))
        {
            throw new InvalidOperationException("İz taraf isteği geçersiz.");
        }

        var companyId = request.CompanyId;
        var items = new List<InventoryLabelTracePartyMatchV1>();
        foreach (var source in request.Sources.Where(source => source.SourceKind == InventoryGoodsReceiptContractV1.SourceKind))
        {
            items.Add(await ResolveSupplierAsync(companyId, source, cancellationToken));
        }

        var shipmentSources = request.Sources
            .Where(source => source.SourceKind == InventoryLabelTraceStepsV1.DefiniteShipmentSourceKind)
            .ToArray();
        var lineIds = shipmentSources
            .Where(source => source.SourceLineId is { } lineId && lineId != Guid.Empty)
            .Select(source => source.SourceLineId!.Value)
            .Distinct()
            .ToArray();
        var customers = new Dictionary<Guid, LogisticsShipmentCustomerV1>();
        foreach (var batch in lineIds.Chunk(ILogisticsShipmentCustomerReadPortV1.MaximumBatchSize))
        {
            var answer = await shipments.ReadByInstructionLinesAsync(batch, cancellationToken)
                ?? throw new InvalidOperationException("Lojistik sahibinin müşteri cevabı yok.");
            foreach (var customer in answer)
            {
                if (customer is null || !batch.Contains(customer.InstructionLineId) || customer.CustomerPartyId == Guid.Empty
                    || !customers.TryAdd(customer.InstructionLineId, customer))
                {
                    throw new InvalidOperationException("Lojistik sahibinin müşteri cevabı tutarsız.");
                }
            }
        }

        foreach (var source in shipmentSources)
        {
            Guid? partyId = source.SourceLineId is { } lineId && customers.TryGetValue(lineId, out var customer)
                ? customer.CustomerPartyId
                : null;
            items.Add(new InventoryLabelTracePartyMatchV1(source.SourceKind, source.SourceId, source.SourceLineId, source.LabelId,
                InventoryLabelTracePartyRolesV1.Customer, partyId, null));
        }

        return new InventoryLabelTracePartyAnswerV1(true, items);
    }

    private async Task<InventoryLabelTracePartyMatchV1> ResolveSupplierAsync(
        Guid companyId,
        InventoryLabelTracePartySourceV1 source,
        CancellationToken cancellationToken)
    {
        Guid? supplierPartyId = null;
        var receipt = await receipts.ReadAsync(source.SourceId, cancellationToken);
        if (receipt?.OriginalRequest is { } original)
        {
            if (original.CompanyId != companyId || original.AcceptanceId != source.SourceId)
            {
                throw new InvalidOperationException("Stok kabul kaynağı tutarsız.");
            }

            var line = await purchaseLines.GetApprovedLineAsync(original.PurchaseOrderId, original.PurchaseOrderLineId,
                cancellationToken) ?? throw new InvalidOperationException("Satın alma sahibinin cevabı yok.");
            if (line.Outcome == PurchaseSupplyReferenceOutcomeV1.Approved)
            {
                if (line.Line is null || line.Line.CompanyId != companyId || line.Line.OrderId != original.PurchaseOrderId
                    || line.Line.LineId != original.PurchaseOrderLineId || line.Line.SupplierPartyId == Guid.Empty)
                {
                    throw new InvalidOperationException("Satın alma sahibinin cevabı tutarsız.");
                }

                supplierPartyId = line.Line.SupplierPartyId;
            }
        }

        string? supplierLabelNumber = null;
        if (!string.IsNullOrWhiteSpace(source.LabelCode))
        {
            var matches = await receiptLabels.SearchLabelsAsync(source.LabelCode, ReceiptLabelLookupSize, cancellationToken)
                ?? throw new InvalidOperationException("Satın alma sahibinin etiket cevabı yok.");
            var numbers = matches
                .Where(match => match is not null && match.LabelId == source.LabelId)
                .Select(match => string.IsNullOrWhiteSpace(match.SupplierLabelNumber) ? null : match.SupplierLabelNumber.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (numbers.Length > 1)
            {
                throw new InvalidOperationException("Satın alma sahibinin etiket cevabı tutarsız.");
            }

            supplierLabelNumber = numbers.SingleOrDefault();
        }

        return new InventoryLabelTracePartyMatchV1(source.SourceKind, source.SourceId, source.SourceLineId, source.LabelId,
            InventoryLabelTracePartyRolesV1.Supplier, supplierPartyId, supplierLabelNumber);
    }
}

/// <summary>
/// STK-S33 (KLN-D41): the host adapter from the receipt owner's label search (our code or the supplier's original number, prefix)
/// to <see cref="IInventoryLabelTraceSupplierSearchSourceV1"/>. The trace keeps only labels Inventory itself knows in the same
/// company. Fail-closed: another company or a null answer throws (Unavailable); cancellation is rethrown.
/// </summary>
public sealed class InventoryLabelTraceSupplierSearchHostSource(
    IPurchaseGoodsReceiptBatchStore receiptLabels,
    ICompanyContext companyContext) : IInventoryLabelTraceSupplierSearchSourceV1
{
    public async Task<InventoryLabelTraceSupplierSearchAnswerV1> SearchAsync(
        Guid companyId,
        string text,
        int take,
        CancellationToken cancellationToken = default)
    {
        InventoryLabelTraceHostGuard.RequireCompany(companyContext, companyId);
        if (string.IsNullOrWhiteSpace(text) || take is < 1 or > InventoryLabelTraceApiV1.MaximumSearchResults + 1)
        {
            throw new InvalidOperationException("Tedarikçi etiket araması geçersiz.");
        }

        var matches = await receiptLabels.SearchLabelsAsync(text, take, cancellationToken)
            ?? throw new InvalidOperationException("Satın alma sahibinin etiket cevabı yok.");
        var items = matches
            .Select(match => match is null
                ? throw new InvalidOperationException("Satın alma sahibinin etiket cevabı tutarsız.")
                : new InventoryLabelTraceSupplierMatchV1(match.LabelId, match.LabelCode,
                    string.IsNullOrWhiteSpace(match.SupplierLabelNumber) ? null : match.SupplierLabelNumber.Trim()))
            .ToArray();
        return new InventoryLabelTraceSupplierSearchAnswerV1(true, items);
    }
}

internal static class InventoryLabelTraceHostGuard
{
    public static void RequireCompany(ICompanyContext companyContext, Guid companyId)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || companyContext.CompanyId != companyId)
        {
            throw new InvalidOperationException("İz kaynağı çözümlenmiş şirket bağlamı dışında çağrıldı.");
        }
    }
}
