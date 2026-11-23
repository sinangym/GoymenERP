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

    /// <summary>FNS-D113: confirm a line-to-record match (the only way a match becomes final) and reverse a confirmed one.</summary>
    public const string Match = "finance.bank-statements.match";
}

/// <summary>
/// FNS-D52: the file kinds a statement may be. <see cref="Xlsx"/> is read only through a closed list of known header names
/// (FinanceBankStatementXlsxHeadersV1, FNS-D120); any other header refuses the file.
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
/// FNS-D13 / FNS-D113: a statement line never creates a movement. A suggestion only names an EXISTING Finance record whose
/// amount is exactly the line's (no tolerance); a match becomes final only when a person confirms it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceBankStatementMatchStatusV1>))]
public enum FinanceBankStatementMatchStatusV1
{
    /// <summary>Exactly one unmatched recorded movement has the same direction, exact amount and date; a person must confirm it.</summary>
    Suggested = 1,

    /// <summary>A person decides ("elle"); for FNS-D13 an unmatched line is a "elle kaydedilmemiş hareket" warning.</summary>
    Manual = 2,

    /// <summary>A person confirmed the match (FNS-D113); it stays until a reversal record is written.</summary>
    Confirmed = 3,
}

/// <summary>Why a line is <see cref="FinanceBankStatementMatchStatusV1.Manual"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceBankStatementManualReasonV1>))]
public enum FinanceBankStatementManualReasonV1
{
    /// <summary>No unmatched recorded movement of this bank account has the same direction, exact amount and date.</summary>
    NoExactCandidate = 1,

    /// <summary>Exact candidates exist, but the line's IBAN proves another counterparty for every one of them.</summary>
    CounterpartyMismatch = 2,

    /// <summary>More than one movement passes, or another line of the same import points at the same movement.</summary>
    Ambiguous = 3,
}

/// <summary>
/// What the line's counterparty IBAN says about a candidate (FNS-D114): the cari's IBAN list (Counterparty owner) for a
/// party record, the approved IBAN of the other own bank account for a transfer. An IBAN is never learned from a statement.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceBankStatementCounterpartyCheckV1>))]
public enum FinanceBankStatementCounterpartyCheckV1
{
    /// <summary>The line's IBAN belongs to the record's counterparty.</summary>
    Verified = 1,

    /// <summary>No comparison was possible (no single IBAN on the line, no IBAN list source, or a record without a party).</summary>
    NotAvailable = 2,

    /// <summary>The line's IBAN belongs to a different counterparty.</summary>
    Mismatch = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter<FinanceBankStatementMatchStateV1>))]
public enum FinanceBankStatementMatchStateV1
{
    Confirmed = 1,

    /// <summary>A reversal record undid the confirmation; the match row itself is kept (no deletion).</summary>
    Reversed = 2,
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
    public const string LineNotFound = "finance_bank_statement_line_not_found";
    public const string MatchNotFound = "finance_bank_statement_match_not_found";
    public const string RecordNotEligible = "finance_bank_statement_record_not_eligible";
    public const string CounterpartyMismatch = "finance_bank_statement_counterparty_mismatch";
    public const string AlreadyMatched = "finance_bank_statement_already_matched";
    public const string ReasonRequired = "finance_bank_statement_reason_required";
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
    public const int ReasonMaximumLength = 1000;
}

/// <summary>
/// An XLSX statement is read only when its header row uses these known names (compared trimmed, case-insensitive in the
/// Turkish culture); any other header refuses the file. The amount comes EITHER from one signed <see cref="Amount"/> column
/// (FNS-D120: minus = out of the account, plus = into it) OR from the explicit inflow/outflow pair ("Alacak"/"Giriş" = into
/// the account, "Borç"/"Çıkış" = out of it); a file with both is refused (FNS-D120).
/// </summary>
public static class FinanceBankStatementXlsxHeadersV1
{
    public static IReadOnlyList<string> TransactionDate { get; } = ["Tarih", "İşlem Tarihi"];
    public static IReadOnlyList<string> ValueDate { get; } = ["Valör", "Valör Tarihi"];
    public static IReadOnlyList<string> Description { get; } = ["Açıklama"];
    public static IReadOnlyList<string> Inflow { get; } = ["Alacak", "Giriş"];
    public static IReadOnlyList<string> Outflow { get; } = ["Borç", "Çıkış"];
    public static IReadOnlyList<string> Amount { get; } = ["Tutar", "İşlem Tutarı"];
    public static IReadOnlyList<string> Balance { get; } = ["Bakiye"];
    public static IReadOnlyList<string> Reference { get; } = ["Referans", "Dekont No"];
    public static IReadOnlyList<string> Currency { get; } = ["Para Birimi", "Döviz"];
}

