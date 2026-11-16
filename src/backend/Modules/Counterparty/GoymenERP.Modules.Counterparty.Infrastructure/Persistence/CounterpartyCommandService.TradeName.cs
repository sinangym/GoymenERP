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
/// F-027 / C-02 trade name edit. The Counterparty command shape: company-wide <c>counterparty.parties.update.basic</c>
/// and <c>counterparty.parties.read</c> measured first and re-validated before the single save, one Idempotency-Key
/// (stored terminal answers replay), the PARTY version as the fence, the company's account as the existence fence (no
/// account in the active company = 404), and one audit event per real change. A save of the stored value is a guarded
/// no-op: the same Version + rowversion predicate runs, the logical version does not move and no audit event is written.
/// The legal name is never touched here (ALS-D111/D113: its own maker-checker commands and permissions); the persistence guard refuses any other Party
/// column in the same save.
/// </summary>
internal sealed partial class CounterpartyCommandService : ICounterpartyTradeNameService
{
    private const string UpdateTradeNameCommandName = "counterparty.party.trade-name.update.v1";

    public async Task<CounterpartyTradeNameView?> GetTradeNameAsync(
        Guid partyId, CancellationToken cancellationToken)
    {
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        var party = await FindFencedPartyAsync(authorityFence.CompanyId, partyId, tracking: false, cancellationToken);
        var canUpdate = await HasCompanyWideAsync(ErpPermissions.CounterpartyPartiesUpdateBasic, cancellationToken);
        await authorityFence.RequireStillAsync(
            currentUser, companyContext, authorityRevalidationService,
            [ErpPermissions.CounterpartyPartiesRead], cancellationToken);
        return party is null ? null : new CounterpartyTradeNameView(
            party.Id,
            authorityFence.CompanyId,
            party.Code.Value,
            party.Version.ToString(CultureInfo.InvariantCulture),
            party.LegalName,
            party.TradeName,
            new CounterpartyTradeNameViewer(canUpdate));
    }

    public async Task<CounterpartyCommandExecution> UpdateTradeNameAsync(
        Guid partyId, UpdateCounterpartyTradeName command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        if (authorityFence.CompanyId != metadata.CompanyId
            || !string.Equals(authorityFence.ActorId, metadata.ActorId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Cari ticari ad command bağlamı değişti.");
        }

        var scope = await RequireCompanyWidePermissionAsync(
            ErpPermissions.CounterpartyPartiesUpdateBasic, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => authorityFence.RequireStillAsync(
            currentUser, companyContext, authorityRevalidationService,
            [ErpPermissions.CounterpartyPartiesUpdateBasic, ErpPermissions.CounterpartyPartiesRead], token);
        RequirePartyId(partyId);
        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var tradeName = NormalizeTradeNameInput(command.TradeName);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            PartyId = partyId, ExpectedVersion = expectedVersion, TradeName = tradeName,
        });

        var party = await FindFencedPartyAsync(metadata.CompanyId, partyId, tracking: true, cancellationToken);
        var existing = await FindCommandExecutionAsync(UpdateTradeNameCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, party is not null);
        }

        if (party is null)
        {
            return await CompleteCommandAsync(UpdateTradeNameCommandName, Responses.NotFound(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (party.Version != expectedVersion)
        {
            return await CompleteCommandAsync(UpdateTradeNameCommandName,
                Responses.VersionConflict(expectedVersion, party.Version),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        var beforeVersion = party.Version;
        var beforeTradeName = party.TradeName;
        if (!party.ChangeTradeName(tradeName, DateTimeOffset.UtcNow))
        {
            // Guard the no-op with the same Version + rowversion predicate without advancing the logical business
            // version or writing an audit event.
            dbContext.Entry(party).Property(value => value.TradeName).IsModified = true;
            var unchanged = await CompleteCommandAsync(UpdateTradeNameCommandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, new PartyConcurrencyContext(partyId, expectedVersion),
                cancellationToken, beforeSave);
            LogTradeNameCommand(metadata, partyId, changed: false, unchanged);
            return unchanged;
        }

        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            "counterparty.party.trade-name.changed.v1",
            "Counterparty.Party", party.Id.ToString("D"),
            "UpdateTradeName",
            beforeVersion, party.Version,
            AuditActorTypes.User, metadata.ActorId,
            ErpPermissions.CounterpartyPartiesUpdateBasic, scope.RoleCodes,
            SerializeTradeNameSnapshot(beforeTradeName, beforeVersion),
            SerializeTradeNameSnapshot(party.TradeName, party.Version),
            Reason: "Cari ticari ad güncelleme",
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
            metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));

        var result = await CompleteCommandAsync(UpdateTradeNameCommandName, Responses.Succeeded(),
            keyHash, requestHash, metadata, new PartyConcurrencyContext(partyId, expectedVersion),
            cancellationToken, beforeSave);
        LogTradeNameCommand(metadata, partyId, changed: true, result);
        return result;
    }

    /// <summary>The submitted trade name as the party would store it; too long is a 400, never truncated.</summary>
    private static string? NormalizeTradeNameInput(string? tradeName)
    {
        try
        {
            return Party.NormalizeTradeName(tradeName);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentException(
                $"tradeName: Ticari ad {Party.TradeNameMaximumLength} karakterden uzun olamaz.",
                nameof(tradeName));
        }
    }

    private static string SerializeTradeNameSnapshot(string? tradeName, long version) =>
        JsonSerializer.Serialize(new { TradeName = tradeName, Version = version });

    private void LogTradeNameCommand(
        CounterpartyCommandMetadata metadata, Guid partyId, bool changed, CounterpartyCommandExecution result)
    {
        // The name itself is not logged; the audit event carries the before/after.
        logger.LogInformation(
            "Counterparty trade-name command completed. CompanyId={CompanyId} PartyId={PartyId} CorrelationId={CorrelationId} Result={Result} Changed={Changed} IsReplay={IsReplay}",
            metadata.CompanyId, partyId, metadata.CorrelationId, result.Response.Result, changed, result.IsReplay);
    }
}
