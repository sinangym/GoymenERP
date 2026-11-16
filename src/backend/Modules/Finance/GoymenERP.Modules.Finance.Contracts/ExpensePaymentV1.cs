using System.Globalization;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Contracts;

// FNS-D25 (SINAN_KARARLARI_2026-11-11_FINANS_F005.md, API-25): a payment that is not tied to a party (tax, social
// security, salary, rent, bank fee, loan interest) is entered for now with ONE expense item and a MANDATORY
// description. The item is kept by identity, not by text, so the records can move to the expense structure once it
// is specified (NEEDS-SPEC; its migration is a separate slice). Preparation and approval move no money; only the
// separately authorized, explicitly confirmed final record is the money fact (FNS-D11: refused, never clipped, when
// the treasury balance would go below zero).

[JsonConverter(typeof(JsonStringEnumConverter<ExpensePaymentSourceKind>))]
public enum ExpensePaymentSourceKind
{
    Bank = 0,
    Cash = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<ExpensePaymentStatus>))]
public enum ExpensePaymentStatus
{
    Draft = 0,
    Approved = 1,
    Recorded = 2
}

/// <summary>
/// The full preparation. <paramref name="TreasuryAccountId"/> names a bank account when
/// <paramref name="SourceKind"/> is Bank and a cash account when it is Cash. <paramref name="Description"/> is
/// mandatory (FNS-D25). Null optional values clear their fields on a replacement.
/// </summary>
public sealed record ExpensePaymentPreparation(
    ExpensePaymentSourceKind SourceKind,
    Guid TreasuryAccountId,
    Guid ExpenseItemId,
    MoneyText Money,
    ProfileReference Profile,
    DateOnly OccurredOn,
    DateOnly? ValueOn,
    string? Reference,
    string Description);

/// <summary>Creates or completely replaces a draft.</summary>
public sealed record ExpensePaymentDraftSaveRequest(
    Guid? PaymentId,
    string? ExpectedVersion,
    ExpensePaymentPreparation Preparation);

/// <summary>The approver must be a different actor from the draft preparer.</summary>
public sealed record ApproveExpensePaymentRequest(Guid PaymentId, string ExpectedVersion);

/// <summary>
/// The final record. <paramref name="PaymentConfirmed"/> is the actor's explicit statement that the payment left
/// the bank or the cash box; an unconfirmed record is refused.
/// </summary>
public sealed record RecordExpensePaymentRequest(Guid PaymentId, string ExpectedVersion, bool PaymentConfirmed);

public sealed record ExpensePaymentView(
    Guid Id,
    Guid CompanyId,
    ExpensePaymentStatus Status,
    ExpensePaymentPreparation Preparation,
    string PreparedBy,
    DateTimeOffset PreparedAtUtc,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc,
    string? RecordedBy,
    DateTimeOffset? RecordedAtUtc,
    bool PaymentConfirmed,
    string Version);

public sealed record ExpensePaymentPage(
    Guid CompanyId,
    IReadOnlyList<ExpensePaymentView> Items,
    string? NextCursor);