/// <summary>
/// Reserves an import for an approved Finance bank account. The client chooses <c>ImportId</c> (the vault upload names it
/// as the owner record). <c>BankKey</c> is the Settings CSV mapping key, required for <see cref="FinanceBankStatementFormatV1.Csv"/>
/// and absent for MT940 and XLSX.
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
    DateOnly OccurredOn,
    FinanceBankStatementCounterpartyCheckV1 CounterpartyCheck);

/// <summary>
/// <c>Candidate</c> is set for Suggested and Confirmed; <c>Match</c> only for Confirmed (the active confirmation).
/// </summary>
public sealed record FinanceBankStatementSuggestionV1(
    FinanceBankStatementMatchStatusV1 Status,
    FinanceBankStatementManualReasonV1? ManualReason,
    FinanceBankStatementCandidateV1? Candidate,
    FinanceBankStatementMatchViewV1? Match = null);

/// <summary>
/// FNS-D113: a person's confirmation that the statement line IS the named recorded movement. Immutable; undone only by a
/// reversal record (<c>Reversal*</c>), never deleted.
/// </summary>
public sealed record FinanceBankStatementMatchViewV1(
    Guid Id,
    Guid CompanyId,
    Guid ImportId,
    Guid LineId,
    FinanceBankStatementCandidateKindV1 Kind,
    Guid RecordId,
    string Version,
    FinanceBankStatementMatchStateV1 State,
    FinanceBankStatementCounterpartyCheckV1 CounterpartyCheck,
    string ConfirmedBy,
    DateTimeOffset ConfirmedAtUtc,
    Guid? ReversalId,
    string? ReversedBy,
    DateTimeOffset? ReversedAtUtc,
    string? ReversalReason);

/// <summary>
/// Confirms one line of the import as one recorded movement of the same bank account with the same direction and exactly
/// the same amount (FNS-D113). The client chooses <c>MatchId</c>. Neither the line nor the record may hold another active match.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmFinanceBankStatementMatchV1(
    [property: JsonRequired] Guid MatchId,
    [property: JsonRequired] Guid LineId,
    [property: JsonRequired] FinanceBankStatementCandidateKindV1 Kind,
    [property: JsonRequired] Guid RecordId);

/// <summary>Reverses a confirmed match with a reversal record; <c>ExpectedVersion</c> is the match's version.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReverseFinanceBankStatementMatchV1(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] Guid ReversalId,
    [property: JsonRequired] string Reason);

public sealed record FinanceBankStatementMatchCommandResultV1(
    CommandOutcome Outcome,
    FinanceBankStatementMatchViewV1? Resource,
    string? Code,
    bool Replayed = false);

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

    Task<FinanceBankStatementMatchCommandResultV1> ConfirmMatchAsync(FinanceContext context, CommandMetadata metadata,
        Guid importId, ConfirmFinanceBankStatementMatchV1 request, CancellationToken cancellationToken);

    Task<FinanceBankStatementMatchCommandResultV1> ReverseMatchAsync(FinanceContext context, CommandMetadata metadata,
        Guid matchId, ReverseFinanceBankStatementMatchV1 request, CancellationToken cancellationToken);
}

public enum FinancePartyIbanOutcomeV1 { Unavailable, Available }

/// <summary>
/// FNS-D114: for each asked IBAN, the cari (party) ids whose card lists that IBAN in the context company. An IBAN missing
/// from the map belongs to no card. <see cref="FinancePartyIbanOutcomeV1.Unavailable"/> means no IBAN list source exists
/// or it cannot be read: party matching by IBAN is then off (never a guess).
/// </summary>
public sealed record FinancePartyIbanAnswerV1(
    FinancePartyIbanOutcomeV1 Outcome,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> PartiesByIban);

/// <summary>
/// FNS-D114: Finance asks the Counterparty owner which cari cards list a given IBAN. The owner implements it from the cari
/// card's IBAN list; Finance never stores or learns an IBAN from a statement.
/// </summary>
public interface IFinancePartyIbanReadPortV1
{
    Task<FinancePartyIbanAnswerV1> ReadAsync(FinanceContext context, IReadOnlyCollection<string> ibans,
        CancellationToken cancellationToken);
}

/// <summary>
/// The default while the cari card carries no IBAN list (Counterparty has none on this tree): always Unavailable, so party
/// IBAN matching stays off (FNS-D114 "port yoksa kapalı").
/// </summary>
public sealed class UnavailableFinancePartyIbanReadPort : IFinancePartyIbanReadPortV1
{
    public Task<FinancePartyIbanAnswerV1> ReadAsync(FinanceContext context, IReadOnlyCollection<string> ibans,
        CancellationToken cancellationToken) =>
        Task.FromResult(new FinancePartyIbanAnswerV1(FinancePartyIbanOutcomeV1.Unavailable,
            new Dictionary<string, IReadOnlyList<Guid>>()));
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
