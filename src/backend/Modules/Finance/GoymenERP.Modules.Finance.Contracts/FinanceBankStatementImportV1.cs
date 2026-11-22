using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// FNS-D13 / FNS-D52 (WP12, DALGA_PLANI_v2 1.13): the bank statement import's own names. Registered, NOT granted: no role,
/// seed or user receives them here (a human grant gate). Company-wide only. Not Critical.
/// </summary>
public static class FinanceBankStatementPermissionsV1
{
    /// <summary>Reserve an import for one Finance bank account and parse its uploaded statement file into lines.</summary>
    public const string Import = "finance.bank-statements.import";

    /// <summary>Read imports, their lines and the deterministic match suggestions.</summary>
    public const string Read = "finance.bank-statements.read";
}

/// <summary>
/// FNS-D52: the file kinds a statement may be. <see cref="Xlsx"/> is accepted by the document vault but the importer
/// refuses it (<see cref="FinanceBankStatementCodesV1.FormatNotSupported"/>) until the column source of a workbook is decided
/// (NEEDS-SINAN WP12-S2): the Settings mapping is a CSV mapping and no source says it also governs a workbook.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceBankStatementFormatV1>))]
public enum FinanceBankStatementFormatV1
{
    Csv = 1,
    Mt940 = 2,
    Xlsx = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter<FinanceBankStatementImportStateV1>))]
public enum FinanceBankStatementImportStateV1
{
    /// <summary>The import exists and waits for its file; the file is uploaded to the vault on this record.</summary>
    Reserved = 1,

    /// <summary>The file was read once; its new lines are stored. Terminal: a corrected file is a new import.</summary>
    Parsed = 2,
}

/// <summary>Money into the account or out of it, as the bank wrote it. No debit/credit reading is chosen here.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceBankStatementDirectionV1>))]
public enum FinanceBankStatementDirectionV1
{
    Inflow = 1,
    Outflow = 2,
}

/// <summary>
/// FNS-D13: a statement line never creates a movement. A suggestion only names an EXISTING Finance record; recording a
/// missing movement is done by a person with Finance's own commands.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceBankStatementMatchStatusV1>))]
public enum FinanceBankStatementMatchStatusV1
{
    /// <summary>Exactly one recorded movement has the same direction, exact amount, date and verified counterparty.</summary>
    Suggested = 1,

    /// <summary>A person decides ("elle"); for FNS-D13 an unmatched line is a "elle kaydedilmemiş hareket" warning.</summary>
    Manual = 2,
}

/// <summary>Why a line is <see cref="FinanceBankStatementMatchStatusV1.Manual"/>; the first reason that applies, in this order.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceBankStatementManualReasonV1>))]
public enum FinanceBankStatementManualReasonV1
{
    /// <summary>No recorded movement of this bank account has the same direction, exact amount and date.</summary>
    NoExactCandidate = 1,

    /// <summary>The line names no single counterparty IBAN, so the counterparty cannot be compared.</summary>
    NoCounterpartyIdentity = 2,

    /// <summary>
    /// Candidates exist but none has a counterparty identity to compare with: a party receipt/payment (no party IBAN
    /// source exists, NEEDS-SINAN WP12-S1), an expense payment, or a transfer leg of a cash account or of a bank account
    /// without an approved IBAN.
    /// </summary>
    CounterpartyNotComparable = 3,

    /// <summary>More than one movement passes, or another line of the same import points at the same movement.</summary>
    Ambiguous = 4,
}

[JsonConverter(typeof(JsonStringEnumConverter<FinanceBankStatementCandidateKindV1>))]
public enum FinanceBankStatementCandidateKindV1
{
    /// <summary>A recorded receipt (tahsilat) into the bank account, read at its bank net amount for POS.</summary>
    Receipt = 1,

    /// <summary>A recorded party payment (ödeme) out of the bank account.</summary>
    Payment = 2,

    /// <summary>A recorded expense payment out of the bank account.</summary>
    ExpensePayment = 3,

    /// <summary>A recorded transfer (virman) leg on the bank account.</summary>
    Transfer = 4,
}

