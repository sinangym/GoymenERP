using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>KLN-D38/D71/D72/D76/D77. One authorized voucher, full currency balances, bounded allocation positions.</summary>
public sealed class FinancePartyBalanceTransferServiceV1(FinanceDbContext database, ICompanyContext company,
    ICurrentUser user, IFinancePermissionPort permissions, IFinancePartyBalanceTransferOwnerPortV1 owners,
    IFinancePartyBalanceTransferEvidencePortV1 evidence, IPartyLedgerService ledger, IFinanceRecordPeriodRule periods,
    IAuditEventCollector audit, FinanceAuditEnvelope auditEnvelope, TimeProvider clock,
    IAuditTransactionWriterV1? auditOwner = null) : IFinancePartyBalanceTransferServiceV1
{
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<FinancePartyBalanceTransferReadResultV1> PreviewAuthorizedAsync(FinanceContext context,
        FinancePartyBalanceTransferSelectionV1 selection, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, cancellationToken) is { } refusal) return ReadRefused(refusal);
        return await FinanceTreasuryTransaction.ExecuteAsync(database, async () =>
        {
            try
            {
                var answer = await OwnerAsync(context, selection, cancellationToken);
                if (await FenceAsync(context, cancellationToken) is { } ownerFence) return ReadRefused(ownerFence);
                if (answer.Proof is not { } owner) return new FinancePartyBalanceTransferReadResultV1(
                    answer.Outcome is FinancePartyBalanceTransferOwnerOutcomeV1.NotFound or FinancePartyBalanceTransferOwnerOutcomeV1.InvalidRelation
                        ? ReadOutcome.NotFound : ReadOutcome.Unavailable, null, null);
                var snapshot = await FinancePartyBalanceTransferSnapshotReader.ReadAsync(database, ledger, context, owner, cancellationToken);
                if (await FenceAsync(context, cancellationToken) is { } late) return ReadRefused(late);
                return snapshot is null ? new FinancePartyBalanceTransferReadResultV1(ReadOutcome.Unavailable, null, null) : new FinancePartyBalanceTransferReadResultV1(ReadOutcome.Found, snapshot.Preview, null);
            }
            catch (InvalidOperationException) { return new FinancePartyBalanceTransferReadResultV1(ReadOutcome.Unavailable, null, null); }
        }, () => Task.FromResult(new FinancePartyBalanceTransferReadResultV1(ReadOutcome.Unavailable, null, null)),
            auditOwner, cancellationToken);
    }

    public async Task<FinancePartyBalanceTransferReadResultV1> GetAuthorizedAsync(FinanceContext context,
        Guid transferId, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, cancellationToken) is { } refusal) return ReadRefused(refusal);
        var row = await database.PartyBalanceTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == transferId, cancellationToken);
        if (await FenceAsync(context, cancellationToken) is { } late) return ReadRefused(late);
        if (row is null) return new FinancePartyBalanceTransferReadResultV1(ReadOutcome.NotFound, null, null);
        try { return new FinancePartyBalanceTransferReadResultV1(ReadOutcome.Found, null, View(row)); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        { return new FinancePartyBalanceTransferReadResultV1(ReadOutcome.Unavailable, null, null); }
    }

    public async Task<FinancePartyBalanceTransferCommandResultV1> RecordAsync(FinanceContext context,
        CommandMetadata metadata, RecordFinancePartyBalanceTransferV1 request, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, cancellationToken) is { } refusal) return Refused(refusal);
        if (metadata is null || !Text(metadata.IdempotencyKey, 128) || request is null || request.TransferId == Guid.Empty
            || request.Selection is null || !Text(request.ExpectedSnapshotVersion, 64) || request.ExpectedSnapshotVersion.Length != 64
            || request.OccurredOn == default || !Text(request.Reason, 1000) || request.Evidence is null
            || request.Evidence.DocumentId == Guid.Empty || request.Evidence.VersionNo < 1
            || request.Evidence.Sha256 is not { Length: 64 } hash || hash.Any(c => !Uri.IsHexDigit(c)))
            return Refused(CommandOutcome.ValidationFailed, "finance_balance_transfer_request_invalid");
        return await FinanceTreasuryTransaction.ExecuteAsync(database,
            () => RecordCoreAsync(context, metadata, request, cancellationToken),
            () => Task.FromResult(Refused(CommandOutcome.Unavailable, "finance_balance_transfer_unavailable")),
            auditOwner, cancellationToken);
    }

    private async Task<FinancePartyBalanceTransferCommandResultV1> RecordCoreAsync(FinanceContext context,
        CommandMetadata metadata, RecordFinancePartyBalanceTransferV1 request, CancellationToken cancellationToken)
    {
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payload = FinancePartyBalanceTransferPolicy.Hash(request);
        var prior = await database.PartyBalanceTransferCommands.AsNoTracking().SingleOrDefaultAsync(x => x.KeyHash == key, cancellationToken);
        if (prior is not null)
        {
            if (await FenceAsync(context, cancellationToken) is { } replayFence) return Refused(replayFence);
            if (prior.ActorId != context.ActorId || prior.PayloadHash != payload) return Refused(CommandOutcome.IdempotencyConflict);
            try
            {
                var stored = JsonSerializer.Deserialize<StoredCommandResult>(prior.ResultJson, Json) ?? throw new JsonException();
                // Older receipts have no original correlation. Never substitute the retry's correlation for it.
                return new(stored.Outcome, stored.Resource, stored.Code, true, stored.OriginalCorrelationId);
            }
            catch (JsonException) { return Refused(CommandOutcome.Unavailable); }
        }
        if (await database.PartyBalanceTransfers.AsNoTracking().AnyAsync(x => x.Id == request.TransferId, cancellationToken))
            return Refused(CommandOutcome.VersionConflict);
        var ownerAnswer = await OwnerAsync(context, request.Selection, cancellationToken);
        if (await FenceAsync(context, cancellationToken) is { } ownerFence) return Refused(ownerFence);
        if (ownerAnswer.Proof is not { } owner) return Refused(
            ownerAnswer.Outcome is FinancePartyBalanceTransferOwnerOutcomeV1.NotFound or FinancePartyBalanceTransferOwnerOutcomeV1.InvalidRelation
                ? CommandOutcome.ValidationFailed : CommandOutcome.Unavailable, "finance_balance_transfer_owner_unavailable");
        FinancePartyBalanceTransferSnapshot? snapshot;
        try { snapshot = await FinancePartyBalanceTransferSnapshotReader.ReadAsync(database, ledger, context, owner, cancellationToken); }
        catch (InvalidOperationException) { return Refused(CommandOutcome.Unavailable); }
        if (snapshot is null) return Refused(CommandOutcome.Unavailable);
        if (snapshot.Preview.SnapshotVersion != request.ExpectedSnapshotVersion) return Refused(CommandOutcome.VersionConflict);
        if (!snapshot.Preview.CanRecord) return Refused(CommandOutcome.ValidationFailed, "finance_balance_transfer_empty");
        var now = clock.GetUtcNow();
        var period = await periods.CheckAsync(context, request.OccurredOn, null, now, cancellationToken);
        if (period.Outcome != RecordPeriodOutcome.Open)
            return Refused(period.Outcome == RecordPeriodOutcome.NotOpen ? CommandOutcome.ValidationFailed : CommandOutcome.Unavailable,
                period.Code);
        var checkedEvidence = await evidence.CheckAsync(context, owner, request.Evidence, cancellationToken);
        if (checkedEvidence != ReadOutcome.Found) return Refused(checkedEvidence == ReadOutcome.Denied
            ? CommandOutcome.Denied : checkedEvidence == ReadOutcome.NotFound ? CommandOutcome.ValidationFailed : CommandOutcome.Unavailable,
            "finance_balance_transfer_evidence_required");
        // Counterparty is the legal relation's owner. Check its exact versions again immediately before staging writes.
        var currentOwner = (await OwnerAsync(context, request.Selection, cancellationToken)).Proof;
        if (currentOwner is null || FinancePartyBalanceTransferPolicy.Hash(currentOwner) != FinancePartyBalanceTransferPolicy.Hash(owner))
            return Refused(CommandOutcome.VersionConflict, "finance_balance_transfer_owner_changed");
        if (await FenceAsync(context, cancellationToken) is { } late) return Refused(late);
        var row = new FinancePartyBalanceTransferRow
        {
            Id = request.TransferId, CompanyId = context.CompanyId,
            PredecessorPartyId = owner.PredecessorPartyId, PredecessorAccountId = owner.PredecessorAccountId,
            SuccessorPartyId = owner.SuccessorPartyId, SuccessorAccountId = owner.SuccessorAccountId,
            OwnerProofJson = JsonSerializer.Serialize(owner, Json), CurrenciesJson = JsonSerializer.Serialize(snapshot.Preview.Currencies, Json),
            EvidenceJson = JsonSerializer.Serialize(request.Evidence, Json),
            AssignmentSnapshotJson = JsonSerializer.Serialize(snapshot.Sources, Json), SnapshotVersion = snapshot.Preview.SnapshotVersion,
            OccurredOn = request.OccurredOn, Reason = request.Reason, RecordedBy = context.ActorId, RecordedAtUtc = now,
        };
        database.PartyBalanceTransfers.Add(row);
        foreach (var source in snapshot.Sources) await MovePositionAsync(context, row, source, now, cancellationToken);
        var result = new FinancePartyBalanceTransferCommandResultV1(CommandOutcome.Succeeded, View(row), null,
            OriginalCorrelationId: context.CorrelationId);
        database.PartyBalanceTransferCommands.Add(new FinancePartyBalanceTransferCommandRow()
        {
            CompanyId = context.CompanyId, KeyHash = key, ActorId = context.ActorId,
            PayloadHash = payload, ResultJson = JsonSerializer.Serialize(new StoredCommandResult(
                result.Outcome, result.Resource, result.Code, result.Replayed, result.OriginalCorrelationId), Json),
        });
        if (await FenceAsync(context, cancellationToken) is { } finalFence) return Refused(finalFence);
        audit.Enqueue(new AuditEventDraft(context.CompanyId, "finance.party-balance-transfer.recorded.v1",
            "Finance.PartyBalanceTransfer", row.Id.ToString("D"), "Record", null, 1, AuditActorTypes.User, context.ActorId,
            auditEnvelope.PermissionCode, auditEnvelope.RoleCodes, "{}", JsonSerializer.Serialize(View(row), Json), row.Reason,
            null, AuditAiInvolvement.None, null, auditEnvelope.SourceChannel, auditEnvelope.ClientIpAddress,
            auditEnvelope.UserAgent, context.CorrelationId, context.CausationId, metadata.IdempotencyKey));
        await database.SaveChangesAsync(cancellationToken);
        return result;
    }

    // Additive metadata in the existing immutable receipt JSON; no second command ledger or database column.
    private sealed record StoredCommandResult(CommandOutcome Outcome, FinancePartyBalanceTransferViewV1? Resource,
        string? Code, bool Replayed = false, string? OriginalCorrelationId = null);

    private async Task MovePositionAsync(FinanceContext context, FinancePartyBalanceTransferRow voucher,
        FinanceBalanceTransferSourceFact source, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await database.PartyBalanceAssignments.Where(x => x.Kind == source.Kind && x.SourceId == source.SourceId)
            .ToListAsync(cancellationToken);
        var amount = FinanceAllocationPositions.Amount(source.AvailableAmount);
        var from = rows.SingleOrDefault(x => x.CurrentAccountId == voucher.PredecessorAccountId);
        if (from is not null)
        {
            if (from.Version != source.AssignmentVersion || !FinanceExactDecimalText.Equal(from.AvailableAmountText, source.AvailableAmount))
                throw new DbUpdateConcurrencyException();
            from.AvailableAmountText = amount.Subtract(amount).Text;
            from.LastTransferId = voucher.Id;
            from.Version = checked(from.Version + 1);
        }
        var to = rows.SingleOrDefault(x => x.CurrentAccountId == voucher.SuccessorAccountId);
        if (voucher.SuccessorAccountId == source.OriginalAccountId)
        {
            // The original remainder is derived from the total less explicit positions; no self-assignment is stored.
        }
        else if (to is null)
        {
            database.PartyBalanceAssignments.Add(new FinancePartyBalanceAssignmentRow()
            {
                CompanyId = context.CompanyId, Kind = source.Kind, SourceId = source.SourceId,
                OriginalPartyId = source.OriginalPartyId, OriginalAccountId = source.OriginalAccountId,
                CurrentPartyId = voucher.SuccessorPartyId, CurrentAccountId = voucher.SuccessorAccountId,
                CurrencyId = source.CurrencyId, AvailableAmountText = amount.Text, LastTransferId = voucher.Id, Version = 1,
            });
        }
        else
        {
            to.AvailableAmountText = FinanceAllocationPositions.Amount(to.AvailableAmountText).Add(amount).Text;
            to.LastTransferId = voucher.Id;
            to.Version = checked(to.Version + 1);
        }
        // The original aggregate token is also advanced: an in-flight legacy allocation that read no assignment
        // cannot commit after this first transfer. Original account, amounts and historical documents stay unchanged.
        if (source.Kind == FinanceBalanceAssignmentKinds.OpenItem)
        {
            var item = await database.OpenItems.SingleAsync(x => x.Id == source.SourceId, cancellationToken);
            if (item.Version != source.SourceVersion) throw new DbUpdateConcurrencyException();
            item.Version = checked(item.Version + 1); item.UpdatedBy = context.ActorId; item.UpdatedAtUtc = now;
        }
        else if (source.Kind == FinanceBalanceAssignmentKinds.Receipt)
        {
            var state = await database.SourceAllocationStates.SingleOrDefaultAsync(x => x.SourceId == source.SourceId, cancellationToken);
            if (state is null)
                database.SourceAllocationStates.Add(new FinanceSourceAllocationStateRow() { CompanyId = context.CompanyId, SourceId = source.SourceId,
                    CurrencyId = source.CurrencyId, OriginalAmountText = source.OriginalAmount, UnallocatedAmountText = source.OriginalAmount,
                    Version = 1, UpdatedBy = context.ActorId, UpdatedAtUtc = now });
            else
            {
                if (state.Version != source.AllocationVersion) throw new DbUpdateConcurrencyException();
                state.Version = checked(state.Version + 1); state.UpdatedBy = context.ActorId; state.UpdatedAtUtc = now;
            }
        }
        else
        {
            var state = await database.PaymentAllocationStates.SingleOrDefaultAsync(x => x.PaymentId == source.SourceId, cancellationToken);
            if (state is null)
                database.PaymentAllocationStates.Add(new FinancePaymentAllocationStateRow() { CompanyId = context.CompanyId, PaymentId = source.SourceId,
                    CurrencyId = source.CurrencyId, OriginalAmountText = source.OriginalAmount, UnallocatedAmountText = source.OriginalAmount,
                    Version = 1, UpdatedBy = context.ActorId, UpdatedAtUtc = now });
            else
            {
                if (state.Version != source.AllocationVersion) throw new DbUpdateConcurrencyException();
                state.Version = checked(state.Version + 1); state.UpdatedBy = context.ActorId; state.UpdatedAtUtc = now;
            }
        }
    }

    private async Task<FinancePartyBalanceTransferOwnerAnswerV1> OwnerAsync(FinanceContext context,
        FinancePartyBalanceTransferSelectionV1 selection, CancellationToken cancellationToken)
    {
        if (selection is null) return new FinancePartyBalanceTransferOwnerAnswerV1(FinancePartyBalanceTransferOwnerOutcomeV1.InvalidRelation, null);
        var answer = await owners.ReadAsync(context, selection.PredecessorPartyId, selection.PredecessorAccountId,
            selection.SuccessorPartyId, selection.SuccessorAccountId, cancellationToken);
        if (!Same(context)) return new FinancePartyBalanceTransferOwnerAnswerV1(FinancePartyBalanceTransferOwnerOutcomeV1.Unavailable, null);
        if (answer.Outcome != FinancePartyBalanceTransferOwnerOutcomeV1.Available) return answer with { Proof = null };
        return answer.Outcome == FinancePartyBalanceTransferOwnerOutcomeV1.Available
            && answer.Proof is { } proof && proof.CompanyId == context.CompanyId
            && proof.PredecessorPartyId == selection.PredecessorPartyId && proof.PredecessorAccountId == selection.PredecessorAccountId
            && proof.SuccessorPartyId == selection.SuccessorPartyId && proof.SuccessorAccountId == selection.SuccessorAccountId
                ? answer : new FinancePartyBalanceTransferOwnerAnswerV1(FinancePartyBalanceTransferOwnerOutcomeV1.Unavailable, null);
    }

    private async Task<CommandOutcome?> FenceAsync(FinanceContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Same(context)) return CommandOutcome.Denied;
        foreach (var action in new[] { FinanceAction.RecordPartyBalanceTransfer, FinanceAction.ReadPartyLedger, FinanceAction.ReadPartyLedgerAmount })
        {
            var permission = await permissions.CheckAsync(context, action, null, cancellationToken);
            if (!Same(context)) return CommandOutcome.Denied;
            if (permission != PermissionDecision.Allowed) return permission == PermissionDecision.Denied ? CommandOutcome.Denied : CommandOutcome.Unavailable;
        }
        return null;
    }
    private bool Same(FinanceContext context) => context is not null && company.IsResolved && company.CompanyId == context.CompanyId
        && context.CompanyId != Guid.Empty && user.IsAuthenticated && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId)
        && Text(context.ActorId, 128) && Text(context.CorrelationId, 128)
        && (context.CausationId is null || Text(context.CausationId, 128));
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && value == value.Trim();
    private static FinancePartyBalanceTransferReadResultV1 ReadRefused(CommandOutcome refusal) =>
        new FinancePartyBalanceTransferReadResultV1(refusal == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, null, null);
    private static FinancePartyBalanceTransferCommandResultV1 Refused(CommandOutcome refusal, string? code = null) => new FinancePartyBalanceTransferCommandResultV1(refusal, null, code);
    private static FinancePartyBalanceTransferViewV1 View(FinancePartyBalanceTransferRow row) =>
        new FinancePartyBalanceTransferViewV1(row.Id, row.CompanyId, "1",
            JsonSerializer.Deserialize<FinancePartyBalanceTransferOwnerProofV1>(row.OwnerProofJson, Json) ?? throw new JsonException(),
            JsonSerializer.Deserialize<FinancePartyBalanceTransferCurrencyV1[]>(row.CurrenciesJson, Json) ?? throw new JsonException(),
            row.OccurredOn, row.Reason, row.RecordedBy, row.RecordedAtUtc,
            JsonSerializer.Deserialize<FinancePartyBalanceTransferEvidenceV1>(row.EvidenceJson, Json) ?? throw new JsonException());
}
