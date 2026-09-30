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
/// AT-009 VKN/TCKN on the Cari card (user decision 2026-09-30):
/// <list type="bullet">
/// <item><b>Validation:</b> a number is stored only when the official VKN (10) / TCKN (11) check digits hold
/// (<see cref="TaxIdentityNumber"/>); anything else is refused as <c>tax_identity_invalid</c> before any read or write.</item>
/// <item><b>Duplicate:</b> the same number on another Cari card of the ACTIVE company is a warning in the read, never a
/// refusal (a branch or the same firm may carry it). Cards of other companies are not looked at: the company fence.</item>
/// <item><b>Maker-checker:</b> the first entry is direct; replacing an existing number is a change request that a
/// DIFFERENT person with the approve permission decides. The proposer cannot approve or reject it; approval re-checks
/// that the party still carries the number the request was made against (otherwise stale).</item>
/// </list>
/// Every command is the Counterparty command shape: company-wide permission measured first, one Idempotency-Key
/// (stored terminal answers replay), the PARTY version as the fence, the company's account as the existence fence
/// (no account in the active company = 404), and one audit event per real change.
/// </summary>
internal sealed partial class CounterpartyCommandService : ICounterpartyTaxIdentityService
{
    private const string EnterTaxIdentityCommandName = "counterparty.party.tax-identity.enter.v1";
    private const string RequestTaxIdentityChangeCommandName = "counterparty.party.tax-identity.change-request.v1";
    private const string DecideTaxIdentityChangeCommandName = "counterparty.party.tax-identity.change-decision.v1";
    private const int DuplicateLimit = 20;

