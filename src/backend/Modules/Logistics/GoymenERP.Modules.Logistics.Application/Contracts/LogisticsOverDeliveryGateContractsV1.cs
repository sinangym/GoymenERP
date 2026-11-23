namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// WP4 (SAT-D475): the Logistics side of the Sales over-delivery gate. Before a definite shipment the shipped total it would make
/// is judged by the Sales owner (ISalesOverDeliveryGatePortV1); in the order's approval band without an approval Sales opens an
/// over-delivery request in the shipper's name and the shipment stops until it is approved; in the block band it stops for good.
/// Logistics never writes a Sales table. A missing or failing port is a 503 that records nothing (fail-closed).
/// </summary>
public static class LogisticsOverDeliveryGateCodesV1
{
    /// <summary>The shipment would put the order line in the approval band: a request waits for its approval (terminal, recorded).</summary>
    public const string ApprovalPending = "logistics_over_delivery_approval_pending";

    /// <summary>The shipment would put the order line in the block band: never approvable (terminal, recorded).</summary>
    public const string Blocked = "logistics_over_delivery_blocked";

    /// <summary>503 (never recorded): the Sales gate port is not registered, failed or answered another question.</summary>
    public const string GateUnavailable = "logistics_over_delivery_gate_unavailable";

    /// <summary>Field of the refusal error (the order line).</summary>
    public const string ErrorField = "overDelivery";

    /// <summary>The closed 409 refusals of the gate; only these become durable terminal attempts.</summary>
    public static IReadOnlySet<string> TerminalCodes { get; } = new HashSet<string>(StringComparer.Ordinal) { ApprovalPending, Blocked };
}
