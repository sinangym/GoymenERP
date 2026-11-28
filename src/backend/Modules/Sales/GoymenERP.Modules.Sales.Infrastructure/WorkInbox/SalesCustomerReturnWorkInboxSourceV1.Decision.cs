using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

internal sealed partial class SalesCustomerReturnWorkInboxSourceV1
{
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid id, CancellationToken token) => ReadSelected(context, id, null, token);
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 context, Guid id, string expectedProposalVersion, CancellationToken token) => ReadSelected(context, id, expectedProposalVersion, token);
    private async Task<WorkApprovalDecisionSnapshotV1?> ReadSelected(WorkInboxSourceContextV1 context, Guid id, string? selected, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(context, token);
        if (id == Guid.Empty) return null;
        if (owner is null) throw new InvalidOperationException("İade kararı sahibi kayıtlı değil.");
        if (selected is not null)
        {
            if (!SalesRequestDecisionBindingV1.TryVersion(selected, out var n) || n == long.MaxValue) throw new InvalidOperationException("İade karar sürümü geçersiz.");
            var original = await owner.ReadCustomerReturnOriginalVersionAsync(id, selected, ct => Fence(context, ct), token);
            token.ThrowIfCancellationRequested(); if (!SameContext(context)) throw new UnauthorizedAccessException();
            if (original is { State: SalesCustomerReturnDecisionReadStateV1.Denied, Receipt: null }) throw new UnauthorizedAccessException();
            if (original is { State: SalesCustomerReturnDecisionReadStateV1.Found, Receipt: { } receipt, CurrentVersion: { } current })
            {
                if (receipt.CompanyId != context.CompanyId || receipt.ReturnId != id || receipt.ActorId != context.UserId || receipt.ExpectedVersion != n
                    || SalesCustomerReturnDecisionBindingV1.Item(receipt, current) is not { } item) throw new InvalidOperationException("Özgün iade kararı doğrulanamadı.");
                return new(context.CompanyId, item, selected, false) { SalesCustomerReturnProof = SalesCustomerReturnDecisionBindingV1.Proof(receipt, current) };
            }
            if (original is not { State: SalesCustomerReturnDecisionReadStateV1.NotFound, Receipt: null }) throw new InvalidOperationException("Özgün iade makbuzu kullanılamıyor.");
            if (original.CurrentVersion is null) return null;
            if (original.CurrentVersion != n) throw new InvalidOperationException("Geçmiş iade kararı makbuz taşımıyor.");
        }
        var answer = await owner.ReadCustomerReturnDecisionAsync(id, ct => Fence(context, ct), token);
        token.ThrowIfCancellationRequested(); if (!SameContext(context)) throw new UnauthorizedAccessException();
        if (answer is { State: SalesCustomerReturnDecisionReadStateV1.Denied, Detail: null }) throw new UnauthorizedAccessException();
        if (answer is { State: SalesCustomerReturnDecisionReadStateV1.NotFound, Detail: null }) return null;
        if (answer is not { State: SalesCustomerReturnDecisionReadStateV1.Found, Detail: { } d } || d.CompanyId != context.CompanyId || d.CustomerReturn.Id != id
            || selected is not null && selected != d.CustomerReturn.Version || SalesCustomerReturnDecisionBindingV1.Item(d) is not { } currentItem)
            throw new InvalidOperationException("İade karar olguları doğrulanamadı.");
        var p = SalesCustomerReturnDecisionBindingV1.Proof(d);
        return new(context.CompanyId, currentItem, p.SelectedExpectedVersion.ToString(CultureInfo.InvariantCulture), d.CustomerReturn.State == "PendingApproval") { SalesCustomerReturnProof = p };
    }
    private async Task Fence(WorkInboxSourceContextV1 context, CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (!await DecisionAllowedAsync(context, token)) throw new UnauthorizedAccessException(); token.ThrowIfCancellationRequested(); if (!SameContext(context)) throw new UnauthorizedAccessException(); }
}