    public async Task<CounterpartyTaxIdentityView?> GetTaxIdentityAsync(
        Guid partyId, CancellationToken cancellationToken)
    {
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId ?? throw new UnauthorizedAccessException("Cari okuması kimliği doğrulanmış kullanıcı ister.");
        var party = await FindFencedPartyAsync(companyId, partyId, tracking: false, cancellationToken);
        if (party is null)
        {
            return null;
        }

        var requests = dbContext.PartyTaxIdentityChangeRequests.AsNoTracking()
            .Where(request => request.CompanyId == companyId && request.PartyId == partyId);
        var pending = await requests
            .Where(request => request.Status == PartyTaxIdentityChangeStatus.Pending)
            .SingleOrDefaultAsync(cancellationToken);
        var lastDecided = await requests
            .Where(request => request.Status != PartyTaxIdentityChangeStatus.Pending)
            .OrderByDescending(request => request.DecidedAtUtc)
            .ThenByDescending(request => request.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var duplicates = party.TaxIdentityNumber is null
            ? []
            : await FindDuplicatesAsync(companyId, partyId, party.TaxIdentityNumber, cancellationToken);
        var canUpdate = await HasCompanyWideAsync(CounterpartyTaxIdentityPermissionsV1.Update, cancellationToken);
        var canApprove = await HasCompanyWideAsync(CounterpartyTaxIdentityPermissionsV1.Approve, cancellationToken);
        await RequireTaxIdentityReadStillAsync(companyId, actorId, additionalPermission: null, cancellationToken);
        return new CounterpartyTaxIdentityView(
            party.Id,
            companyId,
            party.Version.ToString(CultureInfo.InvariantCulture),
            party.TaxIdentityNumber,
            party.TaxIdentityNumber is null ? null : KindOf(party.TaxIdentityNumber),
            duplicates,
            pending is null
                ? null
                : ToView(pending, await FindDuplicatesAsync(
                    companyId, partyId, pending.ProposedTaxIdentityNumber, cancellationToken)),
            lastDecided is null ? null : ToView(lastDecided, []),
            new CounterpartyTaxIdentityViewer(
                canUpdate,
                canApprove,
                pending is not null && pending.IsRequestedBy(currentUser.UserId ?? string.Empty)));
    }

    public async Task<CounterpartyTaxIdentityDuplicateCheck?> CheckDuplicatesAsync(
        Guid partyId, string taxIdentityNumber, CancellationToken cancellationToken)
    {
        await RequireCompanyWidePermissionAsync(CounterpartyTaxIdentityPermissionsV1.Update, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        var number = ParseTaxIdentity(taxIdentityNumber);
        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId ?? throw new UnauthorizedAccessException("Cari okuması kimliği doğrulanmış kullanıcı ister.");
        var party = await FindFencedPartyAsync(companyId, partyId, tracking: false, cancellationToken);
        var result = party is null
            ? null
            : new CounterpartyTaxIdentityDuplicateCheck(
                number.Value,
                KindOf(number),
                await FindDuplicatesAsync(companyId, partyId, number.Value, cancellationToken));
        await RequireTaxIdentityReadStillAsync(
            companyId, actorId, CounterpartyTaxIdentityPermissionsV1.Update, cancellationToken);
        return result;
    }

    public async Task<CounterpartyCommandExecution> EnterTaxIdentityAsync(
        Guid partyId, EnterCounterpartyTaxIdentity command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var scope = await RequireCompanyWidePermissionAsync(
            CounterpartyTaxIdentityPermissionsV1.Update, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => RequireTaxIdentityCommandStillAsync(
            metadata, CounterpartyTaxIdentityPermissionsV1.Update, token);
        RequirePartyId(partyId);
        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var number = ParseTaxIdentity(command.TaxIdentityNumber);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            PartyId = partyId, ExpectedVersion = expectedVersion, TaxIdentityNumber = number.Value,
        });

        var party = await FindFencedPartyAsync(metadata.CompanyId, partyId, tracking: true, cancellationToken);
        var existing = await FindCommandExecutionAsync(EnterTaxIdentityCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, party is not null);
        }

        if (party is null)
        {
            return await CompleteCommandAsync(EnterTaxIdentityCommandName, Responses.NotFound(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (party.Version != expectedVersion)
        {
            return await CompleteCommandAsync(EnterTaxIdentityCommandName,
                Responses.VersionConflict(expectedVersion, party.Version),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (party.TaxIdentityNumber is not null)
        {
            return await CompleteCommandAsync(EnterTaxIdentityCommandName,
                TaxResponses.Refused(CounterpartyCommandResult.TaxIdentityChangeRequiresApproval),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        var beforeVersion = party.Version;
        party.EnterTaxIdentityNumber(number, DateTimeOffset.UtcNow);
        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            "counterparty.party.tax-identity.entered.v1",
            "Counterparty.Party", party.Id.ToString("D"),
            "EnterTaxIdentity",
            beforeVersion, party.Version,
            AuditActorTypes.User, metadata.ActorId,
            CounterpartyTaxIdentityPermissionsV1.Update, scope.RoleCodes,
            SerializeTaxIdentitySnapshot(null, beforeVersion),
            SerializeTaxIdentitySnapshot(party.TaxIdentityNumber, party.Version),
            Reason: "Cari vergi/kimlik numarası ilk giriş",
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
            metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));

        var result = await CompleteCommandAsync(EnterTaxIdentityCommandName, Responses.Succeeded(),
            keyHash, requestHash, metadata, new PartyConcurrencyContext(partyId, expectedVersion), cancellationToken, beforeSave);
        LogTaxIdentityCommand("enter", metadata, partyId, result);
        return result;
    }

    public async Task<CounterpartyCommandExecution> RequestTaxIdentityChangeAsync(
        Guid partyId, RequestCounterpartyTaxIdentityChange command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var scope = await RequireCompanyWidePermissionAsync(
            CounterpartyTaxIdentityPermissionsV1.Update, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => RequireTaxIdentityCommandStillAsync(
            metadata, CounterpartyTaxIdentityPermissionsV1.Update, token);
        RequirePartyId(partyId);
        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var proposed = ParseTaxIdentity(command.ProposedTaxIdentityNumber);
        ValidateReason(command.Reason);
        var reason = command.Reason.Trim();
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            PartyId = partyId, ExpectedVersion = expectedVersion, Proposed = proposed.Value, Reason = reason,
        });

        var party = await FindFencedPartyAsync(metadata.CompanyId, partyId, tracking: true, cancellationToken);
        var existing = await FindCommandExecutionAsync(
            RequestTaxIdentityChangeCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, party is not null);
        }

        var refusal = party is null
            ? Responses.NotFound()
            : party.Version != expectedVersion
                ? Responses.VersionConflict(expectedVersion, party.Version)
                : party.TaxIdentityNumber is null
                    ? TaxResponses.Refused(CounterpartyCommandResult.TaxIdentityNotSet)
                    : string.Equals(party.TaxIdentityNumber, proposed.Value, StringComparison.Ordinal)
                        ? TaxResponses.Refused(CounterpartyCommandResult.TaxIdentityUnchanged)
                        : await HasPendingChangeAsync(metadata.CompanyId, partyId, cancellationToken)
                            ? TaxResponses.Refused(CounterpartyCommandResult.TaxIdentityChangePending)
                            : null;
        if (refusal is not null)
        {
            return await CompleteCommandAsync(RequestTaxIdentityChangeCommandName, refusal,
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        var request = new PartyTaxIdentityChangeRequest(
            Guid.NewGuid(), metadata.CompanyId, partyId, party!.TaxIdentityNumber, proposed, party.Version,
            reason, metadata.ActorId, DateTimeOffset.UtcNow);
        dbContext.PartyTaxIdentityChangeRequests.Add(request);
        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            "counterparty.party.tax-identity.change-requested.v1",
            "Counterparty.PartyTaxIdentityChangeRequest", request.Id.ToString("D"),
            "RequestTaxIdentityChange",
            EntityVersionBefore: null, request.Version,
            AuditActorTypes.User, metadata.ActorId,
            CounterpartyTaxIdentityPermissionsV1.Update, scope.RoleCodes,
            BeforeJson: "{}",
            SerializeChangeRequestSnapshot(request),
            reason,
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
            metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));

        CounterpartyCommandExecution result;
        try
        {
            result = await CompleteCommandAsync(RequestTaxIdentityChangeCommandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }
        catch (DbUpdateException)
        {
            // A concurrent request won the one-pending-per-party index; this attempt stores the same refusal it
            // would have met a moment later. Any other failure is not ours to reinterpret.
            dbContext.ChangeTracker.Clear();
            if (!await HasPendingChangeAsync(metadata.CompanyId, partyId, cancellationToken))
            {
                throw;
            }

            result = await CompleteCommandAsync(RequestTaxIdentityChangeCommandName,
                TaxResponses.Refused(CounterpartyCommandResult.TaxIdentityChangePending),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        LogTaxIdentityCommand("change-request", metadata, partyId, result);
        return result;
    }

    public async Task<CounterpartyCommandExecution> DecideTaxIdentityChangeAsync(
        Guid partyId, DecideCounterpartyTaxIdentityChange command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var scope = await RequireCompanyWidePermissionAsync(
            CounterpartyTaxIdentityPermissionsV1.Approve, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => RequireTaxIdentityCommandStillAsync(
            metadata, CounterpartyTaxIdentityPermissionsV1.Approve, token);
        RequirePartyId(partyId);
        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        if (command.ChangeRequestId == Guid.Empty)
        {
            throw new ArgumentException("Değişiklik talebi kimliği boş olamaz.", nameof(command));
        }

        var approve = command.Decision switch
        {
            CounterpartyTaxIdentityDecisionsV1.Approve => true,
            CounterpartyTaxIdentityDecisionsV1.Reject => false,
            _ => throw new ArgumentException("Karar 'approve' veya 'reject' olmalıdır.", nameof(command)),
        };
        var note = command.Note?.Trim();
        if (note is { Length: > PartyTaxIdentityChangeRequest.NoteMaximumLength })
        {
            throw new ArgumentException("Karar notu 1000 karakterden uzun olamaz.", nameof(command));
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
            DecideTaxIdentityChangeCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, party is not null);
        }

        var request = party is null
            ? null
            : await dbContext.PartyTaxIdentityChangeRequests.SingleOrDefaultAsync(
                candidate => candidate.Id == command.ChangeRequestId
                    && candidate.CompanyId == metadata.CompanyId
                    && candidate.PartyId == partyId,
                cancellationToken);
        var refusal = party is null || request is null
            ? Responses.NotFound()
            : request.Status != PartyTaxIdentityChangeStatus.Pending
                ? TaxResponses.Refused(CounterpartyCommandResult.TaxIdentityChangeNotPending)
                : request.IsRequestedBy(metadata.ActorId)
                    ? TaxResponses.Refused(CounterpartyCommandResult.TaxIdentitySelfDecision)
                    : party.Version != expectedVersion
                        ? Responses.VersionConflict(expectedVersion, party.Version)
                        : approve && (party.Version != request.PartyVersionAtRequest
                            || !string.Equals(party.TaxIdentityNumber, request.CurrentTaxIdentityNumber,
                                StringComparison.Ordinal))
                            ? TaxResponses.Refused(CounterpartyCommandResult.TaxIdentityChangeStale)
                            : null;
        if (refusal is not null)
        {
            return await CompleteCommandAsync(DecideTaxIdentityChangeCommandName, refusal,
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        var now = DateTimeOffset.UtcNow;
        var requestVersionBefore = request!.Version;
        var approvalContext = JsonSerializer.Serialize(new
        {
            ChangeRequestId = request.Id,
            request.RequestedBy,
            request.RequestedAtUtc,
            DecidedBy = metadata.ActorId,
            Decision = approve ? CounterpartyTaxIdentityDecisionsV1.Approve : CounterpartyTaxIdentityDecisionsV1.Reject,
            Note = note,
        });
        if (approve)
        {
            var partyVersionBefore = party!.Version;
            var numberBefore = party.TaxIdentityNumber;
            party.ApplyApprovedTaxIdentityChange(
                request.CurrentTaxIdentityNumber,
                TaxIdentityNumber.Parse(request.ProposedTaxIdentityNumber),
                now);
            request.Approve(metadata.ActorId, note, now);
            auditEventCollector.Enqueue(new AuditEventDraft(
                metadata.CompanyId,
                "counterparty.party.tax-identity.change-approved.v1",
                "Counterparty.Party", party.Id.ToString("D"),
                "ApproveTaxIdentityChange",
                partyVersionBefore, party.Version,
                AuditActorTypes.User, metadata.ActorId,
                CounterpartyTaxIdentityPermissionsV1.Approve, scope.RoleCodes,
                SerializeTaxIdentitySnapshot(numberBefore, partyVersionBefore),
                SerializeTaxIdentitySnapshot(party.TaxIdentityNumber, party.Version),
                request.Reason,
                approvalContext, AuditAiInvolvement.None, AiContextJson: null,
                metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
                metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        }
        else
        {
            var before = SerializeChangeRequestSnapshot(request);
            request.Reject(metadata.ActorId, note, now);
            auditEventCollector.Enqueue(new AuditEventDraft(
                metadata.CompanyId,
                "counterparty.party.tax-identity.change-rejected.v1",
                "Counterparty.PartyTaxIdentityChangeRequest", request.Id.ToString("D"),
                "RejectTaxIdentityChange",
                requestVersionBefore, request.Version,
                AuditActorTypes.User, metadata.ActorId,
                CounterpartyTaxIdentityPermissionsV1.Approve, scope.RoleCodes,
                before, SerializeChangeRequestSnapshot(request),
                note ?? "Vergi/kimlik numarası değişiklik talebi reddedildi",
                approvalContext, AuditAiInvolvement.None, AiContextJson: null,
                metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
                metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        }

        var result = await CompleteCommandAsync(DecideTaxIdentityChangeCommandName, Responses.Succeeded(),
            keyHash, requestHash, metadata, new PartyConcurrencyContext(partyId, expectedVersion), cancellationToken, beforeSave);
        LogTaxIdentityCommand(approve ? "approve" : "reject", metadata, partyId, result);
        return result;
    }

    /// <summary>A stored success is not replayed to a caller whose company no longer has the card (non-disclosure).</summary>
    private static CounterpartyCommandExecution ReplayFenced(
        CommandExecution existing, string requestHash, CounterpartyCommandMetadata metadata, bool partyVisible)
    {
        var execution = ResolveExistingExecution(existing, requestHash, metadata);
        return !partyVisible && execution.Response.Result == CounterpartyCommandResult.Succeeded
            ? new(Responses.NotFound(), false, metadata.CorrelationId)
            : execution;
    }

    /// <summary>The party, only when the active company holds an account for it.</summary>
    private Task<Party?> FindFencedPartyAsync(
        Guid companyId, Guid partyId, bool tracking, CancellationToken cancellationToken)
    {
        var parties = tracking ? dbContext.Parties : dbContext.Parties.AsNoTracking();
        return parties.Where(party => party.Id == partyId
                && dbContext.CompanyPartyAccounts.Any(account =>
                    account.CompanyId == companyId && account.PartyId == party.Id))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private Task<long?> ReadFencedPartyVersionAsync(
        Guid companyId, Guid partyId, CancellationToken cancellationToken) =>
        dbContext.Parties.AsNoTracking()
            .Where(party => party.Id == partyId
                && dbContext.CompanyPartyAccounts.Any(account =>
                    account.CompanyId == companyId && account.PartyId == party.Id))
            .Select(party => (long?)party.Version)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<bool> HasPendingChangeAsync(Guid companyId, Guid partyId, CancellationToken cancellationToken) =>
        dbContext.PartyTaxIdentityChangeRequests.AsNoTracking().AnyAsync(
            request => request.CompanyId == companyId && request.PartyId == partyId
                && request.Status == PartyTaxIdentityChangeStatus.Pending,
            cancellationToken);

    /// <summary>
    /// Other Cari cards of the active company whose party carries the number. Only this company's accounts are joined,
    /// so a card another company holds is never named here.
    /// </summary>
    private async Task<IReadOnlyList<CounterpartyTaxIdentityDuplicate>> FindDuplicatesAsync(
        Guid companyId, Guid partyId, string number, CancellationToken cancellationToken)
    {
        var rows = await (
                from account in dbContext.CompanyPartyAccounts.AsNoTracking()
                join party in dbContext.Parties.AsNoTracking() on account.PartyId equals party.Id
                where account.CompanyId == companyId
                    && party.Id != partyId
                    && party.TaxIdentityNumber == number
                orderby party.Id
                select new { party.Id, party.Code, party.LegalName, account.IsActive })
            .Take(DuplicateLimit)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new CounterpartyTaxIdentityDuplicate(row.Id, row.Code.Value, row.LegalName, row.IsActive))
            .OrderBy(row => row.Code, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<bool> HasCompanyWideAsync(string permission, CancellationToken cancellationToken)
    {
        var scope = await authorizationService.GetPermissionScopeAsync(
            permission, companyContext.CompanyId, cancellationToken);
        return scope.IsGranted && scope.IsCompanyWide;
    }

    private async Task RequireTaxIdentityReadStillAsync(
        Guid companyId, string actorId, string? additionalPermission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
            || !companyContext.IsResolved
            || companyContext.CompanyId != companyId
            || !currentUser.CanAccessCompany(companyId))
        {
            throw new UnauthorizedAccessException("Cari vergi/kimlik okuma bağlamı değişti.");
        }

        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        if (additionalPermission is not null)
        {
            await RequireCompanyWidePermissionAsync(additionalPermission, cancellationToken);
        }
    }

    private async Task RequireTaxIdentityCommandStillAsync(
        CounterpartyCommandMetadata metadata, string permission, CancellationToken cancellationToken)
    {
        ValidateCommandMetadata(metadata);
        await RequireCompanyWidePermissionAsync(permission, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
    }

    private static TaxIdentityNumber ParseTaxIdentity(string? value) =>
        TaxIdentityNumber.TryParse(value, out var number, out var problem)
            ? number
            : throw new CounterpartyTaxIdentityInvalidException(problem);

    private static void RequirePartyId(Guid partyId)
    {
        if (partyId == Guid.Empty)
        {
            throw new ArgumentException("Cari kimliği boş olamaz.", nameof(partyId));
        }
    }

    private static string KindOf(TaxIdentityNumber number) => number.Kind == TaxIdentityKind.Vkn
        ? CounterpartyTaxIdentityKindsV1.Vkn
        : CounterpartyTaxIdentityKindsV1.Tckn;

    private static string KindOf(string storedNumber) => KindOf(TaxIdentityNumber.Parse(storedNumber));

    private static CounterpartyTaxIdentityChangeView ToView(
        PartyTaxIdentityChangeRequest request, IReadOnlyList<CounterpartyTaxIdentityDuplicate> proposedDuplicates) =>
        new(
            request.Id,
            request.Status switch
            {
                PartyTaxIdentityChangeStatus.Pending => CounterpartyTaxIdentityChangeStatusesV1.Pending,
                PartyTaxIdentityChangeStatus.Approved => CounterpartyTaxIdentityChangeStatusesV1.Approved,
                PartyTaxIdentityChangeStatus.Rejected => CounterpartyTaxIdentityChangeStatusesV1.Rejected,
                _ => throw new InvalidOperationException("Bilinmeyen vergi/kimlik değişiklik talebi durumu."),
            },
            request.CurrentTaxIdentityNumber,
            request.ProposedTaxIdentityNumber,
            KindOf(request.ProposedTaxIdentityNumber),
            request.Reason,
            request.RequestedBy,
            request.RequestedAtUtc,
            request.DecidedBy,
            request.DecidedAtUtc,
            request.DecisionNote,
            proposedDuplicates);

    private static string SerializeTaxIdentitySnapshot(string? number, long version) =>
        JsonSerializer.Serialize(new { TaxIdentityNumber = MaskTaxIdentity(number), Version = version });

    private static string SerializeChangeRequestSnapshot(PartyTaxIdentityChangeRequest request) =>
        JsonSerializer.Serialize(new
        {
            request.Id,
            request.CompanyId,
            request.PartyId,
            CurrentTaxIdentityNumber = MaskTaxIdentity(request.CurrentTaxIdentityNumber),
            ProposedTaxIdentityNumber = MaskTaxIdentity(request.ProposedTaxIdentityNumber),
            request.PartyVersionAtRequest,
            Status = request.Status.ToString(),
            request.RequestedBy,
            request.DecidedBy,
            request.DecisionNote,
            request.Version,
        });

    private static string? MaskTaxIdentity(string? number) => number is null
        ? null
        : $"{new string('*', number.Length - 4)}{number[^4..]}";

    private void LogTaxIdentityCommand(
        string operation, CounterpartyCommandMetadata metadata, Guid partyId, CounterpartyCommandExecution result)
    {
        // The number itself is never logged (a TCKN is personal data).
        logger.LogInformation(
            "Counterparty tax-identity command completed. Operation={Operation} CompanyId={CompanyId} PartyId={PartyId} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            operation, metadata.CompanyId, partyId, metadata.CorrelationId, result.Response.Result, result.IsReplay);
    }

    private static class TaxResponses
    {
        public static CounterpartyCommandResponse Refused(CounterpartyCommandResult result) => result switch
        {
            CounterpartyCommandResult.TaxIdentityChangeRequiresApproval => Problem(result, 409,
                "Numara değişikliği onay gerektirir.",
                "Bu carinin kayıtlı bir vergi/kimlik numarası var; değiştirmek için değişiklik talebi açın. Talep başka bir yetkili tarafından onaylanınca uygulanır."),
            CounterpartyCommandResult.TaxIdentityNotSet => Problem(result, 409,
                "Kayıtlı numara yok.",
                "Bu carinin henüz vergi/kimlik numarası yok; değişiklik talebi yerine numarayı doğrudan girin."),
            CounterpartyCommandResult.TaxIdentityUnchanged => Problem(result, 409,
                "Numara aynı.",
                "Önerilen vergi/kimlik numarası kayıtlı numarayla aynı."),
            CounterpartyCommandResult.TaxIdentityChangePending => Problem(result, 409,
                "Onay bekleyen talep var.",
                "Bu cari için onay bekleyen bir vergi/kimlik numarası değişiklik talebi zaten var."),
            CounterpartyCommandResult.TaxIdentityChangeNotPending => Problem(result, 409,
                "Talep artık beklemede değil.",
                "Bu değişiklik talebi daha önce onaylanmış veya reddedilmiş."),
            CounterpartyCommandResult.TaxIdentityChangeStale => Problem(result, 409,
                "Talep güncel değil.",
                "Carinin vergi/kimlik numarası talep açıldıktan sonra değişmiş; talep onaylanamaz. Talebi reddedip yenisini açın."),
            CounterpartyCommandResult.TaxIdentitySelfDecision => Problem(result, 409,
                "Kendi talebinizi karara bağlayamazsınız.",
                "Vergi/kimlik numarası değişikliğini talebi açan kişiden farklı bir yetkili onaylamalı veya reddetmelidir."),
            _ => throw new InvalidOperationException($"AT-009 ret sonucu değil: {result}."),
        };

        private static CounterpartyCommandResponse Problem(
            CounterpartyCommandResult result, int statusCode, string title, string detail) =>
            new(result, statusCode, title, detail, Resource: null, Conflict: null);
    }
}
