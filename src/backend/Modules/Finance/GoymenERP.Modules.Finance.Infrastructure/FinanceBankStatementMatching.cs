using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// The deterministic line rules shared by the import and the suggestion read: the line fingerprint that protects against
/// a second import of the same line, and the counterparty IBAN a line names.
/// </summary>
internal static class FinanceBankStatementLinePolicy
{
    private const int TurkishIbanLength = 26;

    /// <summary>
    /// The SHA-256 (upper-case hex) of the line's canonical content: dates, direction, exact amount (trailing fraction
    /// zeros dropped, so <c>10,50</c> and <c>10,5</c> are one amount), balance, currency, reference and description. The bank
    /// account is the index's other half, so the same line on another account is another line. Two identical lines in one
    /// file are kept apart by their occurrence ordinal, so a genuine repeat is stored and a re-read line is not.
    /// </summary>
    public static string Fingerprint(FinanceParsedStatementLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var canonical = string.Join('\u001f',
            line.TransactionOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            line.ValueOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            line.Direction == FinanceBankStatementDirectionV1.Inflow ? "I" : "O",
            Normalize(line.Amount),
            line.Balance is null ? string.Empty : (line.Balance.StartsWith('-') ? "-" + Normalize(line.Balance[1..]) : Normalize(line.Balance)),
            line.CurrencyText ?? string.Empty,
            line.Reference ?? string.Empty,
            line.Description);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>
    /// The one counterparty IBAN the line names, or null. Only Turkish IBANs (<c>TR</c> + 24 digits, spaces between groups
    /// allowed) with a valid ISO 13616 check are read from the reference and description; the account's own IBAN is ignored.
    /// Zero or more than one distinct IBAN is "no identity": nothing is chosen between two.
    /// </summary>
    public static string? CounterpartyIban(string? reference, string description, string? ownIban)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in new[] { reference, description })
        {
            if (string.IsNullOrEmpty(text)) continue;
            var upper = text.ToUpperInvariant();
            for (var start = 0; start + 1 < upper.Length; start++)
            {
                if (upper[start] != 'T' || upper[start + 1] != 'R') continue;
                if (start > 0 && char.IsAsciiLetterOrDigit(upper[start - 1])) continue;
                var candidate = new StringBuilder("TR");
                var position = start + 2;
                while (position < upper.Length && candidate.Length < TurkishIbanLength)
                {
                    var character = upper[position];
                    if (char.IsAsciiDigit(character)) candidate.Append(character);
                    else if (character != ' ' || position + 1 >= upper.Length || !char.IsAsciiDigit(upper[position + 1])) break;
                    position++;
                }

                if (candidate.Length != TurkishIbanLength) continue;
                if (position < upper.Length && char.IsAsciiDigit(upper[position])) continue;
                var iban = candidate.ToString();
                if (FinanceIban.IsStructurallyValid(iban) && !string.Equals(iban, ownIban, StringComparison.Ordinal)) found.Add(iban);
            }
        }

        return found.Count == 1 ? found.Single() : null;
    }

    private static string Normalize(string amount)
    {
        if (!amount.Contains('.')) return amount;
        var trimmed = amount.TrimEnd('0');
        return trimmed.EndsWith('.') ? trimmed[..^1] : trimmed;
    }
}

/// <summary>A recorded Finance movement on the statement's bank account, with the counterparty identity it can prove.</summary>
internal sealed record FinanceStatementMovement(
    FinanceBankStatementCandidateKindV1 Kind,
    Guid RecordId,
    DateOnly OccurredOn,
    FinanceBankStatementDirectionV1 Direction,
    string Amount,
    bool CounterpartyComparable,
    string? CounterpartyIban);

internal sealed record FinanceStatementMatchLine(
    Guid LineId,
    DateOnly TransactionOn,
    FinanceBankStatementDirectionV1 Direction,
    string Amount,
    string? CounterpartyIban);

/// <summary>
/// WP12 / FNS-D13: the suggestion rule. No tolerance or matching policy is decided, so the only rule is the exact one:
/// same bank account (the movements given), same direction, exactly equal amount, the line's transaction date equal to the
/// movement's date, and the line's counterparty IBAN equal to the movement's proven counterparty IBAN. Exactly one movement
/// must pass, and no other line of the same import may point at it; everything else is Manual ("elle"). Nothing is
/// recorded: a suggestion is a read.
/// </summary>
internal static class FinanceBankStatementMatcher
{
    public static IReadOnlyDictionary<Guid, FinanceBankStatementSuggestionV1> Suggest(
        IReadOnlyList<FinanceStatementMatchLine> lines, IReadOnlyList<FinanceStatementMovement> movements)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(movements);
        var first = new Dictionary<Guid, (FinanceBankStatementManualReasonV1? Reason, FinanceStatementMovement? Movement)>();
        foreach (var line in lines)
        {
            var exact = movements.Where(movement => movement.Direction == line.Direction
                    && movement.OccurredOn == line.TransactionOn
                    && FinanceExactDecimalText.Equal(movement.Amount, line.Amount))
                .ToArray();
            if (exact.Length == 0)
            {
                first[line.LineId] = (FinanceBankStatementManualReasonV1.NoExactCandidate, null);
                continue;
            }

            if (line.CounterpartyIban is null)
            {
                first[line.LineId] = (FinanceBankStatementManualReasonV1.NoCounterpartyIdentity, null);
                continue;
            }

            var comparable = exact.Where(movement => movement.CounterpartyComparable && movement.CounterpartyIban is not null).ToArray();
            if (comparable.Length == 0)
            {
                first[line.LineId] = (FinanceBankStatementManualReasonV1.CounterpartyNotComparable, null);
                continue;
            }

            var passing = comparable.Where(movement => string.Equals(movement.CounterpartyIban, line.CounterpartyIban,
                StringComparison.Ordinal)).ToArray();
            first[line.LineId] = passing.Length switch
            {
                0 => (FinanceBankStatementManualReasonV1.NoExactCandidate, null),
                1 => (null, passing[0]),
                _ => (FinanceBankStatementManualReasonV1.Ambiguous, null),
            };
        }

        var claims = first.Values.Where(value => value.Movement is not null)
            .GroupBy(value => (value.Movement!.Kind, value.Movement.RecordId))
            .ToDictionary(group => group.Key, group => group.Count());
        var result = new Dictionary<Guid, FinanceBankStatementSuggestionV1>();
        foreach (var (lineId, (reason, movement)) in first)
        {
            if (movement is not null && claims[(movement.Kind, movement.RecordId)] == 1)
            {
                result[lineId] = new FinanceBankStatementSuggestionV1(FinanceBankStatementMatchStatusV1.Suggested, null,
                    new FinanceBankStatementCandidateV1(movement.Kind, movement.RecordId, movement.OccurredOn));
            }
            else
            {
                result[lineId] = new FinanceBankStatementSuggestionV1(FinanceBankStatementMatchStatusV1.Manual,
                    reason ?? FinanceBankStatementManualReasonV1.Ambiguous, null);
            }
        }

        return result;
    }
}
