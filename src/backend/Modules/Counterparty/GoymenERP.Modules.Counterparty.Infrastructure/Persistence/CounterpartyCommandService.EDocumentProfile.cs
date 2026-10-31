using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// SAT-D102 / SAT-D119 (Sinan, 2026-10-28 and 2026-10-31) — the customer's e-belge profile, shared across the group by
/// VKN/TCKN (<see cref="CustomerEDocumentProfile"/>, no CompanyId). A command names a <c>partyId</c>; the service
/// resolves the active company's account for it (existence/company fence), the party's VKN, and upserts the one
/// VKN-keyed profile — so a check recorded in one company is seen by the others. A party with no VKN cannot have a
/// profile (422 <c>e_document_tax_identity_required</c>, fail-closed).
/// <list type="bullet">
/// <item><b>Status</b>: e-Fatura mükellefi / e-Arşiv as a user checked it, with the server time and checker; Unknown
/// until then (never inferred). A known status always records a new check (version moves); Unknown resets it.</item>
/// <item><b>Scenario override</b>: only a code of the writing company's Settings list in force today; no list → 422
/// <c>einvoice_scenario_code_list_missing</c>; not listed → 422 <c>einvoice_scenario_not_listed</c>; Settings
/// unreadable → 503, nothing stored. Re-sending the stored code is a no-op.</item>
/// </list>
/// Both commands run under <c>counterparty.accounts.e-document.update</c> (plus <c>counterparty.parties.read</c>),
/// measured first and re-measured before the write; one Idempotency-Key (stored 204/404/409/422 replay); the fence is the
/// PAIR the read returned — the opaque profile token (which profile, bound to the party's VKN) and the PROFILE version
/// ("0" creates) — so a party whose VKN changed meanwhile (OPEN-030) is 409 <c>version_conflict</c>, never an ABA
/// overwrite of another taxpayer's record; one audit event per real change, written in the acting company's context.
/// After a VKN change the card reads the NEW VKN's profile and the old VKN's profile stays as it is (not moved, not
/// deleted). The checker's identity is shown only inside the company the check was recorded in. No GİB or other
/// external call.
/// </summary>
internal sealed partial class CounterpartyCommandService : ICounterpartyEDocumentProfileService
{
    private const string RecordEDocumentStatusCommandName = "counterparty.customer-e-document.status.record.v1";
    private const string SetEInvoiceScenarioCommandName = "counterparty.customer-e-document.e-invoice-scenario.set.v1";
    private const string EDocumentStatusRecordedEvent = "counterparty.customer-e-document.status.recorded.v1";
    private const string EInvoiceScenarioChangedEvent = "counterparty.customer-e-document.e-invoice-scenario.changed.v1";
    private const string EDocumentProfileEntityType = "Counterparty.CustomerEDocumentProfile";

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

        var taxIdentityNumber = await ReadPartyTaxIdentityAsync(partyId, cancellationToken);
        var profile = string.IsNullOrEmpty(taxIdentityNumber)
            ? null
            : await FindEDocumentProfileAsync(taxIdentityNumber, tracking: false, cancellationToken);
        var evaluatedOn = CounterpartyEInvoiceScenarioSettings.BusinessDateOf(timeProvider.GetUtcNow());
        var (codes, settingsDefault, effective) = await CounterpartyEInvoiceScenarioSettings.ResolveAsync(
            eInvoiceScenarioPolicy, companyId, profile?.EInvoiceScenarioOverrideCode, evaluatedOn, cancellationToken);

