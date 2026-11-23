using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Shell;

/// <summary>
/// KIM-S19 (B-13): Finance's <see cref="IFinanceOpeningPeriodReader"/> answered from the Settings company policy registry
/// through <see cref="ISettingsFinancePolicyProviderV1"/>. It lives in the host because only the host sees both modules.
/// <list type="bullet">
/// <item><b>Never assumed open.</b> No recorded version, a refused or unreadable read, an invalid stored row or any other
/// failure answers <see cref="FinanceOpeningPeriodAnswer.NotAvailable"/>; only cancellation propagates.</item>
/// <item><b>The version changes on every open or close.</b> Each open or close is a new Settings version, and the version
/// text carries it (<see cref="SettingsFinancePolicyV1.VersionText"/>).</item>
/// </list>
/// </summary>
public sealed class SettingsFinanceOpeningPeriodReader(ISettingsFinancePolicyProviderV1 policies) : IFinanceOpeningPeriodReader
{
    public async Task<FinanceOpeningPeriodAnswer> ReadAsync(Guid companyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty)
        {
            return FinanceOpeningPeriodAnswer.NotAvailable;
        }

        SettingsFinancePolicyV1 policy;
        try
        {
            policy = await policies.GetFinancePolicyAsync(companyId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return FinanceOpeningPeriodAnswer.NotAvailable;
        }

        if (policy is null || policy.CompanyId != companyId || policy.Version <= 0)
        {
            return FinanceOpeningPeriodAnswer.NotAvailable;
        }

        return policy.OpeningPeriod switch
        {
            FinanceOpeningPeriodStateV1.Open => new FinanceOpeningPeriodAnswer(OpeningPeriodState.Open, policy.VersionText),
            FinanceOpeningPeriodStateV1.Closed => new FinanceOpeningPeriodAnswer(OpeningPeriodState.Closed, policy.VersionText),
            _ => FinanceOpeningPeriodAnswer.NotAvailable,
        };
    }
}
