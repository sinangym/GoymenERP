using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// S-11 sales contract (SAT-D28 EK3, NS-13, SAT-D346) — the domain half: the closed validity window, the installment payment plan
/// (positive, due-date ordered, fitting the money profile's scale, never rounded), the line / total discount rules (valid
/// percentages, unique labels per kind, at most one total rule), the bounded clauses, the approval decision against the limit
/// snapshotted at creation (no rule → active; no configured limit → every discount pending; above the limit → pending; at the
/// limit → within it), the maker≠checker decision with expected version, the rejection reason and the strict restore. Every
/// identity is synthetic.
/// </summary>
public sealed class SalesContractTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 11, 15);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Party = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Account = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Profile = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private const string Seller = "synthetic-seller";
    private const string Manager = "synthetic-manager";

    private static MoneyProfileSnapshot MoneyProfile(int scale = 2, bool active = true, string operation = SalesOrderDraft.MoneyOperation,
        Guid? company = null) =>
        new(company ?? Company, Currency, operation, Profile, "3", Day, active, new DateOnly(2026, 9, 1), null, scale);

    private static readonly SalesContractInstallmentInput[] TwoInstallments =
    [
        new(new DateOnly(2026, 12, 1), 1500m),
        new(new DateOnly(2027, 1, 1), 1500.25m),
    ];

    private static SalesContract Create(
        IReadOnlyList<SalesContractDiscountRuleInput>? rules = null,
        decimal? limit = null,
        IReadOnlyList<SalesContractInstallmentInput>? installments = null,
        MoneyProfileSnapshot? money = null,
        DateOnly? validFrom = null,
        DateOnly? validThrough = null,
        string? clauses = "SENTETIK teslim maddesi") =>
        SalesContract.Create(Guid.NewGuid(), Company, 1, Party, Account, money ?? MoneyProfile(), validFrom ?? new DateOnly(2026, 11, 1),
            validThrough ?? new DateOnly(2027, 10, 31), installments ?? TwoInstallments, rules, clauses, limit, Seller, T0);

    private static SalesContractDiscountRuleInput Line(decimal rate, string label = "SENTETIK hacim") =>
        new(SalesContractDiscountKind.Line, label, rate);

    private static SalesContractDiscountRuleInput Total(decimal rate, string label = "SENTETIK peşin") =>
        new(SalesContractDiscountKind.Total, label, rate);

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    [Fact]
    public void A_contract_without_discount_rules_is_born_active_at_version_one_with_its_plan_total()
    {
        var contract = Create();

        Assert.Equal(SalesContractState.Active, contract.State);
        Assert.False(contract.DiscountApprovalRequired);
        Assert.Equal(1, contract.Version);
        Assert.Null(contract.Decision);
        Assert.Equal(3000.25m, contract.PlannedTotal.Amount);
        Assert.Equal([1, 2], contract.Installments.Select(item => item.InstallmentNo));
        Assert.Equal(2, contract.MoneyScale);
        Assert.True(contract.IsInEffectOn(Day));
        Assert.False(contract.IsInEffectOn(new DateOnly(2027, 11, 1)));
    }

    [Fact]
    public void Every_discount_goes_to_approval_while_no_limit_is_configured()
    {
        var contract = Create(rules: [Line(0.5m)], limit: null);

        Assert.Equal(SalesContractState.PendingDiscountApproval, contract.State);
        Assert.True(contract.DiscountApprovalRequired);
        Assert.Null(contract.DiscountLimitPercent);
        Assert.False(contract.IsInEffectOn(Day));
    }

    [Fact]
    public void A_discount_above_the_configured_limit_needs_approval_and_one_at_the_limit_does_not()
    {
        Assert.Equal(SalesContractState.PendingDiscountApproval, Create(rules: [Line(5m), Total(10.0001m)], limit: 10m).State);

        var within = Create(rules: [Line(5m), Total(10m)], limit: 10m);
        Assert.Equal(SalesContractState.Active, within.State);
        Assert.Equal(10m, within.DiscountLimitPercent);
    }

    [Fact]
    public void A_zero_limit_sends_every_discount_to_approval()
    {
        Assert.Equal(SalesContractState.PendingDiscountApproval, Create(rules: [Line(0.0001m)], limit: 0m).State);
    }

    [Fact]
    public void Requires_discount_approval_is_false_without_rules_whatever_the_limit()
    {
        Assert.False(SalesContract.RequiresDiscountApproval([], null));
        Assert.False(SalesContract.RequiresDiscountApproval([], 0m));
    }

    [Fact]
    public void Validity_must_be_closed_and_ordered()
    {
        Assert.Equal(SalesRuleCodes.ContractValidityInvalid,
            CodeOf(() => Create(validFrom: new DateOnly(2027, 1, 1), validThrough: new DateOnly(2026, 12, 31))));
        Assert.Equal(SalesContractState.Active, Create(validFrom: Day, validThrough: Day).State);
    }

    [Fact]
    public void The_payment_plan_refuses_no_line_too_many_lines_a_non_positive_amount_an_unordered_due_date_and_excess_decimals()
    {
        Assert.Equal(SalesRuleCodes.ContractInstallmentsInvalid, CodeOf(() => Create(installments: [])));
        var tooMany = Enumerable.Range(0, SalesContract.MaximumInstallments + 1)
            .Select(index => new SalesContractInstallmentInput(Day.AddDays(index), 1m)).ToArray();
        Assert.Equal(SalesRuleCodes.ContractInstallmentsInvalid, CodeOf(() => Create(installments: tooMany)));
        Assert.Equal(SalesRuleCodes.ContractInstallmentsInvalid,
            CodeOf(() => Create(installments: [new(Day, 0m)])));
        Assert.Equal(SalesRuleCodes.ContractInstallmentsInvalid,
            CodeOf(() => Create(installments: [new(Day, -1m)])));
        Assert.Equal(SalesRuleCodes.ContractInstallmentsInvalid,
            CodeOf(() => Create(installments: [new(Day.AddDays(1), 1m), new(Day, 1m)])));
        Assert.Equal(SalesRuleCodes.ContractInstallmentsInvalid,
            CodeOf(() => Create(installments: [new(Day, 1.005m)])));
        Assert.Equal(SalesContractState.Active, Create(installments: [new(Day, 1m), new(Day, 2m)]).State);
    }

    [Fact]
    public void The_payment_plan_follows_the_money_profile_scale_without_rounding()
    {
        Assert.Equal(SalesRuleCodes.ContractInstallmentsInvalid, CodeOf(() => Create(money: MoneyProfile(scale: 0), installments: [new(Day, 1.5m)])));
        Assert.Equal(0, Create(money: MoneyProfile(scale: 0), installments: [new(Day, 15m)]).MoneyScale);
    }

    [Fact]
    public void Discount_rules_refuse_a_zero_or_excess_rate_too_many_decimals_a_blank_or_long_label_a_duplicate_and_two_totals()
    {
        Assert.Equal(SalesRuleCodes.ContractDiscountInvalid, CodeOf(() => Create(rules: [Line(0m)])));
        Assert.Equal(SalesRuleCodes.ContractDiscountInvalid, CodeOf(() => Create(rules: [Line(100.0001m)])));
        Assert.Equal(SalesRuleCodes.ContractDiscountInvalid, CodeOf(() => Create(rules: [Line(1.00001m)])));
        Assert.Equal(SalesRuleCodes.ContractDiscountInvalid, CodeOf(() => Create(rules: [Line(1m, "  ")])));
        Assert.Equal(SalesRuleCodes.ContractDiscountInvalid,
            CodeOf(() => Create(rules: [Line(1m, new string('x', SalesContract.MaximumLabelLength + 1))])));
        Assert.Equal(SalesRuleCodes.ContractDiscountInvalid, CodeOf(() => Create(rules: [Line(1m, "A"), Line(2m, " A ")])));
        Assert.Equal(SalesRuleCodes.ContractDiscountInvalid, CodeOf(() => Create(rules: [Total(1m, "A"), Total(2m, "B")])));
        Assert.Equal(SalesRuleCodes.ContractDiscountInvalid,
            CodeOf(() => Create(rules: [new SalesContractDiscountRuleInput((SalesContractDiscountKind)99, "A", 1m)])));
        var tooMany = Enumerable.Range(0, SalesContract.MaximumDiscountRules + 1).Select(index => Line(1m, $"R{index}")).ToArray();
        Assert.Equal(SalesRuleCodes.ContractDiscountInvalid, CodeOf(() => Create(rules: tooMany)));

        var same = Create(rules: [Line(1m, "A"), Total(100m, "A")], limit: 100m);
        Assert.Equal(["A", "A"], same.DiscountRules.Select(rule => rule.Label));
        Assert.Equal([1, 2], same.DiscountRules.Select(rule => rule.RuleNo));
    }

    [Fact]
    public void Clauses_are_bounded_never_cut_and_blank_clauses_are_none()
    {
        Assert.Equal(SalesRuleCodes.ContractClausesTooLong,
            CodeOf(() => Create(clauses: new string('x', SalesContract.MaximumClausesLength + 1))));
        Assert.Null(Create(clauses: "   ").Clauses);
        Assert.Equal(SalesContract.MaximumClausesLength, Create(clauses: new string('x', SalesContract.MaximumClausesLength)).Clauses!.Length);
    }

    [Fact]
    public void The_money_profile_must_be_the_companys_for_the_sales_operation_and_in_effect()
    {
        Assert.Throws<ArgumentException>(() => Create(money: MoneyProfile(company: Guid.Parse("99999999-9999-4999-8999-999999999999"))));
        Assert.Equal(SalesRuleCodes.SnapshotOperationMismatch, CodeOf(() => Create(money: MoneyProfile(operation: "purchase"))));
        Assert.Equal(SalesRuleCodes.MoneyProfileNotInEffect, CodeOf(() => Create(money: MoneyProfile(active: false))));
    }

    [Fact]
    public void A_limit_outside_zero_to_one_hundred_is_a_caller_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(rules: [Line(1m)], limit: -1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(rules: [Line(1m)], limit: 100.5m));
    }

    [Fact]
    public void Another_person_approves_the_pending_discount_at_the_expected_version()
    {
        var contract = Create(rules: [Line(5m)]);

        contract.ApproveDiscount(1, $" {Manager} ", T0.AddHours(1));

        Assert.Equal(SalesContractState.Active, contract.State);
        Assert.Equal(2, contract.Version);
        Assert.Equal(Manager, contract.Decision!.DecidedBy);
        Assert.Null(contract.Decision.RejectionReason);
        Assert.Equal(T0.AddHours(1).ToUniversalTime(), contract.Decision.DecidedAtUtc);
        Assert.True(contract.IsInEffectOn(Day));
    }

    [Fact]
    public void Another_person_rejects_the_pending_discount_with_a_reason_and_the_contract_never_takes_effect()
    {
        var contract = Create(rules: [Line(5m)]);

        contract.RejectDiscount(1, Manager, "SENTETIK oran yüksek", T0.AddHours(1));

        Assert.Equal(SalesContractState.DiscountRejected, contract.State);
        Assert.Equal(2, contract.Version);
        Assert.Equal("SENTETIK oran yüksek", contract.Decision!.RejectionReason);
        Assert.False(contract.IsInEffectOn(Day));
    }

    [Fact]
    public void The_creator_never_decides_their_own_contracts_discount()
    {
        var contract = Create(rules: [Line(5m)]);

        Assert.Equal(SalesRuleCodes.ContractSelfDecision, CodeOf(() => contract.ApproveDiscount(1, Seller, T0)));
        Assert.Equal(SalesRuleCodes.ContractSelfDecision, CodeOf(() => contract.RejectDiscount(1, $" {Seller} ", "SENTETIK", T0)));
        Assert.Equal(SalesContractState.PendingDiscountApproval, contract.State);
        Assert.Equal(1, contract.Version);
    }

    [Fact]
    public void A_stale_version_conflicts_and_changes_nothing()
    {
        var contract = Create(rules: [Line(5m)]);

        var conflict = Assert.Throws<SalesVersionConflictException>(() => contract.ApproveDiscount(2, Manager, T0));

        Assert.Equal(1, conflict.CurrentVersion);
        Assert.Equal(SalesContractState.PendingDiscountApproval, contract.State);
    }

    [Fact]
    public void Only_a_pending_contract_is_decided_and_only_once()
    {
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => Create().ApproveDiscount(1, Manager, T0)));
        var decided = Create(rules: [Line(5m)]);
        decided.ApproveDiscount(1, Manager, T0);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => decided.RejectDiscount(2, Manager, "SENTETIK", T0)));
        Assert.Equal(2, decided.Version);
    }

    [Fact]
    public void A_rejection_needs_a_non_blank_reason_within_the_bound_and_never_cuts_it()
    {
        var contract = Create(rules: [Line(5m)]);

        Assert.Equal(SalesRuleCodes.ContractReasonRequired, CodeOf(() => contract.RejectDiscount(1, Manager, "  ", T0)));
        Assert.Equal(SalesRuleCodes.ContractReasonRequired, CodeOf(() => contract.RejectDiscount(1, Manager, null, T0)));
        Assert.Equal(SalesRuleCodes.ContractReasonTooLong,
            CodeOf(() => contract.RejectDiscount(1, Manager, new string('x', SalesContract.MaximumReasonLength + 1), T0)));
        Assert.Equal(SalesContractState.PendingDiscountApproval, contract.State);
    }

    [Fact]
    public void Restore_rebuilds_a_decided_contract_and_refuses_a_state_the_snapshotted_limit_does_not_allow()
    {
        var created = Create(rules: [Line(5m)], limit: 2m);
        created.ApproveDiscount(1, Manager, T0);

        var restored = Restore(created, created.State, created.Decision, created.Version);
        Assert.Equal(SalesContractState.Active, restored.State);
        Assert.Equal(created.PlannedTotal, restored.PlannedTotal);
        Assert.Equal(2m, restored.DiscountLimitPercent);

        // Active without a decision while the snapshotted limit required approval.
        Assert.Throws<InvalidOperationException>(() => Restore(created, SalesContractState.Active, null, 2));
        // Decided by the creator.
        Assert.Throws<InvalidOperationException>(() =>
            Restore(created, SalesContractState.Active, new SalesContractDecision(Seller, T0, null), 2));
        // Rejected without a reason.
        Assert.Throws<InvalidOperationException>(() =>
            Restore(created, SalesContractState.DiscountRejected, new SalesContractDecision(Manager, T0, null), 2));
        // Decided at version one.
        Assert.Throws<InvalidOperationException>(() => Restore(created, SalesContractState.Active, created.Decision, 1));
    }

    [Fact]
    public void Restore_refuses_an_unnumbered_plan_or_amounts_off_the_scale()
    {
        var created = Create();
        var shuffled = created.Installments.Select(item => item with { InstallmentNo = item.InstallmentNo + 1 }).ToArray();

        Assert.Throws<InvalidOperationException>(() => Restore(created, SalesContractState.Active, null, 1, installments: shuffled));
        Assert.Throws<InvalidOperationException>(() => Restore(created, SalesContractState.Active, null, 1, moneyScale: 0));
    }

    private static SalesContract Restore(SalesContract source, SalesContractState state, SalesContractDecision? decision,
        long version, IReadOnlyList<SalesContractInstallment>? installments = null, int? moneyScale = null) =>
        SalesContract.Restore(source.Id, source.CompanyId, source.ContractNo, source.PartyId, source.CompanyAccountId,
            source.CurrencyId, source.MoneyProfileId, source.MoneyProfileVersion, moneyScale ?? source.MoneyScale, source.ValidFrom,
            source.ValidThrough, installments ?? source.Installments, source.DiscountRules, source.Clauses,
            source.DiscountLimitPercent, state, source.CreatedBy, source.CreatedAtUtc, decision, version, source.UpdatedAtUtc);
}
