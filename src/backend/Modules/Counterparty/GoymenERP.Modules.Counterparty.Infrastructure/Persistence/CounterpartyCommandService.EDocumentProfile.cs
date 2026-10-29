using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// SAT-D102 (Sinan, 2026-10-28) — the e-belge profile on the Cari card's company account:
/// <list type="bullet">
/// <item><b>Status</b> (<see cref="CompanyPartyAccount.EDocumentStatus"/>): e-Fatura mükellefi / e-Arşiv as a user
/// checked it, with the server time of the check and the checker; <c>Unknown</c> until then (never inferred).</item>
/// <item><b>Scenario override</b> (<see cref="CompanyPartyAccount.EInvoiceScenarioOverrideCode"/>): only a code of the
/// Settings list in force today (Europe/Istanbul business day); with no list in Settings the override cannot be set
/// (422 <c>einvoice_scenario_code_list_missing</c>, "kaynak yok"); an explicit null clears it.</item>
/// </list>
/// Both commands are the Counterparty command shape under <c>counterparty.accounts.e-document.update</c> (plus
/// <c>counterparty.parties.read</c>), measured first and re-measured before the write; one Idempotency-Key (stored
/// 204/404/409/422 answers replay); the ACCOUNT version as decimal text (shared with the other account commands); one
/// audit event per real change and none for a no-op. Nothing here calls GİB or any other external service.
/// </summary>
internal sealed partial class CounterpartyCommandService : ICounterpartyEDocumentProfileService
{
    private const string RecordEDocumentStatusCommandName = "counterparty.company-account.e-document-status.record.v1";
    private const string SetEInvoiceScenarioCommandName = "counterparty.company-account.e-invoice-scenario.set.v1";
    private const string EDocumentStatusRecordedEvent = "counterparty.company-account.e-document-status.recorded.v1";
    private const string EInvoiceScenarioChangedEvent = "counterparty.company-account.e-invoice-scenario.changed.v1";

    public async Task<CounterpartyAccountEDocumentView?> GetAccountEDocumentProfileAsync(
        Guid partyId,
        CancellationToken cancellationToken)
    {
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Cari okuması kimliği doğrulanmış kullanıcı ister.");
        var account = await dbContext.CompanyPartyAccounts.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.CompanyId == companyId && candidate.PartyId == partyId,
                cancellationToken);
        var canUpdate = await HasCompanyWideAsync(CounterpartyEDocumentPermissionsV1.Update, cancellationToken);
        if (account is null)
        {
            await RequireCustomerGroupReadStillAsync(companyId, actorId, cancellationToken);
            return null;
        }