public sealed record ExpensePaymentCommandResult(
    CommandOutcome Outcome,
    ExpensePaymentView? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// One expense item. <paramref name="Code"/> is a stable technical key the client turns into its localized label;
/// the record keeps only <paramref name="Id"/>.
/// </summary>
public sealed record ExpenseItemView(Guid Id, string Code);

/// <summary>The expense items the company's payments may name today (FNS-D25: one item).</summary>
public sealed record ExpenseItemCatalogView(Guid CompanyId, IReadOnlyList<ExpenseItemView> Items);

/// <summary>
/// The expense items a payment may name. FNS-D25: one item today. The future expense structure (NEEDS-SPEC)
/// replaces this registration; the stored identities stay valid because the new structure starts from them.
/// </summary>
public interface IFinanceExpenseItemCatalog
{
    IReadOnlyList<ExpenseItemView> Items { get; }
}

/// <summary>
/// What a draft must name for the chosen source account and occurrence date: the account's currency and the
/// approved Settings money profile effective on that date. Read-only; it moves nothing and chooses no default.
/// </summary>
public sealed record ExpensePaymentDraftContextView(
    Guid CompanyId,
    ExpensePaymentSourceKind SourceKind,
    Guid TreasuryAccountId,
    Guid CurrencyId,
    ProfileReference Profile,
    DateOnly OccurredOn);

public enum TreasuryBalanceOutcome { Unavailable, Sufficient, Insufficient }

public sealed record TreasuryBalanceAnswer(TreasuryBalanceOutcome Outcome, string? Code)
{
    public const string UnavailableCode = "finance_treasury_balance_unavailable";
    public const string InsufficientCode = "finance_treasury_balance_insufficient";
}

/// <summary>
/// FNS-D11 / FNS-D22: before the final record the treasury account's CURRENT balance (at record time) must cover the
/// outgoing amount; a balance that would go below zero is refused with an explicit code, never clipped. FNS-D87: a bank
/// account never goes below zero — credit use (KMH/flexible commercial accounts included) is managed in separate credit
/// accounts, not as a negative bank balance — so an implementation grants no overdraft limit on a bank account. The
/// guard answers both the expense payment and the manual bank payment record. The implementation must protect the answer against
/// concurrent records in the same database transaction (version or lock). Until a balance source exists the
/// registered answer is <see cref="TreasuryBalanceOutcome.Unavailable"/> and the record is refused (fail-closed).
/// </summary>
public interface IFinanceTreasuryBalanceGuard
{
    Task<TreasuryBalanceAnswer> CheckOutgoingAsync(
        FinanceContext context,
        ExpensePaymentSourceKind sourceKind,
        Guid treasuryAccountId,
        MoneyText money,
        CancellationToken cancellationToken);
}

/// <summary>
/// Finance-owned FNS-D25 contract. Implementations must fence company/action authority after awaited owner reads
/// and immediately before persistence; a successful Record is the sole money effect.
/// </summary>
public interface IExpensePaymentService
{
    Task<ExpensePaymentCommandResult> DraftSaveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ExpensePaymentDraftSaveRequest request,
        CancellationToken cancellationToken);

    Task<ExpensePaymentCommandResult> ApproveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ApproveExpensePaymentRequest request,
        CancellationToken cancellationToken);

    Task<ExpensePaymentCommandResult> RecordAsync(
        FinanceContext context,
        CommandMetadata metadata,
        RecordExpensePaymentRequest request,
        CancellationToken cancellationToken);

    Task<ReadResult<ExpensePaymentPage>> ListAuthorizedAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken);

    Task<ReadResult<ExpensePaymentView>> DetailAuthorizedAsync(
        FinanceContext context,
        Guid paymentId,
        CancellationToken cancellationToken);

    /// <summary>The items a draft may name; the draft-read permission is fenced before and after.</summary>
    Task<ReadResult<ExpenseItemCatalogView>> ExpenseItemsAuthorizedAsync(
        FinanceContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// The currency and money profile a draft names for an eligible source account on a date, under the draft-save
    /// permission (fenced before and after). An ineligible or unknown account is NotFound; a missing or
    /// non-matching Settings profile is Unavailable (a setup gap, never a default).
    /// </summary>
    Task<ReadResult<ExpensePaymentDraftContextView>> DraftContextAuthorizedAsync(
        FinanceContext context,
        ExpensePaymentSourceKind sourceKind,
        Guid treasuryAccountId,
        DateOnly occurredOn,
        CancellationToken cancellationToken);
}

public static class ExpensePaymentValidator
{
    public const int DescriptionMaximumLength = 1000;
    public const int ReferenceMaximumLength = 128;

    public static bool IsValidDraftIdentity(ExpensePaymentDraftSaveRequest request) =>
        request.PaymentId is null
            ? request.ExpectedVersion is null
            : request.PaymentId != Guid.Empty && IsVersion(request.ExpectedVersion);

