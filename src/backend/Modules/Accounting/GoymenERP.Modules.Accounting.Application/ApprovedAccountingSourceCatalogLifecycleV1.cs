using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Accounting.Application;

public enum ApprovedAccountingSourceCatalogStatusV1
{
    Prepared = 1,
    Active = 2,
    Superseded = 3,
}

public static class ApprovedAccountingSourceCatalogCommandNamesV1
{
    public const string Prepare = "accounting.approved-source-catalog.prepare.v1";
    public const string Activate = "accounting.approved-source-catalog.activate.v1";
}

/// <summary>Technical names only. Central registration grants neither permission to any role.</summary>
public static class ApprovedAccountingSourceCatalogPermissionNamesV1
{
    public const string Prepare = "accounting.approved-source-catalog.prepare";
    public const string Activate = "accounting.approved-source-catalog.activate";
}

public static class ApprovedAccountingSourceCatalogCodesV1
{
    public const string ContentConflict = "accounting_source_catalog_content_conflict";
    public const string DigestMismatch = "accounting_source_catalog_digest_mismatch";
    public const string NotFound = "accounting_source_catalog_not_found";
    public const string NotPrepared = "accounting_source_catalog_not_prepared";
    public const string ActiveVersionConflict = "accounting_source_catalog_active_version_conflict";
}

public sealed record PrepareApprovedAccountingSourceCatalogRequestV1(
    Guid PackageId,
    string AccountPlanSourceVersion,
    ReadOnlyMemory<byte> AccountsUtf8Csv,
    ReadOnlyMemory<byte> PostingMappingsUtf8Csv,
    string ExpectedAccountsSourceSha256,
    string ExpectedPostingMappingsSourceSha256);

public sealed record ActivateApprovedAccountingSourceCatalogRequestV1(
    long ExpectedPackageVersion,
    long ExpectedActiveCatalogVersion);

public sealed record ApprovedAccountingSourceCatalogRecordV1(
    Guid PackageId,
    Guid CompanyId,
    ApprovedAccountingSourceCatalogStatusV1 Status,
    long Version,
    long? CatalogVersion,
    string TemplateVersion,
    string AccountPlanSourceVersion,
    string AccountsSourceSha256,
    string PostingMappingsSourceSha256,
    Guid PreparedByUserId,
    DateTimeOffset PreparedAtUtc,
    Guid? ActivatedByUserId,
    DateTimeOffset? ActivatedAtUtc,
    ApprovedAccountingSourcePackageV1 Package);

public sealed record ApprovedAccountingSourceCatalogViewV1(
    Guid PackageId,
    Guid CompanyId,
    string Status,
    long Version,
    long? CatalogVersion,
    string TemplateVersion,
    string AccountPlanSourceVersion,
    string AccountsSourceSha256,
    string PostingMappingsSourceSha256,
    Guid PreparedByUserId,
    DateTimeOffset PreparedAtUtc,
    Guid? ActivatedByUserId,
    DateTimeOffset? ActivatedAtUtc,
    int AccountCount,
    int BindingCount);

public interface IApprovedAccountingSourceCatalogStoreV1 : IApprovedNormalJournalSourceBindingCatalogV1
{
    Task<ApprovedAccountingSourceCatalogRecordV1?> ReadAsync(
        Guid packageId,
        CancellationToken cancellationToken = default);

