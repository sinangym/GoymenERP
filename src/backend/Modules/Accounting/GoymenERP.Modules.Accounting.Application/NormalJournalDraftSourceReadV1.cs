using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Accounting.Application;

public sealed record NormalJournalDraftSourceMappingV1(
    string MappingKey,
    string Side,
    Guid AccountId,
    string AccountCode,
    string AccountName);

public sealed record NormalJournalDraftSourcePreparationV1(
    Guid CompanyId,
    Guid SourceId,
    string PreparationVersion,
    string SourceVersion,
    string OwnerProfileId,
    string OwnerProfileVersion,
    Guid CurrencyId,
    string PostingPurpose,
    string PostingRuleVersion,
    string AccountPlanSourceVersion,
    string AccountPlanSourceSha256,
    IReadOnlyList<NormalJournalDraftSourceMappingV1> Mappings)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static NormalJournalDraftSourcePreparationV1? TryCreate(
        Guid companyId,
        Guid sourceId,
        NormalJournalDraftServerSourceV1? source)
    {
        if (companyId == Guid.Empty || sourceId == Guid.Empty || source is null
            || source.CurrencyId == Guid.Empty || source.Rule.Mirror.CompanyId != companyId
            || !IsCanonical(source.SourceVersion) || !IsCanonical(source.OwnerProfileId)
            || !IsCanonical(source.OwnerProfileVersion))
        {
            return null;
        }

        var mappings = new List<NormalJournalDraftSourceMappingV1>(source.Rule.Mappings.Count);
        foreach (var mapping in source.Rule.Mappings)
        {
            var row = source.Rule.Mirror.Lookup(mapping.Account);
            if (row is null)
            {
                return null;
            }

            mappings.Add(new NormalJournalDraftSourceMappingV1(
                mapping.MappingKey,
                mapping.Side.ToString(),
                mapping.Account.AccountId,
                row.Code,
                row.Name));
        }

        mappings = [.. mappings
            .OrderBy(mapping => mapping.MappingKey, StringComparer.Ordinal)
            .ThenBy(mapping => mapping.Side, StringComparer.Ordinal)
            .ThenBy(mapping => mapping.AccountId)];
        if (mappings.Count < 2 || mappings
            .GroupBy(mapping => (mapping.Side, mapping.AccountId))
            .Any(group => group.Count() != 1))
        {
            return null;
        }

        var canonical = new
        {
            companyId,
            sourceId,
            sourceVersion = source.SourceVersion,
            ownerProfileId = source.OwnerProfileId,
            ownerProfileVersion = source.OwnerProfileVersion,
            source.CurrencyId,
            mirror = new
            {
                source.Rule.Mirror.SourceVersion,
                sha256 = source.Rule.Mirror.SourceSha256.ToUpperInvariant(),
                source.Rule.Mirror.ApprovedByUserId,
                approvedAtUtc = Utc(source.Rule.Mirror.ApprovedAtUtc),
            },
            rule = new
            {
                source.Rule.PostingPurpose,
                source.Rule.Version,
                source.Rule.ApprovedByUserId,
                approvedAtUtc = Utc(source.Rule.ApprovedAtUtc),
            },
            mappings,
        };
        var fingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, Json))));
        return new NormalJournalDraftSourcePreparationV1(
            companyId,
            sourceId,
            fingerprint,
            source.SourceVersion!,
            source.OwnerProfileId!,
            source.OwnerProfileVersion!,
            source.CurrencyId,
            source.Rule.PostingPurpose,
            source.Rule.Version,
            source.Rule.Mirror.SourceVersion,
            source.Rule.Mirror.SourceSha256.ToUpperInvariant(),
            mappings.AsReadOnly());
    }

    public static bool IsPreparationVersion(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("O");
}

public sealed class NormalJournalDraftSourceReadServiceV1(
    INormalJournalDraftServerSourceProviderV1 sources,
    NormalJournalPermissionBindingV1 permissions,
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser)
{
    public async Task<AccountingApiResult<NormalJournalDraftSourcePreparationV1>> ReadAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        if (sourceId == Guid.Empty)
        {
            return new AccountingApiResult<NormalJournalDraftSourcePreparationV1>(
                AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, "sourceId");
        }

        if (!TryContext(out var company, out var actor)
            || !await HasAuthorityAsync(company, actor, cancellationToken))
        {
            return Denied();
        }

        var source = await sources.ResolveAsync(company, sourceId, cancellationToken);
        var preparation = NormalJournalDraftSourcePreparationV1.TryCreate(company, sourceId, source);
        if (preparation is null)
        {
            return new AccountingApiResult<NormalJournalDraftSourcePreparationV1>(
                AccountingApiStatus.Unavailable, null, false, null);
        }

        return await HasAuthorityAsync(company, actor, cancellationToken)
            ? new AccountingApiResult<NormalJournalDraftSourcePreparationV1>(
                AccountingApiStatus.Ok, preparation, false, null)
            : Denied();
    }

    private bool TryContext(out Guid company, out Guid actor)
    {
        company = companyContext.CompanyId;
        actor = Guid.Empty;
        return companyContext.IsResolved && company != Guid.Empty && currentUser.IsAuthenticated
            && Guid.TryParse(currentUser.UserId, out actor) && actor != Guid.Empty
            && currentUser.CanAccessCompany(company);
    }

    private async Task<bool> HasAuthorityAsync(Guid company, Guid actor, CancellationToken cancellationToken)
    {
        var scope = await authority.RevalidatePermissionScopeAsync(
            permissions.CreatePermission, company, cancellationToken);
        return scope.IsGranted && scope.IsCompanyWide
            && TryContext(out var currentCompany, out var currentActor)
            && currentCompany == company && currentActor == actor;
    }

    private static AccountingApiResult<NormalJournalDraftSourcePreparationV1> Denied() =>
        new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);
}
