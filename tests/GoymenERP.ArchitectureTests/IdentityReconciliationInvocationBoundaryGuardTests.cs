using System.Text.RegularExpressions;

namespace GoymenERP.ArchitectureTests;

public sealed partial class PlatformTenantQueryFilterBypassGuardTests
{
    private const string IdentityReconciliationContractPath =
        "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Application/Contracts/"
        + "ISystemRolePermissionReconciler.cs";

    private const string IdentityReconciliationImplementationPath =
        "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Seeding/"
        + "SystemRolePermissionReconciler.cs";

    private const string IdentityModuleCompositionRootPath =
        "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/"
        + "IdentityModuleExtensions.cs";

    private static readonly Regex IdentityReconcilerTypeRegex = new(
        @"(?<![\p{L}\p{N}_])(?:ISystemRolePermissionReconciler|SystemRolePermissionReconciler)(?![\p{L}\p{N}_])",
        RegexOptions.CultureInvariant);

    private static readonly Regex ReconcileMethodRegex = new(
        @"(?<![\p{L}\p{N}_])ReconcileAsync(?![\p{L}\p{N}_])",
        RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, ReconcilerSourceExpectation>
        ReviewedIdentityReconcilerSources =
            new Dictionary<string, ReconcilerSourceExpectation>(StringComparer.Ordinal)
            {
                [IdentityReconciliationContractPath] = new(
                    ReconcilerTypeReferenceCount: 1,
                    ReconcileMethodReferenceCount: 1,
                    RequiredShape: new Regex(
                        @"public\s+interface\s+ISystemRolePermissionReconciler",
                        RegexOptions.CultureInvariant)),
                [IdentityReconciliationImplementationPath] = new(
                    ReconcilerTypeReferenceCount: 2,
                    ReconcileMethodReferenceCount: 1,
                    RequiredShape: new Regex(
                        @"internal\s+sealed\s+class\s+SystemRolePermissionReconciler[\s\S]*:\s*ISystemRolePermissionReconciler",
                        RegexOptions.CultureInvariant)),
                [IdentityModuleCompositionRootPath] = new(
                    ReconcilerTypeReferenceCount: 2,
                    ReconcileMethodReferenceCount: 0,
                    RequiredShape: new Regex(
                        @"services\s*\.\s*AddScoped\s*<\s*ISystemRolePermissionReconciler\s*,\s*SystemRolePermissionReconciler\s*>\s*\(\s*\)\s*;",
                        RegexOptions.CultureInvariant))
            };

    [Fact]
    public void AllProductionIdentityReconcilerSources_AreInertAndHaveNoRuntimeInvocationSurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var inventory = ReadBackendSourceInventory(repositoryRoot);
        var allProductionSources = IncludeCounterpartyProductionSources(
            repositoryRoot,
            inventory.Sources);

        var errors = ValidateIdentityReconcilerInvocationBoundary(allProductionSources);

        Assert.True(
            errors.Count == 0,
            "Identity reconciliation must remain an inert DI registration across every production "
            + "source, including Counterparty. Normal API startup, hosted services, endpoints and "
            + "request-pipeline code cannot reference or invoke it."
            + Environment.NewLine
            + string.Join(Environment.NewLine, errors));
    }

