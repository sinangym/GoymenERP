using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceOfficialSalesInvoiceServiceV1 : IFinanceOfficialSalesInvoiceDecisionReadV1
{
    public async Task<ReadResult<FinanceOfficialSalesInvoiceViewV1>> ReadDecisionAsync(FinanceContext context,
        Guid invoiceId, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(freshConsumerFence);
        cancellationToken.ThrowIfCancellationRequested();
        if (await Fence() is { } before) return before;
        if (invoiceId == Guid.Empty) return new(ReadOutcome.NotFound, null);
        // Reuse the actual owner projection, source-difference facts and tax warning; no second calculator.
        var answer = await GetAsync(context, invoiceId, cancellationToken);
        if (await Fence() is { } after) return after;
        if (answer.Outcome != ReadOutcome.Found || answer.Resource is not { } view) return answer;
        if (view.InvoiceId != invoiceId || view.CompanyId != context.CompanyId
            || string.IsNullOrWhiteSpace(view.RecordedBy)) return new(ReadOutcome.Unavailable, null);
        return view.RecordedBy == context.ActorId ? new(ReadOutcome.Denied, null) : answer;

        async Task<ReadResult<FinanceOfficialSalesInvoiceViewV1>?> Fence()
        {
            await freshConsumerFence(cancellationToken);
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            var refusal = await FenceAsync(context, FinanceOfficialSalesInvoiceAuthorityActionV1.Approve, invoiceId, cancellationToken);
            await freshConsumerFence(cancellationToken);
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            return refusal is null ? null : new(refusal.Outcome == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }
    }
}
