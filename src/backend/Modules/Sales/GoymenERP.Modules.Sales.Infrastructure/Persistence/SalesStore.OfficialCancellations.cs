using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>SAT-D96 / SAT-SC34. This owner commits Sales fact, share release, immutable answer,
/// original Finance source, outbox and Audit together. It never calls an external cancellation system.</summary>
public sealed partial class SalesStore : ISalesOfficialInvoiceCancellationStoreV1
{
    public async Task<SalesOfficialCancellationCandidatesV1> ListCandidatesAsync(SalesOfficialInvoiceKindV1 kind, Guid? after,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany(); await OfficialFence(companyId, freshFence, cancellationToken);
        const int pageSize = 50;
        if (kind == SalesOfficialInvoiceKindV1.Normal)
        {
            var query = dbContext.Invoices.AsNoTracking().Where(x => x.State == 6 || x.State == 7);
            if (after is { } cursor) query = query.Where(x => x.Id.CompareTo(cursor) > 0);
            var rows = await query.OrderBy(x => x.Id).Take(pageSize + 1).Select(x => new { x.Id, x.DocumentNo, x.Version, x.State }).ToArrayAsync(cancellationToken);
            await OfficialFence(companyId, freshFence, cancellationToken);
            var ids = rows.Take(pageSize).Select(x => x.Id).ToArray();
            var matches = await dbContext.InvoiceMatches.AsNoTracking().Where(x => ids.Contains(x.InvoiceId)).ToArrayAsync(cancellationToken);
            await OfficialFence(companyId, freshFence, cancellationToken);
            var items = rows.Take(pageSize).Select(x => { var match = matches.Single(m => m.InvoiceId == x.Id);
                return new SalesOfficialCancellationCandidateV1(x.Id, kind, x.DocumentNo, Number(x.Version), ((SalesInvoiceState)x.State).ToString(), match.OfficialInvoiceNo, match.Ettn); }).ToArray();
            return new(items, rows.Length > pageSize ? items[^1].InvoiceId.ToString("D") : null);
        }
        if (kind != SalesOfficialInvoiceKindV1.Subcontract) throw InvalidOfficial();
        var serviceQuery = dbContext.SubcontractServiceInvoices.AsNoTracking().Where(x => x.State == 6 || x.State == 7);
        if (after is { } last) serviceQuery = serviceQuery.Where(x => x.Id.CompareTo(last) > 0);
        var services = await serviceQuery.OrderBy(x => x.Id).Take(pageSize + 1).ToArrayAsync(cancellationToken);
        await OfficialFence(companyId, freshFence, cancellationToken);
        var result = new List<SalesOfficialCancellationCandidateV1>();
        foreach (var row in services.Take(pageSize))
        {
            var invoice = await RestoreSubcontractServiceInvoiceAsync(row, cancellationToken);
            await OfficialFence(companyId, freshFence, cancellationToken);
            if (invoice.Match is not { } match || invoice.Finalization is null) throw InvalidOfficial();
            result.Add(new(invoice.Id, kind, invoice.DocumentNo, Number(invoice.Version), invoice.State.ToString(), match.OfficialNo, match.Ettn));
        }
        return new(result, services.Length > pageSize ? result[^1].InvoiceId.ToString("D") : null);
    }

