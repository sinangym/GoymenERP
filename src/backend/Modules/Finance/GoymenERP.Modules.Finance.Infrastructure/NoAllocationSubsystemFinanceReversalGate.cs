using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// The current modular monolith has no allocation persistence or allocation command surface, so an
/// allocation cannot exist. This narrowly states that physical fact; it is not an "assume clear"
/// fallback. When an allocation owner is introduced, composition must replace this registration with
/// its real company-filtered reader before that owner can write allocations.
/// </summary>
public sealed class NoAllocationSubsystemFinanceReversalGate : IFinanceReversalAllocationGate
{
    public Task<ReversalAllocationAnswer> CheckAsync(
        FinanceContext context,
        Guid originalSourceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(originalSourceId == Guid.Empty
            ? new ReversalAllocationAnswer(ReversalAllocationOutcome.Unavailable, "finance_source_identity_required")
            : new ReversalAllocationAnswer(ReversalAllocationOutcome.NoAllocation));
    }
}
