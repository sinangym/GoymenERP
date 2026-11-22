using System.Globalization;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Modules.Logistics.Application;

/// <summary>
/// AI-D35 (Y-A1): the instruction list page with each instruction's shipment state, so the AI READ tool can leave out
/// instructions already turned into definite shipment without one detail read per instruction. Same permission, fences,
/// validation and store call as <see cref="GetInstructionListAsync"/>; the state comes from the stored aggregate's lines.
/// </summary>
public sealed partial class LogisticsInstructionService : ILogisticsPendingShipmentReadV1
{
    public async Task<LogisticsPendingShipmentPageV1> GetPendingShipmentPageAsync(LogisticsPendingShipmentPageQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        var pageSize = query.PageSize == 0 ? DefaultPageSize : query.PageSize;
        if (pageSize is < 1 or > MaximumPageSize || query.Cursor?.Length > MaximumCursorLength
            || (query.Cursor is not null && string.IsNullOrWhiteSpace(query.Cursor)))
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed");
        }

        if (query.Status is null
            || !Enum.TryParse<LoadingInstructionStatus>(query.Status, ignoreCase: false, out var status) || !Enum.IsDefined(status)
            || !string.Equals(status.ToString(), query.Status, StringComparison.Ordinal))
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed", [new("status", "validation_failed")]);
        }

        LogisticsInstructionPage page;
        try
        {
            page = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.ListInstructionsAsync(pageSize, query.Cursor, status, cancellationToken), cancellationToken);
        }
        catch (LogisticsStoreException exception)
        {
            throw Store(exception);
        }

        return new LogisticsPendingShipmentPageV1([.. page.Items.Select(PendingShipmentRow)], page.NextCursor);
    }

    private static LogisticsPendingShipmentRowV1 PendingShipmentRow(StoredLoadingInstruction stored)
    {
        var instruction = stored.Instruction;
        var (state, fullyShipped) = LogisticsShipmentStateV1.Of(instruction.Lines);
        return new LogisticsPendingShipmentRowV1(instruction.Id, instruction.CompanyId, instruction.Status.ToString(),
            instruction.Version.ToString(CultureInfo.InvariantCulture), instruction.CustomerPartyId, stored.EffectiveOn,
            instruction.CreatedAtUtc, instruction.Lines.Count, fullyShipped, state);
    }
}

/// <summary>AI-D35: an instruction's shipment state from its own lines (Logistics owner calculation).</summary>
public static class LogisticsShipmentStateV1
{
    /// <summary>
    /// A line is fully shipped when its shipped quantity reached the instructed quantity (an approved plan excess allows
    /// more, it does not oblige it). No lines, or differing units, give <see cref="LogisticsShipmentStatesV1.Unknown"/>.
    /// </summary>
    public static (string State, int? LinesFullyShipped) Of(IReadOnlyList<LoadingInstructionLine> lines)
    {
        if (lines.Count == 0)
        {
            return (LogisticsShipmentStatesV1.Unknown, null);
        }

        var fully = 0;
        var anyShipped = false;
        foreach (var line in lines)
        {
            if (line.ShippedQuantity.Unit != line.InstructedQuantity.Unit)
            {
                return (LogisticsShipmentStatesV1.Unknown, null);
            }

            if (!line.ShippedQuantity.IsZero)
            {
                anyShipped = true;
            }

            if (line.ShippedQuantity.CompareTo(line.InstructedQuantity) >= 0)
            {
                fully++;
            }
        }

        var state = fully == lines.Count
            ? LogisticsShipmentStatesV1.FullyShipped
            : anyShipped ? LogisticsShipmentStatesV1.PartiallyShipped : LogisticsShipmentStatesV1.NotShipped;
        return (state, fully);
    }
}
