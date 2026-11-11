namespace GoymenERP.ArchitectureTests;

/// <summary>
/// security.SecurityEvents is append-only and written by exactly one path: SecurityEventWriter ->
/// ISecurityEventStore -> SecurityEventsDbContext.AppendAsync. These facts pin that path so a new
/// caller (another module, an endpoint, a second store) cannot write the table without review.
/// </summary>
public sealed class SecurityEventsWriteOwnershipGuardTests
{
    private const string ModuleRoot = "src/backend/Modules/SecurityEvents/";
    private const string InfrastructureRoot =
        ModuleRoot + "GoymenERP.Modules.SecurityEvents.Infrastructure/";
    private const string MigrationsRoot = InfrastructureRoot + "Persistence/Migrations/";
    private const string DbContextPath = InfrastructureRoot + "Persistence/SecurityEventsDbContext.cs";
    private const string ModelBuilderPath =
        InfrastructureRoot + "Persistence/SecurityEventModelBuilderExtensions.cs";
    private const string DesignTimeFactoryPath =
        InfrastructureRoot + "Persistence/SecurityEventsDesignTimeDbContextFactory.cs";
    private const string ModuleExtensionsPath = InfrastructureRoot + "SecurityEventsModuleExtensions.cs";
    private const string StorePath = InfrastructureRoot + "Writing/SecurityEventStore.cs";
    private const string WriterPath = InfrastructureRoot + "Writing/SecurityEventWriter.cs";
    private const string QueryServicePath = InfrastructureRoot + "Reading/SecurityEventQueryService.cs";

    [Fact]
    public void SecurityEventsDbContext_IsReferencedOnlyByReviewedModuleFiles()
    {
        Assert.Equal(
            new[] { DbContextPath, DesignTimeFactoryPath, StorePath, ModuleExtensionsPath, QueryServicePath }
                .Order(StringComparer.Ordinal),
            FindProductionSourcesContaining("SecurityEventsDbContext")
                .Where(path => !path.StartsWith(MigrationsRoot, StringComparison.Ordinal)));
    }

    [Fact]
    public void SecurityEventsDbContext_AppendIsCalledOnlyByTheStore()
    {
        Assert.Equal(
            new[] { StorePath },
            FindProductionSourcesContaining(".AppendAsync(securityEvent"));
        Assert.Equal(
            new[] { DbContextPath },
            FindProductionSourcesContaining("SecurityEvents.Add("));
    }

    [Fact]
    public void SecurityEventMapping_IsAppliedOnlyByTheModuleDbContext()
    {
        // A second same-transaction writer (ADR-SEC-001 decision 2) maps this configuration with
        // ExcludeFromMigrations in its own DbContext; adding it must extend this allowlist in review.
        Assert.Equal(
            new[] { DbContextPath },
            FindProductionSourcesContaining(".ConfigureSecurityEvent("));
    }

    [Fact]
    public void SecurityEventStore_IsUsedOnlyByTheWriterAndItsRegistration()
    {
        Assert.Equal(
            new[] { ModuleExtensionsPath, StorePath, WriterPath }.Order(StringComparer.Ordinal),
            FindProductionSourcesContaining("ISecurityEventStore"));
    }

    [Fact]
    public void SecurityEventsTableName_IsMappedOnlyByTheModelBuilderAndMigrations()
    {
        Assert.Equal(
            new[] { ModelBuilderPath },
            FindProductionSourcesContaining("\"SecurityEvents\"")
                .Where(path => !path.StartsWith(MigrationsRoot, StringComparison.Ordinal)));
    }

    [Fact]
    public void SecurityEventsRead_IsServedOnlyByTheModuleQueryServiceAndOneEndpoint()
    {
        // ADR-SEC-001 section 5: the only read of the channel is the query service, which records
        // security.events.read before it reads. A second reader must extend this allowlist in review.
        Assert.Equal(
            new[] { QueryServicePath },
            FindProductionSourcesContaining(".SecurityEvents.AsNoTracking("));
        Assert.Equal(
            new[] { "src/backend/GoymenERP.Api/Endpoints/SecurityEventsEndpoints.cs", ModuleExtensionsPath, QueryServicePath }
                .Order(StringComparer.Ordinal),
            FindProductionSourcesContaining("ISecurityEventQueryServiceV1")
                .Where(path => !path.EndsWith("/Reading/SecurityEventQueryContractsV1.cs", StringComparison.Ordinal)));
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), QueryServicePath));
        Assert.True(
            source.IndexOf("RecordReadAsync(companyId", StringComparison.Ordinal)
                < source.IndexOf(".ToListAsync(", StringComparison.Ordinal),
            "The read must be recorded before the rows are read.");
    }

    [Theory]
    [InlineData("SecurityEventRecord")]
    [InlineData("SecurityEventsDbContext")]
    [InlineData("ISecurityEventStore")]
    public void SecurityEventsPersistenceTypes_DoNotLeaveTheModule(string typeName)
    {
        Assert.DoesNotContain(
            FindProductionSourcesContaining(typeName),
            path => !path.StartsWith(ModuleRoot, StringComparison.Ordinal));
    }

    [Fact]
    public void SecurityEventsDbContext_RefusesOrdinarySaveChanges()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), DbContextPath));
        Assert.Contains("internal async Task AppendAsync(", source, StringComparison.Ordinal);
        Assert.Contains("public override int SaveChanges()", source, StringComparison.Ordinal);
        Assert.Contains("public override int SaveChanges(bool acceptAllChangesOnSuccess)", source, StringComparison.Ordinal);
        Assert.Contains(
            "public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteUpdate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteDelete", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityEventsModule_HasNoBulkOrRawSqlWritePaths()
    {
        var repositoryRoot = FindRepositoryRoot();
        var offenders = FindProductionSourcesUnder(repositoryRoot, ModuleRoot)
            .Where(path => !path.StartsWith(MigrationsRoot, StringComparison.Ordinal))
            .Where(path =>
            {
                var source = File.ReadAllText(Path.Combine(repositoryRoot, path));
                return source.Contains("ExecuteUpdate", StringComparison.Ordinal)
                    || source.Contains("ExecuteDelete", StringComparison.Ordinal)
                    || source.Contains("ExecuteSql", StringComparison.Ordinal)
                    || source.Contains("FromSql", StringComparison.Ordinal)
                    || source.Contains(".Remove(", StringComparison.Ordinal)
                    || source.Contains(".RemoveRange(", StringComparison.Ordinal)
                    || source.Contains(".Update(", StringComparison.Ordinal)
                    || source.Contains(".UpdateRange(", StringComparison.Ordinal)
                    || source.Contains(".Attach(", StringComparison.Ordinal);
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
