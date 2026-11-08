using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// The first-release party ledger (cari hesap ekstresi): one party account's RECORDED Finance facts in an inclusive
/// business-date range, read from the tenant-filtered <see cref="FinanceDbContext"/> only (the context company's rows;
/// no raw SQL, no filter bypass, no write):
/// <list type="bullet">
/// <item>every money event of the account — a recorded receipt (kind 1), a FIN-T06 reversal counter-event (kind 2, its
/// own row, linked to the original through its approved proposal) and a cheque / note collection (kind 3, linked to its
/// instrument) — by its <c>OccurredOn</c>;</item>
/// <item>every outgoing money event of a recorded payment to the account, by its <c>OccurredOn</c>;</item>
/// <item>every admitted open item of the account, by the UTC day Finance admitted it (the source contract carries no
/// document date yet), with its official document and due date;</item>
/// <item>F2b: every invoice-cancellation reversal of one of those open items, by the UTC day of the cancellation.</item>
/// </list>
/// Drafts are never read: a draft has no money event and no open item. Each fact is one stored row, read once, so a
/// replayed command (which stored nothing new) and a reversal (which stored its own counter-event) appear exactly as
/// recorded. F-03 allocations are not rows (DEC-FIN-006: a distribution is not a second movement). OPEN-018: nothing
/// before the range is carried in. A money event kind this read does not know fails the read rather than being
/// silently left out, because a missing row would be a wrong balance.
/// <para>
/// Authority follows the portfolio's risk view: <c>ReadPartyLedger</c> is measured afresh before anything is read and
/// again after the reads; every amount, total and balance is disclosed only with a fresh <c>ReadPartyLedgerAmount</c>.
/// </para>
/// </summary>
public sealed class PartyLedgerService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions) : IPartyLedgerService
{
    public async Task<PartyLedgerResult> ReadAuthorizedAsync(FinanceContext context, PartyLedgerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return Refused(PartyLedgerOutcome.Denied);
        }

        var read = await permissions.CheckAsync(context, FinanceAction.ReadPartyLedger, null, cancellationToken);
        if (read != PermissionDecision.Allowed || !SameContext(context))
        {
            return Refused(ReadRefusal(read));
        }

        if (request.PartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty || request.To < request.From
            || request.To == DateOnly.MaxValue)
        {
            return Refused(PartyLedgerOutcome.Invalid);
        }

        var partyId = request.PartyId;
        var accountId = request.CompanyAccountId;
        var from = request.From;
        var to = request.To;
        // An open item's day is the UTC day of its admission: [from 00:00Z, to+1 00:00Z).
        var admittedFrom = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var admittedBefore = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var take = PartyLedgerRules.MaximumEntries + 1;

        var moneyEvents = await dbContext.MoneyEvents.AsNoTracking()
            .Where(row => row.PartyId == partyId && row.CompanyAccountId == accountId
                && row.OccurredOn >= from && row.OccurredOn <= to)
            .OrderBy(row => row.OccurredOn).ThenBy(row => row.Id)
            .Take(take)
            .Select(row => new MoneyEventFact(row.Id, row.SourceId, row.Kind, row.CurrencyId, row.AmountText,
                row.OccurredOn, row.RecordedAtUtc))
            .ToListAsync(cancellationToken);
        var payments = await dbContext.OutgoingMoneyEvents.AsNoTracking()
            .Where(row => row.PartyId == partyId && row.CompanyAccountId == accountId
                && row.OccurredOn >= from && row.OccurredOn <= to)
            .OrderBy(row => row.OccurredOn).ThenBy(row => row.Id)
            .Take(take)
            .Select(row => new PaymentFact(row.Id, row.PaymentId, row.CurrencyId, row.AmountText, row.OccurredOn,
                row.RecordedAtUtc))
            .ToListAsync(cancellationToken);
        var openItems = await dbContext.OpenItems.AsNoTracking()
            .Where(row => row.PartyId == partyId && row.CompanyAccountId == accountId
                && row.CreatedAtUtc >= admittedFrom && row.CreatedAtUtc < admittedBefore)
            .OrderBy(row => row.CreatedAtUtc).ThenBy(row => row.Id)
            .Take(take)
            .Select(row => new OpenItemFact(row.Id, row.SourceId, row.CurrencyId, row.OriginalAmountText, row.DueOn,
                row.CreatedAtUtc, row.Direction))
            .ToListAsync(cancellationToken);
        var cancellations = await CancellationsQuery(partyId, accountId)
            .Where(row => row.CancelledAtUtc >= admittedFrom && row.CancelledAtUtc < admittedBefore)
            .OrderBy(row => row.CancelledAtUtc).ThenBy(row => row.OpenItemId)
            .Take(take)
            .ToListAsync(cancellationToken);
        if (moneyEvents.Count + payments.Count + openItems.Count + cancellations.Count > PartyLedgerRules.MaximumEntries)
        {
            return await AfterReadAsync(context, PartyLedgerOutcome.TooManyEntries, cancellationToken);
        }

