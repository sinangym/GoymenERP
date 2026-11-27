using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore : IAccountingNormalJournalDraftDecisionStoreV1
{
    public async Task<NormalJournalDraftRecordV1?> ReadNormalDraftDecisionAsync(
        Guid draftId, Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshReadFence);
        RequireResolvedCompany();
        await freshReadFence(cancellationToken);
        var row = await dbContext.NormalJournalDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == draftId, cancellationToken);
        await freshReadFence(cancellationToken);
        if (row is null) return null;
        var lines = await dbContext.NormalJournalDraftLines.AsNoTracking().Where(x => x.DraftId == draftId)
            .OrderBy(x => x.LineNumber).ToListAsync(cancellationToken);
        await freshReadFence(cancellationToken);
        return RestoreNormalDraft(row, lines);
    }
}
