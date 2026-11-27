using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// ALS-D111 + ALS-D113 (replacing the one-step ALS-D95/ALS-D110 path) official legal name change by maker-checker:
/// <list type="bullet">
/// <item><b>Propose</b> (<c>counterparties.legal_name.propose</c>): a new name with a reason is stored as a pending
/// request; the party's legal name does NOT change. One pending request per party in a company (database index).</item>
/// <item><b>Decide</b> (<c>counterparties.legal_name.approve</c>): a DIFFERENT person approves (one approval is enough)
/// or rejects it. The proposer cannot decide their own request, not even to reject it. Approval re-checks that the party
/// is still at the version and the legal name the request was made against (otherwise stale) and only then writes the
/// new name as a dated identity revision with one audit event carrying the old and the new name.</item>
/// </list>
/// Every command also needs <c>counterparty.parties.read</c>; all names are measured company-wide first and re-validated
/// before the single save. One Idempotency-Key (stored terminal answers replay), the PARTY version as the fence, the
/// company's account as the existence fence (no account in the active company = 404). Nothing but the legal name changes
/// here; the persistence guard refuses any other Party column in the same save.
/// </summary>
internal sealed partial class CounterpartyCommandService : ICounterpartyLegalNameService
{
    private const string ProposeLegalNameChangeCommandName = "counterparty.party.legal-name.change-request.v1";
    private const string DecideLegalNameChangeCommandName = "counterparty.party.legal-name.change-decision.v1";

    public async Task<CounterpartyLegalNameView?> GetLegalNameAsync(
        Guid partyId, CancellationToken cancellationToken)
    {
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        var party = await FindFencedPartyAsync(authorityFence.CompanyId, partyId, tracking: false, cancellationToken);
        if (party is null)
        {
            await authorityFence.RequireStillAsync(
                currentUser, companyContext, authorityRevalidationService,
                [ErpPermissions.CounterpartyPartiesRead], cancellationToken);
            return null;
        }

        var requests = dbContext.PartyLegalNameChangeRequests.AsNoTracking()
            .Where(request => request.CompanyId == authorityFence.CompanyId && request.PartyId == partyId);
        var pending = await requests
            .Where(request => request.Status == PartyLegalNameChangeStatus.Pending)
            .SingleOrDefaultAsync(cancellationToken);
        var lastDecided = await requests
            .Where(request => request.Status != PartyLegalNameChangeStatus.Pending)
            .OrderByDescending(request => request.DecidedAtUtc)
            .ThenByDescending(request => request.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var canPropose = await HasCompanyWideAsync(CounterpartyLegalNamePermissionsV1.Propose, cancellationToken);
        var canApprove = await HasCompanyWideAsync(CounterpartyLegalNamePermissionsV1.Approve, cancellationToken);
        await authorityFence.RequireStillAsync(
            currentUser, companyContext, authorityRevalidationService,
            [ErpPermissions.CounterpartyPartiesRead], cancellationToken);
        return new CounterpartyLegalNameView(
            party.Id,
            authorityFence.CompanyId,
            party.Code.Value,
            party.Version.ToString(CultureInfo.InvariantCulture),
            party.LegalName,
            pending is null ? null : ToLegalNameView(pending),
            lastDecided is null ? null : ToLegalNameView(lastDecided),
            new CounterpartyLegalNameViewer(
                canPropose,
                canApprove,
                pending is not null && pending.IsRequestedBy(authorityFence.ActorId)));
    }

    public async Task<CounterpartyCommandExecution> ProposeLegalNameChangeAsync(
        Guid partyId, ProposeCounterpartyLegalNameChange command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var authorityFence = CaptureLegalNameCommandFence(metadata);
        var scope = await RequireCompanyWidePermissionAsync(
            CounterpartyLegalNamePermissionsV1.Propose, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => authorityFence.RequireStillAsync(
            currentUser, companyContext, authorityRevalidationService,
            [CounterpartyLegalNamePermissionsV1.Propose, ErpPermissions.CounterpartyPartiesRead], token);
        RequirePartyId(partyId);
        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var proposed = NormalizeLegalNameInput(command.ProposedLegalName);
        ValidateReason(command.Reason);
        var reason = command.Reason.Trim();
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            PartyId = partyId, ExpectedVersion = expectedVersion, Proposed = proposed, Reason = reason,
        });

        var party = await FindFencedPartyAsync(metadata.CompanyId, partyId, tracking: true, cancellationToken);
        var existing = await FindCommandExecutionAsync(
            ProposeLegalNameChangeCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, party is not null);
        }

