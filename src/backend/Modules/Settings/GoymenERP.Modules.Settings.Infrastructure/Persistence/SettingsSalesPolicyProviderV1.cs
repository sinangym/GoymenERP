using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The host's <see cref="ISettingsSalesPolicyProviderV1"/>. The delivery tolerance (SAT-D5) and the amount threshold (SAT-D6)
/// have two sources per company and family (F-029 S4b, URUN_F029 spec §5.4(1)): until the family's one-time baseline transfer
/// the approved catalog (<see cref="ApprovedSalesPolicyCatalog"/>) answers exactly as before; once the transfer marker exists
/// the Settings version table is the only source and answers with the same shape and the same window rule
/// (<see cref="SalesPolicyTimelineV1"/>): the version for the date with its real window, or
/// <see cref="SettingsPrerequisiteCodesV1.SalesPolicyNotFound"/> when the key has no version. The interface is unchanged.
/// The discount limit (SAT-D2, SAT-D36, SAT-D61…D64) has two sources as well.
/// <list type="number">
/// <item>cancelled → <see cref="OperationCanceledException"/>; no access → <c>settings_access_denied</c>; an empty group id →
/// <see cref="SettingsPrerequisiteCodesV1.SalesPolicyRequestInvalid"/> (the catalog's order).</item>
/// <item>A group the company's limit-group STORE does not define → the approved catalog answers, unchanged (today: "not
/// found" for every group of GDC/GSP/GIN, <c>settings_provider_unavailable</c> elsewhere).</item>
/// <item>A group the store defines: if the approved catalog ALSO has an entry for that identity the sources contradict each
/// other → <c>settings_profile_invalid</c> (fail-closed, 503 at Sales; group ids are server-minted, so this means corrupt
/// data, never a precedence to guess). Otherwise <see cref="SalesDiscountLimitCalculatorV1"/> answers for the date: a
/// limit in force → its exact record (profile id + version) and the window the value holds over (a passive group still
/// counts, SAT-D117); none (no
/// record in force, linked group without a limit) → <see cref="SettingsPrerequisiteCodesV1.SalesPolicyNotFound"/>, which
/// Sales reads as "this group contributes nothing" (SAT-D34); contradictory records (a cycle) →
/// <c>settings_profile_invalid</c>.</item>
/// <item>The store cannot be read → <c>settings_provider_unavailable</c> (503 at Sales, as before).</item>
/// </list>
/// Unlike the catalog, a date before the group's first record answers "not found" rather than the first record with its
/// window: a linked limit has no value outside its window. Sales treats both the same way (contributes nothing).
/// </summary>
public sealed class SettingsSalesPolicyProviderV1 : ISettingsSalesPolicyProviderV1
{
    private readonly SettingsDbContext _database;
    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;
    private readonly ISettingsSalesPolicyProviderV1 _catalog;

    public SettingsSalesPolicyProviderV1(SettingsDbContext database, ICompanyContext companyContext, ICurrentUser currentUser)
        : this(database, companyContext, currentUser, new ApprovedSettingsPrerequisiteProvider(companyContext, currentUser))
    {
    }

    /// <summary>Test seam: a synthetic catalog in place of the approved one.</summary>
    internal SettingsSalesPolicyProviderV1(SettingsDbContext database, ICompanyContext companyContext,
        ICurrentUser currentUser, ISettingsSalesPolicyProviderV1 catalog)
    {
        _database = database;
        _companyContext = companyContext;
        _currentUser = currentUser;
        _catalog = catalog;
    }

