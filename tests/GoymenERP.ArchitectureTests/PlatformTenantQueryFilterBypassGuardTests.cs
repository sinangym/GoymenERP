using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GoymenERP.ArchitectureTests;

public sealed partial class PlatformTenantQueryFilterBypassGuardTests
{
    private const string AllowedSeederPath =
        "src/backend/Modules/Organization/"
        + "GoymenERP.Modules.Organization.Infrastructure/Seeding/OrganizationDataSeeder.cs";

    private const int ExpectedSeederBypassCount = 4;
    private const string BackendRootPath = "src/backend";
    private const string ExcludedCounterpartyPrefix =
        "src/backend/Modules/Counterparty/";
    private const string ReviewedSeederFingerprint =
        "62f10533863d13b7172d557e99703d8a38fb34953294bc7fa6da9c7fc930c6ac";

    // This is a reviewed source inventory, not a claim that every entity mapped by every
    // context has a global query filter. Any context change must receive a fresh tenant-safety
    // review and a new fingerprint.
    private static readonly IReadOnlyDictionary<string, string> ReviewedPlatformDbContexts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/"
             + "Persistence/AuditDbContext.cs"] =
                "97ed5c2c671b121708c037e953ae47c662a9d5bd5de8ee58945549631ce8281f",
            ["src/backend/Modules/Idempotency/"
             + "GoymenERP.Modules.Idempotency.Infrastructure/Persistence/"
             + "CommandExecutionDbContext.cs"] =
                "3622036369dbfa41ea6f9fb8dbf1c68d63574f36cdb69a53a11e987990220118",
            ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/"
             + "Persistence/IdentityDbContext.cs"] =
                "9e258b4673942a179c8bf34ee8ba19a76e7b513bd11a46f17eba0ab0e4391b36",
            ["src/backend/Modules/Organization/"
             + "GoymenERP.Modules.Organization.Infrastructure/Persistence/"
             + "OrganizationDbContext.cs"] =
                "7b418484911bf7097779937d1a38d74d3f6849b0e2217428235413ff2e537f89"
        };

    private static readonly IReadOnlyDictionary<string, ReviewedContextFactory>
        ReviewedPlatformContextFactories =
            new Dictionary<string, ReviewedContextFactory>(StringComparer.Ordinal)
            {
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/"
                 + "Persistence/AuditDesignTimeDbContextFactory.cs"] =
                    new(
                        "AuditDbContext",
                        "38245af104fd713832e2926dba5798614515bb324d2560da8217f9cd49492b56"),
                ["src/backend/Modules/Idempotency/"
                 + "GoymenERP.Modules.Idempotency.Infrastructure/Persistence/"
                 + "CommandExecutionDesignTimeDbContextFactory.cs"] =
                    new(
                        "CommandExecutionDbContext",
                        "1d588ddec5f6d7e57a6ce3981eb0cd04ce110d6ac9380e2d177a64156950dc4c"),
                ["src/backend/Modules/Identity/"
                 + "GoymenERP.Modules.Identity.Infrastructure/Persistence/"
                 + "IdentityDesignTimeDbContextFactory.cs"] =
                    new(
                        "IdentityDbContext",
                        "6856fafc70ce956a698cdca896ed529f6f1bfdd743475162f5907356df9429e5"),
                ["src/backend/Modules/Organization/"
                 + "GoymenERP.Modules.Organization.Infrastructure/Persistence/"
                 + "OrganizationDesignTimeDbContextFactory.cs"] =
                    new(
                        "OrganizationDbContext",
                        "d02da60cb32fdd67dceeae4cc0b1936c562d06ef10021f5b9cd1b9c686cc6626")
            };

    // Migration data/raw SQL operations are deployment-time DDL/DML, not runtime global-query-
    // filter behavior. They remain exact source/API reviewed because they can mutate tenant data.
    private static readonly IReadOnlyDictionary<string, ReviewedMigrationMutationSource>
        ReviewedMigrationMutationSources =
            new Dictionary<string, ReviewedMigrationMutationSource>(StringComparer.Ordinal)
            {
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/"
                 + "Persistence/Migrations/20260827160529_InitialAudit.cs"] =
                    new(
                        "356d30c600bccd0f8e134a0d25208336da8bd2a6d6e8fb5ed6387ac92ca72278",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        }),
                ["src/backend/Modules/Idempotency/"
                 + "GoymenERP.Modules.Idempotency.Infrastructure/Persistence/"
                 + "Migrations/20260827175405_InitialIdempotency.cs"] =
                    new(
                        "298b1aed84ba4f5ba663b90076a0914ee87d49d7016eacf71e8ee9c6128e1e9d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Sql"] = 1
                        })
            };

    private static readonly HashSet<string> ReviewedProjectSdks =
        new(StringComparer.Ordinal)
        {
            "Microsoft.NET.Sdk",
            "Microsoft.NET.Sdk.Web"
        };

    private static readonly IReadOnlyDictionary<string, string> ReviewedPackageReferences =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Microsoft.AspNetCore.Authentication.JwtBearer"] = "10.0.8",
            ["Microsoft.EntityFrameworkCore.Design"] = "10.0.8",
            ["Microsoft.EntityFrameworkCore.SqlServer"] = "10.0.8",
            ["Microsoft.Extensions.Options.ConfigurationExtensions"] = "10.0.8"
        };

    private static readonly HashSet<string> ForbiddenGeneratedSourceItems =
        new(StringComparer.Ordinal)
        {
            "AdditionalFiles",
            "Analyzer",
            "CompilerVisibleItemMetadata",
            "CompilerVisibleProperty",
            "RazorComponent",
            "RazorGenerate",
            "SourceRoot"
        };

    private static readonly HashSet<string> UnsupportedManagedSourceExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".csx",
            ".fs",
            ".fsi",
            ".fsx",
            ".il",
            ".vb",
            ".vbx"
        };

    private static readonly HashSet<string> ReviewedProjectProperties =
        new(StringComparer.Ordinal)
        {
            "ImplicitUsings",
            "Nullable",
            "TargetFramework"
        };

    private static readonly HashSet<string> ReviewedProjectItems =
        new(StringComparer.Ordinal)
        {
            "Compile",
            "PackageReference",
            "ProjectReference"
        };

    private static readonly HashSet<string> ReviewedRepositoryBuildProperties =
        new(StringComparer.Ordinal)
        {
            "Deterministic",
            "EnforceCodeStyleInBuild",
            "TreatWarningsAsErrors"
        };

    [Fact]
    public void Platform_non_counterparty_bypass_surfaces_match_the_reviewed_inventory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var inventory = ReadBackendSourceInventory(repositoryRoot);
        var errors = inventory.DefinitionErrors.ToList();
        errors.AddRange(ValidateNonVacuousScope(inventory));
        errors.AddRange(ValidateExactAllowlist(
            FindOccurrences(inventory.Sources),
            inventory.Sources));
        errors.AddRange(ValidateNoForbiddenBypassCapabilities(inventory.Sources));
        errors.AddRange(ValidateNoDirectDatabaseBypassCapabilities(inventory.Sources));
        errors.AddRange(ValidateReviewedMigrationMutationSurface(inventory.Sources));
        errors.AddRange(ValidateReviewedPlatformDbContextInventory(inventory.Sources));
        errors.AddRange(ValidateReviewedPlatformContextConstructions(inventory.Sources));

        Assert.True(errors.Count == 0, string.Join("\n\n", errors));
    }

    [Fact]
    public void Synthetic_unauthorized_bypass_is_rejected_without_comment_noninterpolated_string_or_path_noise()
    {
        var allowedSource = string.Join(
            '\n',
            "companies.IgnoreQueryFilters();",
            "sites.IgnoreQueryFilters();",
            "locations.IgnoreQueryFilters();",
            "workCenters.IgnoreQueryFilters();",
            "// comments.IgnoreQueryFilters();",
            "/* blockComments.IgnoreQueryFilters(); */",
            "const string regularText = \"text.IgnoreQueryFilters();\";",
            "const string verbatimText = @\"text.IgnoreQueryFilters();\";");

        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AllowedSeederPath] = allowedSource,
            ["src/backend/GoymenERP.Api/UnauthorizedProbe.cs"] =
                "query.IgnoreQueryFilters();"
        };

        var occurrences = FindOccurrences(sources);
        Assert.Equal(ExpectedSeederBypassCount + 1, occurrences.Count);

        var errors = ValidateExactAllowlist(occurrences, sources);

        Assert.Contains(errors, error => error.Contains(
            "Unauthorized IgnoreQueryFilters calls",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "UnauthorizedProbe.cs:1",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "reviewed fingerprint",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Seeder_allowlist_requires_the_canonical_path_and_reviewed_fingerprint()
    {
        const string arbitraryFourCalls = """
            a.IgnoreQueryFilters();
            b.IgnoreQueryFilters();
            c.IgnoreQueryFilters();
            d.IgnoreQueryFilters();
            """;
        var wrongCasePath = AllowedSeederPath.Replace(
            "OrganizationDataSeeder.cs",
            "organizationdataseeder.cs",
            StringComparison.Ordinal);
        var wrongCaseSources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [wrongCasePath] = arbitraryFourCalls
        };

        var pathErrors = ValidateExactAllowlist(
            FindOccurrences(wrongCaseSources),
            wrongCaseSources);
        Assert.Contains(pathErrors, error => error.Contains(
            "Unauthorized IgnoreQueryFilters calls",
            StringComparison.Ordinal));

        var arbitrarySources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AllowedSeederPath] = arbitraryFourCalls
        };
        var fingerprintErrors = ValidateExactAllowlist(
            FindOccurrences(arbitrarySources),
            arbitrarySources);
        Assert.Contains(fingerprintErrors, error => error.Contains(
            "reviewed fingerprint",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Escaped_identifier_cannot_hide_an_unauthorized_bypass()
    {
        var occurrences = FindOccurrences(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Audit/EscapedProbe.cs"] =
                    @"query.IgnoreQuery\u0046ilters();"
            });

        var occurrence = Assert.Single(occurrences);
        Assert.Equal("src/backend/Modules/Audit/EscapedProbe.cs", occurrence.RepositoryPath);
        Assert.Equal(1, occurrence.LineNumber);
    }

    [Theory]
    [InlineData("query.Ignore‌QueryFilters();")] // Literal U+200C ZERO WIDTH NON-JOINER.
    [InlineData("query.Ignore‍QueryFilters();")] // Literal U+200D ZERO WIDTH JOINER.
    [InlineData(@"query.Ignore\u200CQueryFilters();")]
    [InlineData(@"query.Ignore\u200DQueryFilters();")]
    public void Format_characters_cannot_split_the_guarded_identifier(string source)
    {
        var occurrences = FindOccurrences(
            new Dictionary<string, string>
            {
                ["src/backend/BuildingBlocks/FormatProbe.cs"] = source
            });

        Assert.Single(occurrences);
    }

    [Fact]
    public void Interpolated_expression_cannot_hide_a_bypass_after_a_nested_literal()
    {
        var occurrences = FindOccurrences(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Identity/InterpolatedProbe.cs"] =
                    "var text = $\"{(name == \"x\" ? first : second).IgnoreQueryFilters()}\";"
            });

        var occurrence = Assert.Single(occurrences);
        Assert.Equal("src/backend/Modules/Identity/InterpolatedProbe.cs", occurrence.RepositoryPath);
    }

    [Fact]
    public void Method_group_cannot_hide_an_unauthorized_bypass_reference()
    {
        var occurrences = FindOccurrences(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Idempotency/MethodGroupProbe.cs"] =
                    "var bypass = EntityFrameworkQueryableExtensions.IgnoreQueryFilters;"
            });

        Assert.Single(occurrences);
    }

    [Fact]
    public void Reflection_name_concatenation_is_rejected_but_normal_delegate_invoke_is_not()
    {
        const string reflectionProbe = """
            var name = "Ignore" + "QueryFilters";
            var method = typeof(EntityFrameworkQueryableExtensions).GetMethod(name)!;
            method.Invoke(null, new object[] { query });
            """;
        var reflectionSources = new Dictionary<string, string>
        {
            ["src/backend/GoymenERP.Api/ReflectionProbe.cs"] = reflectionProbe
        };

        var reflectionErrors = ValidateNoForbiddenBypassCapabilities(reflectionSources);
        Assert.Contains(reflectionErrors, error => error.Contains(
            "GetMethod",
            StringComparison.Ordinal));

        var delegateSources = new Dictionary<string, string>
        {
            ["src/backend/Modules/Organization/DelegateProbe.cs"] =
                "configureDbContext?.Invoke(serviceProvider, options);"
        };
        Assert.Empty(ValidateNoForbiddenBypassCapabilities(delegateSources));
    }

    [Theory]
    [InlineData("using System.Reflection;")]
    [InlineData("MethodInfo method = value; method.Invoke(null, null);")]
    [InlineData("MethodBase method = value; method.Invoke(null, null);")]
    [InlineData("typeof(Target).GetMethod(\"Run\");")]
    [InlineData("typeof(Target).GetMethods();")]
    [InlineData("typeof(Target).GetMember(\"Run\");")]
    [InlineData("typeof(Target).GetMembers();")]
    [InlineData("Expression.Call(method);")]
    [InlineData("dynamic target = value;")]
    [InlineData("handler.DynamicInvoke();")]
    [InlineData("var type = Type.GetType(name);")]
    [InlineData("var value = Activator.CreateInstance(type);")]
    [InlineData("var value = ActivatorUtilities.CreateInstance<AuditDbContext>(services);")]
    [InlineData("var assembly = Assembly.Load(name);")]
    [InlineData("AssemblyLoadContext.Default.LoadFromAssemblyPath(path);")]
    [InlineData("typeof(AuditDbContext).GetConstructor(types);")]
    [InlineData("typeof(AuditDbContext).GetConstructors();")]
    [InlineData("PropertyInfo property = type.GetProperty(name);")]
    [InlineData("FieldInfo field = type.GetField(name);")]
    [InlineData("EventInfo eventInfo = type.GetEvent(name);")]
    [InlineData("var flags = BindingFlags.Instance | BindingFlags.NonPublic;")]
    [InlineData(
        "typeof(CompanyContext).GetField(\"_companyId\", flags)!"
        + ".SetValue(context, companyB);")]
    [InlineData("var value = property.GetValue(instance);")]
    [InlineData("using ReflectionAlias = @System.@Reflection;")]
    public void Reviewed_reflection_and_dynamic_capability_routes_have_a_zero_allowlist(
        string source)
    {
        var errors = ValidateNoForbiddenBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/CapabilityProbe.cs"] = source
            });

        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("""
        using E = System.Linq.Expressions.Expression;
        var methodName = "Ignore" + "QueryFilters";
        var call = E.Call(instance: null, method: Resolve(methodName));
        """)]
    [InlineData("""
        using static System.Linq.Expressions.Expression;
        var methodName = "Ignore" + "QueryFilters";
        var call = Call(instance: null, method: Resolve(methodName));
        """)]
    public void Linq_expression_capability_is_rejected_through_alias_or_using_static(
        string source)
    {
        var errors = ValidateNoForbiddenBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/ExpressionProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "System.Linq.Expressions",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Linq_expression_namespace_cannot_be_hidden_behind_a_parent_namespace_alias()
    {
        const string source = """
            using Linq = System.Linq;
            var methodName = "Ignore" + "QueryFilters";
            var call = Linq.Expressions.Expression.Call(
                instance: null,
                method: Resolve(methodName));
            """;

        var errors = ValidateNoForbiddenBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/ExpressionPrefixAliasProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Expressions",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("database.ExecuteSqlRaw(\"DELETE FROM T\");")]
    [InlineData("database.ExecuteSqlRawAsync(\"DELETE FROM T\");")]
    [InlineData("database.ExecuteSqlInterpolated($\"DELETE FROM T\");")]
    [InlineData("database.ExecuteSqlInterpolatedAsync($\"DELETE FROM T\");")]
    [InlineData("database.SqlQueryRaw<int>(\"SELECT 1\");")]
    [InlineData("database.SqlQuery<int>($\"SELECT 1\");")]
    [InlineData("entities.FromSqlRaw(\"SELECT * FROM T\");")]
    [InlineData("entities.FromSqlInterpolated($\"SELECT * FROM T\");")]
    [InlineData("entities.FromSql($\"SELECT * FROM T\");")]
    [InlineData("context.Database.GetDbConnection();")]
    [InlineData("await entities.FindAsync(id);")]
    [InlineData("entities.Find(id);")]
    [InlineData("await entry.GetDatabaseValuesAsync();")]
    [InlineData("entry.GetDatabaseValues();")]
    [InlineData("await entry.ReloadAsync();")]
    [InlineData("entry.Reload();")]
    [InlineData("var find = entities.Find;")]
    [InlineData("Func<Task> reload = entry.ReloadAsync;")]
    [InlineData("var databaseValues = entry.GetDatabaseValues;")]
    [InlineData("var tracked = entities.Local;")]
    [InlineData("var context = new DbContext(options);")]
    [InlineData("var context = new Microsoft.EntityFrameworkCore.DbContext(options);")]
    [InlineData("DbContext context = new(options);")]
    [InlineData("DbConnection connection = provider.Open();")]
    [InlineData("IDbConnection connection = provider.Open();")]
    [InlineData("DbCommand command = connection.CreateCommand();")]
    [InlineData("DbDataAdapter adapter = provider.CreateDataAdapter();")]
    [InlineData("IDbDataAdapter adapter = provider.CreateDataAdapter();")]
    [InlineData("DbDataSource dataSource = provider.CreateDataSource();")]
    [InlineData("DbBatch batch = connection.CreateBatch();")]
    [InlineData("DbBatchCommand command = batch.CreateBatchCommand();")]
    [InlineData("DbProviderFactories.GetFactory(providerName);")]
    [InlineData("SqlConnection connection = new(value);")]
    [InlineData("SqlCommand command = new(value);")]
    [InlineData("SqlDataAdapter adapter = new(command);")]
    [InlineData("SqlDataSource dataSource = SqlDataSource.Create(value);")]
    [InlineData("SqlBatch batch = connection.CreateBatch();")]
    [InlineData("SqlBatchCommand command = batch.CreateBatchCommand();")]
    [InlineData("SqlBulkCopy bulkCopy = new(connection);")]
    [InlineData("using Microsoft.Data.SqlClient;")]
    [InlineData("using @Microsoft.@Data.@SqlClient;")]
    [InlineData("using System.Data.Common;")]
    [InlineData("using System.Data.SqlClient;")]
    [InlineData("IRelationalConnection connection = value;")]
    [InlineData("IDbContextFactory<AuditDbContext> factory = value;")]
    [InlineData("services.AddDbContextFactory<AuditDbContext>();")]
    [InlineData("services.AddDbContextPool<AuditDbContext>();")]
    [InlineData("services.AddPooledDbContextFactory<AuditDbContext>();")]
    [InlineData("PooledDbContextFactory<AuditDbContext> factory = value;")]
    [InlineData("options.UseModel(model);")]
    [InlineData("options.ReplaceService<IModelSource, CustomModelSource>();")]
    [InlineData("options.UseInternalServiceProvider(provider);")]
    [InlineData("IModelCustomizer customizer = value;")]
    [InlineData("IModelCacheKeyFactory cacheKeys = value;")]
    [InlineData("IQueryExpressionInterceptor interceptor = value;")]
    [InlineData("DbCommandInterceptor interceptor = value;")]
    public void Direct_database_and_raw_sql_capability_routes_have_a_zero_allowlist(
        string source)
    {
        var errors = ValidateNoDirectDatabaseBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/DirectDatabaseProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Forbidden direct database/query-filter bypass capability",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "runtime application",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(@"database.Execute\u0053qlRaw(""SELECT 1"");")]
    [InlineData("database.Execute‌SqlRaw(\"SELECT 1\");")] // Literal U+200C.
    [InlineData("database.GetDb‍Connection();")] // Literal U+200D.
    public void Unicode_identifier_obfuscation_cannot_hide_a_direct_database_route(
        string source)
    {
        Assert.NotEmpty(ValidateNoDirectDatabaseBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/UnicodeDatabaseProbe.cs"] = source
            }));
    }

    [Fact]
    public void Direct_database_route_noise_in_comments_and_noninterpolated_literals_is_ignored()
    {
        const string source = """
            // database.ExecuteSqlRaw("DELETE FROM T");
            /* DbConnection connection = value; */
            const string regular = "SqlCommand CreateCommand GetDbConnection";
            const string verbatim = @"FromSqlRaw ExecuteSqlInterpolated System.Data.Common";
            """;

        Assert.Empty(ValidateNoDirectDatabaseBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/DirectDatabaseNoise.cs"] = source
            }));
    }

    [Fact]
    public void Migration_and_design_time_sources_are_classified_but_not_exempted()
    {
        var errors = ValidateNoDirectDatabaseBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Audit/Infrastructure/Persistence/Migrations/Probe.cs"] =
                    "database.ExecuteSqlRaw(\"DELETE FROM T\");",
                ["src/backend/Modules/Audit/Infrastructure/Persistence/"
                 + "ProbeDesignTimeDbContextFactory.cs"] =
                    "DbConnection connection = provider.Open();"
            });

        Assert.Contains(errors, error => error.Contains(
            "migration",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "design-time",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("migrationBuilder.Sql(\"DELETE FROM T\");", "Sql")]
    [InlineData("var sql = migrationBuilder.Sql;", "Sql")]
    [InlineData("Sql(\"DELETE FROM T\");", "Sql")]
    [InlineData("var sql = Sql;", "Sql")]
    [InlineData("migrationBuilder.@InsertData(table: \"T\");", "InsertData")]
    [InlineData("migrationBuilder.UpdateData(table: \"T\");", "UpdateData")]
    [InlineData("migrationBuilder.DeleteData(table: \"T\");", "DeleteData")]
    [InlineData("var operation = new SqlOperation();", "SqlOperation")]
    [InlineData("var operation = new InsertDataOperation();", "InsertDataOperation")]
    [InlineData("var operation = new UpdateDataOperation();", "UpdateDataOperation")]
    [InlineData("var operation = new DeleteDataOperation();", "DeleteDataOperation")]
    public void Unreviewed_migration_raw_or_data_mutation_surface_is_rejected(
        string source,
        string apiName)
    {
        var errors = ValidateReviewedMigrationMutationSurface(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Reporting/Infrastructure/Persistence/"
                 + "Migrations/Unreviewed.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            $"Unreviewed migration mutation API '{apiName}'",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Migration_mutation_api_is_forbidden_in_runtime_application_source()
    {
        var errors = ValidateReviewedMigrationMutationSurface(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/RuntimeMigrationProbe.cs"] =
                    "migrationBuilder.Sql(\"DELETE FROM T\");"
            });

        Assert.Contains(errors, error => error.Contains(
            "forbidden outside migration source",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Reviewed_migration_mutation_source_requires_exact_fingerprint()
    {
        var (path, reviewed) = ReviewedMigrationMutationSources.First();
        var errors = ValidateReviewedMigrationMutationSurface(
            new Dictionary<string, string>
            {
                [path] = "migrationBuilder.Sql(\"SELECT 1\");"
            });

        Assert.Contains(errors, error => error.Contains(
            "does not match the reviewed fingerprint",
            StringComparison.Ordinal));
        Assert.NotEmpty(reviewed.ExpectedApiOccurrences);
    }

    [Fact]
    public void Migration_mutation_noise_in_comments_and_literals_is_ignored()
    {
        const string source = """
            // migrationBuilder.Sql("DELETE FROM T");
            /* migrationBuilder.InsertData(table: "T"); */
            const string text = "SqlOperation UpdateData DeleteData";
            """;
        var errors = ValidateReviewedMigrationMutationSurface(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Reporting/Infrastructure/Persistence/"
                 + "Migrations/Noise.cs"] = source
            });

        Assert.DoesNotContain(errors, error => error.Contains(
            "Unreviewed migration mutation API",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Compiled_source_categories_keep_runtime_migration_design_time_and_build_distinct()
    {
        Assert.Equal(
            PlatformSourceCategory.RuntimeApplication,
            ClassifySource("src/backend/GoymenERP.Api/Program.cs"));
        Assert.Equal(
            PlatformSourceCategory.Migration,
            ClassifySource("src/backend/Modules/Audit/Persistence/Migrations/Probe.cs"));
        Assert.Equal(
            PlatformSourceCategory.DesignTime,
            ClassifySource(
                "src/backend/Modules/Audit/Persistence/AuditDesignTimeDbContextFactory.cs"));
        Assert.Equal(
            PlatformSourceCategory.BuildArtifact,
            ClassifySource("src/backend/Modules/Audit/obj/GeneratedProbe.cs"));
    }

    [Fact]
    public void New_filterless_platform_dbcontext_is_rejected()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            public sealed class ReportingDbContext(
                DbContextOptions<ReportingDbContext> options)
                : DbContext(options)
            {
            }
            """;
        var errors = ValidateReviewedPlatformDbContextInventory(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Reporting/Infrastructure/ReportingDbContext.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Unreviewed platform DbContext declaration",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "ReportingDbContext",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("using ContextBase = Microsoft.EntityFrameworkCore.DbContext;")]
    [InlineData("global using ContextBase = global::Microsoft.EntityFrameworkCore.DbContext;")]
    public void Dbcontext_base_type_alias_cannot_hide_a_new_filterless_context(string alias)
    {
        var errors = ValidateReviewedPlatformDbContextInventory(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Reporting/Infrastructure/ReportingDbContext.cs"] =
                    alias + "\npublic sealed class ReportingDbContext : ContextBase { }"
            });

        Assert.Contains(errors, error => error.Contains(
            "DbContext base-type alias",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Reviewed_dbcontext_path_and_fingerprint_are_both_fail_closed()
    {
        var reviewedPath = ReviewedPlatformDbContexts.Keys.First();
        var errors = ValidateReviewedPlatformDbContextInventory(
            new Dictionary<string, string>
            {
                [reviewedPath] = "public sealed class AuditDbContext : DbContext { }"
            });

        Assert.Contains(errors, error => error.Contains(
            "reviewed fingerprint",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        "new OrganizationDbContext(options, fakeCompanyContext, bootstrapScope);")]
    [InlineData(
        "new @OrganizationDbContext(options, fakeCompanyContext, bootstrapScope);")]
    [InlineData(
        "new GoymenERP.Modules.Organization.Infrastructure.Persistence."
        + "@OrganizationDbContext(options, fakeCompanyContext, bootstrapScope);")]
    [InlineData(
        "OrganizationDbContext context = new(options, fakeCompanyContext, bootstrapScope);")]
    [InlineData(
        "services.AddScoped<OrganizationDbContext>(_ => "
        + "new(options, fakeCompanyContext, bootstrapScope));")]
    [InlineData(
        "OrganizationDbContext Create() { "
        + "return new(options, fakeCompanyContext, bootstrapScope); }")]
    [InlineData(
        "OrganizationDbContext Context { get; } = "
        + "new(options, fakeCompanyContext, bootstrapScope);")]
    public void Runtime_construction_of_a_reviewed_context_is_rejected(string source)
    {
        var errors = ValidateReviewedPlatformContextConstructions(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/FabricatedCompanyContextProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Unauthorized reviewed platform DbContext construction",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "OrganizationDbContext",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Reviewed_context_alias_cannot_hide_an_unauthorized_construction()
    {
        const string source = """
            using Context = GoymenERP.Modules.Organization.Infrastructure.Persistence
                .OrganizationDbContext;
            var context = new Context(options, fakeCompanyContext, bootstrapScope);
            """;
        var errors = ValidateReviewedPlatformContextConstructions(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/AliasedContextConstructionProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Reviewed platform DbContext type alias",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Design_time_context_construction_requires_the_exact_factory_fingerprint()
    {
        var (path, factory) = ReviewedPlatformContextFactories.First();
        var errors = ValidateReviewedPlatformContextConstructions(
            new Dictionary<string, string>
            {
                [path] = $"return new {factory.ContextTypeName}(options);"
            });

        Assert.Contains(errors, error => error.Contains(
            "factory does not match the reviewed fingerprint",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        "new OrganizationDesignTimeDbContextFactory().CreateDbContext([]);")]
    [InlineData(
        "IDesignTimeDbContextFactory<OrganizationDbContext> factory = value;")]
    public void Runtime_access_to_a_design_time_context_factory_is_rejected(string source)
    {
        var errors = ValidateReviewedPlatformContextConstructions(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/DesignTimeFactoryAccessProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Unauthorized design-time DbContext factory reference",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public sealed class Reporting : @DbContext { }")]
    [InlineData(
        "public sealed class Reporting : "
        + "Microsoft.EntityFrameworkCore.@DbContext { }")]
    public void Verbatim_identifier_cannot_hide_a_new_filterless_context(string source)
    {
        var errors = ValidateReviewedPlatformDbContextInventory(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Reporting/Infrastructure/ReportingDbContext.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Unreviewed platform DbContext declaration",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Verbatim_identifier_cannot_hide_a_dbcontext_base_type_alias()
    {
        const string source = """
            using Base = Microsoft.EntityFrameworkCore.@DbContext;
            public sealed class Reporting : Base { }
            """;
        var errors = ValidateReviewedPlatformDbContextInventory(
            new Dictionary<string, string>
            {
                ["src/backend/Modules/Reporting/Infrastructure/ReportingDbContext.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "DbContext base-type alias",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Project_definition_guard_tracks_literal_links_and_rejects_generation_indirection()
    {
        var safe = AnalyzeProjectDefinition(
            XDocument.Parse("""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="..\Shared\TenantGuard.cs">
                      <Link>Linked\TenantGuard.cs</Link>
                    </Compile>
                    <Compile Remove="Legacy.cs" />
                  </ItemGroup>
                </Project>
                """),
            "src/backend/Synthetic/Synthetic.csproj");

        Assert.Empty(safe.Errors);
        Assert.Contains(safe.CompileDirectives, directive =>
            directive.Kind == CompileDirectiveKind.Include
            && directive.Items.Single() == @"..\Shared\TenantGuard.cs"
            && directive.Link == @"Linked\TenantGuard.cs");
        Assert.Contains(safe.CompileDirectives, directive =>
            directive.Kind == CompileDirectiveKind.Remove);

        var unsafeDefinition = AnalyzeProjectDefinition(
            XDocument.Parse("""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="generated.targets" />
                  <ItemGroup>
                    <Analyzer Include="Generator.dll" />
                    <Compile Include="$(IntermediateOutputPath)Generated.cs" />
                    <PackageReference Include="Unreviewed.SourceGenerator" Version="1.0.0" />
                  </ItemGroup>
                  <Target Name="GenerateSource" />
                </Project>
                """),
            "src/backend/Synthetic/Synthetic.csproj");

        Assert.Contains(unsafeDefinition.Errors, error => error.Contains(
            "Import",
            StringComparison.Ordinal));
        Assert.Contains(unsafeDefinition.Errors, error => error.Contains(
            "Analyzer",
            StringComparison.Ordinal));
        Assert.Contains(unsafeDefinition.Errors, error => error.Contains(
            "MSBuild indirection",
            StringComparison.Ordinal));
        Assert.Contains(unsafeDefinition.Errors, error => error.Contains(
            "Unreviewed package",
            StringComparison.Ordinal));
        Assert.Contains(unsafeDefinition.Errors, error => error.Contains(
            "Target",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(@"..\..\Shared\LinkedBypass.cs")]
    [InlineData("../../Shared/LinkedBypass.cs")]
    public void Linked_compile_source_outside_backend_is_scanned_on_both_path_styles(
        string compileInclude)
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-{Guid.NewGuid():N}");

        try
        {
            var projectDirectory = Path.Combine(repositoryRoot, "src", "backend", "Synthetic");
            var linkedDirectory = Path.Combine(repositoryRoot, "src", "Shared");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(linkedDirectory);
            File.WriteAllText(
                Path.Combine(projectDirectory, "Synthetic.csproj"),
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="{{compileInclude}}">
                      <Link>Linked\LinkedBypass.cs</Link>
                    </Compile>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(linkedDirectory, "LinkedBypass.cs"),
                "query.IgnoreQueryFilters();");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Empty(inventory.DefinitionErrors);
            Assert.Contains("src/Shared/LinkedBypass.cs", inventory.Sources.Keys);
            var occurrence = Assert.Single(FindOccurrences(inventory.Sources));
            Assert.Equal("src/Shared/LinkedBypass.cs", occurrence.RepositoryPath);
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
    public void Non_csharp_explicit_compile_input_is_rejected_fail_closed()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-noncs-{Guid.NewGuid():N}");

        try
        {
            var projectDirectory = Path.Combine(repositoryRoot, "src", "backend", "Synthetic");
            var linkedDirectory = Path.Combine(repositoryRoot, "src", "Shared");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(linkedDirectory);
            File.WriteAllText(
                Path.Combine(projectDirectory, "Synthetic.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="..\..\Shared\Bypass.txt">
                      <Link>Linked\Bypass.txt</Link>
                    </Compile>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(linkedDirectory, "Bypass.txt"),
                "query.IgnoreQueryFilters();");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "canonical .cs source extension",
                StringComparison.Ordinal));
            Assert.DoesNotContain("src/Shared/Bypass.txt", inventory.Sources.Keys);
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(@"..\..\Shared\CaseVariant.CS")]
    [InlineData("../../Shared/CaseVariant.CS")]
    public void Explicit_compile_input_requires_canonical_lowercase_cs_on_every_platform(
        string compileInclude)
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-case-source-{Guid.NewGuid():N}");

        try
        {
            var projectDirectory = Path.Combine(repositoryRoot, "src", "backend", "Synthetic");
            var linkedDirectory = Path.Combine(repositoryRoot, "src", "Shared");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(linkedDirectory);
            File.WriteAllText(
                Path.Combine(projectDirectory, "Synthetic.csproj"),
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="{{compileInclude}}" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(linkedDirectory, "CaseVariant.CS"),
                "internal class CaseVariant;");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "canonical .cs source extension",
                StringComparison.Ordinal));
            Assert.DoesNotContain("src/Shared/CaseVariant.CS", inventory.Sources.Keys);
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
    public void Unsupported_backend_project_and_managed_source_extensions_are_rejected()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-unsupported-language-{Guid.NewGuid():N}");

        try
        {
            var backendRoot = Path.Combine(repositoryRoot, "src", "backend");
            var csharpDirectory = Path.Combine(backendRoot, "Synthetic");
            var hiddenDirectory = Path.Combine(backendRoot, "Hidden");
            Directory.CreateDirectory(csharpDirectory);
            Directory.CreateDirectory(hiddenDirectory);
            File.WriteAllText(
                Path.Combine(csharpDirectory, "Synthetic.csproj"),
                """<Project Sdk="Microsoft.NET.Sdk" />""");
            File.WriteAllText(
                Path.Combine(hiddenDirectory, "Hidden.fsproj"),
                """<Project Sdk="Microsoft.NET.Sdk" />""");
            File.WriteAllText(
                Path.Combine(hiddenDirectory, "WrongCase.CSPROJ"),
                """<Project Sdk="Microsoft.NET.Sdk" />""");
            File.WriteAllText(Path.Combine(hiddenDirectory, "Hidden.fs"), "module Hidden");
            File.WriteAllText(
                Path.Combine(hiddenDirectory, "Hidden.vb"),
                "Public Class Hidden\nEnd Class");
            File.WriteAllText(
                Path.Combine(hiddenDirectory, "CaseVariant.CS"),
                "internal class CaseVariant;");
            File.WriteAllText(
                Path.Combine(hiddenDirectory, "MixedCase.cS"),
                "internal class MixedCase;");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Hidden.fsproj",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "WrongCase.CSPROJ",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Hidden.fs",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Hidden.vb",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "CaseVariant.CS",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "MixedCase.cS",
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
    public void Project_reference_must_target_a_parsed_canonical_csharp_project()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-project-reference-{Guid.NewGuid():N}");

        try
        {
            var backendRoot = Path.Combine(repositoryRoot, "src", "backend");
            var sourceDirectory = Path.Combine(backendRoot, "Source");
            var hiddenDirectory = Path.Combine(backendRoot, "Hidden");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(hiddenDirectory);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "Source.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Hidden\Hidden.fsproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(hiddenDirectory, "Hidden.fsproj"),
                """<Project Sdk="Microsoft.NET.Sdk" />""");
            File.WriteAllText(Path.Combine(hiddenDirectory, "Hidden.fs"), "module Hidden");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "canonical .csproj",
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

    [Theory]
    [InlineData(@"..\Target\Target.csproj")]
    [InlineData("../Target/Target.csproj")]
    public void Parsed_csharp_project_reference_supports_both_path_styles(
        string projectReferenceInclude)
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-csharp-reference-{Guid.NewGuid():N}");

        try
        {
            var backendRoot = Path.Combine(repositoryRoot, "src", "backend");
            var sourceDirectory = Path.Combine(backendRoot, "Source");
            var targetDirectory = Path.Combine(backendRoot, "Target");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "Source.csproj"),
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="{{projectReferenceInclude}}" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(targetDirectory, "Target.csproj"),
                """<Project Sdk="Microsoft.NET.Sdk" />""");
            File.WriteAllText(Path.Combine(sourceDirectory, "Source.cs"), "internal class Source;");
            File.WriteAllText(Path.Combine(targetDirectory, "Target.cs"), "internal class Target;");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Empty(inventory.DefinitionErrors);
            Assert.Contains("src/backend/Source/Source.csproj", inventory.ProjectPaths);
            Assert.Contains("src/backend/Target/Target.csproj", inventory.ProjectPaths);
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
    public void Project_reference_target_must_be_successfully_parsed()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-malformed-reference-{Guid.NewGuid():N}");

        try
        {
            var backendRoot = Path.Combine(repositoryRoot, "src", "backend");
            var sourceDirectory = Path.Combine(backendRoot, "Source");
            var targetDirectory = Path.Combine(backendRoot, "Target");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "Source.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Target\Target.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(targetDirectory, "Target.csproj"), "<Project");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Could not parse backend project src/backend/Target/Target.csproj",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "not successfully parsed",
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
    public void Non_counterparty_project_cannot_reference_the_excluded_ownership_scope()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-counterparty-reference-{Guid.NewGuid():N}");

        try
        {
            var backendRoot = Path.Combine(repositoryRoot, "src", "backend");
            var sourceDirectory = Path.Combine(backendRoot, "Source");
            var ownedDirectory = Path.Combine(
                backendRoot,
                "Modules",
                "Counterparty",
                "Owned");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(ownedDirectory);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "Source.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference
                      Include="..\Modules\Counterparty\Owned\Owned.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(ownedDirectory, "Owned.csproj"),
                """<Project Sdk="Microsoft.NET.Sdk" />""");

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "excluded Counterparty ownership scope",
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
    public void Project_user_and_all_ancestor_msbuild_definitions_are_fail_closed()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-msbuild-{Guid.NewGuid():N}");

        try
        {
            var sourceRoot = Path.Combine(repositoryRoot, "src");
            var backendRoot = Path.Combine(sourceRoot, "backend");
            var projectDirectory = Path.Combine(backendRoot, "Synthetic");
            Directory.CreateDirectory(projectDirectory);
            var projectPath = Path.Combine(projectDirectory, "Synthetic.csproj");
            File.WriteAllText(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(projectDirectory, "Safe.cs"), "internal class Safe;");
            File.WriteAllText(
                projectPath + ".user",
                """
                <Project>
                  <Import Project="UserInjected.targets" />
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(repositoryRoot, "Directory.Build.props"),
                """
                <Project>
                  <Import Project="RootInjected.targets" />
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(sourceRoot, "Directory.Build.props"),
                """
                <Project>
                  <Import Project="AncestorInjected.targets" />
                  <ItemGroup>
                    <Compile Include="Injected.cs" />
                    <Analyzer Include="Generator.dll" />
                    <SourceGenerator Include="Generator.dll" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(backendRoot, "Directory.Build.targets"),
                """
                <Project>
                  <Target Name="InjectCompile" />
                </Project>
                """);

            var inventory = ReadBackendSourceInventory(repositoryRoot);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Synthetic.csproj.user",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Directory.Build.targets",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Compile: src/Directory.Build.props",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Analyzer: src/Directory.Build.props",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "Import: src/Directory.Build.props",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "SourceGenerator: src/Directory.Build.props",
                StringComparison.Ordinal));
            Assert.Contains(inventory.DefinitionErrors, error => error.EndsWith(
                "Import: Directory.Build.props",
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
    public void Counterparty_ownership_exclusion_is_exact_and_case_sensitive()
    {
        Assert.True(IsExcludedCounterpartyPath(
            "src/backend/Modules/Counterparty/Owned.cs"));
        Assert.False(IsExcludedCounterpartyPath(
            "src/backend/Modules/counterparty/NotCanonical.cs"));
        Assert.False(IsExcludedCounterpartyPath(
            "src/backend/Modules/CounterpartyExtensions/NotOwned.cs"));
    }

    private static BackendSourceInventory ReadBackendSourceInventory(
        string repositoryRoot)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var definitionErrors = new List<string>();
        var projectPaths = new List<string>();
        var automaticDefinitionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var backendRoot = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            BackendRootPath.Replace('/', Path.DirectorySeparatorChar)));

        if (!IsUnderDirectory(repositoryRoot, backendRoot) || !Directory.Exists(backendRoot))
        {
            throw new DirectoryNotFoundException(
                $"Tenant query-filter guard backend scope is missing: {BackendRootPath}");
        }

        foreach (var sourcePath in Directory.EnumerateFiles(
                     backendRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            AddPhysicalSource(repositoryRoot, sourcePath, sources, definitionErrors);
        }

        ValidateNoUnsupportedBackendInputs(
            repositoryRoot,
            backendRoot,
            definitionErrors);

        var projectDocuments = new List<BackendProjectDocument>();
        foreach (var projectPath in Directory.EnumerateFiles(
                     backendRoot,
                     "*",
                     SearchOption.AllDirectories)
                 .Where(path => string.Equals(
                     Path.GetExtension(path),
                     ".csproj",
                     StringComparison.Ordinal)))
        {
            var repositoryProjectPath = NormalizeRepositoryPath(
                Path.GetRelativePath(repositoryRoot, projectPath));
            if (IsExcludedCounterpartyPath(repositoryProjectPath)
                || IsBuildOutput(repositoryProjectPath))
            {
                continue;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(projectPath, LoadOptions.SetLineInfo);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.Xml.XmlException)
            {
                definitionErrors.Add(
                    $"Could not parse backend project {repositoryProjectPath}: "
                    + exception.Message);
                continue;
            }

            projectDocuments.Add(new BackendProjectDocument(
                projectPath,
                repositoryProjectPath,
                document));
        }

        var parsedProjectPaths = projectDocuments
            .Select(project => project.RepositoryPath)
            .ToHashSet(StringComparer.Ordinal);
        projectPaths.AddRange(parsedProjectPaths);

        foreach (var project in projectDocuments)
        {
            CollectAutomaticBuildDefinitionPaths(
                repositoryRoot,
                project.FullPath,
                automaticDefinitionPaths,
                definitionErrors);
            var analysis = AnalyzeProjectDefinition(
                project.Document,
                project.RepositoryPath);
            definitionErrors.AddRange(analysis.Errors);
            ValidateProjectReferences(
                repositoryRoot,
                backendRoot,
                project.FullPath,
                project.Document,
                parsedProjectPaths,
                definitionErrors);
            AddExplicitCompileIncludes(
                repositoryRoot,
                project.FullPath,
                analysis.CompileDirectives,
                sources,
                definitionErrors);
        }

        ValidateAutomaticBuildDefinitions(
            repositoryRoot,
            automaticDefinitionPaths,
            definitionErrors);
        ValidateGeneratedViewFiles(backendRoot, repositoryRoot, definitionErrors);

        return new BackendSourceInventory(
            sources,
            projectPaths.Order(StringComparer.Ordinal).ToArray(),
            definitionErrors);
    }

    private static void ValidateNoUnsupportedBackendInputs(
        string repositoryRoot,
        string backendRoot,
        ICollection<string> errors)
    {
        foreach (var path in Directory.EnumerateFiles(
                     backendRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            var repositoryPath = NormalizeRepositoryPath(
                Path.GetRelativePath(repositoryRoot, path));
            if (IsBuildOutput(repositoryPath)
                || IsExcludedCounterpartyPath(repositoryPath))
            {
                continue;
            }

            var extension = Path.GetExtension(path);
            if (extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".csproj", StringComparison.Ordinal))
            {
                errors.Add(
                    $"Unsupported backend project input '{extension}' is outside the reviewed "
                    + $"C# production-source inventory: {repositoryPath}");
            }

            if (UnsupportedManagedSourceExtensions.Contains(extension))
            {
                errors.Add(
                    $"Unsupported managed source input '{extension}' is outside the reviewed "
                    + $"C# production-source inventory: {repositoryPath}");
            }

            if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".cs", StringComparison.Ordinal))
            {
                errors.Add(
                    $"Non-canonical C# source extension '{extension}' is not guaranteed to "
                    + $"participate in the same compiled-input inventory on every platform: "
                    + repositoryPath);
            }
        }
    }

    private static void AddPhysicalSource(
        string repositoryRoot,
        string sourcePath,
        IDictionary<string, string> sources,
        ICollection<string> errors)
    {
        var repositoryPath = NormalizeRepositoryPath(
            Path.GetRelativePath(repositoryRoot, sourcePath));
        if (IsBuildOutput(repositoryPath) || IsExcludedCounterpartyPath(repositoryPath))
        {
            return;
        }

        if (!IsUnderDirectory(repositoryRoot, Path.GetFullPath(sourcePath)))
        {
            errors.Add($"Backend source is outside the repository: {sourcePath}");
            return;
        }

        if (!sources.TryAdd(repositoryPath, File.ReadAllText(sourcePath)))
        {
            errors.Add($"Backend source was scanned more than once: {repositoryPath}");
        }
    }

    private static void ValidateProjectReferences(
        string repositoryRoot,
        string backendRoot,
        string projectPath,
        XDocument document,
        IReadOnlySet<string> parsedProjectPaths,
        ICollection<string> errors)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var repositoryProjectPath = NormalizeRepositoryPath(
            Path.GetRelativePath(repositoryRoot, projectPath));

        foreach (var projectReference in document.Descendants()
                     .Where(element => element.Name.LocalName == "ProjectReference"))
        {
            var include = projectReference.Attribute("Include")?.Value?.Trim();
            if (include is null
                || HasMsBuildIndirection(include)
                || projectReference.Attribute("Condition") is not null
                || projectReference.Ancestors()
                    .Any(ancestor => ancestor.Attribute("Condition") is not null))
            {
                errors.Add(
                    "ProjectReference must be a literal unconditional backend project path: "
                    + repositoryProjectPath);
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(
                projectDirectory,
                ToNativeFileSystemPath(include)));
            if (!IsUnderDirectory(repositoryRoot, targetPath)
                || !IsUnderDirectory(backendRoot, targetPath)
                || !File.Exists(targetPath))
            {
                errors.Add(
                    $"ProjectReference target is missing or outside src/backend: {include} "
                    + $"({repositoryProjectPath})");
                continue;
            }

            var targetRepositoryPath = NormalizeRepositoryPath(
                Path.GetRelativePath(repositoryRoot, targetPath));
            if (!string.Equals(
                    Path.GetExtension(targetPath),
                    ".csproj",
                    StringComparison.Ordinal))
            {
                errors.Add(
                    "ProjectReference target must be a canonical .csproj in the reviewed "
                    + $"C# inventory: {targetRepositoryPath} ({repositoryProjectPath})");
                continue;
            }

            if (IsExcludedCounterpartyPath(targetRepositoryPath))
            {
                errors.Add(
                    "A non-Counterparty project cannot reference the excluded Counterparty "
                    + $"ownership scope without a separate reviewed guard: "
                    + $"{targetRepositoryPath} ({repositoryProjectPath})");
                continue;
            }

            if (!parsedProjectPaths.Contains(targetRepositoryPath))
            {
                errors.Add(
                    "ProjectReference target was not successfully parsed into the reviewed "
                    + $"C# project inventory: {targetRepositoryPath} "
                    + $"({repositoryProjectPath})");
            }
        }
    }

    private static ProjectDefinitionAnalysis AnalyzeProjectDefinition(
        XDocument document,
        string repositoryProjectPath)
    {
        var errors = new List<string>();
        var directives = new List<CompileDirective>();
        var root = document.Root;

        if (root is null || root.Name.LocalName != "Project")
        {
            errors.Add($"Backend project has no Project root: {repositoryProjectPath}");
            return new ProjectDefinitionAnalysis(directives, errors);
        }

        var sdk = root.Attribute("Sdk")?.Value?.Trim();
        if (sdk is null || !ReviewedProjectSdks.Contains(sdk))
        {
            errors.Add(
                $"Backend project uses an unreviewed SDK '{sdk ?? "<missing>"}': "
                + repositoryProjectPath);
        }
        ValidateAllowedAttributes(
            root,
            new HashSet<string>(StringComparer.Ordinal) { "Sdk" },
            "Project",
            repositoryProjectPath,
            errors);

        foreach (var topLevel in root.Elements())
        {
            if (topLevel.Name.LocalName is not ("ItemGroup" or "PropertyGroup"))
            {
                errors.Add(
                    $"Unreviewed top-level MSBuild element {topLevel.Name.LocalName}: "
                    + repositoryProjectPath);
            }
        }

        foreach (var propertyGroup in root.Elements()
                     .Where(element => element.Name.LocalName == "PropertyGroup"))
        {
            ValidateAllowedAttributes(
                propertyGroup,
                new HashSet<string>(StringComparer.Ordinal),
                "PropertyGroup",
                repositoryProjectPath,
                errors);
            foreach (var property in propertyGroup.Elements())
            {
                ValidateAllowedAttributes(
                    property,
                    new HashSet<string>(StringComparer.Ordinal),
                    property.Name.LocalName,
                    repositoryProjectPath,
                    errors);
                if (!ReviewedProjectProperties.Contains(property.Name.LocalName))
                {
                    errors.Add(
                        $"Unreviewed project property {property.Name.LocalName} can alter "
                        + $"compilation: {repositoryProjectPath}");
                }
                if (HasMsBuildIndirection(property.Value))
                {
                    errors.Add(
                        $"Project property {property.Name.LocalName} contains MSBuild "
                        + $"indirection: {repositoryProjectPath}");
                }
            }
        }

        foreach (var itemGroup in root.Elements()
                     .Where(element => element.Name.LocalName == "ItemGroup"))
        {
            ValidateAllowedAttributes(
                itemGroup,
                new HashSet<string>(StringComparer.Ordinal),
                "ItemGroup",
                repositoryProjectPath,
                errors);
            foreach (var item in itemGroup.Elements())
            {
                if (!ReviewedProjectItems.Contains(item.Name.LocalName))
                {
                    errors.Add(
                        $"Unreviewed project item {item.Name.LocalName} can alter compilation: "
                        + repositoryProjectPath);
                }
            }
        }

        foreach (var element in root.Descendants())
        {
            var localName = element.Name.LocalName;
            if (localName is "Import" or "Target" or "Sdk")
            {
                errors.Add(
                    $"Unreviewed MSBuild {localName} can alter compiled production input: "
                    + repositoryProjectPath);
            }

            if (ForbiddenGeneratedSourceItems.Contains(localName))
            {
                errors.Add(
                    $"Unreviewed generated-source item {localName} is forbidden: "
                    + repositoryProjectPath);
            }

            if (localName.Contains("Generator", StringComparison.OrdinalIgnoreCase)
                || localName.Contains("Razor", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Unreviewed generator/Razor property or item {localName} is forbidden: "
                    + repositoryProjectPath);
            }
        }

        foreach (var packageReference in root.Descendants()
                     .Where(element => element.Name.LocalName == "PackageReference"))
        {
            ValidatePackageReference(packageReference, repositoryProjectPath, errors);
        }

        foreach (var projectReference in root.Descendants()
                     .Where(element => element.Name.LocalName == "ProjectReference"))
        {
            ValidateAllowedAttributes(
                projectReference,
                new HashSet<string>(StringComparer.Ordinal) { "Include" },
                "ProjectReference",
                repositoryProjectPath,
                errors);
            if (projectReference.Elements().Any())
            {
                errors.Add(
                    "ProjectReference metadata requires explicit compilation review: "
                    + repositoryProjectPath);
            }
            if (HasMetadata(projectReference, "OutputItemType")
                || HasMetadata(projectReference, "Generator")
                || HasMetadata(projectReference, "ReferenceSourceTarget"))
            {
                errors.Add(
                    "ProjectReference cannot act as an analyzer/source generator without review: "
                    + repositoryProjectPath);
            }
        }

        foreach (var compile in root.Descendants()
                     .Where(element => element.Name.LocalName == "Compile"))
        {
            var directive = AnalyzeCompileDirective(
                compile,
                repositoryProjectPath,
                errors);
            if (directive is not null)
            {
                directives.Add(directive);
            }
        }

        return new ProjectDefinitionAnalysis(directives, errors);
    }

    private static void ValidatePackageReference(
        XElement packageReference,
        string repositoryProjectPath,
        ICollection<string> errors)
    {
        var include = packageReference.Attribute("Include")?.Value?.Trim();
        var version = packageReference.Attribute("Version")?.Value?.Trim()
                      ?? packageReference.Elements()
                          .SingleOrDefault(element => element.Name.LocalName == "Version")
                          ?.Value.Trim();
        ValidateAllowedAttributes(
            packageReference,
            new HashSet<string>(StringComparer.Ordinal) { "Include", "Version" },
            "PackageReference",
            repositoryProjectPath,
            errors);

        if (include is null
            || version is null
            || !ReviewedPackageReferences.TryGetValue(include, out var reviewedVersion)
            || !string.Equals(version, reviewedVersion, StringComparison.Ordinal))
        {
            errors.Add(
                $"Unreviewed package/source-generator route '{include ?? "<missing>"}' "
                + $"version '{version ?? "<missing>"}': {repositoryProjectPath}");
        }

        if (packageReference.Attribute("Condition") is not null
            || packageReference.Ancestors()
                .Any(ancestor => ancestor.Attribute("Condition") is not null)
            || packageReference.Attribute("Update") is not null
            || packageReference.Attribute("Remove") is not null
            || HasMetadata(packageReference, "OutputItemType")
            || HasMetadata(packageReference, "GeneratePathProperty")
            || HasMetadata(packageReference, "Generator"))
        {
            errors.Add(
                "Conditional or generator-shaped PackageReference is forbidden without review: "
                + repositoryProjectPath);
        }

        var allowedMetadata = new HashSet<string>(StringComparer.Ordinal)
        {
            "ExcludeAssets",
            "IncludeAssets",
            "PrivateAssets",
            "Version"
        };
        var unreviewedMetadata = packageReference.Elements()
            .Select(element => element.Name.LocalName)
            .Where(name => !allowedMetadata.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unreviewedMetadata.Length > 0)
        {
            errors.Add(
                "Unreviewed PackageReference metadata can alter compile inputs: "
                + string.Join(", ", unreviewedMetadata)
                + $" ({repositoryProjectPath})");
        }
    }

    private static CompileDirective? AnalyzeCompileDirective(
        XElement compile,
        string repositoryProjectPath,
        ICollection<string> errors)
    {
        ValidateAllowedAttributes(
            compile,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Condition",
                "Exclude",
                "Include",
                "Remove",
                "Update"
            },
            "Compile",
            repositoryProjectPath,
            errors);
        if (compile.Attribute("Condition") is not null
            || compile.Ancestors().Any(ancestor => ancestor.Attribute("Condition") is not null))
        {
            errors.Add(
                "Conditional Compile items require evaluated MSBuild review: "
                + repositoryProjectPath);
        }

        var candidates = new[]
        {
            (Kind: CompileDirectiveKind.Include, Value: compile.Attribute("Include")?.Value),
            (Kind: CompileDirectiveKind.Remove, Value: compile.Attribute("Remove")?.Value),
            (Kind: CompileDirectiveKind.Update, Value: compile.Attribute("Update")?.Value)
        }.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Value)).ToArray();

        if (candidates.Length != 1)
        {
            errors.Add(
                "Compile item must have exactly one literal Include, Remove, or Update: "
                + repositoryProjectPath);
            return null;
        }

        var items = SplitLiteralMsBuildItems(
            candidates[0].Value!,
            "Compile",
            repositoryProjectPath,
            errors);
        var exclude = compile.Attribute("Exclude")?.Value;
        var excludes = string.IsNullOrWhiteSpace(exclude)
            ? Array.Empty<string>()
            : SplitLiteralMsBuildItems(
                exclude,
                "Compile Exclude",
                repositoryProjectPath,
                errors);
        var link = compile.Elements()
            .SingleOrDefault(element => element.Name.LocalName == "Link")
            ?.Value.Trim();
        if (link is not null && HasMsBuildIndirection(link))
        {
            errors.Add(
                $"Compile Link contains MSBuild indirection '{link}': {repositoryProjectPath}");
        }

        var allowedMetadata = new HashSet<string>(StringComparer.Ordinal)
        {
            "AutoGen",
            "CopyToOutputDirectory",
            "DependentUpon",
            "DesignTime",
            "Link",
            "Visible"
        };
        var unreviewedMetadata = compile.Elements()
            .Select(element => element.Name.LocalName)
            .Where(name => !allowedMetadata.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unreviewedMetadata.Length > 0)
        {
            errors.Add(
                "Unreviewed Compile metadata can alter generated inputs: "
                + string.Join(", ", unreviewedMetadata)
                + $" ({repositoryProjectPath})");
        }

        return new CompileDirective(candidates[0].Kind, items, excludes, link);
    }

    private static string[] SplitLiteralMsBuildItems(
        string value,
        string kind,
        string repositoryProjectPath,
        ICollection<string> errors)
    {
        var items = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length == 0)
        {
            errors.Add($"{kind} contains no paths: {repositoryProjectPath}");
        }

        foreach (var item in items.Where(HasMsBuildIndirection))
        {
            errors.Add(
                $"{kind} contains unsupported MSBuild indirection '{item}': "
                + repositoryProjectPath);
        }

        return items;
    }

    private static bool HasMsBuildIndirection(string value)
    {
        return value.IndexOfAny(['*', '?']) >= 0
               || value.Contains("$(", StringComparison.Ordinal)
               || value.Contains("@(", StringComparison.Ordinal)
               || value.Contains("%(", StringComparison.Ordinal);
    }

    private static bool HasMetadata(XElement element, string localName)
    {
        return element.Attribute(localName) is not null
               || element.Elements().Any(child => child.Name.LocalName == localName);
    }

    private static void ValidateAllowedAttributes(
        XElement element,
        IReadOnlySet<string> allowedAttributes,
        string itemKind,
        string repositoryProjectPath,
        ICollection<string> errors)
    {
        var unreviewed = element.Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration)
            .Select(attribute => attribute.Name.LocalName)
            .Where(name => !allowedAttributes.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unreviewed.Length > 0)
        {
            errors.Add(
                $"Unreviewed {itemKind} attributes can alter compile inputs: "
                + string.Join(", ", unreviewed)
                + $" ({repositoryProjectPath})");
        }
    }

    private static void AddExplicitCompileIncludes(
        string repositoryRoot,
        string projectPath,
        IReadOnlyList<CompileDirective> directives,
        IDictionary<string, string> sources,
        ICollection<string> errors)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;

        foreach (var directive in directives.Where(
                     directive => directive.Kind == CompileDirectiveKind.Include))
        {
            var excluded = directive.Excludes
                .Select(path => NormalizeRepositoryPath(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var item in directive.Items)
            {
                if (HasMsBuildIndirection(item)
                    || excluded.Contains(NormalizeRepositoryPath(item)))
                {
                    continue;
                }

                var nativeItem = ToNativeFileSystemPath(item);
                if (!Path.GetExtension(nativeItem).Equals(".cs", StringComparison.Ordinal))
                {
                    errors.Add(
                        $"Explicit Compile Include must use the canonical .cs source extension "
                        + $"and cannot use '{item}': "
                        + NormalizeRepositoryPath(
                            Path.GetRelativePath(repositoryRoot, projectPath)));
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(
                    projectDirectory,
                    nativeItem));
                if (!IsUnderDirectory(repositoryRoot, fullPath) || !File.Exists(fullPath))
                {
                    errors.Add(
                        $"Explicit Compile source is missing or outside the repository: {item} "
                        + $"({NormalizeRepositoryPath(Path.GetRelativePath(repositoryRoot, projectPath))})");
                    continue;
                }

                var repositoryPath = NormalizeRepositoryPath(
                    Path.GetRelativePath(repositoryRoot, fullPath));
                if (IsExcludedCounterpartyPath(repositoryPath))
                {
                    errors.Add(
                        "A non-Counterparty project cannot link excluded Counterparty source: "
                        + repositoryPath);
                    continue;
                }

                if (Path.GetExtension(fullPath).Equals(".cs", StringComparison.OrdinalIgnoreCase)
                    && !sources.ContainsKey(repositoryPath))
                {
                    sources.Add(repositoryPath, File.ReadAllText(fullPath));
                }
            }
        }
    }

    private static void CollectAutomaticBuildDefinitionPaths(
        string repositoryRoot,
        string projectPath,
        ISet<string> automaticDefinitionPaths,
        ICollection<string> errors)
    {
        var userPath = projectPath + ".user";
        if (File.Exists(userPath))
        {
            automaticDefinitionPaths.Add(userPath);
        }

        var current = new DirectoryInfo(Path.GetDirectoryName(projectPath)!);
        var reachedRepositoryRoot = false;
        while (current is not null && IsUnderDirectory(repositoryRoot, current.FullName))
        {
            foreach (var fileName in new[]
                     {
                         "Directory.Build.props",
                         "Directory.Build.targets",
                         "Directory.Packages.props"
                     })
            {
                var candidate = Path.Combine(current.FullName, fileName);
                if (File.Exists(candidate))
                {
                    automaticDefinitionPaths.Add(candidate);
                }
            }

            if (Path.GetRelativePath(repositoryRoot, current.FullName) == ".")
            {
                reachedRepositoryRoot = true;
                break;
            }

            current = current.Parent;
        }

        if (!reachedRepositoryRoot)
        {
            errors.Add(
                "Could not enumerate automatic MSBuild ancestors from project to repository root: "
                + NormalizeRepositoryPath(Path.GetRelativePath(repositoryRoot, projectPath)));
        }
    }

    private static void ValidateAutomaticBuildDefinitions(
        string repositoryRoot,
        IEnumerable<string> definitionPaths,
        ICollection<string> errors)
    {
        foreach (var definitionPath in definitionPaths.Order(StringComparer.OrdinalIgnoreCase))
        {
            var repositoryPath = NormalizeRepositoryPath(
                Path.GetRelativePath(repositoryRoot, definitionPath));
            if (!IsUnderDirectory(repositoryRoot, Path.GetFullPath(definitionPath)))
            {
                errors.Add(
                    "Automatic MSBuild definition is outside the repository: "
                    + definitionPath);
                continue;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(definitionPath, LoadOptions.SetLineInfo);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.Xml.XmlException)
            {
                errors.Add(
                    $"Could not parse automatic MSBuild definition {repositoryPath}: "
                    + exception.Message);
                continue;
            }

            var fileName = Path.GetFileName(definitionPath);
            if (fileName.EndsWith(".csproj.user", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    "Automatic project .csproj.user definition is forbidden without review: "
                    + repositoryPath);
                continue;
            }

            if (fileName.Equals("Directory.Build.targets", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    "Automatic Directory.Build.targets is forbidden without generated-input "
                    + $"review: {repositoryPath}");
                continue;
            }

            if (fileName.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    "Automatic Directory.Packages.props is forbidden because package/source-"
                    + $"generator inputs are exact-allowlisted: {repositoryPath}");
                continue;
            }

            ValidateReviewedDirectoryBuildProps(document, repositoryPath, errors);
        }
    }

    private static void ValidateReviewedDirectoryBuildProps(
        XDocument document,
        string repositoryPath,
        ICollection<string> errors)
    {
        var root = document.Root;
        if (root is null || root.Name.LocalName != "Project" || root.Attributes().Any())
        {
            errors.Add(
                "Repository build props must have an attribute-free Project root: "
                + repositoryPath);
            return;
        }

        foreach (var topLevel in root.Elements())
        {
            if (topLevel.Name.LocalName != "PropertyGroup" || topLevel.Attributes().Any())
            {
                errors.Add(
                    $"Unreviewed repository build element {topLevel.Name.LocalName}: "
                    + repositoryPath);
                continue;
            }

            foreach (var property in topLevel.Elements())
            {
                if (!ReviewedRepositoryBuildProperties.Contains(property.Name.LocalName)
                    || property.Attributes().Any()
                    || property.Elements().Any()
                    || HasMsBuildIndirection(property.Value))
                {
                    errors.Add(
                        $"Unreviewed repository build property {property.Name.LocalName}: "
                        + repositoryPath);
                }
            }
        }

        foreach (var element in document.Descendants())
        {
            var localName = element.Name.LocalName;
            if (localName is "Compile" or "Import" or "PackageReference" or "Target"
                || ForbiddenGeneratedSourceItems.Contains(localName)
                || localName.Contains("Generator", StringComparison.OrdinalIgnoreCase)
                || localName.Contains("Razor", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Repository build definition has unreviewed compile/generator input "
                    + $"{localName}: {repositoryPath}");
            }
        }
    }

    private static void ValidateGeneratedViewFiles(
        string backendRoot,
        string repositoryRoot,
        ICollection<string> errors)
    {
        foreach (var pattern in new[] { "*.razor", "*.cshtml" })
        {
            foreach (var path in Directory.EnumerateFiles(
                         backendRoot,
                         pattern,
                         SearchOption.AllDirectories))
            {
                var repositoryPath = NormalizeRepositoryPath(
                    Path.GetRelativePath(repositoryRoot, path));
                if (!IsBuildOutput(repositoryPath)
                    && !IsExcludedCounterpartyPath(repositoryPath))
                {
                    errors.Add(
                        "Generated Razor/C# source requires explicit tenant-guard review: "
                        + repositoryPath);
                }
            }
        }
    }

    private static IReadOnlyList<string> ValidateNonVacuousScope(
        BackendSourceInventory inventory)
    {
        var errors = new List<string>();
        if (inventory.ProjectPaths.Count == 0 || inventory.Sources.Count == 0)
        {
            errors.Add("Backend project/source inventory is empty.");
        }

        foreach (var requiredSource in new[]
                 {
                     "src/backend/GoymenERP.Api/Program.cs",
                     "src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/PermissionScope.cs",
                     AllowedSeederPath
                 })
        {
            if (!inventory.Sources.ContainsKey(requiredSource))
            {
                errors.Add($"Required backend scope source was not scanned: {requiredSource}");
            }
        }

        var excluded = inventory.Sources.Keys
            .Where(IsExcludedCounterpartyPath)
            .ToArray();
        if (excluded.Length > 0)
        {
            errors.Add(
                "Counterparty ownership sources entered the platform guard scope:\n"
                + string.Join("\n", excluded.Select(path => $"- {path}")));
        }

        return errors;
    }

    private static IReadOnlyList<BypassOccurrence> FindOccurrences(
        IReadOnlyDictionary<string, string> sources)
    {
        var occurrences = new List<BypassOccurrence>();

        foreach (var (path, source) in sources)
        {
            var normalizedPath = NormalizeRepositoryPath(path);
            var searchableSource = NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(source));

            foreach (Match match in IgnoreQueryFiltersIdentifierRegex().Matches(searchableSource))
            {
                occurrences.Add(new BypassOccurrence(
                    normalizedPath,
                    GetLineNumber(searchableSource, match.Index)));
            }
        }

        return occurrences
            .OrderBy(occurrence => occurrence.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(occurrence => occurrence.LineNumber)
            .ToArray();
    }

    private static IReadOnlyList<string> ValidateExactAllowlist(
        IReadOnlyList<BypassOccurrence> occurrences,
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();
        var unauthorized = occurrences
            .Where(occurrence => !string.Equals(
                occurrence.RepositoryPath,
                AllowedSeederPath,
                StringComparison.Ordinal))
            .ToArray();

        if (unauthorized.Length > 0)
        {
            errors.Add(
                "Unauthorized IgnoreQueryFilters calls were found in the platform production scope:\n"
                + string.Join("\n", unauthorized.Select(occurrence => $"- {occurrence}")));
        }

        var allowedCount = occurrences.Count(occurrence => string.Equals(
            occurrence.RepositoryPath,
            AllowedSeederPath,
            StringComparison.Ordinal));

        if (allowedCount != ExpectedSeederBypassCount)
        {
            errors.Add(
                $"Expected exactly {ExpectedSeederBypassCount} IgnoreQueryFilters calls in "
                + $"{AllowedSeederPath}, but found {allowedCount}.");
        }

        var exactSeeder = sources
            .Where(pair => string.Equals(pair.Key, AllowedSeederPath, StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .SingleOrDefault();
        var actualFingerprint = exactSeeder is null
            ? "<missing>"
            : ComputeReviewedSourceFingerprint(exactSeeder);
        if (!string.Equals(
                actualFingerprint,
                ReviewedSeederFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                $"Authorized seeder does not match the reviewed fingerprint. Expected "
                + $"{ReviewedSeederFingerprint}, found {actualFingerprint}: {AllowedSeederPath}");
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateNoForbiddenBypassCapabilities(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();

        foreach (var (path, source) in sources)
        {
            var searchableSource = NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(source));
            foreach (Match match in ForbiddenBypassCapabilityRegex().Matches(searchableSource))
            {
                errors.Add(
                    $"Forbidden reflection/dynamic bypass capability '{match.Value}' at "
                    + $"{NormalizeRepositoryPath(path)}:{GetLineNumber(searchableSource, match.Index)}");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateNoDirectDatabaseBypassCapabilities(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();

        foreach (var (path, source) in sources)
        {
            var normalizedPath = NormalizeRepositoryPath(path);
            var searchableSource = NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(source));
            var sourceCategory = DescribeSourceCategory(ClassifySource(normalizedPath));

            foreach (Match match in DirectDatabaseBypassCapabilityRegex().Matches(
                         searchableSource))
            {
                errors.Add(
                    "Forbidden direct database/query-filter bypass capability "
                    + $"'{match.Value}' in {sourceCategory} source at "
                    + $"{normalizedPath}:{GetLineNumber(searchableSource, match.Index)}");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateReviewedMigrationMutationSurface(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();
        var occurrences = FindMigrationMutationOccurrences(sources);

        foreach (var occurrence in occurrences)
        {
            if (occurrence.SourceCategory != PlatformSourceCategory.Migration)
            {
                errors.Add(
                    $"Migration mutation API '{occurrence.ApiName}' is forbidden outside "
                    + $"migration source: {occurrence.RepositoryPath}:{occurrence.LineNumber}");
                continue;
            }

            if (!ReviewedMigrationMutationSources.TryGetValue(
                    occurrence.RepositoryPath,
                    out var reviewed)
                || !reviewed.ExpectedApiOccurrences.ContainsKey(occurrence.ApiName))
            {
                errors.Add(
                    $"Unreviewed migration mutation API '{occurrence.ApiName}' at "
                    + $"{occurrence.RepositoryPath}:{occurrence.LineNumber}");
            }
        }

        foreach (var (reviewedPath, reviewed) in ReviewedMigrationMutationSources)
        {
            if (!sources.TryGetValue(reviewedPath, out var source))
            {
                errors.Add($"Reviewed migration mutation source is missing: {reviewedPath}");
                continue;
            }

            if (ClassifySource(reviewedPath) != PlatformSourceCategory.Migration)
            {
                errors.Add(
                    "Reviewed migration mutation source unexpectedly left the migration "
                    + $"category: {reviewedPath}");
            }

            var actualFingerprint = ComputeReviewedSourceFingerprint(source);
            if (!string.Equals(
                    actualFingerprint,
                    reviewed.ReviewedFingerprint,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    "Migration mutation source does not match the reviewed fingerprint. "
                    + $"Expected {reviewed.ReviewedFingerprint}, found {actualFingerprint}: "
                    + reviewedPath);
            }

            var sourceOccurrences = occurrences
                .Where(occurrence => string.Equals(
                    occurrence.RepositoryPath,
                    reviewedPath,
                    StringComparison.Ordinal))
                .ToArray();
            foreach (var (apiName, expectedCount) in reviewed.ExpectedApiOccurrences)
            {
                var actualCount = sourceOccurrences.Count(occurrence => string.Equals(
                    occurrence.ApiName,
                    apiName,
                    StringComparison.Ordinal));
                if (actualCount != expectedCount)
                {
                    errors.Add(
                        $"Expected exactly {expectedCount} reviewed migration '{apiName}' "
                        + $"occurrence(s), but found {actualCount}: {reviewedPath}");
                }
            }
        }

        return errors;
    }

    private static IReadOnlyList<MigrationMutationOccurrence>
        FindMigrationMutationOccurrences(IReadOnlyDictionary<string, string> sources)
    {
        var occurrences = new List<MigrationMutationOccurrence>();

        foreach (var (path, source) in sources)
        {
            var normalizedPath = NormalizeRepositoryPath(path);
            var searchableSource = NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(source));
            foreach (Match match in MigrationMutationCapabilityRegex().Matches(
                         searchableSource))
            {
                occurrences.Add(new MigrationMutationOccurrence(
                    normalizedPath,
                    match.Groups["api"].Value,
                    GetLineNumber(searchableSource, match.Index),
                    ClassifySource(normalizedPath)));
            }
        }

        return occurrences
            .OrderBy(occurrence => occurrence.RepositoryPath, StringComparer.Ordinal)
            .ThenBy(occurrence => occurrence.LineNumber)
            .ThenBy(occurrence => occurrence.ApiName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ValidateReviewedPlatformDbContextInventory(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();
        var declarations = new List<DbContextDeclaration>();

        foreach (var (path, source) in sources)
        {
            var normalizedPath = NormalizeRepositoryPath(path);
            var searchableSource = NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(source));

            foreach (Match alias in DbContextBaseTypeAliasRegex().Matches(searchableSource))
            {
                errors.Add(
                    $"DbContext base-type alias '{alias.Groups["alias"].Value}' is forbidden "
                    + "because it can hide an unreviewed filterless context at "
                    + $"{normalizedPath}:{GetLineNumber(searchableSource, alias.Index)}");
            }

            foreach (Match declaration in DbContextDeclarationRegex().Matches(
                         searchableSource))
            {
                declarations.Add(new DbContextDeclaration(
                    normalizedPath,
                    declaration.Groups["name"].Value.TrimStart('@'),
                    GetLineNumber(searchableSource, declaration.Index),
                    ClassifySource(normalizedPath)));
            }
        }

        foreach (var declaration in declarations.Where(
                     declaration => !ReviewedPlatformDbContexts.ContainsKey(
                         declaration.RepositoryPath)))
        {
            errors.Add(
                $"Unreviewed platform DbContext declaration '{declaration.TypeName}' in "
                + $"{DescribeSourceCategory(declaration.SourceCategory)} source at "
                + $"{declaration.RepositoryPath}:{declaration.LineNumber}");
        }

        foreach (var (reviewedPath, reviewedFingerprint) in ReviewedPlatformDbContexts)
        {
            if (!sources.TryGetValue(reviewedPath, out var source))
            {
                errors.Add($"Reviewed platform DbContext source is missing: {reviewedPath}");
                continue;
            }

            var expectedTypeName = Path.GetFileNameWithoutExtension(reviewedPath);
            var pathDeclarations = declarations
                .Where(declaration => string.Equals(
                    declaration.RepositoryPath,
                    reviewedPath,
                    StringComparison.Ordinal))
                .ToArray();

            if (pathDeclarations.Length != 1
                || !string.Equals(
                    pathDeclarations[0].TypeName,
                    expectedTypeName,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"Expected exactly one reviewed DbContext declaration '{expectedTypeName}' "
                    + $"at {reviewedPath}, but found "
                    + (pathDeclarations.Length == 0
                        ? "none."
                        : string.Join(
                            ", ",
                            pathDeclarations.Select(declaration => declaration.TypeName))
                          + "."));
            }

            if (ClassifySource(reviewedPath) != PlatformSourceCategory.RuntimeApplication)
            {
                errors.Add(
                    "Reviewed platform DbContext unexpectedly left the runtime application "
                    + $"source category: {reviewedPath}");
            }

            var actualFingerprint = ComputeReviewedSourceFingerprint(source);
            if (!string.Equals(
                    actualFingerprint,
                    reviewedFingerprint,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"Platform DbContext does not match the reviewed fingerprint. Expected "
                    + $"{reviewedFingerprint}, found {actualFingerprint}: {reviewedPath}");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateReviewedPlatformContextConstructions(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();
        var constructions = new List<ReviewedContextConstruction>();

        foreach (var (path, source) in sources)
        {
            var normalizedPath = NormalizeRepositoryPath(path);
            var searchableSource = NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(source));

            foreach (Match factoryReference in ReviewedDesignTimeFactoryIdentifierRegex()
                         .Matches(searchableSource))
            {
                var isInsideReviewedFactory =
                    ReviewedPlatformContextFactories.ContainsKey(normalizedPath);
                if (!isInsideReviewedFactory)
                {
                    errors.Add(
                        "Unauthorized design-time DbContext factory reference "
                        + $"'{factoryReference.Value}' at "
                        + $"{normalizedPath}:"
                        + GetLineNumber(searchableSource, factoryReference.Index));
                }
            }

            foreach (Match alias in ReviewedContextTypeAliasRegex().Matches(searchableSource))
            {
                errors.Add(
                    $"Reviewed platform DbContext type alias '{alias.Groups["alias"].Value}' "
                    + $"for '{alias.Groups["type"].Value}' is forbidden because it can hide "
                    + "an unauthorized construction at "
                    + $"{normalizedPath}:{GetLineNumber(searchableSource, alias.Index)}");
            }

            foreach (Match construction in ReviewedContextConstructionRegex().Matches(
                         searchableSource))
            {
                constructions.Add(new ReviewedContextConstruction(
                    normalizedPath,
                    construction.Groups["type"].Value,
                    GetLineNumber(searchableSource, construction.Index)));
            }

            if (!ReviewedPlatformContextFactories.ContainsKey(normalizedPath))
            {
                var reviewedTypes = ReviewedContextTypeIdentifierRegex()
                    .Matches(searchableSource)
                    .Select(match => match.Groups["type"].Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                foreach (Match targetTypedNew in TargetTypedNewRegex().Matches(
                             searchableSource))
                {
                    foreach (var reviewedType in reviewedTypes)
                    {
                        constructions.Add(new ReviewedContextConstruction(
                            normalizedPath,
                            reviewedType,
                            GetLineNumber(searchableSource, targetTypedNew.Index)));
                    }
                }
            }
        }

        foreach (var construction in constructions)
        {
            var authorized = ReviewedPlatformContextFactories.TryGetValue(
                                 construction.RepositoryPath,
                                 out var factory)
                             && string.Equals(
                                 construction.ContextTypeName,
                                 factory.ContextTypeName,
                                 StringComparison.Ordinal);
            if (!authorized)
            {
                errors.Add(
                    "Unauthorized reviewed platform DbContext construction "
                    + $"'{construction.ContextTypeName}' at "
                    + $"{construction.RepositoryPath}:{construction.LineNumber}");
            }
        }

        foreach (var (factoryPath, factory) in ReviewedPlatformContextFactories)
        {
            if (!sources.TryGetValue(factoryPath, out var source))
            {
                errors.Add($"Reviewed platform design-time context factory is missing: {factoryPath}");
                continue;
            }

            if (ClassifySource(factoryPath) != PlatformSourceCategory.DesignTime)
            {
                errors.Add(
                    "Reviewed platform context factory unexpectedly left the design-time "
                    + $"source category: {factoryPath}");
            }

            var factoryConstructions = constructions
                .Where(construction => string.Equals(
                    construction.RepositoryPath,
                    factoryPath,
                    StringComparison.Ordinal))
                .ToArray();
            if (factoryConstructions.Length != 1
                || !string.Equals(
                    factoryConstructions[0].ContextTypeName,
                    factory.ContextTypeName,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"Expected exactly one '{factory.ContextTypeName}' construction in reviewed "
                    + $"design-time factory {factoryPath}, but found "
                    + (factoryConstructions.Length == 0
                        ? "none."
                        : string.Join(
                            ", ",
                            factoryConstructions.Select(
                                construction => construction.ContextTypeName))
                          + "."));
            }

            var actualFingerprint = ComputeReviewedSourceFingerprint(source);
            if (!string.Equals(
                    actualFingerprint,
                    factory.ReviewedFingerprint,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    "Platform design-time context factory does not match the reviewed "
                    + $"fingerprint. Expected {factory.ReviewedFingerprint}, found "
                    + $"{actualFingerprint}: {factoryPath}");
            }
        }

        return errors;
    }

    private static string ComputeReviewedSourceFingerprint(string source)
    {
        var normalizedLineEndings = source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var normalized = string.Join(
            '\n',
            normalizedLineEndings.Split('\n').Select(line => line.TrimEnd()))
            .TrimEnd('\n') + "\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
    }

    private static string MaskCommentsAndNonInterpolatedLiterals(string source)
    {
        var masked = source.ToCharArray();
        var index = 0;

        while (index < source.Length)
        {
            if (StartsWith(source, index, "//"))
            {
                var end = source.IndexOf('\n', index + 2);
                end = end < 0 ? source.Length : end;
                Mask(masked, index, end);
                index = end;
                continue;
            }

            if (StartsWith(source, index, "/*"))
            {
                var closing = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var end = closing < 0 ? source.Length : closing + 2;
                Mask(masked, index, end);
                index = end;
                continue;
            }

            if (TryReadRawString(source, index, out var rawEnd, out var isInterpolatedRaw))
            {
                // Interpolated strings stay searchable as a fail-closed tradeoff: their
                // expressions can execute the bypass, while ordinary/raw text is masked.
                if (!isInterpolatedRaw)
                {
                    Mask(masked, index, rawEnd);
                }

                index = rawEnd;
                continue;
            }

            if (StartsWith(source, index, "$@\"") || StartsWith(source, index, "@$\""))
            {
                index = FindInterpolatedStringEnd(source, index + 3, isVerbatim: true);
                continue;
            }

            if (StartsWith(source, index, "$\""))
            {
                index = FindInterpolatedStringEnd(source, index + 2, isVerbatim: false);
                continue;
            }

            if (StartsWith(source, index, "@\""))
            {
                var end = FindVerbatimStringEnd(source, index + 2);
                Mask(masked, index, end);
                index = end;
                continue;
            }

            if (source[index] == '"')
            {
                var end = FindRegularStringEnd(source, index + 1);
                Mask(masked, index, end);
                index = end;
                continue;
            }

            if (source[index] == '\'')
            {
                var end = FindCharacterLiteralEnd(source, index + 1);
                Mask(masked, index, end);
                index = end;
                continue;
            }

            index++;
        }

        return new string(masked);
    }

    private static bool TryReadRawString(
        string source,
        int start,
        out int end,
        out bool isInterpolated)
    {
        var cursor = start;
        var dollarCount = 0;

        while (cursor < source.Length && source[cursor] == '$')
        {
            dollarCount++;
            cursor++;
        }

        var quoteStart = cursor;
        while (cursor < source.Length && source[cursor] == '"')
        {
            cursor++;
        }

        var delimiterLength = cursor - quoteStart;
        if (delimiterLength < 3)
        {
            end = start;
            isInterpolated = false;
            return false;
        }

        isInterpolated = dollarCount > 0;
        while (cursor < source.Length)
        {
            if (source[cursor] != '"')
            {
                cursor++;
                continue;
            }

            var closingStart = cursor;
            while (cursor < source.Length && source[cursor] == '"')
            {
                cursor++;
            }

            if (cursor - closingStart >= delimiterLength)
            {
                end = cursor;
                return true;
            }
        }

        end = source.Length;
        return true;
    }

    private static int FindRegularStringEnd(string source, int contentStart)
    {
        var index = contentStart;

        while (index < source.Length)
        {
            if (source[index] == '\\')
            {
                index = Math.Min(index + 2, source.Length);
                continue;
            }

            if (source[index] == '"')
            {
                return index + 1;
            }

            index++;
        }

        return source.Length;
    }

    private static int FindInterpolatedStringEnd(
        string source,
        int contentStart,
        bool isVerbatim)
    {
        var index = contentStart;
        var interpolationDepth = 0;

        while (index < source.Length)
        {
            if (interpolationDepth == 0)
            {
                if (!isVerbatim && source[index] == '\\')
                {
                    index = Math.Min(index + 2, source.Length);
                    continue;
                }

                if (source[index] == '"')
                {
                    if (isVerbatim
                        && index + 1 < source.Length
                        && source[index + 1] == '"')
                    {
                        index += 2;
                        continue;
                    }

                    return index + 1;
                }

                if (source[index] == '{')
                {
                    if (index + 1 < source.Length && source[index + 1] == '{')
                    {
                        index += 2;
                        continue;
                    }

                    interpolationDepth = 1;
                }

                index++;
                continue;
            }

            if (StartsWith(source, index, "//"))
            {
                var lineEnd = source.IndexOf('\n', index + 2);
                index = lineEnd < 0 ? source.Length : lineEnd;
                continue;
            }

            if (StartsWith(source, index, "/*"))
            {
                var closing = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = closing < 0 ? source.Length : closing + 2;
                continue;
            }

            if (TryReadRawString(source, index, out var rawEnd, out _))
            {
                index = rawEnd;
                continue;
            }

            if (StartsWith(source, index, "$@\"") || StartsWith(source, index, "@$\""))
            {
                index = FindInterpolatedStringEnd(source, index + 3, isVerbatim: true);
                continue;
            }

            if (StartsWith(source, index, "$\""))
            {
                index = FindInterpolatedStringEnd(source, index + 2, isVerbatim: false);
                continue;
            }

            if (StartsWith(source, index, "@\""))
            {
                index = FindVerbatimStringEnd(source, index + 2);
                continue;
            }

            if (source[index] == '"')
            {
                index = FindRegularStringEnd(source, index + 1);
                continue;
            }

            if (source[index] == '\'')
            {
                index = FindCharacterLiteralEnd(source, index + 1);
                continue;
            }

            if (source[index] == '{')
            {
                interpolationDepth++;
            }
            else if (source[index] == '}')
            {
                interpolationDepth--;
            }

            index++;
        }

        return source.Length;
    }

    private static int FindVerbatimStringEnd(string source, int contentStart)
    {
        var index = contentStart;

        while (index < source.Length)
        {
            if (source[index] != '"')
            {
                index++;
                continue;
            }

            if (index + 1 < source.Length && source[index + 1] == '"')
            {
                index += 2;
                continue;
            }

            return index + 1;
        }

        return source.Length;
    }

    private static int FindCharacterLiteralEnd(string source, int contentStart)
    {
        var index = contentStart;

        while (index < source.Length)
        {
            if (source[index] == '\\')
            {
                index = Math.Min(index + 2, source.Length);
                continue;
            }

            if (source[index] == '\'')
            {
                return index + 1;
            }

            index++;
        }

        return source.Length;
    }

    private static string DecodeIdentifierUnicodeEscapes(string source)
    {
        var decoded = UnicodeEightDigitEscapeRegex().Replace(
            source,
            match => DecodeCodePoint(match.Groups[1].Value));
        return UnicodeFourDigitEscapeRegex().Replace(
            decoded,
            match => DecodeCodePoint(match.Groups[1].Value));
    }

    private static string NormalizeExecutableIdentifiers(string source)
    {
        var decoded = DecodeIdentifierUnicodeEscapes(source);
        var normalized = new StringBuilder(decoded.Length);

        foreach (var rune in decoded.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format)
            {
                normalized.Append(rune.ToString());
            }
        }

        return normalized.ToString();
    }

    private static string DecodeCodePoint(string hexadecimal)
    {
        var codePoint = Convert.ToInt32(hexadecimal, 16);
        return codePoint is >= 0 and <= 0x10FFFF
            ? char.ConvertFromUtf32(codePoint)
            : string.Empty;
    }

    private static int GetLineNumber(string source, int position)
    {
        var lineNumber = 1;
        for (var index = 0; index < position; index++)
        {
            if (source[index] == '\n')
            {
                lineNumber++;
            }
        }

        return lineNumber;
    }

    private static void Mask(char[] target, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (target[index] is not ('\r' or '\n'))
            {
                target[index] = ' ';
            }
        }
    }

    private static bool StartsWith(string source, int index, string value)
    {
        return index + value.Length <= source.Length
               && source.AsSpan(index, value.Length).SequenceEqual(value);
    }

    private static bool IsBuildOutput(string repositoryPath)
    {
        return repositoryPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static PlatformSourceCategory ClassifySource(string repositoryPath)
    {
        var normalized = NormalizeRepositoryPath(repositoryPath);
        if (IsBuildOutput(normalized))
        {
            return PlatformSourceCategory.BuildArtifact;
        }

        if (normalized.Contains("/Migrations/", StringComparison.Ordinal))
        {
            return PlatformSourceCategory.Migration;
        }

        if (Path.GetFileName(normalized).EndsWith(
                "DesignTimeDbContextFactory.cs",
                StringComparison.Ordinal))
        {
            return PlatformSourceCategory.DesignTime;
        }

        return PlatformSourceCategory.RuntimeApplication;
    }

    private static string DescribeSourceCategory(PlatformSourceCategory category)
    {
        return category switch
        {
            PlatformSourceCategory.RuntimeApplication => "runtime application",
            PlatformSourceCategory.Migration => "migration",
            PlatformSourceCategory.DesignTime => "design-time",
            PlatformSourceCategory.BuildArtifact => "build artifact",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };
    }

    private static bool IsExcludedCounterpartyPath(string repositoryPath)
    {
        var normalized = NormalizeRepositoryPath(repositoryPath);
        return normalized.StartsWith(
            ExcludedCounterpartyPrefix,
            StringComparison.Ordinal);
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
        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static string ToNativeFileSystemPath(string path)
    {
        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
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

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])IgnoreQueryFilters(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex IgnoreQueryFiltersIdentifierRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])(?:dynamic|MethodInfo|MethodBase|RuntimeMethodHandle|"
        + @"GetMethod|GetMethods|GetMember|GetMembers|GetRuntimeMethod|GetRuntimeMethods|"
        + @"GetProperty|GetProperties|PropertyInfo|GetField|GetFields|FieldInfo|"
        + @"GetEvent|GetEvents|EventInfo|BindingFlags|GetValue|SetValue|"
        + @"GetRuntimeBaseDefinition|GetTypeInfo|GetDeclaredMethod|GetDeclaredMethods|"
        + @"DeclaredMethod|DeclaredMethods|ResolveMethod|ResolveMember|InvokeMember|"
        + @"ConstructorInfo|GetConstructor|GetConstructors|"
        + @"DynamicInvoke|CreateDelegate|GetFunctionPointer|CallSite|GetType|"
        + @"Activator|ActivatorUtilities|CreateInstance|AssemblyLoadContext|Assembly|"
        + @"LoadFromAssemblyPath)"
        + @"(?![\p{L}\p{Nd}_])|"
        + @"(?<![\p{L}\p{Nd}_])(?:@?System\s*\.\s*@?Reflection|"
        + @"@?System\s*\.\s*@?Dynamic|"
        + @"@?System\s*\.\s*@?Linq\s*\.\s*@?Expressions|"
        + @"Expressions|Expression\s*\.\s*Call)"
        + @"(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenBypassCapabilityRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])(?:"
        + @"FromSql(?:Raw|Interpolated)?(?:Async)?|"
        + @"SqlQuery(?:Raw)?(?:Async)?|"
        + @"ExecuteSql(?:Raw|Interpolated)?(?:Async)?|"
        + @"GetDbConnection|CreateDbCommand|CreateCommand|"
        + @"DbConnection|IDbConnection|DbCommand|IDbCommand|DbDataReader|"
        + @"DbDataAdapter|IDbDataAdapter|DbProviderFactory|DbProviderFactories|"
        + @"DbDataSource|DbBatch|DbBatchCommand|"
        + @"SqlConnection|SqlCommand|SqlDataReader|SqlDataAdapter|"
        + @"SqlDataSource|SqlBatch|SqlBatchCommand|SqlBulkCopy|"
        + @"IRelationalConnection|RelationalConnection|"
        + @"IRelationalCommand|RelationalCommand|RawSqlCommand|IRawSqlCommandBuilder|"
        + @"RelationalDatabaseFacadeExtensions|RelationalQueryableExtensions|"
        + @"IDbContextFactory|AddDbContextFactory|AddDbContextPool|"
        + @"AddPooledDbContextFactory|PooledDbContextFactory|UseModel|"
        + @"ReplaceService|UseInternalServiceProvider|UseApplicationServiceProvider|"
        + @"IModelCustomizer|IModelCacheKeyFactory|IModelSource|IModelRuntimeInitializer|"
        + @"IConventionSetPlugin|IQueryExpressionInterceptor|DbCommandInterceptor|"
        + @"IDbCommandInterceptor"
        + @")(?![\p{L}\p{Nd}_])|"
        + @"(?<![\p{L}\p{Nd}_])(?:"
        + @"@?Microsoft\s*\.\s*@?Data\s*\.\s*@?SqlClient|"
        + @"@?System\s*\.\s*@?Data\s*\.\s*@?(?:Common|SqlClient|Odbc|OleDb)"
        + @")(?![\p{L}\p{Nd}_])|"
        + @"(?<![\p{L}\p{Nd}_])new\s+"
        + @"(?:global\s*::\s*)?(?:@?[\p{L}_][\p{L}\p{Nd}_]*\s*\.\s*)*"
        + @"@?DbContext\s*\(|"
        + @"(?<![\p{L}\p{Nd}_])@?DbContext\s+"
        + @"@?[\p{L}_][\p{L}\p{Nd}_]*\s*=\s*new\s*\(|"
        + @"\.\s*@?(?:Find(?:Async)?|GetDatabaseValues(?:Async)?|Reload(?:Async)?)"
        + @"(?![\p{L}\p{Nd}_])|"
        + @"\.\s*@?Local(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex DirectDatabaseBypassCapabilityRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])@?(?<api>Sql|InsertData|UpdateData|DeleteData)"
        + @"(?![\p{L}\p{Nd}_])|"
        + @"(?<![\p{L}\p{Nd}_])@?(?<api>SqlOperation|InsertDataOperation|"
        + @"UpdateDataOperation|DeleteDataOperation)(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex MigrationMutationCapabilityRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])(?:class|record(?:\s+class)?)\s+"
        + @"(?<name>@?[\p{L}_][\p{L}\p{Nd}_]*)[^;{}]*?:\s*"
        + @"(?:global\s*::\s*)?(?:@?[\p{L}_][\p{L}\p{Nd}_]*\s*\.\s*)*"
        + @"@?DbContext(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex DbContextDeclarationRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])(?:global\s+)?using\s+"
        + @"(?<alias>@?[\p{L}_][\p{L}\p{Nd}_]*)\s*=\s*"
        + @"(?:global\s*::\s*)?(?:@?[\p{L}_][\p{L}\p{Nd}_]*\s*\.\s*)*"
        + @"@?DbContext(?![\p{L}\p{Nd}_])\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex DbContextBaseTypeAliasRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])new\s+"
        + @"(?:global\s*::\s*)?(?:@?[\p{L}_][\p{L}\p{Nd}_]*\s*\.\s*)*@?"
        + @"(?<type>AuditDbContext|CommandExecutionDbContext|IdentityDbContext|"
        + @"OrganizationDbContext)(?![\p{L}\p{Nd}_])\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReviewedContextConstructionRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])@?(?<type>AuditDbContext|CommandExecutionDbContext|"
        + @"IdentityDbContext|OrganizationDbContext)(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReviewedContextTypeIdentifierRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])(?:return\s+new|new|=>\s*new)\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex TargetTypedNewRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])(?:global\s+)?using\s+"
        + @"(?<alias>@?[\p{L}_][\p{L}\p{Nd}_]*)\s*=\s*"
        + @"(?:global\s*::\s*)?(?:@?[\p{L}_][\p{L}\p{Nd}_]*\s*\.\s*)*@?"
        + @"(?<type>AuditDbContext|CommandExecutionDbContext|IdentityDbContext|"
        + @"OrganizationDbContext)(?![\p{L}\p{Nd}_])\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReviewedContextTypeAliasRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}_])@?(?:IDesignTimeDbContextFactory|"
        + @"AuditDesignTimeDbContextFactory|"
        + @"CommandExecutionDesignTimeDbContextFactory|"
        + @"IdentityDesignTimeDbContextFactory|"
        + @"OrganizationDesignTimeDbContextFactory)(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReviewedDesignTimeFactoryIdentifierRegex();

    [GeneratedRegex(@"\\u([0-9a-fA-F]{4})", RegexOptions.CultureInvariant)]
    private static partial Regex UnicodeFourDigitEscapeRegex();

    [GeneratedRegex(@"\\U([0-9a-fA-F]{8})", RegexOptions.CultureInvariant)]
    private static partial Regex UnicodeEightDigitEscapeRegex();

    private sealed record BypassOccurrence(string RepositoryPath, int LineNumber)
    {
        public override string ToString() => $"{RepositoryPath}:{LineNumber}";
    }

    private sealed record DbContextDeclaration(
        string RepositoryPath,
        string TypeName,
        int LineNumber,
        PlatformSourceCategory SourceCategory);

    private sealed record ReviewedContextFactory(
        string ContextTypeName,
        string ReviewedFingerprint);

    private sealed record ReviewedMigrationMutationSource(
        string ReviewedFingerprint,
        IReadOnlyDictionary<string, int> ExpectedApiOccurrences);

    private sealed record ReviewedContextConstruction(
        string RepositoryPath,
        string ContextTypeName,
        int LineNumber);

    private sealed record MigrationMutationOccurrence(
        string RepositoryPath,
        string ApiName,
        int LineNumber,
        PlatformSourceCategory SourceCategory);

    private sealed record BackendSourceInventory(
        IReadOnlyDictionary<string, string> Sources,
        IReadOnlyList<string> ProjectPaths,
        IReadOnlyList<string> DefinitionErrors);

    private sealed record BackendProjectDocument(
        string FullPath,
        string RepositoryPath,
        XDocument Document);

    private sealed record ProjectDefinitionAnalysis(
        IReadOnlyList<CompileDirective> CompileDirectives,
        IReadOnlyList<string> Errors);

    private sealed record CompileDirective(
        CompileDirectiveKind Kind,
        IReadOnlyList<string> Items,
        IReadOnlyList<string> Excludes,
        string? Link);

    private enum CompileDirectiveKind
    {
        Include,
        Remove,
        Update
    }

    private enum PlatformSourceCategory
    {
        RuntimeApplication,
        Migration,
        DesignTime,
        BuildArtifact
    }
}