        var refusal = party is null
            ? Responses.NotFound()
            : party.Version != expectedVersion
                ? Responses.VersionConflict(expectedVersion, party.Version)
                : string.Equals(party.LegalName, proposed, StringComparison.Ordinal)
                    ? LegalNameResponses.Refused(CounterpartyCommandResult.LegalNameUnchanged)
                    : await HasPendingLegalNameChangeAsync(metadata.CompanyId, partyId, cancellationToken)
                        ? LegalNameResponses.Refused(CounterpartyCommandResult.LegalNameChangePending)
                        : null;
        if (refusal is not null)
        {
            var refused = await CompleteCommandAsync(ProposeLegalNameChangeCommandName, refusal,
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
            LogLegalNameCommand("change-request", metadata, partyId, refused);
            return refused;
        }

        var request = new PartyLegalNameChangeRequest(
            Guid.NewGuid(), metadata.CompanyId, partyId, party!.LegalName, proposed, party.Version,
            reason, metadata.ActorId, DateTimeOffset.UtcNow);
        dbContext.PartyLegalNameChangeRequests.Add(request);
        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            "counterparty.party.legal-name.change-requested.v1",
            "Counterparty.PartyLegalNameChangeRequest", request.Id.ToString("D"),
            "ProposeLegalNameChange",
            EntityVersionBefore: null, request.Version,
            AuditActorTypes.User, metadata.ActorId,
            CounterpartyLegalNamePermissionsV1.Propose, scope.RoleCodes,
            BeforeJson: "{}",
            SerializeLegalNameChangeRequestSnapshot(request),
            reason,
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
            metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));

        CounterpartyCommandExecution result;
        try
        {
            result = await CompleteCommandAsync(ProposeLegalNameChangeCommandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }
        catch (DbUpdateException)
        {
            // A concurrent proposal won the one-pending-per-party index; this attempt stores the same refusal it would
            // have met a moment later. Any other failure is not ours to reinterpret.
            dbContext.ChangeTracker.Clear();
            if (!await HasPendingLegalNameChangeAsync(metadata.CompanyId, partyId, cancellationToken))
            {
                throw;
            }

            result = await CompleteCommandAsync(ProposeLegalNameChangeCommandName,
                LegalNameResponses.Refused(CounterpartyCommandResult.LegalNameChangePending),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        LogLegalNameCommand("change-request", metadata, partyId, result);
        return result;
    }

    public Task<CounterpartyCommandExecution> DecideLegalNameChangeAsync(
        Guid partyId, DecideCounterpartyLegalNameChange command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return RunIdentityDecisionAsync(DecideLegalNameChangeCommandName, CounterpartyLegalNamePermissionsV1.Approve,
            partyId, command.ExpectedVersion, metadata,
            token => DecideLegalNameChangeCoreAsync(partyId, command, metadata, token), cancellationToken);
    }

    private async Task<CounterpartyCommandExecution> DecideLegalNameChangeCoreAsync(
        Guid partyId, DecideCounterpartyLegalNameChange command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var authorityFence = CaptureLegalNameCommandFence(metadata);
        var scope = await RequireCompanyWidePermissionAsync(
            CounterpartyLegalNamePermissionsV1.Approve, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => authorityFence.RequireStillAsync(
            currentUser, companyContext, authorityRevalidationService,
            [CounterpartyLegalNamePermissionsV1.Approve, ErpPermissions.CounterpartyPartiesRead], token);
        RequirePartyId(partyId);
        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        if (command.ChangeRequestId == Guid.Empty)
        {
            throw new ArgumentException("Değişiklik talebi kimliği boş olamaz.", nameof(command));
        }

        var approve = command.Decision switch
        {
            CounterpartyLegalNameDecisionsV1.Approve => true,
            CounterpartyLegalNameDecisionsV1.Reject => false,
            _ => throw new ArgumentException("Karar 'approve' veya 'reject' olmalıdır.", nameof(command)),
        };
        var note = command.Note?.Trim();
        if (note is { Length: > PartyLegalNameChangeRequest.NoteMaximumLength })
        {
            throw new ArgumentException(
                $"Karar notu {PartyLegalNameChangeRequest.NoteMaximumLength} karakterden uzun olamaz.",
                nameof(command));
        }

        note = string.IsNullOrEmpty(note) ? null : note;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            PartyId = partyId, ExpectedVersion = expectedVersion, command.ChangeRequestId, Approve = approve,
            Note = note,
        });

        var party = await FindFencedPartyAsync(metadata.CompanyId, partyId, tracking: true, cancellationToken);
        var existing = await FindCommandExecutionAsync(
            DecideLegalNameChangeCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, party is not null);
        }

        var request = party is null
            ? null
            : await dbContext.PartyLegalNameChangeRequests.SingleOrDefaultAsync(
                candidate => candidate.Id == command.ChangeRequestId
                    && candidate.CompanyId == metadata.CompanyId
                    && candidate.PartyId == partyId,
                cancellationToken);
        var refusal = party is null || request is null
            ? Responses.NotFound()
            : request.Status != PartyLegalNameChangeStatus.Pending
                ? LegalNameResponses.Refused(CounterpartyCommandResult.LegalNameChangeNotPending)
                : request.IsRequestedBy(metadata.ActorId)
                    ? LegalNameResponses.Refused(CounterpartyCommandResult.LegalNameSelfDecision)
                    : party.Version != expectedVersion
                        ? Responses.VersionConflict(expectedVersion, party.Version)
                        : approve && (party.Version != request.PartyVersionAtRequest
                            || !string.Equals(party.LegalName, request.CurrentLegalName, StringComparison.Ordinal))
                            ? LegalNameResponses.Refused(CounterpartyCommandResult.LegalNameChangeStale)
                            : null;
        if (refusal is not null)
        {
            var refused = await CompleteCommandAsync(DecideLegalNameChangeCommandName, refusal,
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
            LogLegalNameCommand(approve ? "approve" : "reject", metadata, partyId, refused);
            return refused;
        }

        var now = DateTimeOffset.UtcNow;
        var requestVersionBefore = request!.Version;
        var approvalContext = JsonSerializer.Serialize(new
        {
            ChangeRequestId = request.Id,
            request.RequestedBy,
            request.RequestedAtUtc,
            DecidedBy = metadata.ActorId,
            Decision = approve ? CounterpartyLegalNameDecisionsV1.Approve : CounterpartyLegalNameDecisionsV1.Reject,
            Note = note,
        });
        if (approve)
        {
            var partyVersionBefore = party!.Version;
            var legalNameBefore = party.LegalName;
            party.ApplyApprovedLegalNameChange(request.CurrentLegalName, request.ProposedLegalName, now);
            // KLN-D38 (3): the same legal person keeps the same cari; the approved official name is kept dated from the
            // approval's business day, with the name before it as the baseline. The tax-identity source link is not
            // used here: the revision table links only tax-identity corrections to their request.
            await AppendIdentityRevisionAsync(
                party, PartyIdentityAttribute.LegalName, legalNameBefore, partyVersionBefore,
                PartyIdentityRevisionKind.DatedChange, request.Reason, sourceChangeRequestId: null, metadata, now,
                cancellationToken);
            request.Approve(metadata.ActorId, note, now);
            auditEventCollector.Enqueue(new AuditEventDraft(
                metadata.CompanyId,
                "counterparty.party.legal-name.change-approved.v1",
                "Counterparty.Party", party.Id.ToString("D"),
                "ApproveLegalNameChange",
                partyVersionBefore, party.Version,
                AuditActorTypes.User, metadata.ActorId,
                CounterpartyLegalNamePermissionsV1.Approve, scope.RoleCodes,
                SerializeLegalNameSnapshot(legalNameBefore, partyVersionBefore),
                SerializeLegalNameSnapshot(party.LegalName, party.Version),
                request.Reason,
                approvalContext, AuditAiInvolvement.None, AiContextJson: null,
                metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
                metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        }
        else
        {
            var before = SerializeLegalNameChangeRequestSnapshot(request);
            request.Reject(metadata.ActorId, note, now);
            auditEventCollector.Enqueue(new AuditEventDraft(
                metadata.CompanyId,
                "counterparty.party.legal-name.change-rejected.v1",
                "Counterparty.PartyLegalNameChangeRequest", request.Id.ToString("D"),
                "RejectLegalNameChange",
                requestVersionBefore, request.Version,
                AuditActorTypes.User, metadata.ActorId,
                CounterpartyLegalNamePermissionsV1.Approve, scope.RoleCodes,
                before, SerializeLegalNameChangeRequestSnapshot(request),
                note ?? "Resmi unvan değişiklik talebi reddedildi",
                approvalContext, AuditAiInvolvement.None, AiContextJson: null,
                metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
                metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        }

        var result = await CompleteCommandAsync(DecideLegalNameChangeCommandName, Responses.Succeeded(),
            keyHash, requestHash, metadata, new PartyConcurrencyContext(partyId, expectedVersion),
            cancellationToken, beforeSave);
        LogLegalNameCommand(approve ? "approve" : "reject", metadata, partyId, result);
        return result;
    }

    private CounterpartyContactInformationAuthorityFence CaptureLegalNameCommandFence(
        CounterpartyCommandMetadata metadata)
    {
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        if (authorityFence.CompanyId != metadata.CompanyId
            || !string.Equals(authorityFence.ActorId, metadata.ActorId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Cari resmi unvan command bağlamı değişti.");
        }

        return authorityFence;
    }

    private Task<bool> HasPendingLegalNameChangeAsync(
        Guid companyId, Guid partyId, CancellationToken cancellationToken) =>
        dbContext.PartyLegalNameChangeRequests.AsNoTracking().AnyAsync(
            request => request.CompanyId == companyId && request.PartyId == partyId
                && request.Status == PartyLegalNameChangeStatus.Pending,
            cancellationToken);

    /// <summary>The submitted legal name as the party would store it; blank or too long is a 400, never truncated.</summary>
    private static string NormalizeLegalNameInput(string? legalName)
    {
        try
        {
            return Party.NormalizeLegalName(legalName);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentException(
                $"proposedLegalName: Resmi unvan {Party.LegalNameMaximumLength} karakterden uzun olamaz.",
                nameof(legalName));
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("proposedLegalName: Resmi unvan boş olamaz.", nameof(legalName));
        }
    }

    private static CounterpartyLegalNameChangeView ToLegalNameView(PartyLegalNameChangeRequest request) =>
        new(
            request.Id,
            request.Status switch
            {
                PartyLegalNameChangeStatus.Pending => CounterpartyLegalNameChangeStatusesV1.Pending,
                PartyLegalNameChangeStatus.Approved => CounterpartyLegalNameChangeStatusesV1.Approved,
                PartyLegalNameChangeStatus.Rejected => CounterpartyLegalNameChangeStatusesV1.Rejected,
                _ => throw new InvalidOperationException("Bilinmeyen resmi unvan değişiklik talebi durumu."),
            },
            request.CurrentLegalName,
            request.ProposedLegalName,
            request.Reason,
            request.RequestedBy,
            request.RequestedAtUtc,
            request.DecidedBy,
            request.DecidedAtUtc,
            request.DecisionNote);

    private static string SerializeLegalNameSnapshot(string legalName, long version) =>
        JsonSerializer.Serialize(new { LegalName = legalName, Version = version });

    private static string SerializeLegalNameChangeRequestSnapshot(PartyLegalNameChangeRequest request) =>
        JsonSerializer.Serialize(new
        {
            request.Id,
            request.CompanyId,
            request.PartyId,
            request.CurrentLegalName,
            request.ProposedLegalName,
            request.PartyVersionAtRequest,
            Status = request.Status.ToString(),
            request.RequestedBy,
            request.DecidedBy,
            request.DecisionNote,
            request.Version,
        });

    private void LogLegalNameCommand(
        string operation, CounterpartyCommandMetadata metadata, Guid partyId, CounterpartyCommandExecution result)
    {
        // The name itself is not logged; the audit event carries the before/after.
        logger.LogInformation(
            "Counterparty legal-name command completed. Operation={Operation} CompanyId={CompanyId} PartyId={PartyId} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            operation, metadata.CompanyId, partyId, metadata.CorrelationId, result.Response.Result, result.IsReplay);
    }

    private static class LegalNameResponses
    {
        public static CounterpartyCommandResponse Refused(CounterpartyCommandResult result) => result switch
        {
            CounterpartyCommandResult.LegalNameUnchanged => Problem(result,
                "Resmi unvan aynı.",
                "Önerilen resmi unvan kayıtlı unvanla aynı."),
            CounterpartyCommandResult.LegalNameChangePending => Problem(result,
                "Onay bekleyen talep var.",
                "Bu cari için onay bekleyen bir resmi unvan değişiklik talebi zaten var."),
            CounterpartyCommandResult.LegalNameChangeNotPending => Problem(result,
                "Talep artık beklemede değil.",
                "Bu resmi unvan değişiklik talebi daha önce onaylanmış veya reddedilmiş."),
            CounterpartyCommandResult.LegalNameChangeStale => Problem(result,
                "Talep güncel değil.",
                "Cari kartı talep açıldıktan sonra değişmiş; talep onaylanamaz. Talebi reddedip yenisini açın."),
            CounterpartyCommandResult.LegalNameSelfDecision => Problem(result,
                "Kendi talebinizi karara bağlayamazsınız.",
                "Resmi unvan değişikliğini talebi açan kişiden farklı bir yetkili onaylamalı veya reddetmelidir."),
            _ => throw new InvalidOperationException($"ALS-D111 ret sonucu değil: {result}."),
        };

        private static CounterpartyCommandResponse Problem(
            CounterpartyCommandResult result, string title, string detail) =>
            new(result, ConflictStatusCode, title, detail, Resource: null, Conflict: null);

        private const int ConflictStatusCode = 409;
    }
}
