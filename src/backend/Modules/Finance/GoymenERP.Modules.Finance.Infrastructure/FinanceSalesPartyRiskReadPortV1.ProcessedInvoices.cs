using System.Data;
using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

internal sealed partial class FinanceSalesPartyRiskReadPortV1
{
    internal const string ProcessedInvoiceUnreadable = "finance_processed_invoice_unreadable";
    private const int MaximumProcessedItemRows = 16384; // Response bound, never an installment/amount policy.

    public async Task<SalesPartyRiskReadAnswerV2> ReadPartyRiskAsync(SalesPartyRiskReadQueryV2 query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var companyId = companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty;
        if (companyId == Guid.Empty) throw new InvalidOperationException("Risk requires a server company.");
        var partyId = query?.PartyId ?? Guid.Empty; var accountId = query?.CompanyAccountId ?? Guid.Empty;
        SalesPartyRiskReadAnswerV2 Failure() => new(companyId, partyId, accountId, Unavailable(ProcessedInvoiceUnreadable),
            new(SalesPartyRiskFigureStateV1.Unavailable, CreditLimitSource, [], null, ProcessedInvoiceUnreadable),
            new(SalesPartyRiskFigureStateV1.Unavailable, CollateralSource, [], null, ProcessedInvoiceUnreadable),
            new(SalesPartyRiskProcessedSetStateV2.Unavailable, OpenItemsSource, [], ProcessedInvoiceUnreadable));
        if (!SalesPartyRiskReadQueryV2.IsValid(query) || dbContext.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null || dbContext.ChangeTracker.HasChanges()) return Failure();
        try
        {
            SalesPartyRiskFigureV1 receivables;
            IReadOnlyList<Guid>? processed;
            // SERIALIZABLE is supported without a database setting/migration. The existing V1 query/calculation and the
            // membership proof observe one owner snapshot; no second settlement read or revaluation is performed.
            await using (var snapshot = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
            {
                Fence();
                receivables = await OpenReceivablesAsync(companyId, partyId, accountId, cancellationToken); Fence();
                processed = await ProcessedInvoicesAsync(companyId, query!, cancellationToken); Fence();
                await snapshot.CommitAsync(cancellationToken); Fence();
            }
            var credit = await CreditLimitAsync(companyId, partyId, accountId, cancellationToken); Fence();
            var collateral = await CollateralAsync(companyId, partyId, accountId, cancellationToken); Fence();
            var available = processed is not null && receivables.State == SalesPartyRiskFigureStateV1.Available;
            return new(companyId, partyId, accountId, receivables, credit, collateral,
                new(available ? SalesPartyRiskProcessedSetStateV2.Available : SalesPartyRiskProcessedSetStateV2.Unavailable,
                    OpenItemsSource, available ? processed! : [], available ? null : ProcessedInvoiceUnreadable));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Failure(); }
        void Fence()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!companyContext.IsResolved || companyContext.CompanyId != companyId) throw new InvalidOperationException("Risk company changed.");
        }
    }

    private async Task<IReadOnlyList<Guid>?> ProcessedInvoicesAsync(Guid companyId, SalesPartyRiskReadQueryV2 query, CancellationToken token)
    {
        if (query.SalesInvoiceIds.Count == 0) return [];
        var ids = query.SalesInvoiceIds.ToArray();
        const string kind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment;
        var items = await dbContext.OpenItems.AsNoTracking().Where(x => x.CompanyId == companyId && x.SourceKind == kind && ids.Contains(x.SourceId))
            .Take(MaximumProcessedItemRows + 1).ToArrayAsync(token);
        if (items.Length > MaximumProcessedItemRows) return null;
        var batchIds = items.Select(x => x.SourceBatchId).Distinct().ToArray();
        var batches = await dbContext.OpenItemSourceBatches.AsNoTracking().Where(x => x.CompanyId == companyId && batchIds.Contains(x.Id)).ToArrayAsync(token);
        var cancellations = await dbContext.InvoiceCancellations.AsNoTracking().Where(x => x.CompanyId == companyId && x.SourceKind == kind && ids.Contains(x.InvoiceId)).ToArrayAsync(token);
        var cancellationIds = cancellations.Select(x => x.Id).ToArray();
        var reversed = await dbContext.InvoiceCancellationItems.AsNoTracking().Where(x => x.CompanyId == companyId && cancellationIds.Contains(x.CancellationId))
            .Take(MaximumProcessedItemRows + 1).ToArrayAsync(token);
        if (reversed.Length > MaximumProcessedItemRows) return null;
        var processed = new List<Guid>();
        var json = FinanceJson.CreateOptions();
        foreach (var invoiceId in ids)
        {
            var rows = items.Where(x => x.SourceId == invoiceId).ToArray();
            var cancellation = cancellations.SingleOrDefault(x => x.InvoiceId == invoiceId);
            if (rows.Length == 0) { if (cancellation is not null) return null; continue; } // Not admitted, never a fabricated zero.
            var first = rows[0];
            var batch = batches.SingleOrDefault(x => x.Id == first.SourceBatchId);
            var receipt = batch is null ? null : JsonSerializer.Deserialize<FinanceOpenItemAdmissionResult>(batch.ResultJson, json);
            if (first.SourceBatchId == Guid.Empty || string.IsNullOrWhiteSpace(first.SourceVersion)
                || batch is null || batch.SourceKind != kind || batch.SourceVersion != first.SourceVersion
                || batch.SourceChecksum != first.SourceBatchChecksum || batch.SourceChecksum.Length != 64 || batch.PayloadHash != batch.SourceChecksum
                || receipt?.Outcome != CommandOutcome.Succeeded || receipt.OpenItemIds is null
                || !receipt.OpenItemIds.Order().SequenceEqual(rows.Select(x => x.Id).Order())
                || rows.Select(x => x.SourceLineId).Distinct().Count() != rows.Length
                || rows.Any(x => x.PartyId != query.PartyId || x.CompanyAccountId != query.CompanyAccountId || x.SourceBatchId != first.SourceBatchId
                    || x.SourceVersion != first.SourceVersion || x.SourceBatchVersion != first.SourceVersion || x.SourceBatchChecksum != batch.SourceChecksum
                    || x.Direction != FinanceOpenItemDirectionCodes.Receivable || x.Id == Guid.Empty || x.SourceLineId == Guid.Empty
                    || x.CurrencyId == Guid.Empty || x.CurrencyId != first.CurrencyId || x.Version < 1
                    || !FinanceExactDecimalText.TryParsePositive(x.OriginalAmountText, out var original)
                    || !FinanceExactDecimalText.TryParseNonNegative(x.OutstandingAmountText, out var outstanding) || outstanding.CompareTo(original) > 0)) return null;
            if (cancellation is null)
            {
                if (rows.Any(x => x.Status is not (FinanceOpenItemStatuses.Open or FinanceOpenItemStatuses.Closed)
                    || (x.Status == FinanceOpenItemStatuses.Closed) != (FinanceExactDecimalText.TryParseNonNegative(x.OutstandingAmountText, out var amount) && amount.IsZero))) return null;
                // Closed through settlement/return credit still means processed (FNS-D53 concerns official reversal only).
                processed.Add(invoiceId); continue;
            }
            var cancellationReceipt = JsonSerializer.Deserialize<FinanceInvoiceOpenItemCancellationResultV1>(cancellation.ResultJson, json);
            var reversals = reversed.Where(x => x.CancellationId == cancellation.Id).ToArray();
            if (cancellation.Id == Guid.Empty || cancellation.InvoiceVersion != first.SourceVersion
                || cancellationReceipt?.Outcome != CommandOutcome.Succeeded || cancellationReceipt.OpenItemIds is null
                || !cancellationReceipt.OpenItemIds.Order().SequenceEqual(rows.Select(x => x.Id).Order())
                || !reversals.Select(x => x.OpenItemId).Order().SequenceEqual(rows.Select(x => x.Id).Order())
                || rows.Any(x => x.Status != FinanceOpenItemStatuses.Cancelled || x.OutstandingAmountText != "0"
                    || !reversals.Any(r => r.OpenItemId == x.Id && FinanceExactDecimalText.Equal(r.ReversedAmountText, x.OriginalAmountText)))) return null;
        }
        return processed;
    }
}
