using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Work.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

public sealed record WorkDesiredStateWriteMetadataV2(
    string ActorType,
    string ActorId,
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes,
    string SourceChannel,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string Reason);

public sealed class WorkDesiredStateStoreV2
{
    internal const string AppliedEventType = "work.desired-state.applied.v2";
    private const int OutboxSchemaVersion = 2;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly WorkDbContext dbContext;
    private readonly ICompanyContext companyContext;
    private readonly IAuditEventCollector auditEvents;
    private readonly TimeProvider timeProvider;
    private readonly IWorkStorageKeyDigestV1 keyDigest;
    private readonly Func<CancellationToken, Task> beforeSave;
    private readonly Func<CancellationToken, Task> afterRead;
    private readonly Func<CancellationToken, Task> afterCommit;

    public WorkDesiredStateStoreV2(
        WorkDbContext dbContext,
        ICompanyContext companyContext,
        IAuditEventCollector auditEvents,
        TimeProvider timeProvider)
        : this(
            dbContext,
            companyContext,
            auditEvents,
            timeProvider,
            WorkSha256StorageKeyDigestV1.Instance,
            static _ => Task.CompletedTask,
            static _ => Task.CompletedTask,
            static _ => Task.CompletedTask)
    {
    }

    internal WorkDesiredStateStoreV2(
        WorkDbContext dbContext,
        ICompanyContext companyContext,
        IAuditEventCollector auditEvents,
        TimeProvider timeProvider,
        IWorkStorageKeyDigestV1 keyDigest,
        Func<CancellationToken, Task>? beforeSave = null,
        Func<CancellationToken, Task>? afterRead = null,
        Func<CancellationToken, Task>? afterCommit = null)
    {
        this.dbContext = dbContext;
        this.companyContext = companyContext;
        this.auditEvents = auditEvents;
        this.timeProvider = timeProvider;
        this.keyDigest = keyDigest;
        this.beforeSave = beforeSave ?? (static _ => Task.CompletedTask);
        this.afterRead = afterRead ?? (static _ => Task.CompletedTask);
        this.afterCommit = afterCommit ?? (static _ => Task.CompletedTask);
    }