    [Theory]
    [InlineData("src/backend/GoymenERP.Api/Program.cs")]
    [InlineData("src/backend/GoymenERP.Api/Endpoints/ReconciliationEndpoints.cs")]
    [InlineData("src/backend/GoymenERP.Api/ReconciliationHostedService.cs")]
    [InlineData("src/backend/GoymenERP.Api/ReconciliationMiddleware.cs")]
    [InlineData("src/backend/Modules/Counterparty/CounterpartyReconciliationStartup.cs")]
    [InlineData("src/backend/Modules/Counterparty/CounterpartyReconciliationService.cs")]
    [InlineData("src/backend/Modules/Counterparty/CounterpartyReconciliationEndpoint.cs")]
    public void GuardRejectsProductionStartupEndpointHostedServiceRequestPipelineAndCounterpartyReferences(
        string unreviewedPath)
    {
        var sources = CreateReviewedIdentityReconcilerFixture();
        sources.Add(
            unreviewedPath,
            "internal sealed class Probe(ISystemRolePermissionReconciler reconciler) "
            + "{ public Task Run() => reconciler.ReconcileAsync(default!); }");

        var errors = ValidateIdentityReconcilerInvocationBoundary(sources);

        Assert.Contains(
            errors,
            error => error.Contains(unreviewedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void GuardIgnoresCommentsAndStringLiteralsButRejectsExecutableUnicodeEvasion()
    {
        var harmlessSources = CreateReviewedIdentityReconcilerFixture();
        harmlessSources.Add(
            "src/backend/GoymenERP.Api/HarmlessDiagnostics.cs",
            "// ISystemRolePermissionReconciler.ReconcileAsync(plan);\n"
            + "const string Text = \"SystemRolePermissionReconciler\";");

        Assert.Empty(ValidateIdentityReconcilerInvocationBoundary(harmlessSources));

        var evasionSources = CreateReviewedIdentityReconcilerFixture();
        evasionSources.Add(
            "src/backend/GoymenERP.Api/UnicodeEvasion.cs",
            @"internal sealed class Probe(I\u0053ystemRolePermissionReconciler reconciler);");

        Assert.Contains(
            ValidateIdentityReconcilerInvocationBoundary(evasionSources),
            error => error.Contains("UnicodeEvasion.cs", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> ValidateIdentityReconcilerInvocationBoundary(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();
        var normalizedSources = sources.ToDictionary(
            pair => NormalizeRepositoryPath(pair.Key),
            pair => NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(pair.Value)),
            StringComparer.Ordinal);

        foreach (var (path, source) in normalizedSources)
        {
            var typeReferences = IdentityReconcilerTypeRegex.Matches(source);
            if (typeReferences.Count == 0)
            {
                continue;
            }

            if (!ReviewedIdentityReconcilerSources.TryGetValue(path, out var expectation))
            {
                errors.Add(
                    $"Unreviewed production Identity reconciler reference: {path}:"
                    + GetLineNumber(source, typeReferences[0].Index));
                continue;
            }

            var reconcileReferences = ReconcileMethodRegex.Matches(source);
            if (typeReferences.Count != expectation.ReconcilerTypeReferenceCount)
            {
                errors.Add(
                    $"Reviewed Identity reconciler reference count drifted in {path}. Expected "
                    + $"{expectation.ReconcilerTypeReferenceCount}, found {typeReferences.Count}.");
            }

            if (reconcileReferences.Count != expectation.ReconcileMethodReferenceCount)
            {
                errors.Add(
                    $"Identity reconciliation invocation/declaration count drifted in {path}. "
                    + $"Expected {expectation.ReconcileMethodReferenceCount}, found "
                    + $"{reconcileReferences.Count}.");
            }

            if (!expectation.RequiredShape.IsMatch(source))
            {
                errors.Add($"Reviewed Identity reconciler source shape drifted: {path}");
            }
        }

        foreach (var reviewedPath in ReviewedIdentityReconcilerSources.Keys)
        {
            if (!normalizedSources.ContainsKey(reviewedPath))
            {
                errors.Add($"Required reviewed Identity reconciler source is missing: {reviewedPath}");
            }
        }

        return errors;
    }

    private static IReadOnlyDictionary<string, string> IncludeCounterpartyProductionSources(
        string repositoryRoot,
        IReadOnlyDictionary<string, string> reviewedBackendSources)
    {
        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, source) in reviewedBackendSources
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            sources.Add(NormalizeRepositoryPath(path), source);
        }

        var counterpartyRoot = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            ExcludedCounterpartyPrefix.Replace('/', Path.DirectorySeparatorChar)));
        if (!Directory.Exists(counterpartyRoot))
        {
            throw new DirectoryNotFoundException(
                $"Identity reconciliation guard Counterparty scope is missing: "
                + ExcludedCounterpartyPrefix);
        }

        var counterpartySources = Directory.EnumerateFiles(
                counterpartyRoot,
                "*.cs",
                SearchOption.AllDirectories)
            .Select(path => new
            {
                FullPath = path,
                RepositoryPath = NormalizeRepositoryPath(
                    Path.GetRelativePath(repositoryRoot, path))
            })
            .Where(source => !IsIdentityGuardExcludedProductionPath(source.RepositoryPath))
            .OrderBy(source => source.RepositoryPath, StringComparer.Ordinal);

        foreach (var source in counterpartySources)
        {
            if (!sources.TryAdd(source.RepositoryPath, File.ReadAllText(source.FullPath)))
            {
                throw new InvalidOperationException(
                    $"Identity reconciliation guard source inventory contains a duplicate path: "
                    + source.RepositoryPath);
            }
        }

        return sources;
    }

    private static bool IsIdentityGuardExcludedProductionPath(string repositoryPath)
    {
        var segments = repositoryPath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("generated", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("tests", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> CreateReviewedIdentityReconcilerFixture() =>
        new(StringComparer.Ordinal)
        {
            [IdentityReconciliationContractPath] =
                "public interface ISystemRolePermissionReconciler "
                + "{ Task<Result> ReconcileAsync(); }",
            [IdentityReconciliationImplementationPath] =
                "internal sealed class SystemRolePermissionReconciler "
                + ": ISystemRolePermissionReconciler "
                + "{ public Task<Result> ReconcileAsync() => default!; }",
            [IdentityModuleCompositionRootPath] =
                "services.AddScoped<ISystemRolePermissionReconciler, "
                + "SystemRolePermissionReconciler>();"
        };

    private sealed record ReconcilerSourceExpectation(
        int ReconcilerTypeReferenceCount,
        int ReconcileMethodReferenceCount,
        Regex RequiredShape);
}
