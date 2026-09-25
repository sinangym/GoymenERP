using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// Legacy synthetic fixture for tests that intentionally model a world before F-03. Runtime DI must
/// never register this fallback now that Finance owns allocation persistence; production composition
/// uses <see cref="FinanceAllocationReversalGate"/>.
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
