using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The host's <see cref="ISettingsSalesQuotePolicyProviderV1"/> (F-029 S5, URUN_F029 spec §5.4(2)). The loss reason list
/// (SAT-D25) and the default quote validity (SAT-D39) have two sources per company: until the company's one-time
/// <see cref="SalesPolicyFamiliesV1.SalesQuotePolicy"/> baseline transfer the approved catalog
/// (<see cref="ApprovedSettingsSalesQuotePolicyProvider"/>) answers exactly as before; once the transfer marker exists the
/// Settings version tables are the only source and answer with the same shape and the same window rule
/// (<see cref="SalesPolicyTimelineV1.For{T}"/>: the version for the date, or before every start the first one, with its
/// real window), or <see cref="SettingsPrerequisiteCodesV1.SalesPolicyNotFound"/> when the key has no version. The
/// interface is unchanged. Order: cancelled → <see cref="OperationCanceledException"/>; no access →
/// <c>settings_access_denied</c>; an unreadable store → <c>settings_provider_unavailable</c> (never a catalog fallback).
/// </summary>
public sealed class SettingsSalesQuotePolicyProviderV1 : ISettingsSalesQuotePolicyProviderV1
{
    private readonly SettingsDbContext _database;
    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;
    private readonly ISettingsSalesQuotePolicyProviderV1 _catalog;

    public SettingsSalesQuotePolicyProviderV1(SettingsDbContext database, ICompanyContext companyContext, ICurrentUser currentUser)
        : this(database, companyContext, currentUser, new ApprovedSettingsSalesQuotePolicyProvider(companyContext, currentUser))
    {
    }

    /// <summary>Test seam: another catalog provider in place of the approved one.</summary>
    internal SettingsSalesQuotePolicyProviderV1(SettingsDbContext database, ICompanyContext companyContext,
        ICurrentUser currentUser, ISettingsSalesQuotePolicyProviderV1 catalog)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(catalog);
        _database = database;
        _companyContext = companyContext;
        _currentUser = currentUser;
        _catalog = catalog;
    }

    public async Task<SalesQuoteLossReasonListV1> GetSalesQuoteLossReasonsAsync(DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        RequireAccess(cancellationToken);
        var companyId = _companyContext.CompanyId;
        StoredSalesQuoteLossReasonList[]? lists;
        try
        {
            lists = await SalesPolicyReads.IsTransferredAsync(_database, SalesPolicyFamiliesV1.SalesQuotePolicy, cancellationToken)
                ? await SalesQuoteAndEInvoicePolicyReads.LoadLossReasonListsAsync(_database, cancellationToken)
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }

        if (lists is null)
        {
            return await _catalog.GetSalesQuoteLossReasonsAsync(effectiveOn, cancellationToken);
        }

        var entry = SalesPolicyTimelineV1.For(SalesQuoteAndEInvoicePolicyReads.LossReasonTimeline(lists), effectiveOn)
                    ?? throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyNotFound);
        return new SalesQuoteLossReasonListV1(
            companyId,
            new ProfileVersionV1(entry.Value.Header.ProfileId, entry.Version),
            [.. entry.Value.Entries.Select(reason => new SalesQuoteLossReasonV1(reason.ReasonId, reason.Code, reason.Label,
                reason.RequiresExplanation, reason.IsActive))],
            entry.Value.Header.IsActive,
            entry.EffectiveFrom,
            entry.EffectiveThrough);
    }

    public async Task<SalesQuoteDefaultValidityPolicyV1> GetSalesQuoteDefaultValidityAsync(DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        RequireAccess(cancellationToken);
        var companyId = _companyContext.CompanyId;
        SalesQuoteDefaultValidityVersionRow[]? rows;
        try
        {
            rows = await SalesPolicyReads.IsTransferredAsync(_database, SalesPolicyFamiliesV1.SalesQuotePolicy, cancellationToken)
                ? await SalesQuoteAndEInvoicePolicyReads.LoadDefaultValiditiesAsync(_database, cancellationToken)
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }

        if (rows is null)
        {
            return await _catalog.GetSalesQuoteDefaultValidityAsync(effectiveOn, cancellationToken);
        }

        var entry = SalesPolicyTimelineV1.For(SalesQuoteAndEInvoicePolicyReads.DefaultValidityTimeline(rows), effectiveOn)
                    ?? throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyNotFound);
        return new SalesQuoteDefaultValidityPolicyV1(
            companyId,
            new ProfileVersionV1(entry.Value.ProfileId, entry.Version),
            entry.Value.ValidityDays,
            entry.Value.IsActive,
            entry.EffectiveFrom,
            entry.EffectiveThrough);
    }

    /// <summary>Cancelled → <see cref="OperationCanceledException"/>; no access → <c>settings_access_denied</c> (the catalog's order).</summary>
    private void RequireAccess(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !_companyContext.IsResolved || _companyContext.CompanyId == Guid.Empty
            || !_currentUser.CanAccessCompany(_companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException("settings_access_denied");
        }
    }
}
