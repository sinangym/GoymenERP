namespace GoymenERP.Modules.Organization.Domain.Companies;

/// <summary>
/// KIM-D40: the official last-digit check of a ten-digit Turkish tax identity number (VKN). The preparer's proposed VKN
/// must satisfy it before a change request is recorded, so a non-conforming VKN is refused before approval. Golden
/// examples in tests are synthetic values that satisfy the algorithm, never real VKNs.
/// </summary>
public static class CompanyVknCheckDigit
{
    private const int DigitBase = 10;
    private const int WeightedDigitCount = Company.VknLength - 1;
    private const int ModulusNine = 9;

    /// <summary>
    /// True when <paramref name="vkn"/> is in canonical shape (<see cref="Company.IsCanonicalVkn"/>) and its last
    /// digit equals the check digit computed from the first nine digits.
    /// </summary>
    public static bool IsSatisfiedBy(string? vkn)
    {
        if (!Company.IsCanonicalVkn(vkn))
        {
            return false;
        }

        var sum = 0;
        for (var position = 1; position <= WeightedDigitCount; position++)
        {
            var digit = vkn![position - 1] - '0';
            var shifted = (digit + DigitBase - position) % DigitBase;
            sum += shifted == ModulusNine
                ? ModulusNine
                : shifted * (1 << (DigitBase - position)) % ModulusNine;
        }

        var expected = (DigitBase - (sum % DigitBase)) % DigitBase;
        return vkn![WeightedDigitCount] - '0' == expected;
    }
}
