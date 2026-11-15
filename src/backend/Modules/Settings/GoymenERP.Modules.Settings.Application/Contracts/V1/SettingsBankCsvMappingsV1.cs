namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// FNS-D52 (<c>docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA1_EK.md</c>): the Settings owner's name for maintaining the
/// company's per-bank CSV column mappings of the bank statement import. Registered, NOT granted: no role, seed or user
/// receives it here (a human grant gate). Company-wide only. Not Critical: no approved source adds grant-time dual control.
/// </summary>
public static class SettingsBankCsvMappingPermissionManifestV1
{
    /// <summary>Read every bank's CSV column mapping history and record new versions.</summary>
    public const string Manage = "settings.bank-csv-mappings.manage";
}

/// <summary>Whether a bank's mapping version may be used by the CSV import (a stop is a new <c>Inactive</c> version).</summary>
public static class BankCsvMappingStatesV1
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";

    public static IReadOnlyList<string> All { get; } = [Active, Inactive];
}

/// <summary>The field delimiter of the bank's CSV file (PLT-S17 accepts <c>;</c>, <c>,</c> and tab).</summary>
public static class BankCsvDelimitersV1
{
    /// <summary><c>;</c></summary>
    public const string Semicolon = "Semicolon";

    /// <summary><c>,</c></summary>
    public const string Comma = "Comma";

    /// <summary>The tab character.</summary>
    public const string Tab = "Tab";

    public static IReadOnlyList<string> All { get; } = [Semicolon, Comma, Tab];
}

/// <summary>The decimal separator of the bank's amount columns.</summary>
public static class BankCsvDecimalSeparatorsV1
{
    /// <summary><c>,</c> (e.g. <c>1.234,56</c>).</summary>
    public const string Comma = "Comma";

    /// <summary><c>.</c> (e.g. <c>1,234.56</c>).</summary>
    public const string Period = "Period";

    public static IReadOnlyList<string> All { get; } = [Comma, Period];
}

/// <summary>The digit group (thousands) separator of the bank's amount columns; it must differ from the decimal separator.</summary>
public static class BankCsvThousandsSeparatorsV1
{
    /// <summary>The bank writes no digit group separator.</summary>
    public const string None = "None";

    /// <summary><c>.</c></summary>
    public const string Period = "Period";

    /// <summary><c>,</c></summary>
    public const string Comma = "Comma";

    /// <summary>A space character.</summary>
    public const string Space = "Space";

    public static IReadOnlyList<string> All { get; } = [None, Period, Comma, Space];
}

/// <summary>
/// The closed list of date patterns a bank's date columns may use. Each code is the exact .NET custom date pattern the
/// importer parses with <see cref="System.Globalization.CultureInfo.InvariantCulture"/> (so <c>/</c> and <c>:</c> are
/// literal). A new pattern is a code change reviewed with its tests, never free text a person types.
/// </summary>
public static class BankCsvDateFormatsV1
{
    public const string DayMonthYearDots = "dd.MM.yyyy";
    public const string DayMonthYearSlashes = "dd/MM/yyyy";
    public const string DayMonthYearDashes = "dd-MM-yyyy";
    public const string IsoDate = "yyyy-MM-dd";
    public const string CompactDate = "yyyyMMdd";
    public const string MonthDayYearSlashes = "MM/dd/yyyy";
    public const string DayMonthYearDotsMinutes = "dd.MM.yyyy HH:mm";
    public const string DayMonthYearDotsSeconds = "dd.MM.yyyy HH:mm:ss";
    public const string IsoDateSeconds = "yyyy-MM-dd HH:mm:ss";

    public static IReadOnlyList<string> All { get; } =
    [
        DayMonthYearDots,
        DayMonthYearSlashes,
        DayMonthYearDashes,
        IsoDate,
        CompactDate,
        MonthDayYearSlashes,
        DayMonthYearDotsMinutes,
        DayMonthYearDotsSeconds,
        IsoDateSeconds,
    ];
}

