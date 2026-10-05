using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.WorkRuntime;

internal sealed class NativeApprovalReadQueryV1(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlationContext,
    INativeApprovalReadAdapterV1 adapter)
{
    public async Task<ReadResult<NativeApprovalReadPageV1>> ListAsync(
        string? cursor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCapture(out var context, out var refusal))
        {
            return new(refusal, null);
        }

        var result = await adapter.ListAsync(context, cursor, cancellationToken);
        return SameContext(context) ? result : new(ReadOutcome.Denied, null);
    }

    public async Task<ReadResult<NativeApprovalReadDetailV1>> GetAsync(
        string kind,
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(kind, NativeApprovalKindsV1.FinanceOfficialInvoiceImport, StringComparison.Ordinal)
            || approvalId == Guid.Empty)
        {
            return new(ReadOutcome.NotFound, null);
        }
        if (!TryCapture(out var context, out var refusal))
        {
            return new(refusal, null);
        }

        var result = await adapter.GetAsync(context, kind, approvalId, cancellationToken);
        return SameContext(context) ? result : new(ReadOutcome.Denied, null);
    }

    private bool TryCapture(out FinanceContext context, out ReadOutcome refusal)
    {
        context = null!;
        if (!companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty
            || !currentUser.IsAuthenticated
            || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            refusal = ReadOutcome.Denied;
            return false;
        }
        if (!correlationContext.IsResolved || string.IsNullOrWhiteSpace(correlationContext.CorrelationId))
        {
            refusal = ReadOutcome.Unavailable;
            return false;
        }

        context = new FinanceContext(
            companyContext.CompanyId,
            currentUser.UserId,
            correlationContext.CorrelationId,
            correlationContext.CausationId);
        refusal = default;
        return true;
    }

    private bool SameContext(FinanceContext captured) =>
        companyContext.IsResolved
        && companyContext.CompanyId == captured.CompanyId
        && captured.CompanyId != Guid.Empty
        && currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, captured.ActorId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(captured.CompanyId)
        && correlationContext.IsResolved
        && string.Equals(correlationContext.CorrelationId, captured.CorrelationId, StringComparison.Ordinal)
        && string.Equals(correlationContext.CausationId, captured.CausationId, StringComparison.Ordinal);
}
