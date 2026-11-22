using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Domain.Periods;

namespace GoymenERP.Modules.Accounting.Application.Ai;

/// <summary>WP-A3c shared pieces of the Accounting READ tools.</summary>
internal static class AccountingAiReadSupport
{
    public const int PageSize = 100;

    /// <summary>
    /// The owner service reads the ambient company; the tool answers only when that is the server's AI company, before
    /// and after the read. Otherwise nothing is released.
    /// </summary>
    public static bool SameCompany(ICompanyContext company, AiServerContextV1 context) =>
        company.IsResolved && company.CompanyId == context.CompanyId;

    public static AiReadResultV1 Refusal(AccountingApiStatus status) =>
        new(status == AccountingApiStatus.Denied ? AiToolOutcomeV1.Denied : AiToolOutcomeV1.Unavailable);

    /// <summary>Every period of the company, paged through the owner read; null with a refusal when the owner refuses.</summary>
    public static async Task<(List<PeriodViewV1>? Periods, AiReadResultV1? Refusal, bool Complete)> ReadPeriodsAsync(
        IAccountingOwnerReadV1 owner, AiServerContextV1 context, int scanLimit, CancellationToken cancellationToken)
    {
        var periods = new List<PeriodViewV1>();
        string? cursor = null;
        do
        {
            var page = await owner.ListPeriodsAsync(PageSize, cursor, cancellationToken);
            if (page is not { Status: AccountingApiStatus.Ok, Resource: { } found })
            {
                return (null, Refusal(page?.Status ?? AccountingApiStatus.Unavailable), false);
            }

            foreach (var period in found.Items)
            {
                if (period is null || period.CompanyId != context.CompanyId || period.EndsOn < period.StartsOn
                    || !Enum.TryParse<AccountingPeriodStatus>(period.Status, out var status)
                    || !Enum.IsDefined(status))
                {
                    return (null, new AiReadResultV1(AiToolOutcomeV1.Unavailable), false);
                }

                periods.Add(period);
            }

            cursor = found.NextCursor;
            if (periods.Count > scanLimit)
            {
                return (periods, null, false);
            }
        }
        while (cursor is not null);

        return periods.Select(period => period.PeriodId).Distinct().Count() == periods.Count
            ? (periods, null, true)
            : (null, new AiReadResultV1(AiToolOutcomeV1.Unavailable), false);
    }
}
