using System.Text.RegularExpressions;

namespace GoymenERP.ArchitectureTests;

public sealed partial class PlatformTenantQueryFilterBypassGuardTests
{
    private const string AuditDbContextPath =
        "src/backend/Modules/Audit/"
        + "GoymenERP.Modules.Audit.Infrastructure/Persistence/AuditDbContext.cs";
    private const string OrganizationDbContextAuditEventPath =
        "src/backend/Modules/Organization/"
        + "GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationDbContext.cs";
    private const string CounterpartyDbContextAuditEventPath =
        "src/backend/Modules/Counterparty/"
        + "GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyDbContext.cs";
    private const string CompanyContextRegistrationPath =
        "src/backend/GoymenERP.Api/Program.cs";
    private const string SafeAuditEventConfiguration =
        "modelBuilder.Entity<AuditEvent>()\n"
        + "            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);";

    private static readonly IReadOnlySet<string> ReviewedAuditEventDbContextPaths =
        new HashSet<string>(StringComparer.Ordinal)
        {
            AuditDbContextPath,
            OrganizationDbContextAuditEventPath,
            CounterpartyDbContextAuditEventPath,
        };

    [Fact]
    public void Audit_event_context_inventory_is_complete()
    {
        var repositoryRoot = FindRepositoryRoot();
        var actualPaths = ReadBackendSources(repositoryRoot)
            .Where(file => Regex.Replace(
                    MaskCommentsAndNonInterpolatedLiterals(file.Value),
                    @"\s+",
                    string.Empty)
                .Contains(
                    "modelBuilder.ConfigureAuditEvent(",
                    StringComparison.Ordinal))
            .Select(file => file.Key)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            ReviewedAuditEventDbContextPaths.SetEquals(actualPaths),
            "AuditEvent DbContext inventory changed. Expected: "
            + string.Join(", ", ReviewedAuditEventDbContextPaths.Order(StringComparer.Ordinal))
            + "; actual: "
            + string.Join(", ", actualPaths.Order(StringComparer.Ordinal))
            + ". Every AuditEvent-owning context must receive a semantic tenant-filter review.");
    }

    [Theory]
    [InlineData(AuditDbContextPath)]
    [InlineData(OrganizationDbContextAuditEventPath)]
    [InlineData(CounterpartyDbContextAuditEventPath)]
    public void Audit_event_company_filter_is_semantically_locked(string sourcePath)
    {
        var source = ReadRepositorySource(sourcePath);

        var errors = ValidateAuditEventTenantFilter(source);

        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Theory]
    [InlineData(
        AuditDbContextPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => true")]
    [InlineData(
        OrganizationDbContextAuditEventPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => true")]
    [InlineData(
        CounterpartyDbContextAuditEventPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => true")]
    [InlineData(
        AuditDbContextPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => x.CompanyId != companyContext.CompanyId")]
    [InlineData(
        OrganizationDbContextAuditEventPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => x.CompanyId != companyContext.CompanyId")]
    [InlineData(
        CounterpartyDbContextAuditEventPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => x.CompanyId != companyContext.CompanyId")]
    [InlineData(
        AuditDbContextPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => x.CompanyId == Guid.Empty")]
    [InlineData(
        OrganizationDbContextAuditEventPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => x.CompanyId == Guid.Empty")]
    [InlineData(
        CounterpartyDbContextAuditEventPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => x.CompanyId == Guid.Empty")]
    [InlineData(
        AuditDbContextPath,
        ".HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);",
        "")]
    [InlineData(
        OrganizationDbContextAuditEventPath,
        ".HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);",
        "")]
    [InlineData(
        CounterpartyDbContextAuditEventPath,
        ".HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);",
        "")]
    public void Weakening_audit_event_company_filter_is_rejected(
        string sourcePath,
        string expectedFragment,
        string replacement)
    {
        var source = ReadRepositorySource(sourcePath);
        var mutated = MutateExactAuditEventConfiguration(
            source,
            expectedFragment,
            replacement);

        var errors = ValidateAuditEventTenantFilter(mutated);

        Assert.Contains(errors, error => error.Contains(
            "AuditEvent CompanyId query filter",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AuditDbContextPath)]
    [InlineData(OrganizationDbContextAuditEventPath)]
    [InlineData(CounterpartyDbContextAuditEventPath)]
    public void Adding_a_later_audit_event_filter_override_is_rejected(string sourcePath)
    {
        var source = ReadRepositorySource(sourcePath);
        Assert.Equal(1, CountAuditOccurrences(source, SafeAuditEventConfiguration));
        var mutated = source.Replace(
            SafeAuditEventConfiguration,
            SafeAuditEventConfiguration
            + "\n        modelBuilder.Entity<AuditEvent>()\n"
            + "            .HasQueryFilter(x => true);",
            StringComparison.Ordinal);
        Assert.NotEqual(source, mutated);
        Assert.Equal(
            2,
            CountAuditOccurrences(mutated, "modelBuilder.Entity<AuditEvent>()"));

        var errors = ValidateAuditEventTenantFilter(mutated);

        Assert.Contains(errors, error => error.Contains(
            "exactly one AuditEvent entity configuration",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AuditDbContextPath)]
    [InlineData(OrganizationDbContextAuditEventPath)]
    [InlineData(CounterpartyDbContextAuditEventPath)]
    public void Removing_request_scoped_company_context_from_audit_context_is_rejected(
        string sourcePath)
    {
        var source = ReadRepositorySource(sourcePath);
        const string expected = "ICompanyContext companyContext";
        const string replacement = "CompanyContext companyContext";
        Assert.Equal(1, CountAuditOccurrences(source, expected));
        var mutated = source.Replace(expected, replacement, StringComparison.Ordinal);
        Assert.NotEqual(source, mutated);
        Assert.Equal(0, CountAuditOccurrences(mutated, expected));

        var errors = ValidateAuditEventTenantFilter(mutated);

        Assert.Contains(errors, error => error.Contains(
            "request-scoped ICompanyContext",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Company_context_runtime_registration_is_scoped_and_unique()
    {
        var repositoryRoot = FindRepositoryRoot();
        var errors = ValidateCompanyContextRuntimeRegistration(
            ReadBackendSources(repositoryRoot));

        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Theory]
    [InlineData(
        "builder.Services.AddScoped<ICompanyContext, CompanyContext>();",
        "builder.Services.AddSingleton<ICompanyContext, CompanyContext>();")]
    [InlineData(
        "builder.Services.AddScoped<ICompanyContext, CompanyContext>();",
        "builder.Services.AddTransient<ICompanyContext, CompanyContext>();")]
    [InlineData(
        "builder.Services.AddScoped<ICompanyContext, CompanyContext>();",
        "")]
    public void Weakening_company_context_runtime_lifetime_is_rejected(
        string expected,
        string replacement)
    {
        var repositoryRoot = FindRepositoryRoot();
        var sources = ReadBackendSources(repositoryRoot);
        var programSource = sources[CompanyContextRegistrationPath];
        Assert.Equal(1, CountAuditOccurrences(programSource, expected));
        var mutated = programSource.Replace(expected, replacement, StringComparison.Ordinal);
        Assert.NotEqual(programSource, mutated);
        sources[CompanyContextRegistrationPath] = mutated;

        var errors = ValidateCompanyContextRuntimeRegistration(sources);

        Assert.Contains(errors, error => error.Contains(
            "request-scoped ICompanyContext runtime registration",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Adding_a_later_company_context_lifetime_override_is_rejected()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sources = ReadBackendSources(repositoryRoot);
        var programSource = sources[CompanyContextRegistrationPath];
        const string safeRegistration =
            "builder.Services.AddScoped<ICompanyContext, CompanyContext>();";
        Assert.Equal(1, CountAuditOccurrences(programSource, safeRegistration));
        var mutated = programSource.Replace(
            safeRegistration,
            safeRegistration
            + "\nbuilder.Services.AddSingleton<ICompanyContext, CompanyContext>();",
            StringComparison.Ordinal);
        Assert.NotEqual(programSource, mutated);
        sources[CompanyContextRegistrationPath] = mutated;

        var errors = ValidateCompanyContextRuntimeRegistration(sources);

        Assert.Contains(errors, error => error.Contains(
            "exactly one ICompanyContext runtime registration",
            StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> ValidateAuditEventTenantFilter(string source)
    {
        var errors = new List<string>();
        var searchable = MaskCommentsAndNonInterpolatedLiterals(source);
        var compact = Regex.Replace(searchable, @"\s+", string.Empty);
        const string expectedCompanyContext = "ICompanyContextcompanyContext";
        var companyContextCount = CountAuditOccurrences(compact, expectedCompanyContext);
        if (companyContextCount != 1)
        {
            errors.Add(
                "An AuditEvent-owning DbContext must receive the request-scoped "
                + "ICompanyContext through its constructor exactly once; found "
                + companyContextCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }

        const string expectedFilter =
            "modelBuilder.Entity<AuditEvent>()"
            + ".HasQueryFilter(x=>x.CompanyId==companyContext.CompanyId);";
        var filterCount = CountAuditOccurrences(compact, expectedFilter);
        if (filterCount != 1)
        {
            errors.Add(
                "The reviewed DbContext must contain exactly one AuditEvent CompanyId "
                + "query filter; found "
                + filterCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }

        const string entityConfiguration = "modelBuilder.Entity<AuditEvent>()";
        var entityConfigurationCount = CountAuditOccurrences(compact, entityConfiguration);
        if (entityConfigurationCount != 1)
        {
            errors.Add(
                "The reviewed DbContext must contain exactly one AuditEvent entity "
                + "configuration so a later query-filter override cannot hide behind a safe "
                + "declaration; found "
                + entityConfigurationCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateCompanyContextRuntimeRegistration(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();
        const string expectedRegistration =
            ".AddScoped<ICompanyContext,CompanyContext>();";
        var expectedCount = 0;
        var registrationCount = 0;
        var registrationPattern = new Regex(
            @"\.Add(?:Scoped|Singleton|Transient)<ICompanyContext(?:,CompanyContext)?>\(",
            RegexOptions.CultureInvariant);

        foreach (var source in sources.Values)
        {
            var compact = Regex.Replace(
                MaskCommentsAndNonInterpolatedLiterals(source),
                @"\s+",
                string.Empty);
            expectedCount += CountAuditOccurrences(compact, expectedRegistration);
            registrationCount += registrationPattern.Matches(compact).Count;
        }

        if (expectedCount != 1)
        {
            errors.Add(
                "The application must contain exactly one request-scoped ICompanyContext "
                + "runtime registration; found "
                + expectedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }

        if (registrationCount != 1)
        {
            errors.Add(
                "The application must contain exactly one ICompanyContext runtime "
                + "registration so a later lifetime override cannot replace the scoped "
                + "registration; found "
                + registrationCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }

        return errors;
    }

    private static string ReadRepositorySource(string sourcePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            repositoryRoot,
            sourcePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static Dictionary<string, string> ReadBackendSources(string repositoryRoot)
    {
        var backendRoot = Path.Combine(repositoryRoot, "src", "backend");
        return Directory.EnumerateFiles(backendRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsAuditBuildOutput(path))
            .ToDictionary(
                path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                File.ReadAllText,
                StringComparer.Ordinal);
    }

    private static bool IsAuditBuildOutput(string path)
    {
        var segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            || segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static string MutateExactAuditEventConfiguration(
        string source,
        string expectedFragment,
        string replacement)
    {
        Assert.Equal(1, CountAuditOccurrences(source, SafeAuditEventConfiguration));
        Assert.Equal(1, CountAuditOccurrences(SafeAuditEventConfiguration, expectedFragment));
        var mutatedConfiguration = SafeAuditEventConfiguration.Replace(
            expectedFragment,
            replacement,
            StringComparison.Ordinal);
        Assert.NotEqual(SafeAuditEventConfiguration, mutatedConfiguration);
        var mutated = source.Replace(
            SafeAuditEventConfiguration,
            mutatedConfiguration,
            StringComparison.Ordinal);
        Assert.NotEqual(source, mutated);
        Assert.Equal(0, CountAuditOccurrences(mutated, SafeAuditEventConfiguration));
        return mutated;
    }

    private static int CountAuditOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
