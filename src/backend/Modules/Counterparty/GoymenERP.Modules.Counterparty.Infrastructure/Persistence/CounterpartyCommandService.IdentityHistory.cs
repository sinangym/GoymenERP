using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// KLN-D38 (Sinan, 2026-11-14) VKN/TCKN change flow, Counterparty side.
/// <list type="bullet">
/// <item><b>Dated identity</b>: the tax-identity first entry and approved correction (AT-009 / CARI-DEC-004), the legal
/// name change approval (ALS-D111/D113) and the e-belge status check (SAT-D119) append a dated revision in the SAME save as the change.
/// History begins lazily: the value on file before the first dated change is written as an undated baseline in that same
/// save (the migration backfills nothing).</item>
/// <item><b>Succession</b>: a new legal person gets a new cari; <see cref="RecordPartySuccessionAsync"/> makes the old
/// cari's account in the active company passive and links it to the new one. K3 skeleton: separate company-wide
/// <c>counterparty.parties.succession.record</c> plus <c>counterparty.accounts.status.update</c> and
/// <c>counterparty.parties.read</c>, measured first and re-validated before the single save; the PREDECESSOR account's
/// version as the fence; one Idempotency-Key (stored terminal answers replay); one audit event; the passive-cari event
/// rides the same save. NO balance moves here: the balance moves only by an approved transfer voucher (devir fişi)
/// through a Finance port; Counterparty never writes Finance tables.</item>
/// </list>
/// </summary>
internal sealed partial class CounterpartyCommandService : ICounterpartyIdentityHistoryService
{
    private const string RecordPartySuccessionCommandName = "counterparty.party.succession.record.v1";