    Task<(ApprovedAccountingSourceCatalogRecordV1 Record, AccountingWriteOutcome Outcome)> PrepareAsync(
        Guid packageId,
        ApprovedAccountingSourcePackagePreviewV1 preview,
        Guid preparedByUserId,
        DateTimeOffset preparedAtUtc,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<ApprovedAccountingSourceCatalogRecordV1> ActivateAsync(
        Guid packageId,
        long expectedPackageVersion,
        long expectedActiveCatalogVersion,
        Guid activatedByUserId,
        DateTimeOffset activatedAtUtc,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

public sealed class ApprovedAccountingSourceCatalogVersionConflictException(
    long expectedVersion,
    long currentVersion)
    : Exception("The active Accounting source catalog changed since it was read.")
{
    public long ExpectedVersion { get; } = expectedVersion;
    public long CurrentVersion { get; } = currentVersion;
}

public sealed class ApprovedAccountingSourceCatalogAuthorityDeniedException()
    : Exception("The approved Accounting source catalog authority is no longer valid.");

/// <summary>
/// Server-owned lifecycle for a parsed package. Preparation persists immutable parsed facts; activation
/// requires the separate catalog authority and deliberately permits the same actor when that authority is
/// granted (Sinan #66/5946629677). It registers no grant and does not alter voucher approval rules.
/// </summary>
public sealed class ApprovedAccountingSourceCatalogServiceV1(
    IApprovedAccountingSourcePackageParserV1 parser,
    IApprovedAccountingSourceCatalogStoreV1 catalogs,
    IAccountingCommandLog commands,
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>> PrepareAsync(
        PrepareApprovedAccountingSourceCatalogRequestV1? request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.PackageId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.AccountPlanSourceVersion)
            || request.AccountsUtf8Csv.IsEmpty || request.PostingMappingsUtf8Csv.IsEmpty
            || !Sha(request.ExpectedAccountsSourceSha256)
            || !Sha(request.ExpectedPostingMappingsSourceSha256))
        {
            return Task.FromResult(Invalid("request"));
        }

        var accountsDigest = Convert.ToHexString(SHA256.HashData(request.AccountsUtf8Csv.Span));
        var mappingsDigest = Convert.ToHexString(SHA256.HashData(request.PostingMappingsUtf8Csv.Span));
        var canonical = new
        {
            request.PackageId,
            accountPlanSourceVersion = request.AccountPlanSourceVersion.Trim(),
            accountsDigest,
            mappingsDigest,
            expectedAccountsDigest = request.ExpectedAccountsSourceSha256.ToUpperInvariant(),
            expectedMappingsDigest = request.ExpectedPostingMappingsSourceSha256.ToUpperInvariant(),
        };

        return RunAsync(
            ApprovedAccountingSourceCatalogPermissionNamesV1.Prepare,
            ApprovedAccountingSourceCatalogCommandNamesV1.Prepare,
            idempotencyKey,
            canonical,
            AccountingApiStatus.Created,
            async (metadata, actor, now, token) =>
            {
                if (!string.Equals(accountsDigest, request.ExpectedAccountsSourceSha256, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(mappingsDigest, request.ExpectedPostingMappingsSourceSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(
                        AccountingApiStatus.Conflict, null, false,
                        ApprovedAccountingSourceCatalogCodesV1.DigestMismatch);
                }

                var preview = parser.Preview(
                    new TrustedAccountingSourceApprovalContextV1(companyContext.CompanyId, actor, now),
                    new ApprovedAccountingSourcePackagePreviewRequestV1(
                        request.AccountPlanSourceVersion.Trim(),
                        request.AccountsUtf8Csv,
                        request.PostingMappingsUtf8Csv));
                if (!preview.IsValid)
                {
                    var issue = preview.Issues.FirstOrDefault();
                    return new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(
                        AccountingApiStatus.Unprocessable, null, false,
                        issue?.Code ?? ApprovedAccountingSourcePackageCodesV1.RowShapeInvalid,
                        issue?.Field);
                }

                var (record, _) = await catalogs.PrepareAsync(
                    request.PackageId,
                    preview,
                    actor,
                    now,
                    fence => FenceAsync(ApprovedAccountingSourceCatalogPermissionNamesV1.Prepare, actor, fence),
                    metadata,
                    token);
                return Success(AccountingApiStatus.Created, record);
            },
            cancellationToken);
    }

    public Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>> ActivateAsync(
        Guid packageId,
        ActivateApprovedAccountingSourceCatalogRequestV1? request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (packageId == Guid.Empty || request is null || request.ExpectedPackageVersion <= 0
            || request.ExpectedActiveCatalogVersion < 0)
        {
            return Task.FromResult(Invalid("request"));
        }

        return RunAsync(
            ApprovedAccountingSourceCatalogPermissionNamesV1.Activate,
            ApprovedAccountingSourceCatalogCommandNamesV1.Activate,
            idempotencyKey,
            new { packageId, request.ExpectedPackageVersion, request.ExpectedActiveCatalogVersion },
            AccountingApiStatus.Ok,
            async (metadata, actor, now, token) =>
            {
                var record = await catalogs.ActivateAsync(
                    packageId,
                    request.ExpectedPackageVersion,
                    request.ExpectedActiveCatalogVersion,
                    actor,
                    now,
                    fence => FenceAsync(ApprovedAccountingSourceCatalogPermissionNamesV1.Activate, actor, fence),
                    metadata,
                    token);
                return Success(AccountingApiStatus.Ok, record);
            },
            cancellationToken);
    }

    public async Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>> ReadAsync(
        Guid packageId,
        CancellationToken cancellationToken = default)
    {
        if (packageId == Guid.Empty)
        {
            return Invalid("packageId");
        }

        if (!TryContext(out var company, out var actor)
            || !await GrantedAsync(ApprovedAccountingSourceCatalogPermissionNamesV1.Prepare, company,
                cancellationToken))
        {
            return Denied();
        }

        var record = await catalogs.ReadAsync(packageId, cancellationToken);
        return record is not null && TryContext(out var currentCompany, out var currentActor)
            && currentCompany == company && currentActor == actor
            && await GrantedAsync(ApprovedAccountingSourceCatalogPermissionNamesV1.Prepare, company,
                cancellationToken)
                ? Success(AccountingApiStatus.Ok, record)
                : record is null
                    ? new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(
                        AccountingApiStatus.NotFound, null, false, null)
                    : Denied();
    }

    private async Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>> RunAsync(
        string permission,
        string commandName,
        string idempotencyKey,
        object canonicalRequest,
        AccountingApiStatus successStatus,
        Func<AccountingCommandMetadata, Guid, DateTimeOffset, CancellationToken,
            Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>>> write,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || !TryContext(out var company, out var actor)
            || !await GrantedAsync(permission, company, cancellationToken))
        {
            return string.IsNullOrWhiteSpace(idempotencyKey) ? Invalid("idempotencyKey") : Denied();
        }

        var actorId = actor.ToString("D");
        var keyHash = AccountingApiService.Sha256(idempotencyKey.Trim());
        var requestHash = AccountingApiService.Sha256(
            commandName + "|" + JsonSerializer.Serialize(canonicalRequest, Json));
        if (await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } prior)
        {
            return Answer(prior, actorId, requestHash);
        }

        var scope = await authority.RevalidatePermissionScopeAsync(permission, company, cancellationToken);
        if (!Granted(scope) || !TryContext(out var currentCompany, out var currentActor)
            || currentCompany != company || currentActor != actor)
        {
            return Denied();
        }

        AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>? completed = null;
        AccountingCommandRecord Complete(object stored)
        {
            completed = stored switch
            {
                ApprovedAccountingSourceCatalogRecordV1 record => Success(successStatus, record),
                AccountingApiResult<ApprovedAccountingSourceCatalogViewV1> result => result,
                _ => throw new InvalidOperationException("Unsupported Accounting catalog command result."),
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

        AccountingApiResult<ApprovedAccountingSourceCatalogViewV1> result;
        try
        {
            result = await write(metadata, actor, timeProvider.GetUtcNow(), cancellationToken);
            if (completed is not null)
            {
                return completed;
            }
        }
        catch (ApprovedAccountingSourceCatalogAuthorityDeniedException)
        {
            return Denied();
        }
        catch (ApprovedAccountingSourceCatalogVersionConflictException exception)
        {
            result = new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(
                AccountingApiStatus.Conflict, null, false,
                ApprovedAccountingSourceCatalogCodesV1.ActiveVersionConflict,
                "expectedActiveCatalogVersion", exception.ExpectedVersion, exception.CurrentVersion);
        }
        catch (AccountingVersionConflictException exception)
        {
            result = new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(
                AccountingApiStatus.Conflict, null, false, AccountingApiCodes.VersionConflict,
                "expectedPackageVersion", exception.ExpectedVersion, exception.CurrentVersion);
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
        {
            return await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } winner
                ? Answer(winner, actorId, requestHash)
                : new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(
                    AccountingApiStatus.Conflict, null, false, exception.Code);
        }
        catch (AccountingStoreException exception)
        {
            result = exception.Code switch
            {
                ApprovedAccountingSourceCatalogCodesV1.NotFound =>
                    new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(
                        AccountingApiStatus.NotFound, null, false, null),
                ApprovedAccountingSourceCatalogCodesV1.ContentConflict
                    or ApprovedAccountingSourceCatalogCodesV1.NotPrepared =>
                    new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(
                        AccountingApiStatus.Conflict, null, false, exception.Code),
                _ => new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(
                    AccountingApiStatus.Unprocessable, null, false, exception.Code),
            };
        }

        try
        {
            await commands.RecordCommandAsync(Record(commandName, keyHash, actorId, requestHash, result),
                cancellationToken);
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
        {
            return await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } winner
                ? Answer(winner, actorId, requestHash)
                : result;
        }

        return result;
    }

    private async Task FenceAsync(string permission, Guid actor, CancellationToken cancellationToken)
    {
        var company = companyContext.CompanyId;
        if (!await GrantedAsync(permission, company, cancellationToken)
            || !TryContext(out var currentCompany, out var currentActor)
            || currentCompany != company || currentActor != actor)
        {
            throw new ApprovedAccountingSourceCatalogAuthorityDeniedException();
        }
    }

    private async Task<bool> GrantedAsync(string permission, Guid company, CancellationToken cancellationToken) =>
        Granted(await authority.RevalidatePermissionScopeAsync(permission, company, cancellationToken));

    private static bool Granted(PermissionScope scope) => scope.IsGranted && scope.IsCompanyWide;

    private bool TryContext(out Guid company, out Guid actor)
    {
        company = companyContext.CompanyId;
        actor = Guid.Empty;
        return companyContext.IsResolved && company != Guid.Empty && currentUser.IsAuthenticated
            && Guid.TryParse(currentUser.UserId, out actor) && actor != Guid.Empty
            && currentUser.CanAccessCompany(company) && correlation.IsResolved;
    }

    private static AccountingApiResult<ApprovedAccountingSourceCatalogViewV1> Success(
        AccountingApiStatus status,
        ApprovedAccountingSourceCatalogRecordV1 record) =>
        new(status, View(record), false, null);

    private static ApprovedAccountingSourceCatalogViewV1 View(ApprovedAccountingSourceCatalogRecordV1 record) =>
        new(
            record.PackageId,
            record.CompanyId,
            record.Status.ToString(),
            record.Version,
            record.CatalogVersion,
            record.TemplateVersion,
            record.AccountPlanSourceVersion,
            record.AccountsSourceSha256,
            record.PostingMappingsSourceSha256,
            record.PreparedByUserId,
            record.PreparedAtUtc,
            record.ActivatedByUserId,
            record.ActivatedAtUtc,
            record.Package.Mirror.Rows.Count,
            record.Package.Bindings.Count);

    private static AccountingApiResult<ApprovedAccountingSourceCatalogViewV1> Invalid(string field) =>
        new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, field);

    private static AccountingApiResult<ApprovedAccountingSourceCatalogViewV1> Denied() =>
        new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);

    private static bool Sha(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private AccountingCommandRecord Record(
        string commandName,
        string keyHash,
        string actorId,
        string requestHash,
        AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>? result) =>
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

    private static AccountingApiResult<ApprovedAccountingSourceCatalogViewV1> Answer(
        AccountingCommandRecord prior,
        string actorId,
        string requestHash)
    {
        if (!string.Equals(prior.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(
                AccountingApiStatus.Conflict, null, false, AccountingApiCodes.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>>(
            prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Stored Accounting source catalog result could not be read.");
        return stored with { IsReplay = true, OriginalCorrelationId = prior.OriginalCorrelationId };
    }
}
