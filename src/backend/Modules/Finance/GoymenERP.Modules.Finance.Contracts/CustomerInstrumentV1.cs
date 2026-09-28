using System.Globalization;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Contracts;

// DEC-FIN-016 (KILITLI, K1): "Çek ve senet durumları portföy, banka, teminat, ciro, tahsil, karşılıksız, iade/protesto
// ve ters akışı kapsar." DEC-FIN-006: a cheque or a note is a financial SOURCE that uses the same distribution engine
// (F-03); a distribution never creates a second money movement.
//
// First release (this contract): a CUSTOMER cheque or promissory note received into the portfolio, and its status
// lifecycle up to collection:
//
//   receive            → InPortfolio (no money effect; the customer's debt is NOT closed yet)
//   DepositForCollection  InPortfolio → AtBankForCollection (a Finance bank account; no money effect)
//   WithdrawFromBank      AtBankForCollection → InPortfolio (the pre-collection reverse flow; no money effect)
//   collect            InPortfolio | AtBankForCollection → Collected — THE ONLY MONEY EFFECT: one recorded receipt source
//                      and exactly one money event at the face value, which F-03 distributes like any receipt
//   Dishonour          InPortfolio | AtBankForCollection → Bounced (cheque, "karşılıksız") | Protested (note, "protesto");
//                      no money effect, because nothing was credited before collection
//   ReturnToCustomer   InPortfolio | Bounced | Protested → ReturnedToCustomer ("iade"); no money effect
//
// Not here, each behind the blocker that holds it (see the module README):
//  - "teminat" (bank collateral) and its risk/limit effect: OPEN-010 (haircut/risk policy) and no credit-facility master;
//  - "ciro" (endorsement to a supplier): the supplier-side payment effect and the moment the customer is credited for an
//    instrument that is never collected are not decided (NEEDS-BUSINESS, see README);
//  - the reverse flow AFTER collection (a collected cheque returned by the bank): the collection's money event is a
//    kind of its own, so FIN-T06's reversal refuses it (finance_reversal_source_not_original_receipt) until decided;
//  - the journal (portfolio / bank / customer accounts): chart of accounts NEEDS-BUSINESS 5740501573 and voucher
//    approval 5740501655 (ACC-V1), as for every Finance source;
//  - given (outgoing) cheques/notes, DBS: outside this contract (DBS is DEC-FIN-008 / OPEN-010).

/// <summary>A customer instrument's kind (DEC-FIN-016 "çek ve senet").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CustomerInstrumentKind>))]
public enum CustomerInstrumentKind
{
    /// <summary>Müşteri çeki: drawn on a bank, so the bank name is required.</summary>
    Cheque = 1,

    /// <summary>Müşteri senedi (bono): no drawee bank.</summary>
    PromissoryNote = 2,
}

/// <summary>The decided statuses the first release implements (DEC-FIN-016). Collected and ReturnedToCustomer are final.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CustomerInstrumentStatus>))]
public enum CustomerInstrumentStatus
{
    InPortfolio = 1,
    AtBankForCollection = 2,
    Collected = 3,

    /// <summary>A cheque the bank refused to pay ("karşılıksız").</summary>
    Bounced = 4,

    /// <summary>A note protested for non-payment ("protesto").</summary>
    Protested = 5,

    ReturnedToCustomer = 6,
}

/// <summary>The status changes WITHOUT a money effect. Collection is its own command: it is the one money effect.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CustomerInstrumentTransition>))]
public enum CustomerInstrumentTransition
{
    DepositForCollection = 1,
    WithdrawFromBank = 2,
    Dishonour = 3,
    ReturnToCustomer = 4,
}

/// <summary>What the reading actor may do next with an instrument: its state allows it AND a fresh grant allows it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CustomerInstrumentAction>))]
public enum CustomerInstrumentAction
{
    DepositForCollection = 1,
    WithdrawFromBank = 2,
    Dishonour = 3,
    ReturnToCustomer = 4,
    Collect = 5,
}