    public async Task<CounterpartyIdentityHistoryView?> GetIdentityHistoryAsync(
        Guid partyId, CancellationToken cancellationToken)
    {
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        RequirePartyId(partyId);
        var companyId = authorityFence.CompanyId;
        var account = await dbContext.CompanyPartyAccounts.AsNoTracking()
            .Where(candidate => candidate.CompanyId == companyId && candidate.PartyId == partyId)
            .Select(candidate => new { candidate.Version, candidate.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        var party = account is null
            ? null
            : await dbContext.Parties.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == partyId, cancellationToken);
        if (account is null || party is null)
        {
            await authorityFence.RequireStillAsync(
                currentUser, companyContext, authorityRevalidationService,
                [ErpPermissions.CounterpartyPartiesRead], cancellationToken);
            return null;
        }

        var revisions = await dbContext.PartyIdentityRevisions.AsNoTracking()
            .Where(revision => revision.PartyId == partyId)
            .OrderBy(revision => revision.Attribute)
            .ThenBy(revision => revision.Sequence)
            .ToListAsync(cancellationToken);
        var statusRevisions = string.IsNullOrEmpty(party.TaxIdentityNumber)
            ? new List<CustomerEDocumentStatusRevision>()
            : await (
                    from profile in dbContext.CustomerEDocumentProfiles.AsNoTracking()
                    join revision in dbContext.CustomerEDocumentStatusRevisions.AsNoTracking()
                        on profile.Id equals revision.ProfileId
                    where profile.TaxIdentityNumber == party.TaxIdentityNumber
                    orderby revision.Sequence
                    select revision)
                .ToListAsync(cancellationToken);
        var successorLinks = await (
                from succession in dbContext.PartySuccessions.AsNoTracking()
                join other in dbContext.Parties.AsNoTracking() on succession.SuccessorPartyId equals other.Id
                where succession.CompanyId == companyId && succession.PredecessorPartyId == partyId
                select new { Succession = succession, OtherCode = other.Code, OtherLegalName = other.LegalName })
            .ToListAsync(cancellationToken);
        var predecessorLinks = await (
                from succession in dbContext.PartySuccessions.AsNoTracking()
                join other in dbContext.Parties.AsNoTracking() on succession.PredecessorPartyId equals other.Id
                where succession.CompanyId == companyId && succession.SuccessorPartyId == partyId
                select new { Succession = succession, OtherCode = other.Code, OtherLegalName = other.LegalName })
            .ToListAsync(cancellationToken);
        var canRecordSuccession = await HasCompanyWideAsync(
                ErpPermissions.CounterpartyPartiesSuccessionRecord, cancellationToken)
            && await HasCompanyWideAsync(ErpPermissions.CounterpartyAccountsStatusUpdate, cancellationToken);
        await authorityFence.RequireStillAsync(
            currentUser, companyContext, authorityRevalidationService,
            [ErpPermissions.CounterpartyPartiesRead], cancellationToken);

        var links = successorLinks.Concat(predecessorLinks)
            .Select(row => new CounterpartyPartySuccessionView(
                row.Succession.PredecessorPartyId,
                row.Succession.SuccessorPartyId,
                row.Succession.PredecessorPartyId == partyId
                    ? row.Succession.SuccessorPartyId
                    : row.Succession.PredecessorPartyId,
                row.OtherCode.Value,
                row.OtherLegalName,
                ToContract(row.Succession.Kind),
                row.Succession.LegalBasis,
                row.Succession.EffectiveOn,
                row.Succession.RecordedAtUtc))
            .ToArray();
        return new CounterpartyIdentityHistoryView(
            party.Id,
            companyId,
            party.Code.Value,
            party.Kind,
            account.Version.ToString(CultureInfo.InvariantCulture),
            account.IsActive,
            party.TaxIdentityNumber,
            party.LegalName,
            revisions.Select(revision => ToView(revision, companyId)).ToArray(),
            statusRevisions.Select(revision => new CounterpartyEDocumentStatusRevisionView(
                    revision.Sequence.ToString(CultureInfo.InvariantCulture),
                    ToContract(revision.Status),
                    revision.EffectiveFrom,
                    revision.RecordedInCompanyId == companyId,
                    revision.RecordedAtUtc))
                .ToArray(),
            links.SingleOrDefault(link => link.PredecessorPartyId == partyId),
            links.Where(link => link.SuccessorPartyId == partyId)
                .OrderBy(link => link.EffectiveOn)
                .ThenBy(link => link.OtherPartyCode, StringComparer.Ordinal)
                .ToArray(),
            new CounterpartyIdentityHistoryViewer(canRecordSuccession));
    }

    public async Task<CounterpartyCommandExecution> RecordPartySuccessionAsync(
        Guid predecessorPartyId,
        RecordCounterpartyPartySuccession command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        if (authorityFence.CompanyId != metadata.CompanyId
            || !string.Equals(authorityFence.ActorId, metadata.ActorId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Cari halef command bağlamı değişti.");
        }

        var scope = await RequireCompanyWidePermissionAsync(
            ErpPermissions.CounterpartyPartiesSuccessionRecord, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyAccountsStatusUpdate, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => authorityFence.RequireStillAsync(
            currentUser, companyContext, authorityRevalidationService,
            [
                ErpPermissions.CounterpartyPartiesSuccessionRecord,
                ErpPermissions.CounterpartyAccountsStatusUpdate,
                ErpPermissions.CounterpartyPartiesRead,
            ],
            token);
        RequirePartyId(predecessorPartyId);
        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        if (command.SuccessorPartyId == Guid.Empty)
        {
            throw new ArgumentException("successorPartyId: Halef cari kimliği boş olamaz.", nameof(command));
        }

        var kind = ToDomain(command.Kind);
        var legalBasis = NormalizeLegalBasisInput(command.LegalBasis);
        var recordedAt = timeProvider.GetUtcNow();
        if (command.EffectiveOn > CounterpartyBusinessDay.DateOf(recordedAt))
        {
            throw new ArgumentException(
                "effectiveOn: Devralma tarihi bugünün iş gününden sonra olamaz.", nameof(command));
        }

        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            PredecessorPartyId = predecessorPartyId,
            ExpectedVersion = expectedVersion,
            command.SuccessorPartyId,
            Kind = kind,
            LegalBasis = legalBasis,
            command.EffectiveOn,
        });

        var account = await dbContext.CompanyPartyAccounts
            .SingleOrDefaultAsync(
                candidate => candidate.CompanyId == metadata.CompanyId && candidate.PartyId == predecessorPartyId,
                cancellationToken);
        var existing = await FindCommandExecutionAsync(RecordPartySuccessionCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, account is not null);
        }

        var successorAccount = await dbContext.CompanyPartyAccounts.AsNoTracking()
            .Where(candidate =>
                candidate.CompanyId == metadata.CompanyId && candidate.PartyId == command.SuccessorPartyId)
            .Select(candidate => new { candidate.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null || successorAccount is null)
        {
            return await CompleteCommandAsync(RecordPartySuccessionCommandName, Responses.NotFound(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (command.SuccessorPartyId == predecessorPartyId)
        {
            return await CompleteCommandAsync(RecordPartySuccessionCommandName,
                SuccessionResponses.Refused(CounterpartyCommandResult.SuccessionSameParty),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (account.Version != expectedVersion)
        {
            return await CompleteCommandAsync(RecordPartySuccessionCommandName,
                Responses.VersionConflict(expectedVersion, account.Version),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        var taxIdentities = await dbContext.Parties.AsNoTracking()
            .Where(party => party.Id == predecessorPartyId || party.Id == command.SuccessorPartyId)
            .Select(party => new { party.Id, party.TaxIdentityNumber })
            .ToListAsync(cancellationToken);
        var predecessorNumber = taxIdentities.Single(row => row.Id == predecessorPartyId).TaxIdentityNumber;
        var successorNumber = taxIdentities.Single(row => row.Id == command.SuccessorPartyId).TaxIdentityNumber;
        if (!string.IsNullOrEmpty(predecessorNumber)
            && string.Equals(predecessorNumber, successorNumber, StringComparison.Ordinal))
        {
            // KLN-D38 (3): the same VKN is the same legal person — it keeps its cari; that is not a succession.
            return await CompleteCommandAsync(RecordPartySuccessionCommandName,
                SuccessionResponses.Refused(CounterpartyCommandResult.SuccessionSameTaxIdentity),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (!successorAccount.IsActive)
        {
            return await CompleteCommandAsync(RecordPartySuccessionCommandName,
                SuccessionResponses.Refused(CounterpartyCommandResult.SuccessionSuccessorInactive),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        var alreadyRecorded = await dbContext.PartySuccessions.AsNoTracking().AnyAsync(
            succession => succession.CompanyId == metadata.CompanyId
                && succession.PredecessorPartyId == predecessorPartyId,
            cancellationToken);
        if (alreadyRecorded)
        {
            return await CompleteCommandAsync(RecordPartySuccessionCommandName,
                SuccessionResponses.Refused(CounterpartyCommandResult.SuccessionAlreadyRecorded),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (await ClosesSuccessionCycleAsync(
                metadata.CompanyId, predecessorPartyId, command.SuccessorPartyId, cancellationToken))
        {
            return await CompleteCommandAsync(RecordPartySuccessionCommandName,
                SuccessionResponses.Refused(CounterpartyCommandResult.SuccessionCycle),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        var versionBefore = account.Version;
        var isActiveBefore = account.IsActive;
        account.Deactivate();
        var succession = new PartySuccession(
            Guid.NewGuid(),
            metadata.CompanyId,
            predecessorPartyId,
            command.SuccessorPartyId,
            kind,
            legalBasis,
            command.EffectiveOn,
            metadata.ActorId,
            recordedAt);
        dbContext.PartySuccessions.Add(succession);
        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            "counterparty.party.succession.recorded.v1",
            "Counterparty.CompanyPartyAccount",
            account.Id.ToString("D"),
            "RecordSuccession",
            versionBefore,
            account.Version,
            AuditActorTypes.User,
            metadata.ActorId,
            ErpPermissions.CounterpartyPartiesSuccessionRecord,
            scope.RoleCodes,
            SerializeAccountAuditSnapshot(account, isActiveBefore, versionBefore),
            SerializeSuccessionAuditSnapshot(account, succession),
            legalBasis,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));
        if (isActiveBefore)
        {
            // F-027: the old cari's passive event rides the same save (discarded with the change on a conflict).
            EnqueuePartyDeactivatedEvent(account, SuccessionDeactivationReason, metadata);
        }

        var result = await CompleteCommandAsync(RecordPartySuccessionCommandName, Responses.Succeeded(),
            keyHash, requestHash, metadata, new AccountConcurrencyContext(predecessorPartyId, expectedVersion),
            cancellationToken, beforeSave);
        // The legal basis is user text and stays out of the log; the audit event carries it.
        logger.LogInformation(
            "Counterparty party-succession command completed. CompanyId={CompanyId} PredecessorPartyId={PredecessorPartyId} SuccessorPartyId={SuccessorPartyId} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            metadata.CompanyId, predecessorPartyId, command.SuccessorPartyId, metadata.CorrelationId,
            result.Response.Result, result.IsReplay);
        return result;
    }

    /// <summary>The reason the passive-cari event carries when a succession closes the old account (no user text).</summary>
    private const string SuccessionDeactivationReason = "Yeni tüzel/gerçek kişiye halef kaydı (KLN-D38)";

    /// <summary>
    /// True when linking <paramref name="predecessorPartyId"/> → <paramref name="successorPartyId"/> would close a cycle in
    /// the company's chain: following the successor's own successors reaches the predecessor. Each predecessor has at most
    /// one successor, so the walk is a single path; the visited set stops it on any stored loop as well.
    /// </summary>
    private async Task<bool> ClosesSuccessionCycleAsync(
        Guid companyId, Guid predecessorPartyId, Guid successorPartyId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid> { successorPartyId };
        var current = successorPartyId;
        while (true)
        {
            var next = await dbContext.PartySuccessions.AsNoTracking()
                .Where(link => link.CompanyId == companyId && link.PredecessorPartyId == current)
                .Select(link => (Guid?)link.SuccessorPartyId)
                .SingleOrDefaultAsync(cancellationToken);
            if (next is null)
            {
                return false;
            }

            if (next.Value == predecessorPartyId || !visited.Add(next.Value))
            {
                return true;
            }

            current = next.Value;
        }
    }

    /// <summary>
    /// KLN-D38: appends the dated revision of <paramref name="attribute"/> the party now holds, in the caller's save. When the
    /// party has no revision of that attribute yet and held a value before, that value is first written as the undated
    /// baseline at <paramref name="versionBefore"/>. The first VKN/TCKN entry is undated itself (no baseline). Any other
    /// revision is effective from the Europe/Istanbul business day of <paramref name="now"/>.
    /// </summary>
    private async Task AppendIdentityRevisionAsync(
        Party party,
        PartyIdentityAttribute attribute,
        string? valueBefore,
        long versionBefore,
        PartyIdentityRevisionKind kind,
        string? reason,
        Guid? sourceChangeRequestId,
        CounterpartyCommandMetadata metadata,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var value = attribute switch
        {
            PartyIdentityAttribute.TaxIdentityNumber => party.TaxIdentityNumber,
            PartyIdentityAttribute.LegalName => party.LegalName,
            _ => throw new ArgumentOutOfRangeException(nameof(attribute), attribute, "Bilinmeyen kimlik alanı."),
        } ?? throw new InvalidOperationException("Tarihli kimlik kaydı boş bir değer için yazılamaz.");
        var undated = kind == PartyIdentityRevisionKind.InitialEntry;
        if (!undated && !string.IsNullOrEmpty(valueBefore))
        {
            var hasHistory = await dbContext.PartyIdentityRevisions.AsNoTracking().AnyAsync(
                revision => revision.PartyId == party.Id && revision.Attribute == attribute,
                cancellationToken);
            if (!hasHistory)
            {
                dbContext.PartyIdentityRevisions.Add(PartyIdentityRevision.Baseline(
                    Guid.NewGuid(), party.Id, attribute, versionBefore, valueBefore, now));
            }
        }

        dbContext.PartyIdentityRevisions.Add(new PartyIdentityRevision(
            Guid.NewGuid(),
            party.Id,
            attribute,
            party.Version,
            value,
            undated ? null : CounterpartyBusinessDay.DateOf(now),
            kind,
            reason,
            sourceChangeRequestId,
            metadata.ActorId,
            metadata.CompanyId,
            now));
    }

    /// <summary>KLN-D38: the status the profile now holds, dated by the business day of the check (or reset).</summary>
    private void AppendEDocumentStatusRevision(
        CustomerEDocumentProfile profile, CounterpartyCommandMetadata metadata, DateTimeOffset now)
    {
        dbContext.CustomerEDocumentStatusRevisions.Add(new CustomerEDocumentStatusRevision(
            Guid.NewGuid(),
            profile.Id,
            profile.Version,
            profile.EDocumentStatus,
            CounterpartyBusinessDay.DateOf(now),
            CustomerEDocumentStatusRevisionKind.Recorded,
            metadata.ActorId,
            metadata.CompanyId,
            now));
    }

    /// <summary>
    /// KLN-D38: the first dated status change of a profile keeps the earlier KNOWN check as the baseline, dated by that
    /// check's business day. An earlier Unknown (never checked) needs no baseline: no revision already reads Unknown.
    /// </summary>
    private async Task AppendEDocumentStatusBaselineAsync(
        CustomerEDocumentProfile profile,
        EDocumentStatus statusBefore,
        DateTimeOffset? checkedAtBefore,
        long versionBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (statusBefore == EDocumentStatus.Unknown || checkedAtBefore is not { } checkedAt)
        {
            return;
        }

        var hasHistory = await dbContext.CustomerEDocumentStatusRevisions.AsNoTracking().AnyAsync(
            revision => revision.ProfileId == profile.Id, cancellationToken);
        if (hasHistory)
        {
            return;
        }

        dbContext.CustomerEDocumentStatusRevisions.Add(new CustomerEDocumentStatusRevision(
            Guid.NewGuid(),
            profile.Id,
            versionBefore,
            statusBefore,
            CounterpartyBusinessDay.DateOf(checkedAt),
            CustomerEDocumentStatusRevisionKind.Baseline,
            recordedByUserId: null,
            recordedInCompanyId: null,
            now));
    }

    /// <summary>The submitted legal basis as stored; blank or too long is a 400, never truncated.</summary>
    private static string NormalizeLegalBasisInput(string? legalBasis)
    {
        try
        {
            return PartySuccession.NormalizeLegalBasis(legalBasis);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentException(
                $"legalBasis: Hukuki dayanak {PartySuccession.LegalBasisMaximumLength} karakterden uzun olamaz.",
                nameof(legalBasis));
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("legalBasis: Hukuki dayanak boş olamaz.", nameof(legalBasis));
        }
    }

    private static PartySuccessionKind ToDomain(CounterpartyPartySuccessionKindV1 kind) => kind switch
    {
        CounterpartyPartySuccessionKindV1.Conversion => PartySuccessionKind.Conversion,
        CounterpartyPartySuccessionKindV1.Merger => PartySuccessionKind.Merger,
        CounterpartyPartySuccessionKindV1.Death => PartySuccessionKind.Death,
        _ => throw new ArgumentException("kind: Geçersiz halef türü.", nameof(kind)),
    };

    private static CounterpartyPartySuccessionKindV1 ToContract(PartySuccessionKind kind) => kind switch
    {
        PartySuccessionKind.Conversion => CounterpartyPartySuccessionKindV1.Conversion,
        PartySuccessionKind.Merger => CounterpartyPartySuccessionKindV1.Merger,
        PartySuccessionKind.Death => CounterpartyPartySuccessionKindV1.Death,
        _ => throw new InvalidOperationException("Bilinmeyen halef türü."),
    };

    private static CounterpartyIdentityRevisionView ToView(PartyIdentityRevision revision, Guid activeCompanyId)
    {
        var recordedHere = revision.RecordedInCompanyId == activeCompanyId;
        return new CounterpartyIdentityRevisionView(
            revision.Attribute switch
            {
                PartyIdentityAttribute.TaxIdentityNumber => CounterpartyIdentityAttributeV1.TaxIdentityNumber,
                PartyIdentityAttribute.LegalName => CounterpartyIdentityAttributeV1.LegalName,
                _ => throw new InvalidOperationException("Bilinmeyen kimlik alanı."),
            },
            revision.Kind switch
            {
                PartyIdentityRevisionKind.Baseline => CounterpartyIdentityRevisionKindV1.Baseline,
                PartyIdentityRevisionKind.InitialEntry => CounterpartyIdentityRevisionKindV1.InitialEntry,
                PartyIdentityRevisionKind.Correction => CounterpartyIdentityRevisionKindV1.Correction,
                PartyIdentityRevisionKind.DatedChange => CounterpartyIdentityRevisionKindV1.DatedChange,
                _ => throw new InvalidOperationException("Bilinmeyen kimlik değişikliği türü."),
            },
            revision.Sequence.ToString(CultureInfo.InvariantCulture),
            revision.Value,
            revision.EffectiveFrom,
            recordedHere ? revision.Reason : null,
            recordedHere,
            revision.RecordedAtUtc);
    }

    private static string SerializeSuccessionAuditSnapshot(CompanyPartyAccount account, PartySuccession succession) =>
        JsonSerializer.Serialize(new
        {
            account.Id,
            account.CompanyId,
            account.PartyId,
            account.IsActive,
            account.Version,
            Succession = new
            {
                succession.Id,
                succession.PredecessorPartyId,
                succession.SuccessorPartyId,
                Kind = succession.Kind.ToString(),
                succession.LegalBasis,
                EffectiveOn = succession.EffectiveOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                BalanceMoved = false,
            },
        });

    private static class SuccessionResponses
    {
        public static CounterpartyCommandResponse Refused(CounterpartyCommandResult result) => result switch
        {
            CounterpartyCommandResult.SuccessionSameParty => Problem(result, 422,
                "Cari kendi halefi olamaz.",
                "Öncül ve halef aynı cari olamaz; yeni tüzel/gerçek kişi için ayrı bir cari seçin."),
            CounterpartyCommandResult.SuccessionSameTaxIdentity => Problem(result, 422,
                "Aynı vergi/kimlik numarası.",
                "Aynı VKN/TCKN aynı kişidir ve aynı caride kalır; tür değişikliği halef kaydı değildir."),
            CounterpartyCommandResult.SuccessionSuccessorInactive => Problem(result, 409,
                "Halef cari pasif.",
                "Halef olarak seçilen cari bu şirkette pasif; önce aktif bir halef cari seçin."),
            CounterpartyCommandResult.SuccessionCycle => Problem(result, 409,
                "Halef zinciri döngü oluşturuyor.",
                "Seçilen halef zinciri bu cariye geri dönüyor; öncül/halef bağı döngü oluşturamaz."),
            CounterpartyCommandResult.SuccessionAlreadyRecorded => Problem(result, 409,
                "Halef zaten kayıtlı.",
                "Bu cari için bu şirkette bir halef zaten kayıtlı; bağ değiştirilemez."),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Halef reddi değil."),
        };

        private static CounterpartyCommandResponse Problem(
            CounterpartyCommandResult result, int statusCode, string title, string detail) =>
            new(result, statusCode, title, detail, Resource: null, Conflict: null);
    }
}
