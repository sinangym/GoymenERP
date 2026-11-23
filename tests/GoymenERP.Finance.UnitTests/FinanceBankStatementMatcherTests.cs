using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// WP12 / FNS-D113 (exact amount, no tolerance, a person confirms) and FNS-D114 (the counterparty IBAN is looked up only in
/// the cari IBAN lists). SENTETIK IBANs carry valid ISO 13616 check digits.
/// </summary>
public sealed class FinanceBankStatementMatcherTests
{
    private const string Own = "TR420001000000000000000001";
    private const string Other = "TR130006200000000000000002";
    private const string Third = "TR550011100000000000000003";
    private static readonly DateOnly Day = new(2026, 11, 17);
    private static readonly Guid Party = Guid.Parse("f3852000-0000-4000-8000-000000000001");
    private static readonly Guid OtherParty = Guid.Parse("f3852000-0000-4000-8000-000000000002");

    private static FinanceStatementMatchLine Line(string amount = "100.00", string? iban = Other, DateOnly? on = null,
        FinanceBankStatementDirectionV1 direction = FinanceBankStatementDirectionV1.Inflow) =>
        new(Guid.NewGuid(), on ?? Day, direction, amount, iban);

    private static FinanceStatementMovement Transfer(string amount = "100.0", string? iban = Other) =>
        new(FinanceBankStatementCandidateKindV1.Transfer, Guid.NewGuid(), Day, FinanceBankStatementDirectionV1.Inflow, amount, null, iban);

    private static FinanceStatementMovement Receipt(string amount = "100.00", Guid? party = null) =>
        new(FinanceBankStatementCandidateKindV1.Receipt, Guid.NewGuid(), Day, FinanceBankStatementDirectionV1.Inflow, amount,
            party ?? Party, null);

