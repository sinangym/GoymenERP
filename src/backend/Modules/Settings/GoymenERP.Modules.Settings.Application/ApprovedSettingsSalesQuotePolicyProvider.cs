using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// The real Settings provider of the Sales quote policies (SAT-D25, SAT-D39), serving
/// <see cref="ApprovedSalesQuotePolicyCatalog"/> in the order of <see cref="ISettingsSalesQuotePolicyProviderV1"/>:
/// cancellation → access (<c>settings_access_denied</c>, before anything about the source is revealed) → a company
/// without an approved Sales policy source (<c>settings_provider_unavailable</c>) → no record of the family
/// (<see cref="SettingsPrerequisiteCodesV1.SalesPolicyNotFound"/>) → the exact version for the date with its real
/// window. The company is never a request input; an answer authorizes nothing.
/// </summary>
public sealed class ApprovedSettingsSalesQuotePolicyProvider : ISettingsSalesQuotePolicyProviderV1
{
    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;
    private readonly ApprovedSalesQuotePolicyCatalog _catalog;

    /// <summary>The only constructor the host can reach: it always serves <see cref="ApprovedSalesQuotePolicyCatalog.Approved"/>.</summary>
    public ApprovedSettingsSalesQuotePolicyProvider(ICompanyContext companyContext, ICurrentUser currentUser)
        : this(companyContext, currentUser, ApprovedSalesQuotePolicyCatalog.Approved)
    {
    }

    /// <summary>Test seam, internal on purpose: no other module can build an "approved" provider over made-up records.</summary>
    internal ApprovedSettingsSalesQuotePolicyProvider(ICompanyContext companyContext, ICurrentUser currentUser,
        ApprovedSalesQuotePolicyCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(catalog);
        _companyContext = companyContext;
        _currentUser = currentUser;
        _catalog = catalog;
    }

    public Task<SalesQuoteLossReasonListV1> GetSalesQuoteLossReasonsAsync(DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        if (Refusal<SalesQuoteLossReasonListV1>(cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var entry = _catalog.LossReasonsFor(_companyContext.CompanyId, effectiveOn);
        return entry is null
            ? NotFound<SalesQuoteLossReasonListV1>()
            : Task.FromResult(new SalesQuoteLossReasonListV1(
                entry.CompanyId,
                new ProfileVersionV1(entry.ProfileId, entry.Version),
                [.. entry.Reasons.Select(reason => new SalesQuoteLossReasonV1(reason.ReasonId, reason.Code, reason.Label,
                    reason.RequiresExplanation, reason.IsActive))],
                entry.IsActive,
                entry.EffectiveFrom,
                entry.EffectiveThrough));
    }

    public Task<SalesQuoteDefaultValidityPolicyV1> GetSalesQuoteDefaultValidityAsync(DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        if (Refusal<SalesQuoteDefaultValidityPolicyV1>(cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var entry = _catalog.DefaultValidityFor(_companyContext.CompanyId, effectiveOn);
        return entry is null
            ? NotFound<SalesQuoteDefaultValidityPolicyV1>()
            : Task.FromResult(new SalesQuoteDefaultValidityPolicyV1(
                entry.CompanyId,
                new ProfileVersionV1(entry.ProfileId, entry.Version),
                entry.ValidityDays,
                entry.IsActive,
                entry.EffectiveFrom,
                entry.EffectiveThrough));
    }

    private Task<T>? Refusal<T>(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !_companyContext.IsResolved || _companyContext.CompanyId == Guid.Empty
            || !_currentUser.CanAccessCompany(_companyContext.CompanyId))
        {
            return Task.FromException<T>(new SettingsPrerequisiteException("settings_access_denied"));
        }

        return _catalog.Serves(_companyContext.CompanyId)
            ? null
            : Task.FromException<T>(new SettingsPrerequisiteException("settings_provider_unavailable"));
    }

    private static Task<T> NotFound<T>() =>
        Task.FromException<T>(new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyNotFound));
}
