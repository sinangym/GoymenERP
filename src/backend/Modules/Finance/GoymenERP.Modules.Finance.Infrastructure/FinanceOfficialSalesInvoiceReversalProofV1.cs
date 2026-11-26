using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>Pure proof of the existing native official Sales invoice reversal. No write, valuation, settlement or
/// alias is created. It requires the admission, native cancellation receipt, every original reversal and the current
/// owner projections to agree. It deliberately does not reuse Settled to represent a cancelled invoice.</summary>
internal static class FinanceOfficialSalesInvoiceReversalProofV1
{
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    internal static bool IsComplete(Guid company, Guid invoiceId, FinanceOpenItemRow[] items,
        FinanceOpenItemSourceBatchRow admission, FinanceSalesInvoiceSettlementReaderV1.Snapshot proof)
    {
        if (company == Guid.Empty || invoiceId == Guid.Empty || items.Length == 0 || proof.CancellationItems is null) return false;
        var cancellation = proof.Cancellations.SingleOrDefault(x => x.InvoiceId == invoiceId);
        if (cancellation is null || cancellation.CompanyId != company || cancellation.SourceKind != admission.SourceKind
            || cancellation.SourceKind != FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment
            || cancellation.InvoiceVersion != admission.SourceVersion || !Hash(cancellation.IdempotencyKeyHash)
            || !Hash(cancellation.PayloadHash) || !Text(cancellation.ActorId) || !Text(cancellation.CorrelationId)
            || cancellation.CancelledAtUtc == default || cancellation.CancelledAtUtc < admission.AdmittedAtUtc
            || admission.CompanyId != company || !Text(admission.ActorId) || !Text(admission.CorrelationId)
            || admission.AdmittedAtUtc == default || !Hash(admission.IdempotencyKeyHash) || !Hash(admission.SourceChecksum)) return false;
        var native = new FinanceInvoiceOpenItemCancellationV1(cancellation.Id, FinanceInvoiceProducerV1.Sales,
            invoiceId, cancellation.InvoiceVersion, cancellation.Reason);
        if (FinanceInvoiceOpenItemCancellationService.Validate(native, out var kind).Count != 0 || kind != cancellation.SourceKind) return false;
        // Confirmation is represented by the native request hash, not a new policy/flag. Both existing wire variants
        // are understood; releases occurring in this cancellation require its already-supported confirmed variant.
        var confirmed = cancellation.PayloadHash == FinanceInvoiceOpenItemCancellationService.PayloadHash(native with { ReleaseActiveAllocations = true });
        if (!confirmed && cancellation.PayloadHash != FinanceInvoiceOpenItemCancellationService.PayloadHash(native)) return false;
        var receipt = JsonSerializer.Deserialize<FinanceInvoiceOpenItemCancellationResultV1>(cancellation.ResultJson, Json);
        var admitted = JsonSerializer.Deserialize<FinanceOpenItemAdmissionResult>(admission.ResultJson, Json);
        var ids = items.Select(x => x.Id).Order().ToArray();
        if (receipt is null || receipt.Outcome != CommandOutcome.Succeeded || receipt.IsReplay
            || receipt.OriginalCorrelationId != cancellation.CorrelationId || receipt.Issues is null || receipt.Issues.Count != 0
            || receipt.OpenItemIds is null || !receipt.OpenItemIds.Order().SequenceEqual(ids)
            || admitted is null || admitted.OriginalCorrelationId != admission.CorrelationId
            || admitted.Issues is null || admitted.Issues.Count != 0) return false;
        var reversed = proof.CancellationItems.Where(x => x.CancellationId == cancellation.Id || ids.Contains(x.OpenItemId)).ToArray();
        if (reversed.Length != items.Length || !reversed.Select(x => x.OpenItemId).Order().SequenceEqual(ids)
            || proof.Credits.Any(x => x.OriginalInvoiceId == invoiceId)
            || proof.Reductions.Any(x => ids.Contains(x.OpenItemId))) return false;
        var first = items[0];
        foreach (var item in items)
        {
            if (item.CompanyId != company || item.Id == Guid.Empty || item.SourceLineId == Guid.Empty || item.SourceId != invoiceId
                || item.SourceKind != cancellation.SourceKind || item.Direction != FinanceOpenItemDirectionCodes.Receivable
                || item.SourceBatchId != admission.Id || item.SourceVersion != admission.SourceVersion || item.SourceBatchVersion != admission.SourceVersion
                || item.SourceBatchChecksum != admission.SourceChecksum || item.PartyId == Guid.Empty || item.CompanyAccountId == Guid.Empty
                || item.CurrencyId == Guid.Empty || item.PartyId != first.PartyId || item.CompanyAccountId != first.CompanyAccountId
                || item.CurrencyId != first.CurrencyId || item.Version < 2 || item.Status != FinanceOpenItemStatuses.Cancelled
                || item.OutstandingAmountText != "0" || !FinanceExactDecimalText.TryParsePositive(item.OriginalAmountText, out _)
                || item.UpdatedBy != cancellation.ActorId || item.UpdatedAtUtc != cancellation.CancelledAtUtc) return false;
            var reversal = reversed.Single(x => x.OpenItemId == item.Id);
            if (reversal.CompanyId != company || reversal.CancellationId != cancellation.Id
                || !FinanceExactDecimalText.Equal(reversal.ReversedAmountText, item.OriginalAmountText)) return false;
            var assignments = proof.Assignments.Where(x => x.Kind == FinanceBalanceAssignmentKinds.OpenItem && x.SourceId == item.Id).ToArray();
            if (assignments.Any(x => x.CompanyId != company)
                || new FinanceAllocationPositions(item.PartyId, item.CompanyAccountId, item.CurrencyId, item.OutstandingAmountText, assignments)
                    .All().Any(x => !FinanceExactDecimalText.TryParseNonNegative(x.AvailableAmount, out var value) || !value.IsZero)) return false;
            foreach (var allocation in proof.Allocations.Where(x => x.OpenItemId == item.Id))
            {
                if (allocation.CompanyId != company || allocation.Id == Guid.Empty || allocation.SourceId == Guid.Empty
                    || allocation.CurrencyId != item.CurrencyId || allocation.Status != FinanceAllocationStatuses.Released
                    || allocation.Version < 2 || !Text(allocation.ReleasedBy) || !Text(allocation.ReleaseReason)
                    || allocation.ReleasedAtUtc is not { } releasedAt || releasedAt > cancellation.CancelledAtUtc
                    || releasedAt < allocation.CreatedAtUtc || !FinanceExactDecimalText.TryParsePositive(allocation.AmountText, out _)
                    || releasedAt == cancellation.CancelledAtUtc && (!confirmed || allocation.ReleasedBy != cancellation.ActorId
                        || allocation.ReleaseReason != cancellation.Reason)
                    || !ReleasedSourceIsConsistent(company, allocation.SourceId, item.CurrencyId, proof)) return false;
            }
        }
        return true;
    }