    public async Task<SalesDeliveryTolerancePolicyV1> GetSalesDeliveryToleranceAsync(
        DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        RequireAccess(cancellationToken);
        var companyId = _companyContext.CompanyId;
        SalesDeliveryToleranceVersionRow[]? rows;
        try
        {
            rows = await SalesPolicyReads.IsTransferredAsync(_database, SalesPolicyFamiliesV1.DeliveryTolerance, cancellationToken)
                ? await SalesPolicyReads.LoadTolerancesAsync(_database, cancellationToken)
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }

        if (rows is null)
        {
            return await _catalog.GetSalesDeliveryToleranceAsync(effectiveOn, cancellationToken);
        }

        var entry = SalesPolicyTimelineV1.For(SalesPolicyReads.ToleranceTimeline(rows), effectiveOn)
                    ?? throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyNotFound);
        return new SalesDeliveryTolerancePolicyV1(
            companyId,
            new ProfileVersionV1(entry.Value.ProfileId, entry.Version),
            SalesPolicyTimelineV1.Canonical(entry.Value.WarnPercent),
            SalesPolicyTimelineV1.Canonical(entry.Value.ApprovalPercent),
            SalesPolicyTimelineV1.Canonical(entry.Value.BlockPercent),
            entry.Value.IsActive,
            entry.EffectiveFrom,
            entry.EffectiveThrough);
    }

    public async Task<SalesApprovalAmountThresholdPolicyV1> GetSalesApprovalAmountThresholdAsync(
        Guid currencyId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        RequireAccess(cancellationToken);
        if (currencyId == Guid.Empty)
        {
            throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyRequestInvalid);
        }

        var companyId = _companyContext.CompanyId;
        SalesApprovalAmountThresholdVersionRow[]? rows;
        try
        {
            rows = await SalesPolicyReads.IsTransferredAsync(_database, SalesPolicyFamiliesV1.ApprovalAmountThreshold,
                    cancellationToken)
                ? await SalesPolicyReads.LoadThresholdsAsync(_database, currencyId, cancellationToken)
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }

        if (rows is null)
        {
            return await _catalog.GetSalesApprovalAmountThresholdAsync(currencyId, effectiveOn, cancellationToken);
        }

        var entry = SalesPolicyTimelineV1.For(SalesPolicyReads.ThresholdTimeline(rows), effectiveOn)
                    ?? throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyNotFound);
        return new SalesApprovalAmountThresholdPolicyV1(
            companyId,
            currencyId,
            new ProfileVersionV1(entry.Value.ProfileId, entry.Version),
            entry.Value.IsEnabled,
            SalesPolicyReads.AmountText(entry.Value),
            entry.Value.IsActive,
            entry.EffectiveFrom,
            entry.EffectiveThrough);
    }

    public async Task<SalesDiscountLimitPolicyV1> GetSalesDiscountLimitAsync(
        Guid limitGroupId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        RequireAccess(cancellationToken);
        if (limitGroupId == Guid.Empty)
        {
            throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyRequestInvalid);
        }

        var companyId = _companyContext.CompanyId;
        IReadOnlyDictionary<Guid, SalesDiscountLimitGroupDataV1> groups;
        try
        {
            groups = await SalesDiscountLimitReads.LoadGroupDataAsync(_database, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }

        if (!groups.ContainsKey(limitGroupId))
        {
            return await _catalog.GetSalesDiscountLimitAsync(limitGroupId, effectiveOn, cancellationToken);
        }

        try
        {
            _ = await _catalog.GetSalesDiscountLimitAsync(limitGroupId, effectiveOn, cancellationToken);
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }
        catch (SettingsPrerequisiteException exception) when (exception.Code is SettingsPrerequisiteCodesV1.SalesPolicyNotFound
                                                                  or "settings_provider_unavailable")
        {
            // The catalog has no entry for a store-defined group: the store alone answers.
        }

        var resolution = SalesDiscountLimitCalculatorV1.Resolve(groups, limitGroupId, effectiveOn);
        return resolution.Outcome switch
        {
            SalesDiscountLimitResolutionOutcomeV1.Defined => new SalesDiscountLimitPolicyV1(
                companyId,
                limitGroupId,
                new ProfileVersionV1(resolution.Record!.ProfileId, resolution.Record.Version),
                resolution.Percent!.Value,
                IsActive: true,
                resolution.EffectiveFrom!.Value,
                resolution.EffectiveThrough),
            SalesDiscountLimitResolutionOutcomeV1.None =>
                throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyNotFound),
            _ => throw new SettingsPrerequisiteException("settings_profile_invalid"),
        };
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