/// <summary>How the bank writes the movement amount.</summary>
public static class BankCsvAmountLayoutsV1
{
    /// <summary>One signed amount column; <c>SignedAmountPositiveMeans</c> says what a positive value is.</summary>
    public const string SignedAmount = "SignedAmount";

    /// <summary>
    /// Two columns: money into the account (the bank's "alacak" side) and money out of it (the bank's "borç" side). The
    /// mapping names them by direction (inflow / outflow), so no debit/credit reading is chosen here.
    /// </summary>
    public const string InflowOutflowColumns = "InflowOutflowColumns";

    public static IReadOnlyList<string> All { get; } = [SignedAmount, InflowOutflowColumns];
}

/// <summary>What a positive value of a <see cref="BankCsvAmountLayoutsV1.SignedAmount"/> column means (no default).</summary>
public static class BankCsvSignedAmountDirectionsV1
{
    /// <summary>A positive value is money into the account.</summary>
    public const string Inflow = "Inflow";

    /// <summary>A positive value is money out of the account.</summary>
    public const string Outflow = "Outflow";

    public static IReadOnlyList<string> All { get; } = [Inflow, Outflow];
}

/// <summary>The provider's typed amount layout (numbering is stable; zero is never a valid member).</summary>
public enum SettingsBankCsvAmountLayoutV1
{
    SignedAmount = 1,
    InflowOutflowColumns = 2,
}

/// <summary>The provider's typed meaning of a positive signed amount.</summary>
public enum SettingsBankCsvSignedAmountDirectionV1
{
    Inflow = 1,
    Outflow = 2,
}

/// <summary>Technical bounds of the inputs (storage and transport shape, not policy).</summary>
public static class BankCsvMappingInputLimitsV1
{
    /// <summary>SAT-D68 precedent: the mandatory reason of every change.</summary>
    public const int ReasonMaximumLength = 4000;

    /// <summary>The longest bank key (<c>A-Z</c>, <c>0-9</c>, <c>_</c>, <c>-</c>; first character a letter or digit).</summary>
    public const int BankKeyMaximumLength = 32;

    /// <summary>The longest bank display name (trimmed).</summary>
    public const int BankDisplayNameMaximumLength = 200;

    /// <summary>The highest 1-based column number a mapping may name (a storage guard, far beyond any bank's file).</summary>
    public const int MaximumColumnNumber = 256;

    /// <summary>The most leading rows (bank banner lines before the header or the first data row) a mapping may skip.</summary>
    public const int MaximumSkipLeadingRows = 50;
}

