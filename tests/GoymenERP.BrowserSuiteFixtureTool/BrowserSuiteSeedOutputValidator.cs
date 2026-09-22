namespace GoymenERP.BrowserSuiteFixtureTool;

internal static class BrowserSuiteSeedOutputValidator
{
    public static void Validate(
        string suite,
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlySet<string> actors)
    {
        foreach (var (key, value) in outputs)
        {
            if (!IsSafeKey(key))
            {
                throw new InvalidOperationException($"Seed output key '{key}' is not a safe key.");
            }

            var isGuid = Guid.TryParseExact(value, "D", out var guid)
                         && guid != Guid.Empty
                         && string.Equals(value, guid.ToString("D"), StringComparison.Ordinal);
            if (!isGuid && !actors.Contains(value) && !IsSuiteSpecificScalar(suite, key, value))
            {
                throw new InvalidOperationException(
                    $"Seed output '{key}' must be a lowercase GUID, an actor id of this run, or an exact suite scalar.");
            }
        }
    }

    private static bool IsSafeKey(string key)
    {
        if (key.Length is < 1 or > 40 || key[0] is < 'a' or > 'z')
        {
            return false;
        }

        for (var index = 1; index < key.Length; index++)
        {
            var character = key[index];
            if (character is not (>= 'a' and <= 'z')
                && character is not (>= 'A' and <= 'Z')
                && character is not (>= '0' and <= '9'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSuiteSpecificScalar(string suite, string key, string value)
    {
        if (!string.Equals(suite, "s04", StringComparison.Ordinal))
        {
            return false;
        }

        return key switch
        {
            "productVersion" or "productUnitProfileVersion" => IsPositiveDecimal(value),
            "productCode" => value.StartsWith("SYNS04-", StringComparison.Ordinal)
                             && IsPositiveDecimal(value[7..]),
            "productName" => string.Equals(
                value,
                "SENTETIK browser-suite:s04 sales candidate",
                StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool IsPositiveDecimal(string value)
    {
        if (value.Length == 0 || value[0] is < '1' or > '9')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (value[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
