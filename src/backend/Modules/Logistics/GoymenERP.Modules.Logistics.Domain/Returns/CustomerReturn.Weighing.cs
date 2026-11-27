using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Modules.Logistics.Domain.Returns;

/// <summary>LOJ-D22: immutable measured return facts, never a theoretical piece-to-kg conversion.</summary>
public sealed record CustomerReturnWeighing(Guid WeighingId, WeighingDirection Direction, Quantity FirstWeighing,
    Quantity SecondWeighing, Quantity Net, string RecordedBy, DateTimeOffset RecordedAtUtc);

public sealed partial class CustomerReturn
{
    public CustomerReturnWeighing? Weighing { get; private set; }

    public void RecordWeighing(long expectedVersion, Guid weighingId, WeighingDirection direction,
        Quantity first, Quantity second, string actorId, DateTimeOffset recordedAtUtc)
    {
        RequireVersion(expectedVersion);
        if (Status != CustomerReturnStatus.Pending || Weighing is not null)
            throw new CustomerReturnRuleException(CustomerReturnRuleCodesV1.NotPending, "İadeye tartım yalnız bir kez, karar öncesinde kaydedilir.");
        if (Quantity.Unit != QuantityUnit.Piece)
            throw new ArgumentException("Bu tartım komutu adetli müşteri iadesine aittir.");
        Weighing = new(Required(weighingId, nameof(weighingId)), direction, first, second,
            WeighbridgeCalculator.Net(direction, first, second), Required(actorId, nameof(actorId)), recordedAtUtc.ToUniversalTime());
        Version++;
    }
}
