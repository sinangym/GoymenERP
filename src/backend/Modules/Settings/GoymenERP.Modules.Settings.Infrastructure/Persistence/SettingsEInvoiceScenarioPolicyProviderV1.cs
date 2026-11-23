using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The host's <see cref="ISettingsEInvoiceScenarioPolicyProviderV1"/> (F-029 S6, URUN_F029 spec §5.4(2)). The scenario code
/// list (SAT-D120) and the default scenario (SAT-D102) have two sources per company: until the company's one-time
/// <see cref="SalesPolicyFamiliesV1.EInvoiceScenario"/> baseline transfer the approved catalog answers exactly as before;
/// once the transfer marker exists the Settings version tables are the only source. The window rule is the catalog's for
/// this port: only a version whose window COVERS the date answers (<see cref="SalesPolicyTimelineV1.InForceOn{T}"/>);
/// otherwise <see cref="SettingsPrerequisiteCodesV1.EInvoiceScenarioCodeListNotFound"/> /
/// <see cref="SettingsPrerequisiteCodesV1.EInvoiceScenarioDefaultNotFound"/> — for every accessible company, never
/// <c>settings_provider_unavailable</c> for an empty key and never a default. A default that the list in force on the same
/// date does not carry is <c>settings_profile_invalid</c> (never served). An unreadable store is
/// <c>settings_provider_unavailable</c> (never a catalog fallback).
/// </summary>
public sealed class SettingsEInvoiceScenarioPolicyProviderV1 : ISettingsEInvoiceScenarioPolicyProviderV1
{
    private readonly SettingsDbContext _database;
    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;
    private readonly ISettingsEInvoiceScenarioPolicyProviderV1 _catalog;

    public SettingsEInvoiceScenarioPolicyProviderV1(SettingsDbContext database, ICompanyContext companyContext,
        ICurrentUser currentUser)
        : this(database, companyContext, currentUser, new ApprovedSettingsPrerequisiteProvider(companyContext, currentUser))
    {
    }

    /// <summary>Test seam: another catalog provider in place of the approved one.</summary>
    internal SettingsEInvoiceScenarioPolicyProviderV1(SettingsDbContext database, ICompanyContext companyContext,
        ICurrentUser currentUser, ISettingsEInvoiceScenarioPolicyProviderV1 catalog)
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

    public async Task<EInvoiceScenarioCodeListV1> GetEInvoiceScenarioCodesAsync(DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        RequireAccess(cancellationToken);
        var companyId = _companyContext.CompanyId;
        StoredEInvoiceScenarioCodeList[]? lists;
        try
        {
            lists = await SalesPolicyReads.IsTransferredAsync(_database, SalesPolicyFamiliesV1.EInvoiceScenario, cancellationToken)
                ? await SalesQuoteAndEInvoicePolicyReads.LoadCodeListsAsync(_database, cancellationToken)
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }

        if (lists is null)
        {
            return await _catalog.GetEInvoiceScenarioCodesAsync(effectiveOn, cancellationToken);
        }

        var entry = SalesPolicyTimelineV1.InForceOn(SalesQuoteAndEInvoicePolicyReads.CodeListTimeline(lists), effectiveOn)
                    ?? throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.EInvoiceScenarioCodeListNotFound);
        return new EInvoiceScenarioCodeListV1(
            companyId,
            new ProfileVersionV1(entry.Value.Header.ProfileId, entry.Version),
            entry.Value.Codes.ToArray(),
            entry.EffectiveFrom,
            entry.EffectiveThrough);
    }

    public async Task<EInvoiceScenarioDefaultV1> GetEInvoiceScenarioDefaultAsync(DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        RequireAccess(cancellationToken);
        var companyId = _companyContext.CompanyId;
        (StoredEInvoiceScenarioCodeList[] Lists, EInvoiceScenarioDefaultVersionRow[] Defaults)? stored;
        try
        {
            stored = await SalesPolicyReads.IsTransferredAsync(_database, SalesPolicyFamiliesV1.EInvoiceScenario, cancellationToken)
                ? (await SalesQuoteAndEInvoicePolicyReads.LoadCodeListsAsync(_database, cancellationToken),
                    await SalesQuoteAndEInvoicePolicyReads.LoadScenarioDefaultsAsync(_database, cancellationToken))
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }

        if (stored is not { } source)
        {
            return await _catalog.GetEInvoiceScenarioDefaultAsync(effectiveOn, cancellationToken);
        }

        var entry = SalesPolicyTimelineV1.InForceOn(SalesQuoteAndEInvoicePolicyReads.ScenarioDefaultTimeline(source.Defaults),
                        effectiveOn)
                    ?? throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.EInvoiceScenarioDefaultNotFound);
        if (SalesPolicyTimelineV1.InForceOn(SalesQuoteAndEInvoicePolicyReads.CodeListTimeline(source.Lists), effectiveOn)
                is not { } list
            || !list.Value.Codes.Contains(entry.Value.ScenarioCode, StringComparer.Ordinal))
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        return new EInvoiceScenarioDefaultV1(
            companyId,
            new ProfileVersionV1(entry.Value.ProfileId, entry.Version),
            entry.Value.ScenarioCode,
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
