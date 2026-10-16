using System.Data;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed class SettingsManufacturingToleranceProfileCommandServiceV1(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IAuditEventCollector auditEvents) : ISettingsManufacturingToleranceProfileCommandServiceV1
{
    private const int CommandExecutionSchemaVersion = 1;
    private const int ActorMaximumLength = 128;
    private const int ReasonMaximumLength = 500;

    public Task<ManufacturingToleranceProfileCommandExecutionV1> PrepareAsync(
        PrepareManufacturingToleranceProfileCommandV1 command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCandidate(command.MaximumFirePercent, command.EffectiveFrom, command.EffectiveThrough);
        var fingerprint = new PrepareFingerprint(
            command.ProfileId,
            command.Process,
            command.ProfileVersion,
            command.MaximumFirePercent,
            command.EffectiveFrom,
            command.EffectiveThrough,
            command.Context.ExpectedStateVersion,
            command.Context.Reason);
        return ExecuteAsync(
            ManufacturingToleranceProfileActionsV1.Prepare,
            command.Context,
            fingerprint,
            async cancellationToken =>
            {
                var row = await FindLockedAsync(command.ProfileId, cancellationToken);
                ManufacturingToleranceProfileLifecycleV1 next;
                string beforeJson;
                long? beforeVersion;
                if (row is null)
                {
                    next = ManufacturingToleranceProfileLifecyclePolicyV1.Prepare(
                        new PrepareManufacturingToleranceProfileV1(
                            command.ProfileId,
                            command.Process,
                            command.ProfileVersion,
                            LifecycleContext(command.Context)));
                    row = NewRow(next, command.MaximumFirePercent, command.EffectiveFrom,
                        command.EffectiveThrough, command.Context);
                    database.ManufacturingToleranceProfileLifecycles.Add(row);
                    beforeJson = "{}";
                    beforeVersion = null;
                }
                else
                {
                    if (!string.Equals(row.Process, command.Process, StringComparison.Ordinal)
                        || row.ProfileVersion != command.ProfileVersion)
                    {
                        throw Failure(ManufacturingToleranceProfilePersistenceCodesV1.Invalid);
                    }

                    beforeJson = Serialize(row);
                    beforeVersion = row.StateVersion;
                    next = ManufacturingToleranceProfileLifecyclePolicyV1.Revise(
                        Lifecycle(row), LifecycleContext(command.Context));
                    ApplyLifecycle(row, next);
                    row.MaximumFirePercent = command.MaximumFirePercent;
                    row.EffectiveFrom = command.EffectiveFrom;
                    row.EffectiveThrough = command.EffectiveThrough;
                    ApplyPreparationEvidence(row, command.Context);
                }

                return new PendingTransition(row, beforeJson, "Prepare", beforeVersion);
            },
            cancellationToken);
    }

    public Task<ManufacturingToleranceProfileCommandExecutionV1> PrepareNextVersionAsync(
        PrepareNextManufacturingToleranceProfileVersionCommandV1 command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCandidate(command.MaximumFirePercent, command.EffectiveFrom, command.EffectiveThrough);
        var fingerprint = new PrepareNextFingerprint(
            command.PublishedProfileId,
            command.ProfileId,
            command.MaximumFirePercent,
            command.EffectiveFrom,
            command.EffectiveThrough,
            command.Context.ExpectedStateVersion,
            command.Context.Reason);
        return ExecuteAsync(
            ManufacturingToleranceProfileActionsV1.Prepare,
            command.Context,
            fingerprint,
            async cancellationToken =>
            {
                var published = await RequireLockedAsync(command.PublishedProfileId, cancellationToken);
                if (await FindLockedAsync(command.ProfileId, cancellationToken) is not null)
                {
                    throw Failure(ManufacturingToleranceProfilePersistenceCodesV1.Invalid);
                }

                var next = ManufacturingToleranceProfileLifecyclePolicyV1.PrepareNextVersion(
                    Lifecycle(published),
                    new PrepareNextManufacturingToleranceProfileVersionV1(
                        command.ProfileId,
                        LifecycleContext(command.Context)));
                var existingSuccessor = await FindSuccessorAsync(
                    next.Process, next.ProfileVersion, command.ProfileId, cancellationToken);
                if (existingSuccessor is not null)
                {
                    throw SuccessorVersionConflict(existingSuccessor);
                }

                var row = NewRow(next, command.MaximumFirePercent, command.EffectiveFrom,
                    command.EffectiveThrough, command.Context);
                database.ManufacturingToleranceProfileLifecycles.Add(row);
                return new PendingTransition(row, "{}", "PrepareNextVersion", EntityVersionBefore: null);
            },
            cancellationToken);
    }

    public Task<ManufacturingToleranceProfileCommandExecutionV1> ApproveAsync(
        TransitionManufacturingToleranceProfileCommandV1 command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return TransitionAsync(
            ManufacturingToleranceProfileActionsV1.Approve,
            command,
            ManufacturingToleranceProfileLifecyclePolicyV1.Approve,
            "Approve",
            cancellationToken);
    }

    public Task<ManufacturingToleranceProfileCommandExecutionV1> PublishAsync(
        TransitionManufacturingToleranceProfileCommandV1 command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return TransitionAsync(
            ManufacturingToleranceProfileActionsV1.Publish,
            command,
            ManufacturingToleranceProfileLifecyclePolicyV1.Publish,
            "Publish",
            cancellationToken,
            requirePublicationWindow: true);
    }

    private Task<ManufacturingToleranceProfileCommandExecutionV1> TransitionAsync(
        string action,
        TransitionManufacturingToleranceProfileCommandV1 command,
        Func<ManufacturingToleranceProfileLifecycleV1, ManufacturingToleranceProfileCommandContextV1,
            ManufacturingToleranceProfileLifecycleV1> transition,
        string operation,
        CancellationToken cancellationToken,
        bool requirePublicationWindow = false)
    {
        var fingerprint = new TransitionFingerprint(
            command.ProfileId,
            command.Context.ExpectedStateVersion,
            command.Context.Reason);
        return ExecuteAsync(
            action,
            command.Context,
            fingerprint,
            async cancellationToken =>
            {
                var row = await RequireLockedAsync(command.ProfileId, cancellationToken);
                var beforeJson = Serialize(row);
                var beforeVersion = row.StateVersion;
                var next = transition(Lifecycle(row), LifecycleContext(command.Context));
                if (requirePublicationWindow
                    && await HasOverlappingPublicationLockedAsync(row, cancellationToken))
                {
                    throw Failure(ManufacturingToleranceProfilePersistenceCodesV1.PublicationOverlap);
                }

                ApplyLifecycle(row, next);
                if (next.Status == ManufacturingToleranceProfileStatusV1.Approved)
                {
                    ApplyApprovalEvidence(row, command.Context);
                }
                else
                {
                    ApplyPublicationEvidence(row, command.Context);
                }

                return new PendingTransition(row, beforeJson, operation, beforeVersion);
            },
            cancellationToken);
    }

    private async Task<ManufacturingToleranceProfileCommandExecutionV1> ExecuteAsync<TFingerprint>(
        string action,
        ManufacturingToleranceProfilePersistenceContextV1 context,
        TFingerprint fingerprint,
        Func<CancellationToken, Task<PendingTransition>> transition,
        CancellationToken cancellationToken)
    {
        ValidateContext(context);
        await RequireAuthorityAsync(action, context, cancellationToken);
        var keyHash = IdempotencyHash.ComputeKey(context.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(fingerprint);
        var strategy = database.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                database.ChangeTracker.Clear();
                await using var transaction = await BeginSerializableAsync(cancellationToken);
                var existing = await FindExecutionAsync(action, keyHash, cancellationToken);
                if (existing is not null)
                {
                    await RequireAuthorityAsync(action, context, cancellationToken);
                    var replay = ResolveReplay(existing, requestHash, context);
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                    }

                    return replay;
                }

                var pending = await transition(cancellationToken);
                var finalScope = await RequireAuthorityAsync(action, context, cancellationToken);
                var response = Candidate(pending.Row);
                auditEvents.Enqueue(new AuditEventDraft(
                    context.CompanyId,
                    EventType(action),
                    "Settings.ManufacturingToleranceProfileLifecycle",
                    pending.Row.ProfileId.ToString("D"),
                    pending.Operation,
                    pending.EntityVersionBefore,
                    pending.Row.StateVersion,
                    AuditActorTypes.User,
                    context.ActorId,
                    action,
                    finalScope.RoleCodes,
                    pending.BeforeJson,
                    JsonSerializer.Serialize(response),
                    context.Reason,
                    ApprovalContextJson: null,
                    AuditAiInvolvement.None,
                    AiContextJson: null,
                    context.SourceChannel,
                    context.ClientIpAddress,
                    context.UserAgent,
                    context.CorrelationId,
                    context.CausationId,
                    context.IdempotencyKey));
                database.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
                    Guid.NewGuid(),
                    context.CompanyId,
                    action,
                    keyHash,
                    requestHash,
                    context.ActorId,
                    "Succeeded",
                    200,
                    JsonSerializer.Serialize(response),
                    context.CorrelationId,
                    context.OccurredAtUtc,
                    CommandExecutionSchemaVersion)));

                await database.SaveLifecycleCommandChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return new ManufacturingToleranceProfileCommandExecutionV1(
                    response,
                    IsReplay: false,
                    context.CorrelationId);
            });
        }
        catch (DbUpdateConcurrencyException exception)
        {
            database.ChangeTracker.Clear();
            var current = await database.ManufacturingToleranceProfileLifecycles.AsNoTracking()
                .Where(row => row.ProfileId == ProfileId(fingerprint))
                .Select(row => (long?)row.StateVersion)
                .SingleOrDefaultAsync(cancellationToken);
            throw new ManufacturingToleranceProfilePersistenceException(
                ManufacturingToleranceProfilePersistenceCodesV1.VersionConflict,
                context.ExpectedStateVersion,
                current,
                exception);
        }
        catch (DbUpdateException exception)
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(action, keyHash, cancellationToken);
            if (winner is not null)
            {
                await RequireAuthorityAsync(action, context, cancellationToken);
                return ResolveReplay(winner, requestHash, context);
            }

            if (fingerprint is PrepareNextFingerprint prepareNext)
            {
                var successor = await FindSuccessorForPredecessorAsync(
                    prepareNext.PublishedProfileId,
                    prepareNext.ProfileId,
                    cancellationToken);
                if (successor is not null)
                {
                    throw SuccessorVersionConflict(successor, exception);
                }
            }

            throw;
        }
        catch (ManufacturingToleranceProfilePersistenceException)
        {
            database.ChangeTracker.Clear();
            throw;
        }
        catch (ManufacturingToleranceProfileLifecycleException exception)
        {
            database.ChangeTracker.Clear();
            throw exception.Code == ManufacturingToleranceProfileLifecycleCodesV1.VersionConflict
                ? new ManufacturingToleranceProfilePersistenceException(
                    ManufacturingToleranceProfilePersistenceCodesV1.VersionConflict,
                    exception.ExpectedStateVersion,
                    exception.CurrentStateVersion,
                    exception)
                : new ManufacturingToleranceProfilePersistenceException(
                    exception.Code,
                    exception.ExpectedStateVersion,
                    exception.CurrentStateVersion,
                    exception);
        }
    }

    private async Task<PermissionScope> RequireAuthorityAsync(
        string action,
        ManufacturingToleranceProfilePersistenceContextV1 context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw Failure(ManufacturingToleranceProfilePersistenceCodesV1.Unauthenticated);
        }

        if (!companyContext.IsResolved
            || companyContext.CompanyId != context.CompanyId
            || !currentUser.CanAccessCompany(context.CompanyId)
            || !string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal))
        {
            throw Failure(ManufacturingToleranceProfilePersistenceCodesV1.Forbidden);
        }

        var scope = await authority.RevalidatePermissionScopeAsync(
            action, context.CompanyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide)
        {
            throw Failure(ManufacturingToleranceProfilePersistenceCodesV1.Forbidden);
        }

        return scope;
    }

    private async Task<ManufacturingToleranceProfileLifecycleRow?> FindLockedAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        return await database.ManufacturingToleranceProfileLifecycles
            .SingleOrDefaultAsync(row => row.ProfileId == profileId, cancellationToken);
    }

    private async Task<ManufacturingToleranceProfileLifecycleRow> RequireLockedAsync(
        Guid profileId,
        CancellationToken cancellationToken) =>
        await FindLockedAsync(profileId, cancellationToken)
        ?? throw Failure(ManufacturingToleranceProfilePersistenceCodesV1.NotFound);

    private async Task<bool> HasOverlappingPublicationLockedAsync(
        ManufacturingToleranceProfileLifecycleRow candidate,
        CancellationToken cancellationToken)
    {
        var through = candidate.EffectiveThrough ?? DateOnly.MaxValue;
        return await database.ManufacturingToleranceProfileLifecycles
            .AnyAsync(
                row => row.ProfileId != candidate.ProfileId
                    && row.Process == candidate.Process
                    && row.Status == ManufacturingToleranceProfileStatusV1.Published
                    && row.EffectiveFrom <= through
                    && (row.EffectiveThrough == null || row.EffectiveThrough >= candidate.EffectiveFrom),
                cancellationToken);
    }

    private Task<CommandExecution?> FindExecutionAsync(
        string action,
        string keyHash,
        CancellationToken cancellationToken) =>
        database.CommandExecutions.AsNoTracking().SingleOrDefaultAsync(
            row => row.CommandName == action && row.IdempotencyKeyHash == keyHash,
            cancellationToken);

    private Task<ManufacturingToleranceProfileLifecycleRow?> FindSuccessorAsync(
        string process,
        long profileVersion,
        Guid requestedProfileId,
        CancellationToken cancellationToken) =>
        database.ManufacturingToleranceProfileLifecycles.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Process == process
                    && row.ProfileVersion == profileVersion
                    && row.ProfileId != requestedProfileId,
                cancellationToken);

    private async Task<ManufacturingToleranceProfileLifecycleRow?> FindSuccessorForPredecessorAsync(
        Guid publishedProfileId,
        Guid requestedProfileId,
        CancellationToken cancellationToken)
    {
        var predecessor = await database.ManufacturingToleranceProfileLifecycles.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ProfileId == publishedProfileId, cancellationToken);
        if (predecessor is null || predecessor.ProfileVersion == long.MaxValue)
        {
            return null;
        }

        return await FindSuccessorAsync(
            predecessor.Process,
            predecessor.ProfileVersion + 1,
            requestedProfileId,
            cancellationToken);
    }

    private static ManufacturingToleranceProfileCommandExecutionV1 ResolveReplay(
        CommandExecution execution,
        string requestHash,
        ManufacturingToleranceProfilePersistenceContextV1 context)
    {
        if (!string.Equals(execution.ActorId, context.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Failure(ManufacturingToleranceProfilePersistenceCodesV1.IdempotencyConflict);
        }

        if (execution.SchemaVersion != CommandExecutionSchemaVersion
            || !string.Equals(execution.Outcome, "Succeeded", StringComparison.Ordinal)
            || execution.HttpStatusCode != 200)
        {
            throw Failure(ManufacturingToleranceProfilePersistenceCodesV1.StoredResponseInvalid);
        }

        var response = JsonSerializer.Deserialize<ManufacturingToleranceProfileCandidateV1>(
            execution.ResponseJson);
        if (response is null || response.Lifecycle.CompanyId != context.CompanyId)
        {
            throw Failure(ManufacturingToleranceProfilePersistenceCodesV1.StoredResponseInvalid);
        }

        return new ManufacturingToleranceProfileCommandExecutionV1(
            response,
            IsReplay: true,
            execution.OriginalCorrelationId);
    }

    private async Task<IDbContextTransaction?> BeginSerializableAsync(CancellationToken cancellationToken) =>
        database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private static ManufacturingToleranceProfileLifecycleRow NewRow(
        ManufacturingToleranceProfileLifecycleV1 lifecycle,
        decimal maximumFirePercent,
        DateOnly effectiveFrom,
        DateOnly? effectiveThrough,
        ManufacturingToleranceProfilePersistenceContextV1 context)
    {
        var row = new ManufacturingToleranceProfileLifecycleRow
        {
            CompanyId = lifecycle.CompanyId,
            ProfileId = lifecycle.ProfileId,
            Process = lifecycle.Process,
            ProfileVersion = lifecycle.ProfileVersion,
            MaximumFirePercent = maximumFirePercent,
            EffectiveFrom = effectiveFrom,
            EffectiveThrough = effectiveThrough,
        };
        ApplyLifecycle(row, lifecycle);
        ApplyPreparationEvidence(row, context);
        return row;
    }

    private static void ApplyLifecycle(
        ManufacturingToleranceProfileLifecycleRow row,
        ManufacturingToleranceProfileLifecycleV1 lifecycle)
    {
        row.StateVersion = lifecycle.StateVersion;
        row.Status = lifecycle.Status;
        row.PreparedBy = lifecycle.PreparedBy;
        row.PreparedAtUtc = lifecycle.PreparedAtUtc;
        row.PreparationReason = lifecycle.PreparationReason;
        row.ApprovedBy = lifecycle.ApprovedBy;
        row.ApprovedAtUtc = lifecycle.ApprovedAtUtc;
        row.ApprovalReason = lifecycle.ApprovalReason;
        row.PublishedBy = lifecycle.PublishedBy;
        row.PublishedAtUtc = lifecycle.PublishedAtUtc;
        row.PublicationReason = lifecycle.PublicationReason;
    }

    private static void ApplyPreparationEvidence(
        ManufacturingToleranceProfileLifecycleRow row,
        ManufacturingToleranceProfilePersistenceContextV1 context)
    {
        row.PreparationCorrelationId = context.CorrelationId;
        row.PreparationCausationId = context.CausationId;
        row.PreparationSourceChannel = context.SourceChannel;
        row.PreparationClientIpAddress = context.ClientIpAddress;
        row.PreparationUserAgent = context.UserAgent;
    }

    private static void ApplyApprovalEvidence(
        ManufacturingToleranceProfileLifecycleRow row,
        ManufacturingToleranceProfilePersistenceContextV1 context)
    {
        row.ApprovalCorrelationId = context.CorrelationId;
        row.ApprovalCausationId = context.CausationId;
        row.ApprovalSourceChannel = context.SourceChannel;
        row.ApprovalClientIpAddress = context.ClientIpAddress;
        row.ApprovalUserAgent = context.UserAgent;
    }

    private static void ApplyPublicationEvidence(
        ManufacturingToleranceProfileLifecycleRow row,
        ManufacturingToleranceProfilePersistenceContextV1 context)
    {
        row.PublicationCorrelationId = context.CorrelationId;
        row.PublicationCausationId = context.CausationId;
        row.PublicationSourceChannel = context.SourceChannel;
        row.PublicationClientIpAddress = context.ClientIpAddress;
        row.PublicationUserAgent = context.UserAgent;
    }

    private static ManufacturingToleranceProfileLifecycleV1 Lifecycle(
        ManufacturingToleranceProfileLifecycleRow row) => new ManufacturingToleranceProfileLifecycleV1(
        row.CompanyId,
        row.ProfileId,
        row.Process,
        row.ProfileVersion,
        row.StateVersion,
        row.Status,
        row.PreparedBy,
        row.PreparedAtUtc,
        row.PreparationReason,
        row.ApprovedBy,
        row.ApprovedAtUtc,
        row.ApprovalReason,
        row.PublishedBy,
        row.PublishedAtUtc,
        row.PublicationReason);

    private static ManufacturingToleranceProfileCandidateV1 Candidate(
        ManufacturingToleranceProfileLifecycleRow row) => new ManufacturingToleranceProfileCandidateV1(
        Lifecycle(row),
        row.MaximumFirePercent,
        row.EffectiveFrom,
        row.EffectiveThrough);

    private static ManufacturingToleranceProfileCommandContextV1 LifecycleContext(
        ManufacturingToleranceProfilePersistenceContextV1 context) =>
        new ManufacturingToleranceProfileCommandContextV1(
        context.CompanyId,
        context.ActorId,
        context.ExpectedStateVersion,
        context.IdempotencyKey,
        context.Reason,
        context.OccurredAtUtc);

    private static string Serialize(ManufacturingToleranceProfileLifecycleRow row) =>
        JsonSerializer.Serialize(Candidate(row));

    private static string EventType(string action) => action switch
    {
        ManufacturingToleranceProfileActionsV1.Prepare =>
            "settings.manufacturing-tolerance-profile.prepared.v1",
        ManufacturingToleranceProfileActionsV1.Approve =>
            "settings.manufacturing-tolerance-profile.approved.v1",
        ManufacturingToleranceProfileActionsV1.Publish =>
            "settings.manufacturing-tolerance-profile.published.v1",
        _ => throw Failure(ManufacturingToleranceProfilePersistenceCodesV1.Invalid),
    };

    private static void ValidateContext(ManufacturingToleranceProfilePersistenceContextV1 context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.CompanyId == Guid.Empty
            || !ValidText(context.ActorId, ActorMaximumLength)
            || context.ExpectedStateVersion < 0
            || !ValidText(context.IdempotencyKey, 128)
            || !ValidText(context.Reason, ReasonMaximumLength)
            || context.OccurredAtUtc.Offset != TimeSpan.Zero
            || !ValidText(context.CorrelationId, 128)
            || !ValidOptionalText(context.CausationId, 128)
            || !ValidText(context.SourceChannel, 32)
            || !AllowedSourceChannels.Contains(context.SourceChannel)
            || !ValidOptionalText(context.ClientIpAddress, 64)
            || !ValidOptionalText(context.UserAgent, 512))
        {
            throw Failure(ManufacturingToleranceProfilePersistenceCodesV1.Invalid);
        }
    }

    private static void ValidateCandidate(
        decimal maximumFirePercent,
        DateOnly effectiveFrom,
        DateOnly? effectiveThrough)
    {
        if (maximumFirePercent < 0m
            || maximumFirePercent > 999.999999m
            || DecimalScale(maximumFirePercent) > 6
            || effectiveThrough < effectiveFrom)
        {
            throw Failure(ManufacturingToleranceProfilePersistenceCodesV1.Invalid);
        }
    }

    private static int DecimalScale(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0x7F;

    private static bool ValidText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    private static bool ValidOptionalText(string? value, int maximumLength) =>
        value is null || ValidText(value, maximumLength);

    private static Guid ProfileId<TFingerprint>(TFingerprint fingerprint) => fingerprint switch
    {
        PrepareFingerprint value => value.ProfileId,
        PrepareNextFingerprint value => value.ProfileId,
        TransitionFingerprint value => value.ProfileId,
        _ => Guid.Empty,
    };

    private static ManufacturingToleranceProfilePersistenceException Failure(string code) =>
        new ManufacturingToleranceProfilePersistenceException(code);

    private static ManufacturingToleranceProfilePersistenceException SuccessorVersionConflict(
        ManufacturingToleranceProfileLifecycleRow existingSuccessor,
        Exception? innerException = null) =>
        new ManufacturingToleranceProfilePersistenceException(
            ManufacturingToleranceProfilePersistenceCodesV1.VersionConflict,
            expectedStateVersion: 0,
            currentStateVersion: existingSuccessor.StateVersion,
            innerException: innerException);

    private static readonly IReadOnlySet<string> AllowedSourceChannels = new HashSet<string>(StringComparer.Ordinal)
    {
        SourceChannels.Web,
        SourceChannels.Mobile,
        SourceChannels.Import,
        SourceChannels.Integration,
        SourceChannels.AiAssisted,
        SourceChannels.System,
    };

    private sealed record PendingTransition(
        ManufacturingToleranceProfileLifecycleRow Row,
        string BeforeJson,
        string Operation,
        long? EntityVersionBefore);

    private sealed record PrepareFingerprint(
        Guid ProfileId,
        string Process,
        long ProfileVersion,
        decimal MaximumFirePercent,
        DateOnly EffectiveFrom,
        DateOnly? EffectiveThrough,
        long ExpectedStateVersion,
        string Reason);

    private sealed record PrepareNextFingerprint(
        Guid PublishedProfileId,
        Guid ProfileId,
        decimal MaximumFirePercent,
        DateOnly EffectiveFrom,
        DateOnly? EffectiveThrough,
        long ExpectedStateVersion,
        string Reason);

    private sealed record TransitionFingerprint(
        Guid ProfileId,
        long ExpectedStateVersion,
        string Reason);
}
