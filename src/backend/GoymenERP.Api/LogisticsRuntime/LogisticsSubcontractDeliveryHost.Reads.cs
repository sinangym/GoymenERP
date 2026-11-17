using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using static GoymenERP.Modules.Logistics.Application.Contracts.LogisticsSubcontractDeliveryV1;

namespace GoymenERP.Api.LogisticsRuntime;

public sealed partial class LogisticsSubcontractDeliveryHost
{
    public async Task<LogisticsSubcontractListV1> ListAsync(string? cursor, CancellationToken token = default)
    {
        var scope = await RequireAsync(LogisticsPermissionManifestV1.InstructionsRead, [], token);
        LogisticsInstructionPage page;
        try { page = await store.ListInstructionsAsync(25, cursor, cancellationToken: token); }
        catch (LogisticsStoreException error) when (error.Code == LogisticsStoreCodes.InvalidCursor) { throw Error(400, Invalid); }
        await StillAsync(scope, token);
        if (page.Items.Count > 25 || page.Items.Any(item => item.Instruction.CompanyId != scope.CompanyId)) throw Error(503, Unavailable);
        var items = new List<LogisticsSubcontractListItemV1>();
        foreach (var item in page.Items.Where(item => item.Instruction.IsContractProcessingDelivery))
        {
            var sources = await store.ReadSubcontractSourcesAsync(item.Instruction.Id, token); await StillAsync(scope, token);
            if (sources.Count != item.Instruction.Lines.Count) throw Error(503, Unavailable);
            items.Add(new(item.Instruction.Id, Number(item.Instruction.Version), item.Instruction.Status.ToString(), item.Instruction.CreatedAtUtc,
                sources.Values.Select(source => source.LabelCode).Distinct(StringComparer.Ordinal).Take(3).ToArray()));
        }
        return new(scope.CompanyId, items, page.NextCursor);
    }

    public async Task<LogisticsSubcontractSalesLinesV1> SalesLinesAsync(string? cursor, CancellationToken token = default)
    {
        var scope = await RequireAsync(LogisticsPermissionManifestV1.InstructionsCreate, [], token);
        return await TransactionAsync(scope, async ct =>
        {
            // The existing owner returns a bounded price-free page. The actual subcontract owner proves every offered line.
            SalesApprovedLinePageV1 page;
            try { page = await salesLines.ListApprovedLinesAsync(25, cursor, ct); }
            catch (ArgumentException) { throw Error(400, Invalid); }
            await StillAsync(scope, ct);
            if (page.Items.Count > 25 || page.Items.Any(line => line.CompanyId != scope.CompanyId || line.OrderId == Guid.Empty || line.LineId == Guid.Empty))
                throw Error(503, Unavailable);
            await LockLinesAsync(page.Items.Select(line => line.LineId), ct);
            await using var sales = await SalesAsync(ct); var items = new List<LogisticsSubcontractSalesLineV1>();
            foreach (var group in page.Items.GroupBy(line => line.OrderId))
            {
                var answer = await sales.GetAsync(scope.CompanyId, group.Key, ct); await StillAsync(scope, ct);
                if (answer.Outcome == SalesSubcontractServiceOrderOutcomeV1.NotSubcontractService) continue;
                if (answer.Outcome != SalesSubcontractServiceOrderOutcomeV1.Found) throw Error(503, Unavailable);
                var order = await OrderAsync(scope, sales, group.Key, ct);
                foreach (var offered in group)
                {
                    var line = order.Lines.SingleOrDefault(line => line.LineId == offered.LineId && line.ItemType == "Service");
                    if (line is not null) items.Add(new(order.OrderId, line.LineId, order.DocumentNo, line.ProductCode, line.ProductName));
                }
            }
            return new LogisticsSubcontractSalesLinesV1(scope.CompanyId, items, page.NextCursor);
        }, token);
    }

    public async Task<LogisticsSubcontractCandidatesV1> CandidatesAsync(Guid salesOrderId, Guid salesOrderLineId, int pageIndex, CancellationToken token = default)
    {
        var scope = await RequireAsync(LogisticsPermissionManifestV1.InstructionsCreate, [], token);
        if (salesOrderId == Guid.Empty || salesOrderLineId == Guid.Empty || pageIndex < 0) throw Error(400, Invalid);
        return await TransactionAsync(scope, async ct =>
        {
            await LockLinesAsync([salesOrderLineId], ct);
            await using var sales = await SalesAsync(ct); await using var manufacturing = await ManufacturingAsync(ct); await using var stock = await InventoryAsync(scope, ct);
            var order = await OrderAsync(scope, sales, salesOrderId, ct);
            if (!order.Lines.Any(line => line.LineId == salesOrderLineId && line.ItemType == "Service")) throw Error(404, "not_found");
            var page = await manufacturing.ReadBySalesLineAsync(scope.CompanyId, salesOrderId, salesOrderLineId, pageIndex, ct);
            await StillAsync(scope, ct);
            if (page.Outcome != ManufacturingSubcontractCompletionOutcomeV1.Available) throw Error(503, Unavailable);
            var items = new List<LogisticsSubcontractCandidateV1>();
            foreach (var item in page.Items)
            foreach (var output in item.Value!.Outputs)
            {
                var actual = await SourceAsync(scope, salesOrderId, salesOrderLineId, item.ManufacturingOrderId,
                    output.OutputFactId, output.LabelId, sales, manufacturing, stock, ct);
                var positions = actual.Label.Positions.Where(p => p.OwnerKind == "Counterparty" && p.OwnerId == order.PartyId
                    && p.CustodianKind == "Company" && p.CustodianId == scope.CompanyId && p.Block == "Free").ToArray();
                // Discovery offers only an actual single eligible position, just as ReserveAsync later requires.
                if (positions.Length != 1) continue;
                var s = actual.Source; var position = positions[0];
                items.Add(new(s.SalesOrderId, s.SalesOrderLineId, actual.Order.DocumentNo, actual.Line.ProductCode, actual.Line.ProductName,
                    s.ManufacturingOrderId, Number(s.ManufacturingOrderVersion), Number(s.ManufacturingPlanVersion), Number(s.ManufacturingSalesLinkRevision),
                    s.SalesLineRevision, Number(s.SalesOrderVersion), s.CustomerPartyId, s.CustomerAccountId, s.OutputFactId, s.LabelId,
                    actual.Label.LabelCode, actual.Label.LabelVersion, s.ProductId, s.ProductVersion, s.OutputNetKg,
                    position.PhysicalKg, position.ReservedKg, position.AvailableKg, actual.Label.IsReleasable));
            }
            return new LogisticsSubcontractCandidatesV1(scope.CompanyId, items, pageIndex, page.HasNext);
        }, token);
    }

