using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// The real Settings provider for what is approved today: unit profiles, from
/// <see cref="ApprovedUnitProfileCatalog"/> (T02-a — GDC kg; SAT-D18 — GSP and GIN kg), and money
/// profiles, from <see cref="ApprovedMoneyProfileCatalog"/> (T02-b — GDC/TRY; SAT-D18 — GSP and GIN
/// TRY for the Sales draft operation only). Period eligibility is NOT an
/// approved source yet and stays exactly as <see cref="UnavailableSettingsPrerequisiteProvider"/>
/// answers it.
/// <para>
/// Outcomes of <see cref="GetUnitProfileAsync"/>, in order:
/// <list type="number">
/// <item>cancelled → <see cref="OperationCanceledException"/>;</item>
/// <item>no authenticated actor, no actor id, unresolved or empty company, or an actor who is
/// not a member of it → <c>settings_access_denied</c>, before anything about the source is
/// revealed (the same check as the unavailable adapter);</item>
/// <item>no approved profile source for the server-resolved company →
/// <c>settings_provider_unavailable</c> (unreachable: there is nothing to ask);</item>
/// <item>the company has a source but no profile for this unit →
/// <see cref="UnitProfileNotFoundCode"/> (reachable, but empty for this identity — kept apart
/// from both "unavailable" and "invalid");</item>
/// <item>a malformed catalog → <c>settings_profile_invalid</c>, raised when the catalog is
/// built, never repaired;</item>
/// <item>otherwise the owner's exact profile. A date outside every window still returns the
/// real version with its real window; consumers must check activity, window and kilogram
/// identity themselves, as the port requires, and a date before approval is therefore
/// rejected by them rather than served as valid.</item>
/// </list>
/// </para>
/// <para>
/// <see cref="GetMoneyProfileAsync"/> follows the same order, with the operation in it: access
/// → <c>settings_access_denied</c>; a company without an approved money source, or an operation key
/// no approved profile of that company answers for (e.g. Accounting/Finance keys for GSP/GIN,
/// SAT-D18) → <c>settings_provider_unavailable</c> (nothing approved to ask); an approved operation but no profile for the currency (USD, EUR or an empty id for GDC
/// today) → <see cref="SettingsPrerequisiteCodesV1.MoneyProfileNotFound"/>; otherwise the exact
/// version for the date, answered for the requested operation, with its real window.
/// </para>
/// <para>
/// <see cref="GetMoneyCurrencyCandidatesAsync"/> lists, from the same approved money catalog, each
/// currency whose approved profile for the server company is unrestricted — answers every operation
/// key the catalog knows (GDC: TRY; GSP/GIN have none, their TRY is Sales-only, SAT-D18, so they get
/// <c>settings_provider_unavailable</c> as before). The operation-aware
/// <see cref="ISettingsMoneyCandidateReaderV2"/> read lists each currency whose profile answers the
/// given operation (GSP/GIN for <c>sales.order-draft.v1</c>: TRY). Access first; a
/// company without an approved money source → <c>settings_provider_unavailable</c>, never an
/// empty list or a default; the list passes <see cref="MoneyCurrencyCandidatesV1.EnsureWellFormed"/>.
/// Being listed is not validity: a consumer still resolves the chosen currency with
/// <see cref="GetMoneyProfileAsync"/> for its operation and date.
/// </para>
/// <para>
/// <see cref="ISettingsSalesPolicyProviderV1"/> (MOD-03 slice 1) serves
/// <see cref="ApprovedSalesPolicyCatalog"/> in the same order: access → <c>settings_access_denied</c>;
/// an empty limit group or currency id → <see cref="SettingsPrerequisiteCodesV1.SalesPolicyRequestInvalid"/>
/// (after the access check, before any lookup, never "not found"); a company without an approved
/// Sales policy source (any company other than GDC, GSP and GIN today, SAT-D14) →
/// <c>settings_provider_unavailable</c>; a
/// reachable source without a policy for the identity asked about (every discount limit group
/// today, a non-empty currency other than TRY for the threshold) →
/// <see cref="SettingsPrerequisiteCodesV1.SalesPolicyNotFound"/>; otherwise the exact version for the
/// date with its real window.
/// </para>
/// <para>
/// The company is never a request input. A returned profile is reference metadata, not an
/// authorization for any operation: the consuming use case still checks its own permission.
/// </para>
/// </summary>
public sealed class ApprovedSettingsPrerequisiteProvider : ISettingsPrerequisiteProvider, ISettingsMoneyCandidateReader,
    ISettingsMoneyCandidateReaderV2,
    ISettingsManufacturingToleranceProviderV1, ISettingsSalesPolicyProviderV1
{
    /// <summary>
    /// Kept as an alias so existing callers still compile. Consumers map the published contract
    /// value <see cref="SettingsPrerequisiteCodesV1.UnitProfileNotFound"/> instead.
    /// </summary>
    public const string UnitProfileNotFoundCode = SettingsPrerequisiteCodesV1.UnitProfileNotFound;

    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;
    private readonly ApprovedUnitProfileCatalog _catalog;
    private readonly ApprovedMoneyProfileCatalog _money;
    private readonly ApprovedManufacturingToleranceCatalog _manufacturingTolerance;
    private readonly ApprovedSalesPolicyCatalog _sales;
    private readonly UnavailableSettingsPrerequisiteProvider _notApprovedYet;

    /// <summary>
    /// The only constructor the host and other modules can reach: it always serves the reviewed
    /// <see cref="ApprovedUnitProfileCatalog.Approved"/> and <see cref="ApprovedMoneyProfileCatalog.Approved"/>
    /// catalogs.
    /// </summary>
    public ApprovedSettingsPrerequisiteProvider(ICompanyContext companyContext, ICurrentUser currentUser)
        : this(companyContext, currentUser, ApprovedUnitProfileCatalog.Approved, ApprovedMoneyProfileCatalog.Approved,
            ApprovedManufacturingToleranceCatalog.Approved)
    {
    }

    /// <summary>
    /// Test seam, internal on purpose (FİNANS #279 P3-1): no module that references Settings can
    /// build an "approved" provider over profiles it made up; only Settings' own tests can.
    /// </summary>
    internal ApprovedSettingsPrerequisiteProvider(
        ICompanyContext companyContext, ICurrentUser currentUser, ApprovedUnitProfileCatalog catalog)
        : this(companyContext, currentUser, catalog, ApprovedMoneyProfileCatalog.Approved,
            ApprovedManufacturingToleranceCatalog.Approved)
    {
    }

    /// <summary>Settings-only synthetic Manufacturing tolerance seam.</summary>
    internal ApprovedSettingsPrerequisiteProvider(
        ICompanyContext companyContext, ICurrentUser currentUser,
        ApprovedManufacturingToleranceCatalog manufacturingTolerance)
        : this(companyContext, currentUser, ApprovedUnitProfileCatalog.Approved,
            ApprovedMoneyProfileCatalog.Approved, manufacturingTolerance)
    {
    }

    /// <summary>Test seam for both catalogs; internal for the same reason as the one above.</summary>
    internal ApprovedSettingsPrerequisiteProvider(
        ICompanyContext companyContext, ICurrentUser currentUser,
        ApprovedUnitProfileCatalog catalog, ApprovedMoneyProfileCatalog money)
        : this(companyContext, currentUser, catalog, money, ApprovedManufacturingToleranceCatalog.Approved)
    {
    }

    /// <summary>Settings-only synthetic Sales policy seam.</summary>
    internal ApprovedSettingsPrerequisiteProvider(
        ICompanyContext companyContext, ICurrentUser currentUser, ApprovedSalesPolicyCatalog sales)
        : this(companyContext, currentUser, ApprovedUnitProfileCatalog.Approved,
            ApprovedMoneyProfileCatalog.Approved, ApprovedManufacturingToleranceCatalog.Approved, sales)
    {
    }

    internal ApprovedSettingsPrerequisiteProvider(
        ICompanyContext companyContext, ICurrentUser currentUser,
        ApprovedUnitProfileCatalog catalog, ApprovedMoneyProfileCatalog money,
        ApprovedManufacturingToleranceCatalog manufacturingTolerance)
        : this(companyContext, currentUser, catalog, money, manufacturingTolerance, ApprovedSalesPolicyCatalog.Approved)
    {
    }

    internal ApprovedSettingsPrerequisiteProvider(
        ICompanyContext companyContext, ICurrentUser currentUser,
        ApprovedUnitProfileCatalog catalog, ApprovedMoneyProfileCatalog money,
        ApprovedManufacturingToleranceCatalog manufacturingTolerance, ApprovedSalesPolicyCatalog sales)
    {
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(money);
        ArgumentNullException.ThrowIfNull(manufacturingTolerance);
        ArgumentNullException.ThrowIfNull(sales);
        _companyContext = companyContext;
        _currentUser = currentUser;
        _catalog = catalog;
        _money = money;
        _manufacturingTolerance = manufacturingTolerance;
        _sales = sales;
        _notApprovedYet = new UnavailableSettingsPrerequisiteProvider(companyContext, currentUser);
    }

    public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !_companyContext.IsResolved || _companyContext.CompanyId == Guid.Empty
            || !_currentUser.CanAccessCompany(_companyContext.CompanyId))
        {
            return Task.FromException<UnitProfileV1>(new SettingsPrerequisiteException("settings_access_denied"));
        }

        var companyId = _companyContext.CompanyId;
        if (!_catalog.Serves(companyId))
        {
            return Task.FromException<UnitProfileV1>(
                new SettingsPrerequisiteException("settings_provider_unavailable"));
        }

        var entry = _catalog.VersionFor(companyId, unitId, effectiveOn);
        if (entry is null)
        {
            return Task.FromException<UnitProfileV1>(new SettingsPrerequisiteException(UnitProfileNotFoundCode));
        }

        return Task.FromResult(new UnitProfileV1(
            entry.CompanyId,
            entry.UnitId,
            entry.UnitCode,
            entry.DimensionCode,
            entry.IsKilogram,
            new ProfileVersionV1(entry.ProfileId, entry.Version),
            entry.Scale,
            entry.IsActive,
            entry.EffectiveFrom,
            entry.EffectiveThrough));
    }

    /// <summary>The approved money profile (T02-b); see the class remarks for the order of outcomes.</summary>
    public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation,
        DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasCompanyAccess())
        {
            return Task.FromException<MoneyProfileV1>(new SettingsPrerequisiteException("settings_access_denied"));
        }

        var companyId = _companyContext.CompanyId;
        if (!_money.Serves(companyId) || !_money.Approves(companyId, operation))
        {
            return Task.FromException<MoneyProfileV1>(
                new SettingsPrerequisiteException("settings_provider_unavailable"));
        }

        var entry = _money.VersionFor(companyId, currencyId, effectiveOn);
        if (entry is null || !entry.Operations.Contains(operation))
        {
            return Task.FromException<MoneyProfileV1>(
                new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.MoneyProfileNotFound));
        }

        return Task.FromResult(new MoneyProfileV1(
            entry.CompanyId,
            entry.CurrencyId,
            entry.CurrencyCode,
            operation,
            new ProfileVersionV1(entry.ProfileId, entry.Version),
            entry.Scale,
            entry.IsActive,
            entry.EffectiveFrom,
            entry.EffectiveThrough));
    }

    /// <summary>The approved money currencies of the server company (T02-b); see the class remarks.</summary>
    public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasCompanyAccess())
        {
            return Task.FromException<IReadOnlyList<MoneyCurrencyCandidateV1>>(
                new SettingsPrerequisiteException("settings_access_denied"));
        }

        // Operation-less: only unrestricted profiles can be listed. A company whose every profile is
        // narrowed to some operations (GSP/GIN, SAT-D18) keeps its earlier answer: unavailable, so an
        // operation-less consumer such as Finance FS0 is not opened by a Sales-only profile.
        var companyId = _companyContext.CompanyId;
        var unrestricted = _money.Serves(companyId)
            ? _money.UnrestrictedCurrenciesOf(companyId)
            : Array.Empty<MoneyCurrencyCandidateV1>();
        if (unrestricted.Count == 0)
        {
            return Task.FromException<IReadOnlyList<MoneyCurrencyCandidateV1>>(
                new SettingsPrerequisiteException("settings_provider_unavailable"));
        }

        return WellFormed(unrestricted);
    }

    /// <summary>
    /// The approved money currencies of the server company for one operation (SAT-D18 follow-up):
    /// access first; no money source or an operation no profile of the company answers →
    /// <c>settings_provider_unavailable</c>; otherwise each currency whose profile answers it.
    /// </summary>
    public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
        string operation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasCompanyAccess())
        {
            return Task.FromException<IReadOnlyList<MoneyCurrencyCandidateV1>>(
                new SettingsPrerequisiteException("settings_access_denied"));
        }

        var companyId = _companyContext.CompanyId;
        if (!_money.Serves(companyId) || !_money.Approves(companyId, operation))
        {
            return Task.FromException<IReadOnlyList<MoneyCurrencyCandidateV1>>(
                new SettingsPrerequisiteException("settings_provider_unavailable"));
        }

        return WellFormed(_money.CurrenciesOf(companyId, operation));
    }

    private static Task<IReadOnlyList<MoneyCurrencyCandidateV1>> WellFormed(
        IReadOnlyList<MoneyCurrencyCandidateV1> candidates)
    {
        try
        {
            return Task.FromResult(MoneyCurrencyCandidatesV1.EnsureWellFormed(candidates));
        }
        catch (SettingsPrerequisiteException exception)
        {
            return Task.FromException<IReadOnlyList<MoneyCurrencyCandidateV1>>(exception);
        }
    }

    public Task<ManufacturingToleranceProfileV1> GetManufacturingToleranceAsync(
        string process, DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasCompanyAccess())
        {
            return Task.FromException<ManufacturingToleranceProfileV1>(
                new SettingsPrerequisiteException("settings_access_denied"));
        }

        var companyId = _companyContext.CompanyId;
        if (!_manufacturingTolerance.Serves(companyId))
        {
            return Task.FromException<ManufacturingToleranceProfileV1>(
                new SettingsPrerequisiteException("settings_provider_unavailable"));
        }

        var entry = _manufacturingTolerance.VersionFor(companyId, process, effectiveOn);
        if (entry is null)
        {
            return Task.FromException<ManufacturingToleranceProfileV1>(new SettingsPrerequisiteException(
                SettingsPrerequisiteCodesV1.ManufacturingToleranceProfileNotFound));
        }

        return Task.FromResult(new ManufacturingToleranceProfileV1(
            entry.CompanyId,
            entry.Process,
            new ProfileVersionV1(entry.ProfileId, entry.Version),
            entry.MaximumFirePercent,
            entry.IsActive,
            entry.EffectiveFrom,
            entry.EffectiveThrough));
    }

    /// <summary>The approved Sales delivery tolerance (SAT-D5); see the class remarks.</summary>
    public Task<SalesDeliveryTolerancePolicyV1> GetSalesDeliveryToleranceAsync(
        DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        if (SalesSourceRefusal<SalesDeliveryTolerancePolicyV1>(cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var entry = _sales.DeliveryToleranceFor(_companyContext.CompanyId, effectiveOn);
        return entry is null
            ? SalesPolicyNotFound<SalesDeliveryTolerancePolicyV1>()
            : Task.FromResult(new SalesDeliveryTolerancePolicyV1(
                entry.CompanyId,
                new ProfileVersionV1(entry.ProfileId, entry.Version),
                entry.WarnPercent,
                entry.ApprovalPercent,
                entry.BlockPercent,
                entry.IsActive,
                entry.EffectiveFrom,
                entry.EffectiveThrough));
    }

    /// <summary>
    /// The approved discount limit of a limit group (SAT-D2). None is approved yet, so every group of a
    /// served company answers <see cref="SettingsPrerequisiteCodesV1.SalesPolicyNotFound"/>: "no limit
    /// defined", which SAT-D2 routes to approval.
    /// </summary>
    public Task<SalesDiscountLimitPolicyV1> GetSalesDiscountLimitAsync(
        Guid limitGroupId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        if (SalesSourceRefusal<SalesDiscountLimitPolicyV1>(cancellationToken, limitGroupId) is { } refusal)
        {
            return refusal;
        }

        var entry = _sales.DiscountLimitFor(_companyContext.CompanyId, limitGroupId, effectiveOn);
        return entry is null
            ? SalesPolicyNotFound<SalesDiscountLimitPolicyV1>()
            : Task.FromResult(new SalesDiscountLimitPolicyV1(
                entry.CompanyId,
                entry.LimitGroupId,
                new ProfileVersionV1(entry.ProfileId, entry.Version),
                entry.MaximumDiscountPercent,
                entry.IsActive,
                entry.EffectiveFrom,
                entry.EffectiveThrough));
    }

    /// <summary>The approved order-approval amount threshold of a currency (SAT-D6); see the class remarks.</summary>
    public Task<SalesApprovalAmountThresholdPolicyV1> GetSalesApprovalAmountThresholdAsync(
        Guid currencyId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        if (SalesSourceRefusal<SalesApprovalAmountThresholdPolicyV1>(cancellationToken, currencyId) is { } refusal)
        {
            return refusal;
        }

        var entry = _sales.ApprovalAmountThresholdFor(_companyContext.CompanyId, currencyId, effectiveOn);
        return entry is null
            ? SalesPolicyNotFound<SalesApprovalAmountThresholdPolicyV1>()
            : Task.FromResult(new SalesApprovalAmountThresholdPolicyV1(
                entry.CompanyId,
                entry.CurrencyId,
                new ProfileVersionV1(entry.ProfileId, entry.Version),
                entry.IsEnabled,
                entry.ThresholdAmountText,
                entry.IsActive,
                entry.EffectiveFrom,
                entry.EffectiveThrough));
    }

    /// <summary>Not an approved source yet.</summary>
    public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation,
        DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) =>
        _notApprovedYet.GetPeriodEligibilityAsync(operation, occurredOn, valueOn, cancellationToken);

    /// <summary>
    /// Cancellation, then access, then the requested identity (when the read takes one), then the
    /// company's Sales source; null when the read may proceed.
    /// </summary>
    private Task<T>? SalesSourceRefusal<T>(CancellationToken cancellationToken, Guid? requestedId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasCompanyAccess())
        {
            return Task.FromException<T>(new SettingsPrerequisiteException("settings_access_denied"));
        }

        if (requestedId == Guid.Empty)
        {
            return Task.FromException<T>(
                new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyRequestInvalid));
        }

        return _sales.Serves(_companyContext.CompanyId)
            ? null
            : Task.FromException<T>(new SettingsPrerequisiteException("settings_provider_unavailable"));
    }

    private static Task<T> SalesPolicyNotFound<T>() =>
        Task.FromException<T>(new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyNotFound));

    private bool HasCompanyAccess() =>
        _currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(_currentUser.UserId)
        && _companyContext.IsResolved && _companyContext.CompanyId != Guid.Empty
        && _currentUser.CanAccessCompany(_companyContext.CompanyId);
}
