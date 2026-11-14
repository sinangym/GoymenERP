namespace GoymenERP.ArchitectureTests;

/// <summary>
/// F-023 (F023-1): a <c>Refused</c> audit row is written by exactly one path — IRefusedAttemptRecorderV1 ->
/// RefusedAttemptAuditDraftMapper (the restricted draft) -> the request's audit collector, or the standalone
/// AuditDbContext.AppendRefusedAttemptAsync. These facts keep another module from writing a refused row with free
/// text or a payload, and keep the standalone append from gaining a second caller without review.
/// </summary>
public sealed class RefusedAttemptWriteOwnershipGuardTests
{
    private const string ModuleRoot = "src/backend/Modules/Audit/";
    private const string ApplicationContractsRoot = ModuleRoot + "GoymenERP.Modules.Audit.Application/Contracts/";
    private const string InfrastructureRoot = ModuleRoot + "GoymenERP.Modules.Audit.Infrastructure/";
    private const string DbContextPath = InfrastructureRoot + "Persistence/AuditDbContext.cs";
    private const string RecorderPath = InfrastructureRoot + "Services/RefusedAttemptRecorder.cs";
    private const string MapperPath = InfrastructureRoot + "Services/RefusedAttemptAuditDraftMapper.cs";
    private const string FactoryPath = InfrastructureRoot + "Services/AuditEventFactory.cs";
    private const string ModuleExtensionsPath = InfrastructureRoot + "AuditModuleExtensions.cs";
    private const string AuditModelsPath = ApplicationContractsRoot + "AuditModels.cs";

    [Fact]
    public void The_refused_outcome_is_named_only_by_the_audit_module_draft_path()
    {
        Assert.Equal(
            new[] { DbContextPath, FactoryPath, MapperPath }.Order(StringComparer.Ordinal),
            FindProductionSourcesContaining("AuditOutcomes.Refused"));
        Assert.Equal(
            new[] { AuditModelsPath },
            FindProductionSourcesContaining("const string Refused = \"Refused\""));
    }

    [Fact]
    public void The_standalone_refused_append_is_called_only_by_the_recorder()
    {
        Assert.Equal(
            new[] { RecorderPath },
            FindProductionSourcesContaining(".AppendRefusedAttemptAsync("));
        Assert.Equal(
            new[] { DbContextPath },
            FindProductionSourcesContaining("Task AppendRefusedAttemptAsync("));
    }

    [Fact]
    public void The_restricted_draft_is_built_only_by_the_recorder()
    {
        Assert.Equal(
            new[] { RecorderPath },
            FindProductionSourcesContaining("RefusedAttemptAuditDraftMapper.ToDraft("));
        Assert.Equal(
            new[] { ModuleExtensionsPath, RecorderPath }.Order(StringComparer.Ordinal),
            FindProductionSourcesContaining("RefusedAttemptRecorder>")
                .Concat(FindProductionSourcesContaining("class RefusedAttemptRecorder("))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_refused_draft_carries_no_client_address_user_agent_or_approval_payload()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), MapperPath));
        Assert.Contains("ClientIpAddress: null", source, StringComparison.Ordinal);
        Assert.Contains("UserAgent: null", source, StringComparison.Ordinal);
        Assert.Contains("ApprovalContextJson: null", source, StringComparison.Ordinal);
        Assert.Contains("EntityVersionAfter: null", source, StringComparison.Ordinal);
        Assert.Contains("RefusedAttemptReasonTextsV1.For(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_retention_and_timeout_are_settings_and_automatic_disposal_has_no_path()
    {
        var root = FindRepositoryRoot();
        var extensions = File.ReadAllText(Path.Combine(root, ModuleExtensionsPath));
        Assert.Contains("RefusedAttemptRetentionOptions.FromConfiguration(configuration)", extensions, StringComparison.Ordinal);
        Assert.Contains("RefusedAttemptStandaloneWriteOptions.FromConfiguration(configuration)", extensions, StringComparison.Ordinal);

        var offenders = FindProductionSourcesUnder(root, ModuleRoot)
            .Where(path => !path.Contains("/Persistence/Migrations/", StringComparison.Ordinal))
            .Where(path =>
            {
                var source = File.ReadAllText(Path.Combine(root, path));
                return source.Contains("ExecuteDelete", StringComparison.Ordinal)
                    || source.Contains(".RemoveRange(", StringComparison.Ordinal)
                    || source.Contains("AuditEvents.Remove(", StringComparison.Ordinal);
            });
        Assert.Empty(offenders);
    }

    private static IReadOnlyList<string> FindProductionSourcesContaining(string value)
    {
        var repositoryRoot = FindRepositoryRoot();
        return FindProductionSourcesUnder(repositoryRoot, "src/backend/")
            .Where(path => File.ReadAllText(Path.Combine(repositoryRoot, path)).Contains(value, StringComparison.Ordinal))
            .ToArray();
    }

    private static IReadOnlyList<string> FindProductionSourcesUnder(string repositoryRoot, string relativeRoot)
    {
        var root = Path.Combine(repositoryRoot, relativeRoot);
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path, root))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsBuildArtifact(string path, string root)
    {
        var segments = Path.GetRelativePath(root, path).Split(
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