        // P3-1: the check is shared group-wide (SAT-D119), the checker is not: only a reader in the company the check was
        // recorded in sees who checked, as a directory display name (never a raw id); others see only the date.
        bool? checkedInActiveCompany = profile?.EDocumentStatusCheckedInCompanyId is { } checkedIn
            ? checkedIn == companyId
            : null;
        var checkerName = checkedInActiveCompany == true
            ? await ResolveCheckerDisplayNameAsync(companyId, actorId, profile!.EDocumentStatusCheckedByUserId, cancellationToken)
            : null;
        await RequireCustomerGroupReadStillAsync(companyId, actorId, cancellationToken);
        return new CounterpartyAccountEDocumentView(
            account.PartyId,
            account.Id,
            account.CompanyId,
            (profile?.Version ?? 0L).ToString(CultureInfo.InvariantCulture),
            string.IsNullOrEmpty(taxIdentityNumber)
                ? null
                : CounterpartyEDocumentProfileToken.Compute(taxIdentityNumber, profile?.Id),
            account.IsActive,
            !string.IsNullOrEmpty(taxIdentityNumber),
            ToContract(profile?.EDocumentStatus ?? EDocumentStatus.Unknown),
            profile?.EDocumentStatusCheckedAtUtc,
            checkedInActiveCompany,
            checkerName,
            profile?.EInvoiceScenarioOverrideCode,
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
        var expectedVersion = ParseEDocumentExpectedVersion(command.ExpectedVersion);
        var expectedToken = RequireProfileToken(command.ExpectedProfileToken);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            PartyId = partyId, ExpectedVersion = expectedVersion, ExpectedProfileToken = expectedToken,
            Status = status.ToString(),
        });

        return await UpsertEDocumentProfileAsync(
            RecordEDocumentStatusCommandName, partyId, expectedVersion, expectedToken, keyHash, requestHash, metadata,
            beforeSave, scope, EDocumentStatusRecordedEvent,
            ensureSettings: null,
            apply: (profile, now) => profile.RecordStatus(status, metadata.ActorId, metadata.CompanyId, now),
            createWhenAbsentAndNoop: status == EDocumentStatus.Unknown,
            operation: status == EDocumentStatus.Unknown ? "ResetEDocumentStatus" : "RecordEDocumentStatus",
            reason: "Müşteri e-belge durumu kontrolü",
            logOperation: "record-status",
            cancellationToken);
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
        if (scenarioCode is not null && !CustomerEDocumentProfile.IsWellFormedScenarioCode(scenarioCode))
        {
            throw new ArgumentException(
                "e-Fatura senaryo kodu 1-32 karakter, büyük harfle başlayan A-Z, 0-9 veya '_' olmalıdır; kaldırmak için null gönderin.",
                nameof(command));
        }

        var expectedVersion = ParseEDocumentExpectedVersion(command.ExpectedVersion);
        var expectedToken = RequireProfileToken(command.ExpectedProfileToken);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            PartyId = partyId, ExpectedVersion = expectedVersion, ExpectedProfileToken = expectedToken,
            ScenarioCode = scenarioCode,
        });

        // A NEW code must be one the acting company's Settings owner lists today. Re-sending the current code is a
        // no-op; a null clear needs no list; Settings that cannot be read store nothing (503, retry with same key).
        async Task<CounterpartyCommandResult?> EnsureSettings(CustomerEDocumentProfile? profile, CancellationToken token)
        {
            if (scenarioCode is null
                || (profile is not null
                    && string.Equals(profile.EInvoiceScenarioOverrideCode, scenarioCode, StringComparison.Ordinal)))
            {
                return null;
            }

            var today = CounterpartyEInvoiceScenarioSettings.BusinessDateOf(timeProvider.GetUtcNow());
            var codes = await CounterpartyEInvoiceScenarioSettings.ReadCodesAsync(
                eInvoiceScenarioPolicy, metadata.CompanyId, today, token);
            return codes.State switch
            {
                CounterpartySettingsSourceStateV1.NotSet => CounterpartyCommandResult.EInvoiceScenarioCodeListMissing,
                CounterpartySettingsSourceStateV1.Set when !codes.Codes.Contains(scenarioCode, StringComparer.Ordinal) =>
                    CounterpartyCommandResult.EInvoiceScenarioNotListed,
                CounterpartySettingsSourceStateV1.Set => null,
                _ => throw new CounterpartySettingsUnavailableException(),
            };
        }

        return await UpsertEDocumentProfileAsync(
            SetEInvoiceScenarioCommandName, partyId, expectedVersion, expectedToken, keyHash, requestHash, metadata,
            beforeSave, scope, EInvoiceScenarioChangedEvent,
            ensureSettings: EnsureSettings,
            apply: (profile, now) => profile.SetScenarioOverride(scenarioCode, now),
            createWhenAbsentAndNoop: scenarioCode is null,
            operation: scenarioCode is null ? "ClearEInvoiceScenarioOverride" : "SetEInvoiceScenarioOverride",
            reason: "Müşteri e-Fatura senaryosu değişikliği",
            logOperation: "set-scenario",
            cancellationToken);
    }

    /// <summary>
    /// The shared create-or-update for both e-belge commands: resolve the account (existence/company fence) and the
    /// party's VKN, fence on the PAIR (profile token = which profile; profile version, "0" = create), run the
    /// owner-supplied Settings precheck and change, and audit one real change. Group-wide: the profile row carries no
    /// CompanyId. Order of refusals: 404 (no account) → 422 (passive account) → 422 (no VKN) → 409 (another profile
    /// than the one read, e.g. after a VKN change) → 409 (stale version) → 422/503 (Settings).
    /// </summary>
    private async Task<CounterpartyCommandExecution> UpsertEDocumentProfileAsync(
        string commandName,
        Guid partyId,
        long expectedVersion,
        string expectedToken,
        string keyHash,
        string requestHash,
        CounterpartyCommandMetadata metadata,
        Func<CancellationToken, Task> beforeSave,
        PermissionScope scope,
        string eventType,
        Func<CustomerEDocumentProfile?, CancellationToken, Task<CounterpartyCommandResult?>>? ensureSettings,
        Func<CustomerEDocumentProfile, DateTimeOffset, bool> apply,
        bool createWhenAbsentAndNoop,
        string operation,
        string reason,
        string logOperation,
        CancellationToken cancellationToken)
    {
        var accountIsActive = await dbContext.CompanyPartyAccounts.AsNoTracking()
            .Where(candidate => candidate.CompanyId == metadata.CompanyId && candidate.PartyId == partyId)
            .Select(candidate => (bool?)candidate.IsActive)
            .SingleOrDefaultAsync(cancellationToken);
        var accountExists = accountIsActive is not null;
        var taxIdentityNumber = accountExists
            ? await ReadPartyTaxIdentityAsync(partyId, cancellationToken)
            : null;
        var profile = string.IsNullOrEmpty(taxIdentityNumber)
            ? null
            : await FindEDocumentProfileAsync(taxIdentityNumber, tracking: true, cancellationToken);

        var existing = await FindCommandExecutionAsync(commandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, accountExists);
        }

        if (!accountExists)
        {
            return await CompleteCommandAsync(commandName, Responses.NotFound(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        // ASSUMPTION-CARI-EDOC-4: a passive account of the acting company does not change the group-shared profile
        // (review B-P3-4). Reading it stays allowed; reactivate the account first.
        if (accountIsActive is false)
        {
            return await CompleteCommandAsync(commandName,
                EDocumentResponses.Refused(CounterpartyCommandResult.EDocumentAccountInactive),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (string.IsNullOrEmpty(taxIdentityNumber))
        {
            return await CompleteCommandAsync(commandName,
                EDocumentResponses.Refused(CounterpartyCommandResult.EDocumentTaxIdentityRequired),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        // P2-1: the numeric version alone cannot tell X's profile from Y's (ABA after a VKN change, OPEN-030). The token
        // names the profile the client read; another profile — or a profile where "none" was read — is a conflict.
        var currentVersion = profile?.Version ?? 0L;
        var currentToken = CounterpartyEDocumentProfileToken.Compute(taxIdentityNumber, profile?.Id);
        if (!string.Equals(currentToken, expectedToken, StringComparison.Ordinal) || currentVersion != expectedVersion)
        {
            return await CompleteCommandAsync(commandName, Responses.VersionConflict(expectedVersion, currentVersion),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (ensureSettings is not null && await ensureSettings(profile, cancellationToken) is { } refusal)
        {
            return await CompleteCommandAsync(commandName, EDocumentResponses.Refused(refusal),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (profile is null)
        {
            // Nothing to persist for a no-op create (e.g. recording Unknown on an absent profile, or clearing an
            // already-absent override): the absent row IS the value, success without creating it.
            if (createWhenAbsentAndNoop)
            {
                return await CompleteCommandAsync(commandName, Responses.Succeeded(),
                    keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
            }

            // B-P2-2: a concurrent create (possibly from another company) must lose BEFORE the insert. On a duplicate
            // key SQL Server names the duplicate value — the VKN — and EF Core logs that message (Update[10000]) before
            // any catch can run (AT-009: the number never reaches logs). So the create takes a key-range update lock on
            // the VKN inside one transaction: the second creator waits, then sees the winner's row and answers 409.
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var winnerVersion = await ReadEDocumentProfileVersionForCreateAsync(taxIdentityNumber, cancellationToken);
            CounterpartyCommandExecution createdResult;
            Guid? createdId = null;
            if (winnerVersion is { } version)
            {
                // The caller's "0" fence is stale; the conflict names the winner's real version.
                createdResult = await CompleteCommandAsync(commandName, Responses.VersionConflict(expectedVersion, version),
                    keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
            }
            else
            {
                var now = timeProvider.GetUtcNow();
                var created = new CustomerEDocumentProfile(Guid.NewGuid(), taxIdentityNumber, now);
                apply(created, now);
                dbContext.CustomerEDocumentProfiles.Add(created);
                await beforeSave(cancellationToken);
                EnqueueEDocumentAudit(
                    metadata, scope, created, eventType, operation, versionBefore: null, beforeJson: "{}", reason);
                createdResult = await CompleteCommandAsync(commandName, Responses.Succeeded(),
                    keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
                createdId = created.Id;
            }

            await transaction.CommitAsync(cancellationToken);
            LogEDocumentCommand(logOperation, metadata, createdId, createdResult);
            return createdResult;
        }

        var concurrency = new EDocumentProfileConcurrencyContext(profile.Id, expectedVersion);
        var beforeJson = SerializeEDocumentSnapshot(profile, metadata.CompanyId);
        var versionBefore = profile.Version;
        if (!apply(profile, timeProvider.GetUtcNow()))
        {
            // A fenced no-op (Unknown→Unknown, or an unchanged override): no version move, no audit, but the same
            // Version + rowversion predicate still guards it. (A re-recorded known status is a real change — a new
            // check — so apply returns true and it is audited below.)
            dbContext.Entry(profile).Property(value => value.Version).IsModified = true;
            return await CompleteCommandAsync(commandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, concurrency, cancellationToken, beforeSave);
        }

        await beforeSave(cancellationToken);
        EnqueueEDocumentAudit(metadata, scope, profile, eventType, operation, versionBefore, beforeJson, reason);
        var result = await CompleteCommandAsync(commandName, Responses.Succeeded(),
            keyHash, requestHash, metadata, concurrency, cancellationToken, beforeSave);
        LogEDocumentCommand(logOperation, metadata, profile.Id, result);
        return result;
    }

    private static string RequireProfileToken(string? token) =>
        CounterpartyEDocumentProfileToken.IsWellFormed(token)
            ? token!
            : throw new ArgumentException(
                "expectedProfileToken, son okumanın döndürdüğü 64 karakterlik küçük harfli onaltılık değer olmalıdır.",
                nameof(token));

    /// <summary>
    /// The checker's display name from the Identity directory (MOD-08), for a reader of the checking company only.
    /// Any refusal, outage, unknown or non-GUID id is null — the screen then says the name could not be read; it never
    /// falls back to the raw id.
    /// </summary>
    private async Task<string?> ResolveCheckerDisplayNameAsync(
        Guid companyId, string actorId, string? checkerUserId, CancellationToken cancellationToken)
    {
        if (userDirectory is null || !Guid.TryParse(checkerUserId, out var checkerId) || checkerId == Guid.Empty)
        {
            return null;
        }

        try
        {
            var answer = await userDirectory.ResolveAsync(
                new UserDirectoryResolveRequestV1(companyId, actorId, [checkerId]), cancellationToken);
            return answer is { Outcome: UserDirectoryOutcomeV1.Found, Items: { } items }
                ? items.FirstOrDefault(item => item.UserId == checkerId)?.DisplayName
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Task<string?> ReadPartyTaxIdentityAsync(Guid partyId, CancellationToken cancellationToken) =>
        dbContext.Parties.AsNoTracking()
            .Where(party => party.Id == partyId)
            .Select(party => party.TaxIdentityNumber)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The create path's locked probe: <c>UPDLOCK, HOLDLOCK</c> on the unique VKN index holds the key (or the empty key
    /// range) until the surrounding transaction ends, so two creators of the same VKN serialize here instead of meeting
    /// at the unique index. The VKN travels only as a typed parameter.
    /// </summary>
    private async Task<long?> ReadEDocumentProfileVersionForCreateAsync(
        string taxIdentityNumber, CancellationToken cancellationToken)
    {
        var parameter = new SqlParameter("@taxIdentityNumber", SqlDbType.VarChar, 11) { Value = taxIdentityNumber };
        var versions = await dbContext.Database.SqlQueryRaw<long>(
                "SELECT [Version] AS [Value] FROM [counterparty].[CustomerEDocumentProfiles] WITH (UPDLOCK, HOLDLOCK) "
                + "WHERE [TaxIdentityNumber] = @taxIdentityNumber",
                parameter)
            .ToListAsync(cancellationToken);
        return versions.Count == 0 ? null : versions.Single();
    }

    private Task<CustomerEDocumentProfile?> FindEDocumentProfileAsync(
        string taxIdentityNumber, bool tracking, CancellationToken cancellationToken)
    {
        var profiles = tracking ? dbContext.CustomerEDocumentProfiles : dbContext.CustomerEDocumentProfiles.AsNoTracking();
        return profiles.SingleOrDefaultAsync(row => row.TaxIdentityNumber == taxIdentityNumber, cancellationToken);
    }

    internal Task<long?> ReadEDocumentProfileVersionAsync(Guid profileId, CancellationToken cancellationToken) =>
        dbContext.CustomerEDocumentProfiles.AsNoTracking()
            .Where(row => row.Id == profileId)
            .Select(row => (long?)row.Version)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Accepts the canonical "0" (create) or a positive decimal; never a leading zero like "01".</summary>
    private static long ParseEDocumentExpectedVersion(string? value)
    {
        if (value == "0")
        {
            return 0L;
        }

        return CounterpartyContactInformationEditor.ParseVersion(value);
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

    private void EnqueueEDocumentAudit(
        CounterpartyCommandMetadata metadata,
        PermissionScope scope,
        CustomerEDocumentProfile profile,
        string eventType,
        string operation,
        long? versionBefore,
        string beforeJson,
        string reason) =>
        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            eventType,
            EDocumentProfileEntityType,
            profile.Id.ToString("D"),
            operation,
            versionBefore,
            profile.Version,
            AuditActorTypes.User,
            metadata.ActorId,
            CounterpartyEDocumentPermissionsV1.Update,
            scope.RoleCodes,
            beforeJson,
            SerializeEDocumentSnapshot(profile, metadata.CompanyId),
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
        string operation, CounterpartyCommandMetadata metadata, Guid? profileId, CounterpartyCommandExecution result) =>
        logger.LogInformation(
            "Counterparty e-document command completed. Operation={Operation} CompanyId={CompanyId} ProfileId={ProfileId} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            operation, metadata.CompanyId, profileId, metadata.CorrelationId, result.Response.Result, result.IsReplay);

    /// <summary>
    /// The audit Before/After image, written under the ACTING company. The VKN is deliberately NOT in it (AT-009: the
    /// number is kept out of logs and audit); the profile is identified by its surrogate id. The checker's user id and
    /// the checking company are written only when the acting company IS the checking company — another group company's
    /// audit reader learns that a check exists (and when), never who made it or in which company (review B-P2-1).
    /// </summary>
    private static string SerializeEDocumentSnapshot(CustomerEDocumentProfile profile, Guid actingCompanyId)
    {
        var checkedInActingCompany = profile.EDocumentStatusCheckedInCompanyId is { } checkingCompanyId
            && checkingCompanyId == actingCompanyId;
        return JsonSerializer.Serialize(new
        {
            profile.Id,
            EDocumentStatus = profile.EDocumentStatus.ToString(),
            profile.EDocumentStatusCheckedAtUtc,
            EDocumentStatusCheckedInActingCompany = profile.EDocumentStatusCheckedInCompanyId is null
                ? (bool?)null
                : checkedInActingCompany,
            EDocumentStatusCheckedByUserId = checkedInActingCompany ? profile.EDocumentStatusCheckedByUserId : null,
            EDocumentStatusCheckedInCompanyId = checkedInActingCompany ? profile.EDocumentStatusCheckedInCompanyId : null,
            profile.EInvoiceScenarioOverrideCode,
            profile.Version,
        });
    }

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
            CounterpartyCommandResult.EDocumentTaxIdentityRequired => Problem(result,
                "VKN/TCKN gerekli.",
                "Müşterinin e-belge durumu VKN/TCKN'ye bağlıdır; önce cari karta geçerli bir VKN/TCKN girin."),
            CounterpartyCommandResult.EDocumentAccountInactive => Problem(result,
                "Cari hesap pasif.",
                "Pasif cari hesap üzerinden grup genelindeki e-belge profili değiştirilemez; önce hesabı aktifleştirin."),
            _ => throw new InvalidOperationException($"SAT-D102/119 ret sonucu değil: {result}."),
        };

        private static CounterpartyCommandResponse Problem(CounterpartyCommandResult result, string title, string detail) =>
            new(result, 422, title, detail, Resource: null, Conflict: null);
    }
}
