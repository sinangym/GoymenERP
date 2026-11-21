using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// KLN-D22 persistence. An accepted transfer is never cancelled: undoing it stages a NEW, inert transfer plan from the
/// original destination location and custodian back to the original source, plus an immutable link carrying the
/// mandatory reason and the accepted original lines it brings back. No stock moves here; the reverse transfer is
/// dispatched and accepted by the ordinary transfer commands, which re-check the real buckets and label versions.
/// Transfer row, lines, link, command replay and audit share one Inventory save.
/// </summary>
public sealed partial class InventoryStore : IInventoryTransferReverseWriterV1, IInventoryTransferReverseReaderV1
{
    internal const string ReverseTransferCommand = "inventory.transfer.reverse.v1";

    public async Task<InventoryTransferReverseResultV1?> ReplayReverseAsync(
        Guid originalTransferId,
        ReverseInventoryTransferV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = ParseReverse(originalTransferId, request);
        return await FindReverseKeyReplayAsync(GenealogyHash(metadata.IdempotencyKey.Trim()), parsed.PayloadHash,
            metadata.ActorId, freshAuthorityFence, cancellationToken);
    }

    public async Task<InventoryTransferReverseResultV1> ReverseAsync(
        Guid originalTransferId,
        ReverseInventoryTransferV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = ParseReverse(originalTransferId, request);
        var companyId = companyContext.CompanyId;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        if (await FindReverseKeyReplayAsync(keyHash, parsed.PayloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } keyReplay)
        {
            return keyReplay;
        }

        if (await dbContext.Transfers.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == request.ReverseTransferId, cancellationToken) is { } prior)
        {
            var priorLink = await dbContext.TransferReversals.AsNoTracking()
                .SingleOrDefaultAsync(row => row.TransferId == prior.Id, cancellationToken);
            InventoryTransferViewV1? priorView = null;
            InventoryTransferReversalLinkViewV1? priorReversal = null;
            if (priorLink is not null)
            {
                priorView = await TransferViewAsync(prior.Id, cancellationToken);
                priorReversal = (await ReversalLinksAsync([priorLink], cancellationToken))[prior.Id];
            }

            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            if (priorLink is null || priorView is null || priorReversal is null
                || priorLink.OriginalTransferId != originalTransferId
                || !string.Equals(prior.PayloadHash, parsed.PayloadHash, StringComparison.Ordinal)
                || !string.Equals(priorLink.PayloadHash, parsed.PayloadHash, StringComparison.Ordinal))
            {
                throw TransferError(409, InventoryTransferCodesV1.IdempotencyConflict);
            }

            return new(priorView, priorReversal, true, prior.CorrelationId);
        }

