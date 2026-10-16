using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// I04.1 foundation: Inventory owns the real whole-label block/release and its kilograms. Quality
/// supplies only an inspection/decision identity; evidence, transfer acceptance and expiry never
/// auto-release stock. No partial amount or synthetic label is accepted by this boundary.
/// </summary>
public sealed partial class InventoryStore : IInventoryPhysicalHoldWriterV1
{
    internal const string PlacePhysicalHoldCommand = "inventory.physical-hold.place.v1";
    internal const string ReleasePhysicalHoldCommand = "inventory.physical-hold.release.v1";
    internal const int QualityPhysicalHoldOwner = 1;
    internal const int PhysicalHoldActive = 1;
    internal const int PhysicalHoldReleased = 2;

    private const string PhysicalHoldEntityType = "Inventory.PhysicalHold";
    private const string QualityHoldMovementSource = "quality.inspection-hold";
    private const string QualityReleaseMovementSource = "quality.disposition";

    public async Task<InventoryPhysicalHoldResultV1> PlacePhysicalHoldAsync(
        PlaceInventoryPhysicalHoldV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMetadata(metadata);
        RequireResolvedCompany();
        var parsed = ParsePlacement(request);
        var companyId = companyContext.CompanyId;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        var payloadHash = GenealogyPayloadHash(new
        {
            request.HoldId,
            request.LabelId,
            parsed.ExpectedLabelVersion,
            request.InspectionId,
            parsed.Reason,
            expiresAtUtc = request.ExpiresAtUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        });

        if (await FindPhysicalHoldCommandReplayAsync(PlacePhysicalHoldCommand, keyHash, payloadHash,
                metadata.ActorId, cancellationToken) is { } keyReplay)
        {
            return keyReplay;
        }

        if (await FindPlacementIdentityReplayAsync(request.HoldId, payloadHash, metadata.ActorId,
                cancellationToken) is { } identityReplay)
        {
            return identityReplay;
        }

        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var label = await dbContext.Labels.SingleOrDefaultAsync(row => row.Id == request.LabelId, cancellationToken);
        if (label is null)
        {
            throw HoldError(404, InventoryPhysicalHoldCodesV1.LabelNotFound);
        }
        if (label.Version != parsed.ExpectedLabelVersion)
        {
            throw HoldError(409, InventoryPhysicalHoldCodesV1.LabelVersionConflict);
        }

        var balances = await PositiveBalancesAsync(label.Id, cancellationToken);
        var activeHolds = await dbContext.PhysicalHolds.AsNoTracking()
            .Where(row => row.LabelId == label.Id && row.Status == PhysicalHoldActive)
            .ToListAsync(cancellationToken);

        BalanceRow physical;
        Guid? movementId = null;
        if (activeHolds.Count == 0)
        {
            if (balances.Any(row => row.Block == (int)BlockState.Blocked))
            {
                throw HoldError(409, InventoryPhysicalHoldCodesV1.LegacyBlockedAmbiguous);
            }
            physical = SinglePhysicalBalance(balances, BlockState.Free);
            var free = StoredPosition(physical, label);
            var movement = new StockMovement(
                request.HoldId,
                companyId,
                StockMovementKind.Block,
                timeProvider.GetUtcNow(),
                new StockSourceReference(QualityHoldMovementSource, request.InspectionId),
                [
                    new StockMovementLine(free, StockDirection.Out, Kilograms.Positive(physical.QuantityKg)),
                    new StockMovementLine(free.WithBlock(BlockState.Blocked), StockDirection.In,
                        Kilograms.Positive(physical.QuantityKg)),
                ]);
            try
            {
                await StageMovementAsync(movement, metadata, recordAudit: true, cancellationToken);
            }
            catch (InventoryStoreException exception) when (exception.Code == InventoryStoreCodes.ConcurrentChange)
            {
                throw HoldError(409, InventoryPhysicalHoldCodesV1.ConcurrentChange, exception);
            }
            movementId = movement.Id;
        }
        else
        {
            physical = SinglePhysicalBalance(balances, BlockState.Blocked);
            if (activeHolds.Any(row => !SamePhysical(row, physical)))
            {
                throw HoldError(503, InventoryPhysicalHoldCodesV1.Unavailable);
            }
        }

        var now = timeProvider.GetUtcNow();
        if (movementId is null)
        {
            // A second typed hold changes no kilograms, but it still changes the label's hold set.
            // Touch the label so concurrent hold lifecycle commands serialize at one physical boundary.
            label.Version = checked(label.Version + 1);
            label.UpdatedAtUtc = now;
        }
        var row = new InventoryPhysicalHoldRow
        {
            Id = request.HoldId,
            CompanyId = companyId,
            LabelId = label.Id,
            LabelVersionAtPlacement = parsed.ExpectedLabelVersion,
            OwnerKind = QualityPhysicalHoldOwner,
            SourceKind = InventoryPhysicalHoldTokensV1.QualityInspectionSource,
            SourceId = request.InspectionId,
            PositionOwnerKind = physical.OwnerKind,
            PositionOwnerId = physical.OwnerId,
            CustodianKind = physical.CustodianKind,
            CustodianId = physical.CustodianId,
            LocationId = physical.LocationId,
            ProductId = physical.ProductId,
            QuantityKg = physical.QuantityKg,
            Reason = parsed.Reason,
            ExpiresAtUtc = request.ExpiresAtUtc,
            Status = PhysicalHoldActive,
            Version = 1,
            PlacementPayloadHash = payloadHash,
            BlockMovementId = movementId,
            CreatedAtUtc = now,
            CreatedBy = metadata.ActorId.Trim(),
            CreatedCorrelationId = metadata.CorrelationId.Trim(),
        };
        dbContext.PhysicalHolds.Add(row);
        var view = PhysicalHoldView(row, label.Version);
        dbContext.Commands.Add(PhysicalHoldCommandRow(PlacePhysicalHoldCommand, companyId, keyHash, payloadHash,
            metadata, row.Id, view));
        Audit(metadata, companyId, PhysicalHoldEntityType, row.Id, "inventory.physical-hold.placed.v1", "Place",
            versionBefore: null, versionAfter: 1, before: "{}", after: JsonSerializer.Serialize(view, Json), row.Reason);

        try
        {
            await SaveAsync(cancellationToken);
        }
        catch (InventoryStoreException exception) when (exception.Code == InventoryStoreCodes.ConcurrentChange)
        {
            throw HoldError(409, InventoryPhysicalHoldCodesV1.ConcurrentChange, exception);
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return new(view, false, row.CreatedCorrelationId);
    }

    public async Task<InventoryPhysicalHoldResultV1> ReleasePhysicalHoldAsync(
        ReleaseInventoryPhysicalHoldV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMetadata(metadata);
        RequireResolvedCompany();
        var parsed = ParseRelease(request);
        var companyId = companyContext.CompanyId;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        var payloadHash = GenealogyPayloadHash(new
        {
            request.HoldId,
            parsed.ExpectedHoldVersion,
            request.LabelId,
            parsed.ExpectedLabelVersion,
            request.DispositionId,
            parsed.Reason,
        });

        if (await FindPhysicalHoldCommandReplayAsync(ReleasePhysicalHoldCommand, keyHash, payloadHash,
                metadata.ActorId, cancellationToken) is { } keyReplay)
        {
            return keyReplay;
        }

        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var row = await dbContext.PhysicalHolds.SingleOrDefaultAsync(candidate => candidate.Id == request.HoldId,
            cancellationToken) ?? throw HoldError(404, InventoryPhysicalHoldCodesV1.NotFound);
        if (row.OwnerKind != QualityPhysicalHoldOwner)
        {
            throw HoldError(409, InventoryPhysicalHoldCodesV1.OwnerConflict);
        }
        if (row.Status == PhysicalHoldReleased)
        {
            if (row.ReleaseDispositionId == request.DispositionId
                && string.Equals(row.ReleasePayloadHash, payloadHash, StringComparison.Ordinal)
                && string.Equals(row.ReleasedBy, metadata.ActorId.Trim(), StringComparison.Ordinal))
            {
                var releasedLabelVersion = await dbContext.Labels.AsNoTracking()
                    .Where(label => label.Id == row.LabelId).Select(label => label.Version)
                    .SingleAsync(cancellationToken);
                return new(PhysicalHoldView(row, releasedLabelVersion), true,
                    row.ReleaseCorrelationId ?? throw HoldError(503, InventoryPhysicalHoldCodesV1.Unavailable));
            }
            throw HoldError(409, InventoryPhysicalHoldCodesV1.AlreadyReleased);
        }
        if (row.Status != PhysicalHoldActive || row.LabelId != request.LabelId)
        {
            throw HoldError(409, InventoryPhysicalHoldCodesV1.HoldConflict);
        }
        if (row.Version != parsed.ExpectedHoldVersion)
        {
            throw HoldError(409, InventoryPhysicalHoldCodesV1.HoldVersionConflict);
        }
        if (string.Equals(row.CreatedBy, metadata.ActorId.Trim(), StringComparison.Ordinal))
        {
            throw HoldError(409, InventoryPhysicalHoldCodesV1.SelfApproval);
        }

        var label = await dbContext.Labels.SingleOrDefaultAsync(candidate => candidate.Id == row.LabelId,
            cancellationToken) ?? throw HoldError(404, InventoryPhysicalHoldCodesV1.LabelNotFound);
        if (label.Version != parsed.ExpectedLabelVersion)
        {
            throw HoldError(409, InventoryPhysicalHoldCodesV1.LabelVersionConflict);
        }

        var balances = await PositiveBalancesAsync(label.Id, cancellationToken);
        var blocked = SinglePhysicalBalance(balances, BlockState.Blocked);
        if (!SamePhysical(row, blocked))
        {
            throw HoldError(503, InventoryPhysicalHoldCodesV1.Unavailable);
        }
        var hasOtherActiveHold = await dbContext.PhysicalHolds.AsNoTracking()
            .AnyAsync(candidate => candidate.LabelId == row.LabelId && candidate.Id != row.Id
                && candidate.Status == PhysicalHoldActive, cancellationToken);

        Guid? releaseMovementId = null;
        if (!hasOtherActiveHold)
        {
            var blockedPosition = StoredPosition(blocked, label);
            var movement = new StockMovement(
                request.DispositionId,
                companyId,
                StockMovementKind.Release,
                timeProvider.GetUtcNow(),
                new StockSourceReference(QualityReleaseMovementSource, request.DispositionId),
                [
                    new StockMovementLine(blockedPosition, StockDirection.Out, Kilograms.Positive(blocked.QuantityKg)),
                    new StockMovementLine(blockedPosition.WithBlock(BlockState.Free), StockDirection.In,
                        Kilograms.Positive(blocked.QuantityKg)),
                ]);
            try
            {
                await StageMovementAsync(movement, metadata, recordAudit: true, cancellationToken);
            }
            catch (InventoryStoreException exception) when (exception.Code == InventoryStoreCodes.ConcurrentChange)
            {
                throw HoldError(409, InventoryPhysicalHoldCodesV1.ConcurrentChange, exception);
            }
            releaseMovementId = movement.Id;
        }

        var before = PhysicalHoldView(row, label.Version);
        if (releaseMovementId is null)
        {
            // Releasing one of several typed holds changes no kilograms, but the remaining hold set is
            // concurrency-sensitive. The label row serializes those otherwise independent hold rows.
            label.Version = checked(label.Version + 1);
            label.UpdatedAtUtc = timeProvider.GetUtcNow();
        }
        row.Status = PhysicalHoldReleased;
        row.Version = checked(row.Version + 1);
        row.ReleaseDispositionId = request.DispositionId;
        row.ReleasePayloadHash = payloadHash;
        row.ReleaseMovementId = releaseMovementId;
        row.ReleasedAtUtc = timeProvider.GetUtcNow();
        row.ReleasedBy = metadata.ActorId.Trim();
        row.ReleaseReason = parsed.Reason;
        row.ReleaseCorrelationId = metadata.CorrelationId.Trim();
        var view = PhysicalHoldView(row, label.Version);
        dbContext.Commands.Add(PhysicalHoldCommandRow(ReleasePhysicalHoldCommand, companyId, keyHash, payloadHash,
            metadata, row.Id, view));
        Audit(metadata, companyId, PhysicalHoldEntityType, row.Id, "inventory.physical-hold.released.v1", "Release",
            versionBefore: before.Version is { } version ? long.Parse(version, CultureInfo.InvariantCulture) : null,
            versionAfter: row.Version, before: JsonSerializer.Serialize(before, Json),
            after: JsonSerializer.Serialize(view, Json), parsed.Reason);

        try
        {
            await SaveAsync(cancellationToken);
        }
        catch (InventoryStoreException exception) when (exception.Code == InventoryStoreCodes.ConcurrentChange)
        {
            throw HoldError(409, InventoryPhysicalHoldCodesV1.ConcurrentChange, exception);
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return new(view, false, row.ReleaseCorrelationId);
    }

    private async Task<InventoryPhysicalHoldResultV1?> FindPhysicalHoldCommandReplayAsync(
        string command,
        string keyHash,
        string payloadHash,
        string actorId,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (prior is null)
        {
            return null;
        }
        if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal)
            || !string.Equals(prior.ActorId, actorId.Trim(), StringComparison.Ordinal))
        {
            throw HoldError(409, InventoryPhysicalHoldCodesV1.IdempotencyConflict);
        }
        var stored = JsonSerializer.Deserialize<PhysicalHoldCommandResult>(prior.ResultJson, Json)
            ?? throw HoldError(503, InventoryPhysicalHoldCodesV1.Unavailable);
        return new(stored.Hold, true, prior.CorrelationId);
    }

