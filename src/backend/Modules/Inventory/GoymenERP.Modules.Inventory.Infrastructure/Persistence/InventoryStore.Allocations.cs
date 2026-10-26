using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryStore
{
    internal const string CreatePhysicalAllocationCommand = "inventory.physical-allocation.create.v1";
    internal const string ReleasePhysicalAllocationCommand = "inventory.physical-allocation.release.v1";

    public async Task<InventoryPhysicalAllocationResultV1> CreateAsync(
        CreatePhysicalSalesAllocationV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMetadata(metadata);
        RequireAllocationMetadata(metadata);
        RequireResolvedCompany();
        cancellationToken.ThrowIfCancellationRequested();

        var companyId = companyContext.CompanyId;
        var parsed = ParseCreate(request);
        var keyHash = AllocationKeyHash(metadata.IdempotencyKey);
        var payloadHash = AllocationPayloadHash(new
        {
            request.AllocationId,
            request.SalesOrderId,
            request.SalesOrderLineId,
            request.ProductId,
            request.ProductVersion,
            request.LabelId,
            expectedLabelVersion = parsed.ExpectedLabelVersion,
            quantityKg = parsed.Quantity.ToString(),
            request.Reason,
            request.ReservedUntilUtc,
        });
        if (await FindAllocationReplayAsync(CreatePhysicalAllocationCommand, keyHash, payloadHash, metadata.ActorId,
                cancellationToken) is { } replay)
        {
            return replay;
        }

        var label = await dbContext.Labels.SingleOrDefaultAsync(row => row.Id == request.LabelId, cancellationToken)
            ?? throw AllocationError(404, InventoryAllocationCodesV1.InvalidReference);
        if (label.Version != parsed.ExpectedLabelVersion)
        {
            throw AllocationError(409, InventoryAllocationCodesV1.LabelVersionConflict);
        }

        if (label.ProductId != request.ProductId)
        {
            throw AllocationError(422, InventoryAllocationCodesV1.InvalidReference);
        }

        var freeRows = await dbContext.Balances
            .Where(row => row.LabelId == label.Id && row.Block == (int)BlockState.Free && row.QuantityKg > 0m)
            .ToListAsync(cancellationToken);
        if (freeRows.Count != 1)
        {
            throw AllocationError(freeRows.Count == 0 ? 409 : 503,
                freeRows.Count == 0 ? InventoryAllocationCodesV1.InsufficientFreeStock : InventoryAllocationCodesV1.InvalidReference);
        }

        var source = freeRows[0];
        // Sales-sourced reservation of another party's goods is refused (#479 review P2-2); no fason policy.
        // TODO(#66/5830378162): replace with the accepted customer-owned/fason rule once decided.
        if (!InventorySalesOwnershipGuardV1.IsCompanyOwnedAndHeld((StockPartyKind)source.OwnerKind, source.OwnerId,
                (StockPartyKind)source.CustodianKind, source.CustodianId, companyId))
        {
            throw AllocationError(422, InventoryAllocationCodesV1.OwnerNotCompany);
        }

        var activeAllocated = await dbContext.Allocations.AsNoTracking()
            .Where(row => row.LabelId == label.Id
                && row.OwnerKind == source.OwnerKind && row.OwnerId == source.OwnerId
                && row.CustodianKind == source.CustodianKind && row.CustodianId == source.CustodianId
                && row.LocationId == source.LocationId && row.Block == (int)BlockState.Free
                && row.Status == (int)StockAllocationStatus.Active)
            .SumAsync(row => (decimal?)row.RemainingKg, cancellationToken) ?? 0m;
        if (parsed.Quantity.Value > source.QuantityKg - activeAllocated)
        {
            throw AllocationError(409, InventoryAllocationCodesV1.InsufficientFreeStock);
        }

        var identity = new MaterialLabel(label.Id, label.CompanyId, label.Code, label.ProductId, label.LotId,
            label.ProductVersion);
        var position = new StockPosition(companyId,
            new StockOwner((StockPartyKind)source.OwnerKind, source.OwnerId),
            new StockCustodian((StockPartyKind)source.CustodianKind, source.CustodianId),
            source.LocationId, identity, BlockState.Free);
        var allocation = StockAllocation.Create(request.AllocationId, position, parsed.Quantity);
        var now = timeProvider.GetUtcNow();
        var row = new AllocationRow
        {
            Id = allocation.Id,
            CompanyId = companyId,
            OwnerKind = (int)position.Owner.Kind,
            OwnerId = position.Owner.Id,
            CustodianKind = (int)position.Custodian.Kind,
            CustodianId = position.Custodian.Id,
            LocationId = position.LocationId,
            LabelId = position.LabelId,
            Block = (int)position.Block,
            DemandKind = InventoryAllocationDemandKindsV1.SalesOrderLine,
            DemandId = request.SalesOrderId,
            DemandLineId = request.SalesOrderLineId,
            DemandProductId = request.ProductId,
            DemandProductVersion = request.ProductVersion,
            Reason = request.Reason,
            ReservedUntilUtc = request.ReservedUntilUtc,
            AllocatedKg = allocation.Allocated.Value,
            RemainingKg = allocation.Remaining.Value,
            Status = (int)allocation.Status,
            Version = allocation.Version,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.Allocations.Add(row);
        label.Version = checked(label.Version + 1);
        label.UpdatedAtUtc = now;
        var view = AllocationView(row);
        dbContext.Commands.Add(AllocationCommandRow(companyId, CreatePhysicalAllocationCommand, keyHash, payloadHash,
            metadata, row.Id, view));
        Audit(metadata, companyId, "Inventory.StockAllocation", row.Id,
            "inventory.physical-allocation.created.v1", "Create", null, 1, "{}",
            JsonSerializer.Serialize(view, Json), request.Reason);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            if (await FindAllocationReplayAsync(CreatePhysicalAllocationCommand, keyHash, payloadHash, metadata.ActorId,
                    cancellationToken) is { } winner)
            {
                return winner;
            }

            throw AllocationError(409, InventoryAllocationCodesV1.ConcurrentChange, exception);
        }

        return new(view, false, metadata.CorrelationId.Trim());
    }

    public async Task<InventoryPhysicalAllocationResultV1> ReleaseAsync(
        Guid allocationId,
        ReleasePhysicalAllocationV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMetadata(metadata);
        RequireAllocationMetadata(metadata);
        RequireResolvedCompany();
        cancellationToken.ThrowIfCancellationRequested();
        if (allocationId == Guid.Empty || !TryVersion(request.ExpectedAllocationVersion, out var expectedVersion)
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000)
        {
            throw AllocationError(400, InventoryAllocationCodesV1.ValidationFailed);
        }

        var companyId = companyContext.CompanyId;
        var keyHash = AllocationKeyHash(metadata.IdempotencyKey);
        var payloadHash = AllocationPayloadHash(new { allocationId, expectedVersion, request.Reason });
        if (await FindAllocationReplayAsync(ReleasePhysicalAllocationCommand, keyHash, payloadHash, metadata.ActorId,
                cancellationToken) is { } replay)
        {
            return replay;
        }

        var row = await dbContext.Allocations.SingleOrDefaultAsync(candidate => candidate.Id == allocationId, cancellationToken)
            ?? throw AllocationError(404, InventoryAllocationCodesV1.InvalidReference);
        if (!string.Equals(row.DemandKind, InventoryAllocationDemandKindsV1.SalesOrderLine, StringComparison.Ordinal))
        {
            throw AllocationError(422, InventoryAllocationCodesV1.InvalidReference);
        }
        if (await dbContext.PhysicalHolds.AsNoTracking().AnyAsync(hold => hold.LabelId == row.LabelId
                && hold.Status == PhysicalHoldActive, cancellationToken))
        {
            throw AllocationError(409, InventoryAllocationCodesV1.AllocationStateConflict);
        }

        var label = await dbContext.Labels.SingleOrDefaultAsync(candidate => candidate.Id == row.LabelId, cancellationToken)
            ?? throw new InvalidOperationException("Kayıtlı tahsisin etiketi bulunamadı.");
        var identity = new MaterialLabel(label.Id, label.CompanyId, label.Code, label.ProductId, label.LotId,
            label.ProductVersion);
        var position = new StockPosition(companyId,
            new StockOwner((StockPartyKind)row.OwnerKind, row.OwnerId),
            new StockCustodian((StockPartyKind)row.CustodianKind, row.CustodianId),
            row.LocationId, identity, (BlockState)row.Block);
        var allocation = StockAllocation.Restore(row.Id, position, Kilograms.Positive(row.AllocatedKg),
            Kilograms.Of(row.RemainingKg), row.Version, (StockAllocationStatus)row.Status);
        try
        {
            allocation.Release(expectedVersion);
        }
        catch (StockAllocationException exception)
        {
            throw exception.Failure switch
            {
                StockAllocationFailure.VersionConflict => AllocationError(409, InventoryAllocationCodesV1.AllocationVersionConflict),
                StockAllocationFailure.StateConflict => AllocationError(409, InventoryAllocationCodesV1.AllocationStateConflict),
                _ => new InvalidOperationException("Tahsis serbest bırakma reddi eşlenemedi.", exception),
            };
        }

        var now = timeProvider.GetUtcNow();
        var before = AllocationView(row);
        row.RemainingKg = allocation.Remaining.Value;
        row.Status = (int)allocation.Status;
        row.Version = allocation.Version;
        row.UpdatedAtUtc = now;
        row.ReleasedAtUtc = now;
        row.ReleaseReason = request.Reason;
        label.Version = checked(label.Version + 1);
        label.UpdatedAtUtc = now;
        var after = AllocationView(row);
        dbContext.Commands.Add(AllocationCommandRow(companyId, ReleasePhysicalAllocationCommand, keyHash, payloadHash,
            metadata, row.Id, after));
        Audit(metadata, companyId, "Inventory.StockAllocation", row.Id,
            "inventory.physical-allocation.released.v1", "Release", expectedVersion, row.Version,
            JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(after, Json), request.Reason);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            if (await FindAllocationReplayAsync(ReleasePhysicalAllocationCommand, keyHash, payloadHash, metadata.ActorId,
                    cancellationToken) is { } winner)
            {
                return winner;
            }

            throw AllocationError(409, InventoryAllocationCodesV1.ConcurrentChange, exception);
        }

        return new(after, false, metadata.CorrelationId.Trim());
    }

    private static (long ExpectedLabelVersion, Kilograms Quantity) ParseCreate(CreatePhysicalSalesAllocationV1 request)
    {
        if (request.AllocationId == Guid.Empty || request.SalesOrderId == Guid.Empty || request.SalesOrderLineId == Guid.Empty
            || request.ProductId == Guid.Empty || request.LabelId == Guid.Empty
            || !TryVersion(request.ExpectedLabelVersion, out var version)
            || string.IsNullOrWhiteSpace(request.ProductVersion) || request.ProductVersion.Length > 128
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000
            || request.ReservedUntilUtc is { } reservedUntil && reservedUntil.Offset != TimeSpan.Zero
            || !TryQuantity(request.QuantityKg, out var quantity))
        {
            throw AllocationError(400, InventoryAllocationCodesV1.ValidationFailed);
        }

        return (version, quantity);
    }

    private async Task<InventoryPhysicalAllocationResultV1?> FindAllocationReplayAsync(
        string command,
        string keyHash,
        string payloadHash,
        string actorId,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal)
            || !string.Equals(prior.ActorId, actorId, StringComparison.Ordinal))
        {
            throw AllocationError(409, InventoryAllocationCodesV1.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<AllocationCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı tahsis komut sonucu okunamadı.");
        return new(stored.Allocation, true, prior.CorrelationId);
    }

    private InventoryCommandRow AllocationCommandRow(
        Guid companyId,
        string command,
        string keyHash,
        string payloadHash,
        InventoryCommandMetadata metadata,
        Guid targetId,
        InventoryPhysicalAllocationViewV1 view) => new()
        {
            CompanyId = companyId,
            CommandName = command,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = targetId,
            ResultJson = JsonSerializer.Serialize(new AllocationCommandResult(view), Json),
            CorrelationId = metadata.CorrelationId.Trim(),
            RecordedAtUtc = timeProvider.GetUtcNow(),
        };

    private static InventoryPhysicalAllocationViewV1 AllocationView(AllocationRow row)
    {
        if (row.DemandKind is null || row.DemandId is null || row.DemandLineId is null
            || row.DemandProductId is null || row.DemandProductVersion is null || row.Reason is null)
        {
            throw new InvalidOperationException("Eski tahsis satırı fiziksel satış tahsis görünümüne dönüştürülemez.");
        }

        return new InventoryPhysicalAllocationViewV1(
            row.Id,
            row.CompanyId,
            row.Version.ToString(CultureInfo.InvariantCulture),
            ((StockAllocationStatus)row.Status).ToString(),
            row.LabelId,
            row.DemandProductId.Value,
            row.DemandKind,
            row.DemandId.Value,
            row.DemandLineId.Value,
            row.DemandProductVersion,
            Kilograms.Of(row.AllocatedKg).ToString(),
            Kilograms.Of(row.RemainingKg).ToString(),
            row.Reason,
            row.ReservedUntilUtc,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);
    }

    private static string AllocationKeyHash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim())));

    private static void RequireAllocationMetadata(InventoryCommandMetadata metadata)
    {
        if (!string.Equals(metadata.IdempotencyKey, metadata.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || metadata.IdempotencyKey.Length > 128 || metadata.IdempotencyKey.Any(char.IsControl)
            || !string.Equals(metadata.CorrelationId, metadata.CorrelationId.Trim(), StringComparison.Ordinal)
            || metadata.CorrelationId.Length > 128)
        {
            throw AllocationError(400, InventoryAllocationCodesV1.ValidationFailed);
        }
    }

    private static string AllocationPayloadHash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))))
            .ToLowerInvariant();

    private static bool TryVersion(string? value, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(value) && value.All(char.IsAsciiDigit)
            && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version > 0;
    }

    private static bool TryQuantity(string? value, out Kilograms quantity)
    {
        quantity = Kilograms.Zero;
        if (string.IsNullOrEmpty(value) || value.Length > 40
            || value.Count(character => character == '.') > 1
            || value.Any(character => !char.IsAsciiDigit(character) && character != '.')
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0m || ((decimal.GetBits(parsed)[3] >> 16) & 0xFF) > 3)
        {
            return false;
        }

        quantity = Kilograms.Positive(parsed);
        return true;
    }

    private static InventoryAllocationException AllocationError(int status, string code, Exception? inner = null) =>
        new(status, code, innerException: inner);

    private sealed record AllocationCommandResult(InventoryPhysicalAllocationViewV1 Allocation);
}