/// <summary>Published problem codes of the bank CSV mapping routes.</summary>
public static class BankCsvMappingProblemCodesV1
{
    public const string RequestInvalid = "settings_bank_csv_mapping_request_invalid";
    public const string CommandMetadataInvalid = "settings_bank_csv_mapping_command_metadata_invalid";
    public const string ReasonRequired = "settings_bank_csv_mapping_reason_required";
    public const string ReasonTooLong = "settings_bank_csv_mapping_reason_too_long";
    public const string IdempotencyConflict = "settings_bank_csv_mapping_idempotency_conflict";
    public const string VersionConflict = "settings_bank_csv_mapping_version_conflict";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>
/// A new CSV column mapping version of one bank in the server-resolved company (FNS-D52). Every field is required unless
/// noted; nothing has a code default:
/// <list type="bullet">
/// <item><c>BankKey</c>: the company's own short key for the bank (<c>A-Z</c>, <c>0-9</c>, <c>_</c>, <c>-</c>, at most
/// <see cref="BankCsvMappingInputLimitsV1.BankKeyMaximumLength"/>); <c>BankDisplayName</c>: its visible name;</item>
/// <item><c>State</c>: <see cref="BankCsvMappingStatesV1"/>;</item>
/// <item>file shape: <c>Delimiter</c>, <c>SkipLeadingRows</c> (0…<see cref="BankCsvMappingInputLimitsV1.MaximumSkipLeadingRows"/>,
/// lines dropped before the header or the first data row), <c>HasHeaderRow</c>;</item>
/// <item>value shape: <c>DecimalSeparator</c>, <c>ThousandsSeparator</c> (different from the decimal one, or <c>None</c>),
/// <c>DateFormat</c> (<see cref="BankCsvDateFormatsV1"/>);</item>
/// <item>amount: <c>AmountLayout</c>; <c>SignedAmount</c> needs <c>AmountColumn</c> and <c>SignedAmountPositiveMeans</c> and
/// no inflow/outflow column; <c>InflowOutflowColumns</c> needs <c>InflowColumn</c> and <c>OutflowColumn</c> and no amount
/// column or sign meaning;</item>
/// <item>columns (1-based, 1…<see cref="BankCsvMappingInputLimitsV1.MaximumColumnNumber"/>, all different):
/// <c>TransactionDateColumn</c> and <c>DescriptionColumn</c> required; <c>ValueDateColumn</c>, <c>BalanceColumn</c>,
/// <c>ReferenceColumn</c> and <c>CurrencyColumn</c> optional (null = the file has none).</item>
/// </list>
/// <c>ExpectedVersion</c> is the bank's latest stored version ("0" for a bank key without a mapping). The latest version of
/// a bank is the one in force; there is no start date.
/// </summary>
public sealed record RecordBankCsvMappingRequestV1(
    string? ExpectedVersion,
    string? BankKey,
    string? BankDisplayName,
    string? State,
    string? Delimiter,
    int? SkipLeadingRows,
    bool? HasHeaderRow,
    string? DecimalSeparator,
    string? ThousandsSeparator,
    string? DateFormat,
    string? AmountLayout,
    string? SignedAmountPositiveMeans,
    int? TransactionDateColumn,
    int? ValueDateColumn,
    int? DescriptionColumn,
    int? AmountColumn,
    int? InflowColumn,
    int? OutflowColumn,
    int? BalanceColumn,
    int? ReferenceColumn,
    int? CurrencyColumn,
    string? Reason);

public sealed record BankCsvMappingVersionViewV1(
    Guid ProfileId,
    string Version,
    string BankDisplayName,
    string State,
    string Delimiter,
    int SkipLeadingRows,
    bool HasHeaderRow,
    string DecimalSeparator,
    string ThousandsSeparator,
    string DateFormat,
    string AmountLayout,
    string? SignedAmountPositiveMeans,
    int TransactionDateColumn,
    int? ValueDateColumn,
    int DescriptionColumn,
    int? AmountColumn,
    int? InflowColumn,
    int? OutflowColumn,
    int? BalanceColumn,
    int? ReferenceColumn,
    int? CurrencyColumn,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

/// <summary>
/// One bank's mapping history. <c>Version</c> is the latest stored version (the next record's expected version);
/// <c>Versions</c> run oldest first, so the last one is in force.
/// </summary>
public sealed record BankCsvMappingBankViewV1(
    string BankKey,
    string Version,
    IReadOnlyList<BankCsvMappingVersionViewV1> Versions);

/// <summary>The company's bank CSV mappings, ordered by bank key (ordinal). An empty list means no bank has a mapping yet.</summary>
public sealed record BankCsvMappingsViewV1(Guid CompanyId, IReadOnlyList<BankCsvMappingBankViewV1> Banks);

/// <summary>A command's terminal answer (stored for replay). <c>Code</c> is null for a success.</summary>
public sealed record BankCsvMappingCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record BankCsvMappingCommandResultV1(
    BankCsvMappingCommandAnswerV1 Answer,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// The bank CSV mapping administration (FNS-D52). Every read and command is authorized first on
/// <see cref="SettingsBankCsvMappingPermissionManifestV1.Manage"/> company-wide in the server-resolved company and re-measured
/// before every save. <see cref="UnauthorizedAccessException"/> means 403. One person decides (SAT-D68 precedent): a
/// mandatory reason, Idempotency-Key, the bank's expected version and one audit event per real change (none for a no-op).
/// </summary>
public interface ISettingsBankCsvMappingAdministrationV1
{
    Task<BankCsvMappingsViewV1> GetAsync(CancellationToken cancellationToken = default);

    Task<BankCsvMappingCommandResultV1> RecordAsync(RecordBankCsvMappingRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// The 1-based column numbers of a bank's CSV file. Null means the file has no such column. Exactly one of
/// <c>Amount</c> (signed layout) or the <c>Inflow</c>/<c>Outflow</c> pair is set, matching the layout.
/// </summary>
public sealed record SettingsBankCsvColumnsV1(
    int TransactionDate,
    int? ValueDate,
    int Description,
    int? Amount,
    int? Inflow,
    int? Outflow,
    int? Balance,
    int? Reference,
    int? Currency);

/// <summary>
/// A bank's active CSV column mapping (the provider's answer). <c>Profile</c> names the exact stored version the importer
/// read the file with. <c>ThousandsSeparator</c> is null when the bank writes none. <c>DateFormat</c> is a
/// <see cref="BankCsvDateFormatsV1"/> pattern to parse exactly with the invariant culture. <c>SignedAmountPositiveMeans</c>
/// is set only for <see cref="SettingsBankCsvAmountLayoutV1.SignedAmount"/>. The text is plain UTF-8 (PLT-S17); neutralizing
/// spreadsheet formula prefixes (<c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>) in text cells is the importer's job.
/// </summary>
public sealed record SettingsBankCsvMappingV1(
    Guid CompanyId,
    ProfileVersionV1 Profile,
    string BankKey,
    string BankDisplayName,
    char Delimiter,
    int SkipLeadingRows,
    bool HasHeaderRow,
    char DecimalSeparator,
    char? ThousandsSeparator,
    string DateFormat,
    SettingsBankCsvAmountLayoutV1 AmountLayout,
    SettingsBankCsvSignedAmountDirectionV1? SignedAmountPositiveMeans,
    SettingsBankCsvColumnsV1 Columns);

/// <summary>A bank with an active CSV mapping, for the importer's bank choice.</summary>
public sealed record SettingsBankCsvMappingSummaryV1(string BankKey, string BankDisplayName, ProfileVersionV1 Profile);

/// <summary>
/// The read port the bank statement import (Finance, FIN-S21) asks for the server-resolved company's per-bank CSV column
/// mapping (FNS-D52; XLSX and MT940 need none). The caller needs access to the company, not the manage permission. Errors
/// are <see cref="SettingsPrerequisiteException"/> codes, in this order: cancelled → <see cref="OperationCanceledException"/>;
/// no authenticated user or no access to the company → <c>settings_access_denied</c>; an unreadable store →
/// <c>settings_provider_unavailable</c>; no mapping for the key (including a malformed key) →
/// <see cref="SettingsPrerequisiteCodesV1.BankCsvMappingNotFound"/>; malformed stored data → <c>settings_profile_invalid</c>;
/// the latest version inactive → <see cref="SettingsPrerequisiteCodesV1.BankCsvMappingInactive"/>. There is no fallback
/// mapping: the importer refuses a CSV file rather than guess its columns.
/// </summary>
public interface ISettingsBankCsvMappingProviderV1
{
    Task<SettingsBankCsvMappingV1> GetBankCsvMappingAsync(string bankKey, CancellationToken cancellationToken = default);

    /// <summary>The banks whose latest version is active, ordered by bank key (ordinal); empty when none.</summary>
    Task<IReadOnlyList<SettingsBankCsvMappingSummaryV1>> ListActiveBankCsvMappingsAsync(
        CancellationToken cancellationToken = default);
}
