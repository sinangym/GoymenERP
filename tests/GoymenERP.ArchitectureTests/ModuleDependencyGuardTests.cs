using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace GoymenERP.ArchitectureTests;

public sealed class ModuleDependencyGuardTests
{
    private const string AllowlistPath =
        "docs/architecture/MODULE_DEPENDENCY_ALLOWLIST.json";

    [Fact]
    public void Restricted_cross_module_project_references_match_exact_allowlist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var modulesRoot = Path.Combine(repositoryRoot, "src", "backend", "Modules");
        var actualEdges = ReadRestrictedCrossModuleEdges(repositoryRoot, modulesRoot);
        var allowlist = ReadAllowlist(repositoryRoot);

        Assert.Equal(1, allowlist.SchemaVersion);
        Assert.NotEmpty(allowlist.Edges);

        var allowedEdges = new HashSet<DependencyEdge>();
        var validationErrors = new List<string>();

        foreach (var entry in allowlist.Edges)
        {
            var edge = new DependencyEdge(
                NormalizeRepositoryPath(entry.Source),
                NormalizeRepositoryPath(entry.Target));

            if (!allowedEdges.Add(edge))
            {
                validationErrors.Add($"Duplicate allowlist edge: {edge}");
            }

            ValidateRepositoryFile(repositoryRoot, edge.Source, "source", validationErrors);
            ValidateRepositoryFile(repositoryRoot, edge.Target, "target", validationErrors);

            var adr = NormalizeRepositoryPath(entry.Adr);
            ValidateRepositoryFile(repositoryRoot, adr, "ADR", validationErrors);

            if (string.IsNullOrWhiteSpace(entry.Reason))
            {
                validationErrors.Add($"Allowlist edge has no reason: {edge}");
            }
        }

        validationErrors.AddRange(ValidateDependencyGraph(actualEdges, allowedEdges));

