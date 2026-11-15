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
/// F-027 / EK-18 #76-#77 (SINAN_KARARLARI_2026-10-21) duplicate party merge. Party is global, so a merge reaches every
/// company that holds an account for either party; the command therefore requires the company-wide
/// <c>counterparty.parties.merge.approve</c> and <c>counterparty.parties.read</c> permissions in EACH such company (the
/// group-manager persona is a grant decision, never made here), measured first and re-validated before the single save.
/// <list type="bullet">
/// <item>One Idempotency-Key (stored terminal answers replay); both PARTY versions are fences (the survivor's through a
/// guarded no-op predicate, the duplicate's through its real update).</item>
/// <item>The survivor must be held by the active company; a duplicate that is unknown, or that sits in a company where
/// the actor lacks either permission, is the same 404 (non-disclosure).</item>
/// <item>The duplicate becomes passive and points at the survivor for good; a party already merged, a survivor that was
/// itself merged and two different VKN/TCKN values are refused (409). There is no unmerge command (#77).</item>
/// <item>One append-only <see cref="PartyMerge"/> record, one audit event in the approving company and one
/// <see cref="CounterpartyPartyMergedOutboxV1"/> row per affected company, all in one transaction. Company accounts,
/// balances, documents and other modules' references are not touched here: each consumer moves its own.</item>
/// </list>
/// </summary>
internal sealed partial class CounterpartyCommandService : ICounterpartyPartyMergeService
{
    private const string ApprovePartyMergeCommandName = "counterparty.party.merge.approve.v1";

    private static readonly string[] PartyMergePermissions =
    [
        CounterpartyPartyMergePermissionsV1.Approve,
        ErpPermissions.CounterpartyPartiesRead,
    ];

    public async Task<CounterpartyPartyMergeView?> GetMergeAsync(
        Guid partyId, CancellationToken cancellationToken)
    {
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        var companyId = authorityFence.CompanyId;
        var party = await FindFencedPartyAsync(companyId, partyId, tracking: false, cancellationToken);
        CounterpartyPartyMergeTarget? mergedInto = null;
        IReadOnlyList<CounterpartyPartyMergeCandidate> candidates = [];
        if (party?.MergedIntoPartyId is { } survivorId)
        {
            var survivor = await FindFencedPartyAsync(companyId, survivorId, tracking: false, cancellationToken);
            mergedInto = new CounterpartyPartyMergeTarget(survivorId, survivor?.Code.Value);
        }
        else if (party?.TaxIdentityNumber is { } number)
        {
            candidates = await FindMergeCandidatesAsync(companyId, party.Id, number, cancellationToken);
        }

        var canApprove = await HasCompanyWideAsync(CounterpartyPartyMergePermissionsV1.Approve, cancellationToken);
        await authorityFence.RequireStillAsync(
            currentUser, companyContext, authorityRevalidationService,
            [ErpPermissions.CounterpartyPartiesRead], cancellationToken);
        return party is null ? null : new CounterpartyPartyMergeView(
            party.Id,
            companyId,
            party.Code.Value,
            party.Version.ToString(CultureInfo.InvariantCulture),
            party.LegalName,
            mergedInto,
            candidates,
            new CounterpartyPartyMergeViewer(canApprove && mergedInto is null));
    }

