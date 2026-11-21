using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    IReadOnlyList<NormalJournalDraftSourceMappingV1> Mappings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<NormalJournalExactSourceLineV1>? ExactSourceLines = null)
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
            || !IsCanonical(source.OwnerProfileVersion)
            || source.ExactSourceAmount is { } exact && !TryExactAmount(exact, out _)
            || source.ExactSourceLines is { } exactLines && !NormalJournalExactSourceAmountsV1.Valid(exactLines))
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
        if (source.ExactSourceLines?.Any(line => mappings.Count(mapping => mapping.MappingKey == line.MappingKey
                && mapping.Side == line.Side) != 1) == true) return null;

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
        // Keep the existing fingerprint shape for unconstrained manual sources. The two B-28
        // owner sources additionally bind the exact amount, so re-reading a changed amount conflicts.
        var canonicalJson = source.ExactSourceAmount is null
            ? JsonSerializer.Serialize(canonical, Json)
            : JsonSerializer.Serialize(new { source = canonical, exactSourceAmount = source.ExactSourceAmount }, Json);
        if (source.ExactSourceLines is not null)
            canonicalJson = JsonSerializer.Serialize(new { sourceProof = canonicalJson, exactSourceLines = source.ExactSourceLines }, Json);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
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
            mappings.AsReadOnly(), source.ExactSourceLines);
    }

    public static bool IsPreparationVersion(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    public static bool TryExactAmount(string? value, out decimal amount)
    {
        amount = default;
        if (!(value is { Length: > 0 and <= 128 } && value[0] != '.' && value[^1] != '.'
            && value.All(c => char.IsAsciiDigit(c) || c == '.') && value.Count(c => c == '.') <= 1
            && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)
            && amount > 0m)) return false;
        var parts = value.Split('.');
        var integer = parts[0].TrimStart('0');
        if (integer.Length == 0) integer = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : string.Empty;
        var normalized = fraction.Length == 0 ? integer : integer + "." + fraction;
        return normalized == amount.ToString("0.############################", CultureInfo.InvariantCulture);
    }

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