/// <summary>Published codes of the bank statement import (422/409 bodies; 401/403/404/503 are code-less).</summary>
public static class FinanceBankStatementCodesV1
{
    public const string RequestInvalid = "finance_bank_statement_request_invalid";
    public const string ImportNotFound = "finance_bank_statement_import_not_found";
    public const string BankAccountUnavailable = "finance_bank_statement_bank_account_unavailable";
    public const string BankKeyInvalid = "finance_bank_statement_bank_key_invalid";
    public const string CsvMappingNotFound = "finance_bank_statement_csv_mapping_not_found";
    public const string CsvMappingInactive = "finance_bank_statement_csv_mapping_inactive";
    public const string FormatNotSupported = "finance_bank_statement_format_not_supported";
    public const string DocumentUnavailable = "finance_bank_statement_document_unavailable";
    public const string DocumentFormatMismatch = "finance_bank_statement_document_format_mismatch";
    public const string AccountMismatch = "finance_bank_statement_account_mismatch";
    public const string FileAlreadyImported = "finance_bank_statement_file_already_imported";
    public const string FileInvalid = "finance_bank_statement_file_invalid";
    public const string NoLines = "finance_bank_statement_no_lines";
    public const string Unavailable = "finance_bank_statement_unavailable";
}

/// <summary>Technical bounds (storage and transport shape, not business policy).</summary>
public static class FinanceBankStatementLimitsV1
{
    public const int MaximumLines = 20_000;
    public const int MaximumIssues = 100;
    public const int DescriptionMaximumLength = 1000;
    public const int ReferenceMaximumLength = 128;
    public const int MaximumListedImports = 100;
}

/// <summary>
/// Reserves an import for an approved Finance bank account. The client chooses <c>ImportId</c> (the vault upload names it
/// as the owner record). <c>BankKey</c> is the Settings CSV mapping key, required for <see cref="FinanceBankStatementFormatV1.Csv"/>
/// and absent otherwise.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReserveFinanceBankStatementImportV1(
    [property: JsonRequired] Guid ImportId,
    [property: JsonRequired] Guid BankAccountId,
    [property: JsonRequired] FinanceBankStatementFormatV1 Format,
    string? BankKey);

/// <summary>Parses the named vault document version of the import (uploaded on this import record).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ParseFinanceBankStatementImportV1(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] Guid DocumentId,
    [property: JsonRequired] int DocumentVersion);

public sealed record FinanceBankStatementImportViewV1(
    Guid Id,
    Guid CompanyId,
    string Version,
    Guid BankAccountId,
    FinanceBankStatementFormatV1 Format,
    string? BankKey,
    FinanceBankStatementImportStateV1 State,
    Guid? DocumentId,
    int? DocumentVersion,
    string? DocumentSha256,
    Guid? CsvMappingProfileId,
    long? CsvMappingProfileVersion,
    int LineCount,
    int DuplicateLineCount,
    DateOnly? FirstTransactionOn,
    DateOnly? LastTransactionOn,
    string ReservedBy,
    DateTimeOffset ReservedAtUtc,
    string? ParsedBy,
    DateTimeOffset? ParsedAtUtc);

/// <summary>A recorded Finance movement the line may be. Its amount equals the line's exactly, so it is not repeated.</summary>
public sealed record FinanceBankStatementCandidateV1(
    FinanceBankStatementCandidateKindV1 Kind,
    Guid RecordId,
    DateOnly OccurredOn);

public sealed record FinanceBankStatementSuggestionV1(
    FinanceBankStatementMatchStatusV1 Status,
    FinanceBankStatementManualReasonV1? ManualReason,
    FinanceBankStatementCandidateV1? Candidate);

/// <summary>
/// One stored statement line. <c>Amount</c> and <c>Balance</c> are exact non-negative decimal text (<c>.</c> separator) as
/// the bank wrote them; <c>Direction</c> carries the sign. Text is stored as read, with a leading spreadsheet formula
/// character neutralized by a <c>'</c> prefix.
/// </summary>
public sealed record FinanceBankStatementLineViewV1(
    Guid Id,
    int LineNumber,
    DateOnly TransactionOn,
    DateOnly? ValueOn,
    FinanceBankStatementDirectionV1 Direction,
    string Amount,
    string? Balance,
    string? CurrencyText,
    string? Reference,
    string Description,
    string? CounterpartyIban,
    FinanceBankStatementSuggestionV1 Suggestion);