    private static bool ReleasedSourceIsConsistent(Guid company, Guid sourceId, Guid currency,
        FinanceSalesInvoiceSettlementReaderV1.Snapshot proof)
    {
        var source = proof.Sources.Single(x => x.Id == sourceId);
        var state = proof.States.Single(x => x.SourceId == sourceId);
        var money = proof.Events.Single(x => x.SourceId == sourceId);
        if (source.CompanyId != company || source.Stage != ReceiptStages.Recorded || source.RecordedAtUtc is null
            || !Text(source.RecordedBy) || source.CurrencyId != currency || source.PartyId == Guid.Empty || source.CompanyAccountId == Guid.Empty
            || state.CompanyId != company || state.CurrencyId != currency || state.Version < 1
            || !FinanceExactDecimalText.Equal(state.OriginalAmountText, source.AmountText)
            || !FinanceExactDecimalText.TryParsePositive(source.AmountText, out var original)
            || !FinanceExactDecimalText.TryParseNonNegative(state.UnallocatedAmountText, out var available)
            || money.CompanyId != company || money.Kind != MoneyEventKinds.ManualBankReceiptRecorded
            || money.PartyId != source.PartyId || money.CompanyAccountId != source.CompanyAccountId || money.CurrencyId != currency
            || money.RecordedAtUtc != source.RecordedAtUtc || money.RecordedBy != source.RecordedBy
            || money.TreasuryAccountId != source.TreasuryAccountId || !FinanceExactDecimalText.Equal(money.AmountText, source.AmountText)) return false;
        var allocated = new FinanceExactDecimalText();
        foreach (var sibling in proof.Allocations.Where(x => x.SourceId == sourceId))
        {
            if (sibling.CompanyId != company || sibling.CurrencyId != currency
                || sibling.Status is not (FinanceAllocationStatuses.Active or FinanceAllocationStatuses.Released)) return false;
            if (sibling.Status == FinanceAllocationStatuses.Released) continue;
            if (!FinanceExactDecimalText.TryParsePositive(sibling.AmountText, out var amount)) return false;
            allocated = allocated.Add(amount);
        }
        return available.Add(allocated).CompareTo(original) == 0;
    }

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value);
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(x => char.IsAsciiDigit(x) || x is >= 'a' and <= 'f');
}