/// <summary>
/// Receive a customer cheque or note into the portfolio. The company and actor come from the server context; the
/// Settings money profile is resolved by the server for the currency and <see cref="ReceivedOn"/>. No money effect.
/// </summary>
public sealed record CustomerInstrumentReceiveRequest(
    CustomerInstrumentKind Kind,
    Guid PartyId,
    Guid CompanyAccountId,
    string SerialNumber,
    string Drawer,
    string? BankName,
    string? BranchName,
    DateOnly DueOn,
    DateOnly ReceivedOn,
    MoneyText Money,
    string? Description);

/// <summary>
/// A status change without a money effect. <see cref="TreasuryAccountId"/> is the Finance bank account and is given
/// only for <see cref="CustomerInstrumentTransition.DepositForCollection"/>.
/// </summary>
public sealed record CustomerInstrumentStatusChangeRequest(
    Guid InstrumentId,
    string ExpectedVersion,
    CustomerInstrumentTransition Transition,
    DateOnly OccurredOn,
    Guid? TreasuryAccountId,
    string? Note);

/// <summary>
/// Collect the instrument into a Finance bank account: one recorded receipt source and one money event at the face
/// value, credited to the instrument's customer exactly once. The period follows <see cref="CollectedOn"/> (K1).
/// </summary>
public sealed record CustomerInstrumentCollectRequest(
    Guid InstrumentId,
    string ExpectedVersion,
    Guid TreasuryAccountId,
    DateOnly CollectedOn,
    DateOnly? ValueOn);

public sealed record CustomerInstrumentView(
    Guid Id,
    Guid CompanyId,
    string Version,
    CustomerInstrumentKind Kind,
    CustomerInstrumentStatus Status,
    Guid PartyId,
    Guid CompanyAccountId,
    string SerialNumber,
    string Drawer,
    string? BankName,
    string? BranchName,
    DateOnly DueOn,
    DateOnly ReceivedOn,
    Guid CurrencyId,
    AmountView Amount,
    string? Description,
    Guid? TreasuryAccountId,
    DateOnly StatusChangedOn,
    Guid? CollectionSourceId,
    string ReceivedBy,
    DateTimeOffset ReceivedAtUtc);

/// <summary>One append-only step of an instrument's history; the first one is the receipt (no previous status).</summary>
public sealed record CustomerInstrumentMovementView(
    string Version,
    CustomerInstrumentStatus? FromStatus,
    CustomerInstrumentStatus ToStatus,
    DateOnly OccurredOn,
    Guid? TreasuryAccountId,
    Guid? CollectionSourceId,
    string? Note,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc);

public sealed record CustomerInstrumentDetail(
    CustomerInstrumentView Instrument,
    IReadOnlyList<CustomerInstrumentMovementView> Movements,
    IReadOnlyList<CustomerInstrumentAction> AvailableActions);

public sealed record CustomerInstrumentPage(Guid CompanyId, IReadOnlyList<CustomerInstrumentView> Items, string? NextCursor);

