using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// ALS-D95 (confirmed by SINAN-CEVAP 19.22) official legal name change: one step, no approval step, under the separate
/// company-wide <c>counterparty.parties.legal-name.update</c> permission plus <c>counterparty.parties.read</c>, both
/// measured first and re-validated before the single save. One Idempotency-Key (stored terminal answers replay), the
/// PARTY version as the fence, the company's account as the existence fence (no account in the active company = 404),
/// and one audit event per real change whose before/after snapshots carry the old and the new legal name. A save of the
/// stored value is a guarded no-op: the same Version + rowversion predicate runs, the logical version does not move and
/// no audit event is written. Nothing but the legal name changes here; the persistence guard refuses any other Party
/// column (trade name, tax identity, MERSİS) in the same save.
/// </summary>
internal sealed partial class CounterpartyCommandService : ICounterpartyLegalNameService
{
    private const string UpdateLegalNameCommandName = "counterparty.party.legal-name.update.v1";

    public async Task<CounterpartyLegalNameView?> GetLegalNameAsync(
        Guid partyId, CancellationToken cancellationToken)
    {
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        var party = await FindFencedPartyAsync(authorityFence.CompanyId, partyId, tracking: false, cancellationToken);
        var canUpdate = await HasCompanyWideAsync(
            ErpPermissions.CounterpartyPartiesLegalNameUpdate, cancellationToken);
        await authorityFence.RequireStillAsync(
            currentUser, companyContext, authorityRevalidationService,
            [ErpPermissions.CounterpartyPartiesRead], cancellationToken);
        return party is null ? null : new CounterpartyLegalNameView(
            party.Id,
            authorityFence.CompanyId,
            party.Code.Value,
            party.Version.ToString(CultureInfo.InvariantCulture),
            party.LegalName,
            new CounterpartyLegalNameViewer(canUpdate));
    }

    public async Task<CounterpartyCommandExecution> UpdateLegalNameAsync(
        Guid partyId, UpdateCounterpartyLegalName command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        if (authorityFence.CompanyId != metadata.CompanyId
            || !string.Equals(authorityFence.ActorId, metadata.ActorId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Cari resmi unvan command bağlamı değişti.");
        }

        var scope = await RequireCompanyWidePermissionAsync(
            ErpPermissions.CounterpartyPartiesLegalNameUpdate, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => authorityFence.RequireStillAsync(
            currentUser, companyContext, authorityRevalidationService,
            [ErpPermissions.CounterpartyPartiesLegalNameUpdate, ErpPermissions.CounterpartyPartiesRead], token);
        RequirePartyId(partyId);
        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var legalName = NormalizeLegalNameInput(command.LegalName);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            PartyId = partyId, ExpectedVersion = expectedVersion, LegalName = legalName,
        });

        var party = await FindFencedPartyAsync(metadata.CompanyId, partyId, tracking: true, cancellationToken);
        var existing = await FindCommandExecutionAsync(UpdateLegalNameCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, party is not null);
        }

        if (party is null)
        {
            return await CompleteCommandAsync(UpdateLegalNameCommandName, Responses.NotFound(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (party.Version != expectedVersion)
        {
            return await CompleteCommandAsync(UpdateLegalNameCommandName,
                Responses.VersionConflict(expectedVersion, party.Version),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        var beforeVersion = party.Version;
        var beforeLegalName = party.LegalName;
        if (!party.ChangeLegalName(legalName, DateTimeOffset.UtcNow))
        {
            // Guard the no-op with the same Version + rowversion predicate without advancing the logical business
            // version or writing an audit event.
            dbContext.Entry(party).Property(value => value.LegalName).IsModified = true;
            var unchanged = await CompleteCommandAsync(UpdateLegalNameCommandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, new PartyConcurrencyContext(partyId, expectedVersion),
                cancellationToken, beforeSave);
            LogLegalNameCommand(metadata, partyId, changed: false, unchanged);
            return unchanged;
        }

        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            "counterparty.party.legal-name.changed.v1",
            "Counterparty.Party", party.Id.ToString("D"),
            "UpdateLegalName",
            beforeVersion, party.Version,
            AuditActorTypes.User, metadata.ActorId,
            ErpPermissions.CounterpartyPartiesLegalNameUpdate, scope.RoleCodes,
            SerializeLegalNameSnapshot(beforeLegalName, beforeVersion),
            SerializeLegalNameSnapshot(party.LegalName, party.Version),
            Reason: "Cari resmi unvan güncelleme",
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
            metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));

        var result = await CompleteCommandAsync(UpdateLegalNameCommandName, Responses.Succeeded(),
            keyHash, requestHash, metadata, new PartyConcurrencyContext(partyId, expectedVersion),
            cancellationToken, beforeSave);
        LogLegalNameCommand(metadata, partyId, changed: true, result);
        return result;
    }

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
                $"legalName: Resmi unvan {Party.LegalNameMaximumLength} karakterden uzun olamaz.",
                nameof(legalName));
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("legalName: Resmi unvan boş olamaz.", nameof(legalName));
        }
    }

    private static string SerializeLegalNameSnapshot(string legalName, long version) =>
        JsonSerializer.Serialize(new { LegalName = legalName, Version = version });

    private void LogLegalNameCommand(
        CounterpartyCommandMetadata metadata, Guid partyId, bool changed, CounterpartyCommandExecution result)
    {
        // The name itself is not logged; the audit event carries the before/after.
        logger.LogInformation(
            "Counterparty legal-name command completed. CompanyId={CompanyId} PartyId={PartyId} CorrelationId={CorrelationId} Result={Result} Changed={Changed} IsReplay={IsReplay}",
            metadata.CompanyId, partyId, metadata.CorrelationId, result.Response.Result, changed, result.IsReplay);
    }
}
