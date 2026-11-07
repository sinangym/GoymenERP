using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Accounting.Application;

// B-04a (Sinan, #66 6707619685): invoice finalization creates a DRAFT voucher whose accounts are NOT hard-coded
// but mapped in Settings ("Ayarlar") per product type. This file is the Accounting-owned, versioned and
// maker-checker activated mapping catalog that holds those choices. It deliberately chooses none of them:
// how 150/153 is told apart (item type or line purpose), service purchases and the export flag are open
// (NEEDS-SINAN #66 6707675903), so every one of them is an optional selector a company configures, and a
// question no row answers is an explicit "unmapped", never a guessed account.
// Sinan #66 6707864938 then chose how a company fills it: 150/153 by the purchase LINE PURPOSE (production input /
// resale), service purchases to the expense key (770), scrap sales to revenue with an ItemType selector, 601 by
// the invoice's export scenario (IsExport) and 391/191 optionally per VAT rate (VatRateCode). Those are DATA rows
// of a version, not code: the catalog keeps every selector optional and outranks none of them.

public enum AccountingPostingAccountMappingStatusV1
{
    Prepared = 1,
    Active = 2,
    Superseded = 3,
}

/// <summary>The posting purposes a mapping row may serve. Exact, ordinal, lower-case text.</summary>
public static class AccountingPostingAccountMappingPurposesV1
{
    public const string SalesInvoice = "sales-invoice";
    public const string PurchaseInvoice = "purchase-invoice";

    public static IReadOnlyList<string> All { get; } = [SalesInvoice, PurchaseInvoice];

    public static bool IsKnown(string? purpose) => purpose is SalesInvoice or PurchaseInvoice;
}

/// <summary>
/// The mapping keys of each purpose (B-04: sales receivable/revenue/output VAT; purchases payable,
/// inventory, input VAT, purchase withholding and an expense key for whatever a company decides to
/// expense). A key names a role in the voucher, never an account number.
/// </summary>
public static class AccountingPostingAccountMappingKeysV1
{
    public const string Receivable = "receivable";
    public const string Revenue = "revenue";
    public const string VatOutput = "vat-output";

    /// <summary>
    /// The sales-side VAT withholding the buyer keeps (KLN-D12): its counter-account is open (NEEDS-SINAN
    /// #66 6707860683); the key only lets a company configure it, and with no row the line is unmapped.
    /// </summary>
    public const string WithholdingReceivable = "withholding-receivable";
    public const string Payable = "payable";
    public const string Inventory = "inventory";
    public const string VatInput = "vat-input";
    public const string WithholdingPayable = "withholding-payable";
    public const string Expense = "expense";

    public static IReadOnlyList<string> SalesInvoice { get; } = [Receivable, Revenue, VatOutput, WithholdingReceivable];

    public static IReadOnlyList<string> PurchaseInvoice { get; } =
        [Payable, Inventory, VatInput, WithholdingPayable, Expense];

    public static bool IsAllowed(string? purpose, string? key) => purpose switch
    {
        AccountingPostingAccountMappingPurposesV1.SalesInvoice =>
            key is Receivable or Revenue or VatOutput or WithholdingReceivable,
        AccountingPostingAccountMappingPurposesV1.PurchaseInvoice =>
            key is Payable or Inventory or VatInput or WithholdingPayable or Expense,
        _ => false,
    };
}

public static class AccountingPostingAccountMappingCommandNamesV1
{
    public const string Prepare = "accounting.posting-account-mapping.prepare.v1";
    public const string Activate = "accounting.posting-account-mapping.activate.v1";
}

/// <summary>
/// Technical names only (B-04a). Central registration grants none of them to any role: who reads, prepares
/// or activates the mapping is a human grant decision.
/// </summary>
public static class AccountingPostingAccountMappingPermissionNamesV1
{
    public const string Read = "accounting.posting-mappings.read";
    public const string Prepare = "accounting.posting-mappings.prepare";
    public const string Activate = "accounting.posting-mappings.activate";

    public static IReadOnlyList<string> All { get; } = [Read, Prepare, Activate];
}

