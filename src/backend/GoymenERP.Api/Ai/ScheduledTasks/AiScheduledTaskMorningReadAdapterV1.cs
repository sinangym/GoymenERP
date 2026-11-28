using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.Ai.ScheduledTasks;

/// <summary>Calls the actual current-recipient Work owner. No impersonation, digest creation, counts or model dispatch.
/// A missing digest has no attested slot/version, so it stays unavailable instead of inventing a no-content receipt.</summary>
public sealed class AiScheduledTaskMorningReadAdapterV1(ICompanyContext company, ICurrentUser user,
    IWorkMorningSummaryReadV1? morning = null) : IAiScheduledTaskMorningReadV1
{
    public async Task<AiScheduledTaskMorningReadV1?> ReadCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        var actor = user.UserId;
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(actor)) throw new AiScheduledTaskException(401, "unauthenticated");
        if (companyId == Guid.Empty || !user.CanAccessCompany(companyId)) throw new AiScheduledTaskException(403, "forbidden");
        void Fresh()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!user.IsAuthenticated || user.UserId != actor || !company.IsResolved || company.CompanyId != companyId
                || !user.CanAccessCompany(companyId)) throw new AiScheduledTaskException(403, "forbidden");
        }
        if (morning is null) return null;
        WorkMorningSummaryV1? result;
        try
        {
            Fresh();
            // The Work owner performs its own schedule/member/record rechecks. No awaited callback follows its release.
            result = await morning.ReadAsync(cancellationToken);
        }
        catch (WorkCollaborationException failure) when (failure.Status is 401 or 403 or 503)
        {
            Fresh();
            return failure.Status == 503 ? null : new(companyId, "", default, null, AiScheduledTaskMorningReadOutcomeV1.Denied);
        }
        Fresh();
        if (result is null) return null;
        if (result.CompanyId != companyId || result.DigestId == Guid.Empty || result.SlotUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(result.ScheduleVersion) || result.ScheduleVersion.Length > 256
            || result.ScheduleVersion != result.ScheduleVersion.Trim() || result.ScheduleVersion.Any(char.IsControl)) return null;
        return new(companyId, result.ScheduleVersion, result.SlotUtc, result.DigestId, AiScheduledTaskMorningReadOutcomeV1.Available);
    }
}