    public async Task<LogisticsSubcontractInstructionV1> ReadAsync(Guid instructionId, CancellationToken token = default)
    {
        var scope = await RequireAsync(LogisticsPermissionManifestV1.InstructionsRead, [], token);
        // Immutable identities only, without retained locks, establish the lock plan. Fresh rows are checked again below.
        RequireOwnedContext();
        var plan = await store.ReadSubcontractSourcesAsync(instructionId, token); await StillAsync(scope, token);
        return await TransactionAsync(scope, async ct =>
        {
            await LockLinesAsync(plan.Values.Select(source => source.SalesOrderLineId), ct);
            await using var stock = await InventoryAsync(scope, ct); var labels = await LabelsAsync(stock, plan.Values, ct);
            var stored = await store.FindInstructionAsync(instructionId, ct) ?? throw Error(404, "not_found");
            if (!stored.Instruction.IsContractProcessingDelivery) throw Error(404, "not_found");
            var sources = await store.ReadSubcontractSourcesAsync(instructionId, ct);
            if (sources.Count != plan.Count || sources.Any(pair => !plan.TryGetValue(pair.Key, out var old) || pair.Value != old)) throw Error(503, Unavailable);
            return View(stored.Instruction, sources, labels, false, "");
        }, token);
    }

    private static async Task<IReadOnlyDictionary<Guid, InventorySubcontractLabelReadV1>> LabelsAsync(InventorySubcontractDeliveryParticipant stock,
        IEnumerable<LogisticsSubcontractSourceV1> sources, CancellationToken token)
    {
        var answer = new Dictionary<Guid, InventorySubcontractLabelReadV1>();
        foreach (var id in sources.Select(source => source.LabelId).Distinct().Order())
            answer.Add(id, await stock.ReadLabelAsync(id, token) ?? throw Error(503, Unavailable));
        return answer;
    }
    private static LogisticsSubcontractInstructionV1 View(LoadingInstruction instruction,
        IReadOnlyDictionary<Guid, LogisticsSubcontractSourceV1> sources, IReadOnlyDictionary<Guid, InventorySubcontractLabelReadV1>? labels,
        bool replay, string correlation)
    {
        if (!instruction.IsContractProcessingDelivery || instruction.Lines.Count != sources.Count) throw Error(503, Unavailable);
        var lines = instruction.Lines.Select(line =>
        {
            var source = sources[line.Id]; InventorySubcontractLabelReadV1? label = null;
            if (labels is not null) labels.TryGetValue(source.LabelId, out label);
            var allocation = label?.Allocations.SingleOrDefault(allocation => allocation.AllocationId == line.AllocationId);
            return new LogisticsSubcontractLineV1(line.Id, source, line.AllocationId ?? throw Error(503, Unavailable),
                Kg(line.InstructedQuantity.Value), Kg(line.LoadedQuantity.Value), Kg(line.ShippedQuantity.Value), Kg(line.DeliveredQuantity.Value),
                allocation?.Version, label?.LabelVersion);
        }).ToArray();
        var correction = instruction.WeighingCorrections.LastOrDefault();
        var weighing = instruction.Weighing is { } w ? new LogisticsSubcontractWeighingV1(w.WeighingId, instruction.WeighingRevision,
            (correction?.Direction ?? w.Direction).ToString(), Kg(correction?.FirstWeighing.Value ?? w.FirstWeighing.Value),
            Kg(correction?.SecondWeighing.Value ?? w.SecondWeighing.Value), Kg(correction?.Net.Value ?? w.Net.Value), Kg(w.LoadedAtWeighing.Value),
            Kg(correction?.DifferenceKg ?? w.DifferenceKg)) : null;
        return new(instruction.CompanyId, instruction.Id, Number(instruction.Version), instruction.Status.ToString(), instruction.CustomerPartyId,
            instruction.CompanyAccountId, lines, weighing, instruction.WeighingCorrections.Select(c => new LogisticsSubcontractCorrectionV1(c.CorrectionId,
                c.Revision, c.Direction.ToString(), Kg(c.FirstWeighing.Value), Kg(c.SecondWeighing.Value), Kg(c.Net.Value), Kg(c.PreviousNet.Value),
                Kg(c.DifferenceKg), c.Reason, c.CorrectedBy, c.CorrectedAtUtc)).ToArray(), replay, correlation,
            instruction.Weighing is { } original ? new(original.WeighingId, 1, original.Direction.ToString(), Kg(original.FirstWeighing.Value),
                Kg(original.SecondWeighing.Value), Kg(original.Net.Value), Kg(original.LoadedAtWeighing.Value), Kg(original.DifferenceKg)) : null);
    }
}
