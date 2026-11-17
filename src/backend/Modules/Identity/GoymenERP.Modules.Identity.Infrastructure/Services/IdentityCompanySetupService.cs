using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.CompanySetup;
using GoymenERP.Modules.Identity.Domain.Outbox;
using GoymenERP.Modules.Identity.Infrastructure.Bootstrap;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>ID-3b. KIM-D56 explicit first-Admin selection shares the existing company Admin lock; opening events grant nothing.</summary>
public sealed class IdentityCompanySetupService(IdentityDbContext database, ICompanyContext companyContext,
    ICurrentUser currentUser, ICompanyLifecycleReadPortV1 lifecycle, IOptions<IdentityBootstrapOptions> options,
    ICompanySetupGroupAuthorityV1 groupAuthority, IAuditEventCollector auditEvents, IAuditTransactionWriterV1 auditWriter,
    IRefusedAttemptRecorderV1 refusedAttempts, ISecurityEventWriterV1 securityEvents, TimeProvider timeProvider)
    : IIdentityCompanySetupServiceV1, IIdentityFirstAdminCandidatesV1, IWorkInboxConsumerV1
{
    public const string ConsumerName = "identity.company-opened";
    public const string CommandName = "identity.company.first-admin.assign.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public async Task<bool> IsActiveFounderAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || !Guid.TryParse(currentUser.UserId, out var userId)) return false;
        return await database.BootstrapStates.AsNoTracking().AnyAsync(x => x.FounderUserId == userId, cancellationToken)
            && await database.Users.AsNoTracking().AnyAsync(x => x.Id == userId && x.IsActive, cancellationToken);
    }

    public static OrganizationCompanyOpenedV1 Read(WorkInboxEventV1 message, ICompanyContext context)
    {
        if (message.SourceName != OrganizationCompanyOpenedV1.SourceName || message.EventType != OrganizationCompanyOpenedV1.EventType
            || message.Version != 1 || message.EventId == Guid.Empty || !context.IsResolved || context.CompanyId != message.CompanyId
            || string.IsNullOrWhiteSpace(message.CorrelationId) || message.CorrelationId.Length > 128 || message.CausationId?.Length > 128) throw Poison("invalid_company_setup_envelope");
        OrganizationCompanyOpenedV1? payload;
        try { payload = JsonSerializer.Deserialize<OrganizationCompanyOpenedV1>(message.PayloadJson, Json); }
        catch (JsonException) { throw Poison("invalid_company_setup_payload"); }
        if (payload is null || payload.CompanyId != message.CompanyId || payload.OpenedByUserId == Guid.Empty
            || payload.OpenedAtUtc == default || payload.OpenedAtUtc.Offset != TimeSpan.Zero
            || payload.OpenedAtUtc != message.OccurredAtUtc || message.OccurredAtUtc.Offset != TimeSpan.Zero) throw Poison("invalid_company_setup_payload");
        return payload;
    }

    public async Task HandleAsync(WorkInboxEventV1 message, CancellationToken cancellationToken)
    {
        _ = Read(message, companyContext);
        var hash = Hash(message.PayloadJson);
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (!await IdentityAdminLock.TryAcquireAsync(database, message.CompanyId, cancellationToken))
                throw new InvalidOperationException("Company setup is busy.");
            var receipt = await database.CompanySetupInboxReceipts.AsNoTracking().SingleOrDefaultAsync(
                x => x.CompanyId == message.CompanyId && x.EventId == message.EventId, cancellationToken);
            if (receipt is not null)
            {
                if (receipt.PayloadHash != hash) throw Poison("company_setup_payload_mismatch");
                await transaction.RollbackAsync(cancellationToken);
                return;
            }
            // KIM-D56 supersedes the automatic opener grant. Retain the original event and dedup history,
            // including late/repeated pre-change events, without creating any request, assignment or activation event.
            database.CompanySetupInboxReceipts.Add(new(message.CompanyId, message.EventId, hash,
                timeProvider.GetUtcNow().ToUniversalTime()));
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { database.ChangeTracker.Clear(); auditWriter.DiscardPending(); }
    }

    public async Task<IReadOnlyList<FirstCompanyAdminCandidateV1>> ReadCandidatesAsync(string? search,
        CancellationToken cancellationToken = default)
    {
        var authority = await RequireGroupAsync(null, cancellationToken);
        var query = database.Users.AsNoTracking().Where(user => user.IsActive);
        var term = search?.Trim();
        if (term?.Length > 100) throw new ArgumentException("Candidate search is too long.");
        if (!string.IsNullOrEmpty(term)) query = query.Where(user => user.DisplayName.Contains(term));
        var rows = await query.OrderBy(user => user.DisplayName).ThenBy(user => user.Id).Take(100)
            .Select(user => new FirstCompanyAdminCandidateV1(user.Id, user.DisplayName)).ToListAsync(cancellationToken);
        await RequireStillAsync(authority, null, cancellationToken);
        return rows;
    }

    public async Task<IdentityAccessCommandResultV1> AssignFirstAdminAsync(AssignFirstCompanyAdminV1 request,
        IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var authority = await RequireGroupAsync(metadata.CompanyId, cancellationToken);
        if (metadata.ActorId != currentUser.UserId) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 200
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.SourceChannel))
            throw new ArgumentException("Invalid first Admin metadata.");
        var validRequest = request.UserId != Guid.Empty && request.ExpectedCompanyVersion >= 1;
        try { validRequest &= AccessGrantRequest.NormalizeReason(request.Reason) is not null; }
        catch (ArgumentException) { validRequest = false; }
        if (!validRequest)
        {
            var invalid = new IdentityAccessCommandAnswerV1(400, IdentityAccessProblemCodesV1.RequestInvalid,
                "İlk Admin isteği geçersiz.", "Kullanıcı, şirket sürümü ve gerekçeyi kontrol edin.");
            await refusedAttempts.RecordStandaloneAsync(Attempt(invalid, metadata, request.ExpectedCompanyVersion), cancellationToken);
            return Result(invalid, metadata);
        }
        var companyId = companyContext.CompanyId;
        var keyHash = Hash(metadata.IdempotencyKey);
        var requestHash = Hash(JsonSerializer.Serialize(request, Json));
        IDisposable? selectionProof = null;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (!await IdentityAdminLock.TryAcquireAsync(database, companyId, cancellationToken)) return Result(Unavailable(), metadata);
            await RequireStillAsync(authority, companyId, cancellationToken);
            var company = await lifecycle.ReadAsync(companyId, cancellationToken);
            await RequireStillAsync(authority, companyId, cancellationToken);
            if (company is null || company.CompanyId != companyId || company.Version < 1) return Result(Unavailable(), metadata);
            var previous = await database.CommandExecutions.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId
                && x.CommandName == CommandName && x.IdempotencyKeyHash == keyHash, cancellationToken);
            await RequireStillAsync(authority, companyId, cancellationToken);
            if (previous is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await RequireStillAsync(authority, companyId, cancellationToken);
                if (previous.ActorId != metadata.ActorId || previous.RequestHash != requestHash)
                {
                    var conflict = Refusal(IdentityAccessProblemCodesV1.IdempotencyConflict, "İstek anahtarı başka içerikte kullanıldı.");
                    await refusedAttempts.RecordStandaloneAsync(Attempt(conflict, metadata, request.ExpectedCompanyVersion), cancellationToken);
                    return Result(conflict, metadata);
                }
                var answer = JsonSerializer.Deserialize<IdentityAccessCommandAnswerV1>(previous.ResponseJson, Json)
                    ?? throw new InvalidOperationException("First Admin replay is unreadable.");
                if (answer.StatusCode != previous.HttpStatusCode || (answer.Code ?? "Succeeded") != previous.Outcome)
                    throw new InvalidOperationException("First Admin replay is inconsistent.");
                await RequireStillAsync(authority, companyId, cancellationToken);
                if (answer.StatusCode == 409) refusedAttempts.NoteReplayedRefusal(RefusalClass(answer));
                return new(answer, true, previous.OriginalCorrelationId);
            }
            var now = timeProvider.GetUtcNow().ToUniversalTime();
            var role = await AdminRoleAsync(cancellationToken);
            IdentityAccessCommandAnswerV1 terminal;
            if (await IdentityAdminLock.CountOtherEffectiveHoldersAsync(database, companyId, role.Id, Guid.Empty, now, cancellationToken) > 0)
                terminal = Refusal("identity.company.admin_already_assigned", "Şirkette etkin Admin zaten var.");
            else if (company.State != CompanyLifecycleStateV1.SetupPending)
                terminal = Refusal("organization.company.setup_pending_required", "Şirket kurulum bekliyor durumunda değil.");
            else if (company.Version != request.ExpectedCompanyVersion)
                terminal = Refusal(IdentityAccessProblemCodesV1.VersionConflict, "Şirket sürümü değişti; yenileyin.");
            else if (!await database.Users.AsNoTracking().AnyAsync(x => x.Id == request.UserId && x.IsActive, cancellationToken))
                terminal = Refusal(IdentityAccessProblemCodesV1.UserNotAssignable, "Hedef kullanıcı etkin değil.");
            else
            {
                await RequireStillAsync(authority, companyId, cancellationToken);
                selectionProof = database.BeginSelectedFirstAdminWrite(companyId, request.UserId, role.Id, authority);
                var assignment = await StageAssignmentAsync(Guid.Parse(metadata.ActorId), request.UserId, role.Id, role.Code,
                    AccessGrantApprovalMode.CompanySetupAuthority, request.Reason.Trim(), metadata, now, cancellationToken);
                terminal = new(201, null, string.Empty, string.Empty, new(assignment.Id, "1", "Active", assignment.Id));
            }
            await RequireStillAsync(authority, companyId, cancellationToken);
            if (terminal.StatusCode == 409) refusedAttempts.Record(Attempt(terminal, metadata, request.ExpectedCompanyVersion));
            database.CommandExecutions.Add(CommandExecution.Complete(new(Guid.NewGuid(), companyId, CommandName, keyHash,
                requestHash, metadata.ActorId, terminal.Code ?? "Succeeded", terminal.StatusCode, JsonSerializer.Serialize(terminal, Json),
                metadata.CorrelationId, now, 1)));
            await database.SaveChangesAsync(cancellationToken);
            await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
            if (terminal.StatusCode == 201) await WriteSecurityEventAsync(metadata, now, cancellationToken);
            await RequireStillAsync(authority, companyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result(terminal, metadata);
        }
        finally { selectionProof?.Dispose(); database.ChangeTracker.Clear(); auditWriter.DiscardPending(); }
    }

    private async Task<CompanySetupGroupAuthoritySnapshotV1> RequireGroupAsync(Guid? companyId, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !Guid.TryParse(currentUser.UserId, out var actor) || actor == Guid.Empty
            || companyId.HasValue && (!companyContext.IsResolved || companyContext.CompanyId != companyId.Value))
            throw new UnauthorizedAccessException();
        var snapshot = await groupAuthority.ReadAsync(CompanySetupGroupOperationV1.AssignFirstAdmin, cancellationToken);
        if (!snapshot.IsGranted || snapshot.Operation != CompanySetupGroupOperationV1.AssignFirstAdmin
            || snapshot.ActorUserId != actor || string.IsNullOrWhiteSpace(snapshot.Revision)
            || !currentUser.IsAuthenticated || !Guid.TryParse(currentUser.UserId, out var afterActor) || afterActor != actor
            || companyId.HasValue && (!companyContext.IsResolved || companyContext.CompanyId != companyId.Value))
            throw new UnauthorizedAccessException();
        return snapshot;
    }
    private async Task RequireStillAsync(CompanySetupGroupAuthoritySnapshotV1 original, Guid? companyId, CancellationToken cancellationToken)
    {
        var current = await RequireGroupAsync(companyId, cancellationToken);
        if (current.ActorUserId != original.ActorUserId || current.Revision != original.Revision)
            throw new UnauthorizedAccessException();
    }

    private async Task<(Guid Id, string Code)> AdminRoleAsync(CancellationToken cancellationToken)
    {
        var code = options.Value.AdminRoleCode;
        if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Configured Admin role is unavailable.");
        var role = await database.Roles.AsNoTracking().Where(x => x.Code == code && x.IsActive)
            .Select(x => new { x.Id, x.Code }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Configured active Admin role is unavailable.");
        return (role.Id, role.Code);
    }

    private async Task<RoleAssignment> StageAssignmentAsync(Guid actor, Guid user, Guid roleId, string roleCode,
        AccessGrantApprovalMode mode, string reason, IdentityAccessCommandMetadataV1 metadata, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var assignment = new RoleAssignment(Guid.NewGuid(), user, roleId, metadata.CompanyId, now, null, actor);
        assignment.AddScope(AccessScopeType.Company, metadata.CompanyId);
        var request = new AccessGrantRequest(Guid.NewGuid(), actor, user, roleId, metadata.CompanyId, true, now, now, null, reason);
        if (mode != AccessGrantApprovalMode.CompanySetupAuthority) throw new InvalidOperationException("Explicit group selection is required.");
        request.ApproveAsCompanySetupAuthority(actor, now, assignment.Id);
        database.AccessGrantRequests.Add(request);
        database.RoleAssignments.Add(assignment);
        var payload = new IdentityCompanyFirstAdminAssignedV1(metadata.CompanyId, user, assignment.Id, actor, now);
        database.OutboxMessages.Add(new(IdentityCompanyFirstAdminAssignedV1.EventId(metadata.CompanyId), metadata.CompanyId,
            IdentityCompanyFirstAdminAssignedV1.EventType, 1, CommandName, "identity.company-setup", metadata.CompanyId,
            1, JsonSerializer.Serialize(payload, Json), now, metadata.CorrelationId, metadata.CausationId));
        var permissions = await database.RolePermissions.AsNoTracking().Where(x => x.RoleId == roleId)
            .Select(x => x.PermissionCode).Distinct().ToListAsync(cancellationToken);
        foreach (var permission in permissions.Order(StringComparer.Ordinal))
            database.OutboxMessages.Add(new(Guid.NewGuid(), metadata.CompanyId, IdentityPermissionChangedV1.EventType, 1,
                CommandName, "identity.role-assignment", assignment.Id, 1,
                JsonSerializer.Serialize(new IdentityPermissionChangedV1(metadata.CompanyId, user, permission,
                    IdentityPermissionChangedV1.Granted, actor, request.Id, now), Json), now, metadata.CorrelationId, metadata.CausationId));
        var approval = JsonSerializer.Serialize(new { approvalMode = mode.ToString(), requestId = request.Id, decidedByUserId = actor }, Json);
        foreach (var item in new[] { (Type: "Identity.AccessGrantRequest", Id: request.Id, Event: "identity.access-grant-request.approved.v1", Op: "Approve", Image: (object)request),
            (Type: "Identity.RoleAssignment", Id: assignment.Id, Event: "identity.role-assignment.granted.v1", Op: "Assign", Image: (object)new { assignment.Id, assignment.CompanyId, user, roleId, roleCode, assignment.ValidUntilUtc }) })
            auditEvents.Enqueue(new(metadata.CompanyId, item.Event, item.Type, item.Id.ToString("D"), item.Op, null,
                item.Image is AccessGrantRequest ? request.Version : assignment.Version, AuditActorTypes.User, metadata.ActorId,
                ErpPermissions.IdentityCompanyFirstAdminAssign, [], "{}", JsonSerializer.Serialize(item.Image, Json), reason,
                approval, AuditAiInvolvement.None, null, metadata.SourceChannel, metadata.ClientIpAddress,
                metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        return assignment;
    }
    private async Task WriteSecurityEventAsync(IdentityAccessCommandMetadataV1 metadata, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var security = await securityEvents.WriteAsync(new SecurityEventV1
        {
            EventId = Guid.NewGuid(), OccurredAtUtc = now, ScopeKind = SecurityEventScopeKindsV1.Company,
            CompanyId = metadata.CompanyId, ActorType = SecurityEventActorTypesV1.User, ActorId = metadata.ActorId,
            EventType = SecurityEventTypesV1.PermissionChanged, Outcome = SecurityEventOutcomesV1.Allowed,
            Severity = SecurityEventSeveritiesV1.High, PermissionCode = ErpPermissions.IdentityCompanyFirstAdminAssign,
            CorrelationId = metadata.CorrelationId, CausationId = metadata.CausationId, SourceChannel = metadata.SourceChannel,
        }, cancellationToken);
        if (!security.CallerMayContinue) throw new InvalidOperationException("First Admin security event unavailable.");
    }
    private static RefusedAttemptV1 Attempt(IdentityAccessCommandAnswerV1 answer, IdentityAccessCommandMetadataV1 metadata, long version) => new()
    {
        CompanyId = metadata.CompanyId, CommandName = CommandName, Operation = "Assign", TargetType = "Organization.Company",
        TargetId = metadata.CompanyId.ToString("D"), ExpectedVersion = version, ActorType = AuditActorTypes.User,
        ActorId = metadata.ActorId, PermissionCode = ErpPermissions.IdentityCompanyFirstAdminAssign, RoleCodes = [],
        HttpStatus = answer.StatusCode, ProblemCode = answer.Code!, RefusalClass = RefusalClass(answer),
        CorrelationId = metadata.CorrelationId, CausationId = metadata.CausationId, SourceChannel = metadata.SourceChannel,
        IdempotencyKey = metadata.IdempotencyKey,
    };
    private static string RefusalClass(IdentityAccessCommandAnswerV1 answer) => answer.Code switch
    {
        IdentityAccessProblemCodesV1.VersionConflict => RefusedAttemptClassesV1.Concurrency,
        IdentityAccessProblemCodesV1.IdempotencyConflict => RefusedAttemptClassesV1.Idempotency,
        IdentityAccessProblemCodesV1.RequestInvalid => RefusedAttemptClassesV1.Validation,
        _ => RefusedAttemptClassesV1.BusinessRule,
    };
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static IdentityAccessCommandAnswerV1 Refusal(string code, string detail) => new(409, code, "İşlem uygulanamadı.", detail);
    private static IdentityAccessCommandAnswerV1 Unavailable() => new(503, null, "Kurulum doğrulanamadı.", "Daha sonra yeniden deneyin.");
    private static IdentityAccessCommandResultV1 Result(IdentityAccessCommandAnswerV1 answer, IdentityAccessCommandMetadataV1 metadata) => new(answer, false, metadata.CorrelationId);
    private static OutboxPermanentFailureException Poison(string code) => new(code, "Şirket kurulum olayı sözleşmeye uymuyor.");
}
