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

            if (string.Equals(suite, "mfg", StringComparison.Ordinal)
                && key.StartsWith("process", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Seed output '{key}' is private to the deterministic Manufacturing fixture and must not widen the browser seed contract.");
            }

            var isGuid = Guid.TryParseExact(value, "D", out var guid)
                         && guid != Guid.Empty
                         && string.Equals(value, guid.ToString("D"), StringComparison.Ordinal);
            if (string.Equals(suite, "mfg", StringComparison.Ordinal)
                && IsManufacturingScalarKey(key))
            {
                if (!IsSuiteSpecificScalar(suite, key, value))
                {
                    throw new InvalidOperationException(
                        $"Seed output '{key}' must match its exact Manufacturing scalar contract.");
                }

                continue;
            }

            if (string.Equals(suite, "acc01", StringComparison.Ordinal)
                && IsAcc01ScalarKey(key))
            {
                if (!IsSuiteSpecificScalar(suite, key, value))
                {
                    throw new InvalidOperationException(
                        $"Seed output '{key}' must match its exact ACC01 scalar contract.");
                }

                continue;
            }

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
        if (string.Equals(suite, "s04", StringComparison.Ordinal))
        {
            return key switch
            {
                "productVersion" or "productUnitProfileVersion" or "allocationLabelVersion" =>
                    IsPositiveDecimal(value),
                "productCode" => value.StartsWith("SYNS04-", StringComparison.Ordinal)
                                 && IsPositiveDecimal(value[7..]),
                "productName" => string.Equals(
                    value,
                    "SENTETIK browser-suite:s04 sales candidate",
                    StringComparison.Ordinal),
                _ => false,
            };
        }

        if (string.Equals(suite, "r14", StringComparison.Ordinal))
        {
            return key switch
            {
                "productVersion" or "productUnitProfileVersion" => IsPositiveDecimal(value),
                "productCode" => string.Equals(value, "SAC-1", StringComparison.Ordinal),
                "productName" => string.Equals(
                    value,
                    "SENTETIK browser-suite:r14 genuine S235JR 3 mm x 248 mm kaplamasiz sac",
                    StringComparison.Ordinal),
                _ => false,
            };
        }

        if (string.Equals(suite, "mfg", StringComparison.Ordinal))
        {
            return key switch
            {
                "issueRollCode" => value == "SYN-MFG-ROLL-2",
                "replayRollCode" => value == "SYN-MFG-ROLL-3",
                "conflictRollCode" => value == "SYN-MFG-ROLL-4",
                "listRollCode" => value == "SYN-MFG-ROLL-5",
                "closeRollCode" => value == "SYN-MFG-ROLL-6",
                "planCreateRollCode" => value == "SYN-MFG-ROLL-7",
                "planReplayRollCode" => value == "SYN-MFG-ROLL-8",
                "planRefusalRollCode" => value == "SYN-MFG-ROLL-9",
                "outputUiRollCode" => value == "SYN-MFG-ROLL-10",
                "outputProductVersion" => IsPositiveDecimal(value),
                "outputRequiredDocuments" => value == "[]",
                "outputOwnerKind" or "outputCustodianKind" => value == "Company",
                "outputInitialBlock" => value == "Free",
                _ => false,
            };
        }

        if (string.Equals(suite, "acc01", StringComparison.Ordinal))
        {
            return key switch
            {
                "normalSourceLabel" => IsAcc01SourceLabel(value),
                "normalOccurredOn" => value == "2026-09-24",
                "normalAmount" => value == "10.25",
                "normalDebitLabel" => value == "SENTETIK-100 - Sentetik borc hesabi (Borç)",
                "normalCreditLabel" => value == "SENTETIK-120 - Sentetik alacak hesabi (Alacak)",
                _ => false,
            };
        }

        if (!string.Equals(suite, "pur01", StringComparison.Ordinal))
        {
            return false;
        }

        return key switch
        {
            "productVersion" or "productUnitProfileVersion" => IsPositiveDecimal(value),
            "productCode" => value.StartsWith("SYNPUR01-", StringComparison.Ordinal)
                             && IsPositiveDecimal(value[9..]),
            "productName" => string.Equals(
                value,
                "SENTETIK browser-suite:pur01 purchase candidate",
                StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool IsManufacturingScalarKey(string key) => key is
        "issueRollCode" or "replayRollCode" or "conflictRollCode" or "listRollCode"
        or "closeRollCode" or "planCreateRollCode" or "planReplayRollCode"
        or "planRefusalRollCode" or "outputUiRollCode" or "outputProductVersion"
        or "outputRequiredDocuments" or "outputOwnerKind" or "outputCustodianKind"
        or "outputInitialBlock";

    private static bool IsAcc01ScalarKey(string key) => key is
        "normalSourceLabel" or "normalOccurredOn" or "normalAmount"
        or "normalDebitLabel" or "normalCreditLabel";

    private static bool IsAcc01SourceLabel(string value)
    {
        const string prefix = "Kaynak ref. ...";
        const string suffix = " - 2026-09-24";
        if (!value.StartsWith(prefix, StringComparison.Ordinal)
            || !value.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var tail = value[prefix.Length..^suffix.Length];
        return tail.Length == 8
            && tail.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
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
