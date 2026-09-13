namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Lossless positive decimal wire value. Validates lexical syntax and supplied
/// profile scale without floating point, decimal overflow, truncation or rounding.
/// This is not arithmetic, unit conversion or approval of the supplied scale.
/// </summary>
public sealed record ExactPositiveDecimalV1
{
    private ExactPositiveDecimalV1(string text) => Text = text;

    public string Text { get; }

    public static ExactPositiveDecimalV1 Parse(string? text, int approvedScale)
    {
        if (approvedScale < 0)
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        if (string.IsNullOrEmpty(text))
        {
            throw new SettingsPrerequisiteException("settings_decimal_invalid");
        }

        var decimalPoint = -1;
        var hasNonzeroDigit = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '.' && decimalPoint < 0 && index > 0 && index < text.Length - 1)
            {
                decimalPoint = index;
                continue;
            }

            if (character is < '0' or > '9')
            {
                throw new SettingsPrerequisiteException("settings_decimal_invalid");
            }

            hasNonzeroDigit |= character != '0';
        }

        if (!hasNonzeroDigit)
        {
            throw new SettingsPrerequisiteException("settings_decimal_invalid");
        }

        if (decimalPoint >= 0 && text.Length - decimalPoint - 1 > approvedScale)
        {
            throw new SettingsPrerequisiteException("settings_decimal_scale_exceeded");
        }

        return new ExactPositiveDecimalV1(text);
    }
}
