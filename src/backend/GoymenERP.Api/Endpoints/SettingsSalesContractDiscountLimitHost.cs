using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>SAT-D358: the host composes Sales' separate company limit from Settings. Absence is null; failure is never absence.</summary>
public sealed class SettingsSalesContractDiscountLimitHost(ISettingsSalesContractDiscountLimitProviderV1 provider,
    ICompanyContext company) : ISalesContractDiscountLimitSourceV1
{
    public async Task<decimal?> GetContractDiscountLimitPercentAsync(DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!company.IsResolved || company.CompanyId == Guid.Empty) throw new SalesContractDiscountLimitSourceException(isDenied: true);
        if (effectiveOn == default) throw new SalesContractDiscountLimitSourceException(isDenied: false);
        try
        {
            var answer = await provider.GetContractDiscountLimitAsync(company.CompanyId, cancellationToken);
            if (answer.CompanyId != company.CompanyId || answer.Version < 0
                || (answer.Version == 0 ? answer.LimitPercent is not null : answer.LimitPercent is null)
                || answer.LimitPercent is { } rate && (rate < 0 || rate > 100 || decimal.Round(rate, 4) != rate))
                throw new SalesContractDiscountLimitSourceException(isDenied: false);
            return answer.LimitPercent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (SalesContractDiscountLimitSourceException) { throw; }
        catch (SettingsPrerequisiteException exception)
        { throw new SalesContractDiscountLimitSourceException(exception.Code == CompanyPolicyProblemCodesV1.AccessDenied); }
        catch (Exception) { throw new SalesContractDiscountLimitSourceException(isDenied: false); }
    }
}
