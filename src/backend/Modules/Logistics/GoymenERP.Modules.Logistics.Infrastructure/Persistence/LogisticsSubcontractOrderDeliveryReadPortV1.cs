using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed class LogisticsSubcontractOrderDeliveryReadPortV1(LogisticsDbContext db, ICompanyContext company,
    ILogisticsInstructionStore store, ILogisticsSubcontractDeliveryNetWeightReadPortV1 netWeight)
    : ILogisticsSubcontractOrderDeliveryReadPortV1
{
    private const int Bound = 128;
    public async Task<LogisticsSubcontractOrderDeliveryReadV1> ReadAsync(Guid companyId, Guid salesOrderId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken token = default)
    {
        LogisticsSubcontractOrderDeliveryReadV1 Failed() => new(companyId, salesOrderId, false, [], null);
        if (companyId == Guid.Empty || salesOrderId == Guid.Empty || freshConsumerFence is null
            || !company.IsResolved || company.CompanyId != companyId) return Failed();
        try
        {
            await Fence();
            var keys = await Discover();
            if (keys.Length > Bound || store is not ILogisticsSubcontractDeliverySourceStoreV1 sourcesOwner) return Failed();
            var result = new List<LogisticsSubcontractOrderDeliveryV1>();
            foreach (var id in keys)
            {
                await Fence();
                var stored = await store.FindInstructionAsync(id, token); await Fence();
                // Existing aggregate rebuild verifies every share, total, lifecycle and weighing revision.
                if (stored?.Instruction is not { } instruction || instruction.CompanyId != companyId
                    || !instruction.IsContractProcessingDelivery || instruction.Id != id) return Failed();
                var sources = await sourcesOwner.ReadSubcontractSourcesAsync(id, token); await Fence();
                var own = instruction.Lines.Where(x => x.Source.OrderId == salesOrderId).ToArray();
                if (own.Length == 0 || own.Length > 500 || sources.Count != instruction.Lines.Count) return Failed();
                var lines = new List<LogisticsSubcontractOrderDeliveryLineV1>();
                foreach (var line in own)
                {
                    if (!sources.TryGetValue(line.Id, out var source) || source.CompanyId != companyId
                        || source.SalesOrderId != salesOrderId || source.SalesOrderLineId != line.Source.LineId
                        || source.ProductId != line.ProductId || source.SalesServiceProductId == Guid.Empty
                        || source.CustomerPartyId != instruction.CustomerPartyId || source.CustomerAccountId != instruction.CompanyAccountId) return Failed();
                    lines.Add(new(line.Id, line.UnitId, line.InstructedQuantity.Value, line.ShippedQuantity.Value,
                        line.DeliveredQuantity.Value, source, line.ApprovedPlanExcessQuantity.Value));
                }
                var ownIds = own.Select(x => x.Id).ToHashSet();
                var shipments = instruction.Shares.Where(x => ownIds.Contains(x.LineId) && x.Kind == LoadingShareKind.Shipped)
                    .Select(x => new LogisticsSubcontractDeliveryShipmentV1(x.ShareId, x.LineId,
                        x.Quantity.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), x.RecordedAtUtc)).ToArray();
                if (shipments.Length > Bound || result.Sum(x => x.Shipments.Count) + shipments.Length > Bound) return Failed();
                LogisticsSubcontractDeliveryNetWeightV1? weighing = null;
                if (instruction.Weighing is { } weight && instruction.Status != LoadingInstructionStatus.Cancelled)
                {
                    var read = await netWeight.GetAsync(companyId, id, weight.WeighingId, token); await Fence();
                    if (read.Status != LogisticsSubcontractDeliveryNetWeightStatusV1.Available || read.CompanyId != companyId
                        || read.LoadingInstructionId != id || read.WeighingId != weight.WeighingId
                        || read.Value is not { IsContractProcessingDelivery: true } value
                        || value.InstructionVersion != instruction.Version || value.WeighingRevision != instruction.WeighingRevision) return Failed();
                    weighing = value;
                }
                if (shipments.Length > 0 && weighing is null) return Failed();
                result.Add(new(id, instruction.Version, instruction.Status == LoadingInstructionStatus.Cancelled,
                    instruction.Weighing?.WeighingId, weighing, lines, shipments));
            }
            var repeatedKeys = await Discover();
            if (!keys.SequenceEqual(repeatedKeys)) return Failed();
            await Fence();
            return new(companyId, salesOrderId, true, result,
                "logistics.subcontract-order-delivery.v1:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result)))));
        }
        catch (OperationCanceledException) { throw; }
        catch { await Fence(); return Failed(); }
        async Task<Guid[]> Discover()
        {
            await Fence();
            var ids = await db.Lines.AsNoTracking().Where(x => x.CompanyId == companyId && x.SourceOrderId == salesOrderId)
                .Select(x => x.InstructionId).Distinct().Order().Take(Bound + 1).ToArrayAsync(token);
            await Fence(); return ids;
        }
        async Task Fence()
        {
            token.ThrowIfCancellationRequested();
            if (!company.IsResolved || company.CompanyId != companyId) throw new UnauthorizedAccessException();
            await freshConsumerFence(token);
            if (!company.IsResolved || company.CompanyId != companyId) throw new UnauthorizedAccessException();
        }
    }
}
