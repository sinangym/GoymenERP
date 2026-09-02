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

    private static readonly IReadOnlyDictionary<string, string> ReviewedAuditEventDbContexts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AuditDbContextPath] = "AuditDbContext",
            [OrganizationDbContextAuditEventPath] = "OrganizationDbContext",
            [CounterpartyDbContextAuditEventPath] = "CounterpartyDbContext",
        };

    [Fact]
    public void Audit_event_context_inventory_is_complete()
    {
        var repositoryRoot = FindRepositoryRoot();
        var actualPaths = DiscoverAuditEventDbContextPaths(
            ReadBackendSources(repositoryRoot));

        Assert.True(
            ReviewedAuditEventDbContexts.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(actualPaths),
            "AuditEvent DbContext inventory changed. Expected: "
            + string.Join(", ", ReviewedAuditEventDbContexts.Keys.Order(StringComparer.Ordinal))
            + "; actual: "
            + string.Join(", ", actualPaths.Order(StringComparer.Ordinal))
            + ". Every AuditEvent-owning context must receive a semantic tenant-filter review.");
    }

    [Theory]
    [InlineData(AuditDbContextPath)]
    [InlineData(OrganizationDbContextAuditEventPath)]
    [InlineData(CounterpartyDbContextAuditEventPath)]
    public void Audit_event_context_inventory_is_receiver_name_independent(string sourcePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var sources = ReadBackendSources(repositoryRoot);
        var source = sources[sourcePath];
        const string expected = "modelBuilder.ConfigureAuditEvent(";
        const string replacement = "alternateBuilder.ConfigureAuditEvent(";
        Assert.Equal(1, CountAuditOccurrences(source, expected));
        var mutated = source.Replace(expected, replacement, StringComparison.Ordinal);
        Assert.NotEqual(source, mutated);
        sources[sourcePath] = mutated;

        var actualPaths = DiscoverAuditEventDbContextPaths(sources);

        Assert.Contains(sourcePath, actualPaths);
    }

    [Theory]
    [InlineData(AuditDbContextPath)]
    [InlineData(OrganizationDbContextAuditEventPath)]
    [InlineData(CounterpartyDbContextAuditEventPath)]
    public void Audit_event_company_filter_is_semantically_locked(string sourcePath)
    {
        var source = ReadRepositorySource(sourcePath);

        var errors = ValidateAuditEventTenantFilter(sourcePath, source);

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

        var errors = ValidateAuditEventTenantFilter(sourcePath, mutated);

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

        var errors = ValidateAuditEventTenantFilter(sourcePath, mutated);

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

        var errors = ValidateAuditEventTenantFilter(sourcePath, mutated);

        Assert.Contains(errors, error => error.Contains(
            "request-scoped ICompanyContext",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AuditDbContextPath)]
    [InlineData(OrganizationDbContextAuditEventPath)]
    [InlineData(CounterpartyDbContextAuditEventPath)]
    public void Moving_company_context_interface_to_a_dummy_method_is_rejected(
        string sourcePath)
    {
        var source = ReadRepositorySource(sourcePath);
        const string expected = "ICompanyContext companyContext";
        Assert.Equal(1, CountAuditOccurrences(source, expected));
        var mutated = source.Replace(
            expected,
            "CompanyContext companyContext",
            StringComparison.Ordinal);
        var finalBraceIndex = mutated.LastIndexOf('}');
        Assert.True(finalBraceIndex >= 0);
        mutated = mutated.Insert(
            finalBraceIndex,
            "\n    private static void Dummy(ICompanyContext companyContext) { }\n");
        Assert.Equal(1, CountAuditOccurrences(mutated, expected));

        var errors = ValidateAuditEventTenantFilter(sourcePath, mutated);

        Assert.Contains(errors, error => error.Contains(
            "constructor parameter",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AuditDbContextPath)]
    [InlineData(OrganizationDbContextAuditEventPath)]
    [InlineData(CounterpartyDbContextAuditEventPath)]
    public void Safe_filter_in_a_dummy_method_cannot_hide_unsafe_on_model_creating(
        string sourcePath)
    {
        var source = ReadRepositorySource(sourcePath);
        const string expectedSignature = "OnModelCreating(ModelBuilder modelBuilder)";
        Assert.Equal(1, CountAuditOccurrences(source, expectedSignature));
        var mutated = source.Replace(
            expectedSignature,
            "OnModelCreating(ModelBuilder alternateBuilder)",
            StringComparison.Ordinal);
        mutated = mutated.Replace(
            "modelBuilder.",
            "alternateBuilder.",
            StringComparison.Ordinal);
        var alternateSafeConfiguration = SafeAuditEventConfiguration.Replace(
            "modelBuilder.",
            "alternateBuilder.",
            StringComparison.Ordinal);
        Assert.Equal(1, CountAuditOccurrences(mutated, alternateSafeConfiguration));
        mutated = mutated.Replace(
            alternateSafeConfiguration,
            alternateSafeConfiguration.Replace(
                "x => x.CompanyId == companyContext.CompanyId",
                "x => true",
                StringComparison.Ordinal),
            StringComparison.Ordinal);
        var finalBraceIndex = mutated.LastIndexOf('}');
        Assert.True(finalBraceIndex >= 0);
        mutated = mutated.Insert(
            finalBraceIndex,
            "\n    private void Dummy(ModelBuilder modelBuilder)\n"
            + "    {\n"
            + "        modelBuilder.Entity<AuditEvent>()\n"
            + "            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);\n"
            + "    }\n");
        Assert.Equal(1, CountAuditOccurrences(mutated, SafeAuditEventConfiguration));

        var errors = ValidateAuditEventTenantFilter(sourcePath, mutated);

        Assert.Contains(errors, error => error.Contains(
            "declared ModelBuilder receiver",
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

    [Theory]
    [InlineData(
        "builder.Services.AddSingleton<ICompanyContext, AlternateCompanyContext>();")]
    [InlineData(
        "builder.Services.AddSingleton(typeof(ICompanyContext), "
        + "typeof(AlternateCompanyContext));")]
    [InlineData(
        "builder.Services.Replace(ServiceDescriptor.Singleton<ICompanyContext, "
        + "AlternateCompanyContext>());")]
    [InlineData(
        "builder.Services.Add(ServiceDescriptor.Singleton(typeof(ICompanyContext), "
        + "typeof(AlternateCompanyContext)));")]
    [InlineData(
        "builder.Services.Add(new ServiceDescriptor(typeof(ICompanyContext), "
        + "typeof(AlternateCompanyContext), ServiceLifetime.Singleton));")]
    [InlineData(
        "builder.Services.Replace(ServiceDescriptor.Describe(typeof(ICompanyContext), "
        + "typeof(AlternateCompanyContext), ServiceLifetime.Singleton));")]
    [InlineData("builder.Services.RemoveAll<ICompanyContext>();")]
    [InlineData("builder.Services.RemoveAll(typeof(ICompanyContext));")]
    [InlineData(
        "ServiceCollectionServiceExtensions.AddSingleton(builder.Services, "
        + "typeof(ICompanyContext), typeof(AlternateCompanyContext));")]
    [InlineData(
        "ServiceCollectionDescriptorExtensions.RemoveAll<ICompanyContext>("
        + "builder.Services);")]
    [InlineData(
        "builder.Services.Replace(new(typeof(ICompanyContext), "
        + "typeof(AlternateCompanyContext), ServiceLifetime.Singleton));")]
    [InlineData(
        "builder.Services.TryAddSingleton<ICompanyContext, AlternateCompanyContext>();")]
    [InlineData(
        "builder.Services.TryAdd(ServiceDescriptor.Singleton<ICompanyContext, "
        + "AlternateCompanyContext>());")]
    public void Adding_a_later_company_context_lifetime_override_is_rejected(
        string unsafeRegistration)
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
            + "\n"
            + unsafeRegistration,
            StringComparison.Ordinal);
        Assert.NotEqual(programSource, mutated);
        sources[CompanyContextRegistrationPath] = mutated;

        var errors = ValidateCompanyContextRuntimeRegistration(sources);

        Assert.Contains(errors, error => error.Contains(
            "exactly one ICompanyContext runtime registration",
            StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> ValidateAuditEventTenantFilter(
        string sourcePath,
        string source)
    {
        var errors = new List<string>();
        var searchable = MaskCommentsAndNonInterpolatedLiterals(source);
        var normalized = Regex.Replace(searchable, @"\s+", " ");
        var contextTypeName = ReviewedAuditEventDbContexts[sourcePath];
        var constructorPattern = new Regex(
            @"\bclass\s+"
            + Regex.Escape(contextTypeName)
            + @"\s*\([^)]*\bICompanyContext\s+companyContext(?:\s*,|\s*\))",
            RegexOptions.CultureInvariant);
        var constructorParameterCount = constructorPattern.Matches(normalized).Count;
        if (constructorParameterCount != 1)
        {
            errors.Add(
                "An AuditEvent-owning DbContext must receive the request-scoped "
                + "ICompanyContext through its constructor parameter exactly once; found "
                + constructorParameterCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }

        if (!TryReadOnModelCreating(source, out var modelBuilderName, out var methodBody))
        {
            errors.Add(
                "The reviewed DbContext must declare exactly one conventional "
                + "OnModelCreating(ModelBuilder ...) method.");
            return errors;
        }

        var compactMethodBody = Regex.Replace(methodBody, @"\s+", string.Empty);
        var expectedFilter = modelBuilderName
            + ".Entity<AuditEvent>()"
            + ".HasQueryFilter(x=>x.CompanyId==companyContext.CompanyId);";
        var filterCount = CountAuditOccurrences(compactMethodBody, expectedFilter);
        if (filterCount != 1)
        {
            errors.Add(
                "The reviewed DbContext OnModelCreating method must contain exactly one "
                + "AuditEvent CompanyId query filter on its declared ModelBuilder receiver; "
                + "found "
                + filterCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }

        var entityConfiguration = modelBuilderName + ".Entity<AuditEvent>()";
        var entityConfigurationCount = CountAuditOccurrences(
            compactMethodBody,
            entityConfiguration);
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

        foreach (var source in sources.Values)
        {
            var compact = Regex.Replace(
                MaskCommentsAndNonInterpolatedLiterals(source),
                @"\s+",
                string.Empty);
            expectedCount += CountAuditOccurrences(compact, expectedRegistration);
            registrationCount += compact
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Count(IsCompanyContextServiceMutationStatement);
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

    private static bool IsCompanyContextServiceMutationStatement(string statement)
    {
        if (!statement.Contains("ICompanyContext", StringComparison.Ordinal))
        {
            return false;
        }

        return statement.Contains("AddScoped", StringComparison.Ordinal)
            || statement.Contains("AddSingleton", StringComparison.Ordinal)
            || statement.Contains("AddTransient", StringComparison.Ordinal)
            || statement.Contains("TryAdd", StringComparison.Ordinal)
            || statement.Contains("ServiceDescriptor", StringComparison.Ordinal)
            || statement.Contains("ServiceLifetime", StringComparison.Ordinal)
            || statement.Contains("RemoveAll", StringComparison.Ordinal)
            || statement.Contains(".Replace(", StringComparison.Ordinal);
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

    private static IReadOnlySet<string> DiscoverAuditEventDbContextPaths(
        IReadOnlyDictionary<string, string> sources)
    {
        return sources
            .Where(file => TryReadOnModelCreating(
                    file.Value,
                    out _,
                    out var methodBody)
                && Regex.IsMatch(
                    methodBody,
                    @"\.\s*ConfigureAuditEvent\s*\(",
                    RegexOptions.CultureInvariant))
            .Select(file => file.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool TryReadOnModelCreating(
        string source,
        out string modelBuilderName,
        out string methodBody)
    {
        var searchable = MaskCommentsAndNonInterpolatedLiterals(source);
        var signature = Regex.Match(
            searchable,
            @"\bprotected\s+override\s+void\s+OnModelCreating\s*\(\s*"
            + @"ModelBuilder\s+(?<receiver>[\p{L}_][\p{L}\p{Nd}_]*)\s*\)",
            RegexOptions.CultureInvariant);
        if (!signature.Success)
        {
            modelBuilderName = string.Empty;
            methodBody = string.Empty;
            return false;
        }

        var openingBrace = searchable.IndexOf('{', signature.Index + signature.Length);
        if (openingBrace < 0)
        {
            modelBuilderName = string.Empty;
            methodBody = string.Empty;
            return false;
        }

        var depth = 0;
        for (var index = openingBrace; index < searchable.Length; index++)
        {
            if (searchable[index] == '{')
            {
                depth++;
            }
            else if (searchable[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    modelBuilderName = signature.Groups["receiver"].Value;
                    methodBody = searchable[(openingBrace + 1)..index];
                    return true;
                }
            }
        }

        modelBuilderName = string.Empty;
        methodBody = string.Empty;
        return false;
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