        var original = await dbContext.Transfers.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == originalTransferId, cancellationToken);
        if (original is null)
        {
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }

        var originalLines = await dbContext.TransferLines.AsNoTracking()
            .Where(row => row.TransferId == originalTransferId)
            .ToDictionaryAsync(row => row.LineId, cancellationToken);
        var acceptedLineIds = (await dbContext.TransferAcceptances.AsNoTracking()
                .Where(row => row.TransferId == originalTransferId)
                .Select(row => row.LineId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        // KLN-D107: every overage request of a line (the receipt's own, number 1, and each renewal) is pending until it is
        // decided; a rejected first request followed by an undecided renewal keeps the line pending.
        var decidedOverageRequests = (await dbContext.TransferOverageDecisions.AsNoTracking()
                .Where(row => row.TransferId == originalTransferId)
                .Select(row => new { row.LineId, row.RequestNumber })
                .ToListAsync(cancellationToken))
            .Select(row => (row.LineId, row.RequestNumber))
            .ToHashSet();
        var overageRequests = (await dbContext.TransferReceipts.AsNoTracking()
                .Where(row => row.TransferId == originalTransferId && row.OverageKg > 0m)
                .Select(row => row.LineId)
                .ToListAsync(cancellationToken))
            .Select(lineId => (LineId: lineId, RequestNumber: FirstOverageRequestNumber))
            .Concat((await dbContext.TransferOverageRenewals.AsNoTracking()
                    .Where(row => row.TransferId == originalTransferId)
                    .Select(row => new { row.LineId, row.RequestNumber })
                    .ToListAsync(cancellationToken))
                .Select(row => (row.LineId, row.RequestNumber)));
        var pendingOverageLineIds = overageRequests
            .Where(request => !decidedOverageRequests.Contains(request))
            .Select(request => request.LineId)
            .ToHashSet();
        var activeReversedLineIds = await ActiveReversedLineIdsAsync(originalTransferId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);

        if (original.Status is not ((int)InventoryTransferStatus.PartiallyAccepted
            or (int)InventoryTransferStatus.Accepted or (int)InventoryTransferStatus.Closed))
        {
            throw TransferError(409, InventoryTransferReverseCodesV1.OriginalNotAccepted);
        }

        var planLines = new List<InventoryTransferPlanLine>(parsed.Lines.Count);
        foreach (var line in parsed.Lines)
        {
            if (!originalLines.TryGetValue(line.OriginalLineId, out var originalLine))
            {
                throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
            }

            if (!acceptedLineIds.Contains(line.OriginalLineId))
            {
                throw TransferError(409, InventoryTransferReverseCodesV1.LineNotAccepted);
            }

            if (pendingOverageLineIds.Contains(line.OriginalLineId))
            {
                throw TransferError(409, InventoryTransferReverseCodesV1.OveragePending);
            }

            if (activeReversedLineIds.Contains(line.OriginalLineId))
            {
                throw TransferError(409, InventoryTransferReverseCodesV1.LineAlreadyReversed);
            }

            planLines.Add(new InventoryTransferPlanLine(line.LineId, originalLine.LabelId, line.ExpectedLabelVersion,
                line.PlannedQuantity));
        }

        // The reverse transfer leaves from where the original arrived.
        await RequirePlanReferencesAsync(companyId, original.DestinationLocationId, planLines,
            freshAuthorityFence, cancellationToken);

        InventoryTransfer transfer;
        try
        {
            transfer = new InventoryTransfer(request.ReverseTransferId, companyId, original.DestinationLocationId,
                new StockCustodian((StockPartyKind)original.DestinationCustodianKind, original.DestinationCustodianId),
                original.SourceLocationId,
                new StockCustodian((StockPartyKind)original.SourceCustodianKind, original.SourceCustodianId),
                request.PlannedAtUtc, planLines);
        }
        catch (InventoryRuleException exception)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed, exception);
        }

        var now = timeProvider.GetUtcNow();
        var actor = metadata.ActorId.Trim();
        var correlationId = metadata.CorrelationId.Trim();
        var row = new InventoryTransferRow
        {
            Id = transfer.Id,
            CompanyId = companyId,
            Version = transfer.Version,
            Status = (int)transfer.Status,
            SourceLocationId = transfer.SourceLocationId,
            SourceCustodianKind = (int)transfer.SourceCustodian.Kind,
            SourceCustodianId = transfer.SourceCustodian.Id,
            DestinationLocationId = transfer.DestinationLocationId,
            DestinationCustodianKind = (int)transfer.DestinationCustodian.Kind,
            DestinationCustodianId = transfer.DestinationCustodian.Id,
            PlannedAtUtc = transfer.PlannedAtUtc,
            PayloadHash = parsed.PayloadHash,
            RecordedAtUtc = now,
            UpdatedAtUtc = now,
            RecordedBy = actor,
            CorrelationId = correlationId,
        };
        dbContext.Transfers.Add(row);
        var sequence = 0;
        var stagedLines = new List<InventoryTransferLineRow>(transfer.Lines.Count);
        foreach (var line in transfer.Lines)
        {
            var stagedLine = new InventoryTransferLineRow
            {
                CompanyId = companyId,
                TransferId = transfer.Id,
                LineId = line.LineId,
                Sequence = ++sequence,
                LabelId = line.LabelId,
                ExpectedLabelVersion = line.ExpectedLabelVersion,
                PlannedKg = line.PlannedQuantity.Value,
            };
            stagedLines.Add(stagedLine);
            dbContext.TransferLines.Add(stagedLine);
        }

        var reversal = new InventoryTransferReversalRow
        {
            CompanyId = companyId,
            TransferId = transfer.Id,
            OriginalTransferId = originalTransferId,
            Reason = parsed.Reason,
            PayloadHash = parsed.PayloadHash,
            RecordedAtUtc = now,
            RecordedBy = actor,
            CorrelationId = correlationId,
        };
        dbContext.TransferReversals.Add(reversal);
        foreach (var line in parsed.Lines)
        {
            dbContext.TransferReversalLines.Add(new InventoryTransferReversalLineRow
            {
                CompanyId = companyId,
                TransferId = transfer.Id,
                LineId = line.LineId,
                OriginalTransferId = originalTransferId,
                OriginalLineId = line.OriginalLineId,
            });
        }

        var view = TransferView(row, stagedLines, dispatch: null, [], []);
        var link = new InventoryTransferReversalLinkViewV1(transfer.Id, originalTransferId, reversal.Reason,
            reversal.RecordedAtUtc, reversal.RecordedBy,
            [.. parsed.Lines.Select(line => new InventoryTransferReversalLineViewV1(line.LineId, line.OriginalLineId))]);
        dbContext.Commands.Add(new InventoryCommandRow
        {
            CompanyId = companyId,
            CommandName = ReverseTransferCommand,
            IdempotencyKeyHash = keyHash,
            ActorId = actor,
            PayloadHash = parsed.PayloadHash,
            TargetId = transfer.Id,
            ResultJson = JsonSerializer.Serialize(new ReverseCommandResult(view, link), Json),
            CorrelationId = correlationId,
            RecordedAtUtc = now,
        });
        Audit(metadata, companyId, TransferEntityType, row.Id, "inventory.transfer.reverse-planned.v1", "Reverse",
            null, row.Version, "{}", JsonSerializer.Serialize(new InventoryTransferReverseResponseV1(view, link), Json),
            "Kabul edilmiş transfer için ters transfer planlandı");
        return await SaveReverseAsync(view, link, correlationId, metadata, keyHash, parsed.PayloadHash,
            freshAuthorityFence, cancellationToken);
    }

    public async Task<InventoryTransferReversalFactsViewV1?> ReadReversalFactsAsync(
        Guid transferId,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Transfers.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        if (row is null) return null;

        var links = await dbContext.TransferReversals.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId || candidate.OriginalTransferId == transferId)
            .ToListAsync(cancellationToken);
        var views = await ReversalLinksAsync(links, cancellationToken);
        var reversedBy = links.Where(link => link.OriginalTransferId == transferId)
            .OrderBy(link => link.RecordedAtUtc).ThenBy(link => link.TransferId).ToArray();
        var reverseIds = reversedBy.Select(link => link.TransferId).ToArray();
        var statuses = await dbContext.Transfers.AsNoTracking()
            .Where(candidate => reverseIds.Contains(candidate.Id))
            .ToDictionaryAsync(candidate => candidate.Id, candidate => candidate.Status, cancellationToken);
        var reversalOf = links.SingleOrDefault(link => link.TransferId == transferId);
        return new InventoryTransferReversalFactsViewV1(row.Id, row.CompanyId,
            reversalOf is null ? null : views[reversalOf.TransferId],
            [.. reversedBy.Select(link => new InventoryTransferReversedByViewV1(views[link.TransferId],
                ((InventoryTransferStatus)statuses[link.TransferId]).ToString()))]);
    }

    /// <summary>
    /// Original lines already being brought back: a reverse line whose reverse transfer was dispatched and not
    /// dispatch-cancelled. A reverse plan that was never dispatched is inert and does not block another plan; its
    /// later dispatch still has to find the label at the original destination with the expected version.
    /// </summary>
    private async Task<HashSet<Guid>> ActiveReversedLineIdsAsync(
        Guid originalTransferId,
        CancellationToken cancellationToken)
    {
        var reverseLines = await dbContext.TransferReversalLines.AsNoTracking()
            .Where(row => row.OriginalTransferId == originalTransferId)
            .Select(row => new { row.TransferId, row.OriginalLineId })
            .ToListAsync(cancellationToken);
        if (reverseLines.Count == 0) return [];

        var reverseIds = reverseLines.Select(row => row.TransferId).Distinct().ToArray();
        var dispatchedIds = (await dbContext.TransferDispatches.AsNoTracking()
                .Where(row => reverseIds.Contains(row.TransferId))
                .Select(row => row.TransferId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var cancelledIds = (await dbContext.Transfers.AsNoTracking()
                .Where(row => reverseIds.Contains(row.Id)
                    && row.Status == (int)InventoryTransferStatus.DispatchCancelled)
                .Select(row => row.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        return reverseLines
            .Where(row => dispatchedIds.Contains(row.TransferId) && !cancelledIds.Contains(row.TransferId))
            .Select(row => row.OriginalLineId)
            .ToHashSet();
    }

    private async Task<IReadOnlyDictionary<Guid, InventoryTransferReversalLinkViewV1>> ReversalLinksAsync(
        IReadOnlyList<InventoryTransferReversalRow> links,
        CancellationToken cancellationToken)
    {
        if (links.Count == 0) return new Dictionary<Guid, InventoryTransferReversalLinkViewV1>();
        var ids = links.Select(link => link.TransferId).ToArray();
        var lines = await dbContext.TransferReversalLines.AsNoTracking()
            .Where(row => ids.Contains(row.TransferId))
            .ToListAsync(cancellationToken);
        var sequences = await dbContext.TransferLines.AsNoTracking()
            .Where(row => ids.Contains(row.TransferId))
            .Select(row => new { row.TransferId, row.LineId, row.Sequence })
            .ToDictionaryAsync(row => (row.TransferId, row.LineId), row => row.Sequence, cancellationToken);
        return links.ToDictionary(link => link.TransferId, link => new InventoryTransferReversalLinkViewV1(
            link.TransferId, link.OriginalTransferId, link.Reason, link.RecordedAtUtc, link.RecordedBy,
            [.. lines.Where(line => line.TransferId == link.TransferId)
                .OrderBy(line => sequences.TryGetValue((line.TransferId, line.LineId), out var order)
                    ? order : int.MaxValue)
                .ThenBy(line => line.LineId)
                .Select(line => new InventoryTransferReversalLineViewV1(line.LineId, line.OriginalLineId))]));
    }

    private async Task<InventoryTransferReverseResultV1?> FindReverseKeyReplayAsync(
        string keyHash,
        string payloadHash,
        string actorId,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == ReverseTransferCommand && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (prior is null) return null;
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal)
            || !string.Equals(prior.ActorId, actorId.Trim(), StringComparison.Ordinal))
        {
            throw TransferError(409, InventoryTransferCodesV1.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<ReverseCommandResult>(prior.ResultJson, Json);
        return stored is { Transfer: { } transfer, Reversal: { } reversal }
            ? new(transfer, reversal, true, prior.CorrelationId)
            : throw TransferError(503, InventoryTransferCodesV1.Unavailable);
    }

    private async Task<InventoryTransferReverseResultV1> SaveReverseAsync(
        InventoryTransferViewV1 view,
        InventoryTransferReversalLinkViewV1 link,
        string correlationId,
        InventoryCommandMetadata metadata,
        string keyHash,
        string payloadHash,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || IsUniqueConstraintViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            if (await FindReverseKeyReplayAsync(keyHash, payloadHash, metadata.ActorId,
                    freshAuthorityFence, cancellationToken) is { } winner)
            {
                return winner;
            }

            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(409, InventoryTransferCodesV1.ConcurrentChange, exception);
        }

        return new(view, link, false, correlationId);
    }

    private static ParsedReverse ParseReverse(Guid originalTransferId, ReverseInventoryTransferV1 request)
    {
        if (originalTransferId == Guid.Empty || request.ReverseTransferId == Guid.Empty
            || request.ReverseTransferId == originalTransferId || request.PlannedAtUtc.Offset != TimeSpan.Zero
            || request.Lines is null || request.Lines.Count is < 1 or > InventoryTransferApiV1.MaximumLines)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var lines = new List<ParsedReverseLine>(request.Lines.Count);
        foreach (var line in request.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || line.OriginalLineId == Guid.Empty
                || !TryVersion(line.ExpectedLabelVersion, out var version)
                || !TryQuantity(line.PlannedKg, out var quantity))
            {
                throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
            }

            lines.Add(new(line.LineId, line.OriginalLineId, version, quantity));
        }

        if (lines.Select(line => line.LineId).Distinct().Count() != lines.Count
            || lines.Select(line => line.OriginalLineId).Distinct().Count() != lines.Count)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        // KLN-D22 (3): the reason is mandatory; a present but malformed reason is a validation failure.
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw TransferError(422, InventoryTransferReverseCodesV1.ReasonRequired);
        }

        var reason = InventoryTransferReverseApiV1.NormalizeReason(request.Reason)
            ?? throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        var payloadHash = GenealogyPayloadHash(new
        {
            originalTransferId,
            request.ReverseTransferId,
            plannedAtUtc = request.PlannedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            reason,
            lines = lines.Select(line => new
            {
                line.LineId,
                line.OriginalLineId,
                line.ExpectedLabelVersion,
                plannedKg = line.PlannedQuantity.ToString(),
            }),
        });
        return new(reason, lines, payloadHash);
    }

    private sealed record ParsedReverseLine(
        Guid LineId,
        Guid OriginalLineId,
        long ExpectedLabelVersion,
        Kilograms PlannedQuantity);

    private sealed record ParsedReverse(string Reason, IReadOnlyList<ParsedReverseLine> Lines, string PayloadHash);

    private sealed record ReverseCommandResult(
        InventoryTransferViewV1? Transfer,
        InventoryTransferReversalLinkViewV1? Reversal);
}
