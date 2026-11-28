using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

internal sealed partial class SalesRemainderChangeWorkInboxSourceV1
{
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token) => ReadSelectedAsync(context, itemId, null, token);
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 context, Guid itemId, string expectedProposalVersion, CancellationToken token) => ReadSelectedAsync(context, itemId, expectedProposalVersion, token);
    private async Task<WorkApprovalDecisionSnapshotV1?> ReadSelectedAsync(WorkInboxSourceContextV1 context, Guid id, string? selected, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(context, token);
        if (id == Guid.Empty) return null;
        if (owner is null) throw new InvalidOperationException("Satır kalanı karar sahibi kayıtlı değil.");
        if (selected is not null)
        {
            if (!SalesRequestDecisionBindingV1.TryVersion(selected, out var n) || n == long.MaxValue) throw new InvalidOperationException("Özgün kalan karar sürümü geçersiz.");
            var original = await owner.ReadRemainderOriginalVersionAsync(id, selected, ct => Fence(context, ct), token);
            token.ThrowIfCancellationRequested(); if (!SameContext(context)) throw new UnauthorizedAccessException();
            if (original is { State: SalesRemainderDecisionReadStateV1.Denied, Receipt: null }) throw new UnauthorizedAccessException();
            if (original is { State: SalesRemainderDecisionReadStateV1.Found, Receipt: { } r, CurrentVersion: { } current })
            {
                if (r.CompanyId != context.CompanyId || r.DraftId != id || r.ActorId != context.UserId || r.ExpectedVersion != n
                    || SalesRemainderChangeDecisionBindingV1.Item(r, current) is not { } item) throw new InvalidOperationException("Özgün kalan kararı doğrulanamadı.");
                return new(context.CompanyId, item, selected, false) { SalesRemainderChangeProof = SalesRemainderChangeDecisionBindingV1.Proof(r, current) };
            }
            if (original is not { State: SalesRemainderDecisionReadStateV1.NotFound, Receipt: null }) throw new InvalidOperationException("Özgün kalan karar makbuzu kullanılamıyor.");
            if (original.CurrentVersion is null) return null;
            if (original.CurrentVersion != n) throw new InvalidOperationException("Geçmiş kalan kararı özgün makbuz taşımıyor.");
        }
        var answer = await owner.ReadRemainderDecisionAsync(id, ct => Fence(context, ct), token);
        token.ThrowIfCancellationRequested(); if (!SameContext(context)) throw new UnauthorizedAccessException();
        if (answer is { State: SalesRemainderDecisionReadStateV1.Denied, Detail: null }) throw new UnauthorizedAccessException();
        if (answer is { State: SalesRemainderDecisionReadStateV1.NotFound, Detail: null }) return null;
        if (answer is not { State: SalesRemainderDecisionReadStateV1.Found, Detail: { } d } || d.CompanyId != context.CompanyId || d.DraftId != id
            || selected is not null && d.Version != selected || SalesRemainderChangeDecisionBindingV1.Item(d) is not { } currentItem)
            throw new InvalidOperationException("Kalan karar olguları doğrulanamadı.");
        var proof = SalesRemainderChangeDecisionBindingV1.Proof(d);
        return new(context.CompanyId, currentItem, proof.SelectedExpectedVersion.ToString(CultureInfo.InvariantCulture), d.Change.State == "Pending") { SalesRemainderChangeProof = proof };
    }
}
