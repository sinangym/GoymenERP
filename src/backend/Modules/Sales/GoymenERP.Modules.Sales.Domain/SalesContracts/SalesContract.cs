using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Domain.SalesContracts;

/// <summary>
/// The approval axis of a sales contract (S-11; SAT-D28, NS-13, SAT-D346). A contract without a discount that needs approval is
/// born <see cref="Active"/>; one whose discount exceeds the configured limit, or any discount while no limit is configured, is
/// born <see cref="PendingDiscountApproval"/> and moves once: to <see cref="Active"/> (approved) or <see cref="DiscountRejected"/>.
/// Both are terminal for this record; nothing is deleted. Whether an active contract governs a day is a question of its validity
/// window (<see cref="SalesContract.IsInEffectOn"/>), never a stored state change.
/// </summary>
public enum SalesContractState
{
    PendingDiscountApproval = 1,
    Active = 2,
    DiscountRejected = 3,
}

/// <summary>NS-13: a discount rule applies to a line or to the contract total.</summary>
public enum SalesContractDiscountKind
{
    Line = 1,
    Total = 2,
}

/// <summary>One installment of the payment plan (NS-13): its 1-based order, due date and exact amount in the contract currency.</summary>
public sealed record SalesContractInstallment(int InstallmentNo, DateOnly DueDate, Money Amount);

/// <summary>One discount rule (NS-13): a line or total discount with a label and a percentage rate (0 &lt; rate ≤ 100).</summary>
public sealed record SalesContractDiscountRule(int RuleNo, SalesContractDiscountKind Kind, string Label, decimal RatePercent);

/// <summary>An installment as the command gives it: due date and exact amount (validated against the money profile).</summary>
public sealed record SalesContractInstallmentInput(DateOnly DueDate, decimal Amount);

/// <summary>A discount rule as the command gives it.</summary>
public sealed record SalesContractDiscountRuleInput(SalesContractDiscountKind Kind, string? Label, decimal RatePercent);

/// <summary>Who decided the discount approval and when; the reason is a rejection's (required there, absent on an approval).</summary>
public sealed record SalesContractDecision(string DecidedBy, DateTimeOffset DecidedAtUtc, string? RejectionReason);

/// <summary>
/// A sales contract (S-11; SAT-D28 EK3, NS-13): a separate record of the Sales module, owned here and never written by another
/// module.
/// <list type="bullet">
/// <item><b>Fields (SAT-D28):</b> number, version, state, validity window, payment plan, discount rules and clauses. The PDF vault
/// link and the agreement price tier (SAT-D58) are out of this record's scope.</item>
/// <item><b>Payment plan (NS-13):</b> installment lines with a due date and an exact amount in the contract currency; amounts fit
/// the Settings money profile's scale and are never rounded.</item>
/// <item><b>Discount rules (NS-13):</b> line and total discounts as percentage rates.</item>
/// <item><b>Approval (NS-13, SAT-D346):</b> a discount above the configured limit needs an authorized approval; the limit has no
/// default — while it is not configured every discount needs approval. The limit in force at creation is snapshotted on the
/// record (<see cref="DiscountLimitPercent"/>; null = not configured), so a later settings change never re-decides it.</item>
/// <item><b>Maker≠checker:</b> the creator never approves or rejects their own contract's discount (fail-closed implementation
/// detail, reversible).</item>
/// </list>
/// </summary>
public sealed class SalesContract
{
    public const int MaximumClausesLength = SalesOrderDraft.MaximumNoteLength;
    public const int MaximumReasonLength = SalesOrderDraft.MaximumNoteLength;
    public const int MaximumActorLength = SalesOrderDraft.MaximumActorLength;

    /// <summary>A technical bound of one record's payment plan (payload size), not a business policy.</summary>
    public const int MaximumInstallments = 120;

    /// <summary>A technical bound of one record's discount rules (payload size), not a business policy.</summary>
    public const int MaximumDiscountRules = 50;

