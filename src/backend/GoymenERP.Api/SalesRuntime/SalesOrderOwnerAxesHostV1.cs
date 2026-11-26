using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.SalesRuntime;

/// <summary>SAT-D478/479. State-only composition of existing owner evidence. No writes, grants, cost disclosure or bank-match gate.
/// Each observation is bounded and repeated; it is not a cross-module lock. The native close command reads it again at save.</summary>
public sealed class SalesOrderOwnerAxesHostV1(ICompanyContext company, ICurrentUser user, ICorrelationContext correlation,
    ISalesInvoiceableShipmentReadPortV1 shipments, ILogisticsInvoicedShipmentProofReadPortV1 logistics,
    IInventoryShipmentCostReadPortV1 inventory, ISalesFinalizedShipmentClaimReadPortV1 invoices,
    ISalesOrderInvoicedQuantityReadPortV1 invoicedQuantities,
    ISalesOrderAllocationReadPortV2 allocations, IManufacturingSalesOrderSupplyReadPortV1 production,
    IPurchaseSalesOrderObligationReadPortV1 purchasing, IFinanceSalesInvoiceSettlementPortV1 finance)
    : ISalesOrderOwnerAxesSourceV1
{
    private const int MaximumShares = 128;
    private static readonly SalesOrderOwnerAxisV1 Missing = new(SalesDraftStatusAxisStatesV1.NoSource, false);
    private static readonly SalesOrderOwnerAxisV1 Unknown = new(SalesDraftStatusAxisStatesV1.Unreadable, false);
    private static readonly SalesOrderOwnerAxisV1 Open = new(SalesDraftStatusAxisStatesV1.Partial, false);
    private static readonly SalesOrderOwnerAxisV1 Complete = new(SalesDraftStatusAxisStatesV1.Completed, true);

    // The old seam has no caller fence or Sales snapshot: it cannot authorize production reads.
    public Task<SalesOrderOwnerAxesV1?> ReadOrderOwnerAxesAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult<SalesOrderOwnerAxesV1?>(null);

    public async Task<SalesOrderOwnerAxesV1?> ReadOrderOwnerAxesAsync(SalesOrderClosureQueryV1 query,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        var actor = user.UserId;
        SalesOrderOwnerAxesV1 Answer(SalesOrderOwnerAxisV1 source, SalesOrderOwnerAxisV1 invoicing, SalesOrderOwnerAxisV1 financial) =>
            new(query.CompanyId, query.OrderId, source, invoicing, financial);
        bool Current() => company.IsResolved && company.CompanyId != Guid.Empty && company.CompanyId == query.CompanyId
            && user.IsAuthenticated && !string.IsNullOrWhiteSpace(actor) && user.UserId == actor && user.CanAccessCompany(query.CompanyId);
        async Task Fence()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Current() || freshConsumerFence is null) throw new UnauthorizedAccessException();
            await freshConsumerFence(cancellationToken);
            if (!Current()) throw new UnauthorizedAccessException();
        }
        async Task<T> Read<T>(Func<Task<T>> read)
        {
            await Fence();
            try { return await read(); }
            finally { await Fence(); }
        }
        if (!Current() || freshConsumerFence is null || query.OrderId == Guid.Empty || query.PartyId == Guid.Empty
            || query.CompanyAccountId == Guid.Empty || query.Lines is null || query.Lines.Count is 0 or > 500
            || query.Lines.Any(x => x.LineId == Guid.Empty || x.ProductId == Guid.Empty || x.UnitId == Guid.Empty)
            || query.Lines.Select(x => x.LineId).Distinct().Count() != query.Lines.Count) return Answer(Unknown, Unknown, Unknown);
        // Subcontract invoices use their own immutable weighing/claim family; do not misclassify absence of normal invoices.
        if (query.IsSubcontractService) { await Fence(); return Answer(Missing, Missing, Missing); }
        try
        {
            var first = await Observe();
            var second = await Observe();
            await Fence();
            return first.Signature == second.Signature ? first.Axes : Answer(Unknown, Unknown, Unknown);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Answer(Unknown, Unknown, Unknown); }

        async Task<Observation> Observe()
        {
            var shares = await Read(() => shipments.ReadInvoiceableShipmentsAsync(
                new(query.CompanyAccountId, MaximumShares, [query.OrderId]), cancellationToken));
            if (shares.Outcome != SalesDispatchReadOutcomeV1.Found || shares.CompanyId != query.CompanyId
                || shares.HasMore || shares.Shares.Count > MaximumShares
                || shares.Shares.Select(x => x.ShipmentId).Distinct().Count() != shares.Shares.Count)
                return new(Answer(Unknown, Unknown, Unknown), "shipments_unavailable");
            var lines = query.Lines.ToDictionary(x => x.LineId);
            if (shares.Shares.Any(x => x.SalesOrderId != query.OrderId || x.CustomerPartyId != query.PartyId
                    || x.CompanyAccountId != query.CompanyAccountId || x.ShipmentId == Guid.Empty || x.DispatchNoteId == Guid.Empty
                    || x.NoteLineNo < 1 || x.Quantity <= 0m || !lines.TryGetValue(x.SalesOrderLineId, out var line)
                    || line.ProductId != x.ProductId || line.UnitId != x.UnitId))
                return new(Answer(Unknown, Unknown, Unknown), "shipment_identity_invalid");
            // Include actual unapproved/uninvoiced dispatches: the Sales-measured total cannot disappear from the posted picker.
            foreach (var line in query.Lines)
                if (!decimal.TryParse(line.ShippedQuantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var shipped)
                    || shipped < 0m || shares.Shares.Where(x => x.SalesOrderLineId == line.LineId).Sum(x => x.Quantity) != shipped)
                    return new(Answer(Open, Open, Unknown), "shipment_coverage_incomplete");
            if (shares.Shares.Count == 0) return new(Answer(Open, Open, Open), "no_shipments");
            var signature = new List<string> { JsonSerializer.Serialize(shares) };
            var source = await Source();
            var billed = await Read(() => invoicedQuantities.ReadAsync(query.CompanyId, query.OrderId, freshConsumerFence, cancellationToken));
            signature.Add(JsonSerializer.Serialize(billed));
            if (billed.CompanyId != query.CompanyId || billed.SalesOrderId != query.OrderId
                || billed.Outcome != SalesOrderInvoicedQuantityReadOutcomeV1.Available || string.IsNullOrWhiteSpace(billed.SourceVersion)
                || billed.Lines.Any(x => x.InvoiceId == Guid.Empty || x.InvoiceLineId == Guid.Empty || x.InvoiceKind != "Normal"
                    || !lines.TryGetValue(x.SalesOrderLineId, out var line) || line.UnitId != x.UnitId || x.Quantity <= 0m))
                return Result(source, Unknown, Unknown);
            if (query.Lines.Any(line => billed.Lines.Where(x => x.SalesOrderLineId == line.LineId).Sum(x => x.Quantity)
                    != shares.Shares.Where(x => x.SalesOrderLineId == line.LineId).Sum(x => x.Quantity)))
                return Result(source, Open, Open);
            var claims = await Read(() => invoices.ReadAsync(query.CompanyId, shares.Shares.Select(x => x.ShipmentId).ToArray(),
                freshConsumerFence, cancellationToken));
            signature.Add(JsonSerializer.Serialize(claims));
            if (claims.CompanyId != query.CompanyId || claims.Outcome != SalesShipmentClaimReadOutcomeV1.Available
                || claims.Items.Count != shares.Shares.Count || claims.Items.Select(x => x.ShipmentId).Distinct().Count() != claims.Items.Count)
                return Result(source, Unknown, Unknown);
            var ids = new Dictionary<Guid, (Guid AdmissionId, string Version)>();
            foreach (var share in shares.Shares)
            {
                var item = claims.Items.SingleOrDefault(x => x.ShipmentId == share.ShipmentId);
                if (item?.Outcome == SalesShipmentClaimReadOutcomeV1.NotInvoiced) return Result(source, Open, Open);
                if (item?.Outcome != SalesShipmentClaimReadOutcomeV1.Available || item.Claim is not { } claim
                    || claim.CompanyId != query.CompanyId || claim.InvoiceId == Guid.Empty || claim.AdmissionId == Guid.Empty
                    || claim.InvoiceLineId == Guid.Empty || claim.FinalizedEventId == Guid.Empty || claim.FinalizedVersion < 1
                    || string.IsNullOrWhiteSpace(claim.FinalizedPayloadSha256) || claim.Lifecycle.InvoiceId != claim.InvoiceId
                    || claim.Lifecycle.State != "Finalized" || claim.Lifecycle.ReleasedAtUtc is not null
                    || claim.Lifecycle.CancelledAtUtc is not null || claim.Lifecycle.CancelledBy is not null
                    || claim.ShipmentSource is not { } s
                    || s.ShipmentId != share.ShipmentId || s.DispatchNoteId != share.DispatchNoteId || s.DispatchNoteLineNo != share.NoteLineNo
                    || s.SalesOrderId != query.OrderId || s.SalesOrderLineId != share.SalesOrderLineId || s.ProductId != share.ProductId
                    || s.UnitId != share.UnitId || !decimal.TryParse(s.InvoicedQuantity, NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var quantity) || quantity != share.Quantity)
                    return Result(source, Unknown, Unknown);
                if (billed.Lines.Count(x => x.InvoiceId == claim.InvoiceId && x.InvoiceLineId == claim.InvoiceLineId
                        && x.SalesOrderLineId == share.SalesOrderLineId && x.UnitId == share.UnitId && x.Quantity == share.Quantity) != 1)
                    return Result(source, Unknown, Unknown);
                var identity = (claim.AdmissionId, claim.FinalizedVersion.ToString(CultureInfo.InvariantCulture));
                if (ids.TryGetValue(claim.InvoiceId, out var prior) && prior != identity) return Result(source, Unknown, Unknown);
                ids[claim.InvoiceId] = identity;
            }
            if (!ids.Keys.Order().SequenceEqual(billed.Lines.Select(x => x.InvoiceId).Distinct().Order()))
                return Result(source, Unknown, Unknown);
            try
            {
                var context = new FinanceContext(query.CompanyId, actor!, correlation.CorrelationId, correlation.CausationId);
                var settlement = await Read(() => finance.ReadAsync(context, ids.Keys.Order().ToArray(), freshConsumerFence, cancellationToken));
                signature.Add(JsonSerializer.Serialize(settlement));
                if (settlement.Outcome != ReadOutcome.Found || settlement.Resource is not { } batch || batch.CompanyId != query.CompanyId
                    || batch.Invoices.Count != ids.Count || batch.Invoices.Select(x => x.InvoiceId).Distinct().Count() != ids.Count
                    || batch.Invoices.Any(x => !ids.TryGetValue(x.InvoiceId, out var id) || x.ScopeId != id.AdmissionId || x.SourceVersion != id.Version
                        || x.Status is not (FinanceSalesInvoiceSettlementStatusV1.Open or FinanceSalesInvoiceSettlementStatusV1.Settled)))
                    return Result(source, Complete, Unknown);
                return Result(source, Complete, batch.Invoices.All(x => x.Status == FinanceSalesInvoiceSettlementStatusV1.Settled) ? Complete : Open);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return Result(source, Complete, Unknown); }

            Observation Result(SalesOrderOwnerAxisV1 s, SalesOrderOwnerAxisV1 i, SalesOrderOwnerAxisV1 f) =>
                new(Answer(s, i, f), JsonSerializer.Serialize(new { s, i, f, signature }));

            async Task<SalesOrderOwnerAxisV1> Source()
            {
                try
                {
                    var allocation = await Read(() => allocations.ReadOrderAllocationsAsync(query.OrderId, cancellationToken));
                    var manufacture = await Read(() => production.ListOpenForSalesOrderAsync(query.CompanyId, query.OrderId, null, cancellationToken));
                    var purchase = await Read(() => purchasing.ReadAsync(query.CompanyId, query.OrderId, freshConsumerFence, cancellationToken));
                    signature.Add(JsonSerializer.Serialize(new { allocation, manufacture, purchase }));
                    if (allocation.CompanyId != query.CompanyId || allocation.OrderId != query.OrderId
                        || allocation.Lines.Any(x => !lines.ContainsKey(x.OrderLineId) || !SalesOrderAllocationKindsV2.IsKnown(x.Kind)
                            || x.ActiveCount < 0 || x.ActiveQuantity < 0m || (x.ActiveCount == 0) != (x.ActiveQuantity == 0m)
                            || x.Kind == SalesOrderAllocationKindsV2.Physical && x.UnitId is not null
                            || x.Kind == SalesOrderAllocationKindsV2.FutureSupply && (x.UnitId is null || x.UnitId == Guid.Empty))
                        || allocation.Lines.Select(x => (x.OrderLineId, x.Kind, x.UnitId)).Distinct().Count() != allocation.Lines.Count
                        || manufacture.Any(x => x.CompanyId != query.CompanyId || !lines.ContainsKey(x.SalesOrderLineId))
                        || purchase.CompanyId != query.CompanyId || purchase.SalesOrderId != query.OrderId
                        || purchase.Status is not (PurchaseSalesOrderObligationStatusV1.Complete or PurchaseSalesOrderObligationStatusV1.Open)) return Unknown;
                    var pending = allocation.Lines.Any(x => x.ActiveCount > 0) || manufacture.Count > 0
                        || purchase.Status == PurchaseSalesOrderObligationStatusV1.Open;
                    foreach (var share in shares.Shares)
                    {
                        var proof = await Read(() => logistics.GetAsync(query.CompanyId, share.DispatchNoteId, share.NoteLineNo, share.ShipmentId, cancellationToken));
                        var physical = await Read(() => inventory.GetByShipmentAsync(query.CompanyId, share.ShipmentId, cancellationToken));
                        var p = proof.Proof;
                        signature.Add(JsonSerializer.Serialize(new { proof, physical.Outcome, physical.CompanyId, physical.ShipmentId,
                            physical.MovementId, physical.SourceVersion })); // Never serialize a cost stamp into the composition proof.
                        if (proof.Outcome != LogisticsInvoicedShipmentProofOutcomeV1.Found || p is null || p.CompanyId != query.CompanyId
                            || p.SalesOrderId != query.OrderId || p.SalesOrderLineId != share.SalesOrderLineId || p.ShipmentId != share.ShipmentId
                            || p.DispatchNoteId != share.DispatchNoteId || p.DispatchNoteLineNo != share.NoteLineNo
                            || p.ProductId != share.ProductId || p.UnitId != share.UnitId || p.DispatchQuantity != share.Quantity
                            || physical.Outcome != InventoryShipmentCostReadOutcomeV1.Available || physical.CompanyId != query.CompanyId
                            || physical.ShipmentId != share.ShipmentId || physical.MovementId == Guid.Empty || physical.Lines.Count == 0
                            || physical.Lines.Any(x => x.LoadingInstructionId != p.InstructionId || x.LoadingInstructionLineId != p.InstructionLineId
                                || x.ProductId != p.ProductId || x.AllocationId == Guid.Empty || x.QuantityKg <= 0m)) return Unknown;
                        var kilograms = lines[share.SalesOrderLineId].UnitIsKilogram ? share.Quantity : share.Kilograms;
                        if (kilograms is not > 0m || physical.Lines.Sum(x => x.QuantityKg) != kilograms) return Unknown;
                    }
                    return pending ? Open : Complete;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { return Unknown; }
            }
        }
    }
    private sealed record Observation(SalesOrderOwnerAxesV1 Axes, string Signature);
}
