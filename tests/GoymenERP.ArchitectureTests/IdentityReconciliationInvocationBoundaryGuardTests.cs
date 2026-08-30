using System.Security.Cryptography;
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

    // Pinning the reviewed project bytes, while separately validating every automatic MSBuild
    // ancestor, makes compiled-input drift an explicit security review event instead of a
    // source-walk bypass.
    private static readonly IReadOnlyDictionary<string, string> ReviewedCounterpartyProjectHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Application/"
             + "GoymenERP.Modules.Counterparty.Application.csproj"] =
                "C75295E1EB46CA324EB884D55D3CD837A63028E76BE30EAF3507FF9EDDFEAD86",
            ["src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Domain/"
             + "GoymenERP.Modules.Counterparty.Domain.csproj"] =
                "2C82DAE7A6492E5DC0D99B6B5A5D1C89A4702B892F71757981E46302949D6115",
            ["src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/"
             + "GoymenERP.Modules.Counterparty.Infrastructure.csproj"] =
                "BC7347E47FDF53DFD2646E439B045E8A0F317C689BA39AF14C6C2E3B7B3CC266"
        };

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

    [Fact]
    public void GuardRejectsCounterpartyReflectionAndDynamicInvocationWithoutLiteralTypeTokens()
    {
        var sources = CreateReviewedIdentityReconcilerFixture();
        sources.Add(
            "src/backend/Modules/Counterparty/ReflectionProbe.cs",
            "var type = Type.GetType(\"GoymenERP.Identity.ISystemRolePermissionReconciler\"); "
            + "var service = provider.GetRequiredService(type!); "
            + "type!.GetMethod(\"ReconcileAsync\")!.Invoke(service, [default]);");

        var errors = ValidateIdentityReconcilerInvocationBoundary(sources);

        Assert.Contains(
            errors,
            error => error.Contains("Type.GetType", StringComparison.Ordinal)
                     || error.Contains("GetMethod", StringComparison.Ordinal));
    }

    [Fact]
    public void CounterpartyInventoryExcludesOnlySdkBuildOutputs()
    {
        Assert.True(IsIdentityGuardExcludedProductionPath(
            "src/backend/Modules/Counterparty/App/bin/Release/Generated.cs"));
        Assert.True(IsIdentityGuardExcludedProductionPath(
            "src/backend/Modules/Counterparty/App/obj/Release/Generated.cs"));
        Assert.False(IsIdentityGuardExcludedProductionPath(
            "src/backend/Modules/Counterparty/App/generated/ReconciliationStartup.cs"));
        Assert.False(IsIdentityGuardExcludedProductionPath(
            "src/backend/Modules/Counterparty/App/tests/ReconciliationProbe.cs"));
    }

    [Fact]
    public void CounterpartyProjectInventoryRejectsMissingUnexpectedAndHashDriftedProjects()
    {
        var reviewedProjects = ReviewedCounterpartyProjectHashes.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        Assert.Empty(ValidateReviewedCounterpartyProjectInventory(reviewedProjects));

        var missingProject = new Dictionary<string, string>(reviewedProjects, StringComparer.Ordinal);
        missingProject.Remove(missingProject.Keys.Order(StringComparer.Ordinal).First());
        Assert.NotEmpty(ValidateReviewedCounterpartyProjectInventory(missingProject));

        var unexpectedProject = new Dictionary<string, string>(reviewedProjects, StringComparer.Ordinal)
        {
            ["src/backend/Modules/Counterparty/Unreviewed/Unreviewed.csproj"] =
                new string('A', 64)
        };
        Assert.NotEmpty(ValidateReviewedCounterpartyProjectInventory(unexpectedProject));

        var driftedProject = new Dictionary<string, string>(reviewedProjects, StringComparer.Ordinal);
        driftedProject[driftedProject.Keys.Order(StringComparer.Ordinal).First()] = new string('B', 64);
        Assert.NotEmpty(ValidateReviewedCounterpartyProjectInventory(driftedProject));
    }

    [Fact]
    public void CounterpartyAutomaticMsBuildInputsAreFailClosed()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-identity-guard-msbuild-{Guid.NewGuid():N}");

        try
        {
            var projectDirectory = Path.Combine(
                repositoryRoot,
                "src",
                "backend",
                "Modules",
                "Counterparty",
                "Synthetic");
            Directory.CreateDirectory(projectDirectory);
            var projectPath = Path.Combine(projectDirectory, "Synthetic.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(
                projectPath + ".user",
                "<Project><Import Project=\"Injected.targets\" /></Project>");
            File.WriteAllText(
                Path.Combine(projectDirectory, "Directory.Build.targets"),
                "<Project><Target Name=\"InjectCompile\" /></Project>");
            File.WriteAllText(
                Path.Combine(projectDirectory, "Directory.Packages.props"),
                "<Project><ItemGroup><PackageReference Include=\"Generator\" /></ItemGroup></Project>");
            File.WriteAllText(
                Path.Combine(projectDirectory, "Directory.Build.props"),
                "<Project><ItemGroup><Compile Include=\"Linked.cs\" /></ItemGroup></Project>");

            var errors = ValidateCounterpartyAutomaticBuildInputs(
                repositoryRoot,
                [NormalizeRepositoryPath(Path.GetRelativePath(repositoryRoot, projectPath))]);

            Assert.Contains(errors, error => error.Contains(
                "Synthetic.csproj.user",
                StringComparison.Ordinal));
            Assert.Contains(errors, error => error.Contains(
                "Directory.Build.targets",
                StringComparison.Ordinal));
            Assert.Contains(errors, error => error.Contains(
                "Directory.Packages.props",
                StringComparison.Ordinal));
            Assert.Contains(errors, error => error.Contains(
                "Compile:",
                StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ReviewedReconcilerSourcesCannotBeRenamedOrLoseTheirRequiredShape()
    {
        var probes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IdentityReconciliationContractPath] =
                "public interface IRolePermissionSync { Task<Result> RunAsync(); }",
            [IdentityReconciliationImplementationPath] =
                "internal sealed class RolePermissionSync : IRolePermissionSync "
                + "{ public Task<Result> RunAsync() => default!; }",
            [IdentityModuleCompositionRootPath] =
                "services.AddScoped<IRolePermissionSync, RolePermissionSync>();"
        };

        foreach (var (path, replacement) in probes)
        {
            var sources = CreateReviewedIdentityReconcilerFixture();
            sources[path] = replacement;

            Assert.Contains(
                ValidateIdentityReconcilerInvocationBoundary(sources),
                error => error.Contains(path, StringComparison.Ordinal));
        }
    }

    private static IReadOnlyList<string> ValidateIdentityReconcilerInvocationBoundary(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = ValidateNoForbiddenBypassCapabilities(sources).ToList();
        var normalizedSources = sources.ToDictionary(
            pair => NormalizeRepositoryPath(pair.Key),
            pair => NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(pair.Value)),
            StringComparer.Ordinal);

        foreach (var (path, source) in normalizedSources)
        {
            var typeReferences = IdentityReconcilerTypeRegex.Matches(source);
            if (!ReviewedIdentityReconcilerSources.TryGetValue(path, out var expectation))
            {
                if (typeReferences.Count > 0)
                {
                    errors.Add(
                        $"Unreviewed production Identity reconciler reference: {path}:"
                        + GetLineNumber(source, typeReferences[0].Index));
                }

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

        var counterpartyFiles = EnumerateCounterpartyFilesFailClosed(
                repositoryRoot,
                counterpartyRoot)
            .ToArray();
        var projectHashes = counterpartyFiles
            .Where(path => string.Equals(
                Path.GetExtension(path),
                ".csproj",
                StringComparison.Ordinal))
            .ToDictionary(
                path => NormalizeRepositoryPath(Path.GetRelativePath(repositoryRoot, path)),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);
        var projectErrors = ValidateReviewedCounterpartyProjectInventory(projectHashes);
        if (projectErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, projectErrors));
        }

        var automaticBuildErrors = ValidateCounterpartyAutomaticBuildInputs(
            repositoryRoot,
            projectHashes.Keys);
        if (automaticBuildErrors.Count > 0)
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, automaticBuildErrors));
        }

        var counterpartySources = counterpartyFiles
            .Where(path => string.Equals(
                Path.GetExtension(path),
                ".cs",
                StringComparison.OrdinalIgnoreCase))
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
            if (!string.Equals(
                    Path.GetExtension(source.FullPath),
                    ".cs",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Identity reconciliation guard found a non-canonical Counterparty C# "
                    + $"extension: {source.RepositoryPath}");
            }

            if (!sources.TryAdd(source.RepositoryPath, File.ReadAllText(source.FullPath)))
            {
                throw new InvalidOperationException(
                    $"Identity reconciliation guard source inventory contains a duplicate path: "
                    + source.RepositoryPath);
            }
        }

        return sources;
    }

    private static IReadOnlyList<string> ValidateReviewedCounterpartyProjectInventory(
        IReadOnlyDictionary<string, string> projectHashes)
    {
        var errors = new List<string>();
        foreach (var (path, expectedHash) in ReviewedCounterpartyProjectHashes)
        {
            if (!projectHashes.TryGetValue(path, out var actualHash))
            {
                errors.Add($"Reviewed Counterparty project is missing: {path}");
                continue;
            }

            if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            {
                errors.Add(
                    $"Reviewed Counterparty project hash drifted: {path}. "
                    + $"Expected {expectedHash}, found {actualHash}.");
            }
        }

        foreach (var path in projectHashes.Keys)
        {
            if (!ReviewedCounterpartyProjectHashes.ContainsKey(path))
            {
                errors.Add($"Unreviewed Counterparty project can change compiled inputs: {path}");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateCounterpartyAutomaticBuildInputs(
        string repositoryRoot,
        IEnumerable<string> repositoryProjectPaths)
    {
        var errors = new List<string>();
        var automaticDefinitionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var repositoryProjectPath in repositoryProjectPaths.Order(StringComparer.Ordinal))
        {
            var projectPath = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                repositoryProjectPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsUnderDirectory(repositoryRoot, projectPath) || !File.Exists(projectPath))
            {
                errors.Add(
                    "Counterparty project is missing or outside the repository while collecting "
                    + $"automatic MSBuild inputs: {repositoryProjectPath}");
                continue;
            }

            CollectAutomaticBuildDefinitionPaths(
                repositoryRoot,
                projectPath,
                automaticDefinitionPaths,
                errors);
        }

        ValidateAutomaticBuildDefinitions(
            repositoryRoot,
            automaticDefinitionPaths,
            errors);
        return errors;
    }

    private static IEnumerable<string> EnumerateCounterpartyFilesFailClosed(
        string repositoryRoot,
        string counterpartyRoot)
    {
        var pending = new Stack<string>();
        pending.Push(counterpartyRoot);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var directoryPath = NormalizeRepositoryPath(
                Path.GetRelativePath(repositoryRoot, directory));
            if (!string.Equals(directory, counterpartyRoot, StringComparison.OrdinalIgnoreCase)
                && IsIdentityGuardExcludedProductionPath(directoryPath))
            {
                continue;
            }

            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "Identity reconciliation guard refuses a Counterparty reparse directory: "
                    + directoryPath);
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var repositoryPath = NormalizeRepositoryPath(
                    Path.GetRelativePath(repositoryRoot, file));
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "Identity reconciliation guard refuses a Counterparty reparse file: "
                        + repositoryPath);
                }

                yield return file;
            }

            foreach (var child in Directory.EnumerateDirectories(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
            {
                pending.Push(child);
            }
        }
    }

    private static bool IsIdentityGuardExcludedProductionPath(string repositoryPath)
    {
        return IsBuildOutput(repositoryPath);
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
