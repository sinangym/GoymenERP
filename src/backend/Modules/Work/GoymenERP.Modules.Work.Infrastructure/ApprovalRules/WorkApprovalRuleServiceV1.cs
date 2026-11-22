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
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.ApprovalRules;

/// <summary>
/// WP1 (YOL-D17, YOL-D18, D3–D5): the company's versioned approval rule table. One row per registered inbox source
/// (<see cref="IWorkInboxSourceV1"/>, the four native kinds included): the owner module, the owner's approver permission,
/// the duration class and the escalation permission in force. Until the company records a version the decided default
/// (<see cref="WorkApprovalRuleDefaultsV1"/>, version 0) applies. A change is the critical command skeleton (AGENTS.md):
/// permission fence (<c>settings.work-approval-duration.manage</c>, company-wide, revalidated now and before the save),
/// expected version, idempotency key per actor with request-hash replay before any validation or not-found, terminal
/// refusals recorded so the same key replays them, correlation/causation in the row, the attempt and the audit, and one
/// SaveChanges in one transaction. A change leaves Work with no external effect, so nothing goes to the outbox.
/// </summary>
public sealed class WorkApprovalRuleServiceV1(
    WorkDbContext database,
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IAuditEventCollector audit,
    ICorrelationContext correlation,
    TimeProvider time,
    IEnumerable<IWorkInboxSourceV1> registered) : IWorkApprovalRuleServiceV1
{
    public const string RecordCommand = "work.approval-rule.record.v1";
    public const string ManagePermission = ErpPermissions.SettingsWorkApprovalDurationManage;

    private static readonly JsonSerializerOptions HashJson = new(JsonSerializerDefaults.Web);

    public async Task<WorkApprovalRuleTableV1> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequirePermissionAsync(cancellationToken);
        var sources = Registry();
        var rows = await database.ApprovalRuleVersions.AsNoTracking()
            .Where(row => row.CompanyId == scope.CompanyId)
            .ToArrayAsync(cancellationToken);
        RequireStill(scope);
        var byKind = rows.ToLookup(row => row.Kind, StringComparer.Ordinal);
        var views = sources.Values
            .OrderBy(source => source.Module, StringComparer.Ordinal)
            .ThenBy(source => source.Kind, StringComparer.Ordinal)
            .Select(source => View(source, byKind[source.Kind]))
            .ToArray();
        return new WorkApprovalRuleTableV1(scope.CompanyId, views);
    }

    public async Task<WorkApprovalRuleCommandResultV1> RecordAsync(string kind, RecordWorkApprovalRuleRequestV1 request,
        WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequirePermissionAsync(cancellationToken);
        var requestHash = Hash(RecordCommand, kind, request.ExpectedVersion, request.DurationClass, request.EscalationPermission,
            request.Reason);
        var replay = await FindReplayAsync(scope, metadata, requestHash, cancellationToken);
        if (replay is not null)
        {
            return new(await CurrentViewAsync(scope, kind, cancellationToken), true, replay);
        }

        var sources = Registry();
        if (string.IsNullOrWhiteSpace(kind) || !sources.TryGetValue(kind, out var source))
        {
            throw new WorkApprovalRuleException(404, WorkApprovalRuleProblemCodesV1.KindUnknown);
        }

        var expected = ParseVersion(request.ExpectedVersion);
        var durationClass = WorkApprovalDurationClassCodesV1.FromWire(request.DurationClass)
            ?? throw new WorkApprovalRuleException(422, WorkApprovalRuleProblemCodesV1.ValidationFailed);
        var escalation = string.IsNullOrWhiteSpace(request.EscalationPermission) ? null : request.EscalationPermission;
        var reason = request.Reason?.Trim();
        if (escalation is not null && (!WorkInboxRulesV1.IsValidPermission(escalation) || !ErpPermissions.IsKnown(escalation))
            || string.IsNullOrEmpty(reason) || reason.Length > WorkApprovalRuleLimitsV1.ReasonMaximumLength)
        {
            throw new WorkApprovalRuleException(422, WorkApprovalRuleProblemCodesV1.ValidationFailed);
        }

        var latest = await database.ApprovalRuleVersions.AsNoTracking()
            .Where(row => row.CompanyId == scope.CompanyId && row.Kind == source.Kind)
            .OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken);
        RequireStill(scope);
        var currentVersion = latest?.Version ?? WorkApprovalRuleDefaultsV1.Version;
        var targetId = TargetId(scope.CompanyId, source.Kind);
        if (expected != currentVersion)
        {
            throw await TerminalAsync(scope, metadata, requestHash, targetId, WorkApprovalRuleProblemCodesV1.VersionConflict,
                cancellationToken);
        }

        var currentClass = latest is null
            ? WorkApprovalRuleDefaultsV1.DurationClassOf(source.Kind)
            : WorkApprovalDurationClassCodesV1.FromCode(latest.DurationClass)
                ?? throw new WorkApprovalRuleException(503, "service_unavailable");
        var currentEscalation = latest is null ? WorkApprovalRuleDefaultsV1.EscalationPermission : latest.EscalationPermission;
        var currentApprover = latest?.ApproverPermission ?? source.ApproverPermission;
        if (currentClass == durationClass
            && string.Equals(currentEscalation, escalation, StringComparison.Ordinal)
            && string.Equals(currentApprover, source.ApproverPermission, StringComparison.Ordinal))
        {
            throw await TerminalAsync(scope, metadata, requestHash, targetId, WorkApprovalRuleProblemCodesV1.Unchanged,
                cancellationToken);
        }

        await RequireStillPermissionAsync(scope, cancellationToken);
        var now = time.GetUtcNow();
        var row = new WorkApprovalRuleVersionRow
        {
            CompanyId = scope.CompanyId,
            Id = Guid.NewGuid(),
            Kind = source.Kind,
            Version = checked(currentVersion + 1),
            DurationClass = WorkApprovalDurationClassCodesV1.Code(durationClass),
            ApproverPermission = source.ApproverPermission,
            EscalationPermission = escalation,
            RecordedByUserId = scope.ActorUserId,
            RecordedAtUtc = now,
            Reason = reason,
            CorrelationId = metadata.CorrelationId,
            CausationId = metadata.CausationId,
        };
        database.ApprovalRuleVersions.Add(row);
        AddAttempt(scope, metadata, requestHash, targetId, 201, null, now);
        audit.Enqueue(new AuditEventDraft(scope.CompanyId, "work.approval-rule.recorded.v1", "Work.ApprovalRule", targetId.ToString("D"),
            "RecordApprovalRule", currentVersion, row.Version, AuditActorTypes.User, scope.ActorId, scope.Permission, scope.RoleCodes,
            JsonSerializer.Serialize(new
            {
                kind = source.Kind, version = currentVersion, durationClass = currentClass.ToString(),
                approverPermission = currentApprover, escalationPermission = currentEscalation,
            }, HashJson),
            JsonSerializer.Serialize(new
            {
                kind = source.Kind, version = row.Version, durationClass = durationClass.ToString(),
                approverPermission = row.ApproverPermission, escalationPermission = escalation,
            }, HashJson),
            reason, null, AuditAiInvolvement.None, null, correlation.IsResolved ? correlation.SourceChannel : "api",
            correlation.IsResolved ? correlation.ClientIpAddress : null, correlation.IsResolved ? correlation.UserAgent : null,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        var raced = await SaveAsync(scope, metadata, requestHash, cancellationToken);
        return new(await CurrentViewAsync(scope, source.Kind, cancellationToken), raced is not null,
            raced ?? metadata.CorrelationId);
    }

    // ---- Projection ------------------------------------------------------------------------------------------------------

    internal static WorkApprovalRuleViewV1 View(IWorkInboxSourceV1 source, IEnumerable<WorkApprovalRuleVersionRow> rows)
    {
        var history = rows.OrderByDescending(row => row.Version).ToArray();
        var latest = history.FirstOrDefault();
        var versions = history.Take(WorkApprovalRuleLimitsV1.MaximumVersionsShown).Select(row => new WorkApprovalRuleVersionViewV1(
                Text(row.Version),
                ClassWire(row.DurationClass),
                row.ApproverPermission,
                row.EscalationPermission,
                row.RecordedByUserId.ToString("D"),
                row.RecordedAtUtc,
                row.Reason))
            .ToArray();
        if (latest is null)
        {
            return new WorkApprovalRuleViewV1(source.Kind, source.Module, source.ApproverPermission,
                WorkApprovalRuleDefaultsV1.DurationClassOf(source.Kind).ToString(), WorkApprovalRuleDefaultsV1.EscalationPermission,
                Text(WorkApprovalRuleDefaultsV1.Version),
                WorkApprovalRuleVersionTextV1.Of(source.Kind, WorkApprovalRuleDefaultsV1.Version), true, WorkApprovalRuleDefaultsV1.Basis,
                WorkApprovalRuleStatesV1.Current, null, null, null, versions);
        }

        var state = string.Equals(latest.ApproverPermission, source.ApproverPermission, StringComparison.Ordinal)
            ? WorkApprovalRuleStatesV1.Current
            : WorkApprovalRuleStatesV1.ApproverPermissionChanged;
        return new WorkApprovalRuleViewV1(source.Kind, source.Module, source.ApproverPermission, ClassWire(latest.DurationClass),
            latest.EscalationPermission, Text(latest.Version), WorkApprovalRuleVersionTextV1.Of(source.Kind, latest.Version), false, null,
            state, latest.RecordedByUserId.ToString("D"), latest.RecordedAtUtc, latest.Reason, versions);
    }

    private static string ClassWire(int code) =>
        (WorkApprovalDurationClassCodesV1.FromCode(code)
            ?? throw new WorkApprovalRuleException(503, "service_unavailable")).ToString();

    private static string Text(long version) => version.ToString(CultureInfo.InvariantCulture);

    private async Task<WorkApprovalRuleViewV1> CurrentViewAsync(Scope scope, string kind, CancellationToken cancellationToken)
    {
        var sources = Registry();
        if (!sources.TryGetValue(kind, out var source))
        {
            throw new WorkApprovalRuleException(404, WorkApprovalRuleProblemCodesV1.KindUnknown);
        }

        var rows = await database.ApprovalRuleVersions.AsNoTracking()
            .Where(row => row.CompanyId == scope.CompanyId && row.Kind == source.Kind)
            .ToArrayAsync(cancellationToken);
        RequireStill(scope);
        return View(source, rows);
    }

    /// <summary>The registered sources by kind; a registration that is not exact makes the table unavailable (503).</summary>
    private IReadOnlyDictionary<string, IWorkInboxSourceV1> Registry()
    {
        var byKind = new Dictionary<string, IWorkInboxSourceV1>(StringComparer.Ordinal);
        foreach (var source in registered)
        {
            if (source is null
                || !NativeApprovalQueueRulesV1.IsValidKind(source.Module, source.Kind)
                || !WorkInboxRulesV1.IsValidPermission(source.ApproverPermission)
                || !byKind.TryAdd(source.Kind, source))
            {
                throw new WorkApprovalRuleException(503, "service_unavailable");
            }
        }

        return byKind;
    }

    // ---- Command skeleton ------------------------------------------------------------------------------------------------

    private sealed record Scope(Guid CompanyId, string ActorId, Guid ActorUserId, string Permission, IReadOnlyList<string> RoleCodes);

    private async Task<Scope> RequirePermissionAsync(CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated || !Guid.TryParse(user.UserId, out var actorUserId) || actorUserId == Guid.Empty)
        {
            throw new WorkApprovalRuleException(401, "unauthenticated");
        }

        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
        {
            throw new WorkApprovalRuleException(403, "forbidden");
        }

        if (!ErpPermissions.IsKnown(ManagePermission))
        {
            throw new WorkApprovalRuleException(503, WorkApprovalRuleProblemCodesV1.PermissionUnregistered);
        }

        var companyId = company.CompanyId;
        var actorId = user.UserId!;
        var granted = await authority.RevalidatePermissionScopeAsync(ManagePermission, companyId, cancellationToken);
        var scope = new Scope(companyId, actorId, actorUserId, ManagePermission,
            granted.RoleCodes.Order(StringComparer.Ordinal).ToArray());
        RequireStill(scope);
        if (granted is not { IsGranted: true, IsCompanyWide: true })
        {
            throw new WorkApprovalRuleException(403, "forbidden");
        }

        return scope;
    }

    private void RequireStill(Scope scope)
    {
        if (!user.IsAuthenticated || user.UserId != scope.ActorId || !company.IsResolved || company.CompanyId != scope.CompanyId
            || !user.CanAccessCompany(scope.CompanyId))
        {
            throw new WorkApprovalRuleException(403, "forbidden");
        }
    }

    private async Task RequireStillPermissionAsync(Scope scope, CancellationToken cancellationToken)
    {
        RequireStill(scope);
        var granted = await authority.RevalidatePermissionScopeAsync(scope.Permission, scope.CompanyId, cancellationToken);
        RequireStill(scope);
        if (granted is not { IsGranted: true, IsCompanyWide: true })
        {
            throw new WorkApprovalRuleException(403, "forbidden");
        }
    }

    /// <summary>"0" is the decided default; any other version is a positive canonical integer.</summary>
    private static long ParseVersion(string? value) =>
        value is not null && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version >= 0
            && value == version.ToString(CultureInfo.InvariantCulture)
            ? version
            : throw new WorkApprovalRuleException(400, WorkApprovalRuleProblemCodesV1.ValidationFailed);

    /// <summary>A stable non-empty identity of one kind's rule in one company (audit entity id and attempt target).</summary>
    internal static Guid TargetId(Guid companyId, string kind)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"work.approval-rule:{companyId:D}:{kind}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string Hash(params object?[] parts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts, HashJson))));

    private static string KeyHash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>The same key replays its first answer: the success (its original correlation) or the recorded refusal.</summary>
    private async Task<string?> FindReplayAsync(Scope scope, WorkCommandMetadataV1 metadata, string requestHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 128
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || metadata.CorrelationId.Length > 128 || metadata.CausationId?.Length > 128)
        {
            throw new WorkApprovalRuleException(400, WorkApprovalRuleProblemCodesV1.CommandMetadataInvalid);
        }

        var keyHash = KeyHash(metadata.IdempotencyKey);
        var attempt = await database.CollaborationAttempts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == scope.CompanyId && row.ActorUserId == scope.ActorUserId && row.IdempotencyKeySha256 == keyHash,
            cancellationToken);
        RequireStill(scope);
        if (attempt is null)
        {
            return null;
        }

        if (attempt.Command != RecordCommand || attempt.RequestSha256 != requestHash)
        {
            throw new WorkApprovalRuleException(409, WorkApprovalRuleProblemCodesV1.SameKeyPayloadMismatch);
        }

        if (attempt.Status == 409)
        {
            throw new WorkApprovalRuleException(409, attempt.RefusalCode!, isReplay: true, attempt.CorrelationId);
        }

        return attempt.CorrelationId;
    }

    private void AddAttempt(Scope scope, WorkCommandMetadataV1 metadata, string requestHash, Guid targetId, int status,
        string? refusalCode, DateTimeOffset now) => database.CollaborationAttempts.Add(new WorkCollaborationAttemptRow
        {
            CompanyId = scope.CompanyId, ActorUserId = scope.ActorUserId, IdempotencyKeySha256 = KeyHash(metadata.IdempotencyKey),
            Command = RecordCommand, RequestSha256 = requestHash, TargetId = targetId, Status = status, RefusalCode = refusalCode,
            CorrelationId = metadata.CorrelationId, RecordedAtUtc = now,
        });

    /// <summary>A refusal of state is durable: recorded with its key, then thrown; a lost race answers with the winner.</summary>
    private async Task<WorkApprovalRuleException> TerminalAsync(Scope scope, WorkCommandMetadataV1 metadata, string requestHash,
        Guid targetId, string code, CancellationToken cancellationToken)
    {
        RequireStill(scope);
        database.ChangeTracker.Clear();
        AddAttempt(scope, metadata, requestHash, targetId, 409, code, time.GetUtcNow());
        var saved = await SaveAsync(scope, metadata, requestHash, cancellationToken);
        return saved is null
            ? new WorkApprovalRuleException(409, code)
            : new WorkApprovalRuleException(409, WorkApprovalRuleProblemCodesV1.ConcurrentChange);
    }

    /// <summary>
    /// One SaveChanges in one transaction. A lost race on the attempt key or the kind's version number is answered with the
    /// winner's stored answer when the same key won (its correlation is returned), otherwise 409 concurrent change.
    /// </summary>
    private async Task<string?> SaveAsync(Scope scope, WorkCommandMetadataV1 metadata, string requestHash,
        CancellationToken cancellationToken)
    {
        RequireStill(scope);
        try
        {
            if (database.Database.IsRelational())
            {
                await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await database.SaveChangesAsync(cancellationToken);
                RequireStill(scope);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await database.SaveChangesAsync(cancellationToken);
            }

            return null;
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var replay = await FindReplayAsync(scope, metadata, requestHash, cancellationToken);
            return replay ?? throw new WorkApprovalRuleException(409, WorkApprovalRuleProblemCodesV1.ConcurrentChange);
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }
}

