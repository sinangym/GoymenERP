using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// AK-8 / AK-8b (SAT-D433, SAT-D440–SAT-D445), behind <see cref="SupplierReturnLineageGoldenV1"/>: the exit leaves the named
/// source label instead of the receipt's own label. The receipt still bounds the kilograms (SAT-D306 (a), TI3) and is the
/// movement's source.
/// <list type="bullet">
/// <item><b>Lineage, one receipt</b>: the source label's lineage is the receipt's root alone (a split of that receipt).</item>
/// <item><b>Lineage, several receipts</b> (<see cref="StageLineageAsync"/>): one exit per receipt in ONE command; Inventory
/// re-derives the SAT-D440 kilogram and SAT-D442 piece distribution from the lineage it owns and refuses any other.</item>
/// <item><b>Quantity</b>: the source label's lineage is not that receipt alone. Stock leaves at the location/product moving
/// average (SAT-D444): a readable difference is the SAT-D371 409; an unreadable average needs a manual cost approved by
/// another person (SAT-D393) and is otherwise refused 409 — no value is written without that approval.</item>
/// </list>
/// Inside every receipt the invoiced / invoice-pending part is the preparer's explicit choice (K3-B2): it is validated against
/// what is left of each part and recorded with the movement. With one open part the choice is implied; with several and no
/// choice the exit is refused 409.
/// </summary>
public sealed partial class InventorySupplierReturnExitParticipant
{
    public async Task<InventorySupplierReturnLineageExitResultV1> StageLineageAsync(
        InventorySupplierReturnLineageExitRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        Begin();
        var total = InventorySupplierReturnLineageExitContractV1.Validate(request, metadata, companyId);
        var keyHash = Hash(metadata.IdempotencyKey);
        var payloadHash = Hash(JsonSerializer.Serialize(request, Json));
        var existing = await context.Commands.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CommandName == InventorySupplierReturnExitContractV1.LineageCommandName
            && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ActorId, metadata.ActorId, StringComparison.Ordinal)
                || !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw Refused(409, InventorySupplierReturnExitCodesV1.IdempotencyConflict);
            }

            var replay = JsonSerializer.Deserialize<InventorySupplierReturnLineageExitResultV1>(existing.ResultJson, Json)
                ?? throw Refused(503, InventorySupplierReturnExitCodesV1.Unavailable);
            return replay with { IsReplay = true, OriginalCorrelationId = existing.CorrelationId };
        }

        await AcquirePhysicalValidationAsync(store.LockPhysicalValidationAsync, cancellationToken);
        if (!SupplierReturnLineageGoldenV1.IsAccepted())
            throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        var scope = new CompanyContext();
        scope.Set(companyId);
        var lineage = await new InventorySupplierReturnLineageReader(context, scope).ReadAsync(companyId, request.SourceLabelId,
            cancellationToken);
        var ordered = InventorySupplierReturnLineageDistributionV1.Match(lineage, total, request);
        var staged = new Dictionary<Guid, InventorySupplierReturnExitResultV1>();
        foreach (var part in ordered)
        {
            var single = new InventorySupplierReturnExitRequestV1(companyId, request.SupplierReturnId, part.SupplierReturnLineId,
                part.GoodsAcceptanceId, part.InventoryMovementId, request.SourceBlock, part.QuantityKg, part.OriginalUnitCostTryPerKg,
                request.OccurredAtUtc, PieceCount: part.PieceCount, SourceLabelId: request.SourceLabelId,
                LineageMode: InventorySupplierReturnLineageModeV1.Lineage, LineageSelection: part.Selection);
            var input = InventorySupplierReturnExitContractV1.Validate(single, metadata, companyId);
            staged.Add(part.GoodsAcceptanceId, await StageExitAsync(single, input, metadata, lineage, cancellationToken));
            // Each exit is flushed before the next one is checked: the next receipt's ceiling, the label's remaining stock and
            // the cost pool are read from what the earlier exits of this same return already took. Nothing is committed here.
            await SaveStagedAsync(cancellationToken);
        }

        var result = new InventorySupplierReturnLineageExitResultV1(companyId, request.SupplierReturnId, request.SourceLabelId,
            request.Parts.Select(part => staged[part.GoodsAcceptanceId]).ToArray(), IsReplay: false, metadata.CorrelationId);
        context.Commands.Add(new InventoryCommandRow
        {
            CompanyId = companyId,
            CommandName = InventorySupplierReturnExitContractV1.LineageCommandName,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = request.SupplierReturnId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = metadata.CorrelationId,
            RecordedAtUtc = clock.GetUtcNow(),
        });
        if (onStaged is not null)
        {
            await onStaged(cancellationToken);
        }

        await SaveStagedAsync(cancellationToken);
        return result;
    }

    private async Task<ReceiptTrace> LineageExitTraceAsync(ReceiptTrace receipt, InventorySupplierReturnExitRequestV1 request,
        InventorySupplierReturnExitInput input, InventorySupplierReturnLineageV1? plannedLineage, CancellationToken token)
    {
        var sourceLabelId = request.SourceLabelId!.Value;
        // The receipt-label allocation contract names positions of the receipt's own label; it is not a foreign-label choice.
        if (request.CostAllocation is not null) throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
        var mode = request.LineageMode!.Value;
        if ((request.ApprovedCostException is not null || request.SupplierReturnAmountTry is not null)
            && mode != InventorySupplierReturnLineageModeV1.Quantity)
            throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);

        var labelRow = await context.Labels.SingleOrDefaultAsync(row => row.Id == sourceLabelId, token);
        if (labelRow is null || labelRow.ProductId != receipt.ProductId)
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
        }

        InventorySupplierReturnLineageV1 lineage;
        if (plannedLineage is not null) lineage = plannedLineage;
        else
        {
            var scope = new CompanyContext();
            scope.Set(companyId);
            lineage = await new InventorySupplierReturnLineageReader(context, scope).ReadAsync(companyId, sourceLabelId, token);
        }

        if (lineage.LabelId != sourceLabelId || lineage.ProductId != receipt.ProductId)
            throw Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
        if (mode == InventorySupplierReturnLineageModeV1.Lineage)
        {
            if (plannedLineage is null) InventorySupplierReturnLineageGateV1.RequireLineageMode(lineage, receipt.LabelRow.Id);
            else if (!lineage.Roots.Any(root => root.RootLabelId == receipt.LabelRow.Id
                         && root.GoodsAcceptanceId == request.GoodsAcceptanceId && root.ReceiptMovementId == receipt.MovementId))
                throw Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
        }
        else
        {
            // SAT-D445: a lineage that is the named receipt alone is a lineage return, never a quantity return.
            if (lineage.Roots.Count == 1 && lineage.Roots[0].RootLabelId == receipt.LabelRow.Id)
                throw Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
        }

        var selection = await ResolveLineageSelectionAsync(receipt, request, input, sourceLabelId, mode, token);
        MaterialLabel label;
        try
        {
            label = await RestoreLabelAsync(labelRow, token);
        }
        catch (Exception exception) when (exception is InventoryRuleException or ArgumentException or JsonException)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        }

        var exit = receipt with { Label = label, LabelRow = labelRow };
        if (mode == InventorySupplierReturnLineageModeV1.Quantity)
        {
            if (request.SupplierReturnAmountTry is { } billedText)
            {
                var average = await RequireQuantityReturnAmountAsync(exit, input, request.SourceBlock,
                    request.ApprovedCostException == true, decimal.Parse(billedText, CultureInfo.InvariantCulture), token);
                if (average is { } known) quantityAverageCosts[request.InventoryMovementId] = known;
            }
            else
            {
                await RequireQuantityReturnAtAverageAsync(exit, input, request.SourceBlock, request.ApprovedCostException == true, token);
            }
        }

        lineageSelections[request.InventoryMovementId] = selection;
        return exit;
    }

    /// <summary>
    /// K3-B2 / TI3: the invoiced and invoice-pending kilograms this exit takes from the named receipt. The parts are read from the
    /// receipt's recognised invoices and the attribution earlier exits recorded; the preparer's explicit choice is validated
    /// against them. A history that cannot be attributed exactly is the 503 receipt-trace boundary (never an estimated part).
    /// </summary>
    private async Task<InventorySupplierReturnLineageSelectionProofV1> ResolveLineageSelectionAsync(ReceiptTrace receipt,
        InventorySupplierReturnExitRequestV1 request, InventorySupplierReturnExitInput input, Guid sourceLabelId,
        InventorySupplierReturnLineageModeV1 mode, CancellationToken token)
    {
        var (_, cohorts, _) = await InventorySupplierReturnReceiptStates.ReadCohortsAsync(context, companyId,
            request.GoodsAcceptanceId, receipt.MovementId, receipt.QuantityKg, token);
        if (cohorts is null) throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        IReadOnlyList<SupplierReturnCohortSelection>? chosen = null;
        if (request.LineageSelection is { } explicitSelection)
        {
            if (explicitSelection.Invoiced is null || explicitSelection.Invoiced.Count > 10_000
                || explicitSelection.Invoiced.Any(item => item is null || item.SourceEventId == Guid.Empty))
                throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
            chosen = [new(null, Parse(explicitSelection.PendingKg, 3)),
                .. explicitSelection.Invoiced.Select(item => new SupplierReturnCohortSelection(item.SourceEventId, Parse(item.QuantityKg, 3)))];
        }

        var choice = SupplierReturnReceiptCohorts.Choose(cohorts, input.Quantity.Value, chosen);
        return choice.Outcome switch
        {
            SupplierReturnCohortChoiceOutcome.Chosen => new InventorySupplierReturnLineageSelectionProofV1(request.GoodsAcceptanceId,
                sourceLabelId, mode, choice.Selected.Select(item => new InventorySupplierReturnResolvedPartV1(item.SourceEventId,
                    InventoryExactCostRatioV1.From(item.QuantityKg))).ToArray()),
            SupplierReturnCohortChoiceOutcome.ChoiceRequired => throw Refused(409, InventorySupplierReturnExitCodesV1.CostAllocationRequired),
            SupplierReturnCohortChoiceOutcome.LimitExceeded => throw Refused(409, InventorySupplierReturnExitCodesV1.ExitExceedsReceipt),
            _ => throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest),
        };
    }

    /// <summary>
    /// SAT-D456 (with SAT-D444): the caller names the kuruş-rounded amount its document bills. With a readable moving average the
    /// stock leaves at that average and the difference is the kuruş-rounded exit amount minus the billed amount; any difference
    /// is refused (SAT-D371; an approved exception does not bypass it, SAT-D410) and the average is returned as the exit's unit
    /// cost. With an unreadable average only a manual cost approved by another person (SAT-D393) lets the exit proceed, at that
    /// cost (null is returned); the SAT-D306 (c) pool check still applies afterwards.
    /// </summary>
    private async Task<decimal?> RequireQuantityReturnAmountAsync(ReceiptTrace exit, InventorySupplierReturnExitInput input,
        BlockState block, bool approvedCostException, decimal billedAmountTry, CancellationToken token)
    {
        if (exit.OwnerKind != StockPartyKind.Company || exit.OwnerId != companyId)
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
        }

        var source = await SourcePositionAsync(exit, block, token);
        var average = await InventorySupplierReturnReceiptStates.ReadAverageAsync(context, companyId, exit.ProductId,
            source.Position.LocationId, token);
        if (average is not { } known)
        {
            if (!approvedCostException || input.OriginalUnitCostTryPerKg is null)
                throw Refused(409, InventorySupplierReturnExitCodesV1.QuantityCostUnreadable);
            return null;
        }

        // A manual cost has no place while the average is readable (SAT-D454); the caller sends none.
        if (input.OriginalUnitCostTryPerKg is not null || approvedCostException)
            throw Refused(409, InventorySupplierReturnExitCodesV1.CostDifferenceUnposted);
        var (_, difference) = SupplierReturnLineageCalculator.QuantityReturnAmountDifference(input.Quantity.Value, known, billedAmountTry);
        if (difference != 0m) throw Refused(409, InventorySupplierReturnExitCodesV1.CostDifferenceUnposted);
        return known;
    }

    /// <summary>
    /// SAT-D444/TI8: the quantity return leaves at the moving average and bills the chosen order's unit price. When the average
    /// and the chosen receipt's own cost are both readable, all three must be the same value or the difference is refused
    /// (SAT-D371; an approved exception does not bypass it, SAT-D410). When one of them cannot be read, only a manual cost
    /// approved by another person (SAT-D393) lets the exit proceed; the SAT-D306 (c) pool check still applies afterwards.
    /// </summary>
    private async Task RequireQuantityReturnAtAverageAsync(ReceiptTrace exit, InventorySupplierReturnExitInput input,
        BlockState block, bool approvedCostException, CancellationToken token)
    {
        if (exit.OwnerKind != StockPartyKind.Company || exit.OwnerId != companyId)
        {
            // A supplier return bills this company's purchase; stock of another owner has no company cost pool to leave.
            throw Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
        }

        if (input.OriginalUnitCostTryPerKg is not { } price)
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.QuantityCostUnreadable);
        }

        var source = await SourcePositionAsync(exit, block, token);
        var average = await InventorySupplierReturnReceiptStates.ReadAverageAsync(context, companyId, exit.ProductId,
            source.Position.LocationId, token);
        var stamp = await context.MovementCostStamps.AsNoTracking().SingleOrDefaultAsync(item => item.MovementId == exit.MovementId
            && item.LineNumber == 1 && item.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira, token);
        var receiptCost = stamp is { Status: (int)InventoryMovementCostStampStatusV1.Known } ? stamp.UnitCostPerKg : null;
        if (average is not { } known || receiptCost is not { } receiptUnit)
        {
            if (!approvedCostException) throw Refused(409, InventorySupplierReturnExitCodesV1.QuantityCostUnreadable);
            return;
        }

        var result = SupplierReturnLineageCalculator.QuantityReturn(input.Quantity.Value, decimal.MaxValue, known, price);
        if (result.Outcome == SupplierReturnLineageOutcome.CostDifference || receiptUnit != known)
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.CostDifferenceUnposted);
        }
    }
}
