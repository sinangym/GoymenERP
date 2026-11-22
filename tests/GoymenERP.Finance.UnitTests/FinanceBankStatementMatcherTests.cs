using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// WP12: no tolerance or matching policy is decided, so only the exact rule suggests (direction + exact amount + date +
/// proven counterparty IBAN, one-to-one). SENTETIK IBANs carry valid ISO 13616 check digits.
/// </summary>
public sealed class FinanceBankStatementMatcherTests
{
    private const string Own = "TR420001000000000000000001";
    private const string Other = "TR130006200000000000000002";
    private const string Third = "TR550011100000000000000003";
    private static readonly DateOnly Day = new(2026, 11, 17);

    private static FinanceStatementMatchLine Line(string amount = "100.00", string? iban = Other, DateOnly? on = null,
        FinanceBankStatementDirectionV1 direction = FinanceBankStatementDirectionV1.Inflow) =>
        new(Guid.NewGuid(), on ?? Day, direction, amount, iban);

    private static FinanceStatementMovement Transfer(string amount = "100.0", string? iban = Other, DateOnly? on = null) =>
        new(FinanceBankStatementCandidateKindV1.Transfer, Guid.NewGuid(), on ?? Day, FinanceBankStatementDirectionV1.Inflow,
            amount, iban is not null, iban);

    private static FinanceStatementMovement Receipt(string amount = "100.00") =>
        new(FinanceBankStatementCandidateKindV1.Receipt, Guid.NewGuid(), Day, FinanceBankStatementDirectionV1.Inflow, amount,
            false, null);

    [Fact]
    public void Only_an_exact_amount_date_direction_and_counterparty_transfer_is_suggested()
    {
        var line = Line();
        var transfer = Transfer();
        var suggestion = FinanceBankStatementMatcher.Suggest([line], [transfer, Receipt()])[line.LineId];
        Assert.Equal(FinanceBankStatementMatchStatusV1.Suggested, suggestion.Status);
        Assert.Null(suggestion.ManualReason);
        Assert.Equal((FinanceBankStatementCandidateKindV1.Transfer, transfer.RecordId, Day),
            (suggestion.Candidate!.Kind, suggestion.Candidate.RecordId, suggestion.Candidate.OccurredOn));
    }

    [Fact]
    public void Each_near_miss_stays_manual_with_its_reason()
    {
        var oneCent = Line("100.01");
        var nextDay = Line(on: Day.AddDays(1));
        var outflow = Line(direction: FinanceBankStatementDirectionV1.Outflow);
        var noIban = Line(iban: null);
        var party = Line("50.00");
        var wrongIban = Line("75.00", Third);
        var result = FinanceBankStatementMatcher.Suggest([oneCent, nextDay, outflow, noIban, party, wrongIban],
            [Transfer(), Receipt("50.00"), Transfer("75.00")]);
        Assert.Equal(FinanceBankStatementManualReasonV1.NoExactCandidate, result[oneCent.LineId].ManualReason);
        Assert.Equal(FinanceBankStatementManualReasonV1.NoExactCandidate, result[nextDay.LineId].ManualReason);
        Assert.Equal(FinanceBankStatementManualReasonV1.NoExactCandidate, result[outflow.LineId].ManualReason);
        Assert.Equal(FinanceBankStatementManualReasonV1.NoCounterpartyIdentity, result[noIban.LineId].ManualReason);
        Assert.Equal(FinanceBankStatementManualReasonV1.CounterpartyNotComparable, result[party.LineId].ManualReason);
        Assert.Equal(FinanceBankStatementManualReasonV1.NoExactCandidate, result[wrongIban.LineId].ManualReason);
        Assert.All(result.Values, value =>
        {
            Assert.Equal(FinanceBankStatementMatchStatusV1.Manual, value.Status);
            Assert.Null(value.Candidate);
        });
    }

    [Fact]
    public void Two_passing_movements_or_two_lines_on_one_movement_are_ambiguous_and_never_chosen()
    {
        var line = Line();
        Assert.Equal(FinanceBankStatementManualReasonV1.Ambiguous,
            FinanceBankStatementMatcher.Suggest([line], [Transfer(), Transfer()])[line.LineId].ManualReason);
        var first = Line();
        var second = Line();
        var shared = FinanceBankStatementMatcher.Suggest([first, second], [Transfer()]);
        Assert.Equal(FinanceBankStatementManualReasonV1.Ambiguous, shared[first.LineId].ManualReason);
        Assert.Equal(FinanceBankStatementManualReasonV1.Ambiguous, shared[second.LineId].ManualReason);
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

    [Fact]
    public void Fingerprint_ignores_trailing_fraction_zeros_but_not_any_other_content()
    {
        var line = new FinanceParsedStatementLine(1, Day, null, FinanceBankStatementDirectionV1.Inflow, "10.50", null, "TRY",
            "R", "SENTETIK");
        Assert.Equal(FinanceBankStatementLinePolicy.Fingerprint(line), FinanceBankStatementLinePolicy.Fingerprint(line with
        {
            Amount = "10.5", LineNumber = 9,
        }));
        Assert.NotEqual(FinanceBankStatementLinePolicy.Fingerprint(line), FinanceBankStatementLinePolicy.Fingerprint(line with
        {
            Direction = FinanceBankStatementDirectionV1.Outflow,
        }));
        Assert.NotEqual(FinanceBankStatementLinePolicy.Fingerprint(line),
            FinanceBankStatementLinePolicy.Fingerprint(line with { Description = "SENTETIK " + "2" }));
        Assert.NotEqual(FinanceBankStatementLinePolicy.Fingerprint(line),
            FinanceBankStatementLinePolicy.Fingerprint(line with { Amount = "105" }));
    }
}