        var reversalSourceIds = moneyEvents.Where(row => row.Kind == MoneyEventKinds.ManualBankReceiptReversed)
            .Select(row => row.SourceId).ToList();
        var originals = reversalSourceIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await dbContext.ReversalProposals.AsNoTracking()
                .Where(row => row.ReversalSourceId.HasValue && reversalSourceIds.Contains(row.ReversalSourceId.Value))
                .ToDictionaryAsync(row => row.ReversalSourceId!.Value, row => row.OriginalSourceId, cancellationToken);
        var collectionSourceIds = moneyEvents.Where(row => row.Kind == MoneyEventKinds.CustomerInstrumentCollected)
            .Select(row => row.SourceId).ToList();
        var instruments = collectionSourceIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await dbContext.CustomerInstruments.AsNoTracking()
                .Where(row => row.CollectionSourceId.HasValue && collectionSourceIds.Contains(row.CollectionSourceId.Value))
                .ToDictionaryAsync(row => row.CollectionSourceId!.Value, row => row.Id, cancellationToken);

        var amount = await permissions.CheckAsync(context, FinanceAction.ReadPartyLedgerAmount, null, cancellationToken);
        var again = await permissions.CheckAsync(context, FinanceAction.ReadPartyLedger, null, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context))
        {
            return Refused(ReadRefusal(again));
        }

        // A broken stored invariant (an unknown money-event kind or direction, a reversal without its original, an
        // amount that does not parse) withholds the whole ledger as unavailable — never a partial or wrong statement.
        var facts = new List<PartyLedgerFact>(moneyEvents.Count + payments.Count + openItems.Count);
        foreach (var row in moneyEvents)
        {
            switch (row.Kind)
            {
                case MoneyEventKinds.ManualBankReceiptRecorded:
                    facts.Add(Fact(PartyLedgerEntryKind.ManualReceipt, row, null, null));
                    break;
                case MoneyEventKinds.ManualBankReceiptReversed when originals.TryGetValue(row.SourceId, out var original):
                    facts.Add(Fact(PartyLedgerEntryKind.ManualReceiptReversal, row, original, null));
                    break;
                case MoneyEventKinds.CustomerInstrumentCollected:
                    facts.Add(Fact(PartyLedgerEntryKind.CustomerInstrumentCollection, row, null,
                        instruments.TryGetValue(row.SourceId, out var instrument) ? instrument : null));
                    break;
                default:
                    return Refused(PartyLedgerOutcome.Unavailable);
            }
        }

        facts.AddRange(payments.Select(row => new PartyLedgerFact(PartyLedgerEntryKind.ManualBankPayment, row.Id,
            row.OccurredOn, row.RecordedAtUtc, row.CurrencyId, row.AmountText, row.PaymentId, null, null, null, null)));
        foreach (var row in openItems)
        {
            if (OpenItemKind(row.Direction) is not { } kind)
            {
                return Refused(PartyLedgerOutcome.Unavailable);
            }

            facts.Add(new PartyLedgerFact(kind, row.Id, DateOnly.FromDateTime(row.CreatedAtUtc.UtcDateTime),
                row.CreatedAtUtc, row.CurrencyId, row.AmountText, row.Id, null, null, row.DocumentId, row.DueOn));
        }

        foreach (var row in cancellations)
        {
            if (CancellationFactOf(row) is not { } fact)
            {
                return Refused(PartyLedgerOutcome.Unavailable);
            }

            facts.Add(fact);
        }

        IReadOnlyList<PartyLedgerCurrency> currencies;
        try
        {
            currencies = PartyLedgerPolicy.Build(facts, Disclosure(amount));
        }
        catch (InvalidOperationException)
        {
            return Refused(PartyLedgerOutcome.Unavailable);
        }

        return new PartyLedgerResult(PartyLedgerOutcome.Found, new PartyLedgerView(context.CompanyId, partyId, accountId,
            from, to, PartyLedgerCarryForward.NoneOpen018, currencies));
    }

    /// <summary>
    /// F1b / API-04 (FNS-D6): the ledger's own rule over ALL of the account's recorded facts — no range, so no carry-forward
    /// question (OPEN-018) and no row cap — reduced to each currency's debit / credit totals and balance, plus what is still
    /// outstanding on OPEN receivable and payable items (MOD-15). The same two fresh authorities as the ledger; a money
    /// event kind the ledger does not know, or an amount that does not parse, makes the summary unavailable rather than wrong.
    /// </summary>
    public async Task<PartyBalanceResult> SummarizeAuthorizedAsync(FinanceContext context, PartyBalanceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return new PartyBalanceResult(PartyBalanceOutcome.Denied, null);
        }

        var read = await permissions.CheckAsync(context, FinanceAction.ReadPartyLedger, null, cancellationToken);
        if (read != PermissionDecision.Allowed || !SameContext(context))
        {
            return new PartyBalanceResult(BalanceRefusal(read), null);
        }

        if (request.PartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty)
        {
            return new PartyBalanceResult(PartyBalanceOutcome.Invalid, null);
        }

        var partyId = request.PartyId;
        var accountId = request.CompanyAccountId;
        var take = PartyBalanceRules.MaximumFacts + 1;
        var moneyEvents = await dbContext.MoneyEvents.AsNoTracking()
            .Where(row => row.PartyId == partyId && row.CompanyAccountId == accountId)
            .OrderBy(row => row.Id)
            .Take(take)
            .Select(row => new
            {
                row.Id, row.SourceId, row.Kind, row.CurrencyId, row.AmountText, row.OccurredOn, row.RecordedAtUtc,
            })
            .ToListAsync(cancellationToken);
        var payments = await dbContext.OutgoingMoneyEvents.AsNoTracking()
            .Where(row => row.PartyId == partyId && row.CompanyAccountId == accountId)
            .OrderBy(row => row.Id)
            .Take(take)
            .Select(row => new { row.Id, row.CurrencyId, row.AmountText, row.OccurredOn, row.RecordedAtUtc })
            .ToListAsync(cancellationToken);
        var openItems = await dbContext.OpenItems.AsNoTracking()
            .Where(row => row.PartyId == partyId && row.CompanyAccountId == accountId)
            .OrderBy(row => row.Id)
            .Take(take)
            .Select(row => new
            {
                row.Id, row.CurrencyId, row.OriginalAmountText, row.OutstandingAmountText, row.Status, row.Direction,
                row.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);
        var cancellations = await CancellationsQuery(partyId, accountId)
            .OrderBy(row => row.OpenItemId)
            .Take(take)
            .ToListAsync(cancellationToken);
        var tooMany = moneyEvents.Count + payments.Count + openItems.Count + cancellations.Count
            > PartyBalanceRules.MaximumFacts;
        var reversalSourceIds = tooMany
            ? []
            : moneyEvents.Where(row => row.Kind == MoneyEventKinds.ManualBankReceiptReversed)
                .Select(row => row.SourceId).ToList();
        var reversalOriginals = reversalSourceIds.Count == 0
            ? new HashSet<Guid>()
            : (await dbContext.ReversalProposals.AsNoTracking()
                .Where(row => row.ReversalSourceId.HasValue && reversalSourceIds.Contains(row.ReversalSourceId.Value))
                .Select(row => row.ReversalSourceId!.Value)
                .ToListAsync(cancellationToken)).ToHashSet();

        var amount = await permissions.CheckAsync(context, FinanceAction.ReadPartyLedgerAmount, null, cancellationToken);
        var again = await permissions.CheckAsync(context, FinanceAction.ReadPartyLedger, null, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context))
        {
            return new PartyBalanceResult(BalanceRefusal(again), null);
        }

        if (tooMany)
        {
            return new PartyBalanceResult(PartyBalanceOutcome.TooManyFacts, null);
        }

        var facts = new List<PartyLedgerFact>(moneyEvents.Count + payments.Count + openItems.Count);
        foreach (var row in moneyEvents)
        {
            PartyLedgerEntryKind kind;
            switch (row.Kind)
            {
                case MoneyEventKinds.ManualBankReceiptRecorded: kind = PartyLedgerEntryKind.ManualReceipt; break;
                // Like the ledger: a reversal whose original cannot be found is a broken invariant.
                case MoneyEventKinds.ManualBankReceiptReversed when reversalOriginals.Contains(row.SourceId):
                    kind = PartyLedgerEntryKind.ManualReceiptReversal;
                    break;
                case MoneyEventKinds.CustomerInstrumentCollected: kind = PartyLedgerEntryKind.CustomerInstrumentCollection; break;
                default: return new PartyBalanceResult(PartyBalanceOutcome.Unavailable, null);
            }

            facts.Add(new PartyLedgerFact(kind, row.Id, row.OccurredOn, row.RecordedAtUtc, row.CurrencyId, row.AmountText,
                row.Id, null, null, null, null));
        }

        facts.AddRange(payments.Select(row => new PartyLedgerFact(PartyLedgerEntryKind.ManualBankPayment, row.Id,
            row.OccurredOn, row.RecordedAtUtc, row.CurrencyId, row.AmountText, row.Id, null, null, null, null)));
        var receivable = new Dictionary<Guid, FinanceExactDecimalText>();
        var payable = new Dictionary<Guid, FinanceExactDecimalText>();
        foreach (var row in openItems)
        {
            if (row.Direction is not (FinanceOpenItemDirectionCodes.Receivable or FinanceOpenItemDirectionCodes.Payable)
                || !FinanceExactDecimalText.TryParseNonNegative(row.OutstandingAmountText, out var outstanding))
            {
                return new PartyBalanceResult(PartyBalanceOutcome.Unavailable, null);
            }

            facts.Add(new PartyLedgerFact(OpenItemKind(row.Direction)!.Value, row.Id,
                DateOnly.FromDateTime(row.CreatedAtUtc.UtcDateTime), row.CreatedAtUtc, row.CurrencyId, row.OriginalAmountText,
                row.Id, null, null, null, null));
            if (row.Status == FinanceOpenItemStatuses.Open)
            {
                var sums = row.Direction == FinanceOpenItemDirectionCodes.Receivable ? receivable : payable;
                sums[row.CurrencyId] = sums.TryGetValue(row.CurrencyId, out var sum) ? sum.Add(outstanding) : outstanding;
            }
        }

        foreach (var row in cancellations)
        {
            if (CancellationFactOf(row) is not { } fact)
            {
                return new PartyBalanceResult(PartyBalanceOutcome.Unavailable, null);
            }

            facts.Add(fact);
        }

        IReadOnlyList<PartyLedgerCurrency> ledger;
        try
        {
            ledger = PartyLedgerPolicy.Build(facts, Disclosure(amount));
        }
        catch (InvalidOperationException)
        {
            // A stored amount that does not parse is a broken invariant: the summary is withheld, never a partial sum.
            return new PartyBalanceResult(PartyBalanceOutcome.Unavailable, null);
        }

        var disclosure = Disclosure(amount);
        var currencies = ledger.Select(currency => new PartyBalanceCurrency(currency.CurrencyId, currency.DebitTotal,
                currency.CreditTotal, currency.ClosingBalance,
                OpenAmount(receivable, currency.CurrencyId, disclosure), OpenAmount(payable, currency.CurrencyId, disclosure)))
            .ToList();
        return new PartyBalanceResult(PartyBalanceOutcome.Found,
            new PartyBalanceView(context.CompanyId, partyId, accountId, currencies));
    }

    private static AmountView OpenAmount(Dictionary<Guid, FinanceExactDecimalText> sums, Guid currencyId,
        AmountDisclosure disclosure) =>
        disclosure == AmountDisclosure.Visible
            ? new AmountView(AmountDisclosure.Visible, new MoneyText(
                sums.TryGetValue(currencyId, out var sum) ? sum.Text : "0", currencyId))
            : new AmountView(disclosure, null);

    private static PartyBalanceOutcome BalanceRefusal(PermissionDecision decision) =>
        decision == PermissionDecision.Unavailable ? PartyBalanceOutcome.Unavailable : PartyBalanceOutcome.Denied;

    private static PartyLedgerFact Fact(PartyLedgerEntryKind kind, MoneyEventFact row, Guid? reversesSourceId,
        Guid? instrumentId) =>
        new PartyLedgerFact(kind, row.Id, row.OccurredOn, row.RecordedAtUtc, row.CurrencyId, row.AmountText, row.SourceId,
            reversesSourceId, instrumentId, null, null);

    /// <summary>A refusal found by the read is answered only while the actor may still read the ledger.</summary>
    private async Task<PartyLedgerResult> AfterReadAsync(FinanceContext context, PartyLedgerOutcome outcome,
        CancellationToken cancellationToken)
    {
        var again = await permissions.CheckAsync(context, FinanceAction.ReadPartyLedger, null, cancellationToken);
        return again != PermissionDecision.Allowed || !SameContext(context)
            ? Refused(ReadRefusal(again))
            : Refused(outcome);
    }

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    private static PartyLedgerResult Refused(PartyLedgerOutcome outcome) => new PartyLedgerResult(outcome, null);

    private static AmountDisclosure Disclosure(PermissionDecision decision) => decision switch
    {
        PermissionDecision.Allowed => AmountDisclosure.Visible,
        PermissionDecision.Denied => AmountDisclosure.Redacted,
        _ => AmountDisclosure.Unavailable,
    };

    private static PartyLedgerOutcome ReadRefusal(PermissionDecision decision) =>
        decision == PermissionDecision.Unavailable ? PartyLedgerOutcome.Unavailable : PartyLedgerOutcome.Denied;

    private sealed record MoneyEventFact(Guid Id, Guid SourceId, int Kind, Guid CurrencyId, string AmountText,
        DateOnly OccurredOn, DateTimeOffset RecordedAtUtc);

    private sealed record PaymentFact(Guid Id, Guid PaymentId, Guid CurrencyId, string AmountText, DateOnly OccurredOn,
        DateTimeOffset RecordedAtUtc);

    /// <summary>
    /// F2b: the account's invoice-cancellation reversals — one per cancelled open item, with the item's direction and the
    /// cancellation instant. Tenant-filtered like every other read here.
    /// </summary>
    private IQueryable<CancellationFact> CancellationsQuery(Guid partyId, Guid accountId) =>
        from item in dbContext.InvoiceCancellationItems.AsNoTracking()
        join cancellation in dbContext.InvoiceCancellations.AsNoTracking()
            on new { item.CompanyId, Id = item.CancellationId } equals new { cancellation.CompanyId, cancellation.Id }
        join openItem in dbContext.OpenItems.AsNoTracking()
            on new { item.CompanyId, Id = item.OpenItemId } equals new { openItem.CompanyId, openItem.Id }
        where openItem.PartyId == partyId && openItem.CompanyAccountId == accountId
        select new CancellationFact
        {
            OpenItemId = openItem.Id,
            DocumentId = openItem.SourceId,
            CurrencyId = openItem.CurrencyId,
            AmountText = item.ReversedAmountText,
            CancelledAtUtc = cancellation.CancelledAtUtc,
            Direction = openItem.Direction,
        };

    /// <summary>A reversal's row, the reverse side of its open item; an unknown direction is a broken invariant.</summary>
    private static PartyLedgerFact? CancellationFactOf(CancellationFact row) =>
        CancellationKind(row.Direction) is { } kind
            ? new PartyLedgerFact(kind, row.OpenItemId, DateOnly.FromDateTime(row.CancelledAtUtc.UtcDateTime),
                row.CancelledAtUtc, row.CurrencyId, row.AmountText, row.OpenItemId, row.OpenItemId, null, row.DocumentId,
                null)
            : null;

    /// <summary>A member-initialised projection, so the date filter and order still translate to SQL.</summary>
    private sealed class CancellationFact
    {
        public Guid OpenItemId { get; init; }
        public Guid DocumentId { get; init; }
        public Guid CurrencyId { get; init; }
        public string AmountText { get; init; } = string.Empty;
        public DateTimeOffset CancelledAtUtc { get; init; }
        public int Direction { get; init; }
    }

    private static PartyLedgerEntryKind? CancellationKind(int direction) => direction switch
    {
        FinanceOpenItemDirectionCodes.Receivable => PartyLedgerEntryKind.OpenItemCancellation,
        FinanceOpenItemDirectionCodes.Payable => PartyLedgerEntryKind.PayableOpenItemCancellation,
        _ => null,
    };

    private sealed record OpenItemFact(Guid Id, Guid DocumentId, Guid CurrencyId, string AmountText, DateOnly? DueOn,
        DateTimeOffset CreatedAtUtc, int Direction);

    /// <summary>MOD-15: a receivable is the party's debt (debit), a payable the company's debt to it (credit).</summary>
    private static PartyLedgerEntryKind? OpenItemKind(int direction) => direction switch
    {
        FinanceOpenItemDirectionCodes.Receivable => PartyLedgerEntryKind.OpenItem,
        FinanceOpenItemDirectionCodes.Payable => PartyLedgerEntryKind.PayableOpenItem,
        _ => null,
    };
}
