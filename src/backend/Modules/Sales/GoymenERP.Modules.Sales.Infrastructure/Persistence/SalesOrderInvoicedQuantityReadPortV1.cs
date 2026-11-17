using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>Reads only Sales-owned invoice evidence. No amount, external document query, stock or invoice mutation.</summary>
public sealed class SalesOrderInvoicedQuantityReadPortV1(SalesDbContext db, ICompanyContext company,
    ISalesSubcontractServiceInvoiceStore subcontractInvoices, ISalesFinalizedShipmentClaimReadPortV1? normalClaims = null)
    : ISalesOrderInvoicedQuantityReadPortV1
{
    private const int MaximumEvidenceLines = 10000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SalesOrderInvoicedQuantityReadV1> ReadAsync(Guid companyId, Guid salesOrderId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        SalesOrderInvoicedQuantityReadV1 Unavailable() => new(companyId, salesOrderId,
            SalesOrderInvoicedQuantityReadOutcomeV1.Unavailable, [], null);
        if (companyId == Guid.Empty || salesOrderId == Guid.Empty || freshConsumerFence is null) return Unavailable();
        try
        {
            var before = await DiscoverAsync();
            if (before.Normal.Length > 0 && normalClaims is null) return Unavailable();
            var lines = new List<SalesOrderInvoicedQuantityLineV1>();
            var versions = new List<string>();
            foreach (var ids in before.Normal.Select(row => row.ShipmentId).Distinct().Order().Chunk(SalesFinalizedShipmentClaimReadPortV1.MaximumShipmentIds))
            {
                await Fence();
                var answer = await normalClaims!.ReadAsync(companyId, ids, Fence, cancellationToken);
                await Fence();
                if (answer.CompanyId != companyId || answer.Outcome != SalesShipmentClaimReadOutcomeV1.Available
                    || answer.Items.Count != ids.Length || string.IsNullOrWhiteSpace(answer.SourceVersion)
                    || answer.Items.Select(row => row.ShipmentId).Distinct().Count() != ids.Length
                    || answer.Items.Any(row => !ids.Contains(row.ShipmentId))) return Unavailable();
                versions.Add(answer.SourceVersion);
                foreach (var item in answer.Items)
                {
                    // Only the current active claim is invoiced. Historical finalized evidence retained for cost
                    // attribution after official cancellation never restores an active billed quantity.
                    if (item.Outcome == SalesShipmentClaimReadOutcomeV1.NotInvoiced && item.Claim is null) continue;
                    if (item.Outcome != SalesShipmentClaimReadOutcomeV1.Available || item.Claim is not { } claim
                        || claim.CompanyId != companyId || claim.ShipmentSource.SalesOrderId != salesOrderId
                        || claim.ShipmentSource.ShipmentId != item.ShipmentId || claim.Lifecycle.State != "Finalized"
                        || claim.Lifecycle.ReleasedAtUtc is not null || claim.FinalizedVersion < 1
                        || claim.AdmissionId == Guid.Empty || string.IsNullOrWhiteSpace(item.SourceVersion)) return Unavailable();
                    var source = claim.ShipmentSource;
                    if (!decimal.TryParse(source.InvoicedQuantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)
                        || quantity <= 0m) return Unavailable();
                    lines.Add(new(claim.InvoiceId, claim.InvoiceLineId, source.SalesOrderLineId, source.UnitId,
                        quantity, "Normal", item.SourceVersion));
                }
            }
            foreach (var header in before.Subcontract)
            {
                await Fence();
                var invoice = await subcontractInvoices.FindSubcontractServiceInvoiceAsync(header.Id, cancellationToken);
                await Fence();
                if (invoice is null || invoice.CompanyId != companyId || invoice.Id != header.Id
                    || invoice.Source.SalesOrderId != salesOrderId || invoice.Version != header.Version
                    || (int)invoice.State != header.State || invoice.Lines.Count > MaximumEvidenceLines) return Unavailable();
                if (invoice.State != SalesSubcontractServiceInvoiceState.Finalized) continue;
                if (invoice.Finalization is not { FinalizedVersion: > 0 } finalization || finalization.AdmissionId == Guid.Empty
                    || invoice.Match is null || invoice.Source.LoadingInstructionId == Guid.Empty || invoice.Source.WeighingId == Guid.Empty
                    || invoice.Source.WeighingRevision < 1 || invoice.Source.InstructionVersion < 1) return Unavailable();
                var events = await db.OutboxMessages.AsNoTracking().Where(row => row.CompanyId == companyId
                        && row.AggregateId == invoice.Id && row.EventType == SalesStore.InvoiceFinalizedEventType
                        && row.AggregateType == "Sales.SubcontractServiceInvoice")
                    .Take(2).ToArrayAsync(cancellationToken);
                await Fence();
                if (events is not [var finalizedEvent] || finalizedEvent.Id == Guid.Empty || finalizedEvent.SchemaVersion != 1
                    || finalizedEvent.Sequence != 1 || finalizedEvent.AggregateVersion != finalization.FinalizedVersion
                    || finalizedEvent.OccurredAtUtc != finalization.FinalizedAtUtc
                    || finalizedEvent.PayloadJson != SalesStore.FinalizedSubcontractPayload(invoice, finalization)) return Unavailable();
                var sourceVersion = Sha(JsonSerializer.Serialize(new { companyId, invoice.Id, invoice.Version, invoice.State,
                    invoice.Source, finalization.AdmissionId, finalization.FinalizedVersion, finalization.FinalizedAtUtc,
                    EventId = finalizedEvent.Id, PayloadSha256 = Sha(finalizedEvent.PayloadJson),
                    Lines = invoice.Lines.Select(line => new { line.Id, line.Input.SalesOrderLineId, line.Input.UnitId,
                        line.Input.Quantity, line.Input.QuantityText }).ToArray() }, Json));
                versions.Add(sourceVersion);
                foreach (var line in invoice.Lines)
                    lines.Add(new(invoice.Id, line.Id, line.Input.SalesOrderLineId, line.Input.UnitId,
                        line.Input.Quantity, "Subcontract", sourceVersion));
                if (lines.Count > MaximumEvidenceLines) return Unavailable();
            }
            var after = await DiscoverAsync();
            if (JsonSerializer.Serialize(before, Json) != JsonSerializer.Serialize(after, Json)
                || lines.Any(line => line.InvoiceId == Guid.Empty || line.InvoiceLineId == Guid.Empty
                    || line.SalesOrderLineId == Guid.Empty || line.UnitId == Guid.Empty || line.Quantity <= 0m)
                || lines.Select(line => (line.InvoiceKind, line.InvoiceId, line.InvoiceLineId)).Distinct().Count() != lines.Count)
                return Unavailable();
            await Fence();
            var version = ISalesOrderInvoicedQuantityReadPortV1.SourceVersionPrefix
                + Sha(JsonSerializer.Serialize(new { companyId, salesOrderId, after, versions, lines }, Json));
            return new(companyId, salesOrderId, SalesOrderInvoicedQuantityReadOutcomeV1.Available,
                lines.AsReadOnly(), version);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // A failing source cannot release a quantity after losing the consumer's original authority.
            await Fence();
            return Unavailable();
        }

        async Task Fence(CancellationToken token = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            if (!company.IsResolved || company.CompanyId != companyId) throw new UnauthorizedAccessException();
            await freshConsumerFence(cancellationToken);
            if (!company.IsResolved || company.CompanyId != companyId) throw new UnauthorizedAccessException();
        }
        async Task<Discovery> DiscoverAsync()
        {
            await Fence();
            var normal = await (from line in db.InvoiceLines.AsNoTracking()
                                join header in db.Invoices.AsNoTracking() on new { line.CompanyId, Id = line.InvoiceId }
                                    equals new { header.CompanyId, header.Id }
                                where line.CompanyId == companyId && line.SalesOrderId == salesOrderId
                                orderby line.InvoiceId, line.LineNo
                                select new NormalLine(line.InvoiceId, line.LineId, line.ShipmentId,
                                    line.ReleasedAtUtc, header.State, header.Version)).Take(MaximumEvidenceLines + 1).ToArrayAsync(cancellationToken);
            await Fence();
            var subcontract = await db.SubcontractServiceInvoices.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.SalesOrderId == salesOrderId)
                .OrderBy(row => row.Id).Select(row => new SubcontractHeader(row.Id, row.Version, row.State))
                .Take(MaximumEvidenceLines + 1).ToArrayAsync(cancellationToken);
            await Fence();
            if (normal.Length > MaximumEvidenceLines || subcontract.Length > MaximumEvidenceLines)
                throw new InvalidOperationException("Invoice quantity evidence exceeds its bounded read.");
            return new(normal, subcontract);
        }
    }

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private sealed record NormalLine(Guid InvoiceId, Guid LineId, Guid ShipmentId, DateTimeOffset? ReleasedAtUtc, int State, long Version);
    private sealed record SubcontractHeader(Guid Id, long Version, int State);
    private sealed record Discovery(NormalLine[] Normal, SubcontractHeader[] Subcontract);
}