    private async Task<InventoryPhysicalHoldResultV1?> FindPlacementIdentityReplayAsync(
        Guid holdId,
        string payloadHash,
        string actorId,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == PlacePhysicalHoldCommand && row.TargetId == holdId,
                cancellationToken);
        if (prior is null)
        {
            return null;
        }
        if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal)
            || !string.Equals(prior.ActorId, actorId.Trim(), StringComparison.Ordinal))
        {
            throw HoldError(409, InventoryPhysicalHoldCodesV1.HoldConflict);
        }
        var stored = JsonSerializer.Deserialize<PhysicalHoldCommandResult>(prior.ResultJson, Json)
            ?? throw HoldError(503, InventoryPhysicalHoldCodesV1.Unavailable);
        return new(stored.Hold, true, prior.CorrelationId);
    }

    private InventoryCommandRow PhysicalHoldCommandRow(
        string command,
        Guid companyId,
        string keyHash,
        string payloadHash,
        InventoryCommandMetadata metadata,
        Guid holdId,
        InventoryPhysicalHoldViewV1 view) => new()
        {
            CompanyId = companyId,
            CommandName = command,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId.Trim(),
            PayloadHash = payloadHash,
            TargetId = holdId,
            ResultJson = JsonSerializer.Serialize(new PhysicalHoldCommandResult(view), Json),
            CorrelationId = metadata.CorrelationId.Trim(),
            RecordedAtUtc = timeProvider.GetUtcNow(),
        };

    private async Task<List<BalanceRow>> PositiveBalancesAsync(Guid labelId, CancellationToken cancellationToken) =>
        await dbContext.Balances.AsNoTracking()
            .Where(row => row.LabelId == labelId && row.QuantityKg > 0m)
            .ToListAsync(cancellationToken);

    private static BalanceRow SinglePhysicalBalance(IReadOnlyCollection<BalanceRow> balances, BlockState expected)
    {
        if (balances.Count != 1 || balances.Single().Block != (int)expected)
        {
            throw HoldError(409, InventoryPhysicalHoldCodesV1.PositionConflict);
        }
        return balances.Single();
    }

    private static bool SamePhysical(InventoryPhysicalHoldRow hold, BalanceRow balance) =>
        hold.LabelId == balance.LabelId
        && hold.PositionOwnerKind == balance.OwnerKind && hold.PositionOwnerId == balance.OwnerId
        && hold.CustodianKind == balance.CustodianKind && hold.CustodianId == balance.CustodianId
        && hold.LocationId == balance.LocationId && hold.ProductId == balance.ProductId
        && hold.QuantityKg == balance.QuantityKg;

    private static StockPosition StoredPosition(BalanceRow balance, LabelRow label) =>
        Position(balance.CompanyId, balance.OwnerKind, balance.OwnerId, balance.CustodianKind, balance.CustodianId,
            balance.LocationId, IdentityOnly(label), balance.Block);

    private static InventoryPhysicalHoldViewV1 PhysicalHoldView(InventoryPhysicalHoldRow row, long labelVersion) => new(
        row.Id,
        row.CompanyId,
        row.LabelId,
        labelVersion.ToString(CultureInfo.InvariantCulture),
        row.OwnerKind == QualityPhysicalHoldOwner
            ? InventoryPhysicalHoldTokensV1.QualityOwner
            : throw HoldError(503, InventoryPhysicalHoldCodesV1.Unavailable),
        row.SourceKind,
        row.SourceId,
        Kilograms.Positive(row.QuantityKg).ToString(),
        row.Status switch
        {
            PhysicalHoldActive => InventoryPhysicalHoldTokensV1.Active,
            PhysicalHoldReleased => InventoryPhysicalHoldTokensV1.Released,
            _ => throw HoldError(503, InventoryPhysicalHoldCodesV1.Unavailable),
        },
        row.Version.ToString(CultureInfo.InvariantCulture),
        row.Reason,
        row.ExpiresAtUtc,
        row.CreatedAtUtc,
        row.CreatedBy,
        row.ReleasedAtUtc,
        row.ReleasedBy,
        row.ReleaseDispositionId,
        row.ReleaseReason);

    private static (long ExpectedLabelVersion, string Reason) ParsePlacement(PlaceInventoryPhysicalHoldV1 request)
    {
        if (request.HoldId == Guid.Empty || request.LabelId == Guid.Empty || request.InspectionId == Guid.Empty
            || !TryPhysicalHoldVersion(request.ExpectedLabelVersion, out var expectedVersion)
            || !PhysicalHoldReason(request.Reason, out var reason)
            || request.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc.Offset != TimeSpan.Zero)
        {
            throw HoldError(400, InventoryPhysicalHoldCodesV1.ValidationFailed);
        }
        return (expectedVersion, reason);
    }

    private static (long ExpectedHoldVersion, long ExpectedLabelVersion, string Reason) ParseRelease(
        ReleaseInventoryPhysicalHoldV1 request)
    {
        if (request.HoldId == Guid.Empty || request.LabelId == Guid.Empty || request.DispositionId == Guid.Empty
            || !TryPhysicalHoldVersion(request.ExpectedHoldVersion, out var expectedHoldVersion)
            || !TryPhysicalHoldVersion(request.ExpectedLabelVersion, out var expectedLabelVersion)
            || !PhysicalHoldReason(request.Reason, out var reason))
        {
            throw HoldError(400, InventoryPhysicalHoldCodesV1.ValidationFailed);
        }
        return (expectedHoldVersion, expectedLabelVersion, reason);
    }

    private static bool TryPhysicalHoldVersion(string? value, out long version) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version >= 1
        && string.Equals(value, version.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static bool PhysicalHoldReason(string? value, out string reason)
    {
        reason = value?.Trim() ?? string.Empty;
        return reason.Length is > 0 and <= 1000;
    }

    private static InventoryPhysicalHoldException HoldError(int status, string code, Exception? inner = null) =>
        new(status, code, inner);

    private sealed record PhysicalHoldCommandResult(InventoryPhysicalHoldViewV1 Hold);
}
