using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>Price-free reverse lookup from actual Sales rows and immutable finalization outbox evidence.</summary>
public sealed class SalesFinalizedShipmentClaimReadPortV1(SalesDbContext db, ICompanyContext company)
    : ISalesFinalizedShipmentClaimReadPortV1
{
    public const int MaximumShipmentIds = 128;
    private const int MaximumHistoryRows = 2048;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<SalesShipmentClaimBatchV1> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> shipmentIds,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        SalesShipmentClaimBatchV1 Unavailable(string reason) => new SalesShipmentClaimBatchV1(companyId, SalesShipmentClaimReadOutcomeV1.Unavailable, [], null, reason);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Current(companyId) || freshConsumerFence is null) return Unavailable("consumer_context_unavailable");
        if (shipmentIds is null || shipmentIds.Count > MaximumShipmentIds || shipmentIds.Any(id => id == Guid.Empty)
            || shipmentIds.Distinct().Count() != shipmentIds.Count) return Unavailable("shipment_request_invalid");
        var ids = shipmentIds.Order().ToArray();
        try
        {
            await Fence();
            var first = await ReadSnapshot();
            var second = await ReadSnapshot();
            if (Encode(first) != Encode(second)) return Unavailable("shipment_claim_changed");
            var result = Project(companyId, ids, first);
            await Fence();
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Unavailable("shipment_claim_source_unavailable"); }

        async Task Fence()
        {
            if (!Current(companyId)) throw new UnauthorizedAccessException();
            await freshConsumerFence(cancellationToken);
            if (!Current(companyId)) throw new UnauthorizedAccessException();
        }
        async Task<T[]> Rows<T>(IQueryable<T> query)
        {
            await Fence();
            var rows = await query.Take(MaximumHistoryRows + 1).ToArrayAsync(cancellationToken);
            await Fence();
            if (rows.Length > MaximumHistoryRows) throw new InvalidOperationException("Shipment claim evidence exceeds its bounded read.");
            return rows;
        }
        async Task<Snapshot> ReadSnapshot()
        {
            if (ids.Length == 0) return new Snapshot([], [], [], []);
            var lines = await Rows(db.InvoiceLines.AsNoTracking().Where(x => x.CompanyId == companyId && ids.Contains(x.ShipmentId))
                .OrderBy(x => x.InvoiceId).ThenBy(x => x.LineNo)
                .Select(x => new Line(x.CompanyId, x.InvoiceId, x.LineId, x.LineNo, x.ShipmentId, x.DispatchNoteId, x.NoteLineNo,
                    x.SalesOrderId, x.SalesOrderLineId, x.PriceBasisRevision, x.ProductId, x.UnitId, x.QuantityText, x.ReleasedAtUtc)));
            var invoiceIds = lines.Select(x => x.InvoiceId).Distinct().ToArray();
            if (invoiceIds.Length == 0) return new Snapshot(lines, [], [], []);
            var headers = await Rows(db.Invoices.AsNoTracking().Where(x => x.CompanyId == companyId && invoiceIds.Contains(x.Id))
                .OrderBy(x => x.Id).Select(x => new Header(x.CompanyId, x.Id, x.Version, x.State, x.CancelledBy, x.CancelledAtUtc, x.CancellationReason)));
            var finalizations = await Rows(db.InvoiceFinalizations.AsNoTracking().Where(x => x.CompanyId == companyId && invoiceIds.Contains(x.InvoiceId))
                .OrderBy(x => x.InvoiceId).Select(x => new Finalization(x.CompanyId, x.InvoiceId, x.AdmissionId, x.FinalizedVersion,
                    x.FinalizedBy, x.FinalizedAtUtc, x.CorrelationId)));
            var events = await Rows(db.OutboxMessages.AsNoTracking().Where(x => x.CompanyId == companyId && invoiceIds.Contains(x.AggregateId)
                    && x.EventType == SalesStore.InvoiceFinalizedEventType)
                .OrderBy(x => x.AggregateId).ThenBy(x => x.Id).Select(x => new Event(x.CompanyId, x.Id, x.AggregateId,
                    x.AggregateType, x.AggregateVersion, x.SchemaVersion, x.Sequence, x.PayloadJson, x.OccurredAtUtc, x.CorrelationId)));
            var cancellations = await Rows(db.OfficialInvoiceCancellations.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Kind == 1 && invoiceIds.Contains(x.InvoiceId)).OrderBy(x => x.InvoiceId)
                .Select(x => new Cancellation(x.CompanyId, x.CancellationId, x.InvoiceId, x.InvoiceVersion, x.FinalizedVersion,
                    x.AdmissionId, x.ActorId, x.RecordedAtUtc, x.CorrelationId, x.ViewJson)));
            var cancellationEvents = await Rows(db.OutboxMessages.AsNoTracking().Where(x => x.CompanyId == companyId
                    && invoiceIds.Contains(x.AggregateId) && x.EventType == SalesOfficialInvoiceCancellationCommandsV1.EventType)
                .OrderBy(x => x.AggregateId).ThenBy(x => x.Id).Select(x => new Event(x.CompanyId, x.Id, x.AggregateId,
                    x.AggregateType, x.AggregateVersion, x.SchemaVersion, x.Sequence, x.PayloadJson, x.OccurredAtUtc, x.CorrelationId)));
            return new Snapshot(lines, headers, finalizations, events, cancellations, cancellationEvents);
        }
    }

    private bool Current(Guid id) => id != Guid.Empty && company.IsResolved && company.CompanyId == id;

    // Shared pure projection allows source-contract adversarial tests without substituting a fake database provider.
    internal static SalesShipmentClaimBatchV1 Project(Guid companyId, IReadOnlyList<Guid> requested, Snapshot snapshot)
    {
        var items = new List<SalesShipmentClaimReadV1>();
        foreach (var shipmentId in requested)
        {
            try { items.Add(ProjectShipment(companyId, shipmentId, snapshot)); }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or FormatException)
            { items.Add(new SalesShipmentClaimReadV1(shipmentId, SalesShipmentClaimReadOutcomeV1.Unavailable, null, [], null, "shipment_claim_proof_invalid")); }
        }
        return new SalesShipmentClaimBatchV1(companyId, SalesShipmentClaimReadOutcomeV1.Available, items, Sha(Encode(new { companyId, items })));
    }

    private static SalesShipmentClaimReadV1 ProjectShipment(Guid companyId, Guid shipmentId, Snapshot snapshot)
    {
        var lines = snapshot.Lines.Where(x => x.ShipmentId == shipmentId).OrderBy(x => x.InvoiceId).ThenBy(x => x.LineNo).ToArray();
        if (lines.Any(x => x.CompanyId != companyId || x.InvoiceId == Guid.Empty || x.LineId == Guid.Empty || x.LineNo < 1)
            || lines.Select(x => (x.InvoiceId, x.LineNo)).Distinct().Count() != lines.Length
            || lines.Select(x => x.InvoiceId).Distinct().Count() != lines.Length
            || lines.Count(x => x.ReleasedAtUtc is null) > 1) throw Invalid();
        var lifecycles = new List<SalesShipmentInvoiceLifecycleV1>();
        var historical = new List<SalesHistoricalFinalizedShipmentClaimV1>();
        SalesFinalizedShipmentClaimV1? claim = null;
        foreach (var line in lines)
        {
            var header = snapshot.Headers.Single(x => x.InvoiceId == line.InvoiceId);
            if (header.CompanyId != companyId || header.Version < 1 || !Enum.IsDefined((SalesInvoiceState)header.State)) throw Invalid();
            var state = (SalesInvoiceState)header.State;
            var released = state is SalesInvoiceState.Cancelled or SalesInvoiceState.Rejected or SalesInvoiceState.OfficiallyCancelled;
            if (released != line.ReleasedAtUtc.HasValue || line.ReleasedAtUtc is { Offset: var offset } && offset != TimeSpan.Zero) throw Invalid();
            if (state is SalesInvoiceState.Cancelled or SalesInvoiceState.OfficiallyCancelled)
            {
                if (string.IsNullOrWhiteSpace(header.CancelledBy) || header.CancelledAtUtc is not { Offset: var utc } || utc != TimeSpan.Zero
                    || header.CancelledAtUtc.Value == default || line.ReleasedAtUtc != header.CancelledAtUtc) throw Invalid();
            }
            else if (header.CancelledBy is not null || header.CancelledAtUtc is not null || header.CancellationReason is not null) throw Invalid();
            var lifecycle = new SalesShipmentInvoiceLifecycleV1(header.InvoiceId, header.Version, state.ToString(),
                line.ReleasedAtUtc, header.CancelledBy, header.CancelledAtUtc);
            lifecycles.Add(lifecycle);
            var finals = snapshot.Finalizations.Where(x => x.InvoiceId == line.InvoiceId).ToArray();
            var events = snapshot.Events.Where(x => x.InvoiceId == line.InvoiceId).ToArray();
            var cancellations = (snapshot.Cancellations ?? []).Where(x => x.InvoiceId == line.InvoiceId).ToArray();
            var cancellationEvents = (snapshot.CancellationEvents ?? []).Where(x => x.InvoiceId == line.InvoiceId).ToArray();
            if (state != SalesInvoiceState.OfficiallyCancelled && (cancellations.Length != 0 || cancellationEvents.Length != 0)) throw Invalid();
            if (state is not (SalesInvoiceState.Finalized or SalesInvoiceState.OfficiallyCancelled))
            {
                // Unfinalized draft cancellation has no finalization history. Official cancellation is validated below.
                if (finals.Length != 0 || events.Length != 0) throw Invalid();
                continue;
            }
            if (state == SalesInvoiceState.Finalized && claim is not null || finals is not [var finalized] || events is not [var envelope]
                || finalized.CompanyId != companyId || finalized.AdmissionId == Guid.Empty || finalized.Version < 2
                || finalized.Version != (state == SalesInvoiceState.OfficiallyCancelled ? header.Version - 1 : header.Version) || string.IsNullOrWhiteSpace(finalized.FinalizedBy)
                || finalized.FinalizedAtUtc == default || finalized.FinalizedAtUtc.Offset != TimeSpan.Zero
                || string.IsNullOrWhiteSpace(finalized.CorrelationId)
                || envelope.CompanyId != companyId || envelope.Id == Guid.Empty || envelope.AggregateType != "Sales.Invoice"
                || envelope.Version != finalized.Version || envelope.SchemaVersion != 1 || envelope.Sequence != 1
                || envelope.OccurredAtUtc != finalized.FinalizedAtUtc || envelope.CorrelationId != finalized.CorrelationId) throw Invalid();
            using var document = JsonDocument.Parse(envelope.PayloadJson);
            RequireUniqueProperties(document.RootElement);
            var body = JsonSerializer.Deserialize<Payload>(envelope.PayloadJson, Json);
            if (body is null || body.CompanyId != companyId || body.InvoiceId != header.InvoiceId
                || body.Version != finalized.Version || body.AdmissionId != finalized.AdmissionId || body.FinalizedBy != finalized.FinalizedBy
                || body.Lines is not { Length: > 0 and <= MaximumHistoryRows }
                || body.Lines.Any(x => x is null || x.LineId == Guid.Empty || x.LineNo < 1
                    || x.ShipmentSource is null || x.ShipmentSource.ShipmentId == Guid.Empty)
                || body.Lines.Select(x => x.LineId).Distinct().Count() != body.Lines.Length
                || body.Lines.Select(x => x.LineNo).Distinct().Count() != body.Lines.Length
                || body.Lines.Select(x => x.ShipmentSource!.ShipmentId).Distinct().Count() != body.Lines.Length) throw Invalid();
            var sourceLine = body.Lines.Single(x => x.LineId == line.LineId && x.LineNo == line.LineNo);
            var expected = new SalesInvoiceShipmentSourceV1(line.ShipmentId, line.DispatchNoteId, line.NoteLineNo,
                line.SalesOrderId, line.SalesOrderLineId, line.PriceBasisRevision, line.ProductId, line.UnitId, line.QuantityText);
            if (sourceLine.ShipmentSource != expected || sourceLine.ProductId != line.ProductId || sourceLine.UnitId != line.UnitId
                || expected.DispatchNoteId == Guid.Empty || expected.DispatchNoteLineNo < 1 || expected.SalesOrderId == Guid.Empty
                || expected.SalesOrderLineId == Guid.Empty || expected.PriceBasisRevision < 0 || expected.ProductId == Guid.Empty
                || expected.UnitId == Guid.Empty || !decimal.TryParse(expected.InvoicedQuantity,
                    System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var quantity)
                || quantity <= 0 || quantity != sourceLine.Quantity) throw Invalid();
            if (state == SalesInvoiceState.OfficiallyCancelled)
            {
                if (cancellations is not [var cancelled] || cancellationEvents is not [var cancelledEvent]
                    || cancelled.CompanyId != companyId || cancelled.InvoiceVersion != header.Version
                    || cancelled.FinalizedVersion != finalized.Version || cancelled.AdmissionId != finalized.AdmissionId
                    || cancelled.ActorId != header.CancelledBy || cancelled.RecordedAtUtc != header.CancelledAtUtc
                    || cancelledEvent.CompanyId != companyId || cancelledEvent.Id != cancelled.Id
                    || cancelledEvent.AggregateType != "Sales.Invoice" || cancelledEvent.Version != header.Version
                    || cancelledEvent.SchemaVersion != 1 || cancelledEvent.Sequence != 1
                    || cancelledEvent.OccurredAtUtc != cancelled.RecordedAtUtc || cancelledEvent.CorrelationId != cancelled.CorrelationId) throw Invalid();
                var view = JsonSerializer.Deserialize<SalesOfficialInvoiceCancellationViewV1>(cancelled.ViewJson, Json) ?? throw Invalid();
                SalesStore.ValidateOfficialView(view, companyId, cancelled.Id);
                using var cancellationDocument = JsonDocument.Parse(cancelledEvent.PayloadJson);
                RequireUniqueProperties(cancellationDocument.RootElement);
                var bodyCancellation = JsonSerializer.Deserialize<CancellationPayload>(cancelledEvent.PayloadJson, Json);
                if (view.InvoiceId != header.InvoiceId || view.Kind != SalesOfficialInvoiceKindV1.Normal
                    || view.InvoiceVersion != header.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    || view.FinalizedVersion != finalized.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    || view.AdmissionId != finalized.AdmissionId || view.RecordedBy != header.CancelledBy
                    || view.RecordedAtUtc != header.CancelledAtUtc || view.Reason != header.CancellationReason
                    || bodyCancellation is null || bodyCancellation.CompanyId != companyId || bodyCancellation.CancellationId != cancelled.Id
                    || bodyCancellation.InvoiceId != header.InvoiceId || bodyCancellation.AdmissionId != finalized.AdmissionId
                    || bodyCancellation.FinalizedVersion != finalized.Version || bodyCancellation.CancelledBy != Guid.Parse(view.RecordedBy)
                    || bodyCancellation.CancelledAtUtc != view.RecordedAtUtc || bodyCancellation.Reason != view.Reason) throw Invalid();
                historical.Add(new SalesHistoricalFinalizedShipmentClaimV1(new SalesFinalizedShipmentClaimV1(companyId, header.InvoiceId, line.LineId, line.LineNo, finalized.AdmissionId,
                    finalized.Version, expected, envelope.Id, Sha(envelope.PayloadJson), finalized.FinalizedAtUtc, lifecycle),
                    cancelled.Id, cancelledEvent.Id, Sha(cancelledEvent.PayloadJson), bodyCancellation.CancelledBy,
                    bodyCancellation.CancelledAtUtc, bodyCancellation.Reason));
                continue; // Historical evidence is never returned as an active quantity/economic claim.
            }
            claim = new SalesFinalizedShipmentClaimV1(companyId, header.InvoiceId, line.LineId, line.LineNo, finalized.AdmissionId, finalized.Version,
                expected, envelope.Id, Sha(envelope.PayloadJson), finalized.FinalizedAtUtc, lifecycle);
        }
        var sourceVersion = historical.Count == 0 ? Sha(Encode(new { companyId, shipmentId, claim, lifecycles }))
            : Sha(Encode(new { companyId, shipmentId, claim, lifecycles, historical }));
        return new SalesShipmentClaimReadV1(shipmentId, claim is null ? SalesShipmentClaimReadOutcomeV1.NotInvoiced : SalesShipmentClaimReadOutcomeV1.Available,
            claim, lifecycles, sourceVersion, HistoricalFinalizedClaims: historical.Count == 0 ? null : historical);
    }

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw Invalid(); RequireUniqueProperties(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RequireUniqueProperties(child);
    }
    private static InvalidOperationException Invalid() => new InvalidOperationException("Sales shipment claim evidence is inconsistent.");
    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static string Sha(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record Payload(Guid CompanyId, Guid InvoiceId, long Version, Guid AdmissionId, string FinalizedBy, PayloadLine[] Lines);
    private sealed record PayloadLine(Guid LineId, int LineNo, Guid ProductId, Guid UnitId, decimal Quantity, SalesInvoiceShipmentSourceV1? ShipmentSource);
    internal sealed record Line(Guid CompanyId, Guid InvoiceId, Guid LineId, int LineNo, Guid ShipmentId, Guid DispatchNoteId,
        int NoteLineNo, Guid SalesOrderId, Guid SalesOrderLineId, int PriceBasisRevision, Guid ProductId, Guid UnitId,
        string QuantityText, DateTimeOffset? ReleasedAtUtc);
    internal sealed record Header(Guid CompanyId, Guid InvoiceId, long Version, int State,
        string? CancelledBy, DateTimeOffset? CancelledAtUtc, string? CancellationReason);
    internal sealed record Finalization(Guid CompanyId, Guid InvoiceId, Guid AdmissionId, long Version, string FinalizedBy,
        DateTimeOffset FinalizedAtUtc, string CorrelationId);
    internal sealed record Event(Guid CompanyId, Guid Id, Guid InvoiceId, string AggregateType, long Version,
        int SchemaVersion, int Sequence, string PayloadJson, DateTimeOffset OccurredAtUtc, string CorrelationId);
    internal sealed record Cancellation(Guid CompanyId, Guid Id, Guid InvoiceId, long InvoiceVersion, long FinalizedVersion,
        Guid AdmissionId, string ActorId, DateTimeOffset RecordedAtUtc, string CorrelationId, string ViewJson);
    private sealed record CancellationPayload(Guid CompanyId, Guid CancellationId, Guid InvoiceId, Guid AdmissionId,
        long FinalizedVersion, Guid CancelledBy, DateTimeOffset CancelledAtUtc, string Reason);
    internal sealed record Snapshot(Line[] Lines, Header[] Headers, Finalization[] Finalizations, Event[] Events,
        Cancellation[]? Cancellations = null, Event[]? CancellationEvents = null);
}
