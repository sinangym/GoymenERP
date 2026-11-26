using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>Read-only evidence over existing admission, allocation and return-credit records. It verifies the
/// stored outstanding projection using Finance's existing exact arithmetic; it neither allocates nor records money.</summary>
public sealed class FinanceSalesInvoiceSettlementReaderV1(
    FinanceDbContext db, ICompanyContext company, ICurrentUser user) : IFinanceSalesInvoiceSettlementPortV1
{
    public const int MaximumInvoiceIds = 128;
    private const int MaximumRows = 4096;
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<ReadResult<FinanceSalesInvoiceSettlementBatchV1>> ReadAsync(
        FinanceContext context, IReadOnlyList<Guid> invoiceIds,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Current(context)) return new(ReadOutcome.Denied, null);
        if (freshConsumerFence is null || invoiceIds is null || invoiceIds.Count > MaximumInvoiceIds
            || invoiceIds.Any(id => id == Guid.Empty) || invoiceIds.Distinct().Count() != invoiceIds.Count)
            return new(ReadOutcome.Unavailable, null);
        var ids = invoiceIds.Order().ToArray();
        try
        {
            await Fence();
            var first = await ReadSnapshot();
            var second = await ReadSnapshot();
            if (JsonSerializer.Serialize(first, Json) != JsonSerializer.Serialize(second, Json))
                return new(ReadOutcome.Unavailable, null);
            var invoices = ids.Select(id => Project(context.CompanyId, id, first)).ToArray();
            await Fence();
            return new(ReadOutcome.Found, new(context.CompanyId, invoices));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return new(ReadOutcome.Unavailable, null); }

        async Task Fence()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Current(context)) throw new UnauthorizedAccessException();
            await freshConsumerFence(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Current(context)) throw new UnauthorizedAccessException();
        }
        async Task<T[]> Rows<T>(IQueryable<T> query)
        {
            await Fence();
            var rows = await query.Take(MaximumRows + 1).ToArrayAsync(cancellationToken);
            await Fence();
            if (rows.Length > MaximumRows) throw new InvalidOperationException("Settlement proof exceeds its bounded read.");
            return rows;
        }
        async Task<Snapshot> ReadSnapshot()
        {
            var companyId = context.CompanyId;
            var items = await Rows(db.OpenItems.AsNoTracking().Where(x => x.CompanyId == companyId
                && ids.Contains(x.SourceId)).OrderBy(x => x.Id));
            var itemIds = items.Select(x => x.Id).ToArray();
            var batches = items.Select(x => x.SourceBatchId).Distinct().ToArray();
            var admissions = await Rows(db.OpenItemSourceBatches.AsNoTracking().Where(x => x.CompanyId == companyId
                && batches.Contains(x.Id)).OrderBy(x => x.Id));
            var allocations = await Rows(db.Allocations.AsNoTracking().Where(x => x.CompanyId == companyId
                && itemIds.Contains(x.OpenItemId)).OrderBy(x => x.Id));
            var sourceIds = allocations.Select(x => x.SourceId).Distinct().ToArray();
            var sources = await Rows(db.ReceiptDrafts.AsNoTracking().Where(x => x.CompanyId == companyId
                && sourceIds.Contains(x.Id)).OrderBy(x => x.Id));
            var events = await Rows(db.MoneyEvents.AsNoTracking().Where(x => x.CompanyId == companyId
                && sourceIds.Contains(x.SourceId)).OrderBy(x => x.Id));
            var reversals = await Rows(db.ReversalProposals.AsNoTracking().Where(x => x.CompanyId == companyId
                && (sourceIds.Contains(x.OriginalSourceId) || sourceIds.Contains(x.ReversalSourceId ?? Guid.Empty)))
                .OrderBy(x => x.Id));
            var credits = await Rows(db.CustomerReturnCredits.AsNoTracking().Where(x => x.CompanyId == companyId
                && ids.Contains(x.OriginalInvoiceId)).OrderBy(x => x.Id));
            var creditIds = credits.Select(x => x.Id).ToArray();
            var reductions = await Rows(db.CustomerReturnCreditItems.AsNoTracking().Where(x => x.CompanyId == companyId
                && (itemIds.Contains(x.OpenItemId) || creditIds.Contains(x.CreditId)))
                .OrderBy(x => x.CreditId).ThenBy(x => x.OpenItemId));
            var cancellations = await Rows(db.InvoiceCancellations.AsNoTracking().Where(x => x.CompanyId == companyId
                && ids.Contains(x.InvoiceId)).OrderBy(x => x.Id));
            return new(items, admissions, allocations, sources, events, reversals, credits, reductions, cancellations);
        }
    }

    private bool Current(FinanceContext context) => company.IsResolved && context.CompanyId != Guid.Empty
        && company.CompanyId == context.CompanyId && user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.UserId)
        && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId);

    internal sealed record Snapshot(FinanceOpenItemRow[] Items, FinanceOpenItemSourceBatchRow[] Admissions,
        FinanceAllocationRow[] Allocations, ReceiptDraftRow[] Sources, MoneyEventRow[] Events,
        ReversalProposalRow[] Reversals, FinanceCustomerReturnCreditRow[] Credits,
        FinanceCustomerReturnCreditItemRow[] Reductions, FinanceInvoiceCancellationRow[] Cancellations);

    internal static FinanceSalesInvoiceSettlementV1 Project(Guid companyId, Guid invoiceId, Snapshot proof)
    {
        var unknown = new FinanceSalesInvoiceSettlementV1(invoiceId, FinanceSalesInvoiceSettlementStatusV1.Unknown, null, null);
        try
        {
            var items = proof.Items.Where(x => x.SourceId == invoiceId).ToArray();
            if (items.Length == 0 || proof.Cancellations.Any(x => x.InvoiceId == invoiceId)) return unknown;
            var first = items[0];
            if (first.SourceBatchId == Guid.Empty || string.IsNullOrWhiteSpace(first.SourceVersion)
                || items.Select(x => x.Id).Distinct().Count() != items.Length
                || items.Select(x => x.SourceLineId).Distinct().Count() != items.Length) return unknown;
            var admission = proof.Admissions.Single(x => x.Id == first.SourceBatchId);
            var receipt = JsonSerializer.Deserialize<FinanceOpenItemAdmissionResult>(admission.ResultJson, Json);
            if (admission.CompanyId != companyId || admission.SourceKind != FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment
                || admission.SourceVersion != first.SourceVersion || admission.SourceChecksum != first.SourceBatchChecksum
                || admission.PayloadHash != admission.SourceChecksum || admission.SourceChecksum.Length != 64
                || receipt is null || receipt.Outcome != CommandOutcome.Succeeded
                || receipt.OpenItemIds is null || !receipt.OpenItemIds.Order().SequenceEqual(items.Select(x => x.Id).Order())) return unknown;
            var zero = new FinanceExactDecimalText();
            var reduced = items.ToDictionary(x => x.Id, _ => zero);
            foreach (var credit in proof.Credits.Where(x => x.OriginalInvoiceId == invoiceId))
            {
                if (credit.CompanyId != companyId || credit.SourceKind != FinanceCustomerReturnCreditRules.SourceKind
                    || credit.OriginalInvoiceVersion != first.SourceVersion || credit.PartyId != first.PartyId
                    || credit.CompanyAccountId != first.CompanyAccountId || credit.CurrencyId != first.CurrencyId
                    || !FinanceExactDecimalText.TryParsePositive(credit.AmountText, out var total)
                    || !FinanceExactDecimalText.TryParseNonNegative(credit.AppliedAmountText, out var applied)
                    || !FinanceExactDecimalText.TryParseNonNegative(credit.AdvanceAmountText, out var advance)
                    || applied.Add(advance).CompareTo(total) != 0) return unknown;
                var creditItems = proof.Reductions.Where(x => x.CreditId == credit.Id).ToArray();
                var result = JsonSerializer.Deserialize<FinanceInvoiceAcceptanceResultV2>(credit.ResultJson, Json);
                if (result is null || result.Outcome != CommandOutcome.Succeeded || result.AcceptanceId != credit.Id
                    || result.Reductions is null || result.Reductions.Count != creditItems.Length) return unknown;
                var appliedProof = zero;
                foreach (var reduction in creditItems)
                {
                    if (reduction.CompanyId != companyId || !reduced.ContainsKey(reduction.OpenItemId)
                        || !FinanceExactDecimalText.TryParsePositive(reduction.ReducedAmountText, out var amount)
                        || !result.Reductions.Any(x => x.OpenItemId == reduction.OpenItemId
                            && FinanceExactDecimalText.Equal(x.Amount, reduction.ReducedAmountText))) return unknown;
                    reduced[reduction.OpenItemId] = reduced[reduction.OpenItemId].Add(amount);
                    appliedProof = appliedProof.Add(amount);
                }
                if (appliedProof.CompareTo(applied) != 0) return unknown;
            }
            if (proof.Reductions.Any(x => reduced.ContainsKey(x.OpenItemId)
                && !proof.Credits.Any(c => c.Id == x.CreditId && c.OriginalInvoiceId == invoiceId))) return unknown;
            var settled = true;
            foreach (var item in items)
            {
                if (item.CompanyId != companyId || item.Id == Guid.Empty || item.SourceLineId == Guid.Empty
                    || item.SourceKind != FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment
                    || item.Direction != FinanceOpenItemDirectionCodes.Receivable || item.SourceBatchId != first.SourceBatchId
                    || item.SourceVersion != first.SourceVersion || item.SourceBatchVersion != first.SourceVersion
                    || item.SourceBatchChecksum != admission.SourceChecksum || item.PartyId == Guid.Empty
                    || item.CompanyAccountId == Guid.Empty || item.CurrencyId == Guid.Empty || item.Version < 1
                    || item.PartyId != first.PartyId || item.CompanyAccountId != first.CompanyAccountId
                    || item.CurrencyId != first.CurrencyId || item.Status is not (FinanceOpenItemStatuses.Open or FinanceOpenItemStatuses.Closed)
                    || !FinanceExactDecimalText.TryParsePositive(item.OriginalAmountText, out var original)
                    || !FinanceExactDecimalText.TryParseNonNegative(item.OutstandingAmountText, out var outstanding)
                    || outstanding.CompareTo(original) > 0 || (item.Status == FinanceOpenItemStatuses.Closed) != outstanding.IsZero)
                    return unknown;
                var applied = reduced[item.Id];
                foreach (var allocation in proof.Allocations.Where(x => x.OpenItemId == item.Id))
                {
                    if (allocation.CompanyId != companyId || allocation.CurrencyId != item.CurrencyId
                        || allocation.Id == Guid.Empty || allocation.Version < 1
                        || allocation.Status is not (FinanceAllocationStatuses.Active or FinanceAllocationStatuses.Released)) return unknown;
                    if (allocation.Status == FinanceAllocationStatuses.Released) continue;
                    var source = proof.Sources.Single(x => x.Id == allocation.SourceId);
                    var money = proof.Events.Single(x => x.SourceId == source.Id);
                    if (source.CompanyId != companyId || source.Stage != ReceiptStages.Recorded
                        || source.RecordedAtUtc is null || string.IsNullOrWhiteSpace(source.RecordedBy)
                        || source.CurrencyId != item.CurrencyId || allocation.ReleasedAtUtc is not null
                        || allocation.ReleasedBy is not null || allocation.ReleaseReason is not null
                        || money.CompanyId != companyId || money.Kind != MoneyEventKinds.ManualBankReceiptRecorded
                        || money.CurrencyId != source.CurrencyId || money.CompanyAccountId != source.CompanyAccountId
                        || money.PartyId != source.PartyId || money.RecordedAtUtc != source.RecordedAtUtc
                        || money.RecordedBy != source.RecordedBy || money.TreasuryAccountId != source.TreasuryAccountId
                        || !FinanceExactDecimalText.Equal(money.AmountText, source.AmountText)
                        || proof.Reversals.Any(x => x.Status == ReversalProposalStatuses.Approved
                            && (x.OriginalSourceId == source.Id || x.ReversalSourceId == source.Id))
                        || !FinanceExactDecimalText.TryParsePositive(allocation.AmountText, out var amount)) return unknown;
                    applied = applied.Add(amount);
                }
                if (outstanding.Add(applied).CompareTo(original) != 0) return unknown;
                settled &= outstanding.IsZero;
            }
            return new(invoiceId, settled ? FinanceSalesInvoiceSettlementStatusV1.Settled : FinanceSalesInvoiceSettlementStatusV1.Open,
                first.SourceBatchId, first.SourceVersion);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or FormatException)
        { return unknown; }
    }
}
