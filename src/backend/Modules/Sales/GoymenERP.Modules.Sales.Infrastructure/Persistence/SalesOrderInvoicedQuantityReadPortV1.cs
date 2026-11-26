using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<SalesOrderInvoicedQuantityReadV1> ReadAsync(Guid companyId, Guid salesOrderId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        SalesOrderInvoicedQuantityReadV1 Unavailable() => new SalesOrderInvoicedQuantityReadV1(companyId, salesOrderId,
            SalesOrderInvoicedQuantityReadOutcomeV1.Unavailable, [], null);
        if (companyId == Guid.Empty || salesOrderId == Guid.Empty || freshConsumerFence is null) return Unavailable();
        try
        {
            var before = await DiscoverAsync();
            if (before.Normal.Length > 0 && normalClaims is null) return Unavailable();
            var lines = new List<SalesOrderInvoicedQuantityLineV1>();
            var versions = new List<string>();
            var cancelled = new Dictionary<Guid, SalesOrderCancelledInvoiceProofV1>();
            var normalHistoricalLines = new HashSet<(Guid InvoiceId, Guid LineId)>();
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
                    if (item.Outcome is not (SalesShipmentClaimReadOutcomeV1.Available or SalesShipmentClaimReadOutcomeV1.NotInvoiced)) return Unavailable();
                    foreach (var historical in item.HistoricalFinalizedClaims ?? [])
                    {
                        var f = historical.Finalization;
                        if (f.CompanyId != companyId || f.ShipmentSource.SalesOrderId != salesOrderId
                            || f.ShipmentSource.ShipmentId != item.ShipmentId || f.Lifecycle.State != "OfficiallyCancelled"
                            || f.Lifecycle.ReleasedAtUtc != historical.CancelledAtUtc || f.Lifecycle.CancelledAtUtc != historical.CancelledAtUtc
                            || !before.Normal.Any(x => x.InvoiceId == f.InvoiceId && x.LineId == f.InvoiceLineId
                                && x.State == (int)SalesInvoiceState.OfficiallyCancelled && x.Version == f.Lifecycle.Version)
                            || !normalHistoricalLines.Add((f.InvoiceId, f.InvoiceLineId))) return Unavailable();
                        var proof = new SalesOrderCancelledInvoiceProofV1(companyId, f.InvoiceId, "Normal", f.AdmissionId,
                            f.FinalizedVersion, f.Lifecycle.Version, f.FinalizedEventId, f.FinalizedPayloadSha256,
                            historical.CancellationId, historical.CancellationEventId, historical.CancellationPayloadSha256, historical.CancelledAtUtc);
                        if (cancelled.TryGetValue(f.InvoiceId, out var prior) && prior != proof) return Unavailable();
                        cancelled[f.InvoiceId] = proof;
                    }
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
                    lines.Add(new SalesOrderInvoicedQuantityLineV1(claim.InvoiceId, claim.InvoiceLineId, source.SalesOrderLineId, source.UnitId,
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
                if (invoice.State is not (SalesSubcontractServiceInvoiceState.Finalized or SalesSubcontractServiceInvoiceState.OfficiallyCancelled)) continue;
                var officiallyCancelled = invoice.State == SalesSubcontractServiceInvoiceState.OfficiallyCancelled;
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
                    || finalizedEvent.PayloadJson != OriginalSubcontractPayload(invoice, finalization)) return Unavailable();
                if (officiallyCancelled)
                {
                    if (subcontractInvoices is not ISalesOfficialInvoiceCancellationStoreV1 cancellationOwner) return Unavailable();
                    await Fence();
                    var retained = await cancellationOwner.ReadForInvoiceAsync(SalesOfficialInvoiceKindV1.Subcontract, invoice.Id, Fence, cancellationToken);
                    await Fence();
                    if (retained?.Cancellation is not { } view || view.CompanyId != companyId || view.InvoiceId != invoice.Id
                        || view.Kind != SalesOfficialInvoiceKindV1.Subcontract || view.InvoiceVersion != invoice.Version.ToString(CultureInfo.InvariantCulture)
                        || view.FinalizedVersion != finalization.FinalizedVersion.ToString(CultureInfo.InvariantCulture)
                        || view.AdmissionId != finalization.AdmissionId || retained.Admission.AdmissionId != finalization.AdmissionId
                        || retained.Admission.InvoiceId != invoice.Id || retained.Admission.InvoiceVersion != view.FinalizedVersion) return Unavailable();
                    var cancellationEvents = await db.OutboxMessages.AsNoTracking().Where(row => row.CompanyId == companyId
                            && row.AggregateId == invoice.Id && row.EventType == SalesOfficialInvoiceCancellationCommandsV1.EventType)
                        .Take(2).ToArrayAsync(cancellationToken);
                    await Fence();
                    var expectedCancellationPayload = JsonSerializer.Serialize(new { companyId, cancellationId = view.CancellationId,
                        invoiceId = invoice.Id, admissionId = finalization.AdmissionId, finalizedVersion = finalization.FinalizedVersion,
                        cancelledBy = Guid.Parse(view.RecordedBy), cancelledAtUtc = view.RecordedAtUtc, reason = view.Reason }, Json);
                    if (cancellationEvents is not [var cancellationEvent] || cancellationEvent.Id != view.CancellationId
                        || cancellationEvent.AggregateType != "Sales.SubcontractServiceInvoice" || cancellationEvent.SchemaVersion != 1
                        || cancellationEvent.Sequence != 1 || cancellationEvent.AggregateVersion != invoice.Version
                        || cancellationEvent.OccurredAtUtc != view.RecordedAtUtc || cancellationEvent.PayloadJson != expectedCancellationPayload) return Unavailable();
                    cancelled.Add(invoice.Id, new(companyId, invoice.Id, "Subcontract", finalization.AdmissionId, finalization.FinalizedVersion,
                        invoice.Version, finalizedEvent.Id, Sha(finalizedEvent.PayloadJson), view.CancellationId, cancellationEvent.Id,
                        Sha(cancellationEvent.PayloadJson), view.RecordedAtUtc));
                    versions.Add(Sha(JsonSerializer.Serialize(new { invoice.Id, invoice.Version, view.CancellationId,
                        FinalizedPayload = Sha(finalizedEvent.PayloadJson), CancellationPayload = Sha(cancellationEvent.PayloadJson) }, Json)));
                    continue; // Historical invoice never restores active billed quantity.
                }
                var sourceVersion = Sha(JsonSerializer.Serialize(new { companyId, invoice.Id, invoice.Version, invoice.State,
                    invoice.Source, finalization.AdmissionId, finalization.FinalizedVersion, finalization.FinalizedAtUtc,
                    EventId = finalizedEvent.Id, PayloadSha256 = Sha(finalizedEvent.PayloadJson),
                    Lines = invoice.Lines.Select(line => new { line.Id, line.Input.SalesOrderLineId, line.Input.UnitId,
                        line.Input.Quantity, line.Input.QuantityText }).ToArray() }, Json));
                versions.Add(sourceVersion);
                foreach (var line in invoice.Lines)
                    lines.Add(new SalesOrderInvoicedQuantityLineV1(invoice.Id, line.Id, line.Input.SalesOrderLineId, line.Input.UnitId,
                        line.Input.Quantity, "Subcontract", sourceVersion));
                if (lines.Count > MaximumEvidenceLines) return Unavailable();
            }
            if (!normalHistoricalLines.Order().SequenceEqual(before.Normal.Where(x => x.State == (int)SalesInvoiceState.OfficiallyCancelled)
                    .Select(x => (x.InvoiceId, x.LineId)).Order())) return Unavailable();
            var expectedCancelled = before.Normal.Where(x => x.State == (int)SalesInvoiceState.OfficiallyCancelled).Select(x => x.InvoiceId)
                .Concat(before.Subcontract.Where(x => x.State == (int)SalesSubcontractServiceInvoiceState.OfficiallyCancelled).Select(x => x.Id)).Distinct().Order();
            if (!cancelled.Keys.Order().SequenceEqual(expectedCancelled)) return Unavailable();
            var cancelledProofs = cancelled.Values.OrderBy(x => x.InvoiceId).ToArray();
            var after = await DiscoverAsync();
            if (JsonSerializer.Serialize(before, Json) != JsonSerializer.Serialize(after, Json)
                || lines.Any(line => line.InvoiceId == Guid.Empty || line.InvoiceLineId == Guid.Empty
                    || line.SalesOrderLineId == Guid.Empty || line.UnitId == Guid.Empty || line.Quantity <= 0m)
                || lines.Select(line => (line.InvoiceKind, line.InvoiceId, line.InvoiceLineId)).Distinct().Count() != lines.Count)
                return Unavailable();
            await Fence();
            var version = ISalesOrderInvoicedQuantityReadPortV1.SourceVersionPrefix
                + Sha(JsonSerializer.Serialize(new { companyId, salesOrderId, after, versions, lines, cancelledProofs }, Json));
            return new SalesOrderInvoicedQuantityReadV1(companyId, salesOrderId, SalesOrderInvoicedQuantityReadOutcomeV1.Available,
                lines.AsReadOnly(), version,
                after.Normal.Any(x => x.State == (int)SalesInvoiceState.OfficiallyCancelled)
                || after.Subcontract.Any(x => x.State == (int)SalesSubcontractServiceInvoiceState.OfficiallyCancelled), cancelledProofs);
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
            return new Discovery(normal, subcontract);
        }
    }

    private static string OriginalSubcontractPayload(SalesSubcontractServiceInvoice invoice, SalesInvoiceFinalization finalization)
    {
        // The native writer emitted the original finalized version. Cancellation retains all other frozen facts.
        var payload = JsonNode.Parse(SalesStore.FinalizedSubcontractPayload(invoice, finalization))!;
        payload["version"] = finalization.FinalizedVersion;
        return payload.ToJsonString(Json);
    }

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private sealed record NormalLine(Guid InvoiceId, Guid LineId, Guid ShipmentId, DateTimeOffset? ReleasedAtUtc, int State, long Version);
    private sealed record SubcontractHeader(Guid Id, long Version, int State);
    private sealed record Discovery(NormalLine[] Normal, SubcontractHeader[] Subcontract);
}