    public const int MaximumLabelLength = 200;

    /// <summary>The number of decimals a percentage rate carries (rate and limit); a representation bound, not a tolerance.</summary>
    public const int RateScale = 4;

    /// <summary>A percentage is at most a hundred percent; the arithmetic bound of a rate.</summary>
    public const decimal MaximumRatePercent = 100m;

    private bool restored;

    private SalesContract(Guid id, Guid companyId, int contractNo, Guid partyId, Guid companyAccountId, Guid currencyId,
        Guid moneyProfileId, string moneyProfileVersion, int moneyScale, DateOnly validFrom, DateOnly validThrough,
        IReadOnlyList<SalesContractInstallment> installments, IReadOnlyList<SalesContractDiscountRule> discountRules,
        string? clauses, decimal? discountLimitPercent, string createdBy, DateTimeOffset now)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        ContractNo = contractNo >= 1 ? contractNo : throw new ArgumentOutOfRangeException(nameof(contractNo));
        PartyId = Identity.Require(partyId, nameof(partyId));
        CompanyAccountId = Identity.Require(companyAccountId, nameof(companyAccountId));
        CurrencyId = Identity.Require(currencyId, nameof(currencyId));
        MoneyProfileId = Identity.Require(moneyProfileId, nameof(moneyProfileId));
        MoneyProfileVersion = string.IsNullOrWhiteSpace(moneyProfileVersion)
            ? throw new ArgumentException("Para profili sürümü boş olamaz.", nameof(moneyProfileVersion))
            : moneyProfileVersion;
        MoneyScale = moneyScale is >= 0 and <= Money.MaximumScale
            ? moneyScale
            : throw new ArgumentOutOfRangeException(nameof(moneyScale));
        ValidFrom = validFrom;
        ValidThrough = validThrough;
        Installments = installments;
        DiscountRules = discountRules;
        Clauses = clauses;
        DiscountLimitPercent = discountLimitPercent;
        CreatedBy = RequireActor(createdBy);
        CreatedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
        State = RequiresDiscountApproval(discountRules, discountLimitPercent)
            ? SalesContractState.PendingDiscountApproval
            : SalesContractState.Active;
    }

    public Guid Id { get; }

    /// <summary>The server-resolved company; never taken from the request and never changed.</summary>
    public Guid CompanyId { get; }

    /// <summary>1-based number of the contract within its company (SAT-D28 "numara").</summary>
    public int ContractNo { get; }

    /// <summary>The customer party (Counterparty owner reference; read, never written).</summary>
    public Guid PartyId { get; }

    /// <summary>The customer's company account (Counterparty owner reference; read, never written).</summary>
    public Guid CompanyAccountId { get; }

    /// <summary>The contract currency; every installment amount is in it.</summary>
    public Guid CurrencyId { get; }

    public Guid MoneyProfileId { get; }

    public string MoneyProfileVersion { get; }

    /// <summary>The money profile's scale the installment amounts were validated against.</summary>
    public int MoneyScale { get; }

    /// <summary>The first day the contract governs (inclusive).</summary>
    public DateOnly ValidFrom { get; }

    /// <summary>The last day the contract governs (inclusive).</summary>
    public DateOnly ValidThrough { get; }

    /// <summary>NS-13: the payment plan, ordered by <see cref="SalesContractInstallment.InstallmentNo"/>.</summary>
    public IReadOnlyList<SalesContractInstallment> Installments { get; }

    /// <summary>NS-13: the discount rules (possibly none), ordered by <see cref="SalesContractDiscountRule.RuleNo"/>.</summary>
    public IReadOnlyList<SalesContractDiscountRule> DiscountRules { get; }

    /// <summary>SAT-D28: the free-text clauses; optional and bounded (never cut).</summary>
    public string? Clauses { get; }

    /// <summary>SAT-D346: the discount limit in force when the contract was recorded; null when no limit was configured.</summary>
    public decimal? DiscountLimitPercent { get; }

    /// <summary>NS-13/SAT-D346: whether the contract's discount needed an authorized approval when it was recorded.</summary>
    public bool DiscountApprovalRequired => RequiresDiscountApproval(DiscountRules, DiscountLimitPercent);

    public SalesContractState State { get; private set; }

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>The discount decision; null unless the contract needed approval and it was decided.</summary>
    public SalesContractDecision? Decision { get; private set; }

    /// <summary>Optimistic concurrency: 1 at creation, +1 on the decision; a refused command moves nothing.</summary>
    public long Version { get; private set; } = 1;

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>The sum of the payment plan (exact; same currency and scale).</summary>
    public Money PlannedTotal
    {
        get
        {
            var total = Money.Zero(CurrencyId, MoneyScale);
            foreach (var installment in Installments)
            {
                total = total.Add(installment.Amount);
            }

            return total;
        }
    }

    /// <summary>
    /// Records a contract: the validity window is closed and ordered, the payment plan has 1..<see cref="MaximumInstallments"/>
    /// positive amounts in due-date order fitting the money profile's scale, the discount rules are valid percentages with unique
    /// labels and at most one total rule, and the clauses are bounded. The money profile must be the contract currency's, active
    /// and in effect on its read date. <paramref name="discountLimitPercent"/> is the configured limit (null = not configured).
    /// </summary>
    public static SalesContract Create(Guid id, Guid companyId, int contractNo, Guid partyId, Guid companyAccountId,
        MoneyProfileSnapshot money, DateOnly validFrom, DateOnly validThrough,
        IReadOnlyList<SalesContractInstallmentInput> installments, IReadOnlyList<SalesContractDiscountRuleInput>? discountRules,
        string? clauses, decimal? discountLimitPercent, string createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(money);
        ArgumentNullException.ThrowIfNull(installments);
        RequireMoney(money, companyId);
        if (validThrough < validFrom)
        {
            throw new SalesRuleException(SalesRuleCodes.ContractValidityInvalid, "Sözleşme bitiş tarihi başlangıç tarihinden önce olamaz.");
        }

        var plan = RequireInstallments(installments, money.CurrencyId, money.Scale);
        var rules = RequireDiscountRules(discountRules ?? []);
        var text = RequireClauses(clauses);
        if (discountLimitPercent is { } limit && !IsRate(limit, allowZero: true))
        {
            throw new ArgumentOutOfRangeException(nameof(discountLimitPercent), "İskonto sınırı 0 ile 100 arasında bir yüzde olmalıdır.");
        }

        return new SalesContract(id, companyId, contractNo, partyId, companyAccountId, money.CurrencyId, money.ProfileId,
            money.ProfileVersion, money.Scale, validFrom, validThrough, plan, rules, text, discountLimitPercent, createdBy, now);
    }

    /// <summary>
    /// SAT-D346: approval is required when the contract carries any discount and either no limit is configured or a rate exceeds
    /// it. A contract without discount rules never needs approval.
    /// </summary>
    public static bool RequiresDiscountApproval(IReadOnlyList<SalesContractDiscountRule> rules, decimal? limitPercent)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Count == 0)
        {
            return false;
        }

        return limitPercent is not { } limit || rules.Any(rule => rule.RatePercent > limit);
    }

    /// <summary>NS-13: an authorized approver who is not the creator approves the pending discount; the contract becomes active.</summary>
    public void ApproveDiscount(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireDecider(actorId);
        Decide(SalesContractState.Active, new SalesContractDecision(actor, now.ToUniversalTime(), null), now);
    }

    /// <summary>NS-13: an authorized approver who is not the creator rejects the pending discount with a reason.</summary>
    public void RejectDiscount(long expectedVersion, string actorId, string? reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireDecider(actorId);
        var text = RequireReason(reason);
        Decide(SalesContractState.DiscountRejected, new SalesContractDecision(actor, now.ToUniversalTime(), text), now);
    }

    /// <summary>Whether the contract is active and its validity window contains <paramref name="businessDate"/>.</summary>
    public bool IsInEffectOn(DateOnly businessDate) =>
        State == SalesContractState.Active && businessDate >= ValidFrom && businessDate <= ValidThrough;

    /// <summary>
    /// Rebuilds a stored contract (persistence only): the facts are validated as on creation (without the money profile's
    /// in-effect check, which held at creation), then the stored state and decision are restored. The state must agree with the
    /// snapshotted limit (a contract that needed no approval is Active without a decision; one that needed approval is Pending
    /// without a decision or decided with one) and the version is at least the transitions it implies. A corrupt row is refused,
    /// never repaired.
    /// </summary>
    public static SalesContract Restore(Guid id, Guid companyId, int contractNo, Guid partyId, Guid companyAccountId,
        Guid currencyId, Guid moneyProfileId, string moneyProfileVersion, int moneyScale, DateOnly validFrom, DateOnly validThrough,
        IReadOnlyList<SalesContractInstallment> installments, IReadOnlyList<SalesContractDiscountRule> discountRules,
        string? clauses, decimal? discountLimitPercent, SalesContractState state, string createdBy, DateTimeOffset createdAtUtc,
        SalesContractDecision? decision, long storedVersion, DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(installments);
        ArgumentNullException.ThrowIfNull(discountRules);
        try
        {
            var plan = RequireInstallments(
                installments.Select(item => new SalesContractInstallmentInput(item.DueDate, item.Amount.Amount)).ToArray(),
                currencyId, moneyScale);
            var rules = RequireDiscountRules(
                discountRules.Select(rule => new SalesContractDiscountRuleInput(rule.Kind, rule.Label, rule.RatePercent)).ToArray());
            var numbered = installments.Select((item, index) => item.InstallmentNo == index + 1).All(match => match)
                && discountRules.Select((rule, index) => rule.RuleNo == index + 1).All(match => match);
            if (!numbered || validThrough < validFrom
                || discountLimitPercent is { } limit && !IsRate(limit, allowZero: true))
            {
                throw new InvalidOperationException("Kayıtlı sözleşme olguları tutarsız.");
            }

            var contract = new SalesContract(id, companyId, contractNo, partyId, companyAccountId, currencyId, moneyProfileId,
                moneyProfileVersion, moneyScale, validFrom, validThrough, plan, rules, RequireClauses(clauses), discountLimitPercent,
                createdBy, createdAtUtc);
            contract.RestoreState(state, decision, storedVersion, updatedAtUtc);
            return contract;
        }
        catch (SalesRuleException exception)
        {
            throw new InvalidOperationException("Kayıtlı sözleşme olguları kuralları karşılamıyor.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("Kayıtlı sözleşme olguları kuralları karşılamıyor.", exception);
        }
    }

    private void RestoreState(SalesContractState state, SalesContractDecision? decision, long storedVersion, DateTimeOffset updatedAtUtc)
    {
        if (restored || Version != 1)
        {
            throw new InvalidOperationException("Sözleşmenin kayıtlı durumu yalnız bir kez ve yeni kurulmuş kayda geri yüklenir.");
        }

        var consistent = (DiscountApprovalRequired, state) switch
        {
            (false, SalesContractState.Active) => decision is null,
            (true, SalesContractState.PendingDiscountApproval) => decision is null,
            (true, SalesContractState.Active) => decision is { RejectionReason: null },
            (true, SalesContractState.DiscountRejected) =>
                decision is { RejectionReason: { Length: > 0 and <= MaximumReasonLength } reason } && !string.IsNullOrWhiteSpace(reason),
            _ => false,
        };
        if (!consistent || decision is { DecidedBy: var by }
            && (string.IsNullOrWhiteSpace(by) || by.Length > MaximumActorLength || string.Equals(by, CreatedBy, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Kayıtlı sözleşme iskonto kararı kayıtlı durumuyla uyuşmuyor.");
        }

        var minimumVersion = decision is null ? 1L : 2L;
        if (storedVersion < minimumVersion)
        {
            throw new InvalidOperationException("Kayıtlı sözleşme sürümü, kararların hesabıyla uyuşmuyor.");
        }

        State = state;
        Decision = decision;
        Version = storedVersion;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        restored = true;
    }

    private static void RequireMoney(MoneyProfileSnapshot money, Guid companyId)
    {
        if (money.CompanyId != companyId || money.CurrencyId == Guid.Empty || money.ProfileId == Guid.Empty
            || string.IsNullOrWhiteSpace(money.ProfileVersion) || money.Scale is < 0 or > Money.MaximumScale
            || (money.EffectiveThrough is { } through && through < money.EffectiveFrom))
        {
            throw new ArgumentException("Para profili sözleşmenin şirketini ve para birimini adlandırmalı ve tam olmalıdır.", nameof(money));
        }

        if (!string.Equals(money.Operation, SalesOrderDraft.MoneyOperation, StringComparison.Ordinal))
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotOperationMismatch, "Para profili satış operasyonu için alınmamış.");
        }

        if (!money.IsActive || money.EffectiveOn < money.EffectiveFrom || (money.EffectiveThrough is { } until && money.EffectiveOn > until))
        {
            throw new SalesRuleException(SalesRuleCodes.MoneyProfileNotInEffect, "Para profili iş tarihinde etkin değil.");
        }
    }

    private static SalesContractInstallment[] RequireInstallments(IReadOnlyList<SalesContractInstallmentInput> installments,
        Guid currencyId, int scale)
    {
        if (installments.Count is < 1 or > MaximumInstallments)
        {
            throw new SalesRuleException(SalesRuleCodes.ContractInstallmentsInvalid,
                $"Ödeme planı en az 1, en fazla {MaximumInstallments} taksit taşır.");
        }

        var plan = new SalesContractInstallment[installments.Count];
        DateOnly? previous = null;
        for (var index = 0; index < installments.Count; index++)
        {
            var input = installments[index] ?? throw new SalesRuleException(SalesRuleCodes.ContractInstallmentsInvalid, "Taksit boş olamaz.");
            if (input.Amount <= 0m || !Money.TryOf(input.Amount, currencyId, scale, out var amount))
            {
                throw new SalesRuleException(SalesRuleCodes.ContractInstallmentsInvalid,
                    $"Taksit tutarı pozitif olmalı ve en fazla {scale} ondalık taşımalıdır; fazlası yuvarlanmaz, reddedilir.");
            }

            if (previous is { } last && input.DueDate < last)
            {
                throw new SalesRuleException(SalesRuleCodes.ContractInstallmentsInvalid, "Taksitler vade tarihi sırasıyla verilir.");
            }

            previous = input.DueDate;
            plan[index] = new SalesContractInstallment(index + 1, input.DueDate, amount);
        }

        return plan;
    }

    private static SalesContractDiscountRule[] RequireDiscountRules(IReadOnlyList<SalesContractDiscountRuleInput> rules)
    {
        if (rules.Count > MaximumDiscountRules)
        {
            throw new SalesRuleException(SalesRuleCodes.ContractDiscountInvalid, $"Sözleşme en fazla {MaximumDiscountRules} iskonto kuralı taşır.");
        }

        var result = new SalesContractDiscountRule[rules.Count];
        var labels = new HashSet<(SalesContractDiscountKind, string)>();
        var totals = 0;
        for (var index = 0; index < rules.Count; index++)
        {
            var input = rules[index] ?? throw new SalesRuleException(SalesRuleCodes.ContractDiscountInvalid, "İskonto kuralı boş olamaz.");
            if (!Enum.IsDefined(input.Kind))
            {
                throw new SalesRuleException(SalesRuleCodes.ContractDiscountInvalid, "İskonto türü satır ya da toplam olmalıdır.");
            }

            if (string.IsNullOrWhiteSpace(input.Label) || input.Label.Trim().Length > MaximumLabelLength)
            {
                throw new SalesRuleException(SalesRuleCodes.ContractDiscountInvalid,
                    $"İskonto kuralı adı boş olamaz ve en fazla {MaximumLabelLength} karakterdir.");
            }

            var label = input.Label.Trim();
            if (!labels.Add((input.Kind, label)))
            {
                throw new SalesRuleException(SalesRuleCodes.ContractDiscountInvalid, "Aynı türde aynı adla ikinci iskonto kuralı verilemez.");
            }

            if (!IsRate(input.RatePercent, allowZero: false))
            {
                throw new SalesRuleException(SalesRuleCodes.ContractDiscountInvalid,
                    $"İskonto oranı 0'dan büyük, en fazla %{MaximumRatePercent} ve en fazla {RateScale} ondalık olmalıdır.");
            }

            if (input.Kind == SalesContractDiscountKind.Total && ++totals > 1)
            {
                throw new SalesRuleException(SalesRuleCodes.ContractDiscountInvalid, "Sözleşme en fazla bir toplam iskontosu taşır.");
            }

            result[index] = new SalesContractDiscountRule(index + 1, input.Kind, label, input.RatePercent);
        }

        return result;
    }

    private static bool IsRate(decimal value, bool allowZero) =>
        (allowZero ? value >= 0m : value > 0m) && value <= MaximumRatePercent && Money.SignificantDecimals(value) <= RateScale;

    private static string? RequireClauses(string? clauses)
    {
        if (string.IsNullOrWhiteSpace(clauses))
        {
            return null;
        }

        return clauses.Length <= MaximumClausesLength
            ? clauses
            : throw new SalesRuleException(SalesRuleCodes.ContractClausesTooLong,
                $"Sözleşme maddeleri en fazla {MaximumClausesLength} karakterdir; fazlası kesilmez, reddedilir.");
    }

    private string RequireDecider(string actorId)
    {
        var actor = RequireActor(actorId);
        if (State != SalesContractState.PendingDiscountApproval)
        {
            throw new SalesRuleException(SalesRuleCodes.StateConflict, $"Yalnız iskonto onayı bekleyen sözleşme karara bağlanır (durum: {State}).");
        }

        return !string.Equals(actor, CreatedBy, StringComparison.Ordinal)
            ? actor
            : throw new SalesRuleException(SalesRuleCodes.ContractSelfDecision,
                "Sözleşmeyi kaydeden kendi sözleşmesinin iskontosunu onaylayamaz ya da reddedemez.");
    }

    private void Decide(SalesContractState state, SalesContractDecision decision, DateTimeOffset now)
    {
        State = state;
        Decision = decision;
        UpdatedAtUtc = now.ToUniversalTime();
        Version = checked(Version + 1);
    }

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new SalesVersionConflictException(expectedVersion, Version);
        }
    }

    private static string RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new SalesRuleException(SalesRuleCodes.ContractReasonRequired, "İskonto reddi gerekçe ister.");
        }

        var text = reason.Trim();
        return text.Length <= MaximumReasonLength
            ? text
            : throw new SalesRuleException(SalesRuleCodes.ContractReasonTooLong,
                $"Gerekçe en fazla {MaximumReasonLength} karakterdir; fazlası kesilmez, reddedilir.");
    }

    private static string RequireActor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Değer boş olamaz.", nameof(value));
        }

        var text = value.Trim();
        return text.Length <= MaximumActorLength
            ? text
            : throw new ArgumentException($"Değer en fazla {MaximumActorLength} karakterdir.", nameof(value));
    }
}