    public async Task<CounterpartyCommandExecution> ApproveMergeAsync(
        Guid survivorPartyId, ApproveCounterpartyPartyMerge command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        if (authorityFence.CompanyId != metadata.CompanyId
            || !string.Equals(authorityFence.ActorId, metadata.ActorId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Cari birleştirme command bağlamı değişti.");
        }

        var scope = await RequireCompanyWidePermissionAsync(
            CounterpartyPartyMergePermissionsV1.Approve, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> activeBeforeSave = token => authorityFence.RequireStillAsync(
            currentUser, companyContext, authorityRevalidationService, PartyMergePermissions, token);
        RequirePartyId(survivorPartyId);
        var mergedPartyId = command.MergedPartyId;
        if (mergedPartyId == Guid.Empty)
        {
            throw new ArgumentException("mergedPartyId: Birleştirilecek cari kimliği boş olamaz.", nameof(command));
        }

        if (mergedPartyId == survivorPartyId)
        {
            throw new ArgumentException("mergedPartyId: Cari kendisiyle birleştirilemez.", nameof(command));
        }

        var expectedSurvivorVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var expectedMergedVersion = ParseMergedPartyVersion(command.MergedPartyExpectedVersion);
        ValidateReason(command.Reason);
        var reason = command.Reason.Trim();
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            SurvivorPartyId = survivorPartyId,
            MergedPartyId = mergedPartyId,
            ExpectedVersion = expectedSurvivorVersion,
            MergedPartyExpectedVersion = expectedMergedVersion,
            Reason = reason,
        });

        var survivor = await FindFencedPartyAsync(metadata.CompanyId, survivorPartyId, tracking: true, cancellationToken);
        var merged = await dbContext.Parties
            .SingleOrDefaultAsync(party => party.Id == mergedPartyId, cancellationToken);
        var affectedCompanyIds = await dbContext.CompanyPartyAccounts.AsNoTracking()
            .Where(account => account.PartyId == survivorPartyId || account.PartyId == mergedPartyId)
            .Select(account => account.CompanyId)
            .Distinct()
            .OrderBy(companyId => companyId)
            .ToArrayAsync(cancellationToken);
        var mergedHeld = merged is not null && await dbContext.CompanyPartyAccounts.AsNoTracking()
            .AnyAsync(account => account.PartyId == mergedPartyId, cancellationToken);
        var visible = survivor is not null
            && mergedHeld
            && await HoldsPartyMergeAuthorityEverywhereAsync(metadata.CompanyId, affectedCompanyIds, cancellationToken);
        Func<CancellationToken, Task> fullBeforeSave = async token =>
        {
            await activeBeforeSave(token);
            await RequirePartyMergeAuthorityStillAsync(metadata.CompanyId, affectedCompanyIds, token);
            await activeBeforeSave(token);
        };

        var existing = await FindCommandExecutionAsync(ApprovePartyMergeCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await activeBeforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, visible);
        }

        if (!visible || survivor is null || merged is null)
        {
            return await CompleteCommandAsync(ApprovePartyMergeCommandName, Responses.NotFound(),
                keyHash, requestHash, metadata, null, cancellationToken, activeBeforeSave);
        }

        var refusal = survivor.Version != expectedSurvivorVersion
            ? Responses.VersionConflict(expectedSurvivorVersion, survivor.Version)
            : merged.Version != expectedMergedVersion
                ? Responses.VersionConflict(expectedMergedVersion, merged.Version)
                : merged.MergedIntoPartyId is not null
                    ? PartyMergeResponses.AlreadyMerged()
                    : survivor.MergedIntoPartyId is not null
                        ? PartyMergeResponses.TargetMerged()
                        : survivor.TaxIdentityNumber is not null
                          && merged.TaxIdentityNumber is not null
                          && !string.Equals(survivor.TaxIdentityNumber, merged.TaxIdentityNumber, StringComparison.Ordinal)
                            ? PartyMergeResponses.TaxIdentityMismatch()
                            : null;
        if (refusal is not null)
        {
            var refused = await CompleteCommandAsync(ApprovePartyMergeCommandName, refusal,
                keyHash, requestHash, metadata, null, cancellationToken, fullBeforeSave);
            LogPartyMergeCommand(metadata, survivorPartyId, mergedPartyId, affectedCompanyIds.Length, refused);
            return refused;
        }

        var now = timeProvider.GetUtcNow();
        var mergedVersionBefore = merged.Version;
        var before = SerializePartyMergeSnapshot(merged, survivor.Id);
        merged.MergeInto(survivor, now);
        // The survivor is not changed, but its Version + rowversion predicate must hold at the save: a concurrent merge
        // of the survivor itself (or any other edit) makes this a version conflict instead of a merge into a moved card.
        dbContext.Entry(survivor).Property(party => party.MergedIntoPartyId).IsModified = true;

