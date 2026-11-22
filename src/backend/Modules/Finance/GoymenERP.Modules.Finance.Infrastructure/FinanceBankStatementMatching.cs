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
    /// Zero or more than one distinct IBAN is "no identity": nothing is chosen between two. The IBAN is only LOOKED UP in the
    /// cari IBAN lists (FNS-D114); it is never written to a cari card.
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

/// <summary>
/// A recorded Finance movement on the statement's bank account. <c>PartyId</c> is set for a party receipt/payment;
/// <c>CounterpartyIban</c> for a transfer leg whose other own bank account has an approved IBAN.
/// </summary>
internal sealed record FinanceStatementMovement(
    FinanceBankStatementCandidateKindV1 Kind,
    Guid RecordId,
    DateOnly OccurredOn,
    FinanceBankStatementDirectionV1 Direction,
    string Amount,
    Guid? PartyId,
    string? CounterpartyIban);

internal sealed record FinanceStatementMatchLine(
    Guid LineId,
    DateOnly TransactionOn,
    FinanceBankStatementDirectionV1 Direction,
    string Amount,
    string? CounterpartyIban);

/// <summary>
/// FNS-D114: what the cari IBAN lists say. <c>Available</c> is false when no IBAN list source exists (party IBAN matching
/// off); otherwise each listed IBAN maps to the parties whose card lists it.
/// </summary>
internal sealed record FinanceStatementPartyIbans(bool Available, IReadOnlyDictionary<string, IReadOnlyList<Guid>> PartiesByIban)
{
    public static FinanceStatementPartyIbans Off { get; } = new(false, new Dictionary<string, IReadOnlyList<Guid>>());
}

/// <summary>
/// WP12 / FNS-D113 / FNS-D114: the suggestion rule. No tolerance: a candidate is an unmatched recorded movement of the same
/// bank account with the same direction, exactly the same amount and the line's transaction date. The line's single IBAN is
/// compared where a source exists (the cari IBAN list for a party record, the other own account's approved IBAN for a
/// transfer): a Mismatch removes the candidate, a Verified one wins over NotAvailable ones. Exactly one remaining candidate,
/// pointed at by no other line of the import, is Suggested; everything else is Manual ("elle"). A suggestion is a read: it
/// becomes a match only when a person confirms it. A line with an active confirmed match is shown as Confirmed.
/// </summary>
internal static class FinanceBankStatementMatcher
{
    public static FinanceBankStatementCounterpartyCheckV1 Check(string? lineIban, FinanceStatementMovement movement,
        FinanceStatementPartyIbans parties)
    {
        if (lineIban is null) return FinanceBankStatementCounterpartyCheckV1.NotAvailable;
        if (movement.Kind == FinanceBankStatementCandidateKindV1.Transfer)
        {
            return movement.CounterpartyIban is null
                ? FinanceBankStatementCounterpartyCheckV1.NotAvailable
                : string.Equals(movement.CounterpartyIban, lineIban, StringComparison.Ordinal)
                    ? FinanceBankStatementCounterpartyCheckV1.Verified
                    : FinanceBankStatementCounterpartyCheckV1.Mismatch;
        }

        if (movement.PartyId is not { } party || !parties.Available
            || !parties.PartiesByIban.TryGetValue(lineIban, out var owners) || owners.Count == 0)
            return FinanceBankStatementCounterpartyCheckV1.NotAvailable;
        return owners.Contains(party)
            ? FinanceBankStatementCounterpartyCheckV1.Verified
            : FinanceBankStatementCounterpartyCheckV1.Mismatch;
    }

    public static IReadOnlyDictionary<Guid, FinanceBankStatementSuggestionV1> Suggest(
        IReadOnlyList<FinanceStatementMatchLine> lines, IReadOnlyList<FinanceStatementMovement> movements,
        FinanceStatementPartyIbans parties, IReadOnlyDictionary<Guid, FinanceBankStatementMatchViewV1>? activeMatches = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(movements);
        ArgumentNullException.ThrowIfNull(parties);
        activeMatches ??= new Dictionary<Guid, FinanceBankStatementMatchViewV1>();
        var matched = activeMatches.Values.Select(match => (match.Kind, match.RecordId)).ToHashSet();
        var first = new Dictionary<Guid, (FinanceBankStatementManualReasonV1? Reason, FinanceStatementMovement? Movement,
            FinanceBankStatementCounterpartyCheckV1 Check)>();
        foreach (var line in lines.Where(line => !activeMatches.ContainsKey(line.LineId)))
        {
            var exact = movements.Where(movement => !matched.Contains((movement.Kind, movement.RecordId))
                    && movement.Direction == line.Direction && movement.OccurredOn == line.TransactionOn
                    && FinanceExactDecimalText.Equal(movement.Amount, line.Amount))
                .Select(movement => (Movement: movement, Check: Check(line.CounterpartyIban, movement, parties)))
                .ToArray();
            if (exact.Length == 0)
            {
                first[line.LineId] = (FinanceBankStatementManualReasonV1.NoExactCandidate, null, default);
                continue;
            }

            var remaining = exact.Where(item => item.Check != FinanceBankStatementCounterpartyCheckV1.Mismatch).ToArray();
            if (remaining.Length == 0)
            {
                first[line.LineId] = (FinanceBankStatementManualReasonV1.CounterpartyMismatch, null, default);
                continue;
            }

            var verified = remaining.Where(item => item.Check == FinanceBankStatementCounterpartyCheckV1.Verified).ToArray();
            var pool = verified.Length > 0 ? verified : remaining;
            first[line.LineId] = pool.Length == 1
                ? (null, pool[0].Movement, pool[0].Check)
                : (FinanceBankStatementManualReasonV1.Ambiguous, null, default);
        }

        var claims = first.Values.Where(value => value.Movement is not null)
            .GroupBy(value => (value.Movement!.Kind, value.Movement.RecordId))
            .ToDictionary(group => group.Key, group => group.Count());
        var result = new Dictionary<Guid, FinanceBankStatementSuggestionV1>();
        foreach (var (lineId, match) in activeMatches)
        {
            if (lines.All(line => line.LineId != lineId)) continue;
            var occurredOn = movements.FirstOrDefault(movement => movement.Kind == match.Kind && movement.RecordId == match.RecordId)
                ?.OccurredOn;
            result[lineId] = new FinanceBankStatementSuggestionV1(FinanceBankStatementMatchStatusV1.Confirmed, null,
                occurredOn is { } on ? new FinanceBankStatementCandidateV1(match.Kind, match.RecordId, on, match.CounterpartyCheck) : null,
                match);
        }

        foreach (var (lineId, (reason, movement, check)) in first)
        {
            result[lineId] = movement is not null && claims[(movement.Kind, movement.RecordId)] == 1
                ? new FinanceBankStatementSuggestionV1(FinanceBankStatementMatchStatusV1.Suggested, null,
                    new FinanceBankStatementCandidateV1(movement.Kind, movement.RecordId, movement.OccurredOn, check))
                : new FinanceBankStatementSuggestionV1(FinanceBankStatementMatchStatusV1.Manual,
                    reason ?? FinanceBankStatementManualReasonV1.Ambiguous, null);
        }

        return result;
    }
}
