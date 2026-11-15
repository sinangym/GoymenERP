using System.Globalization;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Contracts;

// FNS-D24 (SINAN_KARARLARI_2026-11-11_FINANS_F005.md; FINANS_HAZIRLIK_PLANI_v1.md §5.4 API-32/34): money moved between
// two treasury accounts of the same company (kasa↔banka, banka↔banka, kasa↔kasa) is ONE record. A transfer in the same
// currency carries the same amount on both legs; a currency exchange (döviz bozdurma / alış-satış) carries both amounts
// and the transaction rate on the same record, never two separate movements. The decision does not choose the rate
// source, the rate/amount consistency rule, the exchange difference or the accounting counterpart: the exchange
// commit stays fail-closed behind IFinanceCurrencyExchangeRateRule until those are decided (NEEDS-SINAN), and every
// commit needs the treasury balance guard to answer Sufficient (FNS-D11: refused, never clipped).

/// <summary>The kind of treasury account a transfer leg names.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CashTransferAccountKind>))]
public enum CashTransferAccountKind
{
    Bank = 0,
    Cash = 1
}

/// <summary>
/// <see cref="SameCurrency"/> is a virman (API-32); <see cref="CurrencyExchange"/> is a currency exchange between two
/// accounts in different currencies (API-34).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CashTransferKind>))]
public enum CashTransferKind
{
    SameCurrency = 0,
    CurrencyExchange = 1
}

/// <summary>
/// The full transfer. Each leg names its account, its amount in the account's currency and the approved Settings money
/// profile that governs that amount. <paramref name="Rate"/> is the transaction rate as target units per ONE source
/// unit, an exact positive decimal text; it is required for a currency exchange and must be null for a same-currency
/// transfer, whose two amounts are the same text in the same currency.
/// </summary>
public sealed record CashTransferPreparation(
    CashTransferKind Kind,
    CashTransferAccountKind SourceKind,
    Guid SourceAccountId,
    MoneyText SourceMoney,
    ProfileReference SourceProfile,
    CashTransferAccountKind TargetKind,
    Guid TargetAccountId,
    MoneyText TargetMoney,
    ProfileReference TargetProfile,
    string? Rate,
    DateOnly OccurredOn,
    DateOnly? ValueOn,
    string? Reference,
    string? Description);

/// <summary>
/// The commit. <paramref name="TransferConfirmed"/> is the actor's explicit statement that the money left the source
/// account and reached the target account; an unconfirmed commit is refused.
/// </summary>
public sealed record CommitCashTransferRequest(CashTransferPreparation Preparation, bool TransferConfirmed);

/// <summary>
/// The preview answer: the same checks as the commit (accounts, profiles, rate rule, period, balance) without writing
/// anything. <see cref="CommandOutcome.Succeeded"/> echoes the preparation the commit would record.
/// </summary>
public sealed record CashTransferPreviewResult(
    CommandOutcome Outcome,
    CashTransferPreparation? Preparation,
    IReadOnlyList<ValidationIssue> Issues);

public sealed record CashTransferView(
    Guid Id,
    Guid CompanyId,
    CashTransferPreparation Preparation,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    bool TransferConfirmed,
    string Version);

public sealed record CashTransferPage(
    Guid CompanyId,
    IReadOnlyList<CashTransferView> Items,
    string? NextCursor);

