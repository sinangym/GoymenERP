using System.Globalization;
using System.Text;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

internal static class CounterpartyLinkCandidateQueryParser
{
    private static readonly HashSet<string> KnownParameters = new(
        ["q", "pageSize", "cursor"],
        StringComparer.OrdinalIgnoreCase);

    public static CounterpartyLinkCandidateQueryParseResult Parse(
        IQueryCollection raw,
        out CounterpartyLinkCandidateQuery query,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(raw);
        query = null!;
        error = string.Empty;

        if (raw.Keys.Any(key => !KnownParameters.Contains(key)))
        {
            error = "Bilinmeyen bir cari bağlama adayı parametresi gönderildi.";
            return CounterpartyLinkCandidateQueryParseResult.ValidationFailed;
        }

        if (!TrySingle(raw, "q", out var rawQuery)
            || !TrySingle(raw, "pageSize", out var rawPageSize))
        {
            error = "Cari bağlama adayı parametreleri tek bir değer taşımalıdır.";
            return CounterpartyLinkCandidateQueryParseResult.ValidationFailed;
        }

        if (!TrySingle(raw, "cursor", out var cursor)
            || cursor is not null
            && (string.IsNullOrWhiteSpace(cursor)
                || !string.Equals(cursor, cursor.Trim(), StringComparison.Ordinal)))
        {
            error = "Cursor değeri tekil ve boş olmayan bir değer olmalıdır.";
            return CounterpartyLinkCandidateQueryParseResult.InvalidCursor;
        }

        string? normalizedQuery = null;
        if (rawQuery is not null)
        {
            normalizedQuery = rawQuery.Trim().Normalize(NormalizationForm.FormC);
            if (normalizedQuery.Length == 0)
            {
                normalizedQuery = null;
            }
            else if (normalizedQuery.EnumerateRunes().Count() > 200)
            {
                error = "Arama metni en fazla 200 karakter olabilir.";
                return CounterpartyLinkCandidateQueryParseResult.ValidationFailed;
            }
        }

        if (!TryPageSize(rawPageSize, out var pageSize))
        {
            error = "Sayfa boyutu 1-100 arasında bir tam sayı olmalıdır.";
            return CounterpartyLinkCandidateQueryParseResult.ValidationFailed;
        }

        query = new CounterpartyLinkCandidateQuery(
            normalizedQuery,
            pageSize,
            cursor);
        return CounterpartyLinkCandidateQueryParseResult.Succeeded;
    }

    private static bool TrySingle(
        IQueryCollection raw,
        string name,
        out string? value)
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
            && pageSize is >= 1 and <= 100;
    }
}

internal enum CounterpartyLinkCandidateQueryParseResult
{
    Succeeded,
    ValidationFailed,
    InvalidCursor,
}
