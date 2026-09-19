namespace GoymenERP.Modules.Logistics.Domain.Loading;

/// <summary>
/// The Sales order line a loading line serves, as its identity binding (LOG-4 D4, Business
/// 5737989917 = B): the order, the line and the commercial revision the Sales owner answered when the
/// line was bound. Identities only — Logistics stores no copy of the order. Whether the line is
/// APPROVED is the Sales owner's answer at command time (<c>ISalesOrderLineReferencePortV1</c>), read
/// by the Application layer before the line is added; a later revision of the order is a later fact
/// the consumer re-reads, never a silent change of this binding.
/// </summary>
public sealed record SourceOrderLineRef(Guid OrderId, Guid LineId, int Revision)
{
    internal static SourceOrderLineRef Require(SourceOrderLineRef? source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Identity.Require(source.OrderId, nameof(OrderId));
        Identity.Require(source.LineId, nameof(LineId));
        if (source.Revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Revision), "Sipariş revizyonu negatif olamaz.");
        }

        return source;
    }
}
