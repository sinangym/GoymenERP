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
    private const string InventoryDbContextAuditEventPath =
        "src/backend/Modules/Inventory/"
        + "GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryDbContext.cs";
    private const string AccountingDbContextAuditEventPath =
        "src/backend/Modules/Accounting/"
        + "GoymenERP.Modules.Accounting.Infrastructure/Persistence/AccountingDbContext.cs";
    private const string FinanceDbContextAuditEventPath =
        "src/backend/Modules/Finance/"
        + "GoymenERP.Modules.Finance.Infrastructure/Persistence/FinanceDbContext.cs";
    private const string ManufacturingDbContextAuditEventPath =
        "src/backend/Modules/Manufacturing/"
        + "GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/ManufacturingDbContext.cs";
    private const string SalesDbContextAuditEventPath =
        "src/backend/Modules/Sales/"
        + "GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs";
    private const string PurchasingDbContextAuditEventPath =
        "src/backend/Modules/Purchasing/"
        + "GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingDbContext.cs";
    private const string CompanyContextRegistrationPath =
        "src/backend/GoymenERP.Api/Program.cs";
    private const string SafeAuditEventConfiguration =
        "modelBuilder.Entity<AuditEvent>()\n"
        + "            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);";
    private const string SafeProductAuditLambda =
        "x => groupSharedProductId.HasValue\n"
        + "                ? x.SubjectKind == AuditEvent.GroupSharedProductSubject && x.EntityType == AuditEvent.GroupSharedProductEntityType && x.EntityId == QueryProductId\n"
        + "                : x.SubjectKind == AuditEvent.CompanySubject && x.CompanyId == QueryCompanyId";
    private const string SafeProductAuditEventConfiguration =
        "modelBuilder.Entity<AuditEvent>()\n            .HasQueryFilter(" + SafeProductAuditLambda + ");";

    private static string ExpectedAuditConfiguration(string sourcePath) => sourcePath == AuditDbContextPath
        ? SafeProductAuditEventConfiguration : SafeAuditEventConfiguration;
    private static string ExpectedAuditLambda(string sourcePath) => sourcePath == AuditDbContextPath
        ? SafeProductAuditLambda : "x => x.CompanyId == companyContext.CompanyId";

    private static readonly IReadOnlyDictionary<string, string> ReviewedAuditEventDbContexts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AuditDbContextPath] = "AuditDbContext",
            [OrganizationDbContextAuditEventPath] = "OrganizationDbContext",
            [CounterpartyDbContextAuditEventPath] = "CounterpartyDbContext",
            [InventoryDbContextAuditEventPath] = "InventoryDbContext",
            [AccountingDbContextAuditEventPath] = "AccountingDbContext",
            [FinanceDbContextAuditEventPath] = "FinanceDbContext",
            [ManufacturingDbContextAuditEventPath] = "ManufacturingDbContext",
            [SalesDbContextAuditEventPath] = "SalesDbContext",
            [PurchasingDbContextAuditEventPath] = "PurchasingDbContext",
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
    [InlineData(InventoryDbContextAuditEventPath)]
    [InlineData(AccountingDbContextAuditEventPath)]
    [InlineData(FinanceDbContextAuditEventPath)]
    [InlineData(ManufacturingDbContextAuditEventPath)]
    [InlineData(SalesDbContextAuditEventPath)]
    [InlineData(PurchasingDbContextAuditEventPath)]
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
    [InlineData(InventoryDbContextAuditEventPath)]
    [InlineData(AccountingDbContextAuditEventPath)]
    [InlineData(FinanceDbContextAuditEventPath)]
    [InlineData(ManufacturingDbContextAuditEventPath)]
    [InlineData(SalesDbContextAuditEventPath)]
    [InlineData(PurchasingDbContextAuditEventPath)]
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
    [InlineData(InventoryDbContextAuditEventPath)]
    [InlineData(AccountingDbContextAuditEventPath)]
    [InlineData(FinanceDbContextAuditEventPath)]
    [InlineData(ManufacturingDbContextAuditEventPath)]
    [InlineData(SalesDbContextAuditEventPath)]
    [InlineData(PurchasingDbContextAuditEventPath)]
    public void Adding_a_later_audit_event_filter_override_is_rejected(string sourcePath)
    {
        var source = ReadRepositorySource(sourcePath);
        var safeConfiguration = ExpectedAuditConfiguration(sourcePath);
        Assert.Equal(1, CountAuditOccurrences(source, safeConfiguration));
        var mutated = source.Replace(
            safeConfiguration,
            safeConfiguration
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
    [InlineData(InventoryDbContextAuditEventPath)]
    [InlineData(AccountingDbContextAuditEventPath)]
    [InlineData(FinanceDbContextAuditEventPath)]
    [InlineData(ManufacturingDbContextAuditEventPath)]
    [InlineData(SalesDbContextAuditEventPath)]
    [InlineData(PurchasingDbContextAuditEventPath)]
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
    [InlineData(InventoryDbContextAuditEventPath)]
    [InlineData(AccountingDbContextAuditEventPath)]
    [InlineData(FinanceDbContextAuditEventPath)]
    [InlineData(ManufacturingDbContextAuditEventPath)]
    [InlineData(SalesDbContextAuditEventPath)]
    [InlineData(PurchasingDbContextAuditEventPath)]
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
    [InlineData(InventoryDbContextAuditEventPath)]
    [InlineData(AccountingDbContextAuditEventPath)]
    [InlineData(FinanceDbContextAuditEventPath)]
    [InlineData(ManufacturingDbContextAuditEventPath)]
    [InlineData(SalesDbContextAuditEventPath)]
    [InlineData(PurchasingDbContextAuditEventPath)]
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
        var alternateSafeConfiguration = ExpectedAuditConfiguration(sourcePath).Replace(
            "modelBuilder.",
            "alternateBuilder.",
            StringComparison.Ordinal);
        Assert.Equal(1, CountAuditOccurrences(mutated, alternateSafeConfiguration));
        mutated = mutated.Replace(
            alternateSafeConfiguration,
            alternateSafeConfiguration.Replace(
                ExpectedAuditLambda(sourcePath),
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

    [Theory]
    [InlineData(AuditDbContextPath)]
    [InlineData(OrganizationDbContextAuditEventPath)]
    [InlineData(CounterpartyDbContextAuditEventPath)]
    [InlineData(InventoryDbContextAuditEventPath)]
    [InlineData(AccountingDbContextAuditEventPath)]
    [InlineData(FinanceDbContextAuditEventPath)]
    [InlineData(ManufacturingDbContextAuditEventPath)]
    [InlineData(SalesDbContextAuditEventPath)]
    [InlineData(PurchasingDbContextAuditEventPath)]
    public void Safe_filter_in_a_nested_decoy_context_cannot_hide_target_context(
        string sourcePath)
    {
        var source = ReadRepositorySource(sourcePath);
        var mutated = MutateExactAuditEventConfiguration(
            source,
            "x => x.CompanyId == companyContext.CompanyId",
            "x => true");
        const string targetMethod =
            "    protected override void OnModelCreating(ModelBuilder modelBuilder)";
        const string decoy =
            "    private sealed class DecoyDbContext : DbContext\n"
            + "    {\n"
            + "        protected override void OnModelCreating(ModelBuilder modelBuilder)\n"
            + "        {\n"
            + "            modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);\n"
            + "            modelBuilder.Entity<AuditEvent>()\n"
            + "                .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);\n"
            + "        }\n"
            + "    }\n\n";
        Assert.Equal(1, CountAuditOccurrences(mutated, targetMethod));
        mutated = mutated.Replace(
            targetMethod,
            decoy + targetMethod,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            CountAuditOccurrences(mutated, "private sealed class DecoyDbContext"));

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
    [InlineData(
        "var companyContextServiceType = typeof(ICompanyContext);\n"
        + "ServiceCollectionDescriptorExtensions.RemoveAll(builder.Services, "
        + "companyContextServiceType);\n"
        + "ServiceCollectionServiceExtensions.AddSingleton(builder.Services, "
        + "companyContextServiceType, typeof(AlternateCompanyContext));")]
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

        if (!TryReadOnModelCreating(
                source,
                contextTypeName,
                out var modelBuilderName,
                out var methodBody))
        {
            errors.Add(
                "The reviewed DbContext must declare exactly one conventional "
                + "OnModelCreating(ModelBuilder ...) method.");
            return errors;
        }

        var compactMethodBody = Regex.Replace(methodBody, @"\s+", string.Empty);
        var expectedAuditConfiguration = modelBuilderName + ".ConfigureAuditEvent(";
        var auditConfigurationCount = CountAuditOccurrences(
            compactMethodBody,
            expectedAuditConfiguration);
        if (auditConfigurationCount != 1)
        {
            errors.Add(
                "The reviewed DbContext OnModelCreating method must configure AuditEvent "
                + "exactly once on its declared ModelBuilder receiver; found "
                + auditConfigurationCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }

        var expectedFilter = Regex.Replace(ExpectedAuditConfiguration(sourcePath)
            .Replace("modelBuilder.", modelBuilderName + ".", StringComparison.Ordinal), @"\s+", string.Empty);
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
            || statement.Contains("typeof(", StringComparison.Ordinal)
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
        var configureAuditEventInvocation = new Regex(
            @"\.\s*ConfigureAuditEvent\s*\(",
            RegexOptions.CultureInvariant);
        return sources
            .Where(file => configureAuditEventInvocation.IsMatch(
                MaskCommentsAndNonInterpolatedLiterals(file.Value)))
            .Select(file => file.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool TryReadOnModelCreating(
        string source,
        string contextTypeName,
        out string modelBuilderName,
        out string methodBody)
    {
        if (!TryReadTypeBody(source, contextTypeName, out var typeBody))
        {
            modelBuilderName = string.Empty;
            methodBody = string.Empty;
            return false;
        }

        var signatures = Regex.Matches(
            typeBody,
            @"\bprotected\s+override\s+void\s+OnModelCreating\s*\(\s*"
            + @"ModelBuilder\s+(?<receiver>[\p{L}_][\p{L}\p{Nd}_]*)\s*\)",
            RegexOptions.CultureInvariant)
            .Where(match => GetBraceDepth(typeBody, match.Index) == 0)
            .ToArray();
        if (signatures.Length != 1)
        {
            modelBuilderName = string.Empty;
            methodBody = string.Empty;
            return false;
        }

        var signature = signatures[0];
        var openingBrace = typeBody.IndexOf('{', signature.Index + signature.Length);
        if (openingBrace < 0
            || !TryReadBraceDelimitedBody(typeBody, openingBrace, out methodBody))
        {
            modelBuilderName = string.Empty;
            methodBody = string.Empty;
            return false;
        }

        modelBuilderName = signature.Groups["receiver"].Value;
        return true;
    }

    private static bool TryReadTypeBody(
        string source,
        string contextTypeName,
        out string typeBody)
    {
        var searchable = MaskCommentsAndNonInterpolatedLiterals(source);
        var declaration = Regex.Match(
            searchable,
            @"\bclass\s+" + Regex.Escape(contextTypeName) + @"(?:\s|\()",
            RegexOptions.CultureInvariant);
        if (!declaration.Success)
        {
            typeBody = string.Empty;
            return false;
        }

        var openingBrace = searchable.IndexOf('{', declaration.Index + declaration.Length);
        if (openingBrace < 0)
        {
            typeBody = string.Empty;
            return false;
        }

        return TryReadBraceDelimitedBody(searchable, openingBrace, out typeBody);
    }

    private static bool TryReadBraceDelimitedBody(
        string searchable,
        int openingBrace,
        out string body)
    {
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
                    body = searchable[(openingBrace + 1)..index];
                    return true;
                }
            }
        }

        body = string.Empty;
        return false;
    }

    private static int GetBraceDepth(string source, int exclusiveEnd)
    {
        var depth = 0;
        for (var index = 0; index < exclusiveEnd; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
            }
        }

        return depth;
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
        var safeConfiguration = source.Contains(SafeProductAuditEventConfiguration, StringComparison.Ordinal)
            ? SafeProductAuditEventConfiguration : SafeAuditEventConfiguration;
        if (safeConfiguration == SafeProductAuditEventConfiguration)
            expectedFragment = expectedFragment.Replace("x => x.CompanyId == companyContext.CompanyId", SafeProductAuditLambda, StringComparison.Ordinal);
        Assert.Equal(1, CountAuditOccurrences(source, safeConfiguration));
        Assert.Equal(1, CountAuditOccurrences(safeConfiguration, expectedFragment));
        var mutatedConfiguration = safeConfiguration.Replace(
            expectedFragment,
            replacement,
            StringComparison.Ordinal);
        Assert.NotEqual(safeConfiguration, mutatedConfiguration);
        var mutated = source.Replace(
            safeConfiguration,
            mutatedConfiguration,
            StringComparison.Ordinal);
        Assert.NotEqual(source, mutated);
        Assert.Equal(0, CountAuditOccurrences(mutated, safeConfiguration));
        return mutated;
    }

    [Theory]
    [InlineData("groupSharedProductId.HasValue", "true")]
    [InlineData("groupSharedProductId.HasValue", "false")]
    [InlineData("x.SubjectKind == AuditEvent.GroupSharedProductSubject && ", "")]
    [InlineData("x.EntityType == AuditEvent.GroupSharedProductEntityType && ", "")]
    [InlineData("x.EntityId == QueryProductId", "true")]
    [InlineData("x.EntityId == QueryProductId", "x.EntityId == x.EntityId")]
    [InlineData("x.SubjectKind == AuditEvent.CompanySubject && ", "")]
    [InlineData("x.CompanyId == QueryCompanyId", "true")]
    public void Product_audit_subject_target_and_company_branches_cannot_be_weakened(string expected, string replacement)
    {
        var source = ReadRepositorySource(AuditDbContextPath);
        var mutated = MutateExactAuditEventConfiguration(source, expected, replacement);
        Assert.Contains(ValidateAuditEventTenantFilter(AuditDbContextPath, mutated),
            error => error.Contains("AuditEvent CompanyId query filter", StringComparison.Ordinal));
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
