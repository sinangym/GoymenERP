using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// F-027 / DEC-CAR-003 / CARI-AC-016: making a cari passive needs no separate approval. The set-active-state command
/// already carries the permission, the reason, the expected version, the Idempotency-Key and the audit event; this part
/// adds the <see cref="CounterpartyPartyDeactivatedOutboxV1"/> row to the same unit of work, so the account change, the
/// command execution, the audit event and the event row commit (or roll back) together.
/// </summary>
internal sealed partial class CounterpartyCommandService
{
    private static readonly JsonSerializerOptions OutboxJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Adds the deactivation event for an account that has just moved from active to passive. The caller decides that the
    /// state really changed; a no-op save, a reactivation, a refusal or a replay never reaches here. Sequence and aggregate
    /// version are the account version after the change, so one account version yields at most one event (unique index).
    /// </summary>
    private void EnqueuePartyDeactivatedEvent(
        CompanyPartyAccount account,
        string reason,
        CounterpartyCommandMetadata metadata)
    {
        if (account.IsActive)
        {
            throw new InvalidOperationException("Aktif cari hesabı için pasife alma olayı yazılamaz.");
        }

        var now = timeProvider.GetUtcNow();
        var payload = new CounterpartyPartyDeactivatedV1(
            account.CompanyId,
            account.PartyId,
            reason.Trim(),
            account.Version);
        dbContext.OutboxMessages.Add(new CounterpartyOutboxRow
        {
            Id = Guid.NewGuid(),
            CompanyId = account.CompanyId,
            EventType = CounterpartyPartyDeactivatedOutboxV1.EventType,
            SchemaVersion = CounterpartyPartyDeactivatedOutboxV1.SchemaVersion,
            SourceOperation = CounterpartyPartyDeactivatedOutboxV1.SourceOperation,
            AggregateType = CounterpartyPartyDeactivatedOutboxV1.AggregateType,
            AggregateId = account.Id,
            AggregateVersion = account.Version,
            Sequence = account.Version,
            PayloadJson = JsonSerializer.Serialize(payload, OutboxJson),
            OccurredAtUtc = now,
            CorrelationId = metadata.CorrelationId,
            CausationId = metadata.CausationId,
            RecordedAtUtc = now,
        });
    }
}
