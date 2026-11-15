using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Outbox;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>
/// The K3 skeleton of the ID-3 commands: the permission fence (measured, then re-measured fresh right before every
/// save), the expected version, one Idempotency-Key per company and command (the stored terminal answer replays; another
/// body under the key is 409), correlation/causation, the company applock, one save holding the change, its audit, its
/// outbox and its terminal answer, and the fail-closed <c>identity.permission.changed</c> security event before commit.
/// </summary>
internal sealed partial class IdentityAccessAssignmentService
{
    internal const string RoleAssignmentAggregateType = "identity.role-assignment";

    internal const string RoleAssignmentEntityType = "Identity.RoleAssignment";

    internal const string GrantRequestEntityType = "Identity.AccessGrantRequest";

    internal const string AssignmentGrantedEvent = "identity.role-assignment.granted.v1";

    internal const string AssignmentRevokedEvent = "identity.role-assignment.revoked.v1";

    internal const string RequestCreatedEvent = "identity.access-grant-request.created.v1";

    internal const string RequestApprovedEvent = "identity.access-grant-request.approved.v1";

    internal const string RequestRejectedEvent = "identity.access-grant-request.rejected.v1";

    internal const string RequestWithdrawnEvent = "identity.access-grant-request.withdrawn.v1";

    private const int CommandExecutionSchemaVersion = 1;

    private const int MinimumExpectedVersion = 1;

    private const int SqlUniqueIndexViolation = 2601;

    private const int SqlUniqueConstraintViolation = 2627;

    private const string EmptyImage = "{}";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>What a public method asks the skeleton to run before any read of business rows.</summary>
    private sealed record Intent(
        string Name,
        string Operation,
        string TargetType,
        string TargetId,
        string Permission,
        object Payload,
        string? ExpectedVersionText,
        bool VersionRequired,
        IReadOnlyList<RefusedAttemptFieldErrorV1> FieldErrors);

    /// <summary>A fenced, hashed command ready for the locked body.</summary>
    private sealed record Command(
        string Name,
        string Operation,
        string TargetType,
        string TargetId,
        string Permission,
        Guid CompanyId,
        Guid ActorUserId,
        PermissionScope Scope,
        string KeyHash,
        string RequestHash,
        long? ExpectedVersion,
        IdentityAccessCommandMetadataV1 Metadata);

    private sealed record Prepared(Command? Command, IdentityAccessCommandResultV1? Early);

    /// <summary>
    /// A staged business change: the entities are already on the context; the audits are enqueued only right before the
    /// save (the request's audit collector is shared); <paramref name="OnConflict"/> is the refusal stored as the key's answer
    /// when a concurrent writer wins the row.
    /// </summary>
    private sealed record Staged(
        IdentityAccessCommandAnswerV1 Answer,
        IReadOnlyList<AuditEventDraft> Audits,
        SecurityEventV1? SecurityEvent,
        IdentityAccessCommandAnswerV1 OnConflict);

    private sealed record Step(IdentityAccessCommandAnswerV1? Refusal, Staged? Change)
    {
        public static Step Refuse(IdentityAccessCommandAnswerV1 refusal) => new(refusal, null);

        public static Step Changed(Staged change) => new(null, change);
    }

