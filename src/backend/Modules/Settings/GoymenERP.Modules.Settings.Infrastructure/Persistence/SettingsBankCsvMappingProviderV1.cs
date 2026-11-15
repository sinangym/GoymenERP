using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The host's <see cref="ISettingsBankCsvMappingProviderV1"/> (FNS-D52). The only source is the company's stored per-bank
/// CSV column mapping versions; there is no compiled or seeded mapping and no fallback. The bank's latest version answers
/// and is re-checked with the same rules the administration applied (<see cref="BankCsvMappingRulesV1.Refusal"/>), so a
/// malformed stored row is never handed to the importer. Order: cancelled → <see cref="OperationCanceledException"/>; no
/// access → <c>settings_access_denied</c>; an unreadable store → <c>settings_provider_unavailable</c>; no mapping →
/// <see cref="SettingsPrerequisiteCodesV1.BankCsvMappingNotFound"/>; malformed stored data → <c>settings_profile_invalid</c>;
/// inactive → <see cref="SettingsPrerequisiteCodesV1.BankCsvMappingInactive"/>.
/// </summary>
public sealed class SettingsBankCsvMappingProviderV1 : ISettingsBankCsvMappingProviderV1
{
    private const string ProfileInvalidCode = "settings_profile_invalid";
    private readonly SettingsDbContext _database;
    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;

    public SettingsBankCsvMappingProviderV1(SettingsDbContext database, ICompanyContext companyContext,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        _database = database;
        _companyContext = companyContext;
        _currentUser = currentUser;
    }

    public async Task<SettingsBankCsvMappingV1> GetBankCsvMappingAsync(string bankKey,
        CancellationToken cancellationToken = default)
    {
        RequireAccess(cancellationToken);
        if (!BankCsvMappingRulesV1.IsBankKey(bankKey))
        {
            throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.BankCsvMappingNotFound);
        }

        var companyId = _companyContext.CompanyId;
        var rows = await LoadAsync(() => BankCsvMappingReads.LoadBankAsync(_database, bankKey, cancellationToken));
        if (rows.Length == 0)
        {
            throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.BankCsvMappingNotFound);
        }

        var row = rows[^1];
        var values = BankCsvMappingReads.Values(row);
        if (!IsWellFormed(row, companyId, bankKey)
            || BankCsvMappingRulesV1.Refusal(values) is not null
            || !BankCsvMappingRulesV1.TryParseDelimiter(row.Delimiter, out var delimiter)
            || !BankCsvMappingRulesV1.TryParseDecimalSeparator(row.DecimalSeparator, out var decimalSeparator)
            || !BankCsvMappingRulesV1.TryParseThousandsSeparator(row.ThousandsSeparator, out var thousandsSeparator)
            || !BankCsvMappingRulesV1.TryParseAmountLayout(row.AmountLayout, out var layout))
        {
            throw new SettingsPrerequisiteException(ProfileInvalidCode);
        }

        if (!string.Equals(row.State, BankCsvMappingStatesV1.Active, StringComparison.Ordinal))
        {
            throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.BankCsvMappingInactive);
        }

        SettingsBankCsvSignedAmountDirectionV1? positiveMeans =
            BankCsvMappingRulesV1.TryParseSignedAmountDirection(row.SignedAmountPositiveMeans, out var direction)
                ? direction
                : null;
        return new SettingsBankCsvMappingV1(
            companyId,
            new ProfileVersionV1(row.ProfileId, row.Version),
            row.BankKey,
            row.BankDisplayName,
            delimiter,
            row.SkipLeadingRows,
            row.HasHeaderRow,
            decimalSeparator,
            thousandsSeparator,
            row.DateFormat,
            layout,
            positiveMeans,
            new SettingsBankCsvColumnsV1(
                row.TransactionDateColumn,
                row.ValueDateColumn,
                row.DescriptionColumn,
                row.AmountColumn,
                row.InflowColumn,
                row.OutflowColumn,
                row.BalanceColumn,
                row.ReferenceColumn,
                row.CurrencyColumn));
    }

    public async Task<IReadOnlyList<SettingsBankCsvMappingSummaryV1>> ListActiveBankCsvMappingsAsync(
        CancellationToken cancellationToken = default)
    {
        RequireAccess(cancellationToken);
        var companyId = _companyContext.CompanyId;
        var rows = await LoadAsync(() => BankCsvMappingReads.LoadAllAsync(_database, cancellationToken));
        var summaries = new List<SettingsBankCsvMappingSummaryV1>();
        foreach (var bank in BankCsvMappingReads.ByBank(rows))
        {
            var row = bank[bank.Count - 1];
            if (!IsWellFormed(row, companyId, row.BankKey) || !BankCsvMappingRulesV1.IsState(row.State))
            {
                throw new SettingsPrerequisiteException(ProfileInvalidCode);
            }

            if (string.Equals(row.State, BankCsvMappingStatesV1.Active, StringComparison.Ordinal))
            {
                summaries.Add(new SettingsBankCsvMappingSummaryV1(row.BankKey, row.BankDisplayName,
                    new ProfileVersionV1(row.ProfileId, row.Version)));
            }
        }

        return summaries;
    }

    /// <summary>The row's identity: this company, the exact bank key, a real profile and version, a usable display name.</summary>
    private static bool IsWellFormed(BankCsvMappingVersionRow row, Guid companyId, string bankKey) =>
        row.CompanyId == companyId
        && BankCsvMappingRulesV1.IsBankKey(row.BankKey)
        && string.Equals(row.BankKey, bankKey, StringComparison.Ordinal)
        && row.ProfileId != Guid.Empty
        && row.Version > 0
        && BankCsvMappingRulesV1.TryNormalizeDisplayName(row.BankDisplayName, out var normalized)
        && string.Equals(normalized, row.BankDisplayName, StringComparison.Ordinal);

    private static async Task<BankCsvMappingVersionRow[]> LoadAsync(Func<Task<BankCsvMappingVersionRow[]>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(BankCsvMappingProblemCodesV1.ProviderUnavailable);
        }
    }

    /// <summary>Cancelled → <see cref="OperationCanceledException"/>; no access → <c>settings_access_denied</c>.</summary>
    private void RequireAccess(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !_companyContext.IsResolved || _companyContext.CompanyId == Guid.Empty
            || !_currentUser.CanAccessCompany(_companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException(BankCsvMappingProblemCodesV1.AccessDenied);
        }
    }
}
