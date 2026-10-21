using System.Globalization;
using System.Text;

namespace GoymenERP.Api.OrganizationRuntime;

public static class OrganizationLocationOperatorReadCodesV1
{
    public const string ValidationFailed = "validation_failed";
    public const string InvalidCursor = "invalid_cursor";
}

public sealed record OrganizationLocationOperatorContextV1(
    Guid LocationId,
    OrganizationLocationOperatorDisplayV1? Operator);

public sealed record OrganizationLocationOperatorDisplayV1(
    Guid PartyId,
    string Code,
    string LegalName,
    string? TradeName,
    bool IsCurrentlyEligible);

public sealed record OrganizationLocationOperatorCandidatePageV1(
    IReadOnlyList<OrganizationLocationOperatorCandidateV1> Items,
    string? NextCursor,
    bool HasMore);

public sealed record OrganizationLocationOperatorCandidateV1(
    Guid PartyId,
    string Code,
    string LegalName,
    string? TradeName);

internal sealed record OrganizationLocationOperatorCandidateQueryV1(
    string? Q,
    int PageSize,
    string? Cursor);

internal enum OrganizationLocationOperatorReadOutcomeV1
{
    Success = 1,
    NotFound = 2,
    InvalidCursor = 3,
    Unavailable = 4,
}

internal sealed record OrganizationLocationOperatorReadResultV1<T>(
    OrganizationLocationOperatorReadOutcomeV1 Outcome,
    T? Resource);

internal interface IOrganizationLocationOperatorReadHostV1
{
    Task<OrganizationLocationOperatorReadResultV1<OrganizationLocationOperatorContextV1>>
        ReadDisplayAsync(Guid locationId, CancellationToken cancellationToken = default);

    Task<OrganizationLocationOperatorReadResultV1<OrganizationLocationOperatorCandidatePageV1>>
        SearchCandidatesAsync(
            Guid locationId,
            OrganizationLocationOperatorCandidateQueryV1 query,
            string sessionBinding,
            CancellationToken cancellationToken = default);
}

internal static class OrganizationLocationOperatorCandidateQueryParserV1
{
    private static readonly HashSet<string> KnownParameters = new(
        ["q", "pageSize", "cursor"],
        StringComparer.OrdinalIgnoreCase);

    public static OrganizationLocationOperatorCandidateQueryParseOutcomeV1 Parse(
        IQueryCollection raw,
        out OrganizationLocationOperatorCandidateQueryV1 query,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(raw);
        query = null!;
        error = string.Empty;

        if (raw.Keys.Any(key => !KnownParameters.Contains(key)))
        {
            error = "Bilinmeyen bir lokasyon operatörü adayı parametresi gönderildi.";
            return OrganizationLocationOperatorCandidateQueryParseOutcomeV1.ValidationFailed;
        }

        if (!TrySingle(raw, "q", out var rawQuery)
            || !TrySingle(raw, "pageSize", out var rawPageSize))
        {
            error = "Lokasyon operatörü adayı parametreleri tek bir değer taşımalıdır.";
            return OrganizationLocationOperatorCandidateQueryParseOutcomeV1.ValidationFailed;
        }

        if (!TrySingle(raw, "cursor", out var cursor)
            || cursor is not null
            && (string.IsNullOrWhiteSpace(cursor)
                || !string.Equals(cursor, cursor.Trim(), StringComparison.Ordinal)
                || cursor.Length > 8192))
        {
            error = "Cursor değeri tekil, boş olmayan ve geçerli uzunlukta olmalıdır.";
            return OrganizationLocationOperatorCandidateQueryParseOutcomeV1.InvalidCursor;
        }

        var normalizedQuery = rawQuery?.Trim().Normalize(NormalizationForm.FormC);
        if (normalizedQuery?.Length == 0)
        {
            normalizedQuery = null;
        }
        else if (normalizedQuery is not null && normalizedQuery.EnumerateRunes().Count() > 200)
        {
            error = "Arama metni en fazla 200 karakter olabilir.";
            return OrganizationLocationOperatorCandidateQueryParseOutcomeV1.ValidationFailed;
        }

        if (!TryPageSize(rawPageSize, out var pageSize))
        {
            error = "Sayfa boyutu 1-25 arasında bir tam sayı olmalıdır.";
            return OrganizationLocationOperatorCandidateQueryParseOutcomeV1.ValidationFailed;
        }

        query = new OrganizationLocationOperatorCandidateQueryV1(
            normalizedQuery,
            pageSize,
            cursor);
        return OrganizationLocationOperatorCandidateQueryParseOutcomeV1.Success;
    }

    private static bool TrySingle(IQueryCollection raw, string name, out string? value)
    {
        value = null;
        if (!raw.TryGetValue(name, out var values))
        {
            return true;
        }

        if (values.Count != 1 || values[0] is null)
        {
            return false;
        }

        value = values[0];
        return true;
    }

    private static bool TryPageSize(string? raw, out int pageSize)
    {
        if (raw is null)
        {
            pageSize = 25;
            return true;
        }

        return int.TryParse(
                raw,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out pageSize)
            && pageSize is >= 1 and <= 25;
    }
}

internal enum OrganizationLocationOperatorCandidateQueryParseOutcomeV1
{
    Success = 1,
    ValidationFailed = 2,
    InvalidCursor = 3,
}
