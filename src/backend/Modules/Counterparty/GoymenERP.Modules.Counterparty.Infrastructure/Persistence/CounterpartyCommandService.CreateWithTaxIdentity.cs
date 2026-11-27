using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Views;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed partial class CounterpartyCommandService
{
    private const int AtomicCreateReceiptSchemaVersion = 2;
    private AtomicCreateState? createWithTaxIdentity;

    private async Task<CounterpartyCommandExecution> CreateWithTaxIdentityAsync(CreateCounterparty command,
        CounterpartyCommandMetadata metadata, CancellationToken token)
    {
        // The omitted/null branch never enters here and retains its original normalization, hash and save path.
        var normalized = NormalizeCreate(command);
        var number = ParseTaxIdentity(command.TaxIdentityNumber);
        var hash = IdempotencyHash.ComputePayload(new CounterpartyCreateTaxIdentityFingerprintV1(normalized.Kind,
            normalized.LegalName, normalized.TradeName, normalized.Roles, number.Value));
        var key = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        try { return await RunAsync(mutate: true, originalFailure: null); }
        catch (AtomicCreateSaveRace race) { return await RunAsync(mutate: false, race.InnerException!); }

        async Task<CounterpartyCommandExecution> RunAsync(bool mutate, Exception? originalFailure)
        {
            // Caller state is rejected outside the owned cleanup boundary, never joined or cleared.
            if (identityDecisionAuditWriter is null || !dbContext.Database.IsRelational()
                || System.Transactions.Transaction.Current is not null || dbContext.Database.CurrentTransaction is not null
                || dbContext.ChangeTracker.HasChanges() || identityDecisionAuditWriter.HasPendingDrafts
                || identityDecision is not null || createWithTaxIdentity is not null)
                throw new InvalidOperationException("Atomic tax creation requires a clean owned relational transaction and Audit writer.");
            ValidateCommandMetadata(metadata);
            var authority = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
            if (authority.CompanyId != metadata.CompanyId || authority.ActorId != metadata.ActorId)
                throw new UnauthorizedAccessException("Atomic tax creation context changed.");
            Task Fence(CancellationToken cancellation) => authority.RequireStillAsync(currentUser, companyContext,
                authorityRevalidationService, [ErpPermissions.CounterpartyPartiesCreate,
                    ErpPermissions.CounterpartyPartiesRead, CounterpartyTaxIdentityPermissionsV1.Update], cancellation);
            var state = new AtomicCreateState(metadata.CompanyId, hash, Fence);
            IDbContextTransaction? owned = null;
            var committed = false;
            try
            {
                createWithTaxIdentity = state;
                owned = await dbContext.Database.BeginTransactionAsync(token);
                var creator = await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesCreate, token);
                await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, token);
                var updater = await RequireCompanyWidePermissionAsync(CounterpartyTaxIdentityPermissionsV1.Update, token);
                await Fence(token);
                var existing = await FindCommandExecutionAsync(CreateCommandName, key, token);
                await Fence(token);
                if (existing is not null)
                {
                    var replay = ResolveExistingExecution(existing, hash, metadata);
                    await owned.RollbackAsync(CancellationToken.None);
                    var completedRead = owned; owned = null;
                    try { await completedRead.DisposeAsync(); }
                    catch (Exception error) { throw new InvalidOperationException("Atomic read transaction disposal failed.", error); }
                    await Fence(token);
                    return replay;
                }
                if (!mutate)
                {
                    ExceptionDispatchInfo.Capture(originalFailure!).Throw();
                    throw new InvalidOperationException("A failed creation without a winner cannot become a new receipt.");
                }

                var partyCode = await AllocatePartyCodeAsync(token);
                await Fence(token);
                var party = new Party(Guid.NewGuid(), partyCode, normalized.Kind, normalized.LegalName, normalized.TradeName);
                var account = new CompanyPartyAccount(Guid.NewGuid(), metadata.CompanyId, party.Id, normalized.Roles);
                var beforeVersion = party.Version;
                var createdSnapshot = SerializeCreateAuditSnapshot(party, account);
                var enteredAt = DateTimeOffset.UtcNow;
                party.EnterTaxIdentityNumber(number, enteredAt);
                dbContext.Parties.Add(party); dbContext.CompanyPartyAccounts.Add(account);
                await AppendIdentityRevisionAsync(party, PartyIdentityAttribute.TaxIdentityNumber, valueBefore: null,
                    beforeVersion, PartyIdentityRevisionKind.InitialEntry, reason: null, sourceChangeRequestId: null,
                    metadata, enteredAt, token);
                await Fence(token);
                var resource = PartyCoreViewComposer.ComposeAuthorized(party, [account], new HashSet<Guid> { metadata.CompanyId })
                    ?? throw new InvalidOperationException("Atomic tax creation has no safe resource projection.");
                var evidence = new CounterpartyCreatedTaxIdentityEvidenceV1(CreateCommandName, party.Id,
                    metadata.CompanyId, party.Version, hash);
                var response = Responses.Created(resource) with { CreatedTaxIdentityEvidence = evidence };
                auditEventCollector.Enqueue(new AuditEventDraft(metadata.CompanyId, "counterparty.party.created.v1",
                    "Counterparty.Party", party.Id.ToString("D"), "Create", null, beforeVersion,
                    AuditActorTypes.User, metadata.ActorId, ErpPermissions.CounterpartyPartiesCreate, creator.RoleCodes,
                    "{}", createdSnapshot, "Cari oluşturma", null,
                    AuditAiInvolvement.None, null, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
                    metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
                auditEventCollector.Enqueue(new AuditEventDraft(metadata.CompanyId, "counterparty.party.tax-identity.entered.v1",
                    "Counterparty.Party", party.Id.ToString("D"), "EnterTaxIdentity", beforeVersion, party.Version,
                    AuditActorTypes.User, metadata.ActorId, CounterpartyTaxIdentityPermissionsV1.Update, updater.RoleCodes,
                    SerializeTaxIdentitySnapshot(null, beforeVersion), SerializeTaxIdentitySnapshot(party.TaxIdentityNumber, party.Version),
                    "Cari vergi/kimlik numarası ilk giriş", null, AuditAiInvolvement.None, null,
                    metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
                    metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
                var answer = await CompleteCommandAsync(CreateCommandName, response, key, hash, metadata,
                    concurrencyContext: null, cancellationToken: token);
                await Fence(token);
                if (!state.Saved) throw new InvalidOperationException("Atomic creation has no saved native receipt.");
                if (identityDecisionAuditWriter.HasPendingDrafts)
                {
                    await identityDecisionAuditWriter.WritePendingAsync(owned.GetDbTransaction(), token);
                    await Fence(token);
                }
                if (identityDecisionAuditWriter.HasPendingDrafts)
                    throw new InvalidOperationException("Atomic creation cannot commit pending Audit drafts.");
                await Fence(token);
                await owned.CommitAsync(token); committed = true;
                var completedWrite = owned; owned = null;
                try { await completedWrite.DisposeAsync(); }
                catch (Exception error) { throw new InvalidOperationException("Atomic write transaction disposal failed.", error); }
                // A commit may already be durable; revoked authority must still suppress the response, not claim rollback.
                await Fence(token);
                return answer;
            }
            catch (Exception failure)
            {
                var cleanup = new List<Exception>();
                if (owned is not null)
                {
                    if (!committed) try { await owned.RollbackAsync(CancellationToken.None); } catch (Exception error) { cleanup.Add(error); }
                    try { await owned.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
                    owned = null;
                }
                try { dbContext.ChangeTracker.Clear(); } catch (Exception error) { cleanup.Add(error); }
                try { identityDecisionAuditWriter.DiscardPending(); } catch (Exception error) { cleanup.Add(error); }
                if (cleanup.Count > 0) throw new AggregateException("Atomic creation cleanup failed; winner replay is unsafe.", [failure, .. cleanup]);
                if (mutate && !committed && state.SaveAttempted && !state.Saved && failure is DbUpdateException update)
                    throw new AtomicCreateSaveRace(update);
                throw;
            }
            finally { createWithTaxIdentity = null; }
        }
    }

    private static string SerializeAtomicCreateReceipt(CounterpartyCommandResponse response, AtomicCreateState state)
    {
        var evidence = response.CreatedTaxIdentityEvidence ?? throw new InvalidOperationException("Atomic creation evidence is absent.");
        RequireAtomicCreateEvidence(response, evidence, state.CompanyId, state.PayloadHash);
        return JsonSerializer.Serialize(new AtomicCreateReceipt(response, evidence));
    }

    private static CounterpartyCommandExecution RestoreAtomicCreateReceipt(CommandExecution execution, CounterpartyCommandMetadata metadata)
    {
        if (execution.CommandName != CreateCommandName || execution.CompanyId != metadata.CompanyId
            || execution.Outcome != nameof(CounterpartyCommandResult.Created) || execution.HttpStatusCode != 201
            || string.IsNullOrWhiteSpace(execution.OriginalCorrelationId))
            throw new InvalidOperationException("Atomic creation receipt envelope is inconsistent.");
        AtomicCreateReceipt? receipt;
        try { receipt = JsonSerializer.Deserialize<AtomicCreateReceipt>(execution.ResponseJson,
            new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }); }
        catch (JsonException error) { throw new InvalidOperationException("Atomic creation receipt is malformed.", error); }
        if (receipt?.Response is null || receipt.Evidence is null)
            throw new InvalidOperationException("Atomic creation receipt proof is absent.");
        RequireAtomicCreateEvidence(receipt.Response, receipt.Evidence, metadata.CompanyId, execution.RequestHash);
        return new(receipt.Response with { CreatedTaxIdentityEvidence = receipt.Evidence }, true, execution.OriginalCorrelationId);
    }

    private static void RequireAtomicCreateEvidence(CounterpartyCommandResponse response,
        CounterpartyCreatedTaxIdentityEvidenceV1 evidence, Guid companyId, string payloadHash)
    {
        if (response.Result != CounterpartyCommandResult.Created || response.StatusCode != 201
            || response.Title is not null || response.Detail is not null || response.Conflict is not null
            || response.Resource is not { } resource || resource.Id == Guid.Empty || resource.Version != 2
            || evidence.CommandName != CreateCommandName || evidence.PartyId != resource.Id
            || evidence.CompanyId != companyId || evidence.PartyVersion != 2 || evidence.PayloadHash != payloadHash
            || resource.CompanyAccounts is null || resource.CompanyAccounts.Count != 1
            || resource.CompanyAccounts[0] is not { } account || account.CompanyId != companyId || account.Version != 1)
            throw new InvalidOperationException("Atomic creation evidence is not bound to its immutable native resource.");
    }

    private sealed class AtomicCreateState(Guid companyId, string payloadHash, Func<CancellationToken, Task> fence)
    {
        public Guid CompanyId { get; } = companyId;
        public string PayloadHash { get; } = payloadHash;
        public Func<CancellationToken, Task> Fence { get; } = fence;
        public bool SaveAttempted { get; set; }
        public bool Saved { get; set; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record AtomicCreateReceipt([property: JsonRequired] CounterpartyCommandResponse Response,
        [property: JsonRequired] CounterpartyCreatedTaxIdentityEvidenceV1 Evidence);
    private sealed class AtomicCreateSaveRace(DbUpdateException inner) : Exception("Atomic creation save race.", inner);
}
