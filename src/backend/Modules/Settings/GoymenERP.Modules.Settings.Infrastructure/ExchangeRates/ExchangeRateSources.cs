using System.Globalization;
using System.Net;
using System.Xml;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.Configuration;

namespace GoymenERP.Modules.Settings.Infrastructure.ExchangeRates;

/// <summary>
/// The exchange-rate source switches (configuration section <c>Settings:ExchangeRates</c>). Both are OFF by default and the
/// repository ships no value for them: enabling the outbound TCMB connection (or any half-day source) in an environment is a
/// human gate (KUR_GUNU "Açık kalanlar", EK24 SAT-D169/D171). No secret is involved.
/// </summary>
public sealed record ExchangeRateSourceOptions(bool TcmbEnabled, Uri? TcmbBaseAddress, TimeSpan TcmbTimeout)
{
    public const string SectionName = "Settings:ExchangeRates";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    public static ExchangeRateSourceOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(SectionName);
        var enabled = bool.TryParse(section["TcmbEnabled"], out var flag) && flag;
        var address = Uri.TryCreate(section["TcmbBaseAddress"], UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : null;
        var timeout = int.TryParse(section["TcmbTimeoutSeconds"], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                      && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : DefaultTimeout;
        // Enabled only with an explicit https base address: a flag alone never opens a connection.
        return new ExchangeRateSourceOptions(enabled && address is not null, address, timeout);
    }
}

/// <summary>The default bulletin source: disabled; the import refuses with <c>exchange_rate_source_disabled</c>.</summary>
public sealed class DisabledExchangeRateBulletinSource : IExchangeRateBulletinSourceV1
{
    public bool IsEnabled => false;

    public Task<ExchangeRateBulletinFetchV1> FetchAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("TCMB kur kaynağı bu ortamda etkin değil.");
}

/// <summary>
/// The default (and today the only) half-day source: disabled. SAT-D171 asks for an automatic official source of arife / half
/// days; no reviewed machine-readable official source is wired yet, so every day stays "undetermined" and the resolver chooses
/// no rate for it (fail-closed). Wiring a source and enabling it is a human gate.
/// </summary>
public sealed class DisabledExchangeRateHalfDaySource : IExchangeRateHalfDaySourceV1
{
    public bool IsEnabled => false;

    public Task<ExchangeRateHalfDayFetchV1> FetchAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Arife / yarım gün kaynağı bu ortamda etkin değil.");
}

/// <summary>
/// TCMB's own dated bulletin archive (<c>{base}/kurlar/yyyyMM/ddMMyyyy.xml</c>). Constructed only when the options enable it.
/// <list type="bullet">
/// <item>200 with a bulletin whose <c>Tarih</c> is the asked day → Published (forex selling per one unit).</item>
/// <item>404 → NoBulletin: TCMB's own answer that the dated file does not exist (KUR_GUNU).</item>
/// <item>Anything else (other status, timeout, network error, a bulletin of another day, unreadable XML) → Unverified.</item>
/// </list>
/// <c>today.xml</c> is deliberately not used: it answers with whatever the latest bulletin is and could fill a day with another
/// day's rates, which KUR_GUNU forbids. Only past days are asked (the administration refuses today and later).
/// </summary>
public sealed class TcmbExchangeRateBulletinSource(HttpClient httpClient) : IExchangeRateBulletinSourceV1
{
    public bool IsEnabled => true;

    public async Task<ExchangeRateBulletinFetchV1> FetchAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"kurlar/{date:yyyyMM}/{date:ddMMyyyy}.xml");
        using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new ExchangeRateBulletinFetchV1(date, ExchangeRateBulletinStatesV1.NoBulletin, [], "TCMB: " + path + " 404");
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return new ExchangeRateBulletinFetchV1(date, ExchangeRateBulletinStatesV1.Unverified, [],
                "TCMB: " + path + " " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return TcmbBulletinXml.Parse(date, stream, "TCMB: " + path);
    }
}

/// <summary>
/// Reads one TCMB bulletin XML (<c>Tarih_Date</c> with <c>Currency</c> lines: <c>CurrencyCode</c>, <c>Unit</c>,
/// <c>ForexSelling</c>). DTDs and external resolution are off. A line without a forex selling rate is skipped; a line whose
/// unit is not a power of ten (so the per-one-unit rate would not be exact) is skipped rather than rounded. A bulletin of
/// another day, no usable line, or unreadable XML is Unverified.
/// </summary>
public static class TcmbBulletinXml
{
    private const int MaximumCharacters = 1_000_000;

    public static ExchangeRateBulletinFetchV1 Parse(DateOnly date, Stream stream, string reference)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumCharacters,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };
        try
        {
            using var reader = XmlReader.Create(stream, settings);
            var document = new XmlDocument { XmlResolver = null };
            document.Load(reader);
            var root = document.DocumentElement;
            if (root is null || root.Name != "Tarih_Date"
                || !DateOnly.TryParseExact(root.GetAttribute("Tarih"), "dd.MM.yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var bulletinDate)
                || bulletinDate != date)
            {
                return Unverified(date, reference + " (bülten tarihi farklı veya okunamadı)");
            }

            var lines = new List<ExchangeRateLineV1>();
            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlElement currency in root.GetElementsByTagName("Currency"))
            {
                var code = currency.GetAttribute("CurrencyCode");
                var unitText = currency["Unit"]?.InnerText.Trim();
                var sellingText = currency["ForexSelling"]?.InnerText.Trim();
                if (!ExchangeRateCurrencyIdentitiesV1.IsIsoCode(code) || code == "TRY" || string.IsNullOrEmpty(sellingText)
                    || !int.TryParse(unitText, NumberStyles.None, CultureInfo.InvariantCulture, out var unit)
                    || !IsPowerOfTen(unit)
                    || !decimal.TryParse(sellingText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var selling)
                    || selling <= 0m)
                {
                    continue;
                }

                if (!codes.Add(code))
                {
                    return Unverified(date, reference + " (aynı para birimi iki kez)");
                }

                var perUnit = selling / unit / 1.0000000000000000000000000000m;
                lines.Add(new ExchangeRateLineV1(code, perUnit.ToString(CultureInfo.InvariantCulture)));
            }

            var bulletinNumber = root.GetAttribute("Bulten_No");
            return lines.Count == 0
                ? Unverified(date, reference + " (kullanılabilir kur satırı yok)")
                : new ExchangeRateBulletinFetchV1(date, ExchangeRateBulletinStatesV1.Published, lines,
                    string.IsNullOrWhiteSpace(bulletinNumber) ? reference : reference + " bülten " + bulletinNumber.Trim());
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or IOException)
        {
            return Unverified(date, reference + " (XML okunamadı)");
        }
    }

    private static bool IsPowerOfTen(int unit)
    {
        if (unit <= 0)
        {
            return false;
        }

        while (unit % 10 == 0)
        {
            unit /= 10;
        }

        return unit == 1;
    }

    private static ExchangeRateBulletinFetchV1 Unverified(DateOnly date, string reference) =>
        new(date, ExchangeRateBulletinStatesV1.Unverified, [], reference);
}
