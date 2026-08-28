using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyMigrationArtifactContractTests
{
    private const string ContractPath =
        "src/backend/Modules/Counterparty/COUNTERPARTY_MIGRATION_ARTIFACT.json";
    private const string ExpectedProject =
        "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/GoymenERP.Modules.Counterparty.Infrastructure.csproj";
    private const string ExpectedStartupProject =
        "src/backend/GoymenERP.Api/GoymenERP.Api.csproj";
    private const string ExpectedOutput = "artifacts/counterparty-migration.sql";

    [Fact]
    public void Migration_artifact_contract_matches_runtime_context_and_migrations()
    {
        var repositoryRoot = FindRepositoryRoot();
        var contract = ReadContract(repositoryRoot);
        var errors = ValidateContract(repositoryRoot, contract).ToList();

        var options = new DbContextOptionsBuilder<CounterpartyDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpCounterpartyArtifactContract;Trusted_Connection=True;")
            .Options;
        using var dbContext = new CounterpartyDbContext(
            options,
            new UnresolvedCompanyContext());

        var actualMigrations = dbContext.Database.GetMigrations()
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var expectedMigrations = contract.MigrationIds
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!actualMigrations.SequenceEqual(expectedMigrations, StringComparer.Ordinal))
        {
            errors.Add(
                "Migration IDs do not match the owned artifact contract.\n"
                + $"Expected: [{string.Join(", ", expectedMigrations)}]\n"
                + $"Actual: [{string.Join(", ", actualMigrations)}]");
        }

        var factoryType = typeof(CounterpartyDbContext).Assembly.GetType(
            "GoymenERP.Modules.Counterparty.Infrastructure.Persistence.CounterpartyDesignTimeDbContextFactory",
            throwOnError: false,
            ignoreCase: false);
        if (factoryType is null)
        {
            errors.Add("Counterparty design-time DbContext factory is missing.");
        }
        else if (!factoryType.GetInterfaces().Any(type =>
                     type.IsGenericType
                     && string.Equals(
                         type.GetGenericTypeDefinition().FullName,
                         "Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory`1",
                         StringComparison.Ordinal)
                     && type.GenericTypeArguments.Single() == typeof(CounterpartyDbContext)))
        {
            errors.Add(
                "Counterparty design-time factory does not target CounterpartyDbContext.");
        }

        Assert.True(errors.Count == 0, string.Join("\n\n", errors));
    }

    [Fact]
    public void Contract_validation_rejects_unsafe_command_drift()
    {
        var unsafeContract = new MigrationArtifactContract(
            1,
            "CHATGPT-CARI",
            "Counterparty",
            "docs/features/CARI_MIGRATION_ARTIFACT_CONTRACT_v1.md",
            ExpectedProject,
            ExpectedStartupProject,
            "WrongDbContext",
            "Debug",
            "../counterparty.sql",
            Idempotent: false,
            NoBuild: false,
            ["wrong-migration"],
            ["audit.AuditEvents"]);

        var errors = ValidateContractDefinition(unsafeContract);

        Assert.Contains(errors, error => error.Contains("context", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("configuration", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("output", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("idempotent", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("no_build", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("migration_ids", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("excluded_sidecar_tables", StringComparison.OrdinalIgnoreCase));
    }

    private static MigrationArtifactContract ReadContract(string repositoryRoot)
    {
        var path = ToFullPath(repositoryRoot, ContractPath);
        var bytes = File.ReadAllBytes(path);
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

        return JsonSerializer.Deserialize<MigrationArtifactContract>(
                   bytes,
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = false,
                       UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                   })
               ?? throw new InvalidOperationException(
                   "Counterparty migration artifact contract is empty.");
    }

    private static IReadOnlyList<string> ValidateContract(
        string repositoryRoot,
        MigrationArtifactContract contract)
    {
        var errors = ValidateContractDefinition(contract).ToList();
        ValidateRepositoryFile(repositoryRoot, contract.ContractSource, errors);
        ValidateRepositoryFile(repositoryRoot, contract.Project, errors);
        ValidateRepositoryFile(repositoryRoot, contract.StartupProject, errors);
        return errors;
    }

    private static IReadOnlyList<string> ValidateContractDefinition(
        MigrationArtifactContract contract)
    {
        var errors = new List<string>();

        if (contract.SchemaVersion != 1)
        {
            errors.Add($"Unsupported schema_version: {contract.SchemaVersion}.");
        }

        if (!string.Equals(contract.Owner, "CHATGPT-CARI", StringComparison.Ordinal)
            || !string.Equals(contract.Module, "Counterparty", StringComparison.Ordinal))
        {
            errors.Add("Unexpected owner/module identity.");
        }

        if (!string.Equals(contract.Project, ExpectedProject, StringComparison.Ordinal))
        {
            errors.Add($"Unexpected project: {contract.Project}.");
        }

        if (!string.Equals(
                contract.StartupProject,
                ExpectedStartupProject,
                StringComparison.Ordinal))
        {
            errors.Add($"Unexpected startup_project: {contract.StartupProject}.");
        }

        if (!string.Equals(
                contract.Context,
                nameof(CounterpartyDbContext),
                StringComparison.Ordinal))
        {
            errors.Add($"Unexpected context: {contract.Context}.");
        }

        if (!string.Equals(contract.Configuration, "Release", StringComparison.Ordinal))
        {
            errors.Add($"Unexpected configuration: {contract.Configuration}.");
        }

        if (!string.Equals(contract.Output, ExpectedOutput, StringComparison.Ordinal)
            || ContainsWildcard(contract.Output)
            || Path.IsPathRooted(contract.Output)
            || contract.Output.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment == ".."))
        {
            errors.Add($"Unsafe or unexpected output: {contract.Output}.");
        }

        if (!contract.Idempotent)
        {
            errors.Add("idempotent must be true.");
        }

        if (!contract.NoBuild)
        {
            errors.Add("no_build must be true.");
        }

        var expectedMigrations = new HashSet<string>(StringComparer.Ordinal)
        {
            "20260828123500_InitialCounterparty",
            "20260828150000_AddCounterpartyWriteSupport",
        };
        if (!contract.MigrationIds.ToHashSet(StringComparer.Ordinal)
            .SetEquals(expectedMigrations))
        {
            errors.Add(
                "migration_ids must match the exact current Counterparty migration set.");
        }

        var expectedSidecars = new HashSet<string>(StringComparer.Ordinal)
        {
            "audit.AuditEvents",
            "idempotency.CommandExecutions",
        };
        if (!contract.ExcludedSidecarTables.ToHashSet(StringComparer.Ordinal)
            .SetEquals(expectedSidecars))
        {
            errors.Add(
                "excluded_sidecar_tables must contain exact Audit/Idempotency sidecars.");
        }

        return errors;
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

    private static void ValidateRepositoryFile(
        string repositoryRoot,
        string repositoryPath,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || ContainsWildcard(repositoryPath))
        {
            errors.Add($"Invalid repository file path: {repositoryPath}.");
            return;
        }

        var fullPath = ToFullPath(repositoryRoot, repositoryPath);
        var relative = Path.GetRelativePath(repositoryRoot, fullPath);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || !File.Exists(fullPath))
        {
            errors.Add($"Repository file is missing or outside root: {repositoryPath}.");
        }
    }

    private static string ToFullPath(string repositoryRoot, string repositoryPath)
    {
        return Path.GetFullPath(Path.Combine(
            repositoryRoot,
            repositoryPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool ContainsWildcard(string value)
    {
        return value.Contains('*') || value.Contains('?');
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

    private sealed record MigrationArtifactContract(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("owner")] string Owner,
        [property: JsonPropertyName("module")] string Module,
        [property: JsonPropertyName("contract_source")] string ContractSource,
        [property: JsonPropertyName("project")] string Project,
        [property: JsonPropertyName("startup_project")] string StartupProject,
        [property: JsonPropertyName("context")] string Context,
        [property: JsonPropertyName("configuration")] string Configuration,
        [property: JsonPropertyName("output")] string Output,
        [property: JsonPropertyName("idempotent")] bool Idempotent,
        [property: JsonPropertyName("no_build")] bool NoBuild,
        [property: JsonPropertyName("migration_ids")] IReadOnlyList<string> MigrationIds,
        [property: JsonPropertyName("excluded_sidecar_tables")]
        IReadOnlyList<string> ExcludedSidecarTables);

    private sealed class UnresolvedCompanyContext : ICompanyContext
    {
        public bool IsResolved => false;

        public Guid CompanyId => Guid.Empty;

        public void Set(Guid companyId) =>
            throw new InvalidOperationException(
                "Migration artifact contract test does not resolve CompanyContext.");
    }
}
