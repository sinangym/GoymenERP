using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.ApprovalFollowUp;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Work.Infrastructure.Delegations;

/// <summary>ROL-D27: own delegation or explicit company management permission, never an Identity write or an owner decision.</summary>
public sealed class WorkDelegationServiceV1(WorkDbContext db, ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IWorkPermissionHolderDirectoryV1 people,
    IEnumerable<IWorkInboxSourceV1> sources, IAuditEventCollector audit, IAuditTransactionWriterV1 auditOwner,
    ICorrelationContext correlation, IOptions<WorkDelegationOptionsV1> options, TimeProvider time) : IWorkDelegationServiceV1, IWorkDelegationReaderV1
{
    // Technical page bound, not a retention or business policy. HasMore is never hidden.
    public const int MaximumRows = 200;
    public const string CreateCommand = "work.delegation.create.v1";
    public const string RevokeCommand = "work.delegation.revoke.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Actor(Guid CompanyId, Guid UserId, string Id);
    private sealed record Access(Actor Actor, string Permission, IReadOnlyList<string> Roles);

    public async Task<WorkDelegationTableV1> ListAsync(bool all = false, int page = 0, CancellationToken cancellationToken = default)
    {
        var actor = Caller();
        if (page < 0 || page > int.MaxValue / MaximumRows) throw Error(400, WorkDelegationCodesV1.Invalid);
        if (all) _ = await AccessAsync(actor, null, null, cancellationToken);
        var query = db.Delegations.AsNoTracking().Where(row => row.CompanyId == actor.CompanyId);
        if (!all) query = query.Where(row => row.DelegatorUserId == actor.UserId || row.DelegateUserId == actor.UserId);
        var rows = await query.OrderByDescending(row => row.RecordedAtUtc).ThenBy(row => row.Id)
            .Skip(page * MaximumRows).Take(MaximumRows + 1).ToArrayAsync(cancellationToken);
        Still(actor);
        if (all) _ = await AccessAsync(actor, null, null, cancellationToken);
        return new(actor.CompanyId, rows.Take(MaximumRows).Select(View).ToArray(), page, rows.Length > MaximumRows);
    }

    public async Task<WorkDelegationCommandResultV1> CreateAsync(CreateWorkDelegationV1 request,
        WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Caller(); var owner = Source(request.Kind);
        var delegator = request.DelegatorUserId ?? actor.UserId;
        var access = await AccessAsync(actor, delegator, owner, cancellationToken);
        ValidateMetadata(metadata);
        var hash = Hash(CreateCommand, request);
        return await MutateAsync(actor, metadata, CreateCommand, hash, async transaction =>
        {
            if (await ReplayAsync(actor, metadata, CreateCommand, hash, cancellationToken) is { } replay) return replay;
            var now = time.GetUtcNow(); var start = request.StartsAtUtc ?? now;
            if (delegator == Guid.Empty || request.DelegateUserId == Guid.Empty || delegator == request.DelegateUserId
                || start.Offset != TimeSpan.Zero || request.EndsAtUtc.Offset != TimeSpan.Zero || start < now
                || request.EndsAtUtc <= start || !Reason(request.Reason)) throw Error(422, WorkDelegationCodesV1.Invalid);
            await EligibleAsync(actor, owner, delegator, request.DelegateUserId, cancellationToken);
            // The SERIALIZABLE predicate covers overlapping intervals of this approval kind and permission, including future rows.
            var overlapping = await db.Delegations.AsNoTracking().Where(row => row.CompanyId == actor.CompanyId
                && row.Kind == owner.Kind && row.ApproverPermission == owner.ApproverPermission && row.RevokedAtUtc == null
                && row.StartsAtUtc < request.EndsAtUtc && row.EndsAtUtc > start).ToArrayAsync(cancellationToken);
            Still(actor);
            if (overlapping.Any(row => row.DelegatorUserId == delegator || row.DelegateUserId == delegator
                || row.DelegatorUserId == request.DelegateUserId))
                return await ConflictAsync(actor, metadata, CreateCommand, hash, Guid.NewGuid(), WorkDelegationCodesV1.Chain,
                    access, owner, delegator, transaction, cancellationToken);
            var row = new WorkDelegationRow { CompanyId = actor.CompanyId, Id = Guid.NewGuid(), Kind = owner.Kind,
                ApproverPermission = owner.ApproverPermission, DelegatorUserId = delegator, DelegateUserId = request.DelegateUserId,
                StartsAtUtc = start, EndsAtUtc = request.EndsAtUtc, Version = 1, RecordedByUserId = actor.UserId,
                RecordedAtUtc = now, Reason = request.Reason.Trim() };
            db.Delegations.Add(row);
            AddAttempt(actor, metadata, CreateCommand, hash, row.Id, 201, null);
            EnqueueAudit(access, metadata, row, "work.delegation.created.v1", null, null, row.Reason);
            _ = await AccessAsync(actor, delegator, owner, cancellationToken);
            await EligibleAsync(actor, owner, delegator, request.DelegateUserId, cancellationToken);
            await SaveAsync(transaction, cancellationToken);
            _ = await AccessAsync(actor, delegator, owner, cancellationToken);
            await EligibleAsync(actor, owner, delegator, request.DelegateUserId, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(View(row), false, metadata.CorrelationId);
        }, cancellationToken);
    }

    public async Task<WorkDelegationCommandResultV1> RevokeAsync(Guid id, RevokeWorkDelegationV1 request,
        WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Caller(); ValidateMetadata(metadata);
        var original = await db.Delegations.AsNoTracking().SingleOrDefaultAsync(row => row.CompanyId == actor.CompanyId
            && row.Id == id, cancellationToken) ?? throw Error(404, "not_found");
        Still(actor); var owner = Source(original.Kind);
        var access = await AccessAsync(actor, original.DelegatorUserId, owner, cancellationToken);
        var hash = Hash(RevokeCommand, id, request);
        return await MutateAsync(actor, metadata, RevokeCommand, hash, async transaction =>
        {
            if (await ReplayAsync(actor, metadata, RevokeCommand, hash, cancellationToken) is { } replay) return replay;
            if (!Reason(request.Reason) || !long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture,
                out var expected) || expected <= 0 || request.ExpectedVersion != expected.ToString(CultureInfo.InvariantCulture))
                throw Error(422, WorkDelegationCodesV1.Invalid);
            var row = await db.Delegations.SingleOrDefaultAsync(item => item.CompanyId == actor.CompanyId && item.Id == id, cancellationToken)
                ?? throw Error(404, "not_found");
            Still(actor);
            if (row.Version != expected || row.RevokedAtUtc is not null)
                return await ConflictAsync(actor, metadata, RevokeCommand, hash, id, WorkDelegationCodesV1.Version,
                    access, owner, row.DelegatorUserId, transaction, cancellationToken);
            var before = View(row);
            row.Version = checked(row.Version + 1); row.RevokedByUserId = actor.UserId;
            row.RevokedAtUtc = time.GetUtcNow(); row.RevocationReason = request.Reason.Trim();
            AddAttempt(actor, metadata, RevokeCommand, hash, id, 200, null);
            EnqueueAudit(access, metadata, row, "work.delegation.revoked.v1", expected, before, row.RevocationReason);
            _ = await AccessAsync(actor, row.DelegatorUserId, owner, cancellationToken);
            await SaveAsync(transaction, cancellationToken);
            _ = await AccessAsync(actor, row.DelegatorUserId, owner, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(View(row), false, metadata.CorrelationId);
        }, cancellationToken);
    }

    /// <summary>WP8 may attach this metadata to an already authorized owner item. This method grants no decision authority.</summary>
    public async Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 ownerItem,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return null;
        var actor = Caller();
        if (ownerItem.Excludes(actor.Id)) return null;
        var now = time.GetUtcNow();
        var row = await db.Delegations.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == actor.CompanyId
            && item.Id == id && item.DelegateUserId == actor.UserId && item.Kind == ownerItem.Kind
            && item.RevokedAtUtc == null && item.StartsAtUtc <= now && now < item.EndsAtUtc, cancellationToken);
        Still(actor);
        if (row is null) return null;
        var owner = Source(row.Kind);
        if (row.ApproverPermission != owner.ApproverPermission) return null;
        try
        {
            var holders = await EligibleAsync(actor, owner, row.DelegatorUserId, actor.UserId, cancellationToken);
            bool Covers(WorkPermissionHolderV1 holder) => holder.IsCompanyWide || owner.AcceptsScopedApprovers
                && new WorkInboxGrantScopeV1(holder.SiteIds, holder.LocationIds, holder.WorkCenterIds).Covers(ownerItem.Scope);
            if (!Covers(holders.Delegator) || !Covers(holders.Delegate)) return null;
            _ = await AccessAsync(actor, actor.UserId, owner, cancellationToken);
            return View(row);
        }
        catch (WorkDelegationException error) when (error.Status == 403 || error.Status == 422) { return null; }
    }

    private async Task<(WorkPermissionHolderV1 Delegator, WorkPermissionHolderV1 Delegate)> EligibleAsync(Actor actor,
        IWorkInboxSourceV1 source, Guid delegator, Guid target, CancellationToken token)
    {
        var answer = await people.ListHoldersAsync(actor.CompanyId, source.ApproverPermission, token);
        Still(actor);
        if (!answer.Available || answer.Holders.Any(item => item.UserId == Guid.Empty)
            || answer.Holders.Select(item => item.UserId).Distinct().Count() != answer.Holders.Count)
            throw Error(503, "service_unavailable");
        var sender = answer.Holders.SingleOrDefault(item => item.UserId == delegator);
        var receiver = answer.Holders.SingleOrDefault(item => item.UserId == target);
        if (sender?.IsGeneralManager == true && receiver is not null)
        {
            if (receiver.ModuleManagerScope is not { } manager) throw Error(422, WorkDelegationCodesV1.MissingPermission);
            // ROL-D4: another role's broader grant must not broaden the module manager's own eligible scope.
            receiver = receiver with { IsCompanyWide = manager.IsCompanyWide, SiteIds = manager.SiteIds,
                LocationIds = manager.LocationIds, WorkCenterIds = manager.WorkCenterIds };
        }
        if (sender is null || receiver is null || (!source.AcceptsScopedApprovers || ErpPermissions.RequiresCompanyWideScope(source.ApproverPermission))
            && (!sender.IsCompanyWide || !receiver.IsCompanyWide))
            throw Error(422, WorkDelegationCodesV1.MissingPermission);
        if (!receiver.IsCompanyWide && (sender.IsCompanyWide || !sender.SiteIds.IsSubsetOf(receiver.SiteIds)
            || !sender.LocationIds.IsSubsetOf(receiver.LocationIds) || !sender.WorkCenterIds.IsSubsetOf(receiver.WorkCenterIds)))
            throw Error(422, WorkDelegationCodesV1.ScopeMismatch);
        return (sender, receiver);
    }

    private Actor Caller()
    {
        if (!user.IsAuthenticated || !Guid.TryParse(user.UserId, out var id) || id == Guid.Empty) throw Error(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId)) throw Error(403, "forbidden");
        if (!options.Value.Enabled) throw Error(503, WorkDelegationCodesV1.Disabled);
        return new(company.CompanyId, id, user.UserId!);
    }

    private void Still(Actor actor)
    {
        if (!options.Value.Enabled || !user.IsAuthenticated || user.UserId != actor.Id || !company.IsResolved
            || company.CompanyId != actor.CompanyId || !user.CanAccessCompany(actor.CompanyId)) throw Error(403, "forbidden");
    }

    private async Task<Access> AccessAsync(Actor actor, Guid? delegator, IWorkInboxSourceV1? source, CancellationToken token)
    {
        Still(actor);
        var permission = delegator == actor.UserId && source is not null ? source.ApproverPermission : ErpPermissions.WorkDelegationsManage;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, actor.CompanyId, token);
        Still(actor);
        if (!scope.IsGranted && permission != ErpPermissions.WorkDelegationsManage)
        {
            permission = ErpPermissions.WorkDelegationsManage;
            scope = await authority.RevalidatePermissionScopeAsync(permission, actor.CompanyId, token);
            Still(actor);
        }
        if (!scope.IsGranted || !scope.IsCompanyWide && (ErpPermissions.RequiresCompanyWideScope(permission)
            || permission == ErpPermissions.WorkDelegationsManage || source is not { AcceptsScopedApprovers: true }))
            throw Error(403, "forbidden");
        return new(actor, permission, scope.RoleCodes.Order(StringComparer.Ordinal).ToArray());
    }

    private IWorkInboxSourceV1 Source(string kind)
    {
        var matches = sources.Where(item => item.Kind == kind).ToArray();
        if (matches.Length != 1 || !ErpPermissions.IsKnown(matches[0].ApproverPermission)) throw Error(422, WorkDelegationCodesV1.Invalid);
        return matches[0];
    }

    private async Task<WorkDelegationCommandResultV1> MutateAsync(Actor actor, WorkCommandMetadataV1 metadata, string command,
        string hash, Func<IDbContextTransaction?, Task<WorkDelegationCommandResultV1>> mutate, CancellationToken token)
    {
        // No draft owned by an earlier command may be flushed or discarded here.
        if (auditOwner.HasPendingDrafts || db.ChangeTracker.HasChanges()) throw Error(503, "service_unavailable");
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
        try { return await mutate(transaction); }
        catch (Exception error) when (error is DbUpdateException or SqlException)
        {
            if (transaction is not null)
            {
                // Dispose aborts an uncommitted transaction and also handles a SQL deadlock victim already rolled back.
                await transaction.DisposeAsync();
            }
            db.ChangeTracker.Clear(); auditOwner.DiscardPending();
            if (await ReplayAsync(actor, metadata, command, hash, token) is { } replay) return replay;
            throw Error(409, WorkDelegationCodesV1.Concurrent);
        }
        finally { db.ChangeTracker.Clear(); auditOwner.DiscardPending(); }
    }

    private async Task SaveAsync(IDbContextTransaction? transaction, CancellationToken token)
    {
        if (transaction is not null) await auditOwner.WritePendingAsync(transaction.GetDbTransaction(), token);
        await db.SaveChangesAsync(token);
    }

    private async Task<WorkDelegationCommandResultV1?> ReplayAsync(Actor actor, WorkCommandMetadataV1 metadata, string command,
        string hash, CancellationToken token)
    {
        var key = Digest(metadata.IdempotencyKey);
        var attempt = await db.CollaborationAttempts.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == actor.CompanyId
            && item.ActorUserId == actor.UserId && item.IdempotencyKeySha256 == key, token);
        Still(actor);
        if (attempt is null) return null;
        if (attempt.Command != command || attempt.RequestSha256 != hash) throw Error(409, WorkDelegationCodesV1.KeyMismatch);
        if (attempt.Status == 409) throw new WorkDelegationException(409, attempt.RefusalCode!, true, attempt.CorrelationId);
        var row = await db.Delegations.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == actor.CompanyId
            && item.Id == attempt.TargetId, token) ?? throw Error(503, "service_unavailable");
        Still(actor);
        _ = await AccessAsync(actor, row.DelegatorUserId, Source(row.Kind), token);
        return new(View(row), true, attempt.CorrelationId);
    }

    private async Task<WorkDelegationCommandResultV1> ConflictAsync(Actor actor, WorkCommandMetadataV1 metadata,
        string command, string hash, Guid target, string code, Access access, IWorkInboxSourceV1 source, Guid delegator,
        IDbContextTransaction? transaction, CancellationToken token)
    {
        _ = access;
        AddAttempt(actor, metadata, command, hash, target, 409, code);
        _ = await AccessAsync(actor, delegator, source, token);
        await SaveAsync(transaction, token);
        _ = await AccessAsync(actor, delegator, source, token);
        if (transaction is not null) await transaction.CommitAsync(token);
        throw Error(409, code);
    }

    private void AddAttempt(Actor actor, WorkCommandMetadataV1 metadata, string command, string hash, Guid target, int status,
        string? refusal) => db.CollaborationAttempts.Add(new WorkCollaborationAttemptRow { CompanyId = actor.CompanyId,
        ActorUserId = actor.UserId, IdempotencyKeySha256 = Digest(metadata.IdempotencyKey), Command = command,
        RequestSha256 = hash, TargetId = target, Status = status, RefusalCode = refusal,
        CorrelationId = metadata.CorrelationId, RecordedAtUtc = time.GetUtcNow() });

    private void EnqueueAudit(Access access, WorkCommandMetadataV1 metadata, WorkDelegationRow row, string eventType,
        long? beforeVersion, WorkDelegationViewV1? before, string reason) => audit.Enqueue(new AuditEventDraft(row.CompanyId,
        eventType, "Work.ApprovalDelegation", row.Id.ToString("D"), eventType, beforeVersion, row.Version,
        AuditActorTypes.User, access.Actor.Id, access.Permission, access.Roles,
        before is null ? "{}" : JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(View(row), Json),
        reason, null, AuditAiInvolvement.None, null, correlation.IsResolved ? correlation.SourceChannel : "api",
        correlation.IsResolved ? correlation.ClientIpAddress : null, correlation.IsResolved ? correlation.UserAgent : null,
        metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));

    private static bool Reason(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 500;
    private static void ValidateMetadata(WorkCommandMetadataV1 metadata)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 128
            || metadata.IdempotencyKey != metadata.IdempotencyKey.Trim() || metadata.IdempotencyKey.Contains(',')
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || metadata.CorrelationId.Length > 128 || metadata.CausationId?.Length > 128)
            throw Error(400, WorkDelegationCodesV1.Metadata);
    }
    private static WorkDelegationException Error(int status, string code) => new(status, code);
    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Hash(params object?[] value) => Digest(JsonSerializer.Serialize(value, Json));
    private static WorkDelegationViewV1 View(WorkDelegationRow row) => new(row.Id, row.CompanyId, row.Kind,
        row.ApproverPermission, row.DelegatorUserId, row.DelegateUserId, row.StartsAtUtc, row.EndsAtUtc,
        row.Version.ToString(CultureInfo.InvariantCulture), row.RecordedByUserId, row.RecordedAtUtc, row.Reason,
        row.RevokedByUserId, row.RevokedAtUtc, row.RevocationReason);
}