/// <summary>
/// The rule table read for the Work inbox (<see cref="IWorkApprovalRuleReaderV1"/>): the latest version of each registered
/// kind in the resolved company, or the decided default. Never throws for a store failure: answers null.
/// </summary>
public sealed class WorkApprovalRuleReaderV1(WorkDbContext database) : IWorkApprovalRuleReaderV1
{
    public async Task<IReadOnlyDictionary<string, WorkApprovalRuleResolutionV1>?> ResolveAsync(Guid companyId,
        IReadOnlyList<IWorkInboxSourceV1> sources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (companyId == Guid.Empty)
        {
            return null;
        }

        var kinds = sources.Select(source => source.Kind).Distinct(StringComparer.Ordinal).ToArray();
        WorkApprovalRuleVersionRow[] rows;
        try
        {
            rows = await database.ApprovalRuleVersions.AsNoTracking()
                .Where(row => row.CompanyId == companyId && kinds.Contains(row.Kind))
                .ToArrayAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        var resolved = new Dictionary<string, WorkApprovalRuleResolutionV1>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var latest = rows.Where(row => row.CompanyId == companyId && string.Equals(row.Kind, source.Kind, StringComparison.Ordinal))
                .OrderByDescending(row => row.Version)
                .FirstOrDefault();
            if (latest is null)
            {
                resolved[source.Kind] = new(source.Kind, WorkApprovalRuleDefaultsV1.DurationClassOf(source.Kind),
                    WorkApprovalRuleDefaultsV1.EscalationPermission,
                    WorkApprovalRuleDefaultsV1.Version, true, true);
                continue;
            }

            if (WorkApprovalDurationClassCodesV1.FromCode(latest.DurationClass) is not { } durationClass)
            {
                return null;
            }

            resolved[source.Kind] = new(source.Kind, durationClass, latest.EscalationPermission, latest.Version, false,
                string.Equals(latest.ApproverPermission, source.ApproverPermission, StringComparison.Ordinal));
        }

        return resolved;
    }
}