public sealed record CashTransferCommandResult(
    CommandOutcome Outcome,
    CashTransferView? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// What a leg must name for an eligible account on a date: the account's currency and the approved Settings money
/// profile effective on that date. Read-only; it moves nothing and chooses no default.
/// </summary>
public sealed record CashTransferAccountContextView(
    Guid CompanyId,
    CashTransferAccountKind AccountKind,
    Guid AccountId,
    Guid CurrencyId,
    ProfileReference Profile,
    DateOnly OccurredOn);

public enum CurrencyExchangeRateOutcome { Unavailable, Consistent, Inconsistent }

public sealed record CurrencyExchangeRateAnswer(CurrencyExchangeRateOutcome Outcome, string? Code)
{
    public const string UnavailableCode = "finance_exchange_rate_rule_unavailable";
    public const string InconsistentCode = "finance_exchange_rate_inconsistent";
}

/// <summary>
/// FNS-D24: whether a currency exchange's rate and its two amounts agree, and whether the rate is admissible for the
/// date (the rate source and any allowed deviation from the Settings rate record). None of this is decided yet
/// (NEEDS-SINAN: rounding of target = source × rate, quote direction, rate source, tolerance); the registered default
/// answers <see cref="CurrencyExchangeRateOutcome.Unavailable"/> and every exchange is refused (fail-closed). The
/// rule is a deterministic backend calculator; screens never recompute it.
/// </summary>
public interface IFinanceCurrencyExchangeRateRule
{
    Task<CurrencyExchangeRateAnswer> CheckAsync(
        FinanceContext context,
        MoneyText sourceMoney,
        MoneyText targetMoney,
        string rate,
        DateOnly occurredOn,
        CancellationToken cancellationToken);
}

/// <summary>
/// Finance-owned FNS-D24 contract (API-32/34). Implementations fence company/action authority after every awaited
/// owner read and immediately before persistence; a successful commit is the one money fact for both legs.
/// </summary>
public interface ICashTransferService
{
    /// <summary>The commit's checks under the commit permission; nothing is written, no key is consumed.</summary>
    Task<CashTransferPreviewResult> PreviewAsync(
        FinanceContext context,
        CashTransferPreparation preparation,
        CancellationToken cancellationToken);

    Task<CashTransferCommandResult> CommitAsync(
        FinanceContext context,
        CommandMetadata metadata,
        CommitCashTransferRequest request,
        CancellationToken cancellationToken);

    Task<ReadResult<CashTransferPage>> ListAuthorizedAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken);

    Task<ReadResult<CashTransferView>> DetailAuthorizedAsync(
        FinanceContext context,
        Guid transferId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The currency and money profile a leg names for an eligible account on a date, under the commit permission
    /// (fenced before and after). An ineligible or unknown account is NotFound; a missing or non-matching Settings
    /// profile is Unavailable (a setup gap, never a default).
    /// </summary>
    Task<ReadResult<CashTransferAccountContextView>> AccountContextAuthorizedAsync(
        FinanceContext context,
        CashTransferAccountKind accountKind,
        Guid accountId,
        DateOnly occurredOn,
        CancellationToken cancellationToken);
}

public static class CashTransferValidator
{
    public const int DescriptionMaximumLength = 1000;
    public const int ReferenceMaximumLength = 128;

    /// <summary>The stored rate's decimals; the same storage bound as the Settings rate record.</summary>
    public const int RateMaximumScale = ExchangeRateInputLimitsV1.RateMaximumScale;

    /// <summary>The stored rate's integer digits; the same storage bound as the Settings rate record.</summary>
    public const int RateMaximumIntegerDigits = ExchangeRateInputLimitsV1.RateMaximumIntegerDigits;

    /// <summary>
    /// The rules that need no owner read: known kinds, identities, two different accounts, the same-currency rule
    /// (equal currency, equal amount text, no rate) or the exchange rule (different currencies, a well-formed rate),
    /// text lengths and the value date not before the occurrence date.
    /// </summary>
    public static IReadOnlyList<ValidationIssue> ValidateShape(CashTransferPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var issues = new List<ValidationIssue>();
        if (!Enum.IsDefined(preparation.Kind))
            issues.Add(new ValidationIssue("kind", "cash_transfer_kind_invalid"));
        if (!Enum.IsDefined(preparation.SourceKind))
            issues.Add(new ValidationIssue("sourceKind", "cash_transfer_account_kind_invalid"));
        if (!Enum.IsDefined(preparation.TargetKind))
            issues.Add(new ValidationIssue("targetKind", "cash_transfer_account_kind_invalid"));
        if (preparation.SourceMoney is null || preparation.TargetMoney is null
            || preparation.SourceProfile is null || preparation.TargetProfile is null)
        {
            issues.Add(new ValidationIssue("identity", "identity_required"));
            return issues;
        }

        if (preparation.SourceAccountId == Guid.Empty || preparation.TargetAccountId == Guid.Empty
            || preparation.SourceMoney.CurrencyId == Guid.Empty || preparation.TargetMoney.CurrencyId == Guid.Empty)
            issues.Add(new ValidationIssue("identity", "identity_required"));
        if (preparation.SourceKind == preparation.TargetKind
            && preparation.SourceAccountId == preparation.TargetAccountId)
            issues.Add(new ValidationIssue("targetAccountId", "cash_transfer_same_account"));

        var sameCurrency = preparation.SourceMoney.CurrencyId == preparation.TargetMoney.CurrencyId;
        if (preparation.Kind == CashTransferKind.SameCurrency)
        {
            if (!sameCurrency)
                issues.Add(new ValidationIssue("targetMoney.currencyId", "cash_transfer_currency_mismatch"));
            if (!string.Equals(preparation.SourceMoney.Amount, preparation.TargetMoney.Amount, StringComparison.Ordinal))
                issues.Add(new ValidationIssue("targetMoney.amount", "cash_transfer_amount_mismatch"));
            if (preparation.Rate is not null)
                issues.Add(new ValidationIssue("rate", "cash_transfer_rate_not_allowed"));
        }
        else if (preparation.Kind == CashTransferKind.CurrencyExchange)
        {
            if (sameCurrency)
                issues.Add(new ValidationIssue("targetMoney.currencyId", "cash_transfer_currency_same"));
            if (preparation.Rate is null)
                issues.Add(new ValidationIssue("rate", "cash_transfer_rate_required"));
            else if (!IsRateText(preparation.Rate))
                issues.Add(new ValidationIssue("rate", "cash_transfer_rate_invalid"));
        }

        if (preparation.Description is { } description
            && (string.IsNullOrWhiteSpace(description) || description.Length > DescriptionMaximumLength))
            issues.Add(new ValidationIssue("description", "cash_transfer_description_invalid"));
        if (preparation.Reference is { } reference
            && (string.IsNullOrWhiteSpace(reference) || reference.Length > ReferenceMaximumLength))
            issues.Add(new ValidationIssue("reference", "cash_transfer_reference_invalid"));
        if (preparation.ValueOn is { } valueOn && valueOn < preparation.OccurredOn)
            issues.Add(new ValidationIssue("valueOn", "value_date_before_occurred_date"));
        return issues;
    }

    /// <summary>
    /// The approved Settings money profile of one leg must be the one the client named, for this company, the leg's
    /// currency and the operation, active and effective on the occurrence date; the leg's amount must be an exact
    /// positive decimal under the profile's scale (never rounded). A missing profile is a provider gap.
    /// </summary>
    public static IReadOnlyList<ValidationIssue> ValidateLegProfile(
        string field,
        string amountField,
        MoneyText money,
        ProfileReference reference,
        DateOnly occurredOn,
        Guid companyId,
        MoneyProfileV1? profile,
        string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(amountField);
        ArgumentNullException.ThrowIfNull(money);
        var issues = new List<ValidationIssue>();
        if (profile is null)
        {
            issues.Add(new ValidationIssue(field, "settings_provider_unavailable"));
            return issues;
        }

        if (profile.Profile is null || profile.Profile.ProfileId == Guid.Empty || profile.Profile.Version <= 0)
        {
            issues.Add(new ValidationIssue(field, "settings_profile_invalid"));
            return issues;
        }

        if (profile.CompanyId != companyId || profile.CurrencyId != money.CurrencyId
            || !string.Equals(profile.Operation, operation, StringComparison.Ordinal) || !profile.IsActive
            || reference is null
            || profile.Profile.ProfileId.ToString("D") != reference.ProfileId
            || profile.Profile.Version.ToString(CultureInfo.InvariantCulture) != reference.Version
            || occurredOn < profile.EffectiveFrom
            || (profile.EffectiveThrough is { } through && occurredOn > through))
            issues.Add(new ValidationIssue(field, "profile_mismatch"));
        try
        {
            _ = ExactPositiveDecimalV1.Parse(money.Amount, profile.Scale);
        }
        catch (SettingsPrerequisiteException exception)
        {
            issues.Add(new ValidationIssue(amountField, exception.Code));
        }

        return issues;
    }

    public static IReadOnlyList<ValidationIssue> ValidateCommit(CommitCashTransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.TransferConfirmed
            ? []
            : [new ValidationIssue("transferConfirmed", "cash_transfer_not_confirmed")];
    }

    /// <summary>
    /// An exact positive decimal (Settings' lexical rule: digits and at most one inner dot, no sign or exponent, never
    /// rounded) within the rate storage bounds: at most <see cref="RateMaximumScale"/> decimals and
    /// <see cref="RateMaximumIntegerDigits"/> integer digits.
    /// </summary>
    public static bool IsRateText(string? value)
    {
        try
        {
            _ = ExactPositiveDecimalV1.Parse(value, RateMaximumScale);
        }
        catch (SettingsPrerequisiteException)
        {
            return false;
        }

        var dot = value!.IndexOf('.', StringComparison.Ordinal);
        return (dot < 0 ? value.Length : dot) <= RateMaximumIntegerDigits;
    }
}