        var merge = new PartyMerge(
            Guid.NewGuid(),
            metadata.CompanyId,
            survivor.Id,
            survivor.Version,
            merged.Id,
            merged.Version,
            reason,
            metadata.ActorId,
            now,
            metadata.CorrelationId);
        dbContext.PartyMerges.Add(merge);
        foreach (var companyId in affectedCompanyIds)
        {
            EnqueuePartyMergedEvent(companyId, merge, metadata, now);
        }

        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            CounterpartyPartyMergedOutboxV1.EventType,
            "Counterparty.Party", merged.Id.ToString("D"),
            "ApproveMerge",
            mergedVersionBefore, merged.Version,
            AuditActorTypes.User, metadata.ActorId,
            CounterpartyPartyMergePermissionsV1.Approve, scope.RoleCodes,
            before,
            SerializePartyMergeSnapshot(merged, survivor.Id, merge, affectedCompanyIds),
            reason,
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
            metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));

        var result = await CompleteCommandAsync(ApprovePartyMergeCommandName, Responses.Succeeded(),
            keyHash, requestHash, metadata,
            new PartyMergeConcurrencyContext(
                survivorPartyId, expectedSurvivorVersion, mergedPartyId, expectedMergedVersion),
            cancellationToken, fullBeforeSave);
        LogPartyMergeCommand(metadata, survivorPartyId, mergedPartyId, affectedCompanyIds.Length, result);
        return result;
    }

    private static long ParseMergedPartyVersion(string? value)
    {
        try
        {
            return CounterpartyContactInformationEditor.ParseVersion(value);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(
                "mergedPartyExpectedVersion: Sürüm 1 ile Int64 üst sınırı arasında kanonik ondalık metin olmalıdır.",
                nameof(value));
        }
    }

    /// <summary>
    /// Other unmerged cards of the active company whose party carries the same number. Only this company's accounts are
    /// joined, so a card another company holds is never named here.
    /// </summary>
    private async Task<IReadOnlyList<CounterpartyPartyMergeCandidate>> FindMergeCandidatesAsync(
        Guid companyId, Guid partyId, string number, CancellationToken cancellationToken)
    {
        var rows = await (
                from account in dbContext.CompanyPartyAccounts.AsNoTracking()
                join party in dbContext.Parties.AsNoTracking() on account.PartyId equals party.Id
                where account.CompanyId == companyId
                    && party.Id != partyId
                    && party.MergedIntoPartyId == null
                    && party.TaxIdentityNumber == number
                orderby party.Id
                select new { party.Id, party.Code, party.LegalName, party.Version })
            .Take(DuplicateLimit)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new CounterpartyPartyMergeCandidate(
                row.Id, row.Code.Value, row.LegalName, row.Version.ToString(CultureInfo.InvariantCulture)))
            .OrderBy(row => row.Code, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Both merge permissions, company-wide, in every company that holds an account for either party. The active company
    /// was measured already; the others are measured here and must also be companies the caller may access.
    /// </summary>
    private async Task<bool> HoldsPartyMergeAuthorityEverywhereAsync(
        Guid activeCompanyId, IReadOnlyCollection<Guid> affectedCompanyIds, CancellationToken cancellationToken)
    {
        if (!affectedCompanyIds.Contains(activeCompanyId))
        {
            return false;
        }

        foreach (var companyId in affectedCompanyIds.Where(companyId => companyId != activeCompanyId))
        {
            if (!currentUser.CanAccessCompany(companyId))
            {
                return false;
            }

            foreach (var permission in PartyMergePermissions)
            {
                var permissionScope = await authorizationService.GetPermissionScopeAsync(
                    permission, companyId, cancellationToken);
                if (!permissionScope.IsGranted || !permissionScope.IsCompanyWide)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>The same authority re-measured as it stands now, right before the single save.</summary>
    private async Task RequirePartyMergeAuthorityStillAsync(
        Guid activeCompanyId, IReadOnlyCollection<Guid> affectedCompanyIds, CancellationToken cancellationToken)
    {
        foreach (var companyId in affectedCompanyIds.Where(companyId => companyId != activeCompanyId))
        {
            if (!currentUser.CanAccessCompany(companyId))
            {
                throw new UnauthorizedAccessException("Cari birleştirme için etkilenen şirket erişimi artık geçerli değil.");
            }

            foreach (var permission in PartyMergePermissions)
            {
                await RequireFreshCompanyWidePermissionAsync(permission, companyId, cancellationToken);
            }
        }
    }

    /// <summary>
    /// The conflicting fence after a lost save: the survivor (through the active company's account) first, then the
    /// duplicate. A card that has disappeared from that view is a 404.
    /// </summary>
    private async Task<(long ExpectedVersion, long? CurrentVersion)> ReadPartyMergeConflictAsync(
        Guid companyId, PartyMergeConcurrencyContext context, CancellationToken cancellationToken)
    {
        var survivorVersion = await ReadFencedPartyVersionAsync(companyId, context.PartyId, cancellationToken);
        if (survivorVersion is null || survivorVersion.Value != context.ExpectedVersion)
        {
            return (context.ExpectedVersion, survivorVersion);
        }

        var mergedVersion = await dbContext.Parties.AsNoTracking()
            .Where(party => party.Id == context.MergedPartyId)
            .Select(party => (long?)party.Version)
            .SingleOrDefaultAsync(cancellationToken);
        return (context.ExpectedMergedVersion, mergedVersion);
    }

    /// <summary>
    /// One event row per affected company, in the merge's transaction. Each row belongs to its receiving company; the
    /// persistence guard admits a row outside the active company only for this event and only beside the merge record
    /// it names.
    /// </summary>
    private void EnqueuePartyMergedEvent(
        Guid companyId, PartyMerge merge, CounterpartyCommandMetadata metadata, DateTimeOffset now)
    {
        var payload = new CounterpartyPartyMergedV1(
            companyId,
            merge.Id,
            merge.SurvivorPartyId,
            merge.MergedPartyId,
            merge.MergedPartyVersion,
            merge.ApprovalCompanyId);
        dbContext.OutboxMessages.Add(new CounterpartyOutboxRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            EventType = CounterpartyPartyMergedOutboxV1.EventType,
            SchemaVersion = CounterpartyPartyMergedOutboxV1.SchemaVersion,
            SourceOperation = CounterpartyPartyMergedOutboxV1.SourceOperation,
            AggregateType = CounterpartyPartyMergedOutboxV1.AggregateType,
            AggregateId = merge.Id,
            AggregateVersion = CounterpartyPartyMergedOutboxV1.Sequence,
            Sequence = CounterpartyPartyMergedOutboxV1.Sequence,
            PayloadJson = JsonSerializer.Serialize(payload, OutboxJson),
            OccurredAtUtc = now,
            CorrelationId = metadata.CorrelationId,
            CausationId = metadata.CausationId,
            RecordedAtUtc = now,
        });
    }

    private static string SerializePartyMergeSnapshot(Party merged, Guid survivorPartyId) =>
        JsonSerializer.Serialize(new
        {
            PartyId = merged.Id,
            SurvivorPartyId = survivorPartyId,
            merged.MergedIntoPartyId,
            merged.IsActive,
            merged.Version,
        });

    private static string SerializePartyMergeSnapshot(
        Party merged, Guid survivorPartyId, PartyMerge merge, IReadOnlyCollection<Guid> affectedCompanyIds) =>
        JsonSerializer.Serialize(new
        {
            PartyId = merged.Id,
            SurvivorPartyId = survivorPartyId,
            merged.MergedIntoPartyId,
            merged.IsActive,
            merged.Version,
            MergeId = merge.Id,
            merge.SurvivorPartyVersion,
            NotifiedCompanyIds = affectedCompanyIds,
        });

    private void LogPartyMergeCommand(
        CounterpartyCommandMetadata metadata, Guid survivorPartyId, Guid mergedPartyId, int affectedCompanyCount,
        CounterpartyCommandExecution result)
    {
        // The reason is not logged; the merge record and the audit event carry it.
        logger.LogInformation(
            "Counterparty party merge command completed. CompanyId={CompanyId} SurvivorPartyId={SurvivorPartyId} MergedPartyId={MergedPartyId} AffectedCompanyCount={AffectedCompanyCount} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            metadata.CompanyId, survivorPartyId, mergedPartyId, affectedCompanyCount, metadata.CorrelationId,
            result.Response.Result, result.IsReplay);
    }

    /// <summary>An EK-18 #76 merge: fenced on the survivor's version (in <c>PartyId</c>) and the duplicate's.</summary>
    private sealed record PartyMergeConcurrencyContext(
        Guid SurvivorPartyId,
        long ExpectedSurvivorVersion,
        Guid MergedPartyId,
        long ExpectedMergedVersion) : ConcurrencyContext(SurvivorPartyId, ExpectedSurvivorVersion);

    private static class PartyMergeResponses
    {
        public static CounterpartyCommandResponse AlreadyMerged() => new(
            CounterpartyCommandResult.PartyAlreadyMerged,
            409,
            "Cari zaten birleştirilmiş.",
            "Bu cari daha önce başka bir cariyle birleştirilmiş; birleştirme geri alınamaz ve tekrarlanamaz.",
            Resource: null,
            Conflict: null);

        public static CounterpartyCommandResponse TargetMerged() => new(
            CounterpartyCommandResult.PartyMergeTargetMerged,
            409,
            "Hedef cari birleştirilmiş.",
            "Başka bir cariyle birleştirilmiş cari birleştirme hedefi olamaz.",
            Resource: null,
            Conflict: null);

        public static CounterpartyCommandResponse TaxIdentityMismatch() => new(
            CounterpartyCommandResult.PartyMergeTaxIdentityMismatch,
            409,
            "Vergi/kimlik numaraları farklı.",
            "Farklı VKN/TCKN taşıyan cariler mükerrer sayılmaz ve birleştirilemez.",
            Resource: null,
            Conflict: null);
    }
}
