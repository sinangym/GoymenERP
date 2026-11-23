using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// YOL-D31 (WP14) group-company link edit, in the trade-name command shape: company-wide
/// <c>counterparty.parties.group-company-link.update</c> and <c>counterparty.parties.read</c> measured first and re-validated
/// before the single save, one Idempotency-Key (stored terminal answers replay), the PARTY version as the fence, the company's
/// account as the existence fence (404), an unknown or inactive target company refused (400), one audit event per real change.
/// A save of the stored value is a guarded no-op. The persistence guard refuses any other Party column in the same save.
/// </summary>
internal sealed partial class CounterpartyCommandService : ICounterpartyGroupCompanyLinkService
{
    private const string UpdateGroupCompanyLinkCommandName = "counterparty.party.group-company-link.update.v1";

    public async Task<CounterpartyGroupCompanyLinkView?> GetGroupCompanyLinkAsync(Guid partyId, CancellationToken cancellationToken)
    {
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        var party = await FindFencedPartyAsync(authorityFence.CompanyId, partyId, tracking: false, cancellationToken);
        var canUpdate = await HasCompanyWideAsync(ErpPermissions.CounterpartyPartiesGroupCompanyLinkUpdate, cancellationToken);
        await authorityFence.RequireStillAsync(currentUser, companyContext, authorityRevalidationService,
            [ErpPermissions.CounterpartyPartiesRead], cancellationToken);
        return party is null ? null : new CounterpartyGroupCompanyLinkView(party.Id, authorityFence.CompanyId, party.Code.Value,
            party.Version.ToString(CultureInfo.InvariantCulture), party.LegalName, party.GroupCompanyId,
            new CounterpartyGroupCompanyLinkViewer(canUpdate));
    }

    public async Task<CounterpartyCommandExecution> UpdateGroupCompanyLinkAsync(Guid partyId, UpdateCounterpartyGroupCompanyLink command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        if (authorityFence.CompanyId != metadata.CompanyId
            || !string.Equals(authorityFence.ActorId, metadata.ActorId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Cari grup şirketi command bağlamı değişti.");
        }

        var scope = await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesGroupCompanyLinkUpdate, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => authorityFence.RequireStillAsync(currentUser, companyContext,
            authorityRevalidationService, [ErpPermissions.CounterpartyPartiesGroupCompanyLinkUpdate, ErpPermissions.CounterpartyPartiesRead], token);
        RequirePartyId(partyId);
        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        if (command.GroupCompanyId == Guid.Empty)
            throw new ArgumentException("groupCompanyId: Grup şirketi kimliği boş olamaz.", nameof(command));
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { PartyId = partyId, ExpectedVersion = expectedVersion, command.GroupCompanyId });

        var party = await FindFencedPartyAsync(metadata.CompanyId, partyId, tracking: true, cancellationToken);
        var existing = await FindCommandExecutionAsync(UpdateGroupCompanyLinkCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, party is not null);
        }

        if (party is null)
        {
            return await CompleteCommandAsync(UpdateGroupCompanyLinkCommandName, Responses.NotFound(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (party.Version != expectedVersion)
        {
            return await CompleteCommandAsync(UpdateGroupCompanyLinkCommandName, Responses.VersionConflict(expectedVersion, party.Version),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (command.GroupCompanyId is { } target && !await IsKnownGroupCompanyAsync(target, cancellationToken))
            throw new ArgumentException("groupCompanyId: Seçilen şirket grupta etkin bir şirket değil.", nameof(command));

        var beforeVersion = party.Version;
        var before = party.GroupCompanyId;
        if (!party.ChangeGroupCompany(command.GroupCompanyId, timeProvider.GetUtcNow()))
        {
            dbContext.Entry(party).Property(value => value.GroupCompanyId).IsModified = true;
            return await CompleteCommandAsync(UpdateGroupCompanyLinkCommandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, new PartyConcurrencyContext(partyId, expectedVersion), cancellationToken, beforeSave);
        }

        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId, "counterparty.party.group-company-link.changed.v1", "Counterparty.Party", party.Id.ToString("D"),
            "UpdateGroupCompanyLink", beforeVersion, party.Version, AuditActorTypes.User, metadata.ActorId,
            ErpPermissions.CounterpartyPartiesGroupCompanyLinkUpdate, scope.RoleCodes,
            JsonSerializer.Serialize(new { GroupCompanyId = before, Version = beforeVersion }),
            JsonSerializer.Serialize(new { party.GroupCompanyId, party.Version }),
            Reason: "Cari grup şirketi bağlantısı", ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
            metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));

        return await CompleteCommandAsync(UpdateGroupCompanyLinkCommandName, Responses.Succeeded(),
            keyHash, requestHash, metadata, new PartyConcurrencyContext(partyId, expectedVersion), cancellationToken, beforeSave);
    }

    /// <summary>The target is a group company only when Organization answers it as an active company; no answer refuses.</summary>
    private async Task<bool> IsKnownGroupCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var lifecycle = companyLifecycle ?? throw new InvalidOperationException("Şirket dizini bağlı değil; grup şirketi doğrulanamıyor.");
        var view = await lifecycle.ReadAsync(companyId, cancellationToken);
        return view is { IsActive: true } && view.CompanyId == companyId;
    }
}

/// <summary>YOL-D31: every linked party (group-wide), for the WP14 intercompany report. No authority; creates nothing.</summary>
internal sealed class CounterpartyGroupCompanyLinkReadPortV1(CounterpartyDbContext dbContext) : ICounterpartyGroupCompanyLinkReadPortV1
{
    public async Task<IReadOnlyList<CounterpartyGroupCompanyLinkV1>> ListLinksAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Parties.AsNoTracking()
            .Where(party => party.GroupCompanyId != null)
            .OrderBy(party => party.Id)
            .Select(party => new CounterpartyGroupCompanyLinkV1(party.Id, party.GroupCompanyId!.Value))
            .ToArrayAsync(cancellationToken);
}