    /// <summary>
    /// The shape rules that need no owner read: identities, a known source kind, the mandatory description
    /// (FNS-D25), text lengths and the value date not before the occurrence date.
    /// </summary>
    public static IReadOnlyList<ValidationIssue> ValidateShape(ExpensePaymentPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var issues = new List<ValidationIssue>();
        if (!Enum.IsDefined(preparation.SourceKind))
            issues.Add(new ValidationIssue("sourceKind", "expense_payment_source_invalid"));
        if (preparation.TreasuryAccountId == Guid.Empty || preparation.Money.CurrencyId == Guid.Empty)
            issues.Add(new ValidationIssue("identity", "identity_required"));
        if (preparation.ExpenseItemId == Guid.Empty)
            issues.Add(new ValidationIssue("expenseItemId", "expense_item_required"));
        if (string.IsNullOrWhiteSpace(preparation.Description))
            issues.Add(new ValidationIssue("description", "expense_payment_description_required"));
        else if (preparation.Description.Length > DescriptionMaximumLength)
            issues.Add(new ValidationIssue("description", "expense_payment_description_too_long"));
        if (preparation.Reference is { } reference
            && (string.IsNullOrWhiteSpace(reference) || reference.Length > ReferenceMaximumLength))
            issues.Add(new ValidationIssue("reference", "expense_payment_reference_invalid"));
        if (preparation.ValueOn is { } valueOn && valueOn < preparation.OccurredOn)
            issues.Add(new ValidationIssue("valueOn", "value_date_before_occurred_date"));
        return issues;
    }

    public static bool CanApprove(
        ExpensePaymentStatus status,
        string preparedBy,
        string actorId,
        ApproveExpensePaymentRequest request) =>
        status == ExpensePaymentStatus.Draft
        && request.PaymentId != Guid.Empty
        && IsVersion(request.ExpectedVersion)
        && !string.IsNullOrWhiteSpace(preparedBy)
        && !string.IsNullOrWhiteSpace(actorId)
        && !string.Equals(preparedBy, actorId, StringComparison.Ordinal);

    public static IReadOnlyList<ValidationIssue> ValidateRecord(
        ExpensePaymentStatus status,
        RecordExpensePaymentRequest request)
    {
        var issues = new List<ValidationIssue>();
        if (request.PaymentId == Guid.Empty)
            issues.Add(new ValidationIssue("paymentId", "identity_required"));
        if (!IsVersion(request.ExpectedVersion))
            issues.Add(new ValidationIssue("expectedVersion", "expected_version_invalid"));
        if (status != ExpensePaymentStatus.Approved)
            issues.Add(new ValidationIssue("status", "expense_payment_not_approved"));
        if (!request.PaymentConfirmed)
            issues.Add(new ValidationIssue("paymentConfirmed", "expense_payment_not_confirmed"));
        return issues;
    }

    /// <summary>
    /// The approved Settings money profile must be the one the client named, for this company, currency and
    /// operation, active and effective on the occurrence date; the amount must be an exact positive decimal under
    /// the profile's scale (never rounded). A missing profile is a provider gap, not a validation failure.
    /// </summary>
    public static IReadOnlyList<ValidationIssue> ValidateMoneyProfile(
        ExpensePaymentPreparation preparation,
        Guid companyId,
        MoneyProfileV1? profile,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var issues = new List<ValidationIssue>();
        if (profile is null)
        {
            issues.Add(new ValidationIssue("profile", "settings_provider_unavailable"));
            return issues;
        }

        if (profile.Profile is null || profile.Profile.ProfileId == Guid.Empty || profile.Profile.Version <= 0)
        {
            issues.Add(new ValidationIssue("profile", "settings_profile_invalid"));
            return issues;
        }

        if (profile.CompanyId != companyId || profile.CurrencyId != preparation.Money.CurrencyId
            || !string.Equals(profile.Operation, operation, StringComparison.Ordinal) || !profile.IsActive
            || preparation.Profile is null
            || profile.Profile.ProfileId.ToString("D") != preparation.Profile.ProfileId
            || profile.Profile.Version.ToString(CultureInfo.InvariantCulture) != preparation.Profile.Version
            || preparation.OccurredOn < profile.EffectiveFrom
            || (profile.EffectiveThrough is { } through && preparation.OccurredOn > through))
            issues.Add(new ValidationIssue("profile", "profile_mismatch"));
        try
        {
            _ = ExactPositiveDecimalV1.Parse(preparation.Money.Amount, profile.Scale);
        }
        catch (SettingsPrerequisiteException exception)
        {
            issues.Add(new ValidationIssue("money.amount", exception.Code));
        }

        return issues;
    }

    private static bool IsVersion(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version > 0
        && string.Equals(version.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal);
}
