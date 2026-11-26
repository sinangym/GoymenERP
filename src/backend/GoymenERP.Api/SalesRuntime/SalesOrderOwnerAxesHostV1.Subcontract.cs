using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Api.SalesRuntime;

public sealed partial class SalesOrderOwnerAxesHostV1
{
    private async Task<SalesOrderOwnerAxesV1?> ReadSubcontractAsync(SalesOrderClosureQueryV1 query,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken token)
    {
        var actor = user.UserId;
        SalesOrderOwnerAxesV1 Answer(SalesOrderOwnerAxisV1 source, SalesOrderOwnerAxisV1 billed, SalesOrderOwnerAxisV1 financial) =>
            new(query.CompanyId, query.OrderId, source, billed, financial);
        try
        {
            await Fence();
            if (subcontractDeliveries is null || subcontractInvoices is null) return Answer(Missing, Missing, Missing);
            var before = await Observe(); var after = await Observe(); await Fence();
            return before.Signature == after.Signature ? before.Axes : Answer(Unknown, Unknown, Unknown);
        }
        catch (OperationCanceledException) { throw; }
        catch { return Answer(Unknown, Unknown, Unknown); }

        async Task<Observation> Observe()
        {
            var delivery = await Read(() => subcontractDeliveries!.ReadAsync(query.CompanyId, query.OrderId, freshConsumerFence, token));
            var signature = new List<string> { JsonSerializer.Serialize(delivery) };
            Observation Result(SalesOrderOwnerAxisV1 s, SalesOrderOwnerAxisV1 i, SalesOrderOwnerAxisV1 f) =>
                new(Answer(s, i, f), JsonSerializer.Serialize(new { s, i, f, signature }));
            var lines = query.Lines.ToDictionary(x => x.LineId);
            if (!delivery.Available || delivery.CompanyId != query.CompanyId || delivery.SalesOrderId != query.OrderId
                || string.IsNullOrWhiteSpace(delivery.SourceVersion) || delivery.Instructions.Count > MaximumShares
                || delivery.Instructions.Select(x => x.InstructionId).Distinct().Count() != delivery.Instructions.Count)
                return Result(Unknown, Unknown, Unknown);
            var shares = new List<(LogisticsSubcontractOrderDeliveryV1 Instruction, LogisticsSubcontractOrderDeliveryLineV1 Line,
                LogisticsSubcontractDeliveryShipmentV1 Shipment)>();
            var pending = false;
            foreach (var instruction in delivery.Instructions)
            {
                if (instruction.InstructionId == Guid.Empty || instruction.InstructionVersion < 1 || instruction.Lines.Count == 0
                    || instruction.Lines.Select(x => x.InstructionLineId).Distinct().Count() != instruction.Lines.Count)
                    return Result(Unknown, Unknown, Unknown);
                foreach (var line in instruction.Lines)
                {
                    var source = line.Source;
                    if (source.CompanyId != query.CompanyId || source.SalesOrderId != query.OrderId
                        || source.CustomerPartyId != query.PartyId || source.CustomerAccountId != query.CompanyAccountId
                        || !lines.TryGetValue(source.SalesOrderLineId, out var sales) || !sales.UnitIsKilogram
                        || sales.UnitId != line.UnitId || sales.ProductId != source.SalesServiceProductId
                        || line.InstructedKg <= 0m || line.ApprovedExcessKg < 0m || line.ShippedKg < 0m
                        || line.ShippedKg > line.InstructedKg + line.ApprovedExcessKg
                        || line.DeliveredKg < 0m || line.DeliveredKg > line.ShippedKg) return Result(Unknown, Unknown, Unknown);
                    pending |= !instruction.Cancelled && line.ShippedKg < line.InstructedKg;
                    var own = instruction.Shipments.Where(x => x.LineId == line.InstructionLineId).ToArray();
                    if (own.Any(x => x.ShipmentId == Guid.Empty || !Positive(x.ShippedKg, out _))
                        || own.Sum(x => decimal.Parse(x.ShippedKg, CultureInfo.InvariantCulture)) != line.ShippedKg
                        || instruction.Cancelled && own.Length != 0) return Result(Unknown, Unknown, Unknown);
                    shares.AddRange(own.Select(x => (instruction, line, x)));
                }
                if (instruction.Shipments.Any(x => instruction.Lines.All(line => line.InstructionLineId != x.LineId)))
                    return Result(Unknown, Unknown, Unknown);
            }
            if (shares.Count > MaximumShares || shares.Select(x => x.Shipment.ShipmentId).Distinct().Count() != shares.Count)
                return Result(Unknown, Unknown, Unknown);
            foreach (var line in query.Lines)
                if (!decimal.TryParse(line.ShippedQuantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var shipped)
                    || shipped < 0m || shares.Where(x => x.Line.Source.SalesOrderLineId == line.LineId)
                        .Sum(x => decimal.Parse(x.Shipment.ShippedKg, CultureInfo.InvariantCulture)) != shipped)
                    return Result(Open, Unknown, Unknown);
            var sourceAxis = await Source();
            if (shares.Count == 0) return Result(sourceAxis, Open, Open);
            var quantities = await Read(() => invoicedQuantities.ReadAsync(query.CompanyId, query.OrderId, freshConsumerFence, token));
            signature.Add(JsonSerializer.Serialize(quantities));
            if (quantities.CompanyId != query.CompanyId || quantities.SalesOrderId != query.OrderId
                || quantities.Outcome != SalesOrderInvoicedQuantityReadOutcomeV1.Available || string.IsNullOrWhiteSpace(quantities.SourceVersion)
                || quantities.Lines.Any(x => x.InvoiceKind != "Subcontract" || x.InvoiceId == Guid.Empty || x.InvoiceLineId == Guid.Empty
                    || !lines.TryGetValue(x.SalesOrderLineId, out var line) || line.UnitId != x.UnitId || x.Quantity <= 0m))
                return Result(sourceAxis, Unknown, Unknown);
            var ids = new Dictionary<Guid, (Guid AdmissionId, string Version)>();
            var invoiceLineIds = new HashSet<Guid>();
            foreach (var instruction in delivery.Instructions.Where(x => x.Shipments.Count > 0))
            {
                if (instruction.WeighingId is not { } weighingId || instruction.Weighing is not { } weighing
                    || !weighing.IsContractProcessingDelivery || weighing.WeighingRevision < 1
                    || weighing.InstructionVersion != instruction.InstructionVersion || !Positive(weighing.NetKg, out _))
                    return Result(sourceAxis, Unknown, Unknown);
                var invoice = await Read(() => subcontractInvoices!.FindSubcontractServiceInvoiceByWeighingAsync(
                    query.OrderId, instruction.InstructionId, weighingId, token));
                if (invoice is null) return Result(sourceAxis, Open, Open);
                signature.Add(JsonSerializer.Serialize(new { invoice.Id, invoice.Version, invoice.State, invoice.Source,
                    AdmissionId = invoice.Finalization?.AdmissionId, FinalizedVersion = invoice.Finalization?.FinalizedVersion,
                    Lines = invoice.Lines.Select(x => new { x.Id, x.Input.SalesOrderLineId, x.Input.ProductId, x.Input.UnitId, x.Input.Quantity }) }));
                if (invoice.CompanyId != query.CompanyId || invoice.Header.PartyId != query.PartyId
                    || invoice.Header.CompanyAccountId != query.CompanyAccountId || invoice.Source.SalesOrderId != query.OrderId
                    || invoice.Source.LoadingInstructionId != instruction.InstructionId || invoice.Source.WeighingId != weighingId)
                    return Result(sourceAxis, Unknown, Unknown);
                if (invoice.State is SalesSubcontractServiceInvoiceState.Draft or SalesSubcontractServiceInvoiceState.PendingApproval
                    or SalesSubcontractServiceInvoiceState.Approved) return Result(sourceAxis, Open, Open);
                if (invoice.State != SalesSubcontractServiceInvoiceState.Finalized || invoice.Finalization is not { } finalized
                    || finalized.AdmissionId == Guid.Empty || finalized.FinalizedVersion != invoice.Version || invoice.Match is null
                    || invoice.Source.WeighingRevision != weighing.WeighingRevision || invoice.Source.InstructionVersion > instruction.InstructionVersion
                    || invoice.Source.NetKgText != weighing.NetKg || invoice.Lines.Count == 0)
                    return Result(sourceAxis, Unknown, Unknown);
                foreach (var line in invoice.Lines)
                {
                    if (!invoiceLineIds.Add(line.Id) || !lines.TryGetValue(line.Input.SalesOrderLineId, out var sales) || sales.ProductId != line.Input.ProductId
                        || sales.UnitId != line.Input.UnitId || instruction.Lines.All(x => x.Source.SalesOrderLineId != sales.LineId)
                        || quantities.Lines.Count(x => x.InvoiceId == invoice.Id && x.InvoiceLineId == line.Id
                            && x.SalesOrderLineId == sales.LineId && x.UnitId == line.Input.UnitId && x.Quantity == line.Input.Quantity) != 1)
                        return Result(sourceAxis, Unknown, Unknown);
                }
                ids.Add(invoice.Id, (finalized.AdmissionId, finalized.FinalizedVersion.ToString(CultureInfo.InvariantCulture)));
            }
            if (!ids.Keys.Order().SequenceEqual(quantities.Lines.Select(x => x.InvoiceId).Distinct().Order())
                || !quantities.Lines.Select(x => x.InvoiceLineId).Order().SequenceEqual(invoiceLineIds.Order()))
                return Result(sourceAxis, Unknown, Unknown);
            if (subcontractInvoices is not ISalesSubcontractInvoiceListStoreV1 historyOwner)
                return Result(sourceAxis, Complete, Missing);
            var history = await Read(() => historyOwner.ListSubcontractInvoicesAsync(query.OrderId, 100, null, token));
            signature.Add(JsonSerializer.Serialize(history));
            if (history.SalesOrderId != query.OrderId || history.NextCursor is not null || history.Items.Count > 100
                || history.Items.Select(x => x.Id).Distinct().Count() != history.Items.Count
                || history.Items.Any(x => x.Id == Guid.Empty || !long.TryParse(x.Version, out var version) || version < 1)
                || !history.Items.Where(x => x.State == "Finalized").Select(x => x.Id).Order().SequenceEqual(ids.Keys.Order()))
                return Result(sourceAxis, Complete, Unknown);
            // The existing Finance settlement contract deliberately does not declare a cancelled invoice cleared.
            // Retain this gap explicitly even after a replacement invoice settles; never erase reversal-pending debt.
            if (history.Items.Any(x => x.State == "OfficiallyCancelled")) return Result(sourceAxis, Complete, Unknown);
            var settlement = await Read(() => finance.ReadAsync(new(query.CompanyId, actor!, correlation.CorrelationId, correlation.CausationId),
                ids.Keys.Order().ToArray(), freshConsumerFence, token));
            signature.Add(JsonSerializer.Serialize(settlement));
            if (settlement.Outcome != ReadOutcome.Found || settlement.Resource is not { } batch || batch.CompanyId != query.CompanyId
                || batch.Invoices.Count != ids.Count || batch.Invoices.Select(x => x.InvoiceId).Distinct().Count() != ids.Count
                || batch.Invoices.Any(x => !ids.TryGetValue(x.InvoiceId, out var id) || x.ScopeId != id.AdmissionId || x.SourceVersion != id.Version
                    || x.Status is not (FinanceSalesInvoiceSettlementStatusV1.Open or FinanceSalesInvoiceSettlementStatusV1.Settled)))
                return Result(sourceAxis, Complete, Unknown);
            return Result(sourceAxis, Complete, batch.Invoices.All(x => x.Status == FinanceSalesInvoiceSettlementStatusV1.Settled) ? Complete : Open);

            async Task<SalesOrderOwnerAxisV1> Source()
            {
                if (subcontractPhysical is null) return Missing;
                // The delivery aggregate proves cancellation; released Inventory reservation history needs separate owner proof.
                if (delivery.Instructions.Any(x => x.Cancelled)) return Unknown;
                try
                {
                    var physical = await Read(() => subcontractPhysical.ReadAsync(query.CompanyId, query.OrderId,
                        shares.Select(x => x.Shipment.ShipmentId).ToArray(), freshConsumerFence, token));
                    var manufacture = await Read(() => production.ListOpenForSalesOrderAsync(query.CompanyId, query.OrderId, null, token));
                    var purchase = await Read(() => purchasing.ReadAsync(query.CompanyId, query.OrderId, freshConsumerFence, token));
                    signature.Add(JsonSerializer.Serialize(new { physical, manufacture, purchase }));
                    if (!physical.Available || physical.CompanyId != query.CompanyId || physical.SalesOrderId != query.OrderId
                        || string.IsNullOrWhiteSpace(physical.SourceVersion) || physical.Shipments.Count != shares.Count
                        || physical.Shipments.Select(x => x.ShipmentId).Distinct().Count() != shares.Count
                        || manufacture.Any(x => x.CompanyId != query.CompanyId || !lines.ContainsKey(x.SalesOrderLineId))
                        || purchase.CompanyId != query.CompanyId || purchase.SalesOrderId != query.OrderId
                        || purchase.Status is not (PurchaseSalesOrderObligationStatusV1.Open or PurchaseSalesOrderObligationStatusV1.Complete)) return Unknown;
                    foreach (var (instruction, line, share) in shares)
                    {
                        var fact = physical.Shipments.SingleOrDefault(x => x.ShipmentId == share.ShipmentId);
                        var s = fact?.Source; var original = line.Source;
                        if (fact is null || s is null || fact.InstructionId != instruction.InstructionId
                            || fact.InstructionLineId != line.InstructionLineId || fact.MovementId == Guid.Empty
                            || fact.PhysicalKg != decimal.Parse(share.ShippedKg, CultureInfo.InvariantCulture)
                            || s.CompanyId != query.CompanyId || s.CustomerPartyId != query.PartyId || s.CustomerAccountId != query.CompanyAccountId
                            || s.SalesOrderId != query.OrderId || s.SalesOrderLineId != original.SalesOrderLineId
                            || s.ProductId != original.ProductId || s.ProductVersion != original.ProductVersion
                            || s.SalesServiceProductId != original.SalesServiceProductId || s.LabelId != original.LabelId
                            || s.ManufacturingOrderId != original.ManufacturingOrderId || s.ManufacturingPlanId != original.ManufacturingPlanId
                            || s.OutputFactId != original.OutputFactId) return Unknown;
                    }
                    return pending || physical.HasOpenAllocations || manufacture.Count > 0 || purchase.Status == PurchaseSalesOrderObligationStatusV1.Open ? Open : Complete;
                }
                catch (OperationCanceledException) { throw; }
                catch { return Unknown; }
            }
        }
        static bool Positive(string? text, out decimal value) => decimal.TryParse(text, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value) && value > 0m;
        async Task<T> Read<T>(Func<Task<T>> read)
        {
            await Fence(); try { return await read(); } finally { await Fence(); }
        }
        async Task Fence()
        {
            token.ThrowIfCancellationRequested();
            bool Current() => company.IsResolved && company.CompanyId == query.CompanyId && user.IsAuthenticated
                && user.UserId == actor && !string.IsNullOrWhiteSpace(actor) && user.CanAccessCompany(query.CompanyId);
            if (!Current()) throw new UnauthorizedAccessException();
            await freshConsumerFence(token);
            if (!Current()) throw new UnauthorizedAccessException();
        }
    }
}
