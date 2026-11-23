using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.FinanceRuntime;

/// <summary>FNS-D108: the explicit credit correction accounting date selects an existing open period.
/// The Accounting owner reads in Finance's Serializable transaction; its range lock lasts until Finance
/// commits or rolls back. This neither changes the receipt date rule nor opens/reopens a period.</summary>
public sealed class FinanceCreditAccountingPeriodPortV1(FinanceDbContext finance,
    ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority) : IFinanceCreditAccountingPeriodPortV1
{
    public async Task<RecordPeriodAnswer> ReadAsync(FinanceContext context, DateOnly accountingDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool Current() => context.CompanyId != Guid.Empty && !string.IsNullOrWhiteSpace(context.ActorId)
            && company.IsResolved && company.CompanyId == context.CompanyId && user.IsAuthenticated
            && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId);
        async Task<bool> Allowed()
        {
            if (!Current()) return false;
            // Preserve the same narrow owner-read authority as AccountingPeriodStatusPort. Borrowing a
            // transaction grants neither Accounting access nor permission to change its period.
            var scope = await authority.RevalidatePermissionScopeAsync(AccountingPermissionManifestV1.PeriodsRead,
                context.CompanyId, cancellationToken);
            return Current() && scope.IsGranted && scope.IsCompanyWide;
        }
        if (accountingDate == default || !await Allowed() || !finance.Database.IsRelational()
            || finance.Database.CurrentTransaction is not { } transaction) return RecordPeriodAnswer.Unavailable;
        AccountingPeriodStatusAnswerV1 answer;
        try
        {
            await using var owner = await AccountingPeriodStatusReadSession.EnlistAsync(finance.Database.GetDbConnection(),
                transaction.GetDbTransaction(), company, cancellationToken);
            if (!Current()) return RecordPeriodAnswer.Unavailable;
            answer = await owner.ReadAsync(accountingDate, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        { return RecordPeriodAnswer.Unavailable; }
        if (!await Allowed()) return RecordPeriodAnswer.Unavailable;
        return answer.Outcome switch
        {
            AccountingPeriodStatusOutcomeV1.Open => new RecordPeriodAnswer(RecordPeriodOutcome.Open, null),
            AccountingPeriodStatusOutcomeV1.NotOpen or AccountingPeriodStatusOutcomeV1.None =>
                new RecordPeriodAnswer(RecordPeriodOutcome.NotOpen, RecordPeriodAnswer.PeriodNotOpenCode),
            _ => RecordPeriodAnswer.Unavailable
        };
    }
}