public sealed record FinanceBankStatementImportDetailV1(
    FinanceBankStatementImportViewV1 Import,
    IReadOnlyList<FinanceBankStatementLineViewV1> Lines,
    int SuggestedCount,
    int ManualCount);

public sealed record FinanceBankStatementImportListV1(Guid CompanyId, IReadOnlyList<FinanceBankStatementImportViewV1> Imports);

/// <summary>One reason the file was refused (1-based physical line number; 0 for the whole file).</summary>
public sealed record FinanceBankStatementIssueV1(int LineNumber, string Field, string Code);

public sealed record FinanceBankStatementCommandResultV1(
    CommandOutcome Outcome,
    FinanceBankStatementImportViewV1? Resource,
    string? Code,
    IReadOnlyList<FinanceBankStatementIssueV1> Issues,
    bool Replayed = false);

public sealed record FinanceBankStatementDetailResultV1(
    [property: JsonConverter(typeof(JsonStringEnumConverter<ReadOutcome>))] ReadOutcome Outcome,
    FinanceBankStatementImportDetailV1? Detail);

public sealed record FinanceBankStatementListResultV1(
    [property: JsonConverter(typeof(JsonStringEnumConverter<ReadOutcome>))] ReadOutcome Outcome,
    FinanceBankStatementImportListV1? List);

public interface IFinanceBankStatementImportServiceV1
{
    Task<FinanceBankStatementCommandResultV1> ReserveAsync(FinanceContext context, CommandMetadata metadata,
        ReserveFinanceBankStatementImportV1 request, CancellationToken cancellationToken);

    Task<FinanceBankStatementCommandResultV1> ParseAsync(FinanceContext context, CommandMetadata metadata, Guid importId,
        ParseFinanceBankStatementImportV1 request, CancellationToken cancellationToken);

    Task<FinanceBankStatementDetailResultV1> GetAuthorizedAsync(FinanceContext context, Guid importId,
        CancellationToken cancellationToken);

    Task<FinanceBankStatementListResultV1> ListAuthorizedAsync(FinanceContext context, Guid? bankAccountId,
        CancellationToken cancellationToken);

    /// <summary>
    /// B-26: whether the import exists in the context company and the actor may read or import statements (the vault's
    /// owner-link question). Never discloses another company's import.
    /// </summary>
    Task<ReadOutcome> CheckDocumentOwnerAsync(FinanceContext context, Guid importId, CancellationToken cancellationToken);
}

public enum FinanceBankStatementDocumentOutcomeV1 { Unavailable, Denied, NotFound, Found }

/// <summary>The exact vault version read for the import, with its server-classified kind and SHA-256.</summary>
public sealed record FinanceBankStatementDocumentV1(
    Guid CompanyId,
    Guid DocumentId,
    int Version,
    string Sha256,
    FinanceBankStatementFormatV1 Format,
    ReadOnlyMemory<byte> Content);

public sealed record FinanceBankStatementDocumentAnswerV1(
    FinanceBankStatementDocumentOutcomeV1 Outcome,
    FinanceBankStatementDocumentV1? Document);

/// <summary>
/// The host reads a statement file only when it is linked to this import in the vault (B-26 type list, malware scan and
/// owner checks stay the vault's). Finance reads no vault table.
/// </summary>
public interface IFinanceBankStatementDocumentReadV1
{
    Task<FinanceBankStatementDocumentAnswerV1> ReadAsync(FinanceContext context, Guid importId, Guid documentId,
        int version, CancellationToken cancellationToken);
}

/// <summary>The default until the host wires the vault: every read is Unavailable (fail-closed).</summary>
public sealed class UnavailableFinanceBankStatementDocumentRead : IFinanceBankStatementDocumentReadV1
{
    public Task<FinanceBankStatementDocumentAnswerV1> ReadAsync(FinanceContext context, Guid importId, Guid documentId,
        int version, CancellationToken cancellationToken) =>
        Task.FromResult(new FinanceBankStatementDocumentAnswerV1(FinanceBankStatementDocumentOutcomeV1.Unavailable, null));
}