    public async Task<WorkDesiredStateResultV2> ReconcileAsync(
        WorkDesiredStateObservationV2 observation,
        WorkDesiredStateWriteMetadataV2 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(metadata);

        var capturedCompanyId = observation.CompanyId;
        if (!MatchesCapturedCompany(capturedCompanyId))
        {
            return Refused();
        }

        RequireMetadata(metadata);
        _ = WorkDesiredStateProtocolV2.Reconcile(null, observation);
        var sourceKey = WorkStorageKeyV1.Create(observation.SourceKey, keyDigest);
        var scopeKey = WorkStorageKeyV1.Create(observation.ScopeKey, keyDigest);
        var row = await LoadAsync(observation.CompanyId, sourceKey, scopeKey, cancellationToken);
        await afterRead(cancellationToken);
        if (!MatchesCapturedCompany(capturedCompanyId))
        {
            return Refused();
        }

        var prior = row is null ? null : Restore(row, sourceKey, scopeKey);
        var result = WorkDesiredStateProtocolV2.Reconcile(prior, observation);
        if (result.Decision != WorkDesiredStateDecisionV2.Apply)
        {
            return MatchesCapturedCompany(capturedCompanyId) ? result : Refused();
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = row?.Version;
        if (row is null)
        {
            row = NewSnapshot(result.State!, sourceKey, scopeKey, now);
            dbContext.DesiredStateSnapshots.Add(row);
        }
        else
        {
            dbContext.DesiredStateSnapshots.Attach(row);
            ApplySnapshot(row, result.State!, now);
        }

        var inbox = NewInbox(observation, sourceKey, scopeKey, now);
        dbContext.ObservationInbox.Add(inbox);
        var outbox = NewOutbox(result.State!, row.Version, sourceKey, scopeKey, metadata, now);
        dbContext.OutboxMessages.Add(outbox);

        try
        {
            await beforeSave(cancellationToken);
            if (!MatchesCapturedCompany(capturedCompanyId))
            {
                CleanupAttempt(row, inbox, outbox);
                return Refused();
            }

            auditEvents.Enqueue(NewAudit(prior, result.State!, row.Version, sourceKey, scopeKey, metadata));
            await dbContext.SaveChangesAsync(cancellationToken);
            CleanupAttempt(row, inbox, outbox);
            await afterCommit(cancellationToken);
            return MatchesCapturedCompany(capturedCompanyId) ? result : Refused();
        }
        catch (Exception exception) when (IsCasRace(exception))
        {
            CleanupAttempt(row, inbox, outbox);
            var winnerRow = await LoadAsync(observation.CompanyId, sourceKey, scopeKey, cancellationToken);
            await afterRead(cancellationToken);
            if (!MatchesCapturedCompany(capturedCompanyId))
            {
                return Refused();
            }

            if (winnerRow is null)
            {
                throw;
            }

            var winner = Restore(winnerRow, sourceKey, scopeKey);
            var winnerResult = WorkDesiredStateProtocolV2.Reconcile(winner, observation);
            if (winnerResult.Decision == WorkDesiredStateDecisionV2.Apply)
            {
                throw;
            }

            return winnerResult;
        }
        catch
        {
            CleanupAttempt(row, inbox, outbox);
            throw;
        }
    }

    private async Task<WorkDesiredStateSnapshotRow?> LoadAsync(
        Guid companyId,
        WorkStorageKeyV1 sourceKey,
        WorkStorageKeyV1 scopeKey,
        CancellationToken cancellationToken) =>
        await dbContext.DesiredStateSnapshots.AsNoTracking().SingleOrDefaultAsync(
            row => row.CompanyId == companyId
                && row.SourceKeyDigest == sourceKey.Digest
                && row.ScopeKeyDigest == scopeKey.Digest,
            cancellationToken);

    private static WorkDesiredStateSnapshotV2 Restore(
        WorkDesiredStateSnapshotRow row,
        WorkStorageKeyV1 expectedSource,
        WorkStorageKeyV1 expectedScope)
    {
        var storedSource = new WorkStorageKeyV1(row.SourceKeyUtf16Be, row.SourceKeyDigest);
        var storedScope = new WorkStorageKeyV1(row.ScopeKeyUtf16Be, row.ScopeKeyDigest);
        expectedSource.RequireSameRaw(storedSource);
        expectedScope.RequireSameRaw(storedScope);
        var source = storedSource.DecodeAndVerify();
        var scope = storedScope.DecodeAndVerify();
        var highest = new WorkDesiredStateObservationV2(
            row.CompanyId,
            source,
            scope,
            row.HighestObservationOrdinal,
            (WorkSourceAvailabilityV2)row.HighestAvailability,
            row.HighestDesiredState is null ? null : (WorkDesiredStateV2)row.HighestDesiredState,
            row.HighestEvidenceId,
            row.HighestPayloadSha256);
        var anyEffective = row.EffectiveObservationOrdinal is not null
            || row.EffectiveDesiredState is not null
            || row.EffectiveEvidenceId is not null
            || row.EffectivePayloadSha256 is not null;
        var completeEffective = row.EffectiveObservationOrdinal is not null
            && row.EffectiveDesiredState is not null
            && row.EffectiveEvidenceId is not null
            && row.EffectivePayloadSha256 is not null;
        if (anyEffective != completeEffective)
        {
            throw new WorkStorageCorruptionException("Work effective snapshot tuple is incomplete.");
        }

        var effective = row.EffectiveObservationOrdinal is null
            ? null
            : new WorkEffectiveDesiredStateV2(
                row.EffectiveObservationOrdinal.Value,
                (WorkDesiredStateV2)row.EffectiveDesiredState!.Value,
                row.EffectiveEvidenceId!.Value,
                row.EffectivePayloadSha256!);
        var snapshot = new WorkDesiredStateSnapshotV2(row.CompanyId, source, scope, highest, effective);
        _ = WorkDesiredStateProtocolV2.Reconcile(snapshot, highest);
        return snapshot;
    }

    private static WorkDesiredStateSnapshotRow NewSnapshot(
        WorkDesiredStateSnapshotV2 snapshot,
        WorkStorageKeyV1 sourceKey,
        WorkStorageKeyV1 scopeKey,
        DateTimeOffset now)
    {
        var row = new WorkDesiredStateSnapshotRow
        {
            CompanyId = snapshot.CompanyId,
            SourceKeyDigest = sourceKey.Digest.ToArray(),
            ScopeKeyDigest = scopeKey.Digest.ToArray(),
            SourceKeyUtf16Be = sourceKey.Utf16Be.ToArray(),
            ScopeKeyUtf16Be = scopeKey.Utf16Be.ToArray(),
            Version = 1,
            UpdatedAtUtc = now
        };
        ApplySnapshotValues(row, snapshot);
        return row;
    }

    private static void ApplySnapshot(
        WorkDesiredStateSnapshotRow row,
        WorkDesiredStateSnapshotV2 snapshot,
        DateTimeOffset now)
    {
        row.Version = checked(row.Version + 1);
        row.UpdatedAtUtc = now;
        ApplySnapshotValues(row, snapshot);
    }

    private static void ApplySnapshotValues(
        WorkDesiredStateSnapshotRow row,
        WorkDesiredStateSnapshotV2 snapshot)
    {
        var highest = snapshot.HighestReliableObservation;
        row.HighestObservationOrdinal = highest.ObservationOrdinal!.Value;
        row.HighestAvailability = (int)highest.Availability;
        row.HighestDesiredState = highest.DesiredState is null ? null : (int)highest.DesiredState;
        row.HighestEvidenceId = highest.EvidenceId;
        row.HighestPayloadSha256 = highest.PayloadSha256;
        row.EffectiveObservationOrdinal = snapshot.EffectiveState?.ObservationOrdinal;
        row.EffectiveDesiredState = snapshot.EffectiveState is null ? null : (int)snapshot.EffectiveState.DesiredState;
        row.EffectiveEvidenceId = snapshot.EffectiveState?.EvidenceId;
        row.EffectivePayloadSha256 = snapshot.EffectiveState?.PayloadSha256;
    }

    private static WorkObservationInboxRow NewInbox(
        WorkDesiredStateObservationV2 observation,
        WorkStorageKeyV1 sourceKey,
        WorkStorageKeyV1 scopeKey,
        DateTimeOffset now) => new WorkObservationInboxRow()
    {
        CompanyId = observation.CompanyId,
        SourceKeyDigest = sourceKey.Digest.ToArray(),
        ScopeKeyDigest = scopeKey.Digest.ToArray(),
        SourceKeyUtf16Be = sourceKey.Utf16Be.ToArray(),
        ScopeKeyUtf16Be = scopeKey.Utf16Be.ToArray(),
        ObservationOrdinal = observation.ObservationOrdinal!.Value,
        Availability = (int)observation.Availability,
        DesiredState = observation.DesiredState is null ? null : (int)observation.DesiredState,
        EvidenceId = observation.EvidenceId,
        PayloadSha256 = observation.PayloadSha256,
        RecordedAtUtc = now
    };

    private static WorkOutboxRow NewOutbox(
        WorkDesiredStateSnapshotV2 snapshot,
        long aggregateVersion,
        WorkStorageKeyV1 sourceKey,
        WorkStorageKeyV1 scopeKey,
        WorkDesiredStateWriteMetadataV2 metadata,
        DateTimeOffset now)
    {
        var row = new WorkOutboxRow
        {
            Id = Guid.NewGuid(),
            CompanyId = snapshot.CompanyId,
            SourceKeyDigest = sourceKey.Digest.ToArray(),
            ScopeKeyDigest = scopeKey.Digest.ToArray(),
            SourceKeyUtf16Be = sourceKey.Utf16Be.ToArray(),
            ScopeKeyUtf16Be = scopeKey.Utf16Be.ToArray(),
            ObservationOrdinal = snapshot.HighestReliableObservation.ObservationOrdinal!.Value,
            AggregateVersion = aggregateVersion,
            EventType = AppliedEventType,
            SchemaVersion = OutboxSchemaVersion,
            OccurredAtUtc = now,
            CorrelationId = metadata.CorrelationId,
            CausationId = metadata.CausationId
        };
        row.PayloadJson = CreateOutboxPayloadJson(snapshot, sourceKey, scopeKey, aggregateVersion);
        return row;
    }

    internal static string CreateOutboxPayloadJson(
        WorkDesiredStateSnapshotV2 snapshot,
        WorkStorageKeyV1 sourceKey,
        WorkStorageKeyV1 scopeKey,
        long aggregateVersion) => JsonSerializer.Serialize(
        new WorkDesiredStateOutboxPayloadV2(
            OutboxSchemaVersion,
            snapshot.CompanyId,
            sourceKey.Utf16Be,
            sourceKey.Digest,
            scopeKey.Utf16Be,
            scopeKey.Digest,
            snapshot.HighestReliableObservation.ObservationOrdinal!.Value,
            snapshot.HighestReliableObservation.Availability,
            snapshot.HighestReliableObservation.DesiredState,
            snapshot.HighestReliableObservation.EvidenceId,
            snapshot.HighestReliableObservation.PayloadSha256,
            snapshot.EffectiveState,
            aggregateVersion),
        Json);

    internal static WorkDesiredStateSnapshotV2 DecodeOutboxPayload(WorkOutboxRow row)
    {
        if (row.SchemaVersion != OutboxSchemaVersion
            || !string.Equals(row.EventType, AppliedEventType, StringComparison.Ordinal))
        {
            throw new WorkStorageCorruptionException("Work outbox row type or schema is invalid.");
        }

        WorkDesiredStateOutboxPayloadV2 payload;
        try
        {
            payload = JsonSerializer.Deserialize<WorkDesiredStateOutboxPayloadV2>(row.PayloadJson, Json)
                ?? throw new WorkStorageCorruptionException("Work outbox payload is empty.");
        }
        catch (JsonException)
        {
            throw new WorkStorageCorruptionException("Work outbox payload is malformed.");
        }
        if (payload.SchemaVersion != OutboxSchemaVersion
            || payload.CompanyId != row.CompanyId
            || payload.ObservationOrdinal != row.ObservationOrdinal
            || payload.AggregateVersion != row.AggregateVersion)
        {
            throw new WorkStorageCorruptionException("Work outbox envelope does not match its row.");
        }

        var source = new WorkStorageKeyV1(payload.SourceKeyUtf16Be, payload.SourceKeyDigest);
        var scope = new WorkStorageKeyV1(payload.ScopeKeyUtf16Be, payload.ScopeKeyDigest);
        new WorkStorageKeyV1(row.SourceKeyUtf16Be, row.SourceKeyDigest).RequireSameRaw(source);
        new WorkStorageKeyV1(row.ScopeKeyUtf16Be, row.ScopeKeyDigest).RequireSameRaw(scope);
        var sourceText = source.DecodeAndVerify();
        var scopeText = scope.DecodeAndVerify();
        var highest = new WorkDesiredStateObservationV2(
            payload.CompanyId,
            sourceText,
            scopeText,
            payload.ObservationOrdinal,
            payload.Availability,
            payload.DesiredState,
            payload.EvidenceId,
            payload.PayloadSha256);
        var snapshot = new WorkDesiredStateSnapshotV2(
            payload.CompanyId,
            sourceText,
            scopeText,
            highest,
            payload.EffectiveState);
        _ = WorkDesiredStateProtocolV2.Reconcile(snapshot, highest);
        return snapshot;
    }

    private static AuditEventDraft NewAudit(
        WorkDesiredStateSnapshotV2? before,
        WorkDesiredStateSnapshotV2 after,
        long versionAfter,
        WorkStorageKeyV1 sourceKey,
        WorkStorageKeyV1 scopeKey,
        WorkDesiredStateWriteMetadataV2 metadata)
    {
        var identity = new byte[64];
        sourceKey.Digest.CopyTo(identity, 0);
        scopeKey.Digest.CopyTo(identity, 32);
        var entityId = Convert.ToHexString(SHA256.HashData(identity));
        return new AuditEventDraft(
            after.CompanyId,
            AppliedEventType,
            "Work.DesiredStateSnapshot",
            entityId,
            "Reconcile",
            before is null ? null : versionAfter - 1,
            versionAfter,
            metadata.ActorType,
            metadata.ActorId,
            metadata.PermissionCode,
            metadata.RoleCodes,
            before is null ? "{}" : AuditJson(before, versionAfter - 1, sourceKey, scopeKey),
            AuditJson(after, versionAfter, sourceKey, scopeKey),
            metadata.Reason,
            null,
            AuditAiInvolvement.None,
            null,
            metadata.SourceChannel,
            null,
            null,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey);
    }

    private static string AuditJson(
        WorkDesiredStateSnapshotV2 snapshot,
        long version,
        WorkStorageKeyV1 sourceKey,
        WorkStorageKeyV1 scopeKey) => JsonSerializer.Serialize(new
        {
            sourceKeyDigest = Convert.ToHexString(sourceKey.Digest),
            scopeKeyDigest = Convert.ToHexString(scopeKey.Digest),
            highestOrdinal = snapshot.HighestReliableObservation.ObservationOrdinal,
            highestAvailability = snapshot.HighestReliableObservation.Availability,
            highestState = snapshot.HighestReliableObservation.DesiredState,
            effectiveOrdinal = snapshot.EffectiveState?.ObservationOrdinal,
            effectiveState = snapshot.EffectiveState?.DesiredState,
            version
        }, Json);

    private static bool IsCasRace(Exception exception) =>
        exception is DbUpdateConcurrencyException
        || exception is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } };

    private bool MatchesCapturedCompany(Guid companyId) =>
        companyId != Guid.Empty
        && companyContext.IsResolved
        && companyContext.CompanyId == companyId;

    private static WorkDesiredStateResultV2 Refused() =>
        new WorkDesiredStateResultV2(WorkDesiredStateDecisionV2.Refused, null);

    private void CleanupAttempt(params object[] entities)
    {
        foreach (var entity in entities)
        {
            var entry = dbContext.Entry(entity);
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private static void RequireMetadata(WorkDesiredStateWriteMetadataV2 metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata.RoleCodes);
        foreach (var (value, name) in new[]
        {
            (metadata.ActorType, nameof(metadata.ActorType)),
            (metadata.ActorId, nameof(metadata.ActorId)),
            (metadata.PermissionCode, nameof(metadata.PermissionCode)),
            (metadata.SourceChannel, nameof(metadata.SourceChannel)),
            (metadata.CorrelationId, nameof(metadata.CorrelationId)),
            (metadata.IdempotencyKey, nameof(metadata.IdempotencyKey)),
            (metadata.Reason, nameof(metadata.Reason))
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Work write metadata is incomplete.", name);
            }
        }
    }

    private sealed record WorkDesiredStateOutboxPayloadV2(
        int SchemaVersion,
        Guid CompanyId,
        byte[] SourceKeyUtf16Be,
        byte[] SourceKeyDigest,
        byte[] ScopeKeyUtf16Be,
        byte[] ScopeKeyDigest,
        long ObservationOrdinal,
        WorkSourceAvailabilityV2 Availability,
        WorkDesiredStateV2? DesiredState,
        Guid EvidenceId,
        string PayloadSha256,
        WorkEffectiveDesiredStateV2? EffectiveState,
        long AggregateVersion);
}
