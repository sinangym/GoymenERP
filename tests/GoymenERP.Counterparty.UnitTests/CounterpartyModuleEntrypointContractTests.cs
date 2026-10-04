using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyModuleEntrypointContractTests
{
    private const string ContractPath =
        "src/backend/Modules/Counterparty/COUNTERPARTY_MODULE_ENTRYPOINTS.json";
    private const string ExpectedOwner = "CHATGPT-CARI";
    private const string ExpectedModule = "Counterparty";
    private const string ExpectedCounterpartyPrefix =
        "src/backend/Modules/Counterparty/";
    private const string ExpectedCompositionRoot =
        "src/backend/GoymenERP.Api/GoymenERP.Api.csproj";
    private const string ExpectedManufacturingApplication =
        "src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Application/"
        + "GoymenERP.Modules.Manufacturing.Application.csproj";
    private static readonly IReadOnlySet<string> ExpectedCompositionSources =
        new HashSet<string>(StringComparer.Ordinal)
        {
            ExpectedCompositionRoot,
            ExpectedManufacturingApplication,
        };

    [Fact]
    public void Api_counterparty_project_references_match_exact_owned_contract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var contract = ReadContract(repositoryRoot);
        var errors = ValidateContractDefinition(repositoryRoot, contract).ToList();

        var actualEdges = ExpectedCompositionSources
            .SelectMany(source => ReadActualCounterpartyEdges(
                repositoryRoot,
                source,
                contract.ExcludedSourcePrefix,
                errors))
            .ToHashSet();
        var expectedEdges = contract.CompositionRootEdges
            .Select(entry => new EntrypointEdge(
                NormalizeRepositoryPath(entry.Source),
                NormalizeRepositoryPath(entry.Target)))
            .ToHashSet();

        errors.AddRange(DescribeDrift(expectedEdges, actualEdges));

        Assert.True(errors.Count == 0, string.Join("\n\n", errors));
    }

    [Fact]
    public void Drift_detection_rejects_missing_and_unapproved_edges()
    {
        var expected = new HashSet<EntrypointEdge>
        {
            new(ExpectedCompositionRoot,
                "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Application/"
                + "GoymenERP.Modules.Counterparty.Application.csproj"),
            new(ExpectedCompositionRoot,
                "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/"
                + "GoymenERP.Modules.Counterparty.Infrastructure.csproj"),
            new(ExpectedManufacturingApplication,
                "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Application/"
                + "GoymenERP.Modules.Counterparty.Application.csproj"),
        };
        var actual = new HashSet<EntrypointEdge>
        {
            expected.First(),
            expected.Skip(1).First(),
            new(ExpectedManufacturingApplication,
                "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Domain/"
                + "GoymenERP.Modules.Counterparty.Domain.csproj"),
        };

        var errors = DescribeDrift(expected, actual);

        Assert.Contains(errors, error => error.StartsWith(
            "Missing contracted Counterparty composition-root edges",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith(
            "Unapproved Counterparty composition-root edges",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Json_contract_rejects_duplicate_and_unknown_properties()
    {
        const string duplicateOwner = """
            {
              "schema_version": 1,
              "owner": "CHATGPT-CARI",
              "owner": "OTHER",
              "module": "Counterparty",
              "excluded_source_prefix": "src/backend/Modules/Counterparty/",
              "contract_source": "docs/features/CARI_MODULE_ENTRYPOINT_CONTRACT_v1.md",
              "composition_root_edges": []
            }
            """;
        Assert.Throws<InvalidOperationException>(() =>
            DeserializeStrict(System.Text.Encoding.UTF8.GetBytes(duplicateOwner)));

        const string unknownProperty = """
            {
              "schema_version": 1,
              "owner": "CHATGPT-CARI",
              "module": "Counterparty",
              "excluded_source_prefix": "src/backend/Modules/Counterparty/",
              "contract_source": "docs/features/CARI_MODULE_ENTRYPOINT_CONTRACT_v1.md",
              "composition_root_edges": [],
              "unexpected": true
            }
            """;
        Assert.Throws<JsonException>(() =>
            DeserializeStrict(System.Text.Encoding.UTF8.GetBytes(unknownProperty)));
    }

    private static EntrypointContract ReadContract(string repositoryRoot)
    {
        var path = ToFullPath(repositoryRoot, ContractPath);
        return DeserializeStrict(File.ReadAllBytes(path));
    }

    private static EntrypointContract DeserializeStrict(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });

        var duplicateErrors = new List<string>();
        FindDuplicateProperties(document.RootElement, "$", duplicateErrors);
        if (duplicateErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", duplicateErrors));
        }

        return JsonSerializer.Deserialize<EntrypointContract>(
                   bytes,
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = false,
                       UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                   })
               ?? throw new InvalidOperationException(
                   "Counterparty module entrypoint contract is empty.");
    }

    private static IReadOnlyList<string> ValidateContractDefinition(
        string repositoryRoot,
        EntrypointContract contract)
    {
        var errors = new List<string>();

        if (contract.SchemaVersion != 1)
        {
            errors.Add($"Unsupported schema_version: {contract.SchemaVersion}.");
        }

        if (!string.Equals(contract.Owner, ExpectedOwner, StringComparison.Ordinal))
        {
            errors.Add($"Unexpected contract owner: {contract.Owner}.");
        }

        if (!string.Equals(contract.Module, ExpectedModule, StringComparison.Ordinal))
        {
            errors.Add($"Unexpected contract module: {contract.Module}.");
        }

        var excludedPrefix = NormalizeRepositoryPath(contract.ExcludedSourcePrefix);
        if (!string.Equals(
                excludedPrefix,
                ExpectedCounterpartyPrefix,
                StringComparison.Ordinal))
        {
            errors.Add($"Unexpected excluded_source_prefix: {excludedPrefix}.");
        }

        ValidateRepositoryFile(
            repositoryRoot,
            NormalizeRepositoryPath(contract.ContractSource),
            "contract_source",
            errors);

        if (contract.CompositionRootEdges.Count != 3)
        {
            errors.Add(
                "The Counterparty composition-root contract must contain exactly three edges; "
                + $"found {contract.CompositionRootEdges.Count}.");
        }

        var seenEdges = new HashSet<EntrypointEdge>();
        var seenSources = new HashSet<string>(StringComparer.Ordinal);
        var seenLayers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in contract.CompositionRootEdges)
        {
            var source = NormalizeRepositoryPath(entry.Source);
            var target = NormalizeRepositoryPath(entry.Target);
            var edge = new EntrypointEdge(source, target);

            if (!seenEdges.Add(edge))
            {
                errors.Add($"Duplicate composition-root edge: {edge}.");
            }

            if (ContainsWildcard(source) || ContainsWildcard(target))
            {
                errors.Add($"Wildcard paths are forbidden: {edge}.");
            }

            if (!ExpectedCompositionSources.Contains(source))
            {
                errors.Add($"Unexpected composition-root source: {source}.");
            }
            seenSources.Add(source);

            if (!target.StartsWith(ExpectedCounterpartyPrefix, StringComparison.Ordinal))
            {
                errors.Add($"Counterparty target is outside the owned module prefix: {target}.");
            }

            seenLayers.Add(entry.Layer);

            if (!IsExpectedLayerTarget(entry.Layer, target))
            {
                errors.Add(
                    $"Layer '{entry.Layer}' does not match its exact target project: {target}.");
            }

            if (string.IsNullOrWhiteSpace(entry.Reason))
            {
                errors.Add($"Composition-root edge has no reason: {edge}.");
            }

            ValidateRepositoryFile(repositoryRoot, source, "source", errors);
            ValidateRepositoryFile(repositoryRoot, target, "target", errors);
        }

        if (!seenSources.SetEquals(ExpectedCompositionSources))
        {
            errors.Add(
                "Composition-root sources must be exactly the API and Manufacturing application projects; "
                + $"found: {string.Join(", ", seenSources.OrderBy(value => value, StringComparer.Ordinal))}.");
        }

        var expectedLayers = new HashSet<string>(StringComparer.Ordinal)
        {
            "Application",
            "Infrastructure"
        };
        if (!seenLayers.SetEquals(expectedLayers))
        {
            errors.Add(
                "Composition-root layers must be exactly Application and Infrastructure; "
                + $"found: {string.Join(", ", seenLayers.OrderBy(value => value, StringComparer.Ordinal))}.");
        }

        return errors;
    }

    private static HashSet<EntrypointEdge> ReadActualCounterpartyEdges(
        string repositoryRoot,
        string sourceRepositoryPath,
        string counterpartyPrefix,
        ICollection<string> errors)
    {
        var sourceFullPath = ToFullPath(repositoryRoot, sourceRepositoryPath);
        var sourceDirectory = Path.GetDirectoryName(sourceFullPath)
                              ?? throw new InvalidOperationException(
                                  "Composition-root project directory is missing.");
        var document = XDocument.Load(sourceFullPath);
        var edges = new HashSet<EntrypointEdge>();

        foreach (var reference in document.Descendants("ProjectReference"))
        {
            var include = reference.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
            {
                errors.Add("GoymenERP.Api contains a ProjectReference without Include.");
                continue;
            }

            var platformPath = include
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            var targetFullPath = Path.GetFullPath(
                Path.Combine(sourceDirectory, platformPath));
            var targetRepositoryPath = NormalizeRepositoryPath(
                Path.GetRelativePath(repositoryRoot, targetFullPath));

            if (!targetRepositoryPath.StartsWith(
                    NormalizeRepositoryPath(counterpartyPrefix),
                    StringComparison.Ordinal))
            {
                continue;
            }

            var edge = new EntrypointEdge(sourceRepositoryPath, targetRepositoryPath);
            if (!edges.Add(edge))
            {
                errors.Add($"Duplicate actual Counterparty ProjectReference: {edge}.");
            }
        }

        return edges;
    }

    private static IReadOnlyList<string> DescribeDrift(
        IReadOnlySet<EntrypointEdge> expected,
        IReadOnlySet<EntrypointEdge> actual)
    {
        var errors = new List<string>();
        var missing = expected.Except(actual)
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target, StringComparer.Ordinal)
            .ToArray();
        var unapproved = actual.Except(expected)
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target, StringComparer.Ordinal)
            .ToArray();

        if (missing.Length > 0)
        {
            errors.Add(
                "Missing contracted Counterparty composition-root edges:\n"
                + string.Join("\n", missing.Select(edge => $"- {edge}")));
        }

        if (unapproved.Length > 0)
        {
            errors.Add(
                "Unapproved Counterparty composition-root edges:\n"
                + string.Join("\n", unapproved.Select(edge => $"- {edge}")));
        }

        return errors;
    }

    private static bool IsExpectedLayerTarget(string layer, string target)
    {
        return layer switch
        {
            "Application" => target.EndsWith(
                "/GoymenERP.Modules.Counterparty.Application.csproj",
                StringComparison.Ordinal),
            "Infrastructure" => target.EndsWith(
                "/GoymenERP.Modules.Counterparty.Infrastructure.csproj",
                StringComparison.Ordinal),
            _ => false
        };
    }

    private static void FindDuplicateProperties(
        JsonElement element,
        string location,
        ICollection<string> errors)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    errors.Add($"Duplicate JSON property at {location}: {property.Name}.");
                }

                FindDuplicateProperties(
                    property.Value,
                    $"{location}.{property.Name}",
                    errors);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                FindDuplicateProperties(item, $"{location}[{index}]", errors);
                index++;
            }
        }
    }

    private static bool ContainsWildcard(string path)
    {
        return path.Contains('*') || path.Contains('?');
    }

    private static void ValidateRepositoryFile(
        string repositoryRoot,
        string repositoryPath,
        string kind,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            errors.Add($"Counterparty entrypoint {kind} path is empty.");
            return;
        }

        var fullPath = ToFullPath(repositoryRoot, repositoryPath);
        if (!IsUnderDirectory(repositoryRoot, fullPath) || !File.Exists(fullPath))
        {
            errors.Add(
                $"Counterparty entrypoint {kind} path is missing or outside the repository: "
                + repositoryPath);
        }
    }

    private static string ToFullPath(string repositoryRoot, string repositoryPath)
    {
        return Path.GetFullPath(Path.Combine(
            repositoryRoot,
            repositoryPath.Replace('/', Path.DirectorySeparatorChar)));
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

        throw new DirectoryNotFoundException(
            "Göymen ERP repository root was not found.");
    }

    private sealed record EntrypointEdge(string Source, string Target)
    {
        public override string ToString() => $"{Source} -> {Target}";
    }

    private sealed record EntrypointContract(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("owner")] string Owner,
        [property: JsonPropertyName("module")] string Module,
        [property: JsonPropertyName("excluded_source_prefix")] string ExcludedSourcePrefix,
        [property: JsonPropertyName("contract_source")] string ContractSource,
        [property: JsonPropertyName("composition_root_edges")]
        IReadOnlyList<EntrypointContractEntry> CompositionRootEdges);

    private sealed record EntrypointContractEntry(
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("layer")] string Layer,
        [property: JsonPropertyName("reason")] string Reason);
}