public static class AccountingPostingAccountMappingCodesV1
{
    public const string RowsRequired = "accounting_posting_mapping_rows_required";
    public const string RowLimitExceeded = "accounting_posting_mapping_row_limit_exceeded";
    public const string PurposeUnknown = "accounting_posting_mapping_purpose_unknown";
    public const string KeyUnknown = "accounting_posting_mapping_key_unknown";
    public const string SelectorInvalid = "accounting_posting_mapping_selector_invalid";
    public const string DuplicateRow = "accounting_posting_mapping_duplicate_row";
    public const string AccountUnknown = "accounting_posting_mapping_account_unknown";
    public const string AccountPlanUnavailable = "accounting_posting_mapping_account_plan_unavailable";
    public const string AccountNotInActivePlan = "accounting_posting_mapping_account_not_in_active_plan";
    public const string NotFound = "accounting_posting_mapping_not_found";
    public const string NotPrepared = "accounting_posting_mapping_not_prepared";
    public const string SelfActivation = "accounting_posting_mapping_self_activation";
    public const string SequenceConflict = "accounting_posting_mapping_sequence_conflict";

    /// <summary>The route-exact 422 codes of the prepare command.</summary>
    public static IReadOnlyList<string> PrepareUnprocessable { get; } =
    [
        RowsRequired, RowLimitExceeded, PurposeUnknown, KeyUnknown, SelectorInvalid, DuplicateRow, AccountUnknown,
    ];
}

/// <summary>Technical bounds of one version payload; they say nothing about which accounts a company uses.</summary>
public static class AccountingPostingAccountMappingLimitsV1
{
    public const int MaximumRows = 1_000;
    public const int MaximumSelectorLength = 64;
}

/// <summary>One requested row. Null selectors match only a question that leaves that dimension unknown.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccountingPostingAccountMappingRowRequestV1(
    string Purpose,
    string MappingKey,
    string? ItemType,
    string? LinePurpose,
    bool? IsExport,
    Guid AccountId,
    string? VatRateCode = null);

/// <summary>
/// The whole version payload. <see cref="ExpectedLatestMappingVersion"/> is the newest mapping version the
/// preparer saw (0 when none): another preparation in between is a recorded conflict, not a silent fork.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrepareAccountingPostingAccountMappingRequestV1(
    long ExpectedLatestMappingVersion,
    DateOnly EffectiveFrom,
    IReadOnlyList<AccountingPostingAccountMappingRowRequestV1>? Rows);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivateAccountingPostingAccountMappingRequestV1(
    long ExpectedVersion,
    long ExpectedActiveMappingVersion);

/// <summary>A validated row as the store receives it.</summary>
public sealed record AccountingPostingAccountMappingDraftRowV1(
    string Purpose,
    string MappingKey,
    string? ItemType,
    string? LinePurpose,
    bool? IsExport,
    Guid AccountId,
    string? VatRateCode = null);

/// <summary>A stored row: the account code is the active plan's code frozen at preparation.</summary>
public sealed record AccountingPostingAccountMappingEntryV1(
    int LineNumber,
    string Purpose,
    string MappingKey,
    string? ItemType,
    string? LinePurpose,
    bool? IsExport,
    Guid AccountId,
    string AccountCode,
    string? VatRateCode = null);

public sealed record AccountingPostingAccountMappingRecordV1(
    Guid CompanyId,
    long MappingVersion,
    DateOnly EffectiveFrom,
    AccountingPostingAccountMappingStatusV1 Status,
    long Version,
    string AccountPlanSourceVersion,
    string AccountPlanSourceSha256,
    Guid PreparedByUserId,
    DateTimeOffset PreparedAtUtc,
    Guid? ActivatedByUserId,
    DateTimeOffset? ActivatedAtUtc,
    IReadOnlyList<AccountingPostingAccountMappingEntryV1> Rows);

public sealed record AccountingPostingAccountMappingRowViewV1(
    string Purpose,
    string MappingKey,
    string? ItemType,
    string? LinePurpose,
    bool? IsExport,
    Guid AccountId,
    string AccountCode,
    string? VatRateCode = null);

