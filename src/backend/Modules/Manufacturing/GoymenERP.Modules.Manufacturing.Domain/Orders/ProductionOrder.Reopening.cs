using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>URT-D32/D56: immutable prior close header and the separate reasoned reopening that followed it.</summary>
public sealed record ProductionOrderReopeningV1(long ClosedOrderVersion, Guid ClosedByUserId, DateTimeOffset ClosedAtUtc,
    long ReopenedOrderVersion, Guid ReopenedByUserId, DateTimeOffset ReopenedAtUtc, string Reason);

public sealed partial class ProductionOrder
{
    private readonly List<ProductionOrderReopeningV1> _reopenings = [];
    public IReadOnlyList<ProductionOrderReopeningV1> Reopenings => _reopenings.AsReadOnly();

    public void RequireReopenable(long expectedVersion)
    {
        RequireVersion(expectedVersion);
        if (Status != OrderStatus.Closed)
            throw new ManufacturingRuleException("manufacturing_order_not_closed", "Yalnız kapalı üretim emri yeniden açılabilir.");
    }

    /// <summary>The application/owner transaction must first prove the period and Inventory cost fences.</summary>
    public ProductionOrderReopeningV1 Reopen(long expectedVersion, Guid reopenedByUserId, DateTimeOffset reopenedAt, string reason)
    {
        RequireReopenable(expectedVersion); RequireId(reopenedByUserId, nameof(reopenedByUserId));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > ProductionPlan.DecisionReasonMaximumLength || reason.Any(char.IsControl))
            throw new ArgumentException("Yeniden açma gerekçesi geçerli olmalıdır.", nameof(reason));
        var reopening = new ProductionOrderReopeningV1(Version, ClosedByUserId!.Value, ClosedAtUtc!.Value,
            checked(Version + 1), reopenedByUserId, reopenedAt.ToUniversalTime(), reason.Trim());
        _reopenings.Add(reopening); ClosedByUserId = null; ClosedAtUtc = null;
        ClosurePolicy = null; ClosureExceptionApproval = null; Status = OrderStatus.Open; Version = reopening.ReopenedOrderVersion;
        return reopening;
    }

    private void RestoreReopenings(IEnumerable<ProductionOrderReopeningV1> source)
    {
        long previousReopenVersion = 0;
        foreach (var reopening in source.OrderBy(item => item.ReopenedOrderVersion))
        {
            if (reopening.ClosedOrderVersion < 2 || reopening.ClosedOrderVersion <= previousReopenVersion
                || reopening.ClosedOrderVersion > Version + 1
                || reopening.ReopenedOrderVersion != reopening.ClosedOrderVersion + 1
                || reopening.ClosedByUserId == Guid.Empty || reopening.ReopenedByUserId == Guid.Empty
                || string.IsNullOrWhiteSpace(reopening.Reason) || reopening.Reason.Trim() != reopening.Reason
                || reopening.Reason.Length > ProductionPlan.DecisionReasonMaximumLength || reopening.Reason.Any(char.IsControl))
                throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid, "Kayıtlı yeniden açma zinciri geçersiz.");
            _reopenings.Add(reopening); Version = checked(Version + 2); previousReopenVersion = reopening.ReopenedOrderVersion;
        }
    }
}
