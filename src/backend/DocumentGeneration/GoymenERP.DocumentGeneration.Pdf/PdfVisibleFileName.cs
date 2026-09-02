using System.Globalization;
using System.Text;

namespace GoymenERP.DocumentGeneration.Pdf;

public static class PdfVisibleFileName
{
    private const char SafeSeparator = '_';
    private const string PdfExtension = ".pdf";
    private const string ForbiddenCharacters = "<>:\"/\\|?*";
    private const int MaximumPortableFileNameUnits = 255;

    private static readonly HashSet<string> ReservedDeviceNames = CreateReservedDeviceNames();

    public static string Build(PdfDocumentFileNameParts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parts), "Document version must be positive.");
        }

        var documentType = NormalizeComponent(parts.DocumentType, nameof(parts.DocumentType));
        var documentNumber = NormalizeComponent(parts.DocumentNumber, nameof(parts.DocumentNumber));
        var counterpartyShortName = NormalizeComponent(
            parts.CounterpartyShortName,
            nameof(parts.CounterpartyShortName));
        var status = NormalizeComponent(parts.Status, nameof(parts.Status));

        var formattedDate = parts.DocumentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var formattedVersion = $"v{parts.Version.ToString("D2", CultureInfo.InvariantCulture)}";

        var visibleFileName = string.Concat(
                JoinWithSeparator(
                    documentType,
                    documentNumber,
                    counterpartyShortName,
                    formattedDate,
                    formattedVersion,
                    status),
                PdfExtension)
            .Normalize(NormalizationForm.FormC);

        EnsurePortableLength(visibleFileName, nameof(parts));
        return visibleFileName;
    }

    public static string Normalize(string requestedVisibleFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedVisibleFileName);

        var normalized = requestedVisibleFileName.Normalize(NormalizationForm.FormC);
        var stem = normalized.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase)
            ? normalized[..^PdfExtension.Length]
            : normalized;

        var sanitized = new StringBuilder(stem.Length);
        var hasUsableInput = false;
        var unsafeRun = false;

        foreach (var character in stem)
        {
            if (IsUnsafe(character))
            {
                unsafeRun = true;
                continue;
            }

            if (unsafeRun)
            {
                AppendSeparator(sanitized);
                unsafeRun = false;
            }

            sanitized.Append(character);
            hasUsableInput |= character is not ('.' or ' ');
        }

        if (unsafeRun)
        {
            AppendSeparator(sanitized);
        }

        var terminalStart = sanitized.Length;
        while (terminalStart > 0 && sanitized[terminalStart - 1] is '.' or ' ')
        {
            terminalStart--;
        }

        if (terminalStart != sanitized.Length)
        {
            sanitized.Length = terminalStart;
            AppendSeparator(sanitized);
        }

        if (!hasUsableInput || sanitized.Length == 0)
        {
            throw new ArgumentException(
                "The visible PDF file name must contain at least one safe character.",
                nameof(requestedVisibleFileName));
        }

        var safeStem = sanitized.ToString();
        RejectReservedDeviceName(safeStem, requestedVisibleFileName);

        var visibleFileName = string.Concat(safeStem, PdfExtension)
            .Normalize(NormalizationForm.FormC);
        EnsurePortableLength(visibleFileName, nameof(requestedVisibleFileName));
        return visibleFileName;
    }

    private static bool IsUnsafe(char character) =>
        ForbiddenCharacters.Contains(character, StringComparison.Ordinal)
        || CharUnicodeInfo.GetUnicodeCategory(character) is
            UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate;

    private static string NormalizeComponent(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var normalized = value.Normalize(NormalizationForm.FormC);
        var sanitized = new StringBuilder(normalized.Length);
        var separatorPending = false;
        var hasUsableInput = false;

        foreach (var character in normalized)
        {
            if (IsUnsafe(character) || char.IsWhiteSpace(character))
            {
                separatorPending = true;
                continue;
            }

            if (separatorPending)
            {
                AppendSeparator(sanitized);
                separatorPending = false;
            }

            sanitized.Append(character);
            hasUsableInput |= character != '.';
        }

        if (separatorPending || sanitized.Length > 0 && sanitized[^1] == '.')
        {
            while (sanitized.Length > 0 && sanitized[^1] == '.')
            {
                sanitized.Length--;
            }

            AppendSeparator(sanitized);
        }

        if (!hasUsableInput || sanitized.Length == 0)
        {
            throw new ArgumentException(
                "A PDF file name component must contain at least one safe character.",
                parameterName);
        }

        return sanitized.ToString();
    }

    private static void AppendSeparator(StringBuilder builder)
    {
        if (builder.Length == 0 || builder[^1] != SafeSeparator)
        {
            builder.Append(SafeSeparator);
        }
    }

    private static string JoinWithSeparator(params string[] components)
    {
        var result = new StringBuilder(components.Sum(static component => component.Length) + components.Length);
        foreach (var component in components)
        {
            var componentStart = 0;
            while (componentStart < component.Length && component[componentStart] == SafeSeparator)
            {
                componentStart++;
            }

            if (result.Length > 0)
            {
                while (result.Length > 0 && result[^1] == SafeSeparator)
                {
                    result.Length--;
                }

                result.Append(SafeSeparator);
            }

            result.Append(component.AsSpan(componentStart));
        }

        return result.ToString();
    }

    private static void RejectReservedDeviceName(string safeStem, string parameterValue)
    {
        var firstSegment = safeStem.Split('.', 2)[0];
        if (ReservedDeviceNames.Contains(firstSegment))
        {
            throw new ArgumentException(
                "The visible PDF file name cannot use a reserved operating-system device name.",
                nameof(parameterValue));
        }
    }

    private static void EnsurePortableLength(string visibleFileName, string parameterName)
    {
        if (visibleFileName.Length > MaximumPortableFileNameUnits
            || Encoding.UTF8.GetByteCount(visibleFileName) > MaximumPortableFileNameUnits)
        {
            throw new ArgumentException(
                "The visible PDF file name exceeds the portable 255-unit file-name limit.",
                parameterName);
        }
    }

    private static HashSet<string> CreateReservedDeviceNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
        };

        for (var suffix = 1; suffix <= 9; suffix++)
        {
            names.Add($"COM{suffix}");
            names.Add($"LPT{suffix}");
        }

        return names;
    }
}
