using System.Globalization;
using System.Text;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Api.Endpoints;

internal static class CounterpartyPageQueryParser
{
    private static readonly HashSet<string> KnownParameters = new(
        [
            "q",
            "kind",
            "role",
            "roleMatch",
            "partyState",
            "accountState",
            "sort",
            "direction",
            "pageSize",
            "cursor",
            "includeCount",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static CounterpartyPageQueryParseResult Parse(
        IQueryCollection raw,
        out CounterpartyPageQuery query,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(raw);
        query = null!;
        error = string.Empty;

        var unknown = raw.Keys.FirstOrDefault(key => !KnownParameters.Contains(key));
        if (unknown is not null)
        {
            error = "Bilinmeyen bir Cari listeleme parametresi gönderildi.";
            return CounterpartyPageQueryParseResult.ValidationFailed;
        }

        if (!TrySingle(raw, "q", out var rawQuery)
            || !TrySingle(raw, "kind", out var rawKind)
            || !TrySingle(raw, "roleMatch", out var rawRoleMatch)
            || !TrySingle(raw, "partyState", out var rawPartyState)
            || !TrySingle(raw, "accountState", out var rawAccountState)
            || !TrySingle(raw, "sort", out var rawSort)
            || !TrySingle(raw, "direction", out var rawDirection)
            || !TrySingle(raw, "pageSize", out var rawPageSize)
            || !TrySingle(raw, "includeCount", out var rawIncludeCount))
        {
            error = "Tekil Cari listeleme parametreleri birden fazla değer taşıyamaz.";
            return CounterpartyPageQueryParseResult.ValidationFailed;
        }

        if (!TrySingle(raw, "cursor", out var cursor))
        {
            error = "Cursor değeri tekil olmalıdır.";
            return CounterpartyPageQueryParseResult.InvalidCursor;
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
                return CounterpartyPageQueryParseResult.ValidationFailed;
            }
        }

        if (!TryKind(rawKind, out var kind)
            || !TryRoles(raw, out var roles)
            || !TryRoleMatch(rawRoleMatch, out var roleMatch)
            || !TryState(rawPartyState, CounterpartyRecordState.Active, out var partyState)
            || !TryState(rawAccountState, CounterpartyRecordState.All, out var accountState)
            || !TrySort(rawSort, out var sort)
            || !TryDirection(rawDirection, out var direction)
            || !TryPageSize(rawPageSize, out var pageSize)
            || !TryBoolean(rawIncludeCount, out var includeCount))
        {
            error = "Cari listeleme filtresi, sıralaması veya sayfa boyutu geçersiz.";
            return CounterpartyPageQueryParseResult.ValidationFailed;
        }

        query = new CounterpartyPageQuery(
            normalizedQuery,
            kind,
            roles,
            roleMatch,
            partyState,
            accountState,
            sort,
            direction,
            pageSize,
            cursor,
            includeCount);
        return CounterpartyPageQueryParseResult.Succeeded;
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

    private static bool TryKind(string? raw, out PartyKind? kind)
    {
        kind = raw switch
        {
            null => null,
            "LegalEntity" => PartyKind.LegalEntity,
            "NaturalPerson" => PartyKind.NaturalPerson,
            _ => null,
        };
        return raw is null || kind is not null;
    }

    private static bool TryRoles(IQueryCollection raw, out PartyRole roles)
    {
        roles = PartyRole.None;
        if (!raw.TryGetValue("role", out var values))
        {
            return true;
        }

        foreach (var value in values)
        {
            var role = value switch
            {
                "Customer" => PartyRole.Customer,
                "Supplier" => PartyRole.Supplier,
                "Tolling" => PartyRole.Tolling,
                "Service" => PartyRole.Service,
                _ => PartyRole.None,
            };
            if (role == PartyRole.None || value!.Contains(',', StringComparison.Ordinal))
            {
                roles = PartyRole.None;
                return false;
            }

            roles |= role;
        }

        return values.Count > 0;
    }

    private static bool TryRoleMatch(
        string? raw,
        out CounterpartyRoleMatch roleMatch)
    {
        roleMatch = raw switch
        {
            null or "any" => CounterpartyRoleMatch.Any,
            "all" => CounterpartyRoleMatch.All,
            _ => default,
        };
        return roleMatch != default;
    }

    private static bool TryState(
        string? raw,
        CounterpartyRecordState defaultValue,
        out CounterpartyRecordState state)
    {
        state = raw switch
        {
            null => defaultValue,
            "active" => CounterpartyRecordState.Active,
            "inactive" => CounterpartyRecordState.Inactive,
            "all" => CounterpartyRecordState.All,
            _ => default,
        };
        return state != default;
    }

    private static bool TrySort(string? raw, out CounterpartySort sort)
    {
        sort = raw switch
        {
            null or "code" => CounterpartySort.Code,
            "legalName" => CounterpartySort.LegalName,
            "updatedAt" => CounterpartySort.UpdatedAt,
            _ => default,
        };
        return sort != default;
    }

    private static bool TryDirection(
        string? raw,
        out CounterpartySortDirection direction)
    {
        direction = raw switch
        {
            null or "asc" => CounterpartySortDirection.Ascending,
            "desc" => CounterpartySortDirection.Descending,
            _ => default,
        };
        return direction != default;
    }

    private static bool TryPageSize(string? raw, out int pageSize)
    {
        if (raw is null)
        {
            pageSize = 50;
            return true;
        }

        return int.TryParse(
                raw,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out pageSize)
            && pageSize is >= 1 and <= 200;
    }

    private static bool TryBoolean(string? raw, out bool value)
    {
        switch (raw)
        {
            case null or "false":
                value = false;
                return true;
            case "true":
                value = true;
                return true;
            default:
                value = false;
                return false;
        }
    }
}

internal enum CounterpartyPageQueryParseResult
{
    Succeeded,
    ValidationFailed,
    InvalidCursor,
}
