using System.Globalization;
using GoymenERP.Api.Contracts;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>
/// FIRST_USE / W1-CARI: delegates need tracking to the existing Work task owner. It never creates or activates a Party/account.
/// TasksAssign is required; the recipient must currently hold company-wide CounterpartyPartiesCreate. These additional
/// recipient checks are pre/post admission checks, NOT a participant in the Work owner's transaction. Once dispatch starts,
/// an authority denial or an unusable result is unknown (503): retain the exact key/body, never claim rollback or mint a key.
/// </summary>
public sealed class CounterpartyCreationNeedHostV1(ICompanyContext company, ICurrentUser user, ICorrelationContext correlation,
    IErpAuthorityRevalidationService authority, IUserPermissionHolderReadPortV1 holders, IWorkCollaborationServiceV1 tasks)
{
    public async Task<WorkTaskResponseV1> CreateAsync(CounterpartyCreationNeedRequestV1 request, WorkCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captured = Capture();
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 128
            || metadata.IdempotencyKey != metadata.IdempotencyKey.Trim() || metadata.IdempotencyKey.Contains(',')
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || metadata.CorrelationId.Length > 128 || metadata.CausationId?.Length > 128
            || metadata.CorrelationId != captured.CorrelationId || metadata.CausationId != captured.CausationId)
            throw new WorkCollaborationException(400, WorkCollaborationProblemCodesV1.CommandMetadataInvalid);
        var dispatched = false;
        try
        {
            await ActorFence(captured, cancellationToken);
            if (request is null || string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > WorkCollaborationLimitsV1.TaskTitleMaximumLength
                || request.Details?.Length > WorkCollaborationLimitsV1.TaskDetailsMaximumLength
                || request.Details is not null && string.IsNullOrWhiteSpace(request.Details) || request.AssigneeUserId == Guid.Empty
                || (request.RecordType is null) != (request.RecordId is null)
                || request.RecordType is not null && (!WorkRecordTypesV1.TryParse(request.RecordType, out _) || request.RecordId == Guid.Empty))
                throw new WorkCollaborationException(422, WorkCollaborationProblemCodesV1.ValidationFailed);
            // Do not compare the deadline to now here: native replay must still work after the original deadline passed.
            await RecipientFence(captured, request.AssigneeUserId, cancellationToken);
            await ActorFence(captured, cancellationToken);
            dispatched = true;
            var answer = await tasks.CreateTaskAsync(request.ToWorkTask(), metadata, cancellationToken);
            Same(captured, cancellationToken);
            await ActorFence(captured, cancellationToken);
            await RecipientFence(captured, request.AssigneeUserId, cancellationToken);
            Same(captured, cancellationToken);
            if (!Matches(answer, captured, request, metadata)) throw Unavailable();
            return answer;
        }
        catch (OperationCanceledException) { throw; }
        catch (WorkCollaborationException failure) when (dispatched && failure.Status is 401 or 403)
        { throw Unavailable(); }
        catch (WorkCollaborationException) { throw; }
        catch (Exception) { throw Unavailable(); }
    }

    private sealed record Context(Guid CompanyId, string ActorId, Guid ActorUserId, string CorrelationId, string? CausationId);
    private Context Capture()
    {
        if (!user.IsAuthenticated || !Guid.TryParse(user.UserId, out var actor) || actor == Guid.Empty)
            throw new WorkCollaborationException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
            throw new WorkCollaborationException(403, "forbidden");
        if (!correlation.IsResolved) throw Unavailable();
        return new(company.CompanyId, user.UserId!, actor, correlation.CorrelationId, correlation.CausationId);
    }
    private void Same(Context captured, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || user.UserId != captured.ActorId || !company.IsResolved || company.CompanyId != captured.CompanyId
            || !user.CanAccessCompany(captured.CompanyId) || !correlation.IsResolved
            || correlation.CorrelationId != captured.CorrelationId || correlation.CausationId != captured.CausationId)
            throw new WorkCollaborationException(403, "forbidden");
    }
    private async Task ActorFence(Context captured, CancellationToken token)
    {
        Same(captured, token);
        var scope = await authority.RevalidatePermissionScopeAsync(WorkPermissionManifestV1.TasksAssign, captured.CompanyId, token);
        Same(captured, token);
        if (scope is not { IsGranted: true, IsCompanyWide: true }) throw new WorkCollaborationException(403, "forbidden");
    }
    private async Task RecipientFence(Context captured, Guid recipient, CancellationToken token)
    {
        Same(captured, token);
        var answer = await holders.HoldsAsync(new(captured.CompanyId, captured.ActorId, recipient, ErpPermissions.CounterpartyPartiesCreate), token);
        Same(captured, token);
        if (answer is { Outcome: UserDirectoryOutcomeV1.Found, Holds: true }) return;
        if (answer is { Outcome: UserDirectoryOutcomeV1.Found or UserDirectoryOutcomeV1.Denied })
            throw new WorkCollaborationException(403, "forbidden");
        throw Unavailable();
    }
    private static bool Matches(WorkTaskResponseV1? answer, Context captured, CounterpartyCreationNeedRequestV1 request, WorkCommandMetadataV1 metadata) =>
        answer?.Task is { } t && t.Id != Guid.Empty && t.CompanyId == captured.CompanyId && t.AssignerUserId == captured.ActorUserId
        && t.AssigneeUserId == request.AssigneeUserId && t.Title == request.Title && t.Details == request.Details && t.DueAtUtc == request.DueAtUtc
        && t.RecordType == request.RecordType && t.RecordId == request.RecordId
        && long.TryParse(t.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version > 0
        && t.Version == version.ToString(CultureInfo.InvariantCulture) && t.Status is "open" or "completed" or "cancelled"
        && (answer.IsReplay || t.Status == "open" && version == 1)
        && !string.IsNullOrWhiteSpace(answer.OriginalCorrelationId) && answer.OriginalCorrelationId.Length <= 128
        && (answer.IsReplay || answer.OriginalCorrelationId == metadata.CorrelationId);
    private static WorkCollaborationException Unavailable() => new(503, "service_unavailable");
}
