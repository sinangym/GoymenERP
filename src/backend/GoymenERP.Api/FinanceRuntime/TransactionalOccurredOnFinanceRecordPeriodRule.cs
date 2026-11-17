using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.FinanceRuntime;

/// <summary>FIN-T05's date rule is unchanged: occurredOn selects the period; valueOn and the audit
/// instant do not. The existing Finance-owned transaction reader keeps Accounting's shared/range
/// locks until the real money writer commits. It grants no additional Accounting authority.</summary>
public sealed class TransactionalOccurredOnFinanceRecordPeriodRule(IFinanceCreditAccountingPeriodPortV1 periods)
    : IFinanceRecordPeriodRule
{
    public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
        DateTimeOffset recordedAtUtc, CancellationToken cancellationToken) =>
        periods.ReadAsync(context, occurredOn, cancellationToken);
}
