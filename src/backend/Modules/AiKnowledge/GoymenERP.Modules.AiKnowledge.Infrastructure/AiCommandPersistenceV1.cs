using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure;

/// <summary>Reuses the existing AI module context; command tables are in ai, separate from Work and group approvals.</summary>
public sealed class AiCommandPersistenceV1(AiKnowledgeDbContext database, ICompanyContext company) : IAiCommandPersistenceV1
{
    public bool IsAvailable => database.Database.IsSqlServer();

    public async Task<AiCommandStoredRecordV1?> GetAsync(Guid companyId, Guid previewId, CancellationToken cancellationToken = default)
    {
        if (!InCompany(companyId) || previewId == Guid.Empty) return null;
        try
        {
            var row = await database.CommandRecords.AsNoTracking()
                .SingleOrDefaultAsync(row => row.CompanyId == companyId && row.PreviewId == previewId, cancellationToken);
            if (!InCompany(companyId) || row is null) return null;
            return new(row.CompanyId, row.PreviewId, row.ActorId, row.State, row.Version, row.BindingSha256, row.RecordJson);
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            throw new AiCommandPersistenceUnavailableException(exception);
        }
    }

    public async Task<bool> TryAddAsync(AiCommandStoredRecordV1 record, IReadOnlyList<AiCommandStoredEventV1> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(events);
        if (!Valid(record) || record.Version != 1 || record.State != 1 || events.Count == 0
            || events.Any(item => !Valid(item) || item.CompanyId != record.CompanyId || item.PreviewId != record.PreviewId)
            || events.Select(item => item.EventId).Distinct().Count() != events.Count) return false;
        var row = new AiCommandRecordRow { CompanyId = record.CompanyId, PreviewId = record.PreviewId,
            ActorId = record.ActorId, State = record.State, Version = record.Version,
            BindingSha256 = record.BindingSha256, RecordJson = record.RecordJson };
        database.CommandRecords.Add(row);
        var eventRows = events.Select(EventRow).ToArray();
        database.CommandEvents.AddRange(eventRows);
        return await SaveAsync(record.CompanyId, new object[] { row }.Concat(eventRows), cancellationToken);
    }

    public async Task<bool> TryReplaceAsync(AiCommandStoredRecordV1 next, long expectedVersion,
        AiCommandStoredEventV1 auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(auditEvent);
        if (!Valid(next) || !Valid(auditEvent) || expectedVersion is <= 0 or long.MaxValue
            || next.Version != expectedVersion + 1 || auditEvent.CompanyId != next.CompanyId
            || auditEvent.PreviewId != next.PreviewId) return false;
        AiCommandRecordRow? row;
        try
        {
            row = await database.CommandRecords.SingleOrDefaultAsync(item => item.CompanyId == next.CompanyId
                && item.PreviewId == next.PreviewId && item.Version == expectedVersion, cancellationToken);
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            throw new AiCommandPersistenceUnavailableException(exception);
        }
        if (row is null) return false;
        if (!InCompany(next.CompanyId) || !string.Equals(row.ActorId, next.ActorId, StringComparison.Ordinal)
            || !string.Equals(row.BindingSha256, next.BindingSha256, StringComparison.Ordinal))
        {
            database.Entry(row).State = EntityState.Detached;
            return false;
        }
        row.State = next.State;
        row.Version = next.Version;
        row.RecordJson = next.RecordJson;
        var eventRow = EventRow(auditEvent);
        database.CommandEvents.Add(eventRow);
        return await SaveAsync(next.CompanyId, [row, eventRow], cancellationToken);
    }

    public Task<bool> AppendAsync(AiCommandStoredEventV1 auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        if (!Valid(auditEvent)) return Task.FromResult(false);
        var row = EventRow(auditEvent);
        database.CommandEvents.Add(row);
        return SaveAsync(auditEvent.CompanyId, [row], cancellationToken);
    }

    private async Task<bool> SaveAsync(Guid companyId, IEnumerable<object> ownedRows, CancellationToken cancellationToken)
    {
        var rows = ownedRows.ToArray();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!InCompany(companyId)) return false;
            // Relational SaveChanges atomically writes the state CAS and all its content-free events.
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Includes duplicate preview/event and a lost rowversion/version race. No owner command is run here.
            return false;
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            throw new AiCommandPersistenceUnavailableException(exception);
        }
        finally
        {
            // Never clear another service's tracked approval/audit records in the shared module context.
            foreach (var row in rows) database.Entry(row).State = EntityState.Detached;
        }
    }

    private bool InCompany(Guid id) => id != Guid.Empty && company.IsResolved && company.CompanyId == id;

    private bool Valid(AiCommandStoredRecordV1 row)
    {
        if (!InCompany(row.CompanyId) || row.PreviewId == Guid.Empty || !Text(row.ActorId, 128)
            || row.State is < 1 or > 9 || row.Version <= 0 || row.BindingSha256.Length != 64
            || row.BindingSha256.Any(value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) return false;
        try { using var json = JsonDocument.Parse(row.RecordJson); return json.RootElement.ValueKind == JsonValueKind.Object; }
        catch (JsonException) { return false; }
    }

    private bool Valid(AiCommandStoredEventV1 row) => InCompany(row.CompanyId) && row.EventId != Guid.Empty
        && row.PreviewId != Guid.Empty && row.ApprovalId != Guid.Empty && row.Kind is >= 1 and <= 10
        && Text(row.ActorId, 128) && Text(row.CorrelationId, 128) && Optional(row.CausationId, 128)
        && Optional(row.ToolId, 128) && Optional(row.ToolVersion, 64) && Optional(row.ReasonCode, 128)
        && Optional(row.ResultVersion, 256) && row.OccurredAtUtc.Offset == TimeSpan.Zero
        && (row.PreviewHash is null || row.PreviewHash.Length == 64
            && row.PreviewHash.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f'));

    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
    private static bool Optional(string? value, int maximum) => value is null || Text(value, maximum);

    private static AiCommandEventRow EventRow(AiCommandStoredEventV1 row) => new()
    {
        CompanyId = row.CompanyId, EventId = row.EventId, ActorId = row.ActorId, Kind = row.Kind,
        PreviewId = row.PreviewId, ToolId = row.ToolId, ToolVersion = row.ToolVersion, PreviewHash = row.PreviewHash,
        ApprovalId = row.ApprovalId, ReasonCode = row.ReasonCode, ResultVersion = row.ResultVersion,
        CorrelationId = row.CorrelationId, CausationId = row.CausationId, OccurredAtUtc = row.OccurredAtUtc,
    };
}
