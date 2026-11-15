using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// MOD-01: Finance's answer to the Sales-declared risk-input port over SYNTHETIC open-item rows. Open receivables are the
/// outstanding amounts of the customer account's OPEN items, summed per currency exactly; closed items and other customers,
/// accounts or companies never enter. A row Finance cannot read makes the figure unavailable (never a partial sum). The credit
/// limit is the account's one approved API-85a record: an approved Set is available, none or an approved Remove is "NoSource",
/// never zero; a pending proposal never counts. Collateral has no master on main: "NoSource". The read writes nothing.
/// </summary>
public sealed class FinanceSalesPartyRiskReadPortV1Tests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Party = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Account = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly Guid Usd = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid Eur = Guid.Parse("aaaaaaaa-9999-4999-8999-999999999999");

    [Fact]
    public async Task Open_receivables_are_the_outstanding_amounts_of_the_accounts_open_items_per_currency_and_limit_and_collateral_have_no_source()
    {
        await using var run = await Run.CreateAsync(
            Row("125.50", "75.25"),
            Row("10", "10.00"),
            Row("300.00", "0.00", FinanceOpenItemStatuses.Closed),
            Row("40.00", "40.00", currency: Usd),
            Row("999.00", "999.00", party: Guid.NewGuid()),
            Row("999.00", "999.00", account: Guid.NewGuid()),
            Row("999.00", "999.00", company: OtherCompany));

        var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);

        Assert.Equal((Company, Party, Account), (answer.CompanyId, answer.PartyId, answer.CompanyAccountId));
        Assert.Equal((SalesPartyRiskFigureStateV1.Available, FinanceSalesPartyRiskReadPortV1.OpenItemsSource, (bool?)true, (string?)null),
            (answer.OpenReceivables.State, answer.OpenReceivables.Source, answer.OpenReceivables.IncludesTax, answer.OpenReceivables.ReasonCode));
        // 75.25 + 10.00 in TRY (exact, never rounded); USD apart, never converted; ordered by currency identity.
        Assert.Equal([new SalesPartyRiskAmountV1(Try, 85.25m), new SalesPartyRiskAmountV1(Usd, 40m)], answer.OpenReceivables.Amounts);
        Assert.Equal((SalesPartyRiskFigureStateV1.NoSource, FinanceSalesPartyRiskReadPortV1.CreditLimitSource, (bool?)null,
                FinanceSalesPartyRiskReadPortV1.CreditLimitNotSet),
            (answer.CreditLimit.State, answer.CreditLimit.Source, answer.CreditLimit.IncludesTax, answer.CreditLimit.ReasonCode));
        Assert.Empty(answer.CreditLimit.Amounts);
        Assert.Equal((SalesPartyRiskFigureStateV1.NoSource, FinanceSalesPartyRiskReadPortV1.CollateralSource,
                FinanceSalesPartyRiskReadPortV1.CollateralOwnerMissing),
            (answer.Collateral.State, answer.Collateral.Source, answer.Collateral.ReasonCode));
        Assert.Empty(answer.Collateral.Amounts);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Unallocated_customer_payments_are_deducted_per_currency_down_to_zero_and_reversed_ones_never()
    {
        var reversedReceipt = Guid.NewGuid();
        var pendingReversal = Guid.NewGuid();
        var allocatedCheque = Guid.NewGuid();
        await using var run = await Run.CreateAsync(
            [Row("125.50", "75.25"), Row("10", "10.00"), Row("40.00", "40.00", currency: Usd)],
            payments:
            [
                // A recorded bank receipt with no allocation yet: wholly unallocated.
                Payment(Guid.NewGuid(), "30.00"),
                // A COLLECTED cheque, 15.00 of it allocated to an invoice: 5.00 left.
                Payment(allocatedCheque, "20.00", MoneyEventKinds.CustomerInstrumentCollected),
                // A receipt with an APPROVED reversal: not a payment any more (its reversing event is not one either).
                Payment(reversedReceipt, "50.00"),
                Payment(Guid.NewGuid(), "50.00", MoneyEventKinds.ManualBankReceiptReversed),
                // A pending reversal proposal undoes nothing yet: still a payment.
                Payment(pendingReversal, "1.00"),
                // More USD paid than USD owed: the USD receivable stops at zero, never negative, never offset against TRY.
                Payment(Guid.NewGuid(), "100.00", currency: Usd),
                // A currency the customer owes nothing in: deducted from nothing.
                Payment(Guid.NewGuid(), "10.00", currency: Eur),
                // Another customer's and another company's payments never reach this one.
                Payment(Guid.NewGuid(), "500.00", party: Guid.NewGuid()),
                Payment(Guid.NewGuid(), "500.00", company: OtherCompany),
            ],
            states: [State(allocatedCheque, "20.00", "5.00"), State(reversedReceipt, "50.00", "0.00")],
            reversals: [Reversal(reversedReceipt, approved: true), Reversal(pendingReversal, approved: false)]);

        var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);

        // TRY: 85.25 owed − (30.00 + 5.00 + 1.00) unallocated = 49.25. USD: 40.00 − 100.00 → 0 (not below).
        Assert.Equal(SalesPartyRiskFigureStateV1.Available, answer.OpenReceivables.State);
        Assert.Equal([new SalesPartyRiskAmountV1(Try, 49.25m), new SalesPartyRiskAmountV1(Usd, 0m)], answer.OpenReceivables.Amounts);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task An_approved_reversal_alone_excludes_the_payment_even_without_a_zeroed_allocation_state()
    {
        // Review A P3-2: the reversal barrier also zeroes the state, but the exclusion must hold on its own.
        var reversed = Guid.NewGuid();
        await using var run = await Run.CreateAsync([Row("125.50", "75.25")], payments: [Payment(reversed, "50.00")],
            states: [], reversals: [Reversal(reversed, approved: true)]);

        var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);

        Assert.Equal([new SalesPartyRiskAmountV1(Try, 75.25m)], answer.OpenReceivables.Amounts);
    }

    [Fact]
    public async Task A_refund_to_the_customer_is_netted_off_its_unallocated_payments_per_currency_down_to_zero()
    {
        // Review A P2-1: an advance of 100.00, not yet allocated, then refunded in full — nothing is deducted any more.
        await using (var refunded = await Run.CreateAsync([Row("125.50", "75.25")], payments: [Payment(Guid.NewGuid(), "100.00")],
            outgoing: [Outgoing("100.00")]))
        {
            Assert.Equal([new SalesPartyRiskAmountV1(Try, 75.25m)],
                (await refunded.Port(Company).ReadPartyRiskAsync(Party, Account)).OpenReceivables.Amounts);
        }

        // A partial refund of 30.00 leaves 20.00 of a 50.00 advance to deduct; a refund larger than the credit stops at zero
        // (never adds to the receivable); a refund in another currency, to another customer or in another company changes nothing.
        await using (var partial = await Run.CreateAsync([Row("125.50", "75.25"), Row("40.00", "40.00", currency: Usd)],
            payments: [Payment(Guid.NewGuid(), "50.00"), Payment(Guid.NewGuid(), "10.00", currency: Usd)],
            outgoing:
            [
                Outgoing("30.00"), Outgoing("25.00", currency: Usd), Outgoing("500.00", currency: Eur),
                Outgoing("500.00", party: Guid.NewGuid()), Outgoing("500.00", company: OtherCompany),
            ]))
        {
            Assert.Equal([new SalesPartyRiskAmountV1(Try, 55.25m), new SalesPartyRiskAmountV1(Usd, 40m)],
                (await partial.Port(Company).ReadPartyRiskAsync(Party, Account)).OpenReceivables.Amounts);
        }

        // An outgoing row Finance cannot read: unavailable, never a partial netting.
        await using var broken = await Run.CreateAsync([Row("125.50", "75.25")], payments: [Payment(Guid.NewGuid(), "10.00")],
            outgoing: [Outgoing("-1")]);
        Assert.Equal(SalesPartyRiskFigureStateV1.Unavailable,
            (await broken.Port(Company).ReadPartyRiskAsync(Party, Account)).OpenReceivables.State);
    }

    [Fact]
    public async Task A_refund_whose_payment_reversal_was_approved_gave_nothing_back_and_is_not_netted()
    {
        // API-23: the refund of 100.00 was reversed (approved), so the 100.00 advance is deducted in full again; a refund
        // of another company's reversal row changes nothing here.
        var refund = Outgoing("100.00");
        var kept = Outgoing("30.00");
        await using var run = await Run.CreateAsync([Row("125.50", "125.50")], payments: [Payment(Guid.NewGuid(), "100.00")],
            outgoing: [refund, kept], paymentReversals: [PaymentReversal(refund)]);

        Assert.Equal([new SalesPartyRiskAmountV1(Try, 55.50m)],
            (await run.Port(Company).ReadPartyRiskAsync(Party, Account)).OpenReceivables.Amounts);
    }

    [Fact]
    public async Task An_unreadable_payment_or_allocation_state_makes_the_receivables_unavailable_never_a_wrong_deduction()
    {
        var source = Guid.NewGuid();
        foreach (var (payment, state) in new (MoneyEventRow, FinanceSourceAllocationStateRow?)[]
                 {
                     (Payment(source, "-1.00"), null),
                     (Payment(source, "abc"), null),
                     (Payment(source, "20.00"), State(source, "20.00", "25.00")),
                     (Payment(source, "20.00"), State(source, "19.00", "5.00")),
                     (Payment(source, "20.00"), State(source, "20.00", "5.00", currency: Usd)),
                     (Payment(source, "20.00"), State(source, "20.00", "x")),
                 })
        {
            await using var run = await Run.CreateAsync([Row("125.50", "75.25")], payments: [payment],
                states: state is null ? [] : [state]);

            var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);

            Assert.Equal((SalesPartyRiskFigureStateV1.Unavailable, FinanceSalesPartyRiskReadPortV1.OpenItemUnreadable),
                (answer.OpenReceivables.State, answer.OpenReceivables.ReasonCode));
        }
    }

    [Fact]
    public async Task Another_company_sees_none_of_the_items_and_a_customer_without_open_items_is_a_recorded_zero()
    {
        await using var run = await Run.CreateAsync(Row("125.50", "75.25"));

        var foreign = await run.Port(OtherCompany).ReadPartyRiskAsync(Party, Account);
        Assert.Equal((OtherCompany, SalesPartyRiskFigureStateV1.Available), (foreign.CompanyId, foreign.OpenReceivables.State));
        Assert.Empty(foreign.OpenReceivables.Amounts);

        var unknown = await run.Port(Company).ReadPartyRiskAsync(Guid.NewGuid(), Account);
        Assert.Empty(unknown.OpenReceivables.Amounts);
        // Review B P3: no customer named is not a recorded zero — nothing can be read for it.
        foreach (var (party, account) in new[] { (Guid.Empty, Guid.Empty), (Party, Guid.Empty), (Guid.Empty, Account) })
        {
            var empty = await run.Port(Company).ReadPartyRiskAsync(party, account);
            Assert.Equal((SalesPartyRiskFigureStateV1.Unavailable, FinanceSalesPartyRiskReadPortV1.CustomerUnidentified),
                (empty.OpenReceivables.State, empty.OpenReceivables.ReasonCode));
            Assert.Empty(empty.OpenReceivables.Amounts);
        }
    }

    [Fact]
    public async Task A_payable_of_the_same_party_is_neither_added_to_its_receivables_nor_netted_off_them()
    {
        // MOD-15 / ALS-D42: the party is also a supplier (DEC-CAR-001). Its purchase invoice installment is the company's
        // own debt: the risk read ignores it — it neither raises the receivable nor makes the figure unavailable.
        var payable = Row("500.00", "500.00");
        payable.SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialIncomingInvoiceInstallment;
        payable.Direction = FinanceOpenItemDirectionCodes.Payable;
        await using var run = await Run.CreateAsync(Row("125.50", "75.25"), payable);

        var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);

        Assert.Equal(SalesPartyRiskFigureStateV1.Available, answer.OpenReceivables.State);
        Assert.Equal([new SalesPartyRiskAmountV1(Try, 75.25m)], answer.OpenReceivables.Amounts);
    }

    [Theory]
    [InlineData("-1.00", "official.invoice-installment.v1")]
    [InlineData("1,00", "official.invoice-installment.v1")]
    [InlineData(" 1.00", "official.invoice-installment.v1")]
    [InlineData("1.00", "synthetic.opening-balance.v1")]
    public async Task A_row_finance_cannot_read_makes_the_receivables_unavailable_never_a_partial_sum(string outstanding, string sourceKind)
    {
        var broken = Row("5.00", "5.00");
        broken.OutstandingAmountText = outstanding;
        broken.SourceKind = sourceKind;
        await using var run = await Run.CreateAsync(Row("125.50", "75.25"), broken);

        var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);

        Assert.Equal((SalesPartyRiskFigureStateV1.Unavailable, FinanceSalesPartyRiskReadPortV1.OpenItemUnreadable),
            (answer.OpenReceivables.State, answer.OpenReceivables.ReasonCode));
        Assert.Empty(answer.OpenReceivables.Amounts);
    }

    [Fact]
    public async Task A_sum_an_exact_decimal_cannot_hold_is_unavailable_never_rounded()
    {
        // Finance keeps amounts without a digit limit: two of them can exceed System.Decimal.
        await using var run = await Run.CreateAsync(
            Row("79228162514264337593543950335", "79228162514264337593543950335"),
            Row("1", "1"));

        var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);
        Assert.Equal(SalesPartyRiskFigureStateV1.Unavailable, answer.OpenReceivables.State);

        await using var precise = await Run.CreateAsync(Row("0.12345678901234567890123456789", "0.12345678901234567890123456789"));
        Assert.Equal(SalesPartyRiskFigureStateV1.Unavailable,
            (await precise.Port(Company).ReadPartyRiskAsync(Party, Account)).OpenReceivables.State);
    }

    [Fact]
    public async Task The_credit_limit_is_the_accounts_one_approved_set_record_and_a_pending_proposal_never_counts()
    {
        await using var run = await Run.CreateAsync([Row("1", "1")], [], limits:
        [
            Limit(PartyCreditLimitStatus.Superseded, "100.00", revision: 1),
            Limit(PartyCreditLimitStatus.Approved, "250000.50", revision: 2),
            Limit(PartyCreditLimitStatus.Proposed, "999999.00"),
            Limit(PartyCreditLimitStatus.Approved, "5.00", revision: 1, account: Guid.NewGuid()),
            Limit(PartyCreditLimitStatus.Approved, "7.00", revision: 1, company: OtherCompany),
        ]);

        var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);

        Assert.Equal((SalesPartyRiskFigureStateV1.Available, FinanceSalesPartyRiskReadPortV1.CreditLimitSource, (bool?)null,
                (string?)null),
            (answer.CreditLimit.State, answer.CreditLimit.Source, answer.CreditLimit.IncludesTax, answer.CreditLimit.ReasonCode));
        // The exact amount in its own currency: never rounded, never converted.
        Assert.Equal([new SalesPartyRiskAmountV1(Try, 250000.50m)], answer.CreditLimit.Amounts);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task An_approved_remove_or_only_a_pending_or_rejected_proposal_is_no_source_never_zero()
    {
        await using (var removed = await Run.CreateAsync([Row("1", "1")], [], limits:
            [
                Limit(PartyCreditLimitStatus.Superseded, "100.00", revision: 1),
                Limit(PartyCreditLimitStatus.Approved, null, revision: 2),
            ]))
        {
            var answer = await removed.Port(Company).ReadPartyRiskAsync(Party, Account);
            Assert.Equal((SalesPartyRiskFigureStateV1.NoSource, FinanceSalesPartyRiskReadPortV1.CreditLimitNotSet),
                (answer.CreditLimit.State, answer.CreditLimit.ReasonCode));
            Assert.Empty(answer.CreditLimit.Amounts);
        }

        await using var pending = await Run.CreateAsync([Row("1", "1")], [], limits:
            [Limit(PartyCreditLimitStatus.Proposed, "100.00"), Limit(PartyCreditLimitStatus.Rejected, "50.00")]);
        var none = await pending.Port(Company).ReadPartyRiskAsync(Party, Account);
        Assert.Equal((SalesPartyRiskFigureStateV1.NoSource, FinanceSalesPartyRiskReadPortV1.CreditLimitNotSet),
            (none.CreditLimit.State, none.CreditLimit.ReasonCode));
        Assert.Empty(none.CreditLimit.Amounts);
    }

    [Theory]
    [InlineData("-1.00", false)]
    [InlineData("1,00", false)]
    [InlineData("0", false)]
    [InlineData("79228162514264337593543950336", false)]
    [InlineData("100.00", true)]
    public async Task A_limit_finance_cannot_read_or_one_of_another_party_is_unavailable_never_zero(string amount, bool otherParty)
    {
        await using var run = await Run.CreateAsync([Row("1", "1")], [], limits:
            [Limit(PartyCreditLimitStatus.Approved, amount, revision: 1, party: otherParty ? Guid.NewGuid() : null)]);

        var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);

        Assert.Equal((SalesPartyRiskFigureStateV1.Unavailable, FinanceSalesPartyRiskReadPortV1.CreditLimitUnreadable),
            (answer.CreditLimit.State, answer.CreditLimit.ReasonCode));
        Assert.Empty(answer.CreditLimit.Amounts);
    }

    [Fact]
    public async Task Without_a_named_customer_the_credit_limit_is_unavailable()
    {
        await using var run = await Run.CreateAsync([Row("1", "1")], [], limits:
            [Limit(PartyCreditLimitStatus.Approved, "100.00", revision: 1)]);

        var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Guid.Empty);

        Assert.Equal((SalesPartyRiskFigureStateV1.Unavailable, FinanceSalesPartyRiskReadPortV1.CustomerUnidentified),
            (answer.CreditLimit.State, answer.CreditLimit.ReasonCode));
    }

    [Fact]
    public async Task Without_a_resolved_company_nothing_is_read()
    {
        await using var run = await Run.CreateAsync(Row("1", "1"));
        var unresolved = new CompanyContext();
        var port = new FinanceSalesPartyRiskReadPortV1(run.Context(unresolved), unresolved);

        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ReadPartyRiskAsync(Party, Account));
    }

    private static FinanceOpenItemRow Row(string original, string outstanding, int status = FinanceOpenItemStatuses.Open,
        Guid? currency = null, Guid? party = null, Guid? account = null, Guid? company = null) => new()
    { Direction = FinanceOpenItemDirectionCodes.Receivable,
        Id = Guid.NewGuid(),
        CompanyId = company ?? Company,
        SourceKind = "official.invoice-installment.v1",
        SourceId = Guid.NewGuid(),
        SourceLineId = Guid.NewGuid(),
        SourceVersion = "invoice:v1",
        SourceBatchId = Guid.NewGuid(),
        SourceBatchVersion = "batch:v1",
        SourceBatchChecksum = new string('a', 64),
        PartyId = party ?? Party,
        CompanyAccountId = account ?? Account,
        CurrencyId = currency ?? Try,
        OriginalAmountText = original,
        OutstandingAmountText = outstanding,
        DueOn = new DateOnly(2026, 11, 1),
        Status = status,
        Version = 1,
        CreatedBy = "SENTETIK-seed",
        CreatedAtUtc = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero),
        UpdatedBy = "SENTETIK-seed",
        UpdatedAtUtc = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero),
    };

    private static MoneyEventRow Payment(Guid sourceId, string amount, int kind = MoneyEventKinds.ManualBankReceiptRecorded,
        Guid? currency = null, Guid? party = null, Guid? company = null) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = company ?? Company,
        SourceId = sourceId,
        Kind = kind,
        PartyId = party ?? Party,
        CompanyAccountId = Account,
        TreasuryAccountId = Guid.NewGuid(),
        CurrencyId = currency ?? Try,
        AmountText = amount,
        ProfileId = "SENTETIK-profile",
        ProfileVersion = "1",
        OccurredOn = new DateOnly(2026, 10, 27),
        RecordedAtUtc = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero),
        RecordedBy = "SENTETIK-seed",
        CorrelationId = "SENTETIK-correlation",
    };

    private static OutgoingMoneyEventRow Outgoing(string amount, Guid? currency = null, Guid? party = null, Guid? company = null) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = company ?? Company,
        PaymentId = Guid.NewGuid(),
        PartyId = party ?? Party,
        CompanyAccountId = Account,
        TreasuryAccountId = Guid.NewGuid(),
        CurrencyId = currency ?? Try,
        AmountText = amount,
        ProfileId = "SENTETIK-profile",
        ProfileVersion = "1",
        OccurredOn = new DateOnly(2026, 10, 27),
        RecordedAtUtc = new DateTimeOffset(2026, 10, 27, 11, 0, 0, TimeSpan.Zero),
        RecordedBy = "SENTETIK-seed",
        CorrelationId = "SENTETIK-correlation",
    };

    private static OutgoingMoneyEventReversalRow PaymentReversal(OutgoingMoneyEventRow original) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = original.CompanyId,
        PaymentId = original.PaymentId,
        OriginalEventId = original.Id,
        ProposalId = Guid.NewGuid(),
        PartyId = original.PartyId,
        CompanyAccountId = original.CompanyAccountId,
        TreasuryAccountId = original.TreasuryAccountId,
        CurrencyId = original.CurrencyId,
        AmountText = original.AmountText,
        ProfileId = original.ProfileId,
        ProfileVersion = original.ProfileVersion,
        OccurredOn = new DateOnly(2026, 10, 28),
        RecordedAtUtc = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero),
        RecordedBy = "SENTETIK-checker",
        CorrelationId = "SENTETIK-correlation",
    };

    private static FinanceSourceAllocationStateRow State(Guid sourceId, string original, string unallocated, Guid? currency = null) => new()
    {
        CompanyId = Company,
        SourceId = sourceId,
        CurrencyId = currency ?? Try,
        OriginalAmountText = original,
        UnallocatedAmountText = unallocated,
        Version = 2,
        UpdatedBy = "SENTETIK-seed",
        UpdatedAtUtc = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero),
    };

    private static ReversalProposalRow Reversal(Guid originalSourceId, bool approved) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Company,
        OriginalSourceId = originalSourceId,
        OccurredOn = new DateOnly(2026, 10, 27),
        Reason = "SENTETIK ters kayıt",
        Status = approved ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Pending,
        ProposedBy = "SENTETIK-maker",
        ProposedAtUtc = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero),
        DecidedBy = approved ? "SENTETIK-checker" : null,
        DecidedAtUtc = approved ? new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero) : null,
        ReversalSourceId = approved ? Guid.NewGuid() : null,
        Version = approved ? 2 : 1,
    };

    private static PartyCreditLimitRow Limit(PartyCreditLimitStatus status, string? amount, long? revision = null,
        Guid? party = null, Guid? account = null, Guid? company = null)
    {
        var at = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
        var decided = status is PartyCreditLimitStatus.Approved or PartyCreditLimitStatus.Rejected
            || (status == PartyCreditLimitStatus.Superseded && revision is not null);
        return new PartyCreditLimitRow
        {
            Id = Guid.NewGuid(),
            CompanyId = company ?? Company,
            PartyId = party ?? Party,
            CompanyAccountId = account ?? Account,
            Kind = amount is null ? FinancePartyCreditLimitService.RemoveKind : FinancePartyCreditLimitService.SetKind,
            CurrencyId = amount is null ? null : Try,
            AmountText = amount,
            Status = (int)status + 1,
            IsDirect = false,
            BaseRevision = revision is null ? 0 : revision.Value - 1,
            AccountRevision = revision,
            // The version the seeding steps reach: one per decision and one for the supersession.
            Version = 1 + (decided ? 1 : 0) + (status == PartyCreditLimitStatus.Superseded ? 1 : 0),
            ProposedBy = "SENTETIK-proposer",
            ProposedAtUtc = at,
            DecidedBy = decided ? "SENTETIK-approver" : null,
            DecidedAtUtc = decided ? at : null,
            RejectionReason = status == PartyCreditLimitStatus.Rejected ? "SENTETIK neden" : null,
            SupersededAtUtc = status == PartyCreditLimitStatus.Superseded ? at : null,
            UpdatedAtUtc = at,
        };
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string _database = $"party-risk-{Guid.NewGuid():N}";
        public FinanceDbContext Db { get; private set; } = null!;

        public static Task<Run> CreateAsync(params FinanceOpenItemRow[] rows) => CreateAsync(rows, [], [], []);

        public static async Task<Run> CreateAsync(FinanceOpenItemRow[] rows, MoneyEventRow[] payments,
            FinanceSourceAllocationStateRow[]? states = null, ReversalProposalRow[]? reversals = null,
            OutgoingMoneyEventRow[]? outgoing = null, OutgoingMoneyEventReversalRow[]? paymentReversals = null,
            PartyCreditLimitRow[]? limits = null)
        {
            var run = new Run();
            var companies = rows.Select(row => row.CompanyId).Concat(payments.Select(row => row.CompanyId))
                .Concat((outgoing ?? []).Select(row => row.CompanyId)).Concat((limits ?? []).Select(row => row.CompanyId))
                .Append(Company).Distinct();
            foreach (var company in companies)
            {
                var seed = new CompanyContext();
                seed.Set(company);
                await using var db = run.Context(seed);
                db.OpenItems.AddRange(rows.Where(row => row.CompanyId == company));
                db.MoneyEvents.AddRange(payments.Where(row => row.CompanyId == company));
                db.SourceAllocationStates.AddRange((states ?? []).Where(row => row.CompanyId == company));
                db.ReversalProposals.AddRange((reversals ?? []).Where(row => row.CompanyId == company));
                db.OutgoingMoneyEvents.AddRange((outgoing ?? []).Where(row => row.CompanyId == company));
                db.OutgoingMoneyEventReversals.AddRange((paymentReversals ?? []).Where(row => row.CompanyId == company));
                await db.SaveChangesAsync();
            }

            foreach (var limit in limits ?? [])
            {
                await run.SeedLimitAsync(limit);
            }

            return run;
        }

        /// <summary>
        /// FNS-D45: a credit limit row is born an undecided proposal and reaches its state only through the write
        /// guard's legal steps (decision, then supersession), one version at a time, as the service writes it.
        /// </summary>
        private async Task SeedLimitAsync(PartyCreditLimitRow target)
        {
            var seed = new CompanyContext();
            seed.Set(target.CompanyId);
            await using (var db = Context(seed))
            {
                db.PartyCreditLimits.Add(new PartyCreditLimitRow
                {
                    Id = target.Id, CompanyId = target.CompanyId, PartyId = target.PartyId,
                    CompanyAccountId = target.CompanyAccountId, Kind = target.Kind, CurrencyId = target.CurrencyId,
                    AmountText = target.AmountText, Status = FinancePartyCreditLimitService.Proposed, IsDirect = false,
                    BaseRevision = target.BaseRevision, Version = 1, ProposedBy = target.ProposedBy,
                    ProposedAtUtc = target.ProposedAtUtc, UpdatedAtUtc = target.ProposedAtUtc,
                });
                await db.SaveChangesAsync();
            }

            var steps = new List<Action<PartyCreditLimitRow>>();
            if (target.DecidedBy is not null)
            {
                steps.Add(row =>
                {
                    row.Status = target.RejectionReason is null
                        ? FinancePartyCreditLimitService.Approved
                        : FinancePartyCreditLimitService.Rejected;
                    row.DecidedBy = target.DecidedBy;
                    row.DecidedAtUtc = target.DecidedAtUtc;
                    row.AccountRevision = target.AccountRevision;
                    row.RejectionReason = target.RejectionReason;
                });
            }

            if (target.SupersededAtUtc is not null)
            {
                steps.Add(row =>
                {
                    row.Status = FinancePartyCreditLimitService.Superseded;
                    row.SupersededAtUtc = target.SupersededAtUtc;
                });
            }

            foreach (var step in steps)
            {
                await using var db = Context(seed);
                var row = await db.PartyCreditLimits.SingleAsync(candidate => candidate.Id == target.Id);
                step(row);
                row.UpdatedAtUtc = target.UpdatedAtUtc;
                row.Version += 1;
                await db.SaveChangesAsync();
            }
        }

        public FinanceSalesPartyRiskReadPortV1 Port(Guid company)
        {
            var context = new CompanyContext();
            context.Set(company);
            Db = Context(context);
            return new FinanceSalesPartyRiskReadPortV1(Db, context);
        }

        public FinanceDbContext Context(ICompanyContext company) => new(
            new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(_database).Options, company);

        public async ValueTask DisposeAsync()
        {
            if (Db is not null)
            {
                await Db.DisposeAsync();
            }
        }
    }
}
