using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// S-11 (SAT-D28, NS-13, SAT-D346): the sales contract half of the Sales store. The attempt rules are the risk exception's
/// (ADR-011 §2–§8) in the contract's own attempt table: (company, command, key hash) is the identity, the raw key is never
/// stored, the same key, content and actor answers the stored contract, another content or actor is one 409, a lost race stores
/// nothing. One save writes the contract (or its discount decision), the attempt and the audit event. UX_Contracts_No makes a
/// racing per-company number the save's concurrent change. Sales writes only its own tables.
/// </summary>
public sealed partial class SalesStore : ISalesContractStore
{
    private const string ContractEntityType = "Sales.Contract";

    public async Task<SalesContractReplay?> FindContractReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireContractCommand(commandName);
        RequireResolvedCompany();
        var attempt = await FindContractAttemptAsync(commandName, IdempotencyKeyHash(envelope.IdempotencyKey), cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        RequireSameContractRequest(attempt, envelope.ActorId.Trim(), requestHash);
        return await ReadContractReplayAsync(attempt, cancellationToken);
    }

    public async Task<SalesContractStoreResult> StoreContractAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Func<int, DateTimeOffset, SalesContract> build, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        RequireContractAudit(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindContractAttemptAsync(SalesContractCommands.CreateV1, keyHash, cancellationToken);
        if (attempt is not null)
        {
            return await ReplayContractAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        // Tenant-filtered: the company's next number; a racing number loses at UX_Contracts_No (the save's concurrent change).
        var last = await dbContext.Contracts.AsNoTracking().MaxAsync(row => (int?)row.ContractNo, cancellationToken) ?? 0;
        var now = timeProvider.GetUtcNow();
        var item = build(checked(last + 1), now) ?? throw new ArgumentException("Sözleşme kurulmadı.", nameof(build));
        RequireCompany(item.CompanyId);
        if (item.ContractNo != last + 1 || item.Version != 1 || item.Decision is not null
            || item.State == SalesContractState.DiscountRejected
            || !string.Equals(item.CreatedBy, actorId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Sözleşme şirketin sıradaki numarasıyla, yazma zarfının aktörü adına kaydedilir.", nameof(build));
        }

        dbContext.Contracts.Add(SalesContractRows.ToRow(item, envelope.CorrelationId));
        dbContext.ContractAttempts.Add(NewContractAttempt(companyId, SalesContractCommands.CreateV1, keyHash, requestHash, actorId,
            AttemptOutcomeRecorded, item.Id, null, envelope.CorrelationId, now));
        EnqueueContractAudit(envelope, companyId, audit, item, null, "{}");
        await SaveAsync(cancellationToken);
        return new SalesContractStoreResult(item, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task RecordContractRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid contractId, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireContractCommand(commandName);
        if (commandName == SalesContractCommands.CreateV1)
        {
            throw new ArgumentException("Yalnız bir iskonto kararı terminal ret kaydeder; reddedilen kayıt hiçbir şey kaydetmez.", nameof(commandName));
        }

        var companyId = RequireResolvedCompany();
        if (refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
            || refusal.Errors.Any(error => error is null || string.IsNullOrWhiteSpace(error.Field) || string.IsNullOrWhiteSpace(error.Code)))
        {
            throw new ArgumentException("Terminal ret bir durum, bir kod ve tam alan hataları taşır.", nameof(refusal));
        }

        if (contractId == Guid.Empty)
        {
            throw new ArgumentException("Sözleşme kimliği boş olamaz.", nameof(contractId));
        }

        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await FindContractAttemptAsync(commandName, keyHash, cancellationToken) is not null)
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        dbContext.ContractAttempts.Add(NewContractAttempt(companyId, commandName, keyHash, requestHash, envelope.ActorId.Trim(),
            AttemptOutcomeRefused, contractId, JsonSerializer.Serialize(refusal, Json), envelope.CorrelationId, timeProvider.GetUtcNow()));
        await SaveAsync(cancellationToken);
    }

    public async Task<SalesContractStoreResult?> DecideContractAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Guid contractId, Action<SalesContract, DateTimeOffset> mutate, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        RequireContractAudit(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireContractCommand(commandName);
        if (commandName == SalesContractCommands.CreateV1)
        {
            throw new ArgumentException("Kayıt kendi mağaza yöntemini kullanır.", nameof(commandName));
        }

        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindContractAttemptAsync(commandName, keyHash, cancellationToken);
        if (attempt is not null)
        {
            return await ReplayContractAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        if (contractId == Guid.Empty)
        {
            return null;
        }

        var row = await dbContext.Contracts.SingleOrDefaultAsync(candidate => candidate.Id == contractId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var item = SalesContractRows.Restore(row);
        var before = ContractAuditState(item);
        var versionBefore = item.Version;
        var now = timeProvider.GetUtcNow();
        mutate(item, now);
        if (item.Version != versionBefore + 1 || item.Id != row.Id || item.CompanyId != companyId
            || item.State == SalesContractState.PendingDiscountApproval
            || !string.Equals(item.Decision?.DecidedBy, actorId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Sözleşme iskonto kararı tam olarak bir sürüm ilerletir, aktörün adına verilir ve kaydı değiştirmez.");
        }

        SalesContractRows.ApplyDecision(row, item);
        dbContext.ContractAttempts.Add(NewContractAttempt(companyId, commandName, keyHash, requestHash, actorId, AttemptOutcomeRecorded,
            item.Id, null, envelope.CorrelationId, now));
        EnqueueContractAudit(envelope, companyId, audit, item, versionBefore, before);
        await SaveAsync(cancellationToken);
        return new SalesContractStoreResult(item, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<SalesContract?> FindContractAsync(Guid contractId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Contracts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == contractId, cancellationToken);
        return row is null ? null : SalesContractRows.Restore(row);
    }

    public async Task<IReadOnlyList<SalesContract>> ListContractsAsync(int limit, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (limit < 1 || limit > SalesContractApiV1.MaximumListed)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var rows = await dbContext.Contracts.AsNoTracking().OrderByDescending(row => row.ContractNo).Take(limit)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(SalesContractRows.Restore)];
    }

    private Task<ContractAttemptRow?> FindContractAttemptAsync(string commandName, string keyHash, CancellationToken cancellationToken) =>
        dbContext.ContractAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);

    private static ContractAttemptRow NewContractAttempt(Guid companyId, string commandName, string keyHash, string requestHash,
        string actorId, int outcome, Guid? contractId, string? refusalJson, string correlationId, DateTimeOffset now) => new()
    {
        CompanyId = companyId,
        CommandName = commandName,
        IdempotencyKeyHash = keyHash,
        RequestHash = requestHash,
        ActorId = actorId,
        Outcome = outcome,
        ContractId = contractId,
        RefusalJson = refusalJson,
        CorrelationId = correlationId.Trim(),
        RecordedAtUtc = now,
    };

    private void EnqueueContractAudit(SalesDraftWriteEnvelope envelope, Guid companyId, SalesDraftStateAudit audit,
        SalesContract item, long? versionBefore, string before) =>
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            audit.EventType,
            ContractEntityType,
            item.Id.ToString("D"),
            audit.Operation,
            EntityVersionBefore: versionBefore,
            EntityVersionAfter: item.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            before,
            ContractAuditState(item),
            audit.Reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.IdempotencyKey));

    private static void RequireContractCommand(string commandName)
    {
        if (!SalesContractCommands.IsContractCommand(commandName))
        {
            throw new ArgumentException("Yalnız sözleşme kayıt, iskonto onay ve ret komutları bu deneme kaydını taşır.", nameof(commandName));
        }
    }

    private static void RequireContractAudit(SalesDraftStateAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("Sözleşmenin audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
        }
    }

    private static void RequireSameContractRequest(ContractAttemptRow attempt, string actorId, string requestHash)
    {
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch,
                "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }
    }

    private async Task<SalesContractReplay> ReadContractReplayAsync(ContractAttemptRow attempt, CancellationToken cancellationToken)
    {
        if (attempt.Outcome == AttemptOutcomeRecorded && attempt.ContractId is { } contractId && attempt.RefusalJson is null)
        {
            var row = await dbContext.Contracts.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == contractId, cancellationToken)
                ?? throw new InvalidOperationException("Kayıtlı deneme kaydının sözleşmesi bulunamadı.");
            return new SalesContractReplay(SalesContractRows.Restore(row), null, attempt.CorrelationId);
        }

        var refusal = attempt.RefusalJson is null ? null : JsonSerializer.Deserialize<SalesDraftTerminalRefusal>(attempt.RefusalJson, Json);
        if (attempt.Outcome != AttemptOutcomeRefused || refusal is null || refusal.Status is < 400 or > 599
            || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null)
        {
            throw new InvalidOperationException("Sözleşme deneme kaydı okunamadı.");
        }

        return new SalesContractReplay(null, refusal, attempt.CorrelationId);
    }

    private async Task<SalesContractStoreResult> ReplayContractAttemptAsync(ContractAttemptRow attempt, string actorId,
        string requestHash, CancellationToken cancellationToken)
    {
        RequireSameContractRequest(attempt, actorId, requestHash);
        var replay = await ReadContractReplayAsync(attempt, cancellationToken);
        if (replay.Contract is null)
        {
            // ADR-011 §7: the key holds a recorded terminal refusal; the caller replays it through FindContractReplayAsync.
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
        }

        return new SalesContractStoreResult(replay.Contract, SalesDraftWriteOutcome.Replayed, attempt.CorrelationId);
    }

    /// <summary>The contract's facts, its decision and the limit snapshot (NS-13, SAT-D346); amounts and rates as invariant text.</summary>
    private static string ContractAuditState(SalesContract item) => JsonSerializer.Serialize(new
    {
        item.Id,
        item.ContractNo,
        item.PartyId,
        item.CompanyAccountId,
        item.CurrencyId,
        item.MoneyProfileId,
        item.MoneyProfileVersion,
        item.MoneyScale,
        item.ValidFrom,
        item.ValidThrough,
        installments = SalesContractRows.SerializeInstallments(item.Installments),
        discountRules = SalesContractRows.SerializeDiscountRules(item.DiscountRules),
        item.Clauses,
        discountLimitPercent = item.DiscountLimitPercent?.ToString(CultureInfo.InvariantCulture),
        item.DiscountApprovalRequired,
        state = item.State.ToString(),
        version = item.Version,
        item.CreatedBy,
        item.CreatedAtUtc,
        decidedBy = item.Decision?.DecidedBy,
        decidedAtUtc = item.Decision?.DecidedAtUtc,
        rejectionReason = item.Decision?.RejectionReason,
    }, Json);
}