    public async Task<SalesOfficialInvoiceCancellationResultV1?> FindReplayAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        RequireOfficialCommand(envelope, requestHash, commandName);
        var companyId = RequireResolvedCompany();
        var hash = IdempotencyKeyHash(envelope.IdempotencyKey);
        await OfficialFence(companyId, freshFence, cancellationToken);
        var row = await dbContext.OfficialInvoiceCancellationAttempts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CommandName == commandName && x.KeyHash == hash, cancellationToken);
        await OfficialFence(companyId, freshFence, cancellationToken);
        if (row is null) return null;
        if (row.ActorId != envelope.ActorId || row.RequestHash != requestHash)
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch, "Aynı anahtarın aktörü veya içeriği değiştirilemez.");
        if (row.RefusalJson is { } refusalJson)
        {
            var refusal = JsonSerializer.Deserialize<SalesDraftTerminalRefusal>(refusalJson, Json)
                ?? throw new InvalidOperationException("Resmî iptal ret kaydı okunamadı.");
            if (refusal.Status is not (409 or 422) || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null) throw InvalidOfficial();
            throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors, true, row.CorrelationId);
        }
        var view = JsonSerializer.Deserialize<SalesOfficialInvoiceCancellationViewV1>(row.ResultJson!, Json) ?? throw InvalidOfficial();
        ValidateOfficialView(view, companyId, row.CancellationId ?? Guid.Empty);
        return new(view, true, row.CorrelationId);
    }

    public async Task<SalesOfficialCancellationTargetV1?> ReadTargetAsync(SalesOfficialInvoiceKindV1 kind, Guid invoiceId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadOfficialTarget(kind, invoiceId, freshFence, cancellationToken);
        return loaded?.Target;
    }

    public Task<SalesOfficialInvoiceCancellationResultV1> RecordAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        SalesOfficialInvoiceKindV1 kind, Guid invoiceId, long expectedVersion, string reason,
        SalesOfficialInvoiceCancellationEvidenceV1 evidence, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default) => OfficialTransaction(async () =>
    {
        const string command = SalesOfficialInvoiceCancellationCommandsV1.Record;
        RequireOfficialCommand(envelope, requestHash, command);
        var companyId = RequireResolvedCompany();
        var replay = await FindReplayAsync(envelope, requestHash, command, freshFence, cancellationToken);
        if (replay is not null) return replay;
        if (kind == SalesOfficialInvoiceKindV1.Subcontract)
        {
            // Same protocol as invoice create/correction: Sales claim range before the invoice header.
            var key = await dbContext.SubcontractServiceInvoices.AsNoTracking().Where(x => x.Id == invoiceId)
                .Select(x => new { x.LoadingInstructionId, x.WeighingId }).SingleOrDefaultAsync(cancellationToken);
            await OfficialFence(companyId, freshFence, cancellationToken);
            if (key is null) throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Fatura kaynağı değişti.");
            await SalesSubcontractInvoiceClaimReadSession.ReadRangeAsync(dbContext, companyId, key.LoadingInstructionId,
                key.WeighingId, cancellationToken);
            await OfficialFence(companyId, freshFence, cancellationToken);
        }
        var loaded = await LoadOfficialTarget(kind, invoiceId, freshFence, cancellationToken) ?? throw InvalidOfficial();
        var target = loaded.Target;
        var now = timeProvider.GetUtcNow();
        var cancellationId = Guid.NewGuid();
        if (!Guid.TryParseExact(envelope.ActorId, "D", out var actorId) || actorId == Guid.Empty) throw InvalidOfficial();
        if (loaded.Normal is { } normal) normal.RecordOfficialCancellation(expectedVersion, envelope.ActorId, reason, evidence, now);
        else loaded.Subcontract!.RecordOfficialCancellation(expectedVersion, envelope.ActorId, reason, evidence, now);
        var nextVersion = checked(target.Version + 1);
        var view = new SalesOfficialInvoiceCancellationViewV1(companyId, cancellationId, invoiceId, kind,
            Number(nextVersion), target.Admission.InvoiceVersion, target.Admission.AdmissionId, target.OfficialInvoiceNo,
            target.Ettn, envelope.ActorId, now, reason, evidence);
        ValidateOfficialView(view, companyId, cancellationId);
        dbContext.OfficialInvoiceCancellations.Add(new()
        {
            CompanyId = companyId, CancellationId = cancellationId, InvoiceId = invoiceId, Kind = (int)kind,
            NormalInvoiceId = kind == SalesOfficialInvoiceKindV1.Normal ? invoiceId : null,
            SubcontractInvoiceId = kind == SalesOfficialInvoiceKindV1.Subcontract ? invoiceId : null,
            FinalizedVersion = long.Parse(target.Admission.InvoiceVersion, CultureInfo.InvariantCulture), InvoiceVersion = nextVersion,
            AdmissionId = target.Admission.AdmissionId, ActorId = envelope.ActorId, RecordedAtUtc = now,
            CorrelationId = envelope.CorrelationId, ViewJson = JsonSerializer.Serialize(view, Json),
            AdmissionJson = JsonSerializer.Serialize(target.Admission, Json),
        });
        // The SQL header guard must see this same-transaction immutable fact before its UPDATE.
        // This first save does not enqueue audit or commit the owner transaction.
        await SaveAsync(cancellationToken);
        await OfficialFence(companyId, freshFence, cancellationToken);
        if (loaded.Normal is { } normalInvoice)
        {
            var header = await dbContext.Invoices.SingleAsync(x => x.Id == invoiceId, cancellationToken);
            await OfficialFence(companyId, freshFence, cancellationToken);
            if (header.Version != expectedVersion) throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Fatura değişti.");
            SalesInvoiceRows.ApplyDecision(header, normalInvoice);
        }
        else
        {
            var invoice = loaded.Subcontract!;
            var header = await dbContext.SubcontractServiceInvoices.SingleAsync(x => x.Id == invoiceId, cancellationToken);
            await OfficialFence(companyId, freshFence, cancellationToken);
            if (header.Version != expectedVersion) throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Fason fatura değişti.");
            var sequence = await dbContext.SubcontractInvoiceLifecycles.Where(x => x.InvoiceId == invoiceId)
                .Select(x => (long?)x.Sequence).MaxAsync(cancellationToken) ?? 0;
            await OfficialFence(companyId, freshFence, cancellationToken);
            header.State = (int)invoice.State; header.Version = invoice.Version;
            AppendSubcontractLifecycle(invoice, checked(sequence + 1), envelope.CorrelationId, now);
        }
        dbContext.OutboxMessages.Add(new SalesOutboxRow
        {
            Id = cancellationId, CompanyId = companyId, EventType = SalesOfficialInvoiceCancellationCommandsV1.EventType,
            SchemaVersion = 1, SourceOperation = "OfficialCancellation", AggregateType = kind == SalesOfficialInvoiceKindV1.Normal
                ? InvoiceEntityType : SubcontractServiceInvoiceEntityType, AggregateId = invoiceId, AggregateVersion = nextVersion,
            Sequence = 1, PayloadJson = JsonSerializer.Serialize(new { companyId, cancellationId, invoiceId,
                admissionId = target.Admission.AdmissionId, finalizedVersion = long.Parse(target.Admission.InvoiceVersion, CultureInfo.InvariantCulture),
                cancelledBy = actorId, cancelledAtUtc = now, reason }, Json),
            OccurredAtUtc = now, RecordedAtUtc = now, CorrelationId = envelope.CorrelationId, CausationId = envelope.CausationId,
        });
        AddOfficialAttempt(envelope, requestHash, command, view, null, now);
        OfficialAudit(envelope, view, "OfficialCancellation", expectedVersion, nextVersion, "{}", now);
        await SaveOfficialAudit(companyId, freshFence, cancellationToken);
        return new(view, false, envelope.CorrelationId);
    }, freshFence, cancellationToken);

    public async Task<SalesOfficialCancellationDeliveryV1?> ReadForInvoiceAsync(SalesOfficialInvoiceKindV1 kind, Guid invoiceId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        await OfficialFence(companyId, freshFence, cancellationToken);
        var id = await dbContext.OfficialInvoiceCancellations.AsNoTracking().Where(x => x.Kind == (int)kind && x.InvoiceId == invoiceId)
            .Select(x => (Guid?)x.CancellationId).SingleOrDefaultAsync(cancellationToken);
        await OfficialFence(companyId, freshFence, cancellationToken);
        return id is { } recorded ? await ReadDeliveryAsync(recorded, freshFence, cancellationToken) : null;
    }

    public async Task<SalesOfficialCancellationDeliveryV1?> ReadDeliveryAsync(Guid cancellationId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        await OfficialFence(companyId, freshFence, cancellationToken);
        var row = await dbContext.OfficialInvoiceCancellations.AsNoTracking().SingleOrDefaultAsync(x => x.CancellationId == cancellationId, cancellationToken);
        await OfficialFence(companyId, freshFence, cancellationToken);
        if (row is null) return null;
        var view = JsonSerializer.Deserialize<SalesOfficialInvoiceCancellationViewV1>(row.ViewJson, Json) ?? throw InvalidOfficial();
        var admission = JsonSerializer.Deserialize<SalesOfficialCancellationAdmissionV1>(row.AdmissionJson, Json) ?? throw InvalidOfficial();
        ValidateOfficialView(view, companyId, cancellationId);
        if (row.InvoiceId != view.InvoiceId || row.Kind != (int)view.Kind || Number(row.InvoiceVersion) != view.InvoiceVersion
            || Number(row.FinalizedVersion) != view.FinalizedVersion || row.AdmissionId != view.AdmissionId
            || row.ActorId != view.RecordedBy || row.RecordedAtUtc != view.RecordedAtUtc
            || admission.InvoiceId != row.InvoiceId || admission.AdmissionId != row.AdmissionId
            || admission.InvoiceVersion != view.FinalizedVersion || admission.OfficialInvoiceNo != view.OfficialInvoiceNo
            || admission.Ettn != view.Ettn) throw InvalidOfficial();
        var target = await LoadOfficialTarget(view.Kind, view.InvoiceId, freshFence, cancellationToken) ?? throw InvalidOfficial();
        if (target.Target.Version != row.InvoiceVersion || target.Target.Admission != admission
            && JsonSerializer.Serialize(target.Target.Admission, Json) != row.AdmissionJson) throw InvalidOfficial();
        var decision = target.Normal?.Decision ?? target.Subcontract!.Decision;
        if ((int)(target.Normal?.State ?? (SalesInvoiceState)(int)target.Subcontract!.State) != 7
            || decision.CancelledBy != view.RecordedBy || decision.CancelledAtUtc != view.RecordedAtUtc || decision.CancellationReason != view.Reason)
            throw InvalidOfficial();
        var last = await dbContext.OfficialInvoiceCancellationDeliveries.AsNoTracking().Where(x => x.CancellationId == cancellationId)
            .OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(cancellationToken);
        await OfficialFence(companyId, freshFence, cancellationToken);
        if (last is not null)
        {
            var delivered = JsonSerializer.Deserialize<SalesOfficialInvoiceCancellationViewV1>(last.ViewJson, Json) ?? throw InvalidOfficial();
            ValidateOfficialView(delivered, companyId, cancellationId);
            if (delivered.DeliveryVersion != last.Sequence || (delivered with { DeliveryVersion = 0, FinanceStatus = "Pending", FinanceReason = null }) != view)
                throw InvalidOfficial();
            view = delivered;
        }
        return new(view, admission);
    }

    public Task<SalesOfficialInvoiceCancellationResultV1> RecordDeliveryAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Guid cancellationId, long expectedDeliveryVersion, bool releaseActiveAllocations, SalesFinanceCancellationAnswerV1 answer,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default) => OfficialTransaction(
            () => RecordDeliveryCoreAsync(envelope, requestHash, cancellationId, expectedDeliveryVersion,
                releaseActiveAllocations, answer, freshFence, cancellationToken), freshFence, cancellationToken);

    private async Task<SalesOfficialInvoiceCancellationResultV1> RecordDeliveryCoreAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Guid cancellationId, long expectedDeliveryVersion, bool releaseActiveAllocations, SalesFinanceCancellationAnswerV1 answer,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken)
    {
        const string command = SalesOfficialInvoiceCancellationCommandsV1.RetryFinance;
        RequireOfficialCommand(envelope, requestHash, command);
        var replay = await FindReplayAsync(envelope, requestHash, command, freshFence, cancellationToken);
        if (replay is not null) return replay;
        var source = await ReadDeliveryAsync(cancellationId, freshFence, cancellationToken) ?? throw InvalidOfficial();
        if (source.Cancellation.DeliveryVersion != expectedDeliveryVersion)
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Finans teslim denemesi değişti; yeniden okuyun.");
        if (answer.Status is not ("Pending" or "Cancelled" or "AllocationConfirmationRequired")
            || source.Cancellation.FinanceStatus == "Cancelled" && answer.Status != "Cancelled") throw InvalidOfficial();
        var view = source.Cancellation with { DeliveryVersion = checked(expectedDeliveryVersion + 1), FinanceStatus = answer.Status, FinanceReason = answer.Reason };
        var now = timeProvider.GetUtcNow();
        dbContext.OfficialInvoiceCancellationDeliveries.Add(new() { CompanyId = view.CompanyId, CancellationId = cancellationId,
            Sequence = view.DeliveryVersion, ActorId = envelope.ActorId, CorrelationId = envelope.CorrelationId, RecordedAtUtc = now,
            ReleaseActiveAllocations = releaseActiveAllocations, ViewJson = JsonSerializer.Serialize(view, Json) });
        AddOfficialAttempt(envelope, requestHash, command, view, null, now);
        OfficialAudit(envelope, view, "OfficialCancellationFinanceRetry", null, null, JsonSerializer.Serialize(source.Cancellation, Json), now);
        await SaveOfficialAudit(view.CompanyId, freshFence, cancellationToken);
        return new(view, false, envelope.CorrelationId);
    }

    public async Task<SalesOfficialInvoiceCancellationResultV1> RecordRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, SalesDraftTerminalRefusal refusal, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default)
    {
        var result = await OfficialTransaction(async () =>
        {
            RequireOfficialCommand(envelope, requestHash, commandName);
            if (refusal.Status is not (409 or 422) || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null) throw InvalidOfficial();
            var replay = await FindReplayAsync(envelope, requestHash, commandName, freshFence, cancellationToken);
            if (replay is not null) return replay;
            AddOfficialAttempt(envelope, requestHash, commandName, null, refusal, timeProvider.GetUtcNow());
            await SaveAsync(cancellationToken);
            await OfficialFence(RequireResolvedCompany(), freshFence, cancellationToken);
            return null;
        }, freshFence, cancellationToken);
        return result ?? throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors, false, envelope.CorrelationId);
    }

    private async Task<OfficialTarget?> LoadOfficialTarget(SalesOfficialInvoiceKindV1 kind, Guid id,
        Func<CancellationToken, Task> fence, CancellationToken ct)
    {
        var companyId = RequireResolvedCompany();
        await OfficialFence(companyId, fence, ct);
        if (kind == SalesOfficialInvoiceKindV1.Normal)
        {
            var row = await dbContext.Invoices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            await OfficialFence(companyId, fence, ct);
            if (row is null) return null;
            var lines = await dbContext.InvoiceLines.AsNoTracking().Where(x => x.InvoiceId == id).ToListAsync(ct);
            await OfficialFence(companyId, fence, ct);
            var invoice = SalesInvoiceRows.Restore(row, lines);
            var match = await dbContext.InvoiceMatches.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == id, ct);
            await OfficialFence(companyId, fence, ct);
            var final = await dbContext.InvoiceFinalizations.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == id, ct);
            await OfficialFence(companyId, fence, ct);
            if (match is null || final is null) throw new GoymenERP.Modules.Sales.Domain.SalesRuleException(
                GoymenERP.Modules.Sales.Domain.SalesRuleCodes.StateConflict, "Fatura kesinleşmiş değil.");
            invoice.RestoreMatch(SalesInvoiceRows.ToMatchFacts(match));
            invoice.RestoreFinalization(SalesInvoiceRows.ToFinalizationFacts(final));
            return new(new(companyId, id, kind, row.Version, match.OfficialInvoiceNo, match.Ettn,
                Admission(invoice.Id, invoice.Header.PartyId, invoice.Header.CompanyAccountId, invoice.Header.CurrencyId,
                    invoice.Header.Rate?.Rate, invoice.Finalization!, match.OfficialInvoiceNo, match.Ettn)), invoice, null);
        }
        if (kind != SalesOfficialInvoiceKindV1.Subcontract) throw InvalidOfficial();
        var serviceRow = await dbContext.SubcontractServiceInvoices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        await OfficialFence(companyId, fence, ct);
        if (serviceRow is null) return null;
        var service = await RestoreSubcontractServiceInvoiceAsync(serviceRow, ct);
        await OfficialFence(companyId, fence, ct);
        if (service.Match is not { } source || service.Finalization is not { } finalization) throw new GoymenERP.Modules.Sales.Domain.SalesRuleException(
            GoymenERP.Modules.Sales.Domain.SalesRuleCodes.StateConflict, "Fason fatura kesinleşmiş değil.");
        return new(new(companyId, id, kind, service.Version, source.OfficialNo, source.Ettn,
            Admission(service.Id, service.Header.PartyId, service.Header.CompanyAccountId, service.Header.CurrencyId, service.Rate?.Rate, finalization, source.OfficialNo, source.Ettn)), null, service);
    }
    private static SalesOfficialCancellationAdmissionV1 Admission(Guid invoiceId, Guid partyId, Guid accountId, Guid currencyId,
        decimal? rate, SalesInvoiceFinalization final, string officialNo, string ettn) => new(final.AdmissionId, invoiceId, Number(final.FinalizedVersion), partyId,
        accountId, currencyId, rate, final.Plan.Select(x => new SalesFinanceInstalmentV1(x.OpenItemId, x.InstallmentId,
            x.SequenceNo, x.DueDate, Decimal(x.Amount), x.FunctionalAmount is { } amount ? Decimal(amount) : null)).ToArray(),
        final.FinanceStatus == SalesInvoiceFinanceStatus.Admitted, officialNo, ettn);
    private async Task<T> OfficialTransaction<T>(Func<Task<T>> action, Func<CancellationToken, Task> fence, CancellationToken ct)
    {
        var writer = officialCancellationAuditWriter ?? throw new SalesDraftStoreException(SalesDraftProblemCodesV1.OwnerReferenceUnavailable, "Denetim işlem yazıcısı yok.");
        if (writer.HasPendingDrafts) throw InvalidOfficial();
        var companyId = RequireResolvedCompany();
        try { return await InSubcontractTransactionAsync(async () => { await OfficialFence(companyId, fence, ct); var result = await action(); await OfficialFence(companyId, fence, ct); return result; }, ct); }
        finally { writer.DiscardPending(); }
    }
    private async Task SaveOfficialAudit(Guid companyId, Func<CancellationToken, Task> fence, CancellationToken ct)
    {
        await OfficialFence(companyId, fence, ct);
        // Flush through the Audit owner before Sales SaveChanges: the legacy interceptor must not
        // drain this command's drafts into the Sales sidecar. Both writes still share this owner TX.
        await officialCancellationAuditWriter!.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), ct);
        await OfficialFence(companyId, fence, ct);
        await SaveAsync(ct);
        await OfficialFence(companyId, fence, ct);
    }
    private async Task OfficialFence(Guid id, Func<CancellationToken, Task> fence, CancellationToken ct)
    { RequireCompany(id); await fence(ct); RequireCompany(id); }
    private void AddOfficialAttempt(SalesDraftWriteEnvelope envelope, string hash, string command,
        SalesOfficialInvoiceCancellationViewV1? view, SalesDraftTerminalRefusal? refusal, DateTimeOffset now) =>
        dbContext.OfficialInvoiceCancellationAttempts.Add(new() { CompanyId = RequireResolvedCompany(), CommandName = command,
            KeyHash = IdempotencyKeyHash(envelope.IdempotencyKey), RequestHash = hash, ActorId = envelope.ActorId,
            CancellationId = view?.CancellationId, CorrelationId = envelope.CorrelationId, RecordedAtUtc = now,
            ResultJson = view is null ? null : JsonSerializer.Serialize(view, Json),
            RefusalJson = refusal is null ? null : JsonSerializer.Serialize(refusal, Json) });
    private void OfficialAudit(SalesDraftWriteEnvelope envelope, SalesOfficialInvoiceCancellationViewV1 view,
        string operation, long? before, long? after, string beforeJson, DateTimeOffset now)
    {
        auditEvents.Enqueue(new AuditEventDraft(view.CompanyId, "sales.invoice.official-cancellation.recorded.v1", "Sales.Invoice",
            view.InvoiceId.ToString("D"), operation, before, after, AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode,
            envelope.RoleCodes, beforeJson, JsonSerializer.Serialize(view, Json), view.Reason, null, AuditAiInvolvement.None,
            null, envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey));
        _ = now;
    }
    private void RequireOfficialCommand(SalesDraftWriteEnvelope envelope, string hash, string command)
    {
        RequireEnvelope(envelope); RequireHash(hash); RequireResolvedCompany();
        if (command is not (SalesOfficialInvoiceCancellationCommandsV1.Record or SalesOfficialInvoiceCancellationCommandsV1.RetryFinance)
            || envelope.PermissionCode != SalesPermissionManifestV1.InvoicesFinalize) throw InvalidOfficial();
    }
    internal static void ValidateOfficialView(SalesOfficialInvoiceCancellationViewV1 view, Guid companyId, Guid id)
    {
        if (view.CompanyId != companyId || view.CancellationId != id || id == Guid.Empty || view.InvoiceId == Guid.Empty
            || !Enum.IsDefined(view.Kind) || !long.TryParse(view.FinalizedVersion, out var finalized) || finalized < 1
            || view.FinalizedVersion != Number(finalized)
            || view.InvoiceVersion != Number(checked(finalized + 1)) || view.AdmissionId == Guid.Empty
            || !Guid.TryParseExact(view.RecordedBy, "D", out var actor) || actor == Guid.Empty
            || view.RecordedAtUtc == default || view.RecordedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(view.Reason)
            || view.Evidence is null || !view.Evidence.IsValidFor(companyId, view.InvoiceId, view.OfficialInvoiceNo, view.Ettn)
            || view.Evidence.OfficiallyCancelledAtUtc > view.RecordedAtUtc || view.DeliveryVersion < 0
            || view.FinanceStatus is not ("Pending" or "Cancelled" or "AllocationConfirmationRequired")) throw InvalidOfficial();
    }
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Decimal(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static InvalidOperationException InvalidOfficial() => new("Resmî iptal kaydı okunamadı.");
    private sealed record OfficialTarget(SalesOfficialCancellationTargetV1 Target, SalesInvoice? Normal, SalesSubcontractServiceInvoice? Subcontract);
}