        Assert.True(validationErrors.Count == 0, string.Join("\n\n", validationErrors));
    }

    [Fact]
    public void Unlisted_cross_module_infrastructure_reference_is_rejected()
    {
        var actualEdges = new[]
        {
            new DependencyEdge(
                "Modules/Sales/Sales.Infrastructure.csproj",
                "Modules/Stock/Stock.Infrastructure.csproj")
        };

        var validationErrors = ValidateDependencyGraph(actualEdges, Array.Empty<DependencyEdge>());

        Assert.Contains(
            "Unauthorized restricted cross-module project references",
            validationErrors.Single());
    }

    [Theory]
    [InlineData("GoymenERP.Modules.Stock.Application.csproj", false)]
    [InlineData("GoymenERP.Modules.Stock.Domain.csproj", true)]
    [InlineData("GoymenERP.Modules.Stock.Infrastructure.csproj", true)]
    public void Only_domain_and_infrastructure_targets_require_an_explicit_allowlist(
        string targetProject,
        bool expected)
    {
        Assert.Equal(expected, IsRestrictedCrossModuleTarget(targetProject));
    }

    [Fact]
    public void Stale_allowlist_entry_is_rejected()
    {
        var staleEdge = new DependencyEdge(
            "Modules/Organization/Organization.csproj",
            "Modules/Audit/Audit.csproj");

        var validationErrors = ValidateDependencyGraph(
            Array.Empty<DependencyEdge>(),
            new[] { staleEdge });

        Assert.Contains("Stale allowlist entries", validationErrors.Single());
    }

    private static IReadOnlyList<string> ValidateDependencyGraph(
        IEnumerable<DependencyEdge> actualEdges,
        IEnumerable<DependencyEdge> allowedEdges)
    {
        var actual = actualEdges.ToHashSet();
        var allowed = allowedEdges.ToHashSet();
        var validationErrors = new List<string>();
        var unauthorizedEdges = actual.Except(allowed).Order().ToArray();
        var staleEdges = allowed.Except(actual).Order().ToArray();

        if (unauthorizedEdges.Length > 0)
        {
            validationErrors.Add(
                "Unauthorized restricted cross-module project references:\n"
                + string.Join("\n", unauthorizedEdges.Select(edge => $"- {edge}")));
        }

        if (staleEdges.Length > 0)
        {
            validationErrors.Add(
                "Stale allowlist entries:\n"
                + string.Join("\n", staleEdges.Select(edge => $"- {edge}")));
        }

        return validationErrors;
    }

    private static SortedSet<DependencyEdge> ReadRestrictedCrossModuleEdges(
        string repositoryRoot,
        string modulesRoot)
    {
        var edges = new SortedSet<DependencyEdge>();

        foreach (var projectPath in Directory.EnumerateFiles(
                     modulesRoot,
                     "*.csproj",
                     SearchOption.AllDirectories))
        {
            var sourceModule = GetModuleName(modulesRoot, projectPath);
            var document = XDocument.Load(projectPath);

            foreach (var reference in document.Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                var platformInclude = include
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                var targetPath = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(projectPath)!, platformInclude));

                if (!IsUnderDirectory(modulesRoot, targetPath))
                {
                    continue;
                }

                var targetModule = GetModuleName(modulesRoot, targetPath);
                if (string.Equals(sourceModule, targetModule, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!IsRestrictedCrossModuleTarget(targetPath))
                {
                    continue;
                }

                edges.Add(new DependencyEdge(
                    ToRepositoryPath(repositoryRoot, projectPath),
                    ToRepositoryPath(repositoryRoot, targetPath)));
            }
        }

        return edges;
    }

    private static bool IsRestrictedCrossModuleTarget(string targetProjectPath)
    {
        var projectName = Path.GetFileNameWithoutExtension(targetProjectPath);
        return projectName.EndsWith(".Domain", StringComparison.Ordinal)
               || projectName.EndsWith(".Infrastructure", StringComparison.Ordinal);
    }

    private static DependencyAllowlist ReadAllowlist(string repositoryRoot)
    {
        var path = Path.Combine(
            repositoryRoot,
            AllowlistPath.Replace('/', Path.DirectorySeparatorChar));
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<DependencyAllowlist>(json)
               ?? throw new InvalidOperationException("Module dependency allowlist is empty.");
    }

    private static void ValidateRepositoryFile(
        string repositoryRoot,
        string repositoryPath,
        string kind,
        ICollection<string> validationErrors)
    {
        var fullPath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            repositoryPath.Replace('/', Path.DirectorySeparatorChar)));

        if (!IsUnderDirectory(repositoryRoot, fullPath) || !File.Exists(fullPath))
        {
            validationErrors.Add(
                $"Allowlist {kind} path is missing or outside the repository: {repositoryPath}");
        }
    }

    private static string GetModuleName(string modulesRoot, string projectPath)
    {
        var relativePath = Path.GetRelativePath(modulesRoot, projectPath);
        return relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries)[0];
    }

    private static bool IsUnderDirectory(string directory, string candidatePath)
    {
        var relativePath = Path.GetRelativePath(directory, candidatePath);
        return relativePath != ".."
               && !relativePath.StartsWith(
                   $"..{Path.DirectorySeparatorChar}",
                   StringComparison.Ordinal)
               && !Path.IsPathRooted(relativePath);
    }

    private static string ToRepositoryPath(string repositoryRoot, string path)
    {
        return NormalizeRepositoryPath(Path.GetRelativePath(repositoryRoot, path));
    }

    private static string NormalizeRepositoryPath(string path)
    {
        return path.Replace('\\', '/').Trim();
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

    private sealed record DependencyEdge(string Source, string Target)
        : IComparable<DependencyEdge>
    {
        public int CompareTo(DependencyEdge? other)
        {
            if (other is null)
            {
                return 1;
            }

            var sourceComparison = string.Compare(Source, other.Source, StringComparison.Ordinal);
            return sourceComparison != 0
                ? sourceComparison
                : string.Compare(Target, other.Target, StringComparison.Ordinal);
        }

        public override string ToString() => $"{Source} -> {Target}";
    }

    private sealed record DependencyAllowlist(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("edges")] IReadOnlyList<AllowlistEntry> Edges);

    private sealed record AllowlistEntry(
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("adr")] string Adr,
        [property: JsonPropertyName("reason")] string Reason);
}