        var evaluatedOn = CounterpartyEInvoiceScenarioSettings.BusinessDateOf(timeProvider.GetUtcNow());
        var (codes, settingsDefault, effective) = await CounterpartyEInvoiceScenarioSettings.ResolveAsync(
            eInvoiceScenarioPolicy, companyId, account.EInvoiceScenarioOverrideCode, evaluatedOn, cancellationToken);
        await RequireCustomerGroupReadStillAsync(companyId, actorId, cancellationToken);
        return new CounterpartyAccountEDocumentView(
            account.PartyId,
            account.Id,
            account.CompanyId,
            account.Version.ToString(CultureInfo.InvariantCulture),
            account.IsActive,
            ToContract(account.EDocumentStatus),
            account.EDocumentStatusCheckedAtUtc,
            account.EDocumentStatusCheckedByUserId,
            account.EInvoiceScenarioOverrideCode,
            evaluatedOn,
            new CounterpartyEInvoiceScenarioCodesView(codes.State, codes.Codes),
            new CounterpartyEInvoiceScenarioDefaultView(settingsDefault.State, settingsDefault.ScenarioCode),
            effective,
            new CounterpartyAccountEDocumentViewer(canUpdate));
    }

    public async Task<CounterpartyCommandExecution> RecordAccountEDocumentStatusAsync(
        Guid partyId,
        RecordCounterpartyEDocumentStatus command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var scope = await RequireCompanyWidePermissionAsync(
            CounterpartyEDocumentPermissionsV1.Update, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => RequireCustomerGroupCommandStillAsync(
            metadata, CounterpartyEDocumentPermissionsV1.Update, token);
        RequirePartyId(partyId);
        var status = ToDomain(command.Status);
        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            PartyId = partyId, ExpectedVersion = expectedVersion, Status = status.ToString(),
        });

        var account = await dbContext.CompanyPartyAccounts.SingleOrDefaultAsync(
            candidate => candidate.CompanyId == metadata.CompanyId && candidate.PartyId == partyId,
            cancellationToken);
        var existing = await FindCommandExecutionAsync(RecordEDocumentStatusCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, account is not null);
        }

        if (account is null)
        {
            return await CompleteCommandAsync(RecordEDocumentStatusCommandName, Responses.NotFound(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (account.Version != expectedVersion)
        {
            return await CompleteCommandAsync(RecordEDocumentStatusCommandName,
                Responses.VersionConflict(expectedVersion, account.Version),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        var concurrency = new AccountConcurrencyContext(partyId, expectedVersion);
        var beforeJson = SerializeAccountEDocumentSnapshot(account);
        var versionBefore = account.Version;
        if (!account.RecordEDocumentStatus(status, metadata.ActorId, timeProvider.GetUtcNow()))
        {
            // Unknown → Unknown: fenced by the same Version + rowversion predicate, without a version move or audit.
            dbContext.Entry(account).Property(value => value.EDocumentStatus).IsModified = true;
            return await CompleteCommandAsync(RecordEDocumentStatusCommandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, concurrency, cancellationToken, beforeSave);
        }

        await beforeSave(cancellationToken);
        EnqueueAccountEDocumentAudit(metadata, scope, account, EDocumentStatusRecordedEvent,
            status == EDocumentStatus.Unknown ? "ResetEDocumentStatus" : "RecordEDocumentStatus",
            versionBefore, beforeJson, "Cari şirket hesabı e-belge durumu kontrolü");
        var result = await CompleteCommandAsync(RecordEDocumentStatusCommandName, Responses.Succeeded(),
            keyHash, requestHash, metadata, concurrency, cancellationToken, beforeSave);
        LogEDocumentCommand("record-status", metadata, account.Id, result);
        return result;
    }

    public async Task<CounterpartyCommandExecution> SetAccountEInvoiceScenarioOverrideAsync(
        Guid partyId,
        SetCounterpartyEInvoiceScenarioOverride command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var scope = await RequireCompanyWidePermissionAsync(
            CounterpartyEDocumentPermissionsV1.Update, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => RequireCustomerGroupCommandStillAsync(
            metadata, CounterpartyEDocumentPermissionsV1.Update, token);
        RequirePartyId(partyId);
        var scenarioCode = command.ScenarioCode;
        if (scenarioCode is not null && !CompanyPartyAccount.IsWellFormedScenarioCode(scenarioCode))
        {
            throw new ArgumentException(
                "e-Fatura senaryo kodu 1-32 karakter, büyük harfle başlayan A-Z, 0-9 veya '_' olmalıdır; kaldırmak için null gönderin.",
                nameof(command));
        }

        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            PartyId = partyId, ExpectedVersion = expectedVersion, ScenarioCode = scenarioCode,
        });

        var account = await dbContext.CompanyPartyAccounts.SingleOrDefaultAsync(
            candidate => candidate.CompanyId == metadata.CompanyId && candidate.PartyId == partyId,
            cancellationToken);
        var existing = await FindCommandExecutionAsync(SetEInvoiceScenarioCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, account is not null);
        }

        if (account is null)
        {
            return await CompleteCommandAsync(SetEInvoiceScenarioCommandName, Responses.NotFound(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (account.Version != expectedVersion)
        {
            return await CompleteCommandAsync(SetEInvoiceScenarioCommandName,
                Responses.VersionConflict(expectedVersion, account.Version),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        // A NEW code must be one the Settings owner lists today. Re-sending the current code is a no-op below and a
        // null clear needs no list. Settings that cannot be read store nothing (503, retry with the same key).
        if (scenarioCode is not null
            && !string.Equals(account.EInvoiceScenarioOverrideCode, scenarioCode, StringComparison.Ordinal))
        {
            var today = CounterpartyEInvoiceScenarioSettings.BusinessDateOf(timeProvider.GetUtcNow());
            var codes = await CounterpartyEInvoiceScenarioSettings.ReadCodesAsync(
                eInvoiceScenarioPolicy, metadata.CompanyId, today, cancellationToken);
            var refusal = codes.State switch
            {
                CounterpartySettingsSourceStateV1.NotSet => CounterpartyCommandResult.EInvoiceScenarioCodeListMissing,
                CounterpartySettingsSourceStateV1.Set when !codes.Codes.Contains(scenarioCode, StringComparer.Ordinal) =>
                    CounterpartyCommandResult.EInvoiceScenarioNotListed,
                CounterpartySettingsSourceStateV1.Set => (CounterpartyCommandResult?)null,
                _ => throw new CounterpartySettingsUnavailableException(),
            };
            if (refusal is not null)
            {
                return await CompleteCommandAsync(SetEInvoiceScenarioCommandName, EDocumentResponses.Refused(refusal.Value),
                    keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
            }
        }

        var concurrency = new AccountConcurrencyContext(partyId, expectedVersion);
        var beforeJson = SerializeAccountEDocumentSnapshot(account);
        var versionBefore = account.Version;
        if (!account.SetEInvoiceScenarioOverride(scenarioCode))
        {
            dbContext.Entry(account).Property(value => value.EInvoiceScenarioOverrideCode).IsModified = true;
            return await CompleteCommandAsync(SetEInvoiceScenarioCommandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, concurrency, cancellationToken, beforeSave);
        }

        await beforeSave(cancellationToken);
        EnqueueAccountEDocumentAudit(metadata, scope, account, EInvoiceScenarioChangedEvent,
            scenarioCode is null ? "ClearEInvoiceScenarioOverride" : "SetEInvoiceScenarioOverride",
            versionBefore, beforeJson, "Cari şirket hesabı e-Fatura senaryosu değişikliği");
        var result = await CompleteCommandAsync(SetEInvoiceScenarioCommandName, Responses.Succeeded(),
            keyHash, requestHash, metadata, concurrency, cancellationToken, beforeSave);
        LogEDocumentCommand("set-scenario", metadata, account.Id, result);
        return result;
    }

    internal static CounterpartyEDocumentStatusV1 ToContract(EDocumentStatus status) => status switch
    {
        EDocumentStatus.Unknown => CounterpartyEDocumentStatusV1.Unknown,
        EDocumentStatus.EInvoiceTaxpayer => CounterpartyEDocumentStatusV1.EInvoice,
        EDocumentStatus.EArchive => CounterpartyEDocumentStatusV1.EArchive,
        _ => throw new InvalidOperationException($"Bilinmeyen e-belge durumu: {status}."),
    };

    private static EDocumentStatus ToDomain(CounterpartyEDocumentStatusV1 status) => status switch
    {
        CounterpartyEDocumentStatusV1.Unknown => EDocumentStatus.Unknown,
        CounterpartyEDocumentStatusV1.EInvoice => EDocumentStatus.EInvoiceTaxpayer,
        CounterpartyEDocumentStatusV1.EArchive => EDocumentStatus.EArchive,
        _ => throw new ArgumentException("e-Belge durumu Unknown, EInvoice veya EArchive olmalıdır.", nameof(status)),
    };

    private void EnqueueAccountEDocumentAudit(
        CounterpartyCommandMetadata metadata,
        PermissionScope scope,
        CompanyPartyAccount account,
        string eventType,
        string operation,
        long versionBefore,
        string beforeJson,
        string reason) =>
        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            eventType,
            "Counterparty.CompanyPartyAccount",
            account.Id.ToString("D"),
            operation,
            versionBefore,
            account.Version,
            AuditActorTypes.User,
            metadata.ActorId,
            CounterpartyEDocumentPermissionsV1.Update,
            scope.RoleCodes,
            beforeJson,
            SerializeAccountEDocumentSnapshot(account),
            reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));

    private void LogEDocumentCommand(
        string operation, CounterpartyCommandMetadata metadata, Guid accountId, CounterpartyCommandExecution result) =>
        logger.LogInformation(
            "Counterparty e-document command completed. Operation={Operation} CompanyId={CompanyId} AccountId={AccountId} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            operation, metadata.CompanyId, accountId, metadata.CorrelationId, result.Response.Result, result.IsReplay);

    private static string SerializeAccountEDocumentSnapshot(CompanyPartyAccount account) =>
        JsonSerializer.Serialize(new
        {
            account.Id,
            account.CompanyId,
            account.PartyId,
            EDocumentStatus = account.EDocumentStatus.ToString(),
            account.EDocumentStatusCheckedAtUtc,
            account.EDocumentStatusCheckedByUserId,
            account.EInvoiceScenarioOverrideCode,
            account.Version,
        });

    private static class EDocumentResponses
    {
        public static CounterpartyCommandResponse Refused(CounterpartyCommandResult result) => result switch
        {
            CounterpartyCommandResult.EInvoiceScenarioCodeListMissing => Problem(result,
                "e-Fatura senaryo listesi yok.",
                "Ayarlar'da bu şirket için geçerli bir e-Fatura senaryo listesi yok; müşteri senaryosu seçilemez."),
            CounterpartyCommandResult.EInvoiceScenarioNotListed => Problem(result,
                "e-Fatura senaryosu listede yok.",
                "Seçilen senaryo kodu Ayarlar'daki geçerli e-Fatura senaryo listesinde yok."),
            _ => throw new InvalidOperationException($"SAT-D102 ret sonucu değil: {result}."),
        };

        private static CounterpartyCommandResponse Problem(CounterpartyCommandResult result, string title, string detail) =>
            new(result, 422, title, detail, Resource: null, Conflict: null);
    }
}