public sealed record CustomerInstrumentCommandResult(
    CommandOutcome Outcome,
    CustomerInstrumentView? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

// DEC-FIN-016 risk tracking (user decision 2026-09-27): the customer's debt closes AT COLLECTION, and the instrument's
// payment / risk status is tracked SEPARATELY as facts: per customer (and a total), the count and face amount by risk
// state, and the open exposure = everything received and neither collected nor returned (in portfolio, at the bank,
// dishonoured). Read-only, no money effect, no limit or warning threshold (OPEN-010 stays open).

/// <summary>A received instrument's risk state as of a date: the status, with the portfolio split by its due date.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CustomerInstrumentRiskState>))]
public enum CustomerInstrumentRiskState
{
    /// <summary>In the portfolio and due on or after the as-of date.</summary>
    InPortfolioNotDue = 1,

    /// <summary>In the portfolio and its due date is before the as-of date.</summary>
    InPortfolioOverdue = 2,
    AtBankForCollection = 3,

    /// <summary>Karşılıksız (cheque) or protestolu (note).</summary>
    Dishonoured = 4,
    ReturnedToCustomer = 5,
    Collected = 6,
}

/// <summary>The risk view's scope: a date for the due split, and optionally one customer account (both ids or neither).</summary>
public sealed record CustomerInstrumentRiskRequest(DateOnly AsOf, Guid? PartyId, Guid? CompanyAccountId);

/// <summary>A count and the exact sum of face values, in the line's currency; the amount follows the amount grant.</summary>
public sealed record CustomerInstrumentRiskBucket(int Count, AmountView Amount);

/// <summary>Every risk state, plus the open exposure (not due + overdue + at bank + dishonoured).</summary>
public sealed record CustomerInstrumentRiskFigures(
    CustomerInstrumentRiskBucket InPortfolioNotDue,
    CustomerInstrumentRiskBucket InPortfolioOverdue,
    CustomerInstrumentRiskBucket AtBankForCollection,
    CustomerInstrumentRiskBucket Dishonoured,
    CustomerInstrumentRiskBucket ReturnedToCustomer,
    CustomerInstrumentRiskBucket Collected,
    CustomerInstrumentRiskBucket OpenExposure);

/// <summary>One customer account in one currency (amounts of different currencies are never added).</summary>
public sealed record CustomerInstrumentRiskLine(Guid PartyId, Guid CompanyAccountId, Guid CurrencyId,
    CustomerInstrumentRiskFigures Figures);

/// <summary>The total of the returned lines in one currency: the company total, or the one customer's.</summary>
public sealed record CustomerInstrumentRiskTotal(Guid CurrencyId, CustomerInstrumentRiskFigures Figures);

public sealed record CustomerInstrumentRiskView(
    Guid CompanyId,
    DateOnly AsOf,
    Guid? PartyId,
    Guid? CompanyAccountId,
    IReadOnlyList<CustomerInstrumentRiskLine> Customers,
    IReadOnlyList<CustomerInstrumentRiskTotal> Totals);

/// <summary>
/// The customer cheque / note portfolio (DEC-FIN-016 first release). Every read and command measures its own Finance
/// permission afresh, replay included; a command answers the #258-style outcomes (ADR-011 idempotency, stale version).
/// </summary>
public interface ICustomerInstrumentService
{
    Task<CustomerInstrumentCommandResult> ReceiveAsync(FinanceContext context, CommandMetadata metadata,
        CustomerInstrumentReceiveRequest request, CancellationToken cancellationToken);

    Task<CustomerInstrumentCommandResult> ChangeStatusAsync(FinanceContext context, CommandMetadata metadata,
        CustomerInstrumentStatusChangeRequest request, CancellationToken cancellationToken);

    Task<CustomerInstrumentCommandResult> CollectAsync(FinanceContext context, CommandMetadata metadata,
        CustomerInstrumentCollectRequest request, CancellationToken cancellationToken);

    Task<ReadResult<CustomerInstrumentPage>> ListAuthorizedAsync(FinanceContext context, string? cursor,
        CancellationToken cancellationToken);

    Task<ReadResult<CustomerInstrumentDetail>> DetailAuthorizedAsync(FinanceContext context, Guid instrumentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The risk view (read-only, <c>ReadCustomerInstruments</c>; amounts only with <c>ReadCustomerInstrumentAmount</c>).
    /// A request naming only one of the party / company account ids is NotFound (the endpoint's 400).
    /// </summary>
    Task<ReadResult<CustomerInstrumentRiskView>> RiskAuthorizedAsync(FinanceContext context,
        CustomerInstrumentRiskRequest request, CancellationToken cancellationToken);
}

/// <summary>The risk state of one received instrument as of a date (pure).</summary>
public static class CustomerInstrumentRisk
{
    public static CustomerInstrumentRiskState StateOf(CustomerInstrumentStatus status, DateOnly dueOn, DateOnly asOf) =>
        status switch
        {
            CustomerInstrumentStatus.InPortfolio => dueOn < asOf
                ? CustomerInstrumentRiskState.InPortfolioOverdue
                : CustomerInstrumentRiskState.InPortfolioNotDue,
            CustomerInstrumentStatus.AtBankForCollection => CustomerInstrumentRiskState.AtBankForCollection,
            CustomerInstrumentStatus.Bounced or CustomerInstrumentStatus.Protested => CustomerInstrumentRiskState.Dishonoured,
            CustomerInstrumentStatus.ReturnedToCustomer => CustomerInstrumentRiskState.ReturnedToCustomer,
            CustomerInstrumentStatus.Collected => CustomerInstrumentRiskState.Collected,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown customer instrument status."),
        };

    /// <summary>Received and neither collected nor returned: the customer's debt it stands for is still open.</summary>
    public static bool IsOpenExposure(CustomerInstrumentRiskState state) =>
        state is CustomerInstrumentRiskState.InPortfolioNotDue or CustomerInstrumentRiskState.InPortfolioOverdue
            or CustomerInstrumentRiskState.AtBankForCollection or CustomerInstrumentRiskState.Dishonoured;
}

/// <summary>DEC-FIN-016's first-release state machine and the receive checks. Pure; no owner, clock or database.</summary>
public static class CustomerInstrumentRules
{
    public const int SerialNumberMaximumLength = 64;
    public const int PartyTextMaximumLength = 200;
    public const int DescriptionMaximumLength = 1000;
    public const int NoteMaximumLength = 500;

    public const string KindInvalid = "instrument_kind_invalid";
    public const string SerialNumberRequired = "instrument_serial_number_required";
    public const string DrawerRequired = "instrument_drawer_required";
    public const string BankNameRequired = "instrument_bank_name_required";
    public const string TransitionInvalid = "instrument_transition_invalid";
    public const string TransitionNotAllowed = "instrument_transition_not_allowed";
    public const string DateBeforePrevious = "instrument_date_before_previous_movement";
    public const string TreasuryNotApplicable = "instrument_treasury_not_applicable";
    public const string TreasuryRequired = "instrument_treasury_required";
    public const string BankMismatch = "instrument_bank_mismatch";
    public const string Duplicate = "instrument_duplicate";
    public const string NotFound = "instrument_not_found";
    public const string ValueDoesNotFit = "finance_value_does_not_fit";

    /// <summary>
    /// The status a change leads to, or null when DEC-FIN-016's first release does not allow it from here. Collected
    /// and ReturnedToCustomer are final; a cheque is dishonoured as Bounced, a note as Protested.
    /// </summary>
    public static CustomerInstrumentStatus? Target(CustomerInstrumentKind kind, CustomerInstrumentStatus from,
        CustomerInstrumentTransition transition) => (transition, from) switch
    {
        (CustomerInstrumentTransition.DepositForCollection, CustomerInstrumentStatus.InPortfolio) =>
            CustomerInstrumentStatus.AtBankForCollection,
        (CustomerInstrumentTransition.WithdrawFromBank, CustomerInstrumentStatus.AtBankForCollection) =>
            CustomerInstrumentStatus.InPortfolio,
        (CustomerInstrumentTransition.Dishonour, CustomerInstrumentStatus.InPortfolio or CustomerInstrumentStatus.AtBankForCollection) =>
            kind == CustomerInstrumentKind.Cheque ? CustomerInstrumentStatus.Bounced : CustomerInstrumentStatus.Protested,
        (CustomerInstrumentTransition.ReturnToCustomer, CustomerInstrumentStatus.InPortfolio) =>
            CustomerInstrumentStatus.ReturnedToCustomer,
        // Only the kind's own dishonour status: a cheque is never Protested, a note never Bounced.
        (CustomerInstrumentTransition.ReturnToCustomer, CustomerInstrumentStatus.Bounced)
            when kind == CustomerInstrumentKind.Cheque => CustomerInstrumentStatus.ReturnedToCustomer,
        (CustomerInstrumentTransition.ReturnToCustomer, CustomerInstrumentStatus.Protested)
            when kind == CustomerInstrumentKind.PromissoryNote => CustomerInstrumentStatus.ReturnedToCustomer,
        _ => null,
    };

    /// <summary>Collection is allowed from the portfolio or from the bank it was deposited at.</summary>
    public static bool CanCollect(CustomerInstrumentStatus from) =>
        from is CustomerInstrumentStatus.InPortfolio or CustomerInstrumentStatus.AtBankForCollection;

    /// <summary>Every stored status change (the database write guard's rule): a transition target or a collection.</summary>
    public static bool IsAllowedStatusChange(CustomerInstrumentKind kind, CustomerInstrumentStatus from,
        CustomerInstrumentStatus to) =>
        (to == CustomerInstrumentStatus.Collected && CanCollect(from))
        || Enum.GetValues<CustomerInstrumentTransition>().Any(transition => Target(kind, from, transition) == to);

    /// <summary>The actions the state allows, in a stable order; the caller intersects them with fresh grants.</summary>
    public static IReadOnlyList<CustomerInstrumentAction> StateActions(CustomerInstrumentKind kind,
        CustomerInstrumentStatus status)
    {
        var actions = new List<CustomerInstrumentAction>();
        foreach (var transition in Enum.GetValues<CustomerInstrumentTransition>())
        {
            if (Target(kind, status, transition) is not null)
            {
                actions.Add((CustomerInstrumentAction)(int)transition);
            }
        }

        if (CanCollect(status))
        {
            actions.Add(CustomerInstrumentAction.Collect);
        }

        return actions;
    }

    /// <summary>The receive request's own shape: kind, required texts and the storage limits (never cut).</summary>
    public static IReadOnlyList<ValidationIssue> ValidateShape(CustomerInstrumentReceiveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var issues = new List<ValidationIssue>();
        void Reject(string field, string code) => issues.Add(new ValidationIssue(field, code));
        if (!Enum.IsDefined(request.Kind))
        {
            Reject("kind", KindInvalid);
        }

        if (request.PartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty || request.Money is null
            || request.Money.CurrencyId == Guid.Empty)
        {
            Reject("identity", "identity_required");
        }

        if (string.IsNullOrWhiteSpace(request.SerialNumber))
        {
            Reject("serialNumber", SerialNumberRequired);
        }
        else if (!IsCanonical(request.SerialNumber) || request.SerialNumber.Length > SerialNumberMaximumLength)
        {
            Reject("serialNumber", ValueDoesNotFit);
        }

        if (string.IsNullOrWhiteSpace(request.Drawer))
        {
            Reject("drawer", DrawerRequired);
        }
        else if (!IsCanonical(request.Drawer) || request.Drawer.Length > PartyTextMaximumLength)
        {
            Reject("drawer", ValueDoesNotFit);
        }

        if (request.Kind == CustomerInstrumentKind.Cheque && string.IsNullOrWhiteSpace(request.BankName))
        {
            Reject("bankName", BankNameRequired);
        }

        CheckOptional(request.BankName, "bankName", PartyTextMaximumLength);
        CheckOptional(request.BranchName, "branchName", PartyTextMaximumLength);
        CheckOptional(request.Description, "description", DescriptionMaximumLength);
        return issues;

        void CheckOptional(string? value, string field, int maximum)
        {
            if (value is not null && (!IsCanonical(value) || value.Length > maximum))
            {
                Reject(field, ValueDoesNotFit);
            }
        }
    }

    /// <summary>
    /// The face value against the Settings money profile the server resolved for the currency and the receipt date:
    /// the same company, currency and operation, active and in effect, and the amount exact within its scale (Ö2,
    /// never rounded). The profile is the manual bank receipt's, because collection records exactly such a receipt.
    /// </summary>
    public static IReadOnlyList<ValidationIssue> ValidateMoney(CustomerInstrumentReceiveRequest request, Guid companyId,
        MoneyProfileV1? profile, string operation)
    {
        ArgumentNullException.ThrowIfNull(request);
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

        if (profile.CompanyId != companyId || profile.CurrencyId != request.Money.CurrencyId
            || profile.Operation != operation || !profile.IsActive || request.ReceivedOn < profile.EffectiveFrom
            || (profile.EffectiveThrough is { } through && request.ReceivedOn > through))
        {
            issues.Add(new ValidationIssue("profile", "profile_mismatch"));
        }

        try
        {
            _ = ExactPositiveDecimalV1.Parse(request.Money.Amount, profile.Scale);
        }
        catch (SettingsPrerequisiteException exception)
        {
            issues.Add(new ValidationIssue("money.amount", exception.Code));
        }

        return issues;
    }

    /// <summary>The profile reference a validated profile stands for, as the receipt source stores it.</summary>
    public static ProfileReference Reference(MoneyProfileV1 profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new ProfileReference(profile.Profile.ProfileId.ToString("D"),
            profile.Profile.Version.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>A text is stored exactly as given, so it must carry no surrounding whitespace (refused, never trimmed).</summary>
    private static bool IsCanonical(string value) =>
        value.Length > 0 && string.Equals(value, value.Trim(), StringComparison.Ordinal);
}
