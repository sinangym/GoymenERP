using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.SalesContracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    public Task<SalesContractDiscountOriginalReceiptV1?> ReadContractDiscountDecisionReceiptAsync(SalesDraftWriteEnvelope envelope,
        string hash, string command, SalesContractCommandResult response, Func<CancellationToken, Task> freshFence, CancellationToken token)
    {
        RequireEnvelope(envelope); RequireHash(hash); RequireContractDiscountDecisionCommand(envelope, hash, command, writes: false);
        return WithContractDiscountDecisionReadAsync(() => ReadContractDiscountDecisionReceiptCoreAsync(envelope, hash, command, response, token),
            freshFence, token, allowOwnedContractReceipt: true);
    }
    private async Task<SalesContractDiscountOriginalReceiptV1?> ReadContractDiscountDecisionReceiptCoreAsync(SalesDraftWriteEnvelope envelope,
        string hash, string command, SalesContractCommandResult response, CancellationToken token)
    {
        var company = RequireResolvedCompany(); var key = IdempotencyKeyHash(envelope.IdempotencyKey); await Fence();
        var attempts = await dbContext.ContractAttempts.AsNoTracking().Where(x => x.CompanyId == company && x.CommandName == command && x.IdempotencyKeyHash == key)
            .Take(2).ToArrayAsync(token); await Fence();
        if (attempts.Length != 1 || attempts[0].ContractId is not { } id) return null;
        var events = await ContractDecisionAuditCandidatesAsync(company, id, command, key, token); await Fence();
        var current = await ReadContractDiscountDecisionSourceCoreAsync(id, token);
        return RestoreOriginalContractDiscountReceipt(attempts[0], events, envelope.ActorId.Trim(), key, hash, command, response, current);
        Task Fence() => ContractDiscountDecisionSourceFenceAsync(token);
    }
    private Task<AuditEvent[]> ContractDecisionAuditCandidatesAsync(Guid company, Guid id, string command, string key, CancellationToken token)
    {
        var entity = id.ToString("D"); var eventType = command == SalesContractCommands.ApproveDiscountV1 ? "sales.contract.discount-approved.v1" : "sales.contract.discount-rejected.v1";
        return dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company && x.EntityType == ContractEntityType && x.EntityId == entity
            && x.EventType == eventType && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token);
    }
    internal static SalesContractDiscountOriginalReceiptV1? RestoreOriginalContractDiscountReceipt(ContractAttemptRow attempt, IReadOnlyList<AuditEvent> events,
        string actor, string keyHash, string hash, string command, SalesContractCommandResult response, SalesContractDiscountDecisionSourceV1? current)
    {
        if (attempt is null || events is not { Count: 1 } || events[0] is null || current?.Contract is not { } item || response?.Contract is null
            || command is not (SalesContractCommands.ApproveDiscountV1 or SalesContractCommands.RejectDiscountV1)
            || attempt.CompanyId == Guid.Empty || attempt.ContractId is not { } id || id == Guid.Empty || attempt.Outcome != AttemptOutcomeRecorded
            || attempt.RefusalJson is not null || attempt.CommandName != command || attempt.ActorId != actor || string.IsNullOrWhiteSpace(actor)
            || attempt.RequestHash != hash || attempt.IdempotencyKeyHash != keyHash || string.IsNullOrWhiteSpace(attempt.CorrelationId)
            || response.OriginalCorrelationId != attempt.CorrelationId || current.CompanyId != attempt.CompanyId || item.Id != id
            || !SalesContractService.MatchesContractDecisionResponse(current, response.Contract)) return null;
        var audit = events[0]; var approve = command == SalesContractCommands.ApproveDiscountV1;
        if (audit.Id == Guid.Empty || audit.SubjectKind != AuditEvent.CompanySubject || audit.CompanyId != attempt.CompanyId || audit.EntityType != ContractEntityType
            || audit.EntityId != id.ToString("D") || audit.EventType != (approve ? "sales.contract.discount-approved.v1" : "sales.contract.discount-rejected.v1")
            || audit.Operation != (approve ? "ApproveContractDiscount" : "RejectContractDiscount") || audit.ActorType != AuditActorTypes.User || audit.ActorId != actor
            || audit.PermissionCode != SalesPermissionManifestV1.ContractsDiscountApprove || audit.Outcome != AuditOutcomes.Succeeded
            || audit.CorrelationId != attempt.CorrelationId || audit.IdempotencyKeyHash != keyHash || audit.SchemaVersion != 1
            || audit.IntegrityHash != AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope())
            || !TryContractDecisionAudit(attempt.CompanyId, audit.BeforeJson, out var before)
            || !TryContractDecisionAudit(attempt.CompanyId, audit.AfterJson, out var after)) return null;
        try
        {
            var b = before!.Contract; var a = after!.Contract;
            if (b.Id != id || b.Version < 1 || b.Version == long.MaxValue || b.State != SalesContractState.PendingDiscountApproval || b.Decision is not null
                || b.CreatedBy == actor || !b.DiscountApprovalRequired || audit.EntityVersionBefore != b.Version || audit.EntityVersionAfter != b.Version + 1
                || a.Decision is not { } decision || decision.DecidedBy != actor || decision.DecidedAtUtc != attempt.RecordedAtUtc || a.Version != b.Version + 1
                || !FloorJsonEquals(audit.AfterJson, ContractAuditState(item))) return null;
            var original = SalesContractDiscountDecisionDigestsV1.Freeze(before).Contract;
            if (approve) original.ApproveDiscount(b.Version, actor, attempt.RecordedAtUtc);
            else original.RejectDiscount(b.Version, actor, decision.RejectionReason, attempt.RecordedAtUtc);
            if (!FloorJsonEquals(audit.AfterJson, ContractAuditState(original))) return null;
            var expected = b.Version.ToString(CultureInfo.InvariantCulture);
            // The audit retains native trimmed reasons, not an invented raw request. Unprovable legacy spellings keep proof null.
            if (SalesContractHash.Compute(command, id, new(expected, decision.RejectionReason)) != hash) return null;
            var receipt = new SalesContractDiscountOriginalReceiptV1(attempt.CompanyId, id, command, actor, keyHash, hash, expected,
                a.Version.ToString(CultureInfo.InvariantCulture), attempt.CorrelationId, attempt.RecordedAtUtc, audit.Id, audit.IntegrityHash, before, after, "")
                { CurrentSource = SalesContractDiscountDecisionDigestsV1.Freeze(current), CurrentResponseDigest = SalesContractDiscountDecisionDigestsV1.CurrentResponseDigest(response) };
            return receipt with { OriginalDecisionDigest = SalesContractDiscountDecisionDigestsV1.OriginalDecisionDigest(receipt) };
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException or FormatException or OverflowException or JsonException) { return null; }
    }
    private static bool TryContractDecisionAudit(Guid company, string json, out SalesContractDiscountDecisionSourceV1? source)
    {
        source = null;
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            string[] keys = ["id","contractNo","partyId","companyAccountId","currencyId","moneyProfileId","moneyProfileVersion","moneyScale","validFrom","validThrough",
                "installments","discountRules","clauses","discountLimitPercent","discountApprovalRequired","state","version","createdBy","createdAtUtc","decidedBy","decidedAtUtc","rejectionReason"];
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != keys.Length || !keys.All(k => root.TryGetProperty(k, out _))) return false;
            var s = JsonSerializer.Deserialize<ContractDecisionAuditFacts>(json, SalesDraftJson.Options);
            if (s is null || !Enum.TryParse<SalesContractState>(s.State, out var state) || state.ToString() != s.State) return false;
            var row = new ContractRow { Id = s.Id, CompanyId = company, ContractNo = s.ContractNo, PartyId = s.PartyId, CompanyAccountId = s.CompanyAccountId,
                CurrencyId = s.CurrencyId, MoneyProfileId = s.MoneyProfileId, MoneyProfileVersion = s.MoneyProfileVersion, MoneyScale = s.MoneyScale,
                ValidFrom = s.ValidFrom, ValidThrough = s.ValidThrough, InstallmentsJson = s.Installments, DiscountRulesJson = s.DiscountRules, Clauses = s.Clauses,
                DiscountLimitPercent = s.DiscountLimitPercent is null ? null : SalesContractJson.ParseDecimal(s.DiscountLimitPercent, positive: false),
                DiscountApprovalRequired = s.DiscountApprovalRequired, State = (int)state, Version = s.Version, CreatedBy = s.CreatedBy, CreatedAtUtc = s.CreatedAtUtc,
                DecidedBy = s.DecidedBy, DecidedAtUtc = s.DecidedAtUtc, RejectionReason = s.RejectionReason, UpdatedAtUtc = s.DecidedAtUtc ?? s.CreatedAtUtc };
            var item = SalesContractRows.Restore(row);
            if (!FloorJsonEquals(json, ContractAuditState(item))) return false;
            source = new(company, item); return true;
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException or FormatException or OverflowException or JsonException or NotSupportedException) { return false; }
    }
    private sealed record ContractDecisionAuditFacts(Guid Id, int ContractNo, Guid PartyId, Guid CompanyAccountId, Guid CurrencyId,
        Guid MoneyProfileId, string MoneyProfileVersion, int MoneyScale, DateOnly ValidFrom, DateOnly? ValidThrough, string Installments, string DiscountRules,
        string? Clauses, string? DiscountLimitPercent, bool DiscountApprovalRequired, string State, long Version, string CreatedBy, DateTimeOffset CreatedAtUtc,
        string? DecidedBy, DateTimeOffset? DecidedAtUtc, string? RejectionReason);

    public Task<SalesContractDiscountOriginalVersionReadResultV1> ReadContractDiscountOriginalVersionAsync(Guid contractId, long expectedVersion,
        string actorId, Func<CancellationToken, Task> freshFence, CancellationToken token) =>
        WithContractDiscountDecisionReadAsync(() => ReadContractDiscountOriginalVersionCoreAsync(contractId, expectedVersion, actorId, token), freshFence, token);
    private async Task<SalesContractDiscountOriginalVersionReadResultV1> ReadContractDiscountOriginalVersionCoreAsync(Guid id, long expected, string actor, CancellationToken token)
    {
        var company = RequireResolvedCompany(); var entity = id.ToString("D"); await Fence();
        var candidates = await (from attempt in dbContext.ContractAttempts.AsNoTracking()
            join audit in dbContext.Set<AuditEvent>().AsNoTracking() on new { CompanyId = (Guid?)attempt.CompanyId, Key = attempt.IdempotencyKeyHash } equals new { audit.CompanyId, Key = audit.IdempotencyKeyHash }
            where attempt.CompanyId == company && attempt.ContractId == id && attempt.ActorId == actor && attempt.Outcome == AttemptOutcomeRecorded && attempt.RefusalJson == null
                && (attempt.CommandName == SalesContractCommands.ApproveDiscountV1 || attempt.CommandName == SalesContractCommands.RejectDiscountV1)
                && audit.CompanyId == company && audit.EntityType == ContractEntityType && audit.EntityId == entity && audit.ActorId == actor && audit.EntityVersionBefore == expected
                && (audit.EventType == "sales.contract.discount-approved.v1" || audit.EventType == "sales.contract.discount-rejected.v1")
            select attempt).Take(2).ToArrayAsync(token); await Fence();
        var current = await ReadContractDiscountDecisionSourceCoreAsync(id, token);
        if (current?.Contract.CreatedBy == actor) return new(SalesContractDiscountDecisionReadStateV1.Denied, null);
        if (candidates.Length == 0) return new(SalesContractDiscountDecisionReadStateV1.NotFound, null) { CurrentVersion = current?.Contract.Version };
        if (candidates.Length != 1 || current is null) return new(SalesContractDiscountDecisionReadStateV1.Unavailable, null);
        var a = candidates[0]; var events = await ContractDecisionAuditCandidatesAsync(company, id, a.CommandName, a.IdempotencyKeyHash, token); await Fence();
        var view = SalesContractService.ProjectContractDiscountDecision(current, SalesBusinessDay.DateOf(timeProvider.GetUtcNow()), SalesPriceDisclosure.Withheld).Contract;
        var receipt = RestoreOriginalContractDiscountReceipt(a, events, actor, a.IdempotencyKeyHash, a.RequestHash, a.CommandName, new(view, true, a.CorrelationId), current);
        return receipt?.ExpectedVersion == expected.ToString(CultureInfo.InvariantCulture)
            ? new(SalesContractDiscountDecisionReadStateV1.Found, receipt) : new(SalesContractDiscountDecisionReadStateV1.Unavailable, null);
        Task Fence() => ContractDiscountDecisionSourceFenceAsync(token);
    }
}