public sealed record AccountingPostingAccountMappingViewV1(
    Guid CompanyId,
    long MappingVersion,
    DateOnly EffectiveFrom,
    string Status,
    long Version,
    string AccountPlanSourceVersion,
    string AccountPlanSourceSha256,
    Guid PreparedByUserId,
    DateTimeOffset PreparedAtUtc,
    Guid? ActivatedByUserId,
    DateTimeOffset? ActivatedAtUtc,
    IReadOnlyList<AccountingPostingAccountMappingRowViewV1> Rows);

/// <summary>A store refusal with the field it is about (a row index for an account, for example).</summary>
public sealed class AccountingPostingAccountMappingRefusalException(string code, string? field = null)
    : Exception($"The posting account mapping command was refused: {code}.")
{
    public string Code { get; } = code;
    public string? Field { get; } = field;
}

/// <summary>The newest or active mapping version is not the one the caller named.</summary>
public sealed class AccountingPostingAccountMappingSequenceConflictException(
    string field,
    long expectedVersion,
    long currentVersion)
    : Exception("The posting account mapping sequence changed since it was read.")
{
    public string Field { get; } = field;
    public long ExpectedVersion { get; } = expectedVersion;
    public long CurrentVersion { get; } = currentVersion;
}

public sealed class AccountingPostingAccountMappingAuthorityDeniedException()
    : Exception("The posting account mapping authority is no longer valid.");

public interface IAccountingPostingAccountMappingStoreV1
{
    Task<AccountingPostingAccountMappingRecordV1?> ReadAsync(
        long mappingVersion,
        CancellationToken cancellationToken = default);

    Task<AccountingPostingAccountMappingRecordV1?> ReadActiveAsync(CancellationToken cancellationToken = default);

