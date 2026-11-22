namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// AI-D35 read (Y-A1): one page of the company's loading instructions of one exact status with each instruction's
/// shipment state, computed by the Logistics owner from its own lines (instructed vs shipped). Read-only, paged, measured
/// under <c>logistics.loading-instructions.read</c> for the server-side actor and company like the instruction list; no
/// prices, actors or free text. A separate narrow port so that the instruction service contract is unchanged.
/// </summary>
public interface ILogisticsPendingShipmentReadV1
{
    Task<LogisticsPendingShipmentPageV1> GetPendingShipmentPageAsync(LogisticsPendingShipmentPageQueryV1 query,
        CancellationToken cancellationToken = default);
}

/// <param name="Status">An exact instruction status name ("Draft" | "Issued" | "Cancelled"); required.</param>
public sealed record LogisticsPendingShipmentPageQueryV1(string Status, int PageSize = 50, string? Cursor = null);

public sealed record LogisticsPendingShipmentPageV1(IReadOnlyList<LogisticsPendingShipmentRowV1> Items, string? NextCursor);

/// <summary>
/// One instruction and its shipment state (<see cref="LogisticsShipmentStatesV1"/>). <paramref name="LinesFullyShipped"/>
/// counts the lines whose shipped quantity reached the instructed quantity; it is null when the state is unknown.
/// </summary>
public sealed record LogisticsPendingShipmentRowV1(
    Guid InstructionId,
    Guid CompanyId,
    string Status,
    string Version,
    Guid CustomerPartyId,
    DateOnly EffectiveOn,
    DateTimeOffset CreatedAtUtc,
    int LineCount,
    int? LinesFullyShipped,
    string ShipmentState);

public static class LogisticsShipmentStatesV1
{
    /// <summary>No line has a shipped quantity.</summary>
    public const string NotShipped = "NotShipped";

    /// <summary>Some quantity is shipped and at least one line still has an unshipped instructed quantity.</summary>
    public const string PartiallyShipped = "PartiallyShipped";

    /// <summary>Every line's shipped quantity reached its instructed quantity (turned into definite shipment, AI-D35).</summary>
    public const string FullyShipped = "FullyShipped";

    /// <summary>The state cannot be told (no lines, or a line whose shipped and instructed units differ): fail-closed.</summary>
    public const string Unknown = "Unknown";
}
