using System.Globalization;
using System.Text;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// URN-D13/D16: reads the rows of one kg/m table file (<see cref="ProductProfileWeightFileFormatV1"/>) on the server. Nothing is
/// rounded or guessed: a value with more decimals than its column holds, a missing or extra field, an unknown kind, a solid or
/// impossible section or a repeated measure pair refuses the whole file with its line number (Turkish, for the detail). KLN-D87:
/// under the extended header a structural profile row is keyed by its series and size designation.
/// </summary>
internal static class ProductProfileWeightFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private const int FieldCount = 6;
    private const int ExtendedFieldCount = 8;

    /// <summary>Null when the file is a valid table; otherwise the reason. <paramref name="rows"/> keep the file order.</summary>
    internal static string? Refusal(byte[] content, out IReadOnlyList<ProductProfileWeightRowV1> rows)
    {
        ArgumentNullException.ThrowIfNull(content);
        rows = [];
        string text;
        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return "Dosya UTF-8 metin olmalıdır.";
        }

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var lines = text.Split('\n');
        var parsed = new List<ProductProfileWeightRowV1>();
        var keys = new HashSet<(string, decimal?, decimal?, decimal?, decimal?, string?, string?)>();
        var headerSeen = false;
        var fieldCount = FieldCount;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            var lineNo = index + 1;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!headerSeen)
            {
                var header = line.Trim();
                if (string.Equals(header, ProductProfileWeightFileFormatV1.ExtendedHeader, StringComparison.Ordinal))
                {
                    fieldCount = ExtendedFieldCount;
                }
                else if (!string.Equals(header, ProductProfileWeightFileFormatV1.Header, StringComparison.Ordinal))
                {
                    return HeaderRefusal;
                }

                headerSeen = true;
                continue;
            }

            if (parsed.Count == ProductProfileWeightInputLimitsV1.MaximumRows)
            {
                return $"Dosya en fazla {ProductProfileWeightInputLimitsV1.MaximumRows} satır içerebilir.";
            }

            if (Row(line, lineNo, fieldCount, out var row) is { } refusal)
            {
                return refusal;
            }

            if (!keys.Add((row!.SectionKind, row.WidthMm, row.HeightMm, row.OuterDiameterMm, row.WallThicknessMm, row.ProfileSeries,
                    row.ProfileSize)))
            {
                return $"Satır {lineNo}: aynı ölçü dosyada birden fazla kez geçiyor.";
            }

            parsed.Add(row);
        }

        if (!headerSeen)
        {
            return HeaderRefusal;
        }

        if (parsed.Count == 0)
        {
            return "Dosyada en az bir satır olmalıdır.";
        }

        rows = parsed;
        return null;
    }

    private static string HeaderRefusal =>
        $"İlk satır başlık olmalıdır: {ProductProfileWeightFileFormatV1.Header} veya {ProductProfileWeightFileFormatV1.ExtendedHeader}";

    private static string? Row(string line, int lineNo, int fieldCount, out ProductProfileWeightRowV1? row)
    {
        row = null;
        var fields = line.Split(ProductProfileWeightFileFormatV1.Separator);
        if (fields.Length != fieldCount)
        {
            return $"Satır {lineNo}: {fieldCount} alan olmalıdır.";
        }

        var kind = fields[0].Trim();
        var structural = fieldCount == ExtendedFieldCount && kind == ProductDimensionSectionKindsV1.StructuralProfile;
        if (!structural && kind is not (ProductDimensionSectionKindsV1.Profile or ProductDimensionSectionKindsV1.RoundTube))
        {
            return fieldCount == ExtendedFieldCount
                ? $"Satır {lineNo}: kesit türü Profile, RoundTube veya StructuralProfile olmalıdır."
                : $"Satır {lineNo}: kesit türü Profile veya RoundTube olmalıdır.";
        }

        var series = fieldCount == ExtendedFieldCount ? fields[6].Trim() : string.Empty;
        var size = fieldCount == ExtendedFieldCount ? fields[7].Trim() : string.Empty;
        if (!structural && (series.Length > 0 || size.Length > 0))
        {
            return $"Satır {lineNo}: profil serisi ve ölçü adı yalnız yapısal profil satırında girilir.";
        }

        if (!TryNumber(fields[1], out var width) || !TryNumber(fields[2], out var height)
            || !TryNumber(fields[3], out var diameter) || !TryNumber(fields[4], out var wall)
            || !TryNumber(fields[5], out var kg))
        {
            return $"Satır {lineNo}: sayılar işaretsiz, binlik ayırıcısız ve ondalık ayırıcı nokta olmalıdır.";
        }

        if (!ProductDimensionShape.IsMillimetre(width) || !ProductDimensionShape.IsMillimetre(height)
            || !ProductDimensionShape.IsMillimetre(diameter) || !ProductDimensionShape.IsMillimetre(wall))
        {
            return $"Satır {lineNo}: ölçüler sıfırdan büyük ve en fazla {ProductDimensionInputLimitsV1.MillimetreDecimals} ondalıklı "
                + "milimetre olmalıdır.";
        }

        if (kg is null || !ProductDimensionShape.IsKgPerMetre(kg))
        {
            return $"Satır {lineNo}: metre ağırlığı zorunludur; sıfırdan büyük ve en fazla "
                + $"{ProductDimensionInputLimitsV1.KgPerMetreDecimals} ondalıklı olmalıdır.";
        }

        if (structural)
        {
            if (width is not null || height is not null || diameter is not null || wall is not null)
            {
                return $"Satır {lineNo}: yapısal profil için seri, ölçü adı ve metre ağırlığı girilir; ölçüler boş kalır.";
            }

            if (!ProductProfileSeriesV1.IsKnown(series))
            {
                return $"Satır {lineNo}: yapısal profil serisi UPN (NPU), NPI, IPE, HEA, HEB veya HEM olmalıdır.";
            }

            if (!ProductDimensionShape.IsProfileSize(size))
            {
                return $"Satır {lineNo}: profil ölçü adı en fazla {ProductDimensionInputLimitsV1.ProfileSizeMaximumLength} karakter; "
                    + "büyük harf, rakam ve nokta olmalıdır.";
            }

            row = new ProductProfileWeightRowV1(kind, null, null, null, null, ProductDimensionShape.Canonical(kg)!.Value, series,
                size);
            return null;
        }

        if (wall is null)
        {
            return $"Satır {lineNo}: et kalınlığı zorunludur.";
        }

        if (kind == ProductDimensionSectionKindsV1.Profile)
        {
            if (width is null || height is null || diameter is not null)
            {
                return $"Satır {lineNo}: profil için en, yükseklik ve et kalınlığı girilir; dış çap boş kalır.";
            }

            if (!ProductDimensionShape.WallFits(wall.Value, width.Value) || !ProductDimensionShape.WallFits(wall.Value, height.Value))
            {
                return $"Satır {lineNo}: et kalınlığının iki katı profilin eninden veya yüksekliğinden büyük olamaz.";
            }
        }
        else
        {
            if (diameter is null || width is not null || height is not null)
            {
                return $"Satır {lineNo}: yuvarlak boru için dış çap ve et kalınlığı girilir; en ve yükseklik boş kalır.";
            }

            if (!ProductDimensionShape.WallFits(wall.Value, diameter.Value))
            {
                return $"Satır {lineNo}: et kalınlığının iki katı dış çaptan büyük olamaz.";
            }
        }

        row = new ProductProfileWeightRowV1(kind, ProductDimensionShape.Canonical(width), ProductDimensionShape.Canonical(height),
            ProductDimensionShape.Canonical(diameter), ProductDimensionShape.Canonical(wall),
            ProductDimensionShape.Canonical(kg)!.Value);
        return null;
    }

    /// <summary>An empty field is null; otherwise digits with at most one dot between digits (no sign, exponent or grouping).</summary>
    private static bool TryNumber(string raw, out decimal? value)
    {
        value = null;
        var text = raw.Trim();
        if (text.Length == 0)
        {
            return true;
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var digitsOnly = text.All(character => char.IsAsciiDigit(character) || character == '.');
        if (!digitsOnly || dot == 0 || dot == text.Length - 1 || (dot >= 0 && text.IndexOf('.', dot + 1) >= 0))
        {
            return false;
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        value = number;
        return true;
    }
}
