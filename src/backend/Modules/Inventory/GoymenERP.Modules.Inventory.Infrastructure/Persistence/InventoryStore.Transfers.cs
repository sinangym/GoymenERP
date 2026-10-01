using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// INV-R20 persistence. Plans are inert. Dispatch removes every stored bucket of every planned label in one movement;
/// accepting one dispatched whole-label line restores its exact buckets at the target. Transfer row, immutable events,
/// movement, balances, label versions, command replay and audit share one Inventory save.
/// </summary>
public sealed partial class InventoryStore : IInventoryTransferWriterV1, IInventoryTransferReaderV1
{
    internal const string PlanTransferCommand = "inventory.transfer.plan.v1";
    internal const string DispatchTransferCommand = "inventory.transfer.dispatch.v1";
    internal const string AcceptTransferLineCommand = "inventory.transfer.accept-line.v1";
    private const string TransferEntityType = "Inventory.Transfer";

    public async Task<InventoryTransferResultV1> PlanAsync(
        PlanInventoryTransferV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = ParsePlan(request);
        var companyId = companyContext.CompanyId;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        var payloadHash = GenealogyPayloadHash(new
        {
            request.TransferId,
            request.SourceLocationId,
            sourceCustodianKind = (int)parsed.SourceCustodian.Kind,
            request.SourceCustodianId,
            request.DestinationLocationId,
            destinationCustodianKind = (int)parsed.DestinationCustodian.Kind,
            request.DestinationCustodianId,
            plannedAtUtc = request.PlannedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            lines = parsed.Lines.Select(line => new
            {
                line.LineId,
                line.LabelId,
                line.ExpectedLabelVersion,
                plannedKg = line.PlannedQuantity.ToString(),
            }),
        });
        if (await FindTransferKeyReplayAsync(PlanTransferCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } keyReplay)
        {
            return keyReplay;
        }

        if (await dbContext.Transfers.AsNoTracking().SingleOrDefaultAsync(row => row.Id == request.TransferId,
                cancellationToken) is { } prior)
        {
            var priorView = await TransferViewAsync(prior.Id, cancellationToken);
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw TransferError(409, InventoryTransferCodesV1.IdempotencyConflict);
            }

            return new(priorView, true, prior.CorrelationId);
        }

        await RequirePlanReferencesAsync(companyId, request.SourceLocationId, parsed.Lines,
            freshAuthorityFence, cancellationToken);

