namespace GoymenERP.ArchitectureTests;

public sealed class C01E2eActivationBoundaryTests
{
    private const string ActivationType = "C01E2eShellCapabilityActivation";
    private const string DisposableKey = "GOYMEN_ERP_C01_E2E_DISPOSABLE";

    [Fact]
    public void CanonicalCounterpartyProvider_RemainsFullyStaged()
    {
        var source = ReadRepositoryFile(
            "src/backend/Modules/Counterparty/"
            + "GoymenERP.Modules.Counterparty.Infrastructure/Shell/"
            + "CounterpartyShellCapabilityProvider.cs");

        Assert.Equal(4, Count(source, "ImplementationAvailable: false"));
        Assert.DoesNotContain("ImplementationAvailable: true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("C01E2e", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendActivationSeam_IsRestrictedToProgramAndExactAdapter()
    {
        var backendRoot = Path.Combine(FindRepositoryRoot(), "src", "backend");
        var references = Directory
            .EnumerateFiles(backendRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(ActivationType, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(FindRepositoryRoot(), path)
                .Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "src/backend/GoymenERP.Api/Program.cs",
                "src/backend/GoymenERP.Api/Shell/C01E2eShellCapabilityActivation.cs",
            ],
            references);
    }

    [Fact]
    public void ProductionRuntimeConfiguration_DoesNotCarryDisposableInterlock()
    {
        foreach (var path in new[]
                 {
                     "infra/docker-compose.yml",
                     "src/backend/GoymenERP.Api/Dockerfile",
                     "src/backend/GoymenERP.Api/appsettings.json",
                     "src/backend/GoymenERP.Api/appsettings.Development.json",
                 })
        {
            var source = ReadRepositoryFile(path);
            Assert.DoesNotContain(DisposableKey, source, StringComparison.Ordinal);
            Assert.DoesNotContain("C01E2e__EnableListCapability", source, StringComparison.Ordinal);
        }
    }

    // BROWSER-CI a2 part 2 (decision W1): the gated fixture writers. Their keys, their activation and
    // Identity's fixture port registration are named nowhere else in the backend; only the fixture runner
    // (tests/GoymenERP.BrowserSuiteFixtureTool) sets the keys.
    [Fact]
    public void FixtureWriterKeys_AreNamedOnlyByTheGatedActivation()
    {
        Assert.Equal(
            ["src/backend/GoymenERP.Api/Shell/C01E2eFixtureWriterActivation.cs"],
            BackendFilesContaining("BrowserSuiteFixture:"));
        Assert.Equal(
            [
                "src/backend/GoymenERP.Api/Program.cs",
                "src/backend/GoymenERP.Api/Shell/C01E2eFixtureWriterActivation.cs",
            ],
            BackendFilesContaining("C01E2eFixtureWriterActivation"));
        Assert.Equal(
            [
                "src/backend/GoymenERP.Api/Shell/C01E2eFixtureWriterActivation.cs",
                "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Fixtures/"
                + "ProductMasterAssignmentFixtureStore.cs",
            ],
            BackendFilesContaining("AddProductMasterAssignmentFixtureStore"));
        // u02 (#331): Product's code-group port, the same shape.
        Assert.Equal(
            [
                "src/backend/GoymenERP.Api/Shell/C01E2eFixtureWriterActivation.cs",
                "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Fixtures/"
                + "ProductCodeGroupFixtureStore.cs",
            ],
            BackendFilesContaining("AddProductCodeGroupFixtureStore"));
    }

    [Fact]
    public void ProductionRuntimeConfiguration_DoesNotCarryTheFixtureWriterGate()
    {
        foreach (var path in new[]
                 {
                     "infra/docker-compose.yml",
                     "infra/docker-compose.c01-e2e.yml",
                     "infra/docker-compose.browser-normal.yml",
                     "infra/docker-compose.browser-cari-staged.yml",
                     "src/backend/GoymenERP.Api/Dockerfile",
                     "src/backend/GoymenERP.Api/appsettings.json",
                     "src/backend/GoymenERP.Api/appsettings.Development.json",
                 })
        {
            Assert.DoesNotContain("BrowserSuiteFixture", ReadRepositoryFile(path), StringComparison.Ordinal);
        }
    }

    private static string[] BackendFilesContaining(string value)
    {
        var root = FindRepositoryRoot();
        return Directory
            .EnumerateFiles(Path.Combine(root, "src", "backend"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && File.ReadAllText(path).Contains(value, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string ReadRepositoryFile(string relativePath) =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

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

        throw new DirectoryNotFoundException("Göymen ERP repository root was not found.");
    }
}