    private Guid RequireContextCompany()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new UnauthorizedAccessException();
        }

        return companyContext.CompanyId;
    }

    private async Task<PermissionScope> RequireAsync(string permission, Guid companyId, CancellationToken cancellationToken)
    {
        if (!currentUser.CanAccessCompany(companyId))
        {
            throw new UnauthorizedAccessException();
        }

        var scope = await authorization.GetPermissionScopeAsync(permission, companyId, cancellationToken);
        return scope is { IsGranted: true, IsCompanyWide: true } ? scope : throw new UnauthorizedAccessException();
    }

    /// <summary>The same measurement, fresh from the authority rows (a grant revoked meanwhile is seen).</summary>
    private async Task RequireStillAsync(string permission, Guid companyId, CancellationToken cancellationToken)
    {
        if (!currentUser.CanAccessCompany(companyId) || companyContext.CompanyId != companyId)
        {
            throw new UnauthorizedAccessException();
        }

        var scope = await revalidation.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (scope is not { IsGranted: true, IsCompanyWide: true })
        {
            throw new UnauthorizedAccessException();
        }
    }

    private void ValidateMetadata(IdentityAccessCommandMetadataV1 metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!companyContext.IsResolved || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Kimlik yetki komutu bağlamı geçersiz.");
        }
    }

    /// <summary>
    /// Fence, version, hashes and replay, in this order (k3 skeleton). Nothing here reads a business row and nothing here
    /// holds the company lock.
    /// </summary>
    private async Task<Prepared> BeginAsync(
        Intent intent,
        IdentityAccessCommandMetadataV1 metadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var companyId = RequireContextCompany();
        ValidateMetadata(metadata);
        if (!Guid.TryParse(metadata.ActorId, out var actorUserId) || actorUserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException();
        }

        if (!ErpPermissions.IsKnown(intent.Permission))
        {
            // A setup gap, never a grant: answered code-less like every 503 and stored nowhere.
            return new Prepared(null, Unstored(Answers.Unavailable(), metadata));
        }

        var scope = await RequireAsync(intent.Permission, companyId, cancellationToken);

        long? expectedVersion = null;
        var fieldErrors = new List<RefusedAttemptFieldErrorV1>(intent.FieldErrors);
        if (intent.VersionRequired)
        {
            if (TryParseVersion(intent.ExpectedVersionText, MinimumExpectedVersion, out var parsed))
            {
                expectedVersion = parsed;
            }
            else
            {
                fieldErrors.Insert(0, new RefusedAttemptFieldErrorV1("expectedVersion", FieldCodes.Invalid));
            }
        }

        if (fieldErrors.Count > 0)
        {
            var invalid = Answers.Invalid();
            await refusedAttempts.RecordStandaloneAsync(
                Attempt(intent, companyId, metadata, scope, expectedVersion, invalid, RefusedAttemptClassesV1.Validation,
                    fieldErrors),
                cancellationToken);
            return new Prepared(null, Unstored(invalid, metadata));
        }

        var command = new Command(
            intent.Name,
            intent.Operation,
            intent.TargetType,
            intent.TargetId,
            intent.Permission,
            companyId,
            actorUserId,
            scope,
            IdempotencyHash.ComputeKey(metadata.IdempotencyKey),
            IdempotencyHash.ComputePayload(new { Command = intent.Name, Target = intent.TargetId, Request = intent.Payload }),
            expectedVersion,
            metadata);

        var execution = await FindExecutionAsync(command, cancellationToken);
        if (execution is null)
        {
            return new Prepared(command, null);
        }

        await RequireStillAsync(command.Permission, companyId, cancellationToken);
        return new Prepared(null, await ReplayAsync(execution, command, cancellationToken));
    }

    /// <summary>
    /// The locked body: the company applock (<see cref="IdentityAdminLock"/>), a second replay check under it, the
    /// business decision, then one save of the change, its audit, its outbox and its terminal answer, the fail-closed
    /// security event, and the commit. A business refusal is stored as the key's terminal answer with its refused-attempt
    /// audit row in the same save.
    /// </summary>
    private async Task<IdentityAccessCommandResultV1> RunLockedAsync(
        Command command,
        Func<DateTimeOffset, Task<Step>> body,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        if (!await IdentityAdminLock.TryAcquireAsync(database, command.CompanyId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Done(command, Unstored(Answers.Unavailable(), command.Metadata));
        }

        var raced = await FindExecutionAsync(command, cancellationToken);
        if (raced is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RequireStillAsync(command.Permission, command.CompanyId, cancellationToken);
            return Done(command, await ReplayAsync(raced, command, cancellationToken));
        }

        var now = timeProvider.GetUtcNow().ToUniversalTime();
        var step = await body(now);
        if (step.Refusal is { } refusal)
        {
            return Done(command, await StoreRefusalAsync(command, refusal, transaction, cancellationToken));
        }

        var change = step.Change ?? throw new InvalidOperationException("Kimlik yetki komutu sonuç üretmedi.");
        await RequireStillAsync(command.Permission, command.CompanyId, cancellationToken);
        foreach (var audit in change.Audits)
        {
            auditEvents.Enqueue(audit);
        }

        AddExecution(command, change.Answer, now);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(command, cancellationToken);
            if (winner is not null)
            {
                return Done(command, await ReplayAsync(winner, command, cancellationToken));
            }

            return Done(command, await StoreStandaloneRefusalAsync(command, change.OnConflict, cancellationToken));
        }

        if (change.SecurityEvent is { } securityEvent)
        {
            var written = await securityEvents.WriteAsync(securityEvent, cancellationToken);
            if (!written.CallerMayContinue)
            {
                // identity.permission.changed is fail-closed: no grant or revocation stands without its security event.
                await transaction.RollbackAsync(cancellationToken);
                database.ChangeTracker.Clear();
                return Done(command, Unstored(Answers.Unavailable(), command.Metadata));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return Done(command, new IdentityAccessCommandResultV1(change.Answer, false, command.Metadata.CorrelationId));
    }

    /// <summary>A business refusal under the lock: the key's terminal answer and its refused-attempt row, one save.</summary>
    private async Task<IdentityAccessCommandResultV1> StoreRefusalAsync(
        Command command,
        IdentityAccessCommandAnswerV1 refusal,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await RequireStillAsync(command.Permission, command.CompanyId, cancellationToken);
        RecordTerminal(command, refusal);
        AddExecution(command, refusal, timeProvider.GetUtcNow().ToUniversalTime());
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(command, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return await ReplayAsync(winner, command, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new IdentityAccessCommandResultV1(refusal, false, command.Metadata.CorrelationId);
    }

    /// <summary>
    /// The losing writer's refusal after its transaction rolled back: stored alone as the key's answer (a same-key winner
    /// replays), with the losing refused attempt as a separate short write.
    /// </summary>
    private async Task<IdentityAccessCommandResultV1> StoreStandaloneRefusalAsync(
        Command command,
        IdentityAccessCommandAnswerV1 refusal,
        CancellationToken cancellationToken)
    {
        await RequireStillAsync(command.Permission, command.CompanyId, cancellationToken);
        AddExecution(command, refusal, timeProvider.GetUtcNow().ToUniversalTime());
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(command, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return await ReplayAsync(winner, command, cancellationToken);
        }

        if (IsRecordable(refusal))
        {
            await refusedAttempts.RecordStandaloneAsync(
                Attempt(command, refusal, RefusalClassOf(refusal), []),
                cancellationToken);
        }

        return new IdentityAccessCommandResultV1(refusal, false, command.Metadata.CorrelationId);
    }

    private Task<CommandExecution?> FindExecutionAsync(Command command, CancellationToken cancellationToken) =>
        database.CommandExecutions.AsNoTracking()
            .SingleOrDefaultAsync(
                execution => execution.CompanyId == command.CompanyId
                    && execution.CommandName == command.Name
                    && execution.IdempotencyKeyHash == command.KeyHash,
                cancellationToken);

    /// <summary>
    /// The stored answer of the key. Another actor or another body under the same key is 409 (recorded as an Idempotency
    /// refusal, not stored); a replayed coded refusal is only counted (KIM-D11).
    /// </summary>
    private async Task<IdentityAccessCommandResultV1> ReplayAsync(
        CommandExecution execution,
        Command command,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(execution.ActorId, command.Metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, command.RequestHash, StringComparison.Ordinal))
        {
            var conflict = Answers.IdempotencyConflict();
            await refusedAttempts.RecordStandaloneAsync(
                Attempt(command, conflict, RefusedAttemptClassesV1.Idempotency, []),
                cancellationToken);
            return new IdentityAccessCommandResultV1(conflict, false, command.Metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<IdentityAccessCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu kimlik yetki komut sözleşmesiyle uyumsuz.");
        }

        if (IsRecordable(answer))
        {
            refusedAttempts.NoteReplayedRefusal(RefusalClassOf(answer));
        }

        return new IdentityAccessCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(Command command, IdentityAccessCommandAnswerV1 answer, DateTimeOffset completedAtUtc) =>
        database.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
            Guid.NewGuid(),
            command.CompanyId,
            command.Name,
            command.KeyHash,
            command.RequestHash,
            command.Metadata.ActorId,
            Outcome(answer),
            answer.StatusCode,
            JsonSerializer.Serialize(answer, Json),
            command.Metadata.CorrelationId,
            completedAtUtc,
            CommandExecutionSchemaVersion)));

    /// <summary>A coded business 4xx goes to the audit log; 401/403 belong to the security channel and 404 is code-less.</summary>
    private void RecordTerminal(Command command, IdentityAccessCommandAnswerV1 refusal)
    {
        if (IsRecordable(refusal))
        {
            refusedAttempts.Record(Attempt(command, refusal, RefusalClassOf(refusal), []));
        }
    }

    private static bool IsRecordable(IdentityAccessCommandAnswerV1 answer) =>
        answer.Code is not null && answer.StatusCode is >= 400 and < 500 and not 401 and not 403;

    private static string RefusalClassOf(IdentityAccessCommandAnswerV1 answer) => answer.Code switch
    {
        IdentityAccessProblemCodesV1.VersionConflict => RefusedAttemptClassesV1.Concurrency,
        IdentityAccessProblemCodesV1.IdempotencyConflict => RefusedAttemptClassesV1.Idempotency,
        IdentityAccessProblemCodesV1.RequestInvalid => RefusedAttemptClassesV1.Validation,
        _ => RefusedAttemptClassesV1.BusinessRule,
    };

    private static RefusedAttemptV1 Attempt(
        Command command,
        IdentityAccessCommandAnswerV1 refusal,
        string refusalClass,
        IReadOnlyList<RefusedAttemptFieldErrorV1> fieldErrors) => new()
        {
            CompanyId = command.CompanyId,
            CommandName = command.Name,
            Operation = command.Operation,
            TargetType = command.TargetType,
            TargetId = command.TargetId,
            ExpectedVersion = command.ExpectedVersion,
            ActorType = AuditActorTypes.User,
            ActorId = command.Metadata.ActorId,
            PermissionCode = command.Permission,
            RoleCodes = SortedRoleCodes(command.Scope),
            HttpStatus = refusal.StatusCode,
            ProblemCode = refusal.Code!,
            FieldErrors = fieldErrors,
            RefusalClass = refusalClass,
            CorrelationId = command.Metadata.CorrelationId,
            CausationId = command.Metadata.CausationId,
            SourceChannel = command.Metadata.SourceChannel,
            IdempotencyKey = command.Metadata.IdempotencyKey,
        };

    private static RefusedAttemptV1 Attempt(
        Intent intent,
        Guid companyId,
        IdentityAccessCommandMetadataV1 metadata,
        PermissionScope scope,
        long? expectedVersion,
        IdentityAccessCommandAnswerV1 refusal,
        string refusalClass,
        IReadOnlyList<RefusedAttemptFieldErrorV1> fieldErrors) => new()
        {
            CompanyId = companyId,
            CommandName = intent.Name,
            Operation = intent.Operation,
            TargetType = intent.TargetType,
            TargetId = intent.TargetId,
            ExpectedVersion = expectedVersion,
            ActorType = AuditActorTypes.User,
            ActorId = metadata.ActorId,
            PermissionCode = intent.Permission,
            RoleCodes = SortedRoleCodes(scope),
            HttpStatus = refusal.StatusCode,
            ProblemCode = refusal.Code!,
            FieldErrors = fieldErrors,
            RefusalClass = refusalClass,
            CorrelationId = metadata.CorrelationId,
            CausationId = metadata.CausationId,
            SourceChannel = metadata.SourceChannel,
            IdempotencyKey = metadata.IdempotencyKey,
        };

    private static string[] SortedRoleCodes(PermissionScope scope) =>
        scope.RoleCodes.Order(StringComparer.Ordinal).ToArray();

    /// <summary>One audit event of a real change, in the command's write envelope.</summary>
    private static AuditEventDraft Audit(
        Command command,
        string eventType,
        string entityType,
        Guid entityId,
        string operation,
        long? versionBefore,
        long versionAfter,
        object? before,
        object after,
        string reason,
        string? approvalContextJson) => new(
            command.CompanyId,
            eventType,
            entityType,
            entityId.ToString("D", CultureInfo.InvariantCulture),
            operation,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            command.Metadata.ActorId,
            command.Permission,
            SortedRoleCodes(command.Scope),
            before is null ? EmptyImage : JsonSerializer.Serialize(before, Json),
            JsonSerializer.Serialize(after, Json),
            reason,
            approvalContextJson,
            AuditAiInvolvement.None,
            AiContextJson: null,
            command.Metadata.SourceChannel,
            command.Metadata.ClientIpAddress,
            command.Metadata.UserAgent,
            command.Metadata.CorrelationId,
            command.Metadata.CausationId,
            command.Metadata.IdempotencyKey);

    /// <summary>The approval context of an assignment a Critical request closed (KIM-D1 / KIM-D5).</summary>
    private static string ApprovalContext(AccessGrantApprovalMode mode, Guid requestId, Guid deciderUserId) =>
        JsonSerializer.Serialize(
            new
            {
                approvalMode = mode.ToString(),
                requestId = requestId.ToString("D", CultureInfo.InvariantCulture),
                decidedByUserId = deciderUserId.ToString("D", CultureInfo.InvariantCulture),
            },
            Json);

    /// <summary>
    /// <c>identity.permission.changed.v1</c>: one outbox message per permission of the role, in the same save as the
    /// assignment change (the aggregate is the assignment, its version the one after the change).
    /// </summary>
    private void AddPermissionChanges(
        Command command,
        Guid userId,
        Guid assignmentId,
        long assignmentVersion,
        IReadOnlyList<string> permissions,
        string change,
        Guid? requestId,
        DateTimeOffset now)
    {
        foreach (var permission in permissions)
        {
            var payload = new IdentityPermissionChangedV1(
                command.CompanyId,
                userId,
                permission,
                change,
                command.ActorUserId,
                requestId,
                now);
            database.OutboxMessages.Add(new IdentityOutboxMessage(
                Guid.NewGuid(),
                command.CompanyId,
                IdentityPermissionChangedV1.EventType,
                IdentityPermissionChangedV1.SchemaVersion,
                command.Name,
                RoleAssignmentAggregateType,
                assignmentId,
                assignmentVersion,
                JsonSerializer.Serialize(payload, Json),
                now,
                command.Metadata.CorrelationId,
                command.Metadata.CausationId));
        }
    }

    /// <summary>One fail-closed security event per grant or revocation command (KIM-D10).</summary>
    private static SecurityEventV1 PermissionChangedEvent(Command command, DateTimeOffset now) => new()
    {
        EventId = Guid.NewGuid(),
        OccurredAtUtc = now.ToUniversalTime(),
        ScopeKind = SecurityEventScopeKindsV1.Company,
        CompanyId = command.CompanyId,
        ActorType = SecurityEventActorTypesV1.User,
        ActorId = command.ActorUserId.ToString("D", CultureInfo.InvariantCulture),
        EventType = SecurityEventTypesV1.PermissionChanged,
        Outcome = SecurityEventOutcomesV1.Allowed,
        Severity = SecurityEventSeveritiesV1.High,
        PermissionCode = command.Permission,
        CorrelationId = command.Metadata.CorrelationId,
        CausationId = command.Metadata.CausationId,
        SourceChannel = command.Metadata.SourceChannel,
    };

    private static string Outcome(IdentityAccessCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
    {
        200 or 201 or 204 => "Succeeded",
        404 => "NotFound",
        _ => "Refused",
    };

    private IdentityAccessCommandResultV1 Done(Command command, IdentityAccessCommandResultV1 result)
    {
        logger.LogInformation(
            "Identity access command completed. Command={Command} CompanyId={CompanyId} Target={Target} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
            command.Name, command.CompanyId, command.TargetId, command.Metadata.ActorId, command.Metadata.CorrelationId,
            result.Answer.StatusCode, result.Answer.Code, result.IsReplay);
        return result;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: SqlUniqueIndexViolation or SqlUniqueConstraintViolation };

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A version as decimal text: digits only, no sign, no leading zero, within Int64.</summary>
    private static bool TryParseVersion(string? text, long minimum, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version >= minimum;
    }

    private static IdentityAccessCommandResultV1 Unstored(
        IdentityAccessCommandAnswerV1 answer,
        IdentityAccessCommandMetadataV1 metadata) =>
        new(answer, false, metadata.CorrelationId);

    private static class FieldCodes
    {
        public const string Required = "required";

        public const string Invalid = "invalid";
    }

    private static class Operations
    {
        public const string Assign = "Assign";

        public const string Revoke = "Revoke";

        public const string Create = "Create";

        public const string Approve = "Approve";

        public const string Reject = "Reject";

        public const string Withdraw = "Withdraw";
    }

    private static class Answers
    {
        public static IdentityAccessCommandAnswerV1 Created(Guid id, long version, string status, Guid? assignmentId) =>
            new(201, null, "Kaydedildi.", "Değişiklik kaydedildi.",
                new IdentityAccessCommandOutcomeV1(id, Text(version), status, assignmentId));

        public static IdentityAccessCommandAnswerV1 Ok(Guid id, long version, string status, Guid? assignmentId) =>
            new(200, null, "Kaydedildi.", "Değişiklik kaydedildi.",
                new IdentityAccessCommandOutcomeV1(id, Text(version), status, assignmentId));

        public static IdentityAccessCommandAnswerV1 NotFound() =>
            new(404, null, "Kayıt bulunamadı.", "İstenen kayıt bu şirkette bulunamadı.");

        public static IdentityAccessCommandAnswerV1 Unavailable() =>
            new(503, null, "Hizmet kullanılamıyor.", "İşlem şu anda tamamlanamadı; biraz sonra yeniden deneyin.");

        public static IdentityAccessCommandAnswerV1 Invalid() =>
            Problem(400, IdentityAccessProblemCodesV1.RequestInvalid, "İstek geçersiz.",
                "İstekteki alanlardan biri veya birkaçı geçerli değil.");

        public static IdentityAccessCommandAnswerV1 IdempotencyConflict() =>
            Problem(409, IdentityAccessProblemCodesV1.IdempotencyConflict, "İşlem anahtarı kullanılmış.",
                "Bu Idempotency-Key başka bir istekle kullanıldı.");

        public static IdentityAccessCommandAnswerV1 VersionConflict() =>
            Problem(409, IdentityAccessProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static IdentityAccessCommandAnswerV1 SecondApprovalRequired() =>
            Problem(409, IdentityAccessProblemCodesV1.SecondApprovalRequired, "İkinci onay gerekli.",
                "Kritik yetki içeren rol doğrudan atanamaz; yetki talebi açın ve ikinci bir kişinin onayını bekleyin.");

        public static IdentityAccessCommandAnswerV1 DuplicateActiveAssignment() =>
            Problem(409, IdentityAccessProblemCodesV1.DuplicateActiveAssignment, "Atama zaten var.",
                "Kullanıcının bu şirkette bu rol için süresi dolmamış etkin bir ataması var.");

        public static IdentityAccessCommandAnswerV1 AssignmentNotActive() =>
            Problem(409, IdentityAccessProblemCodesV1.AssignmentNotActive, "Atama etkin değil.",
                "Yalnız etkin rol ataması geri alınabilir.");

        public static IdentityAccessCommandAnswerV1 RoleNotAssignable() =>
            Problem(409, IdentityAccessProblemCodesV1.RoleNotAssignable, "Rol atanamaz.",
                "Rol bulunamadı veya pasif.");

        public static IdentityAccessCommandAnswerV1 UserNotAssignable() =>
            Problem(409, IdentityAccessProblemCodesV1.UserNotAssignable, "Kullanıcıya atanamaz.",
                "Kullanıcı bulunamadı veya pasif.");

        public static IdentityAccessCommandAnswerV1 LastAdminRequired() =>
            Problem(409, IdentityAccessProblemCodesV1.LastAdminRequired, "Son yönetici.",
                "Şirketin son etkin yönetici ataması geri alınamaz; önce başka bir yöneticiye rol verin.");

        public static IdentityAccessCommandAnswerV1 DuplicatePendingRequest() =>
            Problem(409, IdentityAccessProblemCodesV1.DuplicatePendingRequest, "Bekleyen talep var.",
                "Bu kullanıcı ve rol için karar bekleyen bir yetki talebi zaten var.");

        public static IdentityAccessCommandAnswerV1 SelfApprovalRefused() =>
            Problem(409, IdentityAccessProblemCodesV1.SelfApprovalRefused, "Kendi talebinize karar veremezsiniz.",
                "Kritik yetki talebini talep eden veya hedef kullanıcı onaylayamaz ya da reddedemez.");

        public static IdentityAccessCommandAnswerV1 RequestNotPending() =>
            Problem(409, IdentityAccessProblemCodesV1.RequestNotPending, "Talep karara bağlanmış.",
                "Yetki talebi artık karar beklemiyor.");

        public static IdentityAccessCommandAnswerV1 ValidityElapsed() =>
            Problem(409, IdentityAccessProblemCodesV1.ValidityElapsed, "Geçerlilik süresi geçmiş.",
                "Yetkinin bitiş zamanı geçmiş veya başlangıçtan sonra değil.");

        public static IdentityAccessCommandAnswerV1 RoleNotCritical() =>
            Problem(409, IdentityAccessProblemCodesV1.RoleNotCritical, "Rol kritik değil.",
                "Kritik yetki içermeyen rol için talep açılmaz; rolü doğrudan atayın.");

        public static IdentityAccessCommandAnswerV1 NotRequester() =>
            Problem(409, IdentityAccessProblemCodesV1.NotRequester, "Talep sizin değil.",
                "Yetki talebini yalnız talep eden geri çekebilir.");

        public static IdentityAccessCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new(status, code, title, detail);
    }
}
