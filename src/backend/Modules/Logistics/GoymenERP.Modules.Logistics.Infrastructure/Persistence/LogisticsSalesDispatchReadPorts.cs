using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// MOD-06 / API-30 / API-31: Logistics answers the two Sales-declared dispatch reads over its OWN company-filtered tables
/// (dispatch notes, their lines, the instruction header for the customer and the shipment share for its recorded time). Every
/// table carries the Logistics company query filter and every join keeps the company, so another company's note is invisible.
/// Untracked; nothing is created, audited or versioned; no Sales table is read. The answers are facts only — no price, tax, rate
/// or due date.
/// <para>
/// Fail-closed outcomes: no resolved company → <see cref="SalesDispatchReadOutcomeV1.Denied"/> before any read; a broken or
/// foreign cursor → <see cref="SalesDispatchReadOutcomeV1.InvalidCursor"/>; a stored record that breaks the owner's own shape
/// (a line without its shipment share, an approved note without its number or approval time) →
/// <see cref="SalesDispatchReadOutcomeV1.Unavailable"/>. A database failure propagates; the Sales consumer maps it to 503.
/// </para>
/// </summary>
internal sealed class LogisticsSalesDispatchReadPorts(LogisticsDbContext dbContext, ICompanyContext companyContext)
    : ISalesDispatchNoteReadPortV1, ISalesInvoiceableShipmentReadPortV1
{
    private const int MaximumPageSize = 100;

    public async Task<SalesDispatchNotePageAnswerV1> ListSalesDispatchNotesAsync(SalesDispatchNotePageQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            return new SalesDispatchNotePageAnswerV1(SalesDispatchReadOutcomeV1.Denied, Guid.Empty, [], null);
        }

        var companyId = companyContext.CompanyId;
        if (query.PageSize is < 1 or > MaximumPageSize || query.State is { } invalid && !Enum.IsDefined(invalid))
        {
            throw new ArgumentOutOfRangeException(nameof(query));
        }

        var notes = from note in dbContext.DispatchNotes.AsNoTracking()
                    join instruction in dbContext.Instructions.AsNoTracking()
                        on new { note.CompanyId, Id = note.InstructionId } equals new { instruction.CompanyId, instruction.Id }
                    select new { note, instruction.CustomerPartyId, instruction.CompanyAccountId };
        if (query.State is { } state)
        {
            var status = (int)state;
            notes = notes.Where(row => row.note.Status == status);
        }

        if (query.Cursor is not null)
        {
            DateTimeOffset createdAtUtc;
            Guid lastId;
            try
            {
                (createdAtUtc, lastId) = LogisticsStore.ListCursor.Decode(query.Cursor, companyId);
            }
            catch (LogisticsStoreException exception) when (exception.Code == LogisticsStoreCodes.InvalidCursor)
            {
                return new SalesDispatchNotePageAnswerV1(SalesDispatchReadOutcomeV1.InvalidCursor, companyId, [], null);
            }

            notes = notes.Where(row => row.note.CreatedAtUtc < createdAtUtc
                || (row.note.CreatedAtUtc == createdAtUtc && row.note.DispatchNoteId.CompareTo(lastId) < 0));
        }

        var page = await notes.OrderByDescending(row => row.note.CreatedAtUtc).ThenByDescending(row => row.note.DispatchNoteId)
            .Take(query.PageSize + 1).ToListAsync(cancellationToken);
        var hasMore = page.Count > query.PageSize;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        if (page.Count == 0)
        {
            return new SalesDispatchNotePageAnswerV1(SalesDispatchReadOutcomeV1.Found, companyId, [], null);
        }

        var ids = page.Select(row => row.note.DispatchNoteId).ToArray();
        var lines = await (from line in dbContext.DispatchNoteLines.AsNoTracking()
                           join shipment in dbContext.Shares.AsNoTracking()
                               on new { line.CompanyId, line.InstructionId, ShareId = line.ShipmentId }
                               equals new { shipment.CompanyId, shipment.InstructionId, shipment.ShareId }
                               into shipments
                           from shipment in shipments.DefaultIfEmpty()
                           where ids.Contains(line.DispatchNoteId)
                           select new
                           {
                               line,
                               ShipmentKind = shipment == null ? (int?)null : shipment.Kind,
                               ShippedAtUtc = shipment == null ? (DateTimeOffset?)null : shipment.RecordedAtUtc,
                           }).ToListAsync(cancellationToken);

        var result = new List<SalesDispatchNoteV1>(page.Count);
        foreach (var row in page)
        {
            var noteLines = lines.Where(candidate => candidate.line.DispatchNoteId == row.note.DispatchNoteId
                    && candidate.line.InstructionId == row.note.InstructionId)
                .OrderBy(candidate => candidate.line.LineNo).ToArray();
            var noteState = (SalesDispatchNoteStateV1)row.note.Status;
            var number = row.note.SeriesCode is { } series && row.note.SequenceNo is { } sequence
                ? DispatchNoteNumber.Of(series, sequence).Text
                : null;
            if (noteLines.Length == 0 || !Enum.IsDefined(noteState)
                || noteLines.Any(candidate => candidate.ShipmentKind != (int)LoadingShareKind.Shipped || candidate.ShippedAtUtc is null
                    || candidate.line.Quantity <= 0m)
                || noteState == SalesDispatchNoteStateV1.Approved && (number is null || row.note.ApprovedAtUtc is null))
            {
                // The owner's own record breaks its shape: fail-closed, never a partial or guessed note.
                return new SalesDispatchNotePageAnswerV1(SalesDispatchReadOutcomeV1.Unavailable, companyId, [], null);
            }

            result.Add(new SalesDispatchNoteV1(row.note.DispatchNoteId, row.note.InstructionId, noteState, row.CustomerPartyId,
                row.CompanyAccountId, number, row.note.EDispatchNumber, row.note.CreatedAtUtc, row.note.ApprovedAtUtc,
                row.note.CancelledAtUtc,
                [.. noteLines.Select(candidate => new SalesDispatchShareV1(candidate.line.LineNo, candidate.line.SourceOrderId,
                    candidate.line.SourceOrderLineId, candidate.line.ShipmentId, candidate.ShippedAtUtc!.Value,
                    candidate.line.ProductId, candidate.line.UnitId, candidate.line.Quantity))]));
        }

        var last = page[^1].note;
        return new SalesDispatchNotePageAnswerV1(SalesDispatchReadOutcomeV1.Found, companyId, result,
            hasMore ? LogisticsStore.ListCursor.Encode(companyId, last.CreatedAtUtc, last.DispatchNoteId) : null);
    }

    public async Task<SalesInvoiceableShipmentAnswerV1> ReadInvoiceableShipmentsAsync(SalesInvoiceableShipmentQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            return new SalesInvoiceableShipmentAnswerV1(SalesDispatchReadOutcomeV1.Denied, Guid.Empty, [], false,
                SalesInvoicedStateSourceV1.NoSource);
        }

        var companyId = companyContext.CompanyId;
        if (query.Limit < 1 || query.CompanyAccountId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(query));
        }

        // AT-055: only an APPROVED note's lines that were never released are posted shares; one statement keeps the header, the
        // line and the shipment share on the same snapshot. Every set carries the company filter and every join the company.
        var shares = from note in dbContext.DispatchNotes.AsNoTracking()
                     join instruction in dbContext.Instructions.AsNoTracking()
                         on new { note.CompanyId, Id = note.InstructionId } equals new { instruction.CompanyId, instruction.Id }
                     join line in dbContext.DispatchNoteLines.AsNoTracking()
                         on new { note.CompanyId, note.InstructionId, note.DispatchNoteId }
                         equals new { line.CompanyId, line.InstructionId, line.DispatchNoteId }
                     join shipment in dbContext.Shares.AsNoTracking()
                         on new { line.CompanyId, line.InstructionId, ShareId = line.ShipmentId }
                         equals new { shipment.CompanyId, shipment.InstructionId, shipment.ShareId }
                         into shipments
                     from shipment in shipments.DefaultIfEmpty()
                     where note.Status == (int)DispatchNoteStatus.Approved && !line.Released
                     select new { note, instruction.CustomerPartyId, instruction.CompanyAccountId, line, shipment };
        if (query.CompanyAccountId is { } account)
        {
            shares = shares.Where(row => row.CompanyAccountId == account);
        }

        var rows = await shares
            .OrderBy(row => row.note.ApprovedAtUtc).ThenBy(row => row.note.DispatchNoteId).ThenBy(row => row.line.LineNo)
            .Select(row => new
            {
                row.note.DispatchNoteId,
                row.note.SeriesCode,
                row.note.SequenceNo,
                row.note.EDispatchNumber,
                row.note.ApprovedAtUtc,
                row.CustomerPartyId,
                row.CompanyAccountId,
                row.line.LineNo,
                row.line.ShipmentId,
                ShipmentKind = row.shipment == null ? (int?)null : row.shipment.Kind,
                ShippedAtUtc = row.shipment == null ? (DateTimeOffset?)null : row.shipment.RecordedAtUtc,
                row.line.SourceOrderId,
                row.line.SourceOrderLineId,
                row.line.ProductId,
                row.line.UnitId,
                row.line.Quantity,
            })
            .Take(query.Limit + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > query.Limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var result = new List<SalesInvoiceableShipmentV1>(rows.Count);
        foreach (var row in rows)
        {
            if (row.SeriesCode is not { } series || row.SequenceNo is not { } sequence || row.ApprovedAtUtc is not { } approvedAt
                || row.ShipmentKind != (int)LoadingShareKind.Shipped || row.ShippedAtUtc is not { } shippedAt || row.Quantity <= 0m)
            {
                return new SalesInvoiceableShipmentAnswerV1(SalesDispatchReadOutcomeV1.Unavailable, companyId, [], false,
                    SalesInvoicedStateSourceV1.NoSource);
            }

            result.Add(new SalesInvoiceableShipmentV1(row.DispatchNoteId, DispatchNoteNumber.Of(series, sequence).Text,
                row.EDispatchNumber, approvedAt, row.CustomerPartyId, row.CompanyAccountId, row.LineNo, row.ShipmentId, shippedAt,
                row.SourceOrderId, row.SourceOrderLineId, row.ProductId, row.UnitId, row.Quantity));
        }

        return new SalesInvoiceableShipmentAnswerV1(SalesDispatchReadOutcomeV1.Found, companyId, result, hasMore,
            SalesInvoicedStateSourceV1.NoSource);
    }
}
