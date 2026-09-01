using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoymenERP.Counterparty.UnitTests;

public sealed partial class C02ScreenIdentitySourceContractTests
{
    private const string JsonContractPath =
        "docs/features/CARI_UI_VERTICAL_SLICE_v1.json";

    private const string MarkdownContractPath =
        "docs/features/CARI_UI_VERTICAL_SLICE_v1.md";

    private const string JitContractPath =
        "docs/features/CARI_C02_LINK_JIT_SPEC_v1.md";

    [Fact]
    public void CreateRoute_UsesCanonicalC02ScreenAndExplicitScopeVariant()
    {
        var repositoryRoot = FindRepositoryRoot();
        var jsonPath = Path.Combine(repositoryRoot, JsonContractPath);
        var json = File.ReadAllText(jsonPath);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var includedScreens = root.GetProperty("screenScope")
            .GetProperty("included")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();

        Assert.Contains("C-02", includedScreens);
        Assert.All(includedScreens, screen => Assert.Matches(ScreenIdRegex(), screen));

        var routeInvariant = root.GetProperty("routeContractInvariants")
            .GetProperty("cari.create");
        var createRoute = root.GetProperty("routes")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "cari.create");

        Assert.Equal("cari.create", routeInvariant.GetProperty("routeId").GetString());
        Assert.Equal("C-02", routeInvariant.GetProperty("screen").GetString());
        Assert.Equal(
            "create-subset",
            routeInvariant.GetProperty("scopeVariant").GetString());
        Assert.Equal("/cari/yeni", routeInvariant.GetProperty("path").GetString());
        Assert.Equal(
            "cari.parties.create",
            routeInvariant.GetProperty("capability").GetString());

        Assert.Equal(routeInvariant.GetProperty("routeId").GetString(), createRoute.GetProperty("id").GetString());
        Assert.Equal(routeInvariant.GetProperty("screen").GetString(), createRoute.GetProperty("screen").GetString());
        Assert.Equal(
            routeInvariant.GetProperty("scopeVariant").GetString(),
            createRoute.GetProperty("scopeVariant").GetString());
        Assert.Equal(routeInvariant.GetProperty("path").GetString(), createRoute.GetProperty("path").GetString());
        Assert.Equal(
            routeInvariant.GetProperty("capability").GetString(),
            createRoute.GetProperty("capability").GetString());
        Assert.Equal("static-before-parameter", createRoute.GetProperty("priority").GetString());
        Assert.Equal("single-company", createRoute.GetProperty("context").GetString());
        Assert.Matches(ScreenIdRegex(), createRoute.GetProperty("screen").GetString()!);

        var routeScreens = root.GetProperty("routes")
            .EnumerateArray()
            .Select(item => item.GetProperty("screen").GetString()!)
            .ToArray();
        var forbiddenScreens = routeInvariant.GetProperty("forbiddenScreenLiterals")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();

        Assert.Equal(["C-02:create-subset"], forbiddenScreens);
        Assert.All(
            forbiddenScreens,
            forbidden =>
            {
                Assert.DoesNotContain(forbidden, includedScreens);
                Assert.DoesNotContain(forbidden, routeScreens);
            });

        var createCapability = root.GetProperty("capabilities")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "cari.parties.create");

        Assert.Equal(
            ["counterparty.parties.create"],
            createCapability.GetProperty("permissions")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
        Assert.Equal("company-wide", createCapability.GetProperty("scope").GetString());

        var markdown = File.ReadAllText(Path.Combine(repositoryRoot, MarkdownContractPath));
        Assert.Contains(
            "Shell-facing canonical screen kimliği `C-02`’dir.",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "| `cari.create` | `C-02` (`scopeVariant=create-subset`) | `/cari/yeni` |",
            markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain("C-02:create-subset", markdown, StringComparison.Ordinal);

        var jitContract = File.ReadAllText(Path.Combine(repositoryRoot, JitContractPath));
        Assert.Contains(
            "Canonical shell screen kimliği `C-02`’dir.",
            jitContract,
            StringComparison.Ordinal);
        Assert.Contains(
            "`C-02A` yalnız business/UX alt-akış etiketidir; shell screen id veya alias değildir.",
            jitContract,
            StringComparison.Ordinal);
        Assert.Contains(
            "`scopeVariant=create-subset`",
            jitContract,
            StringComparison.Ordinal);
        Assert.DoesNotContain("C-02:create-subset", jitContract, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Göymen ERP repository root was not found.");
    }

    [GeneratedRegex("^[A-Z][A-Z0-9-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScreenIdRegex();
}
