using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed partial class CounterpartyCommandService
{
    private const string UpdateContactInformationCommandName =
        "counterparty.company-account.contact-information.update.v1";

    public async Task<CounterpartyContactInformationView?> GetContactInformationAsync(
        Guid partyId, CancellationToken cancellationToken)
    {
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(
            currentUser, companyContext);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        var account = await dbContext.CompanyPartyAccounts.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.CompanyId == authorityFence.CompanyId && candidate.PartyId == partyId,
                cancellationToken);
        await RequireContactInformationReadStillAsync(authorityFence, cancellationToken);
        return account is null ? null : new(
            account.PartyId, account.Id, account.CompanyId,
            account.Version.ToString(CultureInfo.InvariantCulture), account.IsActive,
            CounterpartyContactInformationEditor.Load(account.ContactInformationJson));
    }

    public async Task<CounterpartyCommandExecution> UpdateContactInformationAsync(
        Guid partyId, UpdateCounterpartyContactInformation command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Changes);
        ValidateCommandMetadata(metadata);
        var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(
            currentUser, companyContext);
        if (authorityFence.CompanyId != metadata.CompanyId
            || !string.Equals(authorityFence.ActorId, metadata.ActorId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Cari iletişim bilgisi command bağlamı değişti.");
        }
        var scope = await RequireCompanyWidePermissionAsync(
            ErpPermissions.CounterpartyPartiesUpdateBasic, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeWrite = token =>
            RequireContactInformationCommandStillAsync(authorityFence, token);
        if (partyId == Guid.Empty)
        {
            throw new ArgumentException("Cari kimliği boş olamaz.", nameof(partyId));
        }

        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { PartyId = partyId, Command = command });

        // Re-check the current relation before exposing any persisted result.
        var account = await dbContext.CompanyPartyAccounts.SingleOrDefaultAsync(
            candidate => candidate.CompanyId == metadata.CompanyId && candidate.PartyId == partyId,
            cancellationToken);
        await beforeWrite(cancellationToken);
        var existing = await FindCommandExecutionAsync(
            UpdateContactInformationCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeWrite(cancellationToken);
            var execution = ResolveExistingExecution(existing, requestHash, metadata);
            return account is null && execution.Response.Result == CounterpartyCommandResult.Succeeded
                ? new(Responses.NotFound(), false, metadata.CorrelationId)
                : execution;
        }

        if (account is null)
        {
            return await CompleteCommandAsync(UpdateContactInformationCommandName, Responses.NotFound(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeWrite);
        }

        if (account.Version != expectedVersion)
        {
            return await CompleteCommandAsync(UpdateContactInformationCommandName,
                Responses.VersionConflict(expectedVersion, account.Version),
                keyHash, requestHash, metadata, null, cancellationToken, beforeWrite);
        }

        var before = CounterpartyContactInformationEditor.Load(account.ContactInformationJson);
        var after = CounterpartyContactInformationEditor.Apply(before, command.Changes);
        var beforeJson = CounterpartyContactInformationEditor.Serialize(before);
        var afterJson = CounterpartyContactInformationEditor.Serialize(after);
        var beforeVersion = account.Version;
        if (beforeJson == afterJson)
        {
            // Guard the no-op with the same Version + SQL rowversion predicate without
            // advancing the logical business version or generating an audit event.
            dbContext.Entry(account).Property(value => value.ContactInformationJson).IsModified = true;
            return await CompleteCommandAsync(UpdateContactInformationCommandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, new AccountConcurrencyContext(partyId, expectedVersion),
                cancellationToken, beforeWrite);
        }

        await beforeWrite(cancellationToken);
        account.UpdateContactInformation(afterJson);

        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            "counterparty.company-account.contact-information.changed.v1",
            "Counterparty.CompanyPartyAccount", account.Id.ToString("D"),
            "UpdateContactInformation",
            beforeVersion, account.Version,
            AuditActorTypes.User, metadata.ActorId,
            ErpPermissions.CounterpartyPartiesUpdateBasic, scope.RoleCodes,
            beforeJson, afterJson,
            Reason: "Cari şirket hesabı iletişim bilgisi güncelleme",
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
            metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));

        var result = await CompleteCommandAsync(UpdateContactInformationCommandName, Responses.Succeeded(),
            keyHash, requestHash, metadata, new AccountConcurrencyContext(partyId, expectedVersion),
            cancellationToken, beforeWrite);
        logger.LogInformation(
            "Counterparty contact information command completed. CompanyId={CompanyId} AccountId={AccountId} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            metadata.CompanyId, account.Id, metadata.CorrelationId, result.Response.Result, result.IsReplay);
        return result;
    }

    private Task RequireContactInformationReadStillAsync(
        CounterpartyContactInformationAuthorityFence authorityFence,
        CancellationToken cancellationToken) =>
        authorityFence.RequireStillAsync(
            currentUser,
            companyContext,
            authorityRevalidationService,
            [ErpPermissions.CounterpartyPartiesRead],
            cancellationToken);

    private Task RequireContactInformationCommandStillAsync(
        CounterpartyContactInformationAuthorityFence authorityFence,
        CancellationToken cancellationToken) =>
        authorityFence.RequireStillAsync(
            currentUser,
            companyContext,
            authorityRevalidationService,
            [
                ErpPermissions.CounterpartyPartiesUpdateBasic,
                ErpPermissions.CounterpartyPartiesRead,
            ],
            cancellationToken);
}
