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
/// document date yet), with its official document and due date.</item>
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
                row.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        if (moneyEvents.Count + payments.Count + openItems.Count > PartyLedgerRules.MaximumEntries)
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

        var facts = new List<PartyLedgerFact>(moneyEvents.Count + payments.Count + openItems.Count);
        foreach (var row in moneyEvents)
        {
            facts.Add(row.Kind switch
            {
                MoneyEventKinds.ManualBankReceiptRecorded => Fact(PartyLedgerEntryKind.ManualReceipt, row, null, null),
                MoneyEventKinds.ManualBankReceiptReversed => Fact(PartyLedgerEntryKind.ManualReceiptReversal, row,
                    originals.TryGetValue(row.SourceId, out var original)
                        ? original
                        : throw new InvalidOperationException("Ters kaydın özgün kaynağı bulunamadı; ekstre hesaplanmaz."),
                    null),
                MoneyEventKinds.CustomerInstrumentCollected => Fact(PartyLedgerEntryKind.CustomerInstrumentCollection,
                    row, null, instruments.TryGetValue(row.SourceId, out var instrument) ? instrument : null),
                _ => throw new InvalidOperationException("Tanınmayan para olayı türü; ekstre hesaplanmaz."),
            });
        }

        facts.AddRange(payments.Select(row => new PartyLedgerFact(PartyLedgerEntryKind.ManualBankPayment, row.Id,
            row.OccurredOn, row.RecordedAtUtc, row.CurrencyId, row.AmountText, row.PaymentId, null, null, null, null)));
        facts.AddRange(openItems.Select(row => new PartyLedgerFact(PartyLedgerEntryKind.OpenItem, row.Id,
            DateOnly.FromDateTime(row.CreatedAtUtc.UtcDateTime), row.CreatedAtUtc, row.CurrencyId, row.AmountText, row.Id,
            null, null, row.DocumentId, row.DueOn)));

        return new PartyLedgerResult(PartyLedgerOutcome.Found, new PartyLedgerView(context.CompanyId, partyId, accountId,
            from, to, PartyLedgerCarryForward.NoneOpen018, PartyLedgerPolicy.Build(facts, Disclosure(amount))));
    }

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

    private sealed record OpenItemFact(Guid Id, Guid DocumentId, Guid CurrencyId, string AmountText, DateOnly? DueOn,
        DateTimeOffset CreatedAtUtc);
}
