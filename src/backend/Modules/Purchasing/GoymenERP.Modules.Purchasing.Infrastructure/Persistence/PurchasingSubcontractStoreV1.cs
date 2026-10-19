using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Subcontracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed class PurchasingSubcontractStoreV1(PurchasingDbContext dbContext, ICompanyContext companyContext,
    IAuditEventCollector auditEvents, TimeProvider timeProvider) : IPurchasingSubcontractStoreV1
{
    private const int Recorded = 1;
    private const int Refused = 2;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PurchasingSubcontractReplayV1?> FindReplayAsync(PurchasingWriteEnvelope envelope,
        string requestHash, string commandName, CancellationToken cancellationToken = default)
    {
        var row = await FindAttemptAsync(commandName, KeyHash(envelope.IdempotencyKey), cancellationToken);
        if (row is null) return null;
        Same(row, envelope.ActorId, requestHash);
        if (row.Outcome == Recorded && row.OrderId is { } id)
            return new(await FindAsync(id, cancellationToken) ?? throw new InvalidOperationException("Stored subcontract result is missing."), null, row.CorrelationId);
        var refusal = row.RefusalJson is null ? null : JsonSerializer.Deserialize<PurchasingSubcontractTerminalRefusalV1>(row.RefusalJson, Json);
        return refusal is null ? throw new InvalidOperationException("Stored subcontract refusal is malformed.")
            : new(null, refusal, row.CorrelationId);
    }

    public async Task<PurchasingSubcontractStoreResultV1> StoreDraftAsync(PurchasingWriteEnvelope envelope,
        string requestHash, Func<SubcontractOrderV1> build, CancellationToken cancellationToken = default)
    {
        var replay = await FindReplayAsync(envelope, requestHash, PurchasingSubcontractCommandsV1.Create, cancellationToken);
        if (replay?.Order is { } replayed) return new(replayed, PurchasingWriteOutcome.Replayed, replay.OriginalCorrelationId);
        var company = Company(); var order = build();
        if (order.CompanyId != company) throw Error(PurchasingSubcontractStoreCodesV1.CompanyContextMismatch);
        var actor = envelope.ActorId.Trim();
        dbContext.SubcontractOrders.Add(new PurchasingSubcontractOrderRow
        {
            Id = order.Id, CompanyId = company, SupplierPartyId = order.SupplierPartyId,
            SupplierPartyVersion = order.SupplierPartyVersion, CompanyAccountId = order.CompanyAccountId,
            CompanyAccountVersion = order.CompanyAccountVersion, Status = (int)order.Status, Version = order.Version,
            CreatedAtUtc = order.CreatedAtUtc, UpdatedAtUtc = order.UpdatedAtUtc, CreatedBy = actor,
            CorrelationId = envelope.CorrelationId.Trim(),
        });
        foreach (var pair in order.Lines.Select((line, index) => (line, index)))
            dbContext.SubcontractOrderLines.Add(ToRow(company, order.Id, pair.index + 1, pair.line));
        dbContext.SubcontractAttempts.Add(Attempt(company, PurchasingSubcontractCommandsV1.Create,
            envelope, requestHash, order.Id, Recorded, null));
        Audit(envelope, company, order.Id, "purchasing.subcontract-draft.created.v1", "Create", null, 1,
            "{}", Snapshot(order), "Fason hizmet siparişi taslağı açıldı");
        await SaveAsync(cancellationToken);
        return new(new(order, []), PurchasingWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<StoredPurchasingSubcontractV1?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var header = await dbContext.SubcontractOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (header is null) return null;
        var lines = await dbContext.SubcontractOrderLines.AsNoTracking().Where(x => x.OrderId == id).OrderBy(x => x.LineNo).ToListAsync(cancellationToken);
        var snapshots = await dbContext.SubcontractAdmissions.AsNoTracking().Where(x => x.OrderId == id).OrderBy(x => x.LineId).ToListAsync(cancellationToken);
        return Restore(header, lines, snapshots);
    }

    public async Task<PurchasingSubcontractStoreResultV1?> AdmitAsync(PurchasingWriteEnvelope envelope,
        string requestHash, Guid id, long expectedVersion,
        IReadOnlyList<PurchasingSubcontractAdmissionSnapshotStoreV1> admissions,
        CancellationToken cancellationToken = default)
    {
        var replay = await FindReplayAsync(envelope, requestHash, PurchasingSubcontractCommandsV1.Admit, cancellationToken);
        if (replay is not null)
        {
            if (replay.Refusal is { } refusal) throw new PurchasingSubcontractTerminalStoreExceptionV1(refusal);
            return new(replay.Order!, PurchasingWriteOutcome.Replayed, replay.OriginalCorrelationId);
        }
        var row = await dbContext.SubcontractOrders.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null) return null;
        var lineRows = await dbContext.SubcontractOrderLines.AsNoTracking().Where(x => x.OrderId == id).OrderBy(x => x.LineNo).ToListAsync(cancellationToken);
        var order = RestoreOrder(row, lineRows); var before = Snapshot(order);
        PurchasingSubcontractTerminalRefusalV1? terminal = null;
        if (expectedVersion != order.Version)
            terminal = new(409, PurchasingSubcontractProblemCodesV1.VersionConflict, [new("expectedVersion", "version_conflict")]);
        else if (order.Status != SubcontractOrderStatusV1.Draft)
            terminal = new(409, PurchasingSubcontractProblemCodesV1.NotDraft, [new("status", "not_draft")]);
        if (terminal is not null)
        {
            dbContext.SubcontractAttempts.Add(Attempt(order.CompanyId, PurchasingSubcontractCommandsV1.Admit,
                envelope, requestHash, null, Refused, JsonSerializer.Serialize(terminal, Json)));
            await SaveAsync(cancellationToken);
            throw new PurchasingSubcontractTerminalStoreExceptionV1(terminal);
        }
        if (admissions.Count != order.Lines.Count || admissions.Select(x => x.LineId).Distinct().Count() != order.Lines.Count)
            throw new ArgumentException("One admission snapshot per line is required.", nameof(admissions));
        var versionBefore = order.Version; order.Admit(expectedVersion, timeProvider.GetUtcNow());
        row.Status = (int)order.Status; row.Version = order.Version; row.UpdatedAtUtc = order.UpdatedAtUtc;
        foreach (var snapshot in admissions) dbContext.SubcontractAdmissions.Add(ToRow(order.CompanyId, order.Id, snapshot));
        dbContext.SubcontractAttempts.Add(Attempt(order.CompanyId, PurchasingSubcontractCommandsV1.Admit,
            envelope, requestHash, order.Id, Recorded, null));
        Audit(envelope, order.CompanyId, order.Id, "purchasing.subcontract.admitted.v1", "Admit",
            versionBefore, order.Version, before, Snapshot(order), "Fason hizmet siparişi owner doğrulamalarıyla admit edildi");
        await SaveAsync(cancellationToken);
        return new(new(order, admissions), PurchasingWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    private async Task<PurchasingSubcontractAttemptRow?> FindAttemptAsync(string command, string key,
        CancellationToken ct) => await dbContext.SubcontractAttempts.AsNoTracking()
        .SingleOrDefaultAsync(x => x.CommandName == command && x.IdempotencyKeyHash == key, ct);
    private static void Same(PurchasingSubcontractAttemptRow row, string actor, string hash)
    {
        if (!string.Equals(row.ActorId, actor.Trim(), StringComparison.Ordinal)
            || !string.Equals(row.RequestHash, hash, StringComparison.Ordinal))
            throw Error(PurchasingSubcontractStoreCodesV1.SameKeyPayloadMismatch);
    }
    private PurchasingSubcontractAttemptRow Attempt(Guid company, string command,
        PurchasingWriteEnvelope envelope, string hash, Guid? orderId, int outcome, string? refusal) => new()
    {
        CompanyId = company, CommandName = command, IdempotencyKeyHash = KeyHash(envelope.IdempotencyKey),
        RequestHash = hash, ActorId = envelope.ActorId.Trim(), Outcome = outcome, OrderId = orderId,
        RefusalJson = refusal, CorrelationId = envelope.CorrelationId.Trim(), RecordedAtUtc = timeProvider.GetUtcNow(),
    };
    private static string KeyHash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim()))).ToLowerInvariant();
    private Guid Company() => companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        ? companyContext.CompanyId : throw Error(PurchasingSubcontractStoreCodesV1.CompanyContextMismatch);
    private static PurchasingSubcontractStoreExceptionV1 Error(string code, Exception? inner = null) => new(code, code, inner);

    private static PurchasingSubcontractOrderLineRow ToRow(Guid company, Guid order, int no, SubcontractOrderLineV1 line) => new()
    { CompanyId = company, OrderId = order, LineNo = no, LineId = line.LineId, ServiceId = line.ServiceId,
      ServiceVersion = line.ServiceVersion, ServiceUnitId = line.ServiceUnitId, ServiceQuantity = line.ServiceQuantity,
      SourcePhysicalRequestId = line.SourcePhysicalRequestId, SourcePhysicalRequestVersion = line.SourcePhysicalRequestVersion };
    private static PurchasingSubcontractAdmissionRow ToRow(Guid company, Guid order, PurchasingSubcontractAdmissionSnapshotStoreV1 x) => new()
    { CompanyId = company, OrderId = order, LineId = x.LineId, ServiceId = x.Service.Id,
      ServiceVersion = x.Service.Version, ServiceCode = x.Service.Code, ServiceName = x.Service.Name,
      ExpectedOrderVersion = x.ExpectedOrderVersion, MaterialOwnerKind = x.MaterialOwnerKind,
      MaterialOwnerId = x.MaterialOwnerId, ExternalLocationId = x.ExternalLocationId,
      ExternalLocationVersion = x.ExternalLocationVersion, OperatorPartyId = x.OperatorPartyId,
      OperatorPartyVersion = x.OperatorPartyVersion, SourcePhysicalRequestId = x.SourcePhysicalRequestId,
      SourcePhysicalRequestVersion = x.SourcePhysicalRequestVersion, AdmittedAtUtc = x.AdmittedAtUtc };
    private static StoredPurchasingSubcontractV1 Restore(PurchasingSubcontractOrderRow h,
        IReadOnlyList<PurchasingSubcontractOrderLineRow> lines, IReadOnlyList<PurchasingSubcontractAdmissionRow> admissions) =>
        new(RestoreOrder(h, lines), [.. admissions.Select(x => new PurchasingSubcontractAdmissionSnapshotStoreV1(
            x.LineId, new(PurchasingCommercialLineSubjectKindV1.Service, x.ServiceId, x.ServiceVersion, x.ServiceCode, x.ServiceName),
            x.ExpectedOrderVersion, x.MaterialOwnerId, x.MaterialOwnerKind, x.ExternalLocationId,
            x.ExternalLocationVersion, x.OperatorPartyId, x.OperatorPartyVersion, x.SourcePhysicalRequestId,
            x.SourcePhysicalRequestVersion, x.AdmittedAtUtc))]);
    private static SubcontractOrderV1 RestoreOrder(PurchasingSubcontractOrderRow h, IReadOnlyList<PurchasingSubcontractOrderLineRow> lines) =>
        SubcontractOrderV1.Restore(h.Id, h.CompanyId, h.SupplierPartyId, h.SupplierPartyVersion,
            h.CompanyAccountId, h.CompanyAccountVersion, (SubcontractOrderStatusV1)h.Status, h.Version,
            h.CreatedAtUtc, h.UpdatedAtUtc, [.. lines.Select(x => new SubcontractOrderLineV1(x.LineId, x.ServiceId,
                x.ServiceVersion, x.ServiceUnitId, x.ServiceQuantity, x.SourcePhysicalRequestId, x.SourcePhysicalRequestVersion))]);
    private static string Snapshot(SubcontractOrderV1 order) => JsonSerializer.Serialize(new
    { status = order.Status.ToString(), version = order.Version, lineIds = order.Lines.Select(x => x.LineId) }, Json);
    private void Audit(PurchasingWriteEnvelope envelope, Guid company, Guid id, string eventType, string operation,
        long? beforeVersion, long afterVersion, string before, string after, string reason) =>
        auditEvents.Enqueue(new(company, eventType, "Purchasing.SubcontractOrder", id.ToString("D"), operation,
            beforeVersion, afterVersion, AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode,
            envelope.RoleCodes, before, after, reason, null, AuditAiInvolvement.None, null, envelope.SourceChannel,
            envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey));
    private async Task SaveAsync(CancellationToken ct)
    {
        try { await dbContext.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException ex) { dbContext.ChangeTracker.Clear(); throw Error(PurchasingSubcontractStoreCodesV1.ConcurrentChange, ex); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { dbContext.ChangeTracker.Clear(); throw Error(PurchasingSubcontractStoreCodesV1.ConcurrentChange, ex); }
    }
}