    Task<AccountingPostingAccountMappingRecordV1> PrepareAsync(
        long expectedLatestMappingVersion,
        DateOnly effectiveFrom,
        IReadOnlyList<AccountingPostingAccountMappingDraftRowV1> rows,
        Guid preparedByUserId,
        DateTimeOffset preparedAtUtc,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<AccountingPostingAccountMappingRecordV1> ActivateAsync(
        long mappingVersion,
        long expectedVersion,
        long expectedActiveMappingVersion,
        Guid activatedByUserId,
        DateTimeOffset activatedAtUtc,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>The shape rules of one version payload: pure, deterministic and free of any account choice.</summary>
public static class AccountingPostingAccountMappingPayloadV1
{
    public sealed record Issue(string Code, string Field);

    public static Issue? Validate(
        IReadOnlyList<AccountingPostingAccountMappingRowRequestV1>? rows,
        out IReadOnlyList<AccountingPostingAccountMappingDraftRowV1> validated)
    {
        validated = [];
        if (rows is null || rows.Count == 0)
        {
            return new Issue(AccountingPostingAccountMappingCodesV1.RowsRequired, "rows");
        }

        if (rows.Count > AccountingPostingAccountMappingLimitsV1.MaximumRows)
        {
            return new Issue(AccountingPostingAccountMappingCodesV1.RowLimitExceeded, "rows");
        }

        var result = new List<AccountingPostingAccountMappingDraftRowV1>(rows.Count);
        var seen = new HashSet<(string, string, string?, string?, bool?, string?)>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var prefix = $"rows[{index}]";
            if (row is null)
            {
                return new Issue(AccountingPostingAccountMappingCodesV1.RowsRequired, prefix);
            }

            if (!AccountingPostingAccountMappingPurposesV1.IsKnown(row.Purpose))
            {
                return new Issue(AccountingPostingAccountMappingCodesV1.PurposeUnknown, prefix + ".purpose");
            }

            if (!AccountingPostingAccountMappingKeysV1.IsAllowed(row.Purpose, row.MappingKey))
            {
                return new Issue(AccountingPostingAccountMappingCodesV1.KeyUnknown, prefix + ".mappingKey");
            }

            if (!IsSelector(row.ItemType))
            {
                return new Issue(AccountingPostingAccountMappingCodesV1.SelectorInvalid, prefix + ".itemType");
            }

            if (!IsSelector(row.LinePurpose))
            {
                return new Issue(AccountingPostingAccountMappingCodesV1.SelectorInvalid, prefix + ".linePurpose");
            }

            // Sinan #66 6707864938: 391/191 may optionally be split per VAT rate; only a VAT key names a rate.
            if (!IsSelector(row.VatRateCode)
                || (row.VatRateCode is not null && row.MappingKey is not (AccountingPostingAccountMappingKeysV1.VatOutput
                    or AccountingPostingAccountMappingKeysV1.VatInput)))
            {
                return new Issue(AccountingPostingAccountMappingCodesV1.SelectorInvalid, prefix + ".vatRateCode");
            }

            if (row.AccountId == Guid.Empty)
            {
                return new Issue(AccountingPostingAccountMappingCodesV1.AccountUnknown, prefix + ".accountId");
            }

            if (!seen.Add((row.Purpose, row.MappingKey, row.ItemType, row.LinePurpose, row.IsExport, row.VatRateCode)))
            {
                return new Issue(AccountingPostingAccountMappingCodesV1.DuplicateRow, prefix);
            }

            result.Add(new AccountingPostingAccountMappingDraftRowV1(
                row.Purpose, row.MappingKey, row.ItemType, row.LinePurpose, row.IsExport, row.AccountId,
                row.VatRateCode));
        }

        validated = result;
        return null;
    }

    /// <summary>
    /// A selector is an exact code (for example a Product item type such as <c>Steel</c>, or a line purpose a
    /// company names): 1-64 ASCII letters, digits, '.', '_' or '-', starting with a letter or digit. It is
    /// compared ordinally; it is never trimmed, case-folded or looked up here.
    /// </summary>
    public static bool IsSelector(string? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value.Length is 0 or > AccountingPostingAccountMappingLimitsV1.MaximumSelectorLength
            || !char.IsAsciiLetterOrDigit(value[0]))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}

public enum AccountingPostingAccountMappingMatchKindV1
{
    Unmapped = 1,
    Mapped = 2,
    Ambiguous = 3,
}

/// <summary>
/// The resolution rule, pure and deterministic. A row matches when every selector it names equals the
/// question's value (a named selector never matches an unknown question value); the match with the most
/// named selectors wins. Two or more winners at that level are <see cref="AccountingPostingAccountMappingMatchKindV1.Ambiguous"/>
/// and no row is <see cref="AccountingPostingAccountMappingMatchKindV1.Unmapped"/>: no selector outranks
/// another, no default is invented.
/// </summary>
public static class AccountingPostingAccountMappingMatcherV1
{
    public static (AccountingPostingAccountMappingMatchKindV1 Kind, AccountingPostingAccountMappingEntryV1? Entry) Match(
        IEnumerable<AccountingPostingAccountMappingEntryV1> entries,
        string purpose,
        string mappingKey,
        string? itemType,
        string? linePurpose,
        bool? isExport,
        string? vatRateCode = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var best = -1;
        var winners = new List<AccountingPostingAccountMappingEntryV1>();
        foreach (var entry in entries)
        {
            if (!string.Equals(entry.Purpose, purpose, StringComparison.Ordinal)
                || !string.Equals(entry.MappingKey, mappingKey, StringComparison.Ordinal)
                || (entry.ItemType is not null && !string.Equals(entry.ItemType, itemType, StringComparison.Ordinal))
                || (entry.LinePurpose is not null
                    && !string.Equals(entry.LinePurpose, linePurpose, StringComparison.Ordinal))
                || (entry.IsExport is { } export && export != isExport)
                || (entry.VatRateCode is not null
                    && !string.Equals(entry.VatRateCode, vatRateCode, StringComparison.Ordinal)))
            {
                continue;
            }

            var specificity = (entry.ItemType is null ? 0 : 1) + (entry.LinePurpose is null ? 0 : 1)
                + (entry.IsExport is null ? 0 : 1) + (entry.VatRateCode is null ? 0 : 1);
            if (specificity > best)
            {
                best = specificity;
                winners.Clear();
            }

            if (specificity == best)
            {
                winners.Add(entry);
            }
        }

        return winners.Count switch
        {
            0 => (AccountingPostingAccountMappingMatchKindV1.Unmapped, null),
            1 => (AccountingPostingAccountMappingMatchKindV1.Mapped, winners[0]),
            _ => (AccountingPostingAccountMappingMatchKindV1.Ambiguous, null),
        };
    }
}
