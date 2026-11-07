using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>
/// B-04a: the server-owned lifecycle of the posting account mapping catalog, the same skeleton as the approved
/// source catalog (<see cref="ApprovedAccountingSourceCatalogServiceV1"/>): narrow company-wide permission,
/// fresh authority before and after every read and inside the store transaction, one ADR-011 command attempt per
/// (command, key) replayed with the original correlation, another request or actor under a key refused, and
/// every refusal recorded so the same key answers it again.
/// <list type="bullet">
/// <item><b>Prepare</b> stores one immutable version payload whose every account is in the ACTIVE approved account
/// plan; the caller names the newest version it saw.</item>
/// <item><b>Activate</b> needs a person other than the preparer (maker-checker, also a database check) and the
/// active version the caller saw; it supersedes that version in the same save. One active version per company is
/// a filtered unique index.</item>
/// </list>
/// No grant is registered here and no account is chosen here.
/// </summary>
public sealed class AccountingPostingAccountMappingServiceV1(
    IAccountingPostingAccountMappingStoreV1 mappings,
    IAccountingCommandLog commands,
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>> PrepareAsync(
        PrepareAccountingPostingAccountMappingRequestV1? request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.ExpectedLatestMappingVersion < 0)
        {
            return Task.FromResult(Invalid(request is null ? "request" : "expectedLatestMappingVersion"));
        }

        if (request.EffectiveFrom == default)
        {
            return Task.FromResult(Invalid("effectiveFrom"));
        }

        var canonical = new
        {
            request.ExpectedLatestMappingVersion,
            effectiveFrom = request.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            rows = request.Rows?.Select(row => row is null
                ? null
                : new
                {
                    row.Purpose,
                    row.MappingKey,
                    row.ItemType,
                    row.LinePurpose,
                    row.IsExport,
                    accountId = row.AccountId.ToString("D"),
                    row.VatRateCode,
                }).ToArray(),
        };

        return RunAsync(
            AccountingPostingAccountMappingPermissionNamesV1.Prepare,
            AccountingPostingAccountMappingCommandNamesV1.Prepare,
            idempotencyKey,
            canonical,
            AccountingApiStatus.Created,
            async (metadata, company, actor, now, token) =>
            {
                var issue = AccountingPostingAccountMappingPayloadV1.Validate(request.Rows, out var rows);
                if (issue is not null)
                {
                    return new AccountingApiResult<AccountingPostingAccountMappingViewV1>(
                        AccountingApiStatus.Unprocessable, null, false, issue.Code, issue.Field);
                }

                var record = await mappings.PrepareAsync(
                    request.ExpectedLatestMappingVersion,
                    request.EffectiveFrom,
                    rows,
                    actor,
                    now,
                    fence => FenceAsync(AccountingPostingAccountMappingPermissionNamesV1.Prepare, company, actor, fence),
                    metadata,
                    token);
                return Success(AccountingApiStatus.Created, record);
            },
            cancellationToken);
    }

    public Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>> ActivateAsync(
        long mappingVersion,
        ActivateAccountingPostingAccountMappingRequestV1? request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (mappingVersion <= 0)
        {
            return Task.FromResult(Invalid("mappingVersion"));
        }

        if (request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid(request is null ? "request" : "expectedVersion"));
        }

        if (request.ExpectedActiveMappingVersion < 0)
        {
            return Task.FromResult(Invalid("expectedActiveMappingVersion"));
        }

        return RunAsync(
            AccountingPostingAccountMappingPermissionNamesV1.Activate,
            AccountingPostingAccountMappingCommandNamesV1.Activate,
            idempotencyKey,
            new { mappingVersion, request.ExpectedVersion, request.ExpectedActiveMappingVersion },
            AccountingApiStatus.Ok,
            async (metadata, company, actor, now, token) =>
            {
                var record = await mappings.ActivateAsync(
                    mappingVersion,
                    request.ExpectedVersion,
                    request.ExpectedActiveMappingVersion,
                    actor,
                    now,
                    fence => FenceAsync(AccountingPostingAccountMappingPermissionNamesV1.Activate, company, actor, fence),
                    metadata,
                    token);
                return Success(AccountingApiStatus.Ok, record);
            },
            cancellationToken);
    }

    public Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>> ReadAsync(
        long mappingVersion,
        CancellationToken cancellationToken = default) =>
        mappingVersion <= 0
            ? Task.FromResult(Invalid("mappingVersion"))
            : GuardedReadAsync(token => mappings.ReadAsync(mappingVersion, token), cancellationToken);

    public Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>> ReadActiveAsync(
        CancellationToken cancellationToken = default) =>
        GuardedReadAsync(mappings.ReadActiveAsync, cancellationToken);

    private async Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>> GuardedReadAsync(
        Func<CancellationToken, Task<AccountingPostingAccountMappingRecordV1?>> read,
        CancellationToken cancellationToken)
    {
        const string permission = AccountingPostingAccountMappingPermissionNamesV1.Read;
        if (!ErpPermissions.IsKnown(permission))
        {
            return Unavailable();
        }

        if (!TryContext(out var company, out var actor)
            || !await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
        {
            return Denied();
        }

        AccountingPostingAccountMappingRecordV1? record;
        try
        {
            record = await read(cancellationToken);
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.CompanyContextMismatch)
        {
            return Denied();
        }

        if (!await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
        {
            return Denied();
        }

        return record is null || record.CompanyId != company
            ? new AccountingApiResult<AccountingPostingAccountMappingViewV1>(
                AccountingApiStatus.NotFound, null, false, null)
            : Success(AccountingApiStatus.Ok, record);
    }

    private async Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>> RunAsync(
        string permission,
        string commandName,
        string idempotencyKey,
        object canonicalRequest,
        AccountingApiStatus successStatus,
        Func<AccountingCommandMetadata, Guid, Guid, DateTimeOffset, CancellationToken,
            Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>>> write,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Invalid("idempotencyKey");
        }

        if (!ErpPermissions.IsKnown(permission))
        {
            // A setup gap, never a grant: the endpoint answers it with the module's 503.
            return Unavailable();
        }

        if (!TryContext(out var company, out var actor)
            || !await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
        {
            return Denied();
        }

        var actorId = actor.ToString("D");
        var keyHash = AccountingApiService.Sha256(idempotencyKey.Trim());
        var requestHash = AccountingApiService.Sha256(
            commandName + "|" + JsonSerializer.Serialize(canonicalRequest, Json));
        var prior = await commands.FindCommandAsync(commandName, keyHash, cancellationToken);
        if (!await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
        {
            return Denied();
        }

        if (prior is not null)
        {
            return Answer(prior, actorId, requestHash);
        }

        var scope = await authority.RevalidatePermissionScopeAsync(permission, company, cancellationToken);
        if (!Granted(scope) || !ContextMatches(company, actor))
        {
            return Denied();
        }

        AccountingApiResult<AccountingPostingAccountMappingViewV1>? completed = null;
        AccountingCommandRecord Complete(object stored)
        {
            completed = stored switch
            {
                AccountingPostingAccountMappingRecordV1 record => Success(successStatus, record),
                _ => throw new InvalidOperationException("Unsupported posting account mapping command result."),
            };
            return Record(commandName, keyHash, actorId, requestHash, completed);
        }

        var metadata = new AccountingCommandMetadata(
            actorId,
            permission,
            [.. scope.RoleCodes.Order(StringComparer.Ordinal)],
            correlation.CorrelationId,
            correlation.CausationId,
            idempotencyKey,
            correlation.SourceChannel,
            correlation.ClientIpAddress,
            correlation.UserAgent,
            Complete);

        AccountingApiResult<AccountingPostingAccountMappingViewV1> result;
        try
        {
            result = await write(metadata, company, actor, timeProvider.GetUtcNow(), cancellationToken);
            if (!await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
            {
                return Denied();
            }

            if (completed is not null)
            {
                return completed;
            }
        }
        catch (AccountingPostingAccountMappingAuthorityDeniedException)
        {
            return Denied();
        }
        catch (AccountingPostingAccountMappingSequenceConflictException exception)
        {
            result = new AccountingApiResult<AccountingPostingAccountMappingViewV1>(
                AccountingApiStatus.Conflict, null, false, AccountingPostingAccountMappingCodesV1.SequenceConflict,
                exception.Field, exception.ExpectedVersion, exception.CurrentVersion);
        }
        catch (AccountingVersionConflictException exception)
        {
            result = new AccountingApiResult<AccountingPostingAccountMappingViewV1>(
                AccountingApiStatus.Conflict, null, false, AccountingApiCodes.VersionConflict,
                "expectedVersion", exception.ExpectedVersion, exception.CurrentVersion);
        }
        catch (AccountingPostingAccountMappingRefusalException exception)
        {
            result = exception.Code switch
            {
                AccountingPostingAccountMappingCodesV1.NotFound =>
                    new AccountingApiResult<AccountingPostingAccountMappingViewV1>(
                        AccountingApiStatus.NotFound, null, false, null),
                // Maker-checker: the preparer is refused whatever grants they hold (the Accounting 403 shape of
                // Ö4 and the reversal approval), and the refusal is recorded under the key.
                AccountingPostingAccountMappingCodesV1.SelfActivation =>
                    new AccountingApiResult<AccountingPostingAccountMappingViewV1>(
                        AccountingApiStatus.Denied, null, false, exception.Code, exception.Field),
                AccountingPostingAccountMappingCodesV1.AccountUnknown =>
                    new AccountingApiResult<AccountingPostingAccountMappingViewV1>(
                        AccountingApiStatus.Unprocessable, null, false, exception.Code, exception.Field),
                _ => new AccountingApiResult<AccountingPostingAccountMappingViewV1>(
                    AccountingApiStatus.Conflict, null, false, exception.Code, exception.Field),
            };
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
        {
            var winner = await commands.FindCommandAsync(commandName, keyHash, cancellationToken);
            if (!await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
            {
                return Denied();
            }

            return winner is not null
                ? Answer(winner, actorId, requestHash)
                : new AccountingApiResult<AccountingPostingAccountMappingViewV1>(
                    AccountingApiStatus.Conflict, null, false, exception.Code);
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.CompanyContextMismatch)
        {
            return Denied();
        }
        catch (AccountingStoreException)
        {
            // An unknown store answer is not a business refusal: nothing is recorded and the caller may retry.
            return Unavailable();
        }

        if (!await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
        {
            return Denied();
        }

        try
        {
            await commands.RecordCommandAsync(Record(commandName, keyHash, actorId, requestHash, result),
                cancellationToken);
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
        {
            var winner = await commands.FindCommandAsync(commandName, keyHash, cancellationToken);
            if (!await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
            {
                return Denied();
            }

            return winner is not null ? Answer(winner, actorId, requestHash) : result;
        }

        if (!await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
        {
            return Denied();
        }

        return result;
    }

    private async Task FenceAsync(string permission, Guid company, Guid actor, CancellationToken cancellationToken)
    {
        if (!await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
        {
            throw new AccountingPostingAccountMappingAuthorityDeniedException();
        }
    }

    private async Task<bool> AuthorizeCapturedAsync(
        string permission,
        Guid company,
        Guid actor,
        CancellationToken cancellationToken)
    {
        if (!ContextMatches(company, actor))
        {
            return false;
        }

        var scope = await authority.RevalidatePermissionScopeAsync(permission, company, cancellationToken);
        return Granted(scope) && ContextMatches(company, actor);
    }

    private static bool Granted(PermissionScope scope) => scope.IsGranted && scope.IsCompanyWide;

    private bool TryContext(out Guid company, out Guid actor)
    {
        company = companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty;
        actor = Guid.Empty;
        return companyContext.IsResolved && company != Guid.Empty && currentUser.IsAuthenticated
            && Guid.TryParse(currentUser.UserId, out actor) && actor != Guid.Empty
            && currentUser.CanAccessCompany(company) && correlation.IsResolved;
    }

    private bool ContextMatches(Guid company, Guid actor) =>
        TryContext(out var currentCompany, out var currentActor)
        && currentCompany == company && currentActor == actor;

    private static AccountingApiResult<AccountingPostingAccountMappingViewV1> Success(
        AccountingApiStatus status,
        AccountingPostingAccountMappingRecordV1 record) =>
        new(status, View(record), false, null);

    public static AccountingPostingAccountMappingViewV1 View(AccountingPostingAccountMappingRecordV1 record) =>
        new(
            record.CompanyId,
            record.MappingVersion,
            record.EffectiveFrom,
            record.Status.ToString(),
            record.Version,
            record.AccountPlanSourceVersion,
            record.AccountPlanSourceSha256,
            record.PreparedByUserId,
            record.PreparedAtUtc,
            record.ActivatedByUserId,
            record.ActivatedAtUtc,
            [.. record.Rows.OrderBy(row => row.LineNumber).Select(row => new AccountingPostingAccountMappingRowViewV1(
                row.Purpose, row.MappingKey, row.ItemType, row.LinePurpose, row.IsExport, row.AccountId,
                row.AccountCode, row.VatRateCode))]);

    private static AccountingApiResult<AccountingPostingAccountMappingViewV1> Invalid(string field) =>
        new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, field);

    private static AccountingApiResult<AccountingPostingAccountMappingViewV1> Denied() =>
        new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);

    private static AccountingApiResult<AccountingPostingAccountMappingViewV1> Unavailable() =>
        new(AccountingApiStatus.Unavailable, null, false, null);

    private AccountingCommandRecord Record(
        string commandName,
        string keyHash,
        string actorId,
        string requestHash,
        AccountingApiResult<AccountingPostingAccountMappingViewV1>? result) =>
        new(
            commandName,
            keyHash,
            actorId,
            requestHash,
            JsonSerializer.Serialize((result ?? throw new InvalidOperationException()) with
            {
                IsReplay = false,
                OriginalCorrelationId = null,
            }, Json),
            correlation.CorrelationId);

    private static AccountingApiResult<AccountingPostingAccountMappingViewV1> Answer(
        AccountingCommandRecord prior,
        string actorId,
        string requestHash)
    {
        if (!string.Equals(prior.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new AccountingApiResult<AccountingPostingAccountMappingViewV1>(
                AccountingApiStatus.Conflict, null, false, AccountingApiCodes.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<AccountingApiResult<AccountingPostingAccountMappingViewV1>>(
            prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Stored posting account mapping result could not be read.");
        return stored with { IsReplay = true, OriginalCorrelationId = prior.OriginalCorrelationId };
    }
}
