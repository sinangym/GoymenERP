using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

internal sealed class InventoryGoodsReceiptSourceService(
    InventoryDbContext context,
    ICompanyContext companyContext) : IInventoryGoodsReceiptSourceV1
{
    public Task<InventoryGoodsReceiptSourceV1?> ReadAsync(Guid acceptanceId,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
        }

        if (acceptanceId == Guid.Empty)
        {
            throw InventoryGoodsReceiptException.Refused(400, InventoryGoodsReceiptCodesV1.InvalidRequest);
        }

        return InventoryGoodsReceiptSourceProjection.ReadAsync(
            context, companyContext.CompanyId, acceptanceId, cancellationToken);
    }
}

internal static class InventoryGoodsReceiptSourceProjection
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public static async Task<InventoryGoodsReceiptSourceV1?> ReadAsync(
        InventoryDbContext context,
        Guid companyId,
        Guid acceptanceId,
        CancellationToken cancellationToken)
    {
        var commands = await context.Commands.AsNoTracking()
            .Where(row => row.CommandName == InventoryGoodsReceiptContractV1.CommandName
                && row.TargetId == acceptanceId)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (commands.Count == 0) return null;
        if (commands.Count != 1) throw Unavailable();
        return await RestoreAsync(context, companyId, acceptanceId, commands[0], cancellationToken);
    }

    public static async Task<InventoryGoodsReceiptSourceV1?> ReadReplayAsync(
        InventoryDbContext context,
        Guid companyId,
        Guid acceptanceId,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        InventoryGoodsReceiptContractV1.ValidateReplayLookup(acceptanceId, metadata, companyId);
        var keyHash = Hash(metadata.IdempotencyKey);
        var command = await context.Commands.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CommandName == InventoryGoodsReceiptContractV1.CommandName
            && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (command is null) return null;
        if (command.TargetId != acceptanceId
            || !string.Equals(command.ActorId, metadata.ActorId, StringComparison.Ordinal))
        {
            throw InventoryGoodsReceiptException.Refused(
                409, InventoryGoodsReceiptCodesV1.IdempotencyConflict);
        }

        return await RestoreAsync(context, companyId, acceptanceId, command, cancellationToken);
    }

    private static async Task<InventoryGoodsReceiptSourceV1> RestoreAsync(
        InventoryDbContext context,
        Guid companyId,
        Guid acceptanceId,
        InventoryCommandRow command,
        CancellationToken cancellationToken)
    {
        InventoryGoodsReceiptResultV1 result;
        try
        {
            result = JsonSerializer.Deserialize<InventoryGoodsReceiptResultV1>(command.ResultJson, Json)
                ?? throw Unavailable();
        }
        catch (JsonException)
        {
            throw Unavailable();
        }

        var movements = await context.Movements.AsNoTracking()
            .Where(row => row.Kind == (int)StockMovementKind.Receipt
                && row.SourceKind == InventoryGoodsReceiptContractV1.SourceKind
                && row.SourceId == acceptanceId)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (movements.Count != 1) throw Unavailable();
        var movement = movements[0];

        var lines = await context.MovementLines.AsNoTracking()
            .Where(row => row.MovementId == movement.Id)
            .OrderBy(row => row.LineNumber)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (lines.Count != 1) throw Unavailable();
        var line = lines[0];

        var label = await context.Labels.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == line.LabelId, cancellationToken);
        if (label is null) throw Unavailable();
        var lot = await context.Lots.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == label.LotId, cancellationToken);
        if (lot is null) throw Unavailable();

        string[] requiredDocuments;
        try
        {
            requiredDocuments = JsonSerializer.Deserialize<string[]>(label.RequiredDocumentsJson, Json)
                ?? throw Unavailable();
        }
        catch (JsonException)
        {
            throw Unavailable();
        }

        if (result.CompanyId != companyId || result.AcceptanceId != acceptanceId
            || result.PurchaseOrderId == Guid.Empty || result.PurchaseOrderLineId == Guid.Empty
            || result.ProductId == Guid.Empty || result.PurchaseUnitId == Guid.Empty
            || result.LotId != lot.Id || result.LabelId != label.Id || result.MovementId != movement.Id
            || result.ProductId != lot.ProductId || result.ProductId != label.ProductId
            || result.ProductId != line.ProductId || result.IsReplay
            || !string.Equals(result.OriginalCorrelationId, command.CorrelationId, StringComparison.Ordinal)
            || movement.SourceLineId is not null || movement.SourceGrain != (int)StockSourceGrain.Whole
            || movement.OccurredAtUtc.Offset != TimeSpan.Zero
            || line.LineNumber != 1 || line.Direction != (int)StockDirection.In
            || !Enum.IsDefined((StockPartyKind)line.OwnerKind)
            || !Enum.IsDefined((StockPartyKind)line.CustodianKind)
            || !Enum.IsDefined((BlockState)line.Block)
            || line.OwnerId == Guid.Empty || line.CustodianId == Guid.Empty || line.LocationId == Guid.Empty
            || !string.Equals(label.HeatNumber, lot.HeatNumber, StringComparison.Ordinal)
            || !string.Equals(label.CoilNumber, lot.CoilNumber, StringComparison.Ordinal)
            || !long.TryParse(result.LabelVersion, NumberStyles.None, CultureInfo.InvariantCulture,
                out var originalLabelVersion) || originalLabelVersion < 1 || label.Version < originalLabelVersion)
        {
            throw Unavailable();
        }

        string quantity;
        try
        {
            quantity = Kilograms.Positive(line.QuantityKg).ToString();
        }
        catch (Exception exception) when (exception is ArgumentException or InventoryRuleException)
        {
            throw Unavailable();
        }

        var request = new InventoryGoodsReceiptRequestV1(
            companyId,
            acceptanceId,
            result.PurchaseOrderId,
            result.PurchaseOrderLineId,
            result.PurchaseOrderVersion,
            result.ProductId,
            result.ProductVersion,
            result.PurchaseUnitId,
            lot.Id,
            label.Id,
            label.Code,
            label.HeatNumber,
            label.CoilNumber,
            requiredDocuments.Order(StringComparer.Ordinal).ToArray(),
            (StockPartyKind)line.OwnerKind,
            line.OwnerId,
            (StockPartyKind)line.CustodianKind,
            line.CustodianId,
            line.LocationId,
            (BlockState)line.Block,
            movement.Id,
            quantity,
            movement.OccurredAtUtc);
        try
        {
            InventoryGoodsReceiptContractV1.ValidateRequest(request, companyId);
        }
        catch (InventoryGoodsReceiptException)
        {
            throw Unavailable();
        }

        if (!string.Equals(result.QuantityKg, request.QuantityKg, StringComparison.Ordinal))
        {
            throw Unavailable();
        }

        return new InventoryGoodsReceiptSourceV1(request, result);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToUpperInvariant();

    private static InventoryGoodsReceiptException Unavailable() =>
        InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
}
