using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.FinanceRuntime;

/// <summary>FNS-D108: the explicit credit correction accounting date selects an existing open period.
/// This does not change the manual-receipt occurredOn rule or open/reopen any period.</summary>
public sealed class FinanceCreditAccountingPeriodPortV1(IAccountingPeriodStatusPortV1 periods,
    ICompanyContext company, ICurrentUser user) : IFinanceCreditAccountingPeriodPortV1
{
    public async Task<RecordPeriodAnswer> ReadAsync(FinanceContext context, DateOnly accountingDate, CancellationToken cancellationToken)
    {
        bool Current() => company.IsResolved && company.CompanyId == context.CompanyId && user.IsAuthenticated
            && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId);
        if (accountingDate == default || !Current()) return RecordPeriodAnswer.Unavailable;
        var answer = await periods.GetStatusAsync(accountingDate, cancellationToken);
        if (!Current()) return RecordPeriodAnswer.Unavailable;
        return answer.Outcome switch
        {
            AccountingPeriodStatusOutcomeV1.Open => new(RecordPeriodOutcome.Open, null),
            AccountingPeriodStatusOutcomeV1.NotOpen or AccountingPeriodStatusOutcomeV1.None =>
                new(RecordPeriodOutcome.NotOpen, RecordPeriodAnswer.PeriodNotOpenCode),
            _ => RecordPeriodAnswer.Unavailable
        };
    }
}