    private static FinanceStatementPartyIbans Lists(params (string Iban, Guid Party)[] rows) => new(true,
        rows.GroupBy(row => row.Iban).ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)group.Select(row => row.Party).ToArray()));

    [Fact]
    public void A_single_exact_amount_date_and_direction_record_is_suggested_without_iban_source_as_not_available()
    {
        var line = Line(iban: null);
        var receipt = Receipt();
        var suggestion = FinanceBankStatementMatcher.Suggest([line], [receipt], FinanceStatementPartyIbans.Off)[line.LineId];
        Assert.Equal((FinanceBankStatementMatchStatusV1.Suggested, receipt.RecordId, FinanceBankStatementCounterpartyCheckV1.NotAvailable),
            (suggestion.Status, suggestion.Candidate!.RecordId, suggestion.Candidate.CounterpartyCheck));
    }

    [Fact]
    public void No_tolerance_one_cent_another_day_or_direction_is_manual()
    {
        var cent = Line("100.01");
        var day = Line(on: Day.AddDays(1));
        var outflow = Line(direction: FinanceBankStatementDirectionV1.Outflow);
        var result = FinanceBankStatementMatcher.Suggest([cent, day, outflow], [Receipt()], FinanceStatementPartyIbans.Off);
        Assert.All(result.Values, value =>
            Assert.Equal((FinanceBankStatementMatchStatusV1.Manual, FinanceBankStatementManualReasonV1.NoExactCandidate, (FinanceBankStatementCandidateV1?)null),
                (value.Status, value.ManualReason, value.Candidate)));
    }

    [Fact]
    public void The_cari_iban_list_verifies_one_of_two_same_amount_receipts_and_removes_a_mismatching_one()
    {
        var line = Line();
        var mine = Receipt(party: Party);
        var theirs = Receipt(party: OtherParty);
        var lists = Lists((Other, Party));
        var suggestion = FinanceBankStatementMatcher.Suggest([line], [mine, theirs], lists)[line.LineId];
        Assert.Equal((mine.RecordId, FinanceBankStatementCounterpartyCheckV1.Verified),
            (suggestion.Candidate!.RecordId, suggestion.Candidate.CounterpartyCheck));

        var onlyTheirs = FinanceBankStatementMatcher.Suggest([line], [theirs], lists)[line.LineId];
        Assert.Equal(FinanceBankStatementManualReasonV1.CounterpartyMismatch, onlyTheirs.ManualReason);
        Assert.Equal(FinanceBankStatementManualReasonV1.Ambiguous,
            FinanceBankStatementMatcher.Suggest([line], [mine, theirs], FinanceStatementPartyIbans.Off)[line.LineId].ManualReason);
    }

    [Fact]
    public void An_iban_on_no_card_or_shared_by_both_parties_is_not_a_verification()
    {
        var line = Line();
        var receipt = Receipt();
        Assert.Equal(FinanceBankStatementCounterpartyCheckV1.NotAvailable,
            FinanceBankStatementMatcher.Check(Third, receipt, Lists((Other, Party))));
        Assert.Equal(FinanceBankStatementCounterpartyCheckV1.Verified,
            FinanceBankStatementMatcher.Check(Other, receipt, Lists((Other, Party), (Other, OtherParty))));
        var expense = new FinanceStatementMovement(FinanceBankStatementCandidateKindV1.ExpensePayment, Guid.NewGuid(), Day,
            FinanceBankStatementDirectionV1.Outflow, "1", null, null);
        Assert.Equal(FinanceBankStatementCounterpartyCheckV1.NotAvailable,
            FinanceBankStatementMatcher.Check(Other, expense, Lists((Other, Party))));
        Assert.Equal(FinanceBankStatementMatchStatusV1.Suggested,
            FinanceBankStatementMatcher.Suggest([line], [receipt], Lists((Third, OtherParty)))[line.LineId].Status);
    }

    [Fact]
    public void A_transfer_is_verified_by_the_other_own_accounts_approved_iban()
    {
        Assert.Equal(FinanceBankStatementCounterpartyCheckV1.Verified,
            FinanceBankStatementMatcher.Check(Other, Transfer(), FinanceStatementPartyIbans.Off));
        Assert.Equal(FinanceBankStatementCounterpartyCheckV1.Mismatch,
            FinanceBankStatementMatcher.Check(Third, Transfer(), FinanceStatementPartyIbans.Off));
        var foreign = Line(iban: Third);
        Assert.Equal(FinanceBankStatementManualReasonV1.CounterpartyMismatch,
            FinanceBankStatementMatcher.Suggest([foreign], [Transfer()], FinanceStatementPartyIbans.Off)[foreign.LineId].ManualReason);
    }

    [Fact]
    public void Two_lines_on_one_record_are_ambiguous_and_a_confirmed_line_or_record_is_never_suggested_again()
    {
        var first = Line(iban: null);
        var second = Line(iban: null);
        var receipt = Receipt();
        var shared = FinanceBankStatementMatcher.Suggest([first, second], [receipt], FinanceStatementPartyIbans.Off);
        Assert.All(shared.Values, value => Assert.Equal(FinanceBankStatementManualReasonV1.Ambiguous, value.ManualReason));

        var match = new FinanceBankStatementMatchViewV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), first.LineId,
            FinanceBankStatementCandidateKindV1.Receipt, receipt.RecordId, "1", FinanceBankStatementMatchStateV1.Confirmed,
            FinanceBankStatementCounterpartyCheckV1.NotAvailable, "SENTETIK", DateTimeOffset.UnixEpoch, null, null, null, null);
        var after = FinanceBankStatementMatcher.Suggest([first, second], [receipt], FinanceStatementPartyIbans.Off,
            new Dictionary<Guid, FinanceBankStatementMatchViewV1> { [first.LineId] = match });
        Assert.Equal((FinanceBankStatementMatchStatusV1.Confirmed, match), (after[first.LineId].Status, after[first.LineId].Match));
        Assert.Equal(receipt.RecordId, after[first.LineId].Candidate!.RecordId);
        Assert.Equal(FinanceBankStatementManualReasonV1.NoExactCandidate, after[second.LineId].ManualReason);
    }

    [Theory]
    [InlineData("EFT TR13 0006 2000 0000 0000 0000 02 SENTETIK", null, Other)]
    [InlineData("Gonderen TR130006200000000000000002", null, Other)]
    [InlineData("Kendi hesap TR420001000000000000000001 ve TR130006200000000000000002", null, Other)]
    [InlineData("Iki IBAN TR130006200000000000000002", "TR550011100000000000000003", null)]
    [InlineData("Bozuk TR130006200000000000000003", null, null)]
    [InlineData("Uzun TR1300062000000000000000021", null, null)]
    [InlineData("XTR130006200000000000000002", null, null)]
    [InlineData("Yalniz kendi TR420001000000000000000001", null, null)]
    public void Counterparty_iban_is_one_valid_turkish_iban_other_than_the_own_account(string description, string? reference,
        string? expected) =>
        Assert.Equal(expected, FinanceBankStatementLinePolicy.CounterpartyIban(reference, description, Own));

    [Theory]
    [InlineData("SEPA DE89 3704 0044 0532 0130 00 SENTETIK", null, "DE89370400440532013000")]
    [InlineData("Wire GB82WEST12345698765432", null, "GB82WEST12345698765432")]
    [InlineData("Ref NL91ABNA0417164300 fatura", null, "NL91ABNA0417164300")]
    [InlineData("Bozuk DE88370400440532013000", null, null)]
    [InlineData("Kisa DE8937040044053201300", null, null)]
    [InlineData("Bilinmeyen XX89370400440532013000", null, null)]
    [InlineData("Iki ulke DE89370400440532013000", "NL91ABNA0417164300", null)]
    public void Fns_d122_a_foreign_iban_of_a_registered_country_is_read_with_its_length_and_mod_97(string description,
        string? reference, string? expected) =>
        Assert.Equal(expected, FinanceBankStatementLinePolicy.CounterpartyIban(reference, description, Own));

    [Fact]
    public void Fingerprint_ignores_trailing_fraction_zeros_but_not_any_other_content()
    {
        var line = new FinanceParsedStatementLine(1, Day, null, FinanceBankStatementDirectionV1.Inflow, "10.50", null, "TRY",
            "R", "SENTETIK");
        Assert.Equal(FinanceBankStatementLinePolicy.Fingerprint(line),
            FinanceBankStatementLinePolicy.Fingerprint(line with { Amount = "10.5", LineNumber = 9 }));
        Assert.NotEqual(FinanceBankStatementLinePolicy.Fingerprint(line),
            FinanceBankStatementLinePolicy.Fingerprint(line with { Direction = FinanceBankStatementDirectionV1.Outflow }));
        Assert.NotEqual(FinanceBankStatementLinePolicy.Fingerprint(line),
            FinanceBankStatementLinePolicy.Fingerprint(line with { Description = "SENTETIK 2" }));
        Assert.NotEqual(FinanceBankStatementLinePolicy.Fingerprint(line),
            FinanceBankStatementLinePolicy.Fingerprint(line with { Amount = "105" }));
    }
}