        InventoryTransfer transfer;
        try
        {
            transfer = new InventoryTransfer(request.TransferId, companyId, request.SourceLocationId,
                parsed.SourceCustodian, request.DestinationLocationId, parsed.DestinationCustodian,
                request.PlannedAtUtc, parsed.Lines);
        }
        catch (InventoryRuleException exception)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed, exception);
        }
        var now = timeProvider.GetUtcNow();
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
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            UpdatedAtUtc = now,
            RecordedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
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

        var view = TransferView(row, stagedLines, dispatch: null, [], []);
        dbContext.Commands.Add(TransferCommandRow(PlanTransferCommand, keyHash, payloadHash, metadata, row.Id, view));
        Audit(metadata, companyId, TransferEntityType, row.Id, "inventory.transfer.planned.v1", "Plan",
            null, row.Version, "{}", JsonSerializer.Serialize(view, Json), "Transfer planlandı");
        return await SaveTransferAsync(view, row.CorrelationId, metadata, PlanTransferCommand, keyHash, payloadHash,
            freshAuthorityFence, cancellationToken);
    }

    public async Task<InventoryTransferResultV1> DispatchAsync(
        Guid transferId,
        DispatchInventoryTransferV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        if (transferId == Guid.Empty || !TryVersion(request.ExpectedTransferVersion, out var expectedVersion)
            || request.DispatchedAtUtc.Offset != TimeSpan.Zero)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var document = ParseDocument(request.OutgoingDocument);
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        var payloadHash = GenealogyPayloadHash(new
        {
            transferId,
            expectedVersion,
            dispatchedAtUtc = request.DispatchedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            document,
        });
        if (await FindTransferKeyReplayAsync(DispatchTransferCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } keyReplay)
        {
            return keyReplay;
        }

        if (await dbContext.TransferDispatches.AsNoTracking()
                .SingleOrDefaultAsync(row => row.TransferId == transferId, cancellationToken) is { } prior)
        {
            var priorView = await TransferViewAsync(transferId, cancellationToken);
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
            }

            return new(priorView, true, prior.CorrelationId);
        }

        var row = await dbContext.Transfers.SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        if (row is null)
        {
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (row.Version != expectedVersion)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferVersionConflict);
        }

        var lines = await dbContext.TransferLines.AsNoTracking().Where(line => line.TransferId == transferId)
            .OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var labelIds = lines.Select(line => line.LabelId).ToArray();
        var hasActiveAllocation = await dbContext.Allocations.AsNoTracking().AnyAsync(
            allocation => labelIds.Contains(allocation.LabelId)
                && allocation.Status == (int)StockAllocationStatus.Active && allocation.RemainingKg > 0m,
            cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (hasActiveAllocation)
        {
            throw TransferError(409, InventoryTransferCodesV1.ActiveAllocationConflict);
        }

        var labels = await dbContext.Labels.Where(label => labelIds.Contains(label.Id))
            .ToDictionaryAsync(label => label.Id, cancellationToken);
        var balances = await dbContext.Balances
            .Where(balance => labelIds.Contains(balance.LabelId) && balance.QuantityKg > 0m)
            .ToListAsync(cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        var actualLines = new List<InventoryTransferDispatchLine>(lines.Count);
        foreach (var line in lines)
        {
            if (!labels.TryGetValue(line.LabelId, out var label))
            {
                throw TransferError(404, InventoryTransferCodesV1.LabelNotFound);
            }

            if (label.Version != line.ExpectedLabelVersion)
            {
                throw TransferError(409, InventoryTransferCodesV1.LabelVersionConflict);
            }

            var lineBalances = balances.Where(balance => balance.LabelId == line.LabelId).ToArray();
            if (lineBalances.Length == 0)
            {
                throw TransferError(409, InventoryTransferCodesV1.SourcePositionConflict);
            }

            actualLines.Add(new(line.LineId, label.Version,
                [.. lineBalances.Select(balance => new StockBalance(
                    Position(balance.CompanyId, balance.OwnerKind, balance.OwnerId, balance.CustodianKind,
                        balance.CustodianId, balance.LocationId, IdentityOnly(label), balance.Block),
                    Kilograms.Positive(balance.QuantityKg)))]));
        }

        var aggregate = RestoreTransfer(row, lines, dispatch: null, [], []);
        InventoryTransferDispatchEffect effect;
        try
        {
            effect = aggregate.Dispatch(expectedVersion, request.DispatchedAtUtc, document, actualLines);
        }
        catch (InventoryRuleException exception)
        {
            throw TransferError(409, InventoryTransferCodesV1.SourcePositionConflict, exception);
        }

        var movement = effect.Movement(Guid.NewGuid(), row.CompanyId);
        await StageTransferMovementAsync(movement, metadata, freshAuthorityFence, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var dispatch = new InventoryTransferDispatchRow
        {
            CompanyId = row.CompanyId,
            TransferId = row.Id,
            MovementId = movement.Id,
            DocumentKind = document.Kind,
            DocumentId = document.Id,
            DocumentLineId = document.LineId,
            DispatchedAtUtc = request.DispatchedAtUtc,
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        };
        dbContext.TransferDispatches.Add(dispatch);
        foreach (var line in effect.Lines)
        {
            var planned = lines.Single(candidate => candidate.LineId == line.LineId);
            dbContext.TransferDispatchLines.Add(new InventoryTransferDispatchLineRow
            {
                CompanyId = row.CompanyId,
                TransferId = row.Id,
                LineId = line.LineId,
                LabelVersionAfterDispatch = labels[planned.LabelId].Version,
                ActualKg = line.ActualQuantity.Value,
                DifferenceKg = line.DifferenceKg,
            });
            var bucketSequence = 0;
            foreach (var bucket in line.Buckets)
            {
                dbContext.TransferBuckets.Add(new InventoryTransferBucketRow
                {
                    CompanyId = row.CompanyId,
                    TransferId = row.Id,
                    LineId = line.LineId,
                    Sequence = ++bucketSequence,
                    OwnerKind = (int)bucket.Position.Owner.Kind,
                    OwnerId = bucket.Position.Owner.Id,
                    CustodianKind = (int)bucket.Position.Custodian.Kind,
                    CustodianId = bucket.Position.Custodian.Id,
                    LocationId = bucket.Position.LocationId,
                    LabelId = bucket.Position.LabelId,
                    ProductId = bucket.Position.ProductId,
                    Block = (int)bucket.Position.Block,
                    QuantityKg = bucket.Quantity.Value,
                });
            }
        }

        var before = await TransferViewAsync(row.Id, cancellationToken);
        row.Version = effect.TransferVersion;
        row.Status = (int)aggregate.Status;
        row.UpdatedAtUtc = now;
        var view = TransferView(row, lines, dispatch, effect.Lines.Select(ToDispatchRow).ToArray(), []);
        dbContext.Commands.Add(TransferCommandRow(DispatchTransferCommand, keyHash, payloadHash, metadata, row.Id, view));
        Audit(metadata, row.CompanyId, TransferEntityType, row.Id, "inventory.transfer.dispatched.v1", "Dispatch",
            expectedVersion, row.Version, JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(view, Json),
            "Transfer yola çıkarıldı");
        return await SaveTransferAsync(view, dispatch.CorrelationId, metadata, DispatchTransferCommand, keyHash,
            payloadHash, freshAuthorityFence, cancellationToken);
    }

    public async Task<InventoryTransferResultV1> AcceptLineAsync(
        Guid transferId,
        Guid lineId,
        AcceptInventoryTransferLineV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        if (transferId == Guid.Empty || lineId == Guid.Empty
            || !TryVersion(request.ExpectedTransferVersion, out var expectedVersion)
            || request.AcceptedAtUtc.Offset != TimeSpan.Zero)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var document = ParseDocument(request.IncomingDocument);
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        var payloadHash = GenealogyPayloadHash(new
        {
            transferId,
            lineId,
            expectedVersion,
            acceptedAtUtc = request.AcceptedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            document,
        });
        if (await FindTransferKeyReplayAsync(AcceptTransferLineCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } keyReplay)
        {
            return keyReplay;
        }

        if (await dbContext.TransferAcceptances.AsNoTracking()
                .SingleOrDefaultAsync(row => row.TransferId == transferId && row.LineId == lineId,
                    cancellationToken) is { } prior)
        {
            var priorView = await TransferViewAsync(transferId, cancellationToken);
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
            }

            return new(priorView, true, prior.CorrelationId);
        }

        var row = await dbContext.Transfers.SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        if (row is null)
        {
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (row.Version != expectedVersion)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferVersionConflict);
        }

        var lines = await dbContext.TransferLines.AsNoTracking().Where(line => line.TransferId == transferId)
            .OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (lines.All(line => line.LineId != lineId))
        {
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }

        var dispatch = await dbContext.TransferDispatches.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId, cancellationToken);
        if (dispatch is null)
        {
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }
        var dispatchLines = await dbContext.TransferDispatchLines.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).ToListAsync(cancellationToken);
        var buckets = await dbContext.TransferBuckets.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).OrderBy(candidate => candidate.Sequence)
            .ToListAsync(cancellationToken);
        var accepted = await dbContext.TransferAcceptances.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).ToListAsync(cancellationToken);
        var labelIds = lines.Select(line => line.LabelId).ToArray();
        var labels = await dbContext.Labels.Where(candidate => labelIds.Contains(candidate.Id))
            .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        var dispatchLine = dispatchLines.Single(candidate => candidate.LineId == lineId);
        var labelId = lines.Single(candidate => candidate.LineId == lineId).LabelId;
        if (labels.Count != labelIds.Length || !labels.TryGetValue(labelId, out var label))
        {
            throw TransferError(503, InventoryTransferCodesV1.Unavailable);
        }
        if (label.Version != dispatchLine.LabelVersionAfterDispatch)
        {
            throw TransferError(409, InventoryTransferCodesV1.LabelVersionConflict);
        }

        var aggregate = RestoreTransfer(row, lines, dispatch, dispatchLines, accepted, buckets, labels);
        InventoryTransferAcceptanceEffect effect;
        try
        {
            effect = aggregate.AcceptLine(expectedVersion, lineId, request.AcceptedAtUtc, document);
        }
        catch (InventoryRuleException exception)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict, exception);
        }

        var movement = effect.Movement(Guid.NewGuid(), row.CompanyId);
        await StageTransferMovementAsync(movement, metadata, freshAuthorityFence, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var acceptance = new InventoryTransferAcceptanceRow
        {
            CompanyId = row.CompanyId,
            TransferId = row.Id,
            LineId = lineId,
            MovementId = movement.Id,
            DocumentKind = document.Kind,
            DocumentId = document.Id,
            DocumentLineId = document.LineId,
            AcceptedAtUtc = request.AcceptedAtUtc,
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        };
        dbContext.TransferAcceptances.Add(acceptance);
        var before = TransferView(row, lines, dispatch, dispatchLines, accepted);
        row.Version = effect.TransferVersion;
        row.Status = (int)aggregate.Status;
        row.UpdatedAtUtc = now;
        var afterAccepted = accepted.Append(acceptance).ToArray();
        var view = TransferView(row, lines, dispatch, dispatchLines, afterAccepted);
        dbContext.Commands.Add(TransferCommandRow(AcceptTransferLineCommand, keyHash, payloadHash, metadata, row.Id, view));
        Audit(metadata, row.CompanyId, TransferEntityType, row.Id, "inventory.transfer.line-accepted.v1", "AcceptLine",
            expectedVersion, row.Version, JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(view, Json),
            "Transfer satırı hedefte kabul edildi");
        return await SaveTransferAsync(view, acceptance.CorrelationId, metadata, AcceptTransferLineCommand, keyHash,
            payloadHash, freshAuthorityFence, cancellationToken);
    }

    async Task<InventoryTransferViewV1?> IInventoryTransferReaderV1.ReadAsync(
        Guid transferId,
        CancellationToken cancellationToken)
    {
        RequireResolvedCompany();
        if (transferId == Guid.Empty)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        return await dbContext.Transfers.AsNoTracking().AnyAsync(row => row.Id == transferId, cancellationToken)
            ? await TransferViewAsync(transferId, cancellationToken)
            : null;
    }

    private async Task StageTransferMovementAsync(
        Domain.Movements.StockMovement movement,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await StageMovementAsync(movement, metadata, recordAudit: true, cancellationToken)
                != InventoryWriteOutcome.Recorded)
            {
                throw TransferError(409, InventoryTransferCodesV1.ConcurrentChange);
            }
        }
        catch (InventoryStoreException exception)
        {
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw exception.Code switch
            {
                InventoryScaleCodes.ScaleExceeded => TransferError(400, exception.Code, exception),
                InventoryStoreCodes.ConcurrentChange or InventoryStoreCodes.DuplicateSource
                    or InventoryStoreCodes.SourceGrainMismatch =>
                    TransferError(409, InventoryTransferCodesV1.ConcurrentChange, exception),
                _ => TransferError(503, InventoryTransferCodesV1.Unavailable, exception),
            };
        }
        catch (InventoryRuleException exception)
        {
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(409, InventoryTransferCodesV1.SourcePositionConflict, exception);
        }
    }

    private InventoryTransfer RestoreTransfer(
        InventoryTransferRow row,
        IReadOnlyList<InventoryTransferLineRow> lines,
        InventoryTransferDispatchRow? dispatch,
        IReadOnlyList<InventoryTransferDispatchLineRow> dispatchLines,
        IReadOnlyList<InventoryTransferAcceptanceRow> acceptances,
        IReadOnlyList<InventoryTransferBucketRow>? storedBuckets = null,
        IReadOnlyDictionary<Guid, LabelRow>? storedLabels = null)
    {
        var labelRows = storedLabels ?? new Dictionary<Guid, LabelRow>();
        var buckets = storedBuckets ?? [];
        var actual = dispatch is null ? null : dispatchLines.Select(actualLine =>
        {
            var planned = lines.Single(line => line.LineId == actualLine.LineId);
            if (!labelRows.TryGetValue(planned.LabelId, out var label))
            {
                throw TransferError(503, InventoryTransferCodesV1.Unavailable);
            }

            return new InventoryTransferDispatchLine(actualLine.LineId, planned.ExpectedLabelVersion,
                [.. buckets.Where(bucket => bucket.LineId == actualLine.LineId).OrderBy(bucket => bucket.Sequence)
                    .Select(bucket => new StockBalance(
                        Position(bucket.CompanyId, bucket.OwnerKind, bucket.OwnerId, bucket.CustodianKind,
                            bucket.CustodianId, bucket.LocationId, IdentityOnly(label), bucket.Block),
                        Kilograms.Positive(bucket.QuantityKg)))]);
        }).ToArray();
        return InventoryTransfer.Restore(row.Id, row.CompanyId, row.SourceLocationId,
            new StockCustodian((StockPartyKind)row.SourceCustodianKind, row.SourceCustodianId),
            row.DestinationLocationId,
            new StockCustodian((StockPartyKind)row.DestinationCustodianKind, row.DestinationCustodianId),
            row.PlannedAtUtc,
            lines.Select(line => new InventoryTransferPlanLine(line.LineId, line.LabelId, line.ExpectedLabelVersion,
                Kilograms.Positive(line.PlannedKg))),
            row.Version, (InventoryTransferStatus)row.Status,
            dispatch?.DispatchedAtUtc,
            dispatch is null ? null : new InventoryTransferDocumentReference(
                dispatch.DocumentKind, dispatch.DocumentId, dispatch.DocumentLineId),
            actual,
            acceptances.Select(acceptance => new InventoryTransferAcceptedLineState(acceptance.LineId,
                acceptance.AcceptedAtUtc,
                new InventoryTransferDocumentReference(acceptance.DocumentKind, acceptance.DocumentId,
                    acceptance.DocumentLineId))));
    }

    private async Task<InventoryTransferViewV1> TransferViewAsync(Guid transferId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Transfers.AsNoTracking().SingleAsync(candidate => candidate.Id == transferId,
            cancellationToken);
        var lines = await dbContext.TransferLines.AsNoTracking().Where(line => line.TransferId == transferId)
            .OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var dispatch = await dbContext.TransferDispatches.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId, cancellationToken);
        var actual = await dbContext.TransferDispatchLines.AsNoTracking()
            .Where(line => line.TransferId == transferId).ToListAsync(cancellationToken);
        var accepted = await dbContext.TransferAcceptances.AsNoTracking()
            .Where(line => line.TransferId == transferId).ToListAsync(cancellationToken);
        return TransferView(row, lines, dispatch, actual, accepted);
    }

    private static InventoryTransferViewV1 TransferView(
        InventoryTransferRow row,
        IReadOnlyList<InventoryTransferLineRow> lines,
        InventoryTransferDispatchRow? dispatch,
        IReadOnlyList<InventoryTransferDispatchLineRow> actual,
        IReadOnlyList<InventoryTransferAcceptanceRow> accepted)
    {
        var actualByLine = actual.ToDictionary(line => line.LineId);
        var acceptedIds = accepted.Select(line => line.LineId).ToHashSet();
        return new InventoryTransferViewV1(row.Id, row.CompanyId, row.Version.ToString(CultureInfo.InvariantCulture),
            ((InventoryTransferStatus)row.Status).ToString(), row.SourceLocationId,
            ((StockPartyKind)row.SourceCustodianKind).ToString(), row.SourceCustodianId,
            row.DestinationLocationId, ((StockPartyKind)row.DestinationCustodianKind).ToString(),
            row.DestinationCustodianId, row.PlannedAtUtc, dispatch?.DispatchedAtUtc,
            [.. lines.Select(line =>
            {
                actualByLine.TryGetValue(line.LineId, out var dispatched);
                var status = acceptedIds.Contains(line.LineId) ? InventoryTransferLineStatus.Accepted
                    : dispatched is null ? InventoryTransferLineStatus.Planned : InventoryTransferLineStatus.InTransit;
                return new InventoryTransferLineViewV1(line.LineId, line.LabelId,
                    line.ExpectedLabelVersion.ToString(CultureInfo.InvariantCulture), Kilograms.Positive(line.PlannedKg).ToString(),
                    dispatched is null ? null : Kilograms.Positive(dispatched.ActualKg).ToString(),
                    dispatched is null ? null : SignedKilograms(dispatched.DifferenceKg), status.ToString());
            })]);
    }

    private async Task<InventoryTransferResultV1?> FindTransferKeyReplayAsync(
        string command,
        string keyHash,
        string payloadHash,
        string actorId,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (prior is null) return null;
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal)
            || !string.Equals(prior.ActorId, actorId.Trim(), StringComparison.Ordinal))
        {
            throw TransferError(409, InventoryTransferCodesV1.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<TransferCommandResult>(prior.ResultJson, Json)
            ?? throw TransferError(503, InventoryTransferCodesV1.Unavailable);
        return new(stored.Transfer, true, prior.CorrelationId);
    }

    private async Task<InventoryTransferResultV1> SaveTransferAsync(
        InventoryTransferViewV1 view,
        string correlationId,
        InventoryCommandMetadata metadata,
        string command,
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
            if (await FindTransferKeyReplayAsync(command, keyHash, payloadHash, metadata.ActorId,
                    freshAuthorityFence, cancellationToken) is { } winner)
            {
                return winner;
            }

            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(409, InventoryTransferCodesV1.ConcurrentChange, exception);
        }

        return new(view, false, correlationId);
    }

    private async Task RequireFreshAuthorityAsync(
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        try
        {
            await freshAuthorityFence(cancellationToken);
        }
        catch
        {
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private InventoryCommandRow TransferCommandRow(
        string command,
        string keyHash,
        string payloadHash,
        InventoryCommandMetadata metadata,
        Guid transferId,
        InventoryTransferViewV1 view) => new()
        {
            CompanyId = companyContext.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId.Trim(),
            PayloadHash = payloadHash,
            TargetId = transferId,
            ResultJson = JsonSerializer.Serialize(new TransferCommandResult(view), Json),
            CorrelationId = metadata.CorrelationId.Trim(),
            RecordedAtUtc = timeProvider.GetUtcNow(),
        };

    private static (StockCustodian SourceCustodian, StockCustodian DestinationCustodian,
        InventoryTransferPlanLine[] Lines) ParsePlan(PlanInventoryTransferV1 request)
    {
        if (request.TransferId == Guid.Empty || request.SourceLocationId == Guid.Empty
            || request.DestinationLocationId == Guid.Empty || request.PlannedAtUtc.Offset != TimeSpan.Zero
            || request.Lines is null || request.Lines.Count is < 1 or > InventoryTransferApiV1.MaximumLines
            || !TryParty(request.SourceCustodianKind, request.SourceCustodianId, out var source)
            || !TryParty(request.DestinationCustodianKind, request.DestinationCustodianId, out var destination))
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var lines = new List<InventoryTransferPlanLine>(request.Lines.Count);
        foreach (var line in request.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || line.LabelId == Guid.Empty
                || !TryVersion(line.ExpectedLabelVersion, out var version)
                || !TryQuantity(line.PlannedKg, out var quantity))
            {
                throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
            }

            lines.Add(new(line.LineId, line.LabelId, version, quantity));
        }

        if ((request.SourceLocationId == request.DestinationLocationId && source == destination)
            || lines.Select(line => line.LineId).Distinct().Count() != lines.Count
            || lines.Select(line => line.LabelId).Distinct().Count() != lines.Count)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        return (source, destination, lines.ToArray());
    }

    private async Task RequirePlanReferencesAsync(
        Guid companyId,
        Guid sourceLocationId,
        IReadOnlyList<InventoryTransferPlanLine> lines,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var labelIds = lines.Select(line => line.LabelId).ToArray();
        var labelCount = await dbContext.Labels.AsNoTracking()
            .CountAsync(row => labelIds.Contains(row.Id), cancellationToken);
        var sourceExists = await dbContext.Set<OrganizationLocationReference>().AsNoTracking()
            .AnyAsync(row => row.CompanyId == companyId && row.Id == sourceLocationId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);

        if (labelCount != labelIds.Length)
        {
            throw TransferError(404, InventoryTransferCodesV1.LabelNotFound);
        }

        if (!sourceExists)
        {
            throw TransferError(409, InventoryTransferCodesV1.SourcePositionConflict);
        }
    }

    private static InventoryTransferDocumentReference ParseDocument(InventoryTransferDocumentReferenceV1? value)
    {
        if (value is null)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        try
        {
            return new(value.Kind, value.Id, value.LineId);
        }
        catch (InventoryRuleException exception)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed, exception);
        }
    }

    private static bool TryParty(string? kindText, Guid id, out StockCustodian custodian)
    {
        custodian = default;
        if (id == Guid.Empty || !Enum.TryParse<StockPartyKind>(kindText, ignoreCase: false, out var kind)
            || !Enum.IsDefined(kind)) return false;
        custodian = new StockCustodian(kind, id);
        return true;
    }

    private void RequireTransferCall(InventoryCommandMetadata metadata)
    {
        RequireMetadata(metadata);
        RequireResolvedCompany();
        if (!string.Equals(metadata.IdempotencyKey, metadata.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || metadata.IdempotencyKey.Length > 128 || metadata.IdempotencyKey.Any(char.IsControl)
            || !string.Equals(metadata.ActorId, metadata.ActorId.Trim(), StringComparison.Ordinal)
            || metadata.ActorId.Length > 128 || metadata.ActorId.Any(char.IsControl)
            || !string.Equals(metadata.CorrelationId, metadata.CorrelationId.Trim(), StringComparison.Ordinal)
            || metadata.CorrelationId.Length > 128)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }
    }

    private static InventoryTransferDispatchLineRow ToDispatchRow(InventoryTransferLineEffect line) => new()
    {
        LineId = line.LineId,
        ActualKg = line.ActualQuantity.Value,
        DifferenceKg = line.DifferenceKg,
    };

    private static InventoryTransferException TransferError(int status, string code, Exception? inner = null) =>
        new(status, code, inner);

    private sealed record TransferCommandResult(InventoryTransferViewV1 Transfer);
}
