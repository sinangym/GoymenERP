using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure;

/// <summary>
/// WP-A4 knowledge package approval (AI-D32 "onaylı karar dizini"). Critical command skeleton (AGENTS.md): permission fence
/// (503 for an unregistered name, company-wide grant of <c>ai.knowledge.approve</c> revalidated now and again right before the
/// write), expected version on withdrawal, the idempotency key per actor with request-hash replay before any not-found or
/// validation, correlation/causation in every row and audit, refusals of state recorded so the same key replays them, and one
/// SaveChanges per command. The approver names the SHA-256 of the package they reviewed; a different packaged version is
/// refused (an approval binds to the concrete bytes that were seen). AI-D46: the approval is group-wide — one approval of a
/// package version holds in every company; the approver is any user holding the permission company-wide in the company of the
/// request, which is recorded as the origin company and carries the audit event (entity <c>AiKnowledge.GroupPackageApproval</c>,
/// scope "group" in the audit payload). The permission is registered, never granted here (AI-D46: not Critical, no role).
/// </summary>
public sealed partial class AiKnowledgeApprovalServiceV1(
    AiKnowledgeDbContext database,
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IAiKnowledgePackageCatalogV1 packages,
    IAuditEventCollector audit,
    ICorrelationContext correlation,
    TimeProvider time) : IAiKnowledgeApprovalServiceV1
{
    private static readonly JsonSerializerOptions HashJson = new(JsonSerializerDefaults.Web);

    public async Task<AiKnowledgePackageStatusPageV1> ListPackagesAsync(CancellationToken cancellationToken = default)
    {
        var scope = await RequirePermissionAsync(cancellationToken);
        var items = new List<AiKnowledgePackageStatusV1>();
        foreach (var kind in AiKnowledgeSourceKindsV1.All)
        {
            var package = packages.Find(kind);
            AiKnowledgeApprovalViewV1? active = null;
            if (package is not null)
            {
                var row = await database.Approvals.AsNoTracking().SingleOrDefaultAsync(item =>
                    item.SourceKind == (int)kind && item.PackageSha256 == package.PackageSha256
                    && item.Status == (int)AiKnowledgeApprovalStatusV1.Approved, cancellationToken);
                active = row is null ? null : View(row);
            }

            items.Add(new AiKnowledgePackageStatusV1(AiKnowledgeSourceKindsV1.Wire(kind), package is not null, package?.PackageSha256,
                package?.SourceSetSha256, package?.ItemCount, package?.ExcludedOrConflictCount, package?.MaskedItemCount, active));
        }

        RequireStill(scope);
        return new AiKnowledgePackageStatusPageV1(AiKnowledgeApprovalScopesV1.Group, items);
    }

    public async Task<AiKnowledgeApprovalResponseV1> ApproveAsync(ApproveAiKnowledgePackageRequestV1 request,
        AiKnowledgeCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequirePermissionAsync(cancellationToken);
        var command = AiKnowledgeCommandsV1.ApprovePackage;
        var requestHash = Hash(command, request.SourceKind, request.PackageSha256);
        var replay = await FindReplayAsync(scope, metadata, command, requestHash, cancellationToken);
        if (replay is not null) return new(await ViewAsync(replay.TargetId, cancellationToken), true, replay.CorrelationId);

        if (!AiKnowledgeSourceKindsV1.TryParse(request.SourceKind, out var kind) || request.PackageSha256 is null
            || !Sha256Pattern().IsMatch(request.PackageSha256))
            throw new AiKnowledgeException(422, AiKnowledgeProblemCodesV1.ValidationFailed);
        var package = packages.Find(kind) ?? throw new AiKnowledgeException(503, AiKnowledgeProblemCodesV1.PackageUnavailable);
        // The approver approves what they reviewed: other bytes are another version and need their own approval.
        if (!string.Equals(package.PackageSha256, request.PackageSha256, StringComparison.Ordinal))
            throw new AiKnowledgeException(422, AiKnowledgeProblemCodesV1.PackageChanged);

        var approvalId = Guid.NewGuid();
        var active = await database.Approvals.AsNoTracking().AnyAsync(row =>
            row.SourceKind == (int)kind && row.PackageSha256 == package.PackageSha256
            && row.Status == (int)AiKnowledgeApprovalStatusV1.Approved, cancellationToken);
        if (active)
            throw await TerminalAsync(scope, metadata, command, requestHash, approvalId, AiKnowledgeProblemCodesV1.AlreadyApproved,
                cancellationToken);

        var approval = AiKnowledgeApprovalV1.Approve(scope.ActorUserId);
        await RequireStillPermissionAsync(scope, cancellationToken);
        var now = time.GetUtcNow();
        var row = new AiKnowledgeApprovalRow
        {
            OriginCompanyId = scope.CompanyId, Id = approvalId, SourceKind = (int)kind, PackageSha256 = package.PackageSha256,
            Status = (int)approval.Status, Version = approval.Version, ApprovedByUserId = approval.ApprovedByUserId, ApprovedAtUtc = now,
            CorrelationId = metadata.CorrelationId, CausationId = metadata.CausationId,
        };
        database.Approvals.Add(row);
        AddAttempt(scope, metadata, command, requestHash, approvalId, 201, null, now);
        Audit(scope, metadata, "ai-knowledge.package.approved.v1", approvalId, "ApprovePackage", null, 1, "{}", ApprovalAudit(row),
            "Yapay zekâ bilgi paketi sürümü grup genelinde onaylandı.");
        var saved = await SaveAsync(scope, metadata, command, requestHash, cancellationToken);
        if (saved is not null) return new(await ViewAsync(saved.TargetId, cancellationToken), true, saved.CorrelationId);
        return new(await ViewAsync(approvalId, cancellationToken), false, metadata.CorrelationId);
    }

    public async Task<AiKnowledgeApprovalResponseV1> WithdrawAsync(Guid approvalId, WithdrawAiKnowledgeApprovalRequestV1 request,
        AiKnowledgeCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequirePermissionAsync(cancellationToken);
        var version = ParseVersion(request.ExpectedVersion);
        var command = AiKnowledgeCommandsV1.WithdrawApproval;
        var requestHash = Hash(command, approvalId, request.ExpectedVersion, request.Reason);
        var replay = await FindReplayAsync(scope, metadata, command, requestHash, cancellationToken);
        if (replay is not null) return new(await ViewAsync(replay.TargetId, cancellationToken), true, replay.CorrelationId);
        if (approvalId == Guid.Empty) throw new AiKnowledgeException(404, "not_found");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > AiKnowledgeApprovalV1.ReasonMaximumLength)
            throw new AiKnowledgeException(422, AiKnowledgeProblemCodesV1.ValidationFailed);
        var row = await database.Approvals.SingleOrDefaultAsync(item => item.Id == approvalId, cancellationToken)
            ?? throw new AiKnowledgeException(404, "not_found");
        var approval = AiKnowledgeApprovalV1.Restore(row.ApprovedByUserId, (AiKnowledgeApprovalStatusV1)row.Status, row.Version);
        var before = ApprovalAudit(row);
        var now = time.GetUtcNow();
        try
        {
            approval.Withdraw(version, scope.ActorUserId, request.Reason, now);
        }
        catch (AiKnowledgeRuleException rule)
        {
            throw await TerminalAsync(scope, metadata, command, requestHash, approvalId, rule.Code, cancellationToken);
        }

        if (approval.Version != row.Version + 1) throw new InvalidOperationException("A withdrawal must move exactly one version.");
        await RequireStillPermissionAsync(scope, cancellationToken);
        row.Status = (int)approval.Status;
        row.Version = approval.Version;
        row.WithdrawnByUserId = approval.WithdrawnByUserId;
        row.WithdrawnOriginCompanyId = scope.CompanyId;
        row.WithdrawnAtUtc = approval.WithdrawnAtUtc;
        row.WithdrawReason = approval.WithdrawReason;
        AddAttempt(scope, metadata, command, requestHash, approvalId, 200, null, now);
        Audit(scope, metadata, "ai-knowledge.approval.withdrawn.v1", approvalId, "WithdrawApproval", approval.Version - 1, approval.Version,
            before, ApprovalAudit(row), request.Reason);
        var saved = await SaveAsync(scope, metadata, command, requestHash, cancellationToken);
        if (saved is not null) return new(await ViewAsync(saved.TargetId, cancellationToken), true, saved.CorrelationId);
        return new(await ViewAsync(approvalId, cancellationToken), false, metadata.CorrelationId);
    }

    // ---- Fence -------------------------------------------------------------------------------------------------------------

    private Scope RequireActor()
    {
        if (!user.IsAuthenticated || !Guid.TryParse(user.UserId, out var actorUserId) || actorUserId == Guid.Empty)
            throw new AiKnowledgeException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
            throw new AiKnowledgeException(403, "forbidden");
        return new Scope(company.CompanyId, user.UserId!, actorUserId, AiKnowledgePermissionManifestV1.Approve, []);
    }

    private void RequireStill(Scope scope)
    {
        if (!user.IsAuthenticated || user.UserId != scope.ActorId || !company.IsResolved || company.CompanyId != scope.CompanyId
            || !user.CanAccessCompany(scope.CompanyId))
            throw new AiKnowledgeException(403, "forbidden");
    }

    private async Task<Scope> RequirePermissionAsync(CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        if (!ErpPermissions.IsKnown(actor.Permission))
            throw new AiKnowledgeException(503, AiKnowledgeProblemCodesV1.PermissionUnregistered);
        var granted = await authority.RevalidatePermissionScopeAsync(actor.Permission, actor.CompanyId, cancellationToken);
        RequireStill(actor);
        if (granted is not { IsGranted: true, IsCompanyWide: true }) throw new AiKnowledgeException(403, "forbidden");
        return actor with { RoleCodes = granted.RoleCodes.Order(StringComparer.Ordinal).ToArray() };
    }

    private async Task RequireStillPermissionAsync(Scope scope, CancellationToken cancellationToken)
    {
        RequireStill(scope);
        var granted = await authority.RevalidatePermissionScopeAsync(scope.Permission, scope.CompanyId, cancellationToken);
        RequireStill(scope);
        if (granted is not { IsGranted: true, IsCompanyWide: true }) throw new AiKnowledgeException(403, "forbidden");
    }

    // ---- Idempotency, save, audit ------------------------------------------------------------------------------------------

    private static long ParseVersion(string? value) =>
        value is not null && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version >= 1
            && value == version.ToString(CultureInfo.InvariantCulture)
            ? version
            : throw new AiKnowledgeException(400, AiKnowledgeProblemCodesV1.ValidationFailed);

    private static string Hash(params object?[] parts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts, HashJson))));

    private static string KeyHash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private async Task<Replay?> FindReplayAsync(Scope scope, AiKnowledgeCommandMetadataV1 metadata, string command, string requestHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 128
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || metadata.CorrelationId.Length > 128 || metadata.CausationId?.Length > 128)
            throw new AiKnowledgeException(400, AiKnowledgeProblemCodesV1.CommandMetadataInvalid);
        var keyHash = KeyHash(metadata.IdempotencyKey);
        var attempt = await database.Attempts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == scope.CompanyId && row.ActorUserId == scope.ActorUserId && row.IdempotencyKeySha256 == keyHash, cancellationToken);
        RequireStill(scope);
        if (attempt is null) return null;
        if (attempt.Command != command || attempt.RequestSha256 != requestHash)
            throw new AiKnowledgeException(409, AiKnowledgeProblemCodesV1.SameKeyPayloadMismatch);
        if (attempt.Status == 409)
            throw new AiKnowledgeException(409, attempt.RefusalCode!, isReplay: true, attempt.CorrelationId);
        return new Replay(attempt.TargetId, attempt.CorrelationId);
    }

    private void AddAttempt(Scope scope, AiKnowledgeCommandMetadataV1 metadata, string command, string requestHash, Guid targetId,
        int status, string? refusalCode, DateTimeOffset now) => database.Attempts.Add(new AiKnowledgeCommandAttemptRow
        {
            CompanyId = scope.CompanyId, ActorUserId = scope.ActorUserId, IdempotencyKeySha256 = KeyHash(metadata.IdempotencyKey),
            Command = command, RequestSha256 = requestHash, TargetId = targetId, Status = status, RefusalCode = refusalCode,
            CorrelationId = metadata.CorrelationId, RecordedAtUtc = now,
        });

    /// <summary>A refusal of state is durable: recorded with its key, then thrown; a lost race answers with the winner.</summary>
    private async Task<AiKnowledgeException> TerminalAsync(Scope scope, AiKnowledgeCommandMetadataV1 metadata, string command,
        string requestHash, Guid targetId, string code, CancellationToken cancellationToken)
    {
        RequireStill(scope);
        database.ChangeTracker.Clear();
        AddAttempt(scope, metadata, command, requestHash, targetId, 409, code, time.GetUtcNow());
        var saved = await SaveAsync(scope, metadata, command, requestHash, cancellationToken);
        return saved is null ? new AiKnowledgeException(409, code)
            : new AiKnowledgeException(409, AiKnowledgeProblemCodesV1.ConcurrentChange);
    }

    /// <summary>
    /// One SaveChanges in one transaction. A lost race on the attempt key, the active-approval index or the approval version
    /// is answered with the winner's stored answer (replay) when the same key won, otherwise 409 <c>concurrent_change</c>.
    /// </summary>
    private async Task<Replay?> SaveAsync(Scope scope, AiKnowledgeCommandMetadataV1 metadata, string command, string requestHash,
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
            var replay = await FindReplayAsync(scope, metadata, command, requestHash, cancellationToken);
            return replay ?? throw new AiKnowledgeException(409, AiKnowledgeProblemCodesV1.ConcurrentChange);
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    private void Audit(Scope scope, AiKnowledgeCommandMetadataV1 metadata, string eventType, Guid entityId, string operation,
        long? before, long? after, string beforeJson, string afterJson, string reason) =>
        audit.Enqueue(new AuditEventDraft(scope.CompanyId, eventType, "AiKnowledge.GroupPackageApproval", entityId.ToString("D"), operation,
            before, after, AuditActorTypes.User, scope.ActorId, scope.Permission, scope.RoleCodes, beforeJson, afterJson, reason, null,
            AuditAiInvolvement.None, null, correlation.IsResolved ? correlation.SourceChannel : "api",
            correlation.IsResolved ? correlation.ClientIpAddress : null, correlation.IsResolved ? correlation.UserAgent : null,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));

    private static string ApprovalAudit(AiKnowledgeApprovalRow row) => JsonSerializer.Serialize(new
    {
        scope = AiKnowledgeApprovalScopesV1.Group, row.Id, row.OriginCompanyId, row.WithdrawnOriginCompanyId, sourceKind = AiKnowledgeSourceKindsV1.Wire((AiKnowledgeSourceKindV1)row.SourceKind), row.PackageSha256, row.Status,
        row.Version, row.ApprovedByUserId, row.ApprovedAtUtc, row.WithdrawnByUserId, row.WithdrawnAtUtc,
        withdrawReasonLength = row.WithdrawReason?.Length,
    }, HashJson);

    private async Task<AiKnowledgeApprovalViewV1> ViewAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        var row = await database.Approvals.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == approvalId, cancellationToken)
            ?? throw new AiKnowledgeException(503, "service_unavailable");
        return View(row);
    }

    internal static AiKnowledgeApprovalViewV1 View(AiKnowledgeApprovalRow row) => new(row.Id, AiKnowledgeApprovalScopesV1.Group,
        row.OriginCompanyId,
        AiKnowledgeSourceKindsV1.Wire((AiKnowledgeSourceKindV1)row.SourceKind), row.PackageSha256,
        AiKnowledgeApprovalV1.WireStatus((AiKnowledgeApprovalStatusV1)row.Status), AiKnowledgeApprovalV1.WireVersion(row.Version),
        row.ApprovedByUserId, row.ApprovedAtUtc, row.WithdrawnByUserId, row.WithdrawnOriginCompanyId, row.WithdrawnAtUtc,
        row.WithdrawReason);

    [GeneratedRegex(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    private sealed record Scope(Guid CompanyId, string ActorId, Guid ActorUserId, string Permission, IReadOnlyList<string> RoleCodes);

    private sealed record Replay(Guid TargetId, string CorrelationId);
}

/// <summary>
/// Read port for the AI tools: is the exact package version approved group-wide (AI-D46)? A store failure answers
/// <see cref="AiKnowledgeApprovalStateV1.Unavailable"/> (fail-closed, never "approved").
/// </summary>
public sealed class AiKnowledgeApprovalReaderV1(AiKnowledgeDbContext database) : IAiKnowledgeApprovalReaderV1
{
    public async Task<AiKnowledgeApprovalStateV1> GetStateAsync(AiKnowledgeSourceKindV1 kind, string packageSha256,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(packageSha256)) return AiKnowledgeApprovalStateV1.NotApproved;
        try
        {
            var approved = await database.Approvals.AsNoTracking().AnyAsync(row =>
                row.SourceKind == (int)kind && row.PackageSha256 == packageSha256
                && row.Status == (int)AiKnowledgeApprovalStatusV1.Approved, cancellationToken);
            return approved ? AiKnowledgeApprovalStateV1.Approved : AiKnowledgeApprovalStateV1.NotApproved;
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException or DbUpdateException
                                              or InvalidOperationException or TimeoutException)
        {
            return AiKnowledgeApprovalStateV1.Unavailable;
        }
    }
}
