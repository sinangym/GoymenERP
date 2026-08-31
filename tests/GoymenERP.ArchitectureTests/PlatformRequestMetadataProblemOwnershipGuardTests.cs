namespace GoymenERP.ArchitectureTests;

public sealed class PlatformRequestMetadataProblemOwnershipGuardTests
{
    private const string ProblemCodesPath =
        "src/backend/GoymenERP.Api/Errors/ErpProblemCodes.cs";
    private const string ProblemExtensionsPath =
        "src/backend/GoymenERP.Api/Errors/ErpProblemDetailsExtensions.cs";
    private const string RequestMetadataProblemPath =
        "src/backend/GoymenERP.Api/Errors/ErpRequestMetadataProblem.cs";
    private const string CorrelationMiddlewarePath =
        "src/backend/GoymenERP.Api/Observability/CorrelationContextMiddleware.cs";
    private const string CompanyMiddlewarePath =
        "src/backend/GoymenERP.Api/Multitenancy/CompanyContextMiddleware.cs";
    private const string OpenApiContractPath =
        "src/backend/GoymenERP.Api/OpenApi/ErpOpenApiContract.cs";

    [Theory]
    [InlineData("\"company_context_invalid\"")]
    [InlineData("\"correlation_metadata_invalid\"")]
    public void ReservedProblemCodeLiterals_HaveOnePlatformDeclaration(
        string literal)
    {
        Assert.Equal(
            new[] { ProblemCodesPath },
            FindProductionSourcesContaining(literal));
    }

    [Theory]
    [InlineData(
        "ErpProblemCodes.CompanyContextInvalid",
        ProblemExtensionsPath,
        RequestMetadataProblemPath,
        OpenApiContractPath)]
    [InlineData(
        "ErpProblemCodes.CorrelationMetadataInvalid",
        ProblemExtensionsPath,
        RequestMetadataProblemPath,
        CorrelationMiddlewarePath,
        OpenApiContractPath)]
    public void ReservedProblemCodeReferences_MatchExactPlatformAllowlist(
        string reference,
        params string[] expectedPaths)
    {
        Assert.Equal(
            expectedPaths.Order(StringComparer.Ordinal),
            FindProductionSourcesContaining(reference));
    }

    [Theory]
    [InlineData(
        "WriteCompanyContextInvalidAsync",
        CompanyMiddlewarePath,
        RequestMetadataProblemPath)]
    [InlineData(
        "WriteCorrelationMetadataInvalidAsync",
        RequestMetadataProblemPath,
        CorrelationMiddlewarePath)]
    public void RequestMetadataProblemProducers_MatchExactMiddlewareAllowlist(
        string producerMethod,
        params string[] expectedPaths)
    {
        Assert.Equal(
            expectedPaths.Order(StringComparer.Ordinal),
            FindProductionSourcesContaining(producerMethod));
    }

    private static IReadOnlyList<string> FindProductionSourcesContaining(string value)
    {
        var repositoryRoot = FindRepositoryRoot();
        var backendRoot = Path.Combine(repositoryRoot, "src", "backend");
        return Directory.EnumerateFiles(backendRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path, backendRoot))
            .Where(path => File.ReadAllText(path).Contains(value, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsBuildArtifact(string path, string backendRoot)
    {
        var segments = Path.GetRelativePath(backendRoot, path).Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            || segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("GoymenERP repository root was not found.");
    }
}
