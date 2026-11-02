using System.Net;
using System.Text;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure;
using GoymenERP.Modules.Settings.Infrastructure.ExchangeRates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KUR_GUNU / EK24: the official sources are OFF unless configuration names them (an https base address is required, a human
/// gate per environment); the TCMB bulletin reader never fills a day with another day's bulletin, normalises a per-100 line to
/// one unit exactly and fails closed (unverified) on anything it cannot read. The XML here is SENTETIK (TCMB's shape).
/// The permission names are registered centrally, company-wide, not Critical and seeded to no role.
/// </summary>
public sealed class SettingsExchangeRateSourceTests
{
    private const string Bulletin = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Tarih_Date Tarih="16.10.2026" Date="10/16/2026" Bulten_No="2026/199">
          <Currency CrossOrder="0" Kod="USD" CurrencyCode="USD">
            <Unit>1</Unit><Isim>ABD DOLARI</Isim><CurrencyName>US DOLLAR</CurrencyName>
            <ForexBuying>41.5123</ForexBuying><ForexSelling>41.5871</ForexSelling>
          </Currency>
          <Currency CrossOrder="1" Kod="JPY" CurrencyCode="JPY">
            <Unit>100</Unit><Isim>JAPON YENİ</Isim><CurrencyName>JAPENESE YEN</CurrencyName>
            <ForexBuying>27.3012</ForexBuying><ForexSelling>27.4512</ForexSelling>
          </Currency>
          <Currency CrossOrder="2" Kod="XDR" CurrencyCode="XDR">
            <Unit>1</Unit><Isim>ÖZEL ÇEKME HAKKI (SDR)</Isim><CurrencyName>SPECIAL DRAWING RIGHT (SDR)</CurrencyName>
            <ForexBuying>56.1</ForexBuying><ForexSelling></ForexSelling>
          </Currency>
          <Currency CrossOrder="3" Kod="ABC" CurrencyCode="ABC">
            <Unit>3</Unit><ForexSelling>9.99</ForexSelling>
          </Currency>
        </Tarih_Date>
        """;

    [Fact]
    public void A_bulletin_of_the_asked_day_is_published_per_one_unit_without_rounding()
    {
        var answer = Parse("2026-10-16", Bulletin);

        Assert.Equal(ExchangeRateBulletinStatesV1.Published, answer.State);
        Assert.Equal(["USD:41.5871", "JPY:0.274512"], answer.Rates.Select(line => $"{line.CurrencyCode}:{line.SellingRate}"));
        Assert.Contains("2026/199", answer.SourceReference, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bulletin_of_another_day_is_never_used_for_the_asked_day()
    {
        var answer = Parse("2026-10-19", Bulletin);

        Assert.Equal(ExchangeRateBulletinStatesV1.Unverified, answer.State);
        Assert.Empty(answer.Rates);
    }

    [Theory]
    [InlineData("<Tarih_Date Tarih=\"16.10.2026\"></Tarih_Date>")]
    [InlineData("<html>bakım</html>")]
    [InlineData("not xml")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><Tarih_Date Tarih=\"16.10.2026\">&e;</Tarih_Date>")]
    public void Anything_unreadable_or_without_a_usable_line_is_unverified(string xml)
    {
        Assert.Equal(ExchangeRateBulletinStatesV1.Unverified, Parse("2026-10-16", xml).State);
    }

    [Fact]
    public void A_currency_twice_is_unverified()
    {
        var doubled = Bulletin.Replace("Kod=\"JPY\" CurrencyCode=\"JPY\"", "Kod=\"USD\" CurrencyCode=\"USD\"", StringComparison.Ordinal);
        Assert.Equal(ExchangeRateBulletinStatesV1.Unverified, Parse("2026-10-16", doubled).State);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ExchangeRateBulletinStatesV1.NoBulletin)]
    [InlineData(HttpStatusCode.InternalServerError, ExchangeRateBulletinStatesV1.Unverified)]
    [InlineData(HttpStatusCode.Forbidden, ExchangeRateBulletinStatesV1.Unverified)]
    [InlineData(HttpStatusCode.OK, ExchangeRateBulletinStatesV1.Published)]
    public async Task The_dated_archive_answer_decides_the_state(HttpStatusCode status, string expected)
    {
        var handler = new StubHandler(status, Bulletin);
        var source = new TcmbExchangeRateBulletinSource(new HttpClient(handler) { BaseAddress = new Uri("https://tcmb.example.test/") });

        var answer = await source.FetchAsync(new DateOnly(2026, 10, 16));

        Assert.Equal(expected, answer.State);
        Assert.Equal("https://tcmb.example.test/kurlar/202610/16102026.xml", handler.Requested?.ToString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "https://www.tcmb.gov.tr/")]
    [InlineData("false", "https://www.tcmb.gov.tr/")]
    [InlineData("false", "http://www.tcmb.gov.tr")]
    [InlineData("yes", "https://www.tcmb.gov.tr/")]
    public void The_sources_stay_disabled_unless_explicitly_enabled(string? enabled, string? address)
    {
        var services = Services(enabled, address);

        Assert.IsType<DisabledExchangeRateBulletinSource>(services.GetRequiredService<IExchangeRateBulletinSourceV1>());
        Assert.False(services.GetRequiredService<IExchangeRateBulletinSourceV1>().IsEnabled);
        Assert.IsType<DisabledExchangeRateHalfDaySource>(services.GetRequiredService<IExchangeRateHalfDaySourceV1>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://www.tcmb.gov.tr/")]
    [InlineData("https://www.tcmb.gov.tr")]
    [InlineData("https://www.tcmb.gov.tr/kurlar")]
    [InlineData("https://www.tcmb.gov.tr/?x=1/")]
    [InlineData("not a url/")]
    public void An_explicit_enable_with_an_address_that_is_not_https_ending_in_a_slash_fails_at_startup(string? address)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Services("true", address));
        Assert.Contains("TcmbBaseAddress", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicit_https_configuration_enables_only_the_tcmb_source()
    {
        var services = Services("true", "https://www.tcmb.gov.tr/");

        Assert.IsType<TcmbExchangeRateBulletinSource>(services.GetRequiredService<IExchangeRateBulletinSourceV1>());
        Assert.False(services.GetRequiredService<IExchangeRateHalfDaySourceV1>().IsEnabled);
    }

    [Fact]
    public void The_repository_ships_no_source_configuration()
    {
        var root = FindRepositoryRoot();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src/backend/GoymenERP.Api"), "appsettings*.json"))
        {
            Assert.DoesNotContain("ExchangeRates", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(SettingsExchangeRatePermissionManifestV1.Read, ErpPermissions.SettingsExchangeRatesRead)]
    [InlineData(SettingsExchangeRatePermissionManifestV1.Correct, ErpPermissions.SettingsExchangeRatesCorrect)]
    [InlineData(SettingsExchangeRatePermissionManifestV1.Import, ErpPermissions.SettingsExchangeRatesImport)]
    public void The_names_are_registered_company_wide_not_critical_and_seeded_to_no_role(string owner, string central)
    {
        Assert.Equal(owner, central);
        Assert.Single(ErpPermissions.All, name => name == owner);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(owner));
        Assert.False(ErpPermissions.IsCritical(owner));
        var seed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src/backend/GoymenERP.Api/Seed/identity-seed.json"));
        Assert.DoesNotContain(owner, seed, StringComparison.Ordinal);
    }

    private static ServiceProvider Services(string? enabled, string? address)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = "Server=unused;Database=unused",
            ["Settings:ExchangeRates:TcmbEnabled"] = enabled,
            ["Settings:ExchangeRates:TcmbBaseAddress"] = address,
        }).Build();
        return new ServiceCollection().AddSettingsModule(configuration).BuildServiceProvider();
    }

    private static ExchangeRateBulletinFetchV1 Parse(string date, string xml) =>
        TcmbBulletinXml.Parse(DateOnly.ParseExact(date, "yyyy-MM-dd"), new MemoryStream(Encoding.UTF8.GetBytes(xml.Trim())), "SENTETIK");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/xml") });
        }
    }
}
