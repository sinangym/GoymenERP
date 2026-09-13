using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private const string CounterpartyCompositionRootContractPath =
        "src/backend/Modules/Counterparty/COUNTERPARTY_MODULE_ENTRYPOINTS.json";
    private const string CounterpartyContractSourcePath =
        "docs/features/CARI_MODULE_ENTRYPOINT_CONTRACT_v1.md";
    private const string ApiProjectPath =
        "src/backend/GoymenERP.Api/GoymenERP.Api.csproj";
    private const string PdfProjectPath =
        "src/backend/DocumentGeneration/GoymenERP.DocumentGeneration.Pdf/"
        + "GoymenERP.DocumentGeneration.Pdf.csproj";
    private static readonly HashSet<string> ReviewedPdfContentPaths =
        new(StringComparer.Ordinal)
        {
            "Assets\\Fonts\\NotoSans-Regular.ttf",
            "Assets\\Fonts\\OFL-1.1.txt",
            "Assets\\Fonts\\PROVENANCE.md",
            "Assets\\Licenses\\PDFsharp-MIT.txt",
            "Assets\\Licenses\\PDFsharp-6.2.4-PROVENANCE.md",
            "THIRD_PARTY_NOTICES.md"
        };
    private const string OpenApiGenerationModePath =
        "src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/OpenApi/"
        + "OpenApiDocumentGenerationMode.cs";
    private const string OpenApiStartupPath =
        "src/backend/GoymenERP.Api/Program.cs";
    private const string ReviewedOpenApiStartupFingerprint =
        "d937f1d3607d6a7e49da592da77dfd91ce655f199fbc88b66e18ab0ed18134fa";
    private const string ReviewedOpenApiGenerationModeFingerprint =
        "74ff9d4e8033732b06b8b6639699ce97781a967b3a3f658da4c8571d7573da50";
    private const string ReviewedIdentityReconcilerPath =
        "src/backend/Modules/Identity/"
        + "GoymenERP.Modules.Identity.Infrastructure/Seeding/"
        + "SystemRolePermissionReconciler.cs";
    private const string ReviewedSqlServerRcsiReadinessProbePath =
        "src/backend/BuildingBlocks/GoymenERP.BuildingBlocks.SqlServer/"
        + "SqlServerRcsiReadiness.cs";
    private const string ReviewedSqlServerRcsiToolProgramPath =
        "tools/GoymenERP.SqlServerRcsiReadiness/Program.cs";
    private const string ReviewedSqlServerRcsiWrapperPath =
        "tools/GoymenERP.SqlServerRcsiReadiness/"
        + "Invoke-GoymenERP.SqlServerRcsiReadiness.ps1";
    private const string ReviewedSqlServerRcsiWrapperFingerprint =
        "45598900747141d62bce9b983b43ffb26eef123a1a2a49d1c72e6645e2990d7e";
    private const string ReviewedSqlServerRcsiToolRootPath =
        "tools/GoymenERP.SqlServerRcsiReadiness";
    private const string ReviewedSqlServerRcsiToolProjectPath =
        "tools/GoymenERP.SqlServerRcsiReadiness/"
        + "GoymenERP.SqlServerRcsiReadiness.csproj";
    private const string ReviewedSqlServerRcsiToolProjectFingerprint =
        "bbfb2841dbb968a34c65e9da6acabc67e810660fd77647416421e54deccbe478";
    private const string SolutionPath = "GoymenERP.slnx";
    private const string SqlServerRcsiToolTestsProjectPath =
        "tests/GoymenERP.SqlServerRcsiReadiness.Tests/"
        + "GoymenERP.SqlServerRcsiReadiness.Tests.csproj";
    private const string ReviewedSqlServerRcsiToolTestsProjectFingerprint =
        "0a5d40cdf5b24b687eb6acbf6f96d4b70d050a24e9268498aa6a12007bb9e525";
    private const string SqlServerRcsiIntegrationProjectPath =
        "tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj";
    private const string ReviewedSqlServerRcsiIntegrationProjectFingerprint =
        "ab69dc2cd05e0221e603ad9374f6ccdb5ca41003582251581a370e74305ca042";
    private const string SqlServerRcsiRelationalAcceptancePath =
        "tests/GoymenERP.Api.IntegrationTests/"
        + "SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests.cs";
    private const string SqlServerRcsiVerifierPath =
        "tools/verify_rcsi_relational_acceptance.py";
    private const string ReviewedSqlServerRcsiVerifierFingerprint =
        "e619dd4bc1dbc5db0d06701ca3479814682e4ec3e3384a4a411151719380f44e";
    private const string SqlServerRcsiVerifierTestsPath =
        "tests/rcsi_relational_acceptance_verifier/"
        + "test_verify_rcsi_relational_acceptance.py";
    private const string ReviewedSqlServerRcsiVerifierTestsFingerprint =
        "2306be3f642beb148e86ad59103c851376775b2354477238f9a86adec6fee120";
    private const string ContinuousIntegrationWorkflowPath = ".github/workflows/ci.yml";
    private const string ReviewedContinuousIntegrationWorkflowFingerprint =
        "91a665e6e8982e5a9b66f5d16a14d9fc4d7f98d63775de959a8cc6b2dea2b018";
    private const string ReviewedSeederFingerprint =
        "b9de3d52d72f20ffc6279f32b9cc4b1b646aa3b3f03c6a12fd2fa8d1d4c4c450";

    // This is a reviewed source inventory, not a claim that every entity mapped by every
    // context has a global query filter. Any context change must receive a fresh tenant-safety
    // review and a new fingerprint.
    private static readonly IReadOnlyDictionary<string, string> ReviewedPlatformDbContexts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/"
             + "Persistence/AuditDbContext.cs"] =
                "6339a82c26df4406eded3c5560a346056253b7b58df2c0c69d9614a6eb3e41a1",
            ["src/backend/Modules/Idempotency/"
             + "GoymenERP.Modules.Idempotency.Infrastructure/Persistence/"
             + "CommandExecutionDbContext.cs"] =
                "3622036369dbfa41ea6f9fb8dbf1c68d63574f36cdb69a53a11e987990220118",
            ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/"
             + "Persistence/IdentityDbContext.cs"] =
                "31367b8ff08de6b112ffaf4a5e99c6d4d195fcc7dcde8d73bb4c0d888ede92e4",
            ["src/backend/Modules/Organization/"
             + "GoymenERP.Modules.Organization.Infrastructure/Persistence/"
             + "OrganizationDbContext.cs"] =
                "198fddce9a2fd4552850c2d1545b96b0030411d5d256a70ee932cf5173be2ff2",
            ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
             + "Persistence/ProductDbContext.cs"] =
                "a25322e5924213ccdf3241e0d1ff4cc15f5d6be168345894eaba3f6b77204812",
            ["src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/"
             + "Persistence/GroupSharedCommandDbContext.cs"] =
                "8049fa3a764b3695a477be42aa585c70268601871927429e33674ac860635d08"
        };

    // Accepted module initializers own one exact Migrate/EnsureCreated provider branch each.
    // The Identity reconciler owns one parameterized row/range-lock query. The shared RCSI
    // readiness probe owns one reviewed read-only command. Exact source fingerprints and API
    // counts keep these from becoming general database bypass exemptions.
    private static readonly IReadOnlyDictionary<string, ReviewedRuntimeDatabaseBypassSource>
        ReviewedRuntimeDatabaseBypassSources =
            new Dictionary<string, ReviewedRuntimeDatabaseBypassSource>(StringComparer.Ordinal)
            {
                ["src/backend/GoymenERP.Api/ProductRuntime/ProductRuntimeExtensions.cs"] =
                    new("e89e8869bed6d7706781ee3c86f09bc59963d0ef26da32c7e06d63449ae8ef27",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["MigrateAsync"] = 2 }),
                ["src/backend/GoymenERP.Api/ProductRuntime/ProductSqlCommandRunner.cs"] =
                    new("a7b69d45b14f11c63c12f67b44bed0588dad1e549f7022804e03cc296285fdca",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Microsoft.Data.SqlClient"] = 1, ["SqlConnection"] = 1 }),
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Services/GroupSharedAuditSession.cs"] =
                    new("08e6b2c930864ac99b64ad52ad83602b3446e2a86d9d8be4867f2204b3fbf3d0",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["System.Data.Common"] = 1, ["DbConnection"] = 1 }),
                ["src/backend/Modules/Audit/"
                 + "GoymenERP.Modules.Audit.Infrastructure/AuditModuleExtensions.cs"] =
                    new(
                        "f953cac522b80c54bf4f9bdb8626690f8428f74f49fb22589c0ecd0a9ab1b3f6",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                ["src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/"
                 + "Executions/SqlGroupSharedCommandTerminalStore.cs"] =
                    new(
                        "5ecf927a5505ff79b5d2f3d79e436dc674d1b98aa05db28c8ae6e6669ac9b301",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["DbConnection"] = 1
                        }),
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/"
                 + "Persistence/ProductDraftParticipant.cs"] =
                    new(
                        "9273f2b058ad8f9863504a5f1f5a4c2605431559d0e001c19793135aaf3a238d",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["DbConnection"] = 3
                        }),
                ["src/backend/Modules/Idempotency/"
                 + "GoymenERP.Modules.Idempotency.Infrastructure/"
                 + "IdempotencyModuleExtensions.cs"] =
                    new(
                        "aeb8697c52ce7f0e7dc889035a302b779e8d55b1c88b912f3263acacf75be678",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        }),
                ["src/backend/Modules/Identity/"
                 + "GoymenERP.Modules.Identity.Infrastructure/IdentityModuleExtensions.cs"] =
                    new(
                        "6a938e96c3a23333bf94e4b15a4c5630305176c7acde8e001cf701cde00355e4",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1,
                            ["GetDbConnection"] = 1,
                            ["Microsoft.Data.SqlClient"] = 1
                        }),
                [ReviewedSqlServerRcsiReadinessProbePath] =
                    new(
                        "b6291e2db3fef669f312b3046fdc46d826992901a7a2ca7243d35bd0557c9c34",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["DbConnection"] = 1,
                            ["CreateCommand"] = 1
                        }),
                [ReviewedSqlServerRcsiToolProgramPath] =
                    new(
                        "5da18330229e541cd90e9463a181c88ce72bb821c43449f44673f761faabaf04",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["Microsoft.Data.SqlClient"] = 1,
                            ["SqlConnection"] = 4
                        }),
                [ReviewedIdentityReconcilerPath] =
                    new(
                        "6c5f20ecb02e6803238c4234509ff557dd3e6da1f7a4f5c1c8d7c173a7d90751",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["FromSqlInterpolated"] = 1
                        }),
                ["src/backend/Modules/Organization/"
                 + "GoymenERP.Modules.Organization.Infrastructure/"
                 + "OrganizationModuleExtensions.cs"] =
                    new(
                        "e19705b2bb98278cb7aa7e510be3f646f721c54aa69e78993657071cebaf9ce6",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["EnsureCreatedAsync"] = 1,
                            ["MigrateAsync"] = 1
                        })
            };

    private static readonly IReadOnlyDictionary<string, ReviewedContextFactory>
        ReviewedPlatformContextFactories =
            new Dictionary<string, ReviewedContextFactory>(StringComparer.Ordinal)
            {
                ["src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/GroupSharedCommandDesignTimeDbContextFactory.cs"] =
                    new("GroupSharedCommandDbContext", "cb3032c17681f28ce368afd1b0e13ace3c515a92bf5a23b11f41f8336bb3b37a"),
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/ProductDesignTimeDbContextFactory.cs"] =
                    new("ProductDbContext", "14980801f97830d0887a538d88a4f9ccb68f9ddfbb696fb9b3c4e2d8d45f7374"),
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
                ["src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/Migrations/20260912175128_InitialProductDrafts.cs"] =
                    new("09b167e9837c486f8487bc8a30518d37a98ee90d6cea5842a40df6fd7c7e00f8",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20260912175120_AddExplicitProductAssignments.cs"] =
                    new("b366831c39d682f7f1d45664300082806248a8b54910799c83adfcca3f64d14b",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                ["src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20260912175124_InitialGroupSharedTerminals.cs"] =
                    new("ed1ef07eb88ee1ea324479d5ab498d213ecf3cfdba4cf5ab4eae49292c4dc4c7",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
                ["src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Persistence/Migrations/20260912173914_AddGlobalProductAuditSubject.cs"] =
                    new("e0bc0755399eb10c87793f35c832ce01ea80c4ed7d2c4c02fda465a3feae2df5",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        { ["Sql"] = 1 }),
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
                        }),
                ["src/backend/Modules/Organization/"
                 + "GoymenERP.Modules.Organization.Infrastructure/Persistence/"
                 + "Migrations/20260831142045_AddCompanyFiscalRuntimeSeed.cs"] =
                    new(
                        "27bc99e999554b75715f834aec63b0e5d2bf81a00c70dc18b66eba09266b1f02",
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
            ["Microsoft.AspNetCore.OpenApi"] = "10.0.8",
            ["Microsoft.EntityFrameworkCore.Design"] = "10.0.8",
            ["Microsoft.EntityFrameworkCore.SqlServer"] = "10.0.8",
            ["Microsoft.Extensions.ApiDescription.Server"] = "10.0.11",
            ["Microsoft.Extensions.Hosting.Abstractions"] = "10.0.8",
            ["Microsoft.Extensions.Options.ConfigurationExtensions"] = "10.0.8",
            ["Microsoft.OpenApi"] = "[2.7.5]",
            ["PDFsharp"] = "6.2.4"
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

    private static readonly IReadOnlyDictionary<string, string> ReviewedApiOpenApiProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OpenApiGenerateDocuments"] = "true",
            ["OpenApiGenerateDocumentsOnBuild"] = "true",
            ["OpenApiDocumentsDirectory"] = "../../../artifacts",
            ["OpenApiGenerateDocumentsOptions"] =
                "--document-name v1 --file-name openapi"
        };

    private static readonly HashSet<string> RequiredApiOpenApiPackages =
        new(StringComparer.Ordinal)
        {
            "Microsoft.AspNetCore.OpenApi",
            "Microsoft.Extensions.ApiDescription.Server",
            "Microsoft.OpenApi"
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
        var inventory = ReadBackendSourceInventory(
            repositoryRoot,
            requireCounterpartyContract: true);
        var errors = inventory.DefinitionErrors.ToList();
        var directDatabaseSources = inventory.Sources.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
        var toolInventory = ReadSqlServerRcsiToolSourceInventory(repositoryRoot);
        var toolSources = toolInventory.Sources;
        errors.AddRange(toolInventory.DefinitionErrors);
        foreach (var (path, source) in toolSources)
        {
            directDatabaseSources[path] = source;
        }

        errors.AddRange(ValidateNonVacuousScope(inventory));
        errors.AddRange(ValidateExactAllowlist(
            FindOccurrences(inventory.Sources),
            inventory.Sources));
        errors.AddRange(ValidateNoForbiddenBypassCapabilities(inventory.Sources));
        errors.AddRange(ValidateNoDirectDatabaseBypassCapabilities(
            directDatabaseSources,
            requireReviewedSources: true));
        errors.AddRange(ValidateNoForbiddenBypassCapabilities(toolSources));
        errors.AddRange(ValidateSqlServerRcsiToolMutationSurface(toolSources));
        errors.AddRange(ValidateReviewedSqlServerRcsiToolProject(
            File.ReadAllText(Path.Combine(
                repositoryRoot,
                ReviewedSqlServerRcsiToolProjectPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)))));
        errors.AddRange(ValidateReviewedSqlServerRcsiWrapper(
            File.ReadAllText(Path.Combine(
                repositoryRoot,
                ReviewedSqlServerRcsiWrapperPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)))));
        errors.AddRange(ValidateReviewedMigrationMutationSurface(inventory.Sources));
        errors.AddRange(ValidateReviewedPlatformDbContextInventory(inventory.Sources));
        errors.AddRange(ValidateReviewedPlatformContextConstructions(inventory.Sources));

        Assert.True(errors.Count == 0, string.Join("\n\n", errors));
    }

    [Fact]
    public void Pdfsharp_dependency_is_limited_to_the_isolated_pdf_component()
    {
        var errors = new List<string>();
        ValidatePackageReference(
            XElement.Parse("<PackageReference Include=\"PDFsharp\" Version=\"6.2.4\" />"),
            "src/backend/BuildingBlocks/Unexpected.csproj",
            errors);

        Assert.Contains(
            "PDFsharp is allowed only in the isolated PDF component project.",
            errors);
    }

    [Fact]
    public void Pdf_component_content_inventory_cannot_drop_the_font_license()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            PdfProjectPath.Replace('/', Path.DirectorySeparatorChar));
        var project = XDocument.Load(projectPath);
        project.Descendants()
            .Single(element =>
                element.Name.LocalName == "Content"
                && string.Equals(
                    element.Attribute("Include")?.Value,
                    "Assets\\Fonts\\OFL-1.1.txt",
                    StringComparison.Ordinal))
            .Remove();

        var analysis = AnalyzeProjectDefinition(project, PdfProjectPath);

        Assert.Contains(
            "The PDF component content inventory must match the reviewed font and license assets.",
            analysis.Errors);
    }

    [Fact]
    public void Pdf_component_content_inventory_cannot_drop_the_font_provenance()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            PdfProjectPath.Replace('/', Path.DirectorySeparatorChar));
        var project = XDocument.Load(projectPath);
        project.Descendants()
            .Single(element =>
                element.Name.LocalName == "Content"
                && string.Equals(
                    element.Attribute("Include")?.Value,
                    "Assets\\Fonts\\PROVENANCE.md",
                    StringComparison.Ordinal))
            .Remove();

        var analysis = AnalyzeProjectDefinition(project, PdfProjectPath);

        Assert.Contains(
            "The PDF component content inventory must match the reviewed font and license assets.",
            analysis.Errors);
    }

    [Fact]
    public void Pdf_component_content_inventory_rejects_duplicate_asset_routes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            PdfProjectPath.Replace('/', Path.DirectorySeparatorChar));
        var project = XDocument.Load(projectPath);
        var itemGroup = project.Root!.Elements()
            .First(element => element.Name.LocalName == "ItemGroup"
                              && element.Elements().Any(item => item.Name.LocalName == "Content"));
        itemGroup.Add(new XElement(
            "Content",
            new XAttribute("Include", "Assets\\Fonts\\OFL-1.1.txt"),
            new XAttribute("CopyToOutputDirectory", "PreserveNewest"),
            new XAttribute("CopyToPublishDirectory", "PreserveNewest")));

        var analysis = AnalyzeProjectDefinition(project, PdfProjectPath);

        Assert.Contains(
            "The PDF component content inventory must match the reviewed font and license assets.",
            analysis.Errors);
    }

    [Fact]
    public void OpenApi_build_generation_startup_matches_the_exact_network_isolation_surface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sources = Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, BackendRootPath),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = NormalizeRepositoryPath(Path.GetRelativePath(repositoryRoot, path)),
                Source = File.ReadAllText(path),
            })
            .Where(file => !IsBuildOutput(file.Path))
            .ToDictionary(file => file.Path, file => file.Source, StringComparer.Ordinal);

        var errors = ValidateOpenApiBuildTimeNetworkIsolation(sources);

        Assert.True(errors.Count == 0, string.Join("\n\n", errors));
    }

    [Fact]
    public void Removing_an_OpenApi_build_generation_gate_is_rejected()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            OpenApiStartupPath.Replace('/', Path.DirectorySeparatorChar)));
        var identityPath = ReviewedRuntimeDatabaseBypassSources.Keys.Single(path =>
            path.EndsWith("IdentityModuleExtensions.cs", StringComparison.Ordinal));
        var identitySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            identityPath.Replace('/', Path.DirectorySeparatorChar)));
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OpenApiStartupPath] = programSource.Replace(
                "if (!isOpenApiDocumentGeneration",
                "if (isOpenApiDocumentGeneration",
                StringComparison.Ordinal),
            [identityPath] = identitySource.Replace(
                "if (!OpenApiDocumentGenerationMode.IsActive)",
                "if (OpenApiDocumentGenerationMode.IsActive)",
                StringComparison.Ordinal),
        };

        var errors = ValidateOpenApiBuildTimeNetworkIsolation(sources);

        Assert.Contains(errors, error => error.Contains(
            "startup fingerprint",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "hosted-service gate",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Disabled_unterminated_regular_string_cannot_mask_following_bypass()
    {
        const string source = "#if NEVER\n\"\n#endif\nquery.IgnoreQueryFilters();";

        var masked = MaskCommentsAndNonInterpolatedLiterals(source);
        var occurrences = FindOccurrences(new Dictionary<string, string>
        {
            ["src/backend/GoymenERP.Api/ConditionalProbe.cs"] = source
        });

        Assert.Contains("IgnoreQueryFilters", masked, StringComparison.Ordinal);
        Assert.Single(occurrences);
        Assert.Equal(4, occurrences[0].LineNumber);
    }

    [Theory]
    [InlineData("#if NEVER\n\"\n#endif\nquery.IgnoreQueryFilters();")]
    [InlineData("#if NEVER\n@\"\n#endif\nquery.IgnoreQueryFilters();")]
    [InlineData("#if NEVER\n\"\"\"\n#endif\nquery.IgnoreQueryFilters();")]
    [InlineData("#if NEVER\n/*\n#endif\nquery.IgnoreQueryFilters();")]
    public void Conditional_compilation_is_rejected_before_disabled_text_can_poison_scanning(
        string source)
    {
        var errors = ValidateNoForbiddenBypassCapabilities(
            new Dictionary<string, string>
            {
                ["src/backend/GoymenERP.Api/ConditionalProbe.cs"] = source
            });

        Assert.Contains(errors, error => error.Contains(
            "Conditional compilation is forbidden",
            StringComparison.Ordinal));
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
    [InlineData("database.EnsureCreated();")]
    [InlineData("await database.EnsureCreatedAsync();")]
    [InlineData("database.EnsureDeleted();")]
    [InlineData("await database.EnsureDeletedAsync();")]
    [InlineData("database.Migrate();")]
    [InlineData("await database.MigrateAsync();")]
    [InlineData("IRelationalDatabaseCreator creator = value; await creator.DeleteAsync();")]
    [InlineData("RelationalDatabaseCreator creator = value;")]
    [InlineData("IDatabaseCreator creator = value;")]
    [InlineData("IMigrator migrator = value;")]
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
    public void Reviewed_runtime_database_bypass_requires_exact_source_and_api_count()
    {
        var repositoryRoot = FindRepositoryRoot();
        var exactSources = ReviewedRuntimeDatabaseBypassSources.Keys.ToDictionary(
            path => path,
            path => File.ReadAllText(Path.Combine(
                repositoryRoot,
                path.Replace('/', Path.DirectorySeparatorChar))),
            StringComparer.Ordinal);
        var reviewedSource = exactSources[ReviewedIdentityReconcilerPath];
        var reviewedProbeSource = exactSources[ReviewedSqlServerRcsiReadinessProbePath];
        var reviewedToolSource = exactSources[ReviewedSqlServerRcsiToolProgramPath];

        Assert.Empty(ValidateNoDirectDatabaseBypassCapabilities(
            exactSources,
            requireReviewedSources: true));

        exactSources[ReviewedIdentityReconcilerPath] = reviewedSource + "\n// fingerprint drift";
        Assert.Contains(
            ValidateNoDirectDatabaseBypassCapabilities(
                exactSources,
                requireReviewedSources: true),
            error => error.Contains("does not match the exact fingerprint", StringComparison.Ordinal));

        exactSources[ReviewedIdentityReconcilerPath] = reviewedSource.Replace(
            ".FromSqlInterpolated(",
            ".AsQueryable(",
            StringComparison.Ordinal);
        Assert.Contains(
            ValidateNoDirectDatabaseBypassCapabilities(
                exactSources,
                requireReviewedSources: true),
            error => error.Contains(
                "Expected exactly 1 reviewed runtime 'FromSqlInterpolated' occurrence(s), but found 0",
                StringComparison.Ordinal));

        exactSources[ReviewedIdentityReconcilerPath] = reviewedSource;
        exactSources[ReviewedSqlServerRcsiReadinessProbePath] =
            reviewedProbeSource + "\n// fingerprint drift";
        Assert.Contains(
            ValidateNoDirectDatabaseBypassCapabilities(
                exactSources,
                requireReviewedSources: true),
            error => error.Contains(
                "does not match the exact fingerprint",
                StringComparison.Ordinal)
                && error.Contains(
                    ReviewedSqlServerRcsiReadinessProbePath,
                    StringComparison.Ordinal));

        exactSources[ReviewedSqlServerRcsiReadinessProbePath] =
            reviewedProbeSource.Replace(
                ".CreateCommand()",
                ".CreateReadOnlyCommand()",
                StringComparison.Ordinal);
        Assert.Contains(
            ValidateNoDirectDatabaseBypassCapabilities(
                exactSources,
                requireReviewedSources: true),
            error => error.Contains(
                "Expected exactly 1 reviewed runtime 'CreateCommand' occurrence(s), but found 0",
                StringComparison.Ordinal));

        exactSources[ReviewedSqlServerRcsiReadinessProbePath] = reviewedProbeSource;
        exactSources[ReviewedSqlServerRcsiToolProgramPath] =
            reviewedToolSource + "\nconnection.CreateCommand();";
        Assert.Contains(
            ValidateNoDirectDatabaseBypassCapabilities(
                exactSources,
                requireReviewedSources: true),
            error => error.Contains(
                "Forbidden direct database/query-filter bypass capability 'CreateCommand'",
                StringComparison.Ordinal)
                && error.Contains(
                    ReviewedSqlServerRcsiToolProgramPath,
                    StringComparison.Ordinal));

        exactSources[ReviewedSqlServerRcsiToolProgramPath] =
            reviewedToolSource + "\ncommand.ExecuteNonQuery();";
        Assert.Contains(
            ValidateNoDirectDatabaseBypassCapabilities(
                exactSources,
                requireReviewedSources: true),
            error => error.Contains(
                "Forbidden direct database/query-filter bypass capability 'ExecuteNonQuery'",
                StringComparison.Ordinal)
                && error.Contains(
                    ReviewedSqlServerRcsiToolProgramPath,
                    StringComparison.Ordinal));
    }

    [Fact]
    public void SqlServer_RCSI_tool_project_and_mutation_surface_are_exactly_guarded()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            ReviewedSqlServerRcsiToolProjectPath.Replace(
                '/',
                Path.DirectorySeparatorChar)));
        var toolInventory = ReadSqlServerRcsiToolSourceInventory(repositoryRoot);
        var toolSources = toolInventory.Sources;
        var wrapperSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            ReviewedSqlServerRcsiWrapperPath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Empty(toolInventory.DefinitionErrors);
        Assert.Empty(ValidateReviewedSqlServerRcsiToolProject(projectSource));
        Assert.Empty(ValidateSqlServerRcsiToolMutationSurface(toolSources));
        Assert.Empty(ValidateReviewedSqlServerRcsiWrapper(wrapperSource));

        var withoutPackage = projectSource.Replace(
            "Microsoft.Data.SqlClient",
            "Removed.SqlClient",
            StringComparison.Ordinal);
        Assert.Contains(
            ValidateReviewedSqlServerRcsiToolProject(withoutPackage),
            error => error.Contains(
                "exact Microsoft.Data.SqlClient package",
                StringComparison.Ordinal));

        var withoutSharedProject = projectSource.Replace(
            "GoymenERP.BuildingBlocks.SqlServer.csproj",
            "Removed.BuildingBlocks.SqlServer.csproj",
            StringComparison.Ordinal);
        Assert.Contains(
            ValidateReviewedSqlServerRcsiToolProject(withoutSharedProject),
            error => error.Contains(
                "exact shared SQL Server project reference",
                StringComparison.Ordinal));

        var dangerousSources = toolSources.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
        var dangerousPath = ReviewedSqlServerRcsiToolRootPath + "/Dangerous.cs";
        dangerousSources[dangerousPath] =
            "SqlConnection connection = value; connection.CreateCommand(); "
            + "command.ExecuteNonQuery(); const string sql = \"ALTER DATABASE [X] SET OFFLINE\";";

        var directDatabaseErrors = ValidateNoDirectDatabaseBypassCapabilities(
            dangerousSources);
        Assert.Contains(
            directDatabaseErrors,
            error => error.Contains("CreateCommand", StringComparison.Ordinal)
                     && error.Contains(dangerousPath, StringComparison.Ordinal));
        Assert.Contains(
            directDatabaseErrors,
            error => error.Contains("ExecuteNonQuery", StringComparison.Ordinal)
                     && error.Contains(dangerousPath, StringComparison.Ordinal));
        Assert.Contains(
            ValidateSqlServerRcsiToolMutationSurface(dangerousSources),
            error => error.Contains("ALTER DATABASE", StringComparison.Ordinal)
                     && error.Contains(dangerousPath, StringComparison.Ordinal));

        foreach (var dangerousWrapperFragment in new[]
                 {
                     "ALTER DATABASE [X] SET OFFLINE",
                     "Invoke-Expression $payload",
                     "[Diagnostics.ProcessStartInfo]::new('dotnet')",
                     "Password=PlaintextSecret",
                 })
        {
            Assert.Contains(
                ValidateReviewedSqlServerRcsiWrapper(
                    wrapperSource + Environment.NewLine + dangerousWrapperFragment),
                error => error.Contains("wrapper", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void SqlServer_RCSI_publish_and_test_discovery_contracts_are_fail_closed()
    {
        var repositoryRoot = FindRepositoryRoot();
        var solutionSource = File.ReadAllText(Path.Combine(repositoryRoot, SolutionPath));
        var integrationProjectSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            SqlServerRcsiIntegrationProjectPath.Replace('/', Path.DirectorySeparatorChar)));
        var toolTestsProjectSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            SqlServerRcsiToolTestsProjectPath.Replace('/', Path.DirectorySeparatorChar)));
        var relationalAcceptanceSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            SqlServerRcsiRelationalAcceptancePath.Replace('/', Path.DirectorySeparatorChar)));
        var workflowSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            ContinuousIntegrationWorkflowPath.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedWorkflowSource = workflowSource.Replace(
            "\r\n",
            "\n",
            StringComparison.Ordinal);
        var verifierSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            SqlServerRcsiVerifierPath.Replace('/', Path.DirectorySeparatorChar)));
        var verifierTestsSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            SqlServerRcsiVerifierTestsPath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Equal(
            ReviewedSqlServerRcsiVerifierFingerprint,
            ComputeReviewedSourceFingerprint(verifierSource));
        Assert.Equal(
            ReviewedSqlServerRcsiVerifierTestsFingerprint,
            ComputeReviewedSourceFingerprint(verifierTestsSource));
        Assert.NotEqual(
            ReviewedSqlServerRcsiVerifierFingerprint,
            ComputeReviewedSourceFingerprint(verifierSource + "\n# discovery bypass"));
        Assert.Equal(
            ReviewedSqlServerRcsiToolTestsProjectFingerprint,
            ComputeReviewedSourceFingerprint(toolTestsProjectSource));
        var toolTestsProject = XDocument.Parse(toolTestsProjectSource);
        Assert.Single(toolTestsProject.Descendants("PackageReference"), element =>
            string.Equals(
                element.Attribute("Include")?.Value,
                "Microsoft.NET.Test.Sdk",
                StringComparison.Ordinal));
        Assert.DoesNotContain(toolTestsProject.Descendants("IsTestProject"), element =>
            string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(
            ReviewedSqlServerRcsiToolTestsProjectFingerprint,
            ComputeReviewedSourceFingerprint(toolTestsProjectSource.Replace(
                "<IsPackable>false</IsPackable>",
                "<IsPackable>false</IsPackable>\n    <IsTestProject>false</IsTestProject>",
                StringComparison.Ordinal)));

        Assert.Empty(ValidateSqlServerRcsiPublishAndDiscoveryContracts(
            solutionSource,
            integrationProjectSource,
            relationalAcceptanceSource,
            normalizedWorkflowSource));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                relationalAcceptanceSource,
                normalizedWorkflowSource + "\n# unreviewed workflow drift"),
            error => error.Contains("exact reviewed fingerprint", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource.Replace(
                    "tests/GoymenERP.SqlServerRcsiReadiness.Tests/"
                    + "GoymenERP.SqlServerRcsiReadiness.Tests.csproj",
                    "tests/Removed/Removed.csproj",
                    StringComparison.Ordinal),
                integrationProjectSource,
                relationalAcceptanceSource,
                normalizedWorkflowSource),
            error => error.Contains("solution membership", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource.Replace(
                    "$(MSBuildProjectDirectory)\\..\\..\\tools\\"
                    + "GoymenERP.SqlServerRcsiReadiness\\"
                    + "GoymenERP.SqlServerRcsiReadiness.csproj",
                    "$(MSBuildProjectDirectory)\\..\\Alternate\\Alternate.csproj",
                    StringComparison.Ordinal),
                relationalAcceptanceSource,
                normalizedWorkflowSource),
            error => error.Contains("tool project path", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource.Replace(
                    "SelfContained=true",
                    "SelfContained=false",
                    StringComparison.Ordinal),
                relationalAcceptanceSource,
                normalizedWorkflowSource),
            error => error.Contains("SelfContained=true", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource.Replace(
                    "<RemoveDir Directories=\"$(RcsiToolSelfContainedPublishDir)\" />",
                    "<Message Text=\"clean removed\" />",
                    StringComparison.Ordinal),
                relationalAcceptanceSource,
                normalizedWorkflowSource),
            error => error.Contains("cleaned before publication", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource.Replace(
                    "<RemoveDir Directories=\"$(RcsiToolSelfContainedPublishDir)\" />",
                    "<RemoveDir Directories=\"$(RcsiToolSelfContainedPublishDir)\" "
                    + "Condition=\"'false'\" />",
                    StringComparison.Ordinal),
                relationalAcceptanceSource,
                normalizedWorkflowSource),
            error => error.Contains("cleaned before publication", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                relationalAcceptanceSource.Replace(
                    "SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests",
                    "RenamedRcsiAcceptanceTests",
                    StringComparison.Ordinal),
                normalizedWorkflowSource),
            error => error.Contains("relational discovery", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                relationalAcceptanceSource.Replace(
                    "SqlServerRcsiReadinessProcessAcceptanceTests",
                    "RemovedRcsiProcessAcceptanceTests",
                    StringComparison.Ordinal),
                normalizedWorkflowSource),
            error => error.Contains("process acceptance discovery", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                "#if NEVER" + Environment.NewLine + relationalAcceptanceSource
                + Environment.NewLine + "#endif",
                normalizedWorkflowSource),
            error => error.Contains("relational discovery", StringComparison.Ordinal));

        foreach (var verifierBypass in new[] { " --list-tests", " || true", "; true" })
        {
            Assert.Contains(
                ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                    solutionSource,
                    integrationProjectSource,
                    relationalAcceptanceSource,
                    normalizedWorkflowSource.Replace(
                        "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py",
                        "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py"
                        + verifierBypass,
                        StringComparison.Ordinal)),
                error => error.Contains("relational CI verifier", StringComparison.Ordinal));
        }

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                relationalAcceptanceSource,
                normalizedWorkflowSource.Replace(
                    "GOYMEN_ERP_TEST_SQLSERVER_CONNECTION: >-",
                    "REMOVED_SQLSERVER_CONNECTION: >-",
                    StringComparison.Ordinal)),
            error => error.Contains("SQL Server fixture binding", StringComparison.Ordinal));

        foreach (var softGate in new[]
                 {
                     "        if: ${{ false }}",
                     "        continue-on-error: true",
                     "        shell: echo {0}",
                     "        env:\n          GOYMEN_ERP_TEST_SQLSERVER_CONNECTION: ''",
                 })
        {
            Assert.Contains(
                ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                    solutionSource,
                    integrationProjectSource,
                    relationalAcceptanceSource,
                    normalizedWorkflowSource.Replace(
                        "      - name: Run exact RCSI relational acceptance verifier",
                        "      - name: Run exact RCSI relational acceptance verifier\n"
                        + softGate,
                        StringComparison.Ordinal)),
                error => error.Contains("relational CI verifier", StringComparison.Ordinal));
        }

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                relationalAcceptanceSource,
                normalizedWorkflowSource.Replace(
                    "      - name: Test",
                    "      - name: Test\n        if: ${{ false }}",
                    StringComparison.Ordinal)),
            error => error.Contains("backend solution test step", StringComparison.Ordinal));

        Assert.Contains(
            ValidateSqlServerRcsiPublishAndDiscoveryContracts(
                solutionSource,
                integrationProjectSource,
                relationalAcceptanceSource,
                normalizedWorkflowSource.Replace(
                    "\n    env:\n      GOYMEN_ERP_TEST_SQLSERVER_CONNECTION: >-\n        Server=",
                    "\n        env:\n          GOYMEN_ERP_TEST_SQLSERVER_CONNECTION: >-\n            Server=",
                    StringComparison.Ordinal)),
            error => error.Contains("SQL Server fixture binding", StringComparison.Ordinal));
    }

    [Fact]
    public void SqlServer_RCSI_tool_compile_inputs_and_ancestor_targets_are_scanned()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-rcsi-tool-guard-{Guid.NewGuid():N}");
        var repositoryRoot = Path.Combine(testRoot, "repository");
        var toolRoot = Path.Combine(
            repositoryRoot,
            ReviewedSqlServerRcsiToolRootPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        try
        {
            Directory.CreateDirectory(toolRoot);
            File.WriteAllText(Path.Combine(toolRoot, "Program.cs"), "internal class Program;");
            File.WriteAllText(
                Path.Combine(toolRoot, "AssemblyInfo.cs"),
                "internal class AssemblyInfo;");
            File.WriteAllText(
                Path.Combine(repositoryRoot, "tools", "Dangerous.cs"),
                "SqlConnection connection = value; connection.CreateCommand(); "
                + "command.ExecuteNonQuery(); const string sql = \"ALTER DATABASE [X]\";");
            File.WriteAllText(
                Path.Combine(
                    repositoryRoot,
                    ReviewedSqlServerRcsiToolProjectPath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="..\Dangerous.cs" Link="Dangerous.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(repositoryRoot, "tools", "Directory.Build.targets"),
                "<Project><Target Name=\"Inject\" /></Project>");
            File.WriteAllText(
                Path.Combine(testRoot, "Directory.Build.targets"),
                "<Project><Target Name=\"InjectOutsideRepository\" /></Project>");
            File.WriteAllText(
                Path.Combine(testRoot, "Directory.Packages.props"),
                "<Project><ItemGroup><PackageReference Include=\"Injected\" /></ItemGroup></Project>");
            File.WriteAllText(
                Path.Combine(
                    repositoryRoot,
                    ReviewedSqlServerRcsiToolProjectPath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)) + ".user",
                "<Project><Import Project=\"Injected.targets\" /></Project>");

            var inventory = ReadSqlServerRcsiToolSourceInventory(repositoryRoot);
            var dangerousPath = "tools/Dangerous.cs";
            Assert.Contains(
                inventory.DefinitionErrors,
                error => error.Contains("Directory.Build.targets", StringComparison.Ordinal));
            Assert.Contains(
                inventory.DefinitionErrors,
                error => error.Contains("outside the repository", StringComparison.Ordinal)
                         && error.Contains(
                             "Directory.Build.targets",
                             StringComparison.Ordinal));
            Assert.Contains(
                inventory.DefinitionErrors,
                error => error.Contains("outside the repository", StringComparison.Ordinal)
                         && error.Contains(
                             "Directory.Packages.props",
                             StringComparison.Ordinal));
            Assert.Contains(
                inventory.DefinitionErrors,
                error => error.Contains(".csproj.user", StringComparison.Ordinal));
            Assert.True(inventory.Sources.ContainsKey(dangerousPath));
            var directErrors = ValidateNoDirectDatabaseBypassCapabilities(inventory.Sources);
            Assert.Contains(
                directErrors,
                error => error.Contains("CreateCommand", StringComparison.Ordinal)
                         && error.Contains(dangerousPath, StringComparison.Ordinal));
            Assert.Contains(
                directErrors,
                error => error.Contains("ExecuteNonQuery", StringComparison.Ordinal)
                         && error.Contains(dangerousPath, StringComparison.Ordinal));
            Assert.Contains(
                ValidateSqlServerRcsiToolMutationSurface(inventory.Sources),
                error => error.Contains("ALTER DATABASE", StringComparison.Ordinal)
                         && error.Contains(dangerousPath, StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void SqlServer_RCSI_tool_compile_policy_accepts_literal_Link_and_rejects_hiding_directives()
    {
        var sourceRepositoryRoot = FindRepositoryRoot();
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-rcsi-tool-compile-policy-{Guid.NewGuid():N}");
        var repositoryRoot = Path.Combine(testRoot, "repository");
        var toolRoot = Path.Combine(
            repositoryRoot,
            ReviewedSqlServerRcsiToolRootPath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            Directory.CreateDirectory(toolRoot);
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "tools"));
            File.Copy(
                Path.Combine(sourceRepositoryRoot, "Directory.Build.props"),
                Path.Combine(repositoryRoot, "Directory.Build.props"));
            foreach (var sourceName in new[] { "Program.cs", "AssemblyInfo.cs" })
            {
                File.WriteAllText(Path.Combine(toolRoot, sourceName), $"internal class {sourceName[..^3]};");
            }

            File.WriteAllText(Path.Combine(repositoryRoot, "tools", "Safe.cs"), "internal class Safe;");
            Directory.CreateDirectory(Path.Combine(toolRoot, "obj"));
            Directory.CreateDirectory(Path.Combine(toolRoot, "bin"));
            File.WriteAllText(
                Path.Combine(toolRoot, "obj", "Dangerous.cs"),
                "connection.CreateCommand().ExecuteNonQuery();");
            File.WriteAllText(
                Path.Combine(toolRoot, "bin", "Dangerous.cs"),
                "connection.CreateCommand().ExecuteNonQuery();");

            var projectPath = Path.Combine(
                repositoryRoot,
                ReviewedSqlServerRcsiToolProjectPath.Replace('/', Path.DirectorySeparatorChar));
            var baseProject = File.ReadAllText(Path.Combine(
                sourceRepositoryRoot,
                ReviewedSqlServerRcsiToolProjectPath.Replace('/', Path.DirectorySeparatorChar)));
            var linkedProject = baseProject.Replace(
                "</Project>",
                """
                  <ItemGroup>
                    <Compile Include="..\Safe.cs">
                      <Link>Safe.cs</Link>
                    </Compile>
                  </ItemGroup>
                </Project>
                """,
                StringComparison.Ordinal);
            File.WriteAllText(projectPath, linkedProject);

            var inventory = ReadSqlServerRcsiToolSourceInventory(repositoryRoot);
            Assert.Empty(inventory.DefinitionErrors);
            Assert.Contains("tools/Safe.cs", inventory.Sources.Keys);
            Assert.DoesNotContain(
                inventory.Sources.Keys,
                path => path.Contains("/obj/", StringComparison.Ordinal)
                        || path.Contains("/bin/", StringComparison.Ordinal));

            foreach (var mutation in new[]
                     {
                         "<Compile Remove=\"Program.cs\" />",
                         "<Compile Update=\"Program.cs\" />",
                         "<Compile Include=\"..\\Safe.cs\" Exclude=\"Program.cs\" />",
                     })
            {
                var mutatedProject = baseProject.Replace(
                    "</Project>",
                    $"<ItemGroup>{mutation}</ItemGroup></Project>",
                    StringComparison.Ordinal);
                File.WriteAllText(projectPath, mutatedProject);
                Assert.Contains(
                    ReadSqlServerRcsiToolSourceInventory(repositoryRoot).DefinitionErrors,
                    error => error.Contains("RCSI tool Compile", StringComparison.Ordinal));
            }
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
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
    [InlineData("OpenApiDocumentsDirectory", "../../../artifacts/openapi")]
    [InlineData(
        "OpenApiGenerateDocumentsOptions",
        "--document-name goymen-erp-v1 --file-name openapi")]
    public void Api_OpenApi_canonical_artifact_path_and_name_are_exactly_guarded(
        string propertyName,
        string unsafeValue)
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            repositoryRoot,
            ApiProjectPath.Replace('/', Path.DirectorySeparatorChar)));
        document.Descendants()
            .Single(element => element.Name.LocalName == propertyName)
            .Value = unsafeValue;

        var analysis = AnalyzeProjectDefinition(document, ApiProjectPath);

        Assert.Contains(analysis.Errors, error => error.Contains(
            $"API OpenAPI property {propertyName}",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Api_OpenApi_build_tool_and_security_pin_versions_are_exactly_guarded()
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            repositoryRoot,
            ApiProjectPath.Replace('/', Path.DirectorySeparatorChar)));
        document.Descendants()
            .Single(element =>
                element.Name.LocalName == "PackageReference"
                && element.Attribute("Include")?.Value
                    == "Microsoft.Extensions.ApiDescription.Server")
            .SetAttributeValue("Version", "10.0.8");
        document.Descendants()
            .Single(element =>
                element.Name.LocalName == "PackageReference"
                && element.Attribute("Include")?.Value == "Microsoft.OpenApi")
            .SetAttributeValue("Version", "2.7.5");

        var analysis = AnalyzeProjectDefinition(document, ApiProjectPath);

        Assert.Contains(analysis.Errors, error => error.Contains(
            "Microsoft.Extensions.ApiDescription.Server",
            StringComparison.Ordinal));
        Assert.Contains(analysis.Errors, error => error.Contains(
            "Microsoft.OpenApi",
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

    [Theory]
    [InlineData("missing", "exactly two canonical edges")]
    [InlineData("extra", "exactly two canonical edges")]
    [InlineData("unknown", "unknown property 'unexpected'")]
    [InlineData("duplicate-property", "duplicate property 'schema_version'")]
    [InlineData("wildcard", "not a canonical repository path")]
    [InlineData("alternate-caller", "caller must be the exact API composition root")]
    [InlineData("domain-target", "layer must be Application or Infrastructure")]
    public void Counterparty_contract_rejects_non_exact_declarations(
        string scenario,
        string expectedError)
    {
        var json = scenario switch
        {
            "missing" => CreateCounterpartyContractJson(includeInfrastructure: false),
            "extra" => CreateCounterpartyContractJson(includeExtraEdge: true),
            "unknown" => CreateCounterpartyContractJson(includeUnknownProperty: true),
            "duplicate-property" => CreateCounterpartyContractJson().Replace(
                "{\"schema_version\":1,",
                "{\"schema_version\":1,\"schema_version\":1,",
                StringComparison.Ordinal),
            "wildcard" => CreateCounterpartyContractJson(
                applicationTarget: ExcludedCounterpartyPrefix + "*.csproj"),
            "alternate-caller" => CreateCounterpartyContractJson(
                source: "src/backend/Alternate/Alternate.csproj"),
            "domain-target" => CreateCounterpartyContractJson(
                applicationTarget:
                    ExcludedCounterpartyPrefix
                    + "GoymenERP.Modules.Counterparty.Domain/"
                    + "GoymenERP.Modules.Counterparty.Domain.csproj",
                applicationLayer: "Domain"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };
        var errors = new List<string>();

        _ = ParseCounterpartyCompositionRootContract(json, errors);

        Assert.Contains(errors, error => error.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void Required_counterparty_contract_cannot_disappear_with_all_observed_edges()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-tenant-guard-required-contract-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "backend"));

            var inventory = ReadBackendSourceInventory(
                repositoryRoot,
                requireCounterpartyContract: true);

            Assert.Contains(inventory.DefinitionErrors, error => error.Contains(
                "composition-root contract is missing",
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
    public void Counterparty_contract_and_observed_edges_must_match_exactly()
    {
        var parseErrors = new List<string>();
        var contract = ParseCounterpartyCompositionRootContract(
            CreateCounterpartyContractJson(),
            parseErrors);
        Assert.Empty(parseErrors);

        var exactEdges = contract.Edges.ToArray();
        Assert.Empty(ValidateCounterpartyCompositionRootEdges(contract, exactEdges));

        var missingErrors = ValidateCounterpartyCompositionRootEdges(
            contract,
            exactEdges.Take(1).ToArray());
        Assert.Contains(missingErrors, error => error.Contains(
            "Stale Counterparty composition-root declaration",
            StringComparison.Ordinal));

        var extraErrors = ValidateCounterpartyCompositionRootEdges(
            contract,
            exactEdges.Append(new CompositionRootEdge(
                "src/backend/Unexpected/Unexpected.csproj",
                ExcludedCounterpartyPrefix + "Unexpected/Unexpected.csproj")).ToArray());
        Assert.Contains(extraErrors, error => error.Contains(
            "without an exact module-owned declaration",
            StringComparison.Ordinal));

        var duplicateErrors = ValidateCounterpartyCompositionRootEdges(
            contract,
            exactEdges.Append(exactEdges[0]).ToArray());
        Assert.Contains(duplicateErrors, error => error.Contains(
            "Duplicate Counterparty composition-root ProjectReference",
            StringComparison.Ordinal));
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

    private static string CreateCounterpartyContractJson(
        string? source = null,
        string? applicationTarget = null,
        string applicationLayer = "Application",
        bool includeInfrastructure = true,
        bool includeExtraEdge = false,
        bool includeUnknownProperty = false)
    {
        source ??= ApiProjectPath;
        applicationTarget ??=
            ExcludedCounterpartyPrefix
            + "GoymenERP.Modules.Counterparty.Application/"
            + "GoymenERP.Modules.Counterparty.Application.csproj";
        var edges = new List<Dictionary<string, object?>>
        {
            new(StringComparer.Ordinal)
            {
                ["source"] = source,
                ["target"] = applicationTarget,
                ["layer"] = applicationLayer,
                ["reason"] = "Exact application composition edge."
            }
        };
        if (includeInfrastructure)
        {
            edges.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["source"] = source,
                ["target"] =
                    ExcludedCounterpartyPrefix
                    + "GoymenERP.Modules.Counterparty.Infrastructure/"
                    + "GoymenERP.Modules.Counterparty.Infrastructure.csproj",
                ["layer"] = "Infrastructure",
                ["reason"] = "Exact infrastructure composition edge."
            });
        }

        if (includeExtraEdge)
        {
            edges.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["source"] = source,
                ["target"] = applicationTarget,
                ["layer"] = applicationLayer,
                ["reason"] = "Duplicate extra edge."
            });
        }

        var contract = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schema_version"] = 1,
            ["owner"] = "CHATGPT-CARI",
            ["module"] = "Counterparty",
            ["excluded_source_prefix"] = ExcludedCounterpartyPrefix,
            ["contract_source"] = CounterpartyContractSourcePath,
            ["composition_root_edges"] = edges
        };
        if (includeUnknownProperty)
        {
            contract["unexpected"] = true;
        }

        return JsonSerializer.Serialize(contract);
    }

    private static BackendSourceInventory ReadBackendSourceInventory(
        string repositoryRoot,
        bool requireCounterpartyContract = false)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var definitionErrors = new List<string>();
        var projectPaths = new List<string>();
        var observedCounterpartyCompositionRootEdges = new List<CompositionRootEdge>();
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
                observedCounterpartyCompositionRootEdges,
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
        var counterpartyContractFullPath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            CounterpartyCompositionRootContractPath.Replace(
                '/',
                Path.DirectorySeparatorChar)));
        if (requireCounterpartyContract
            || observedCounterpartyCompositionRootEdges.Count > 0
            || File.Exists(counterpartyContractFullPath))
        {
            var counterpartyContract = ReadCounterpartyCompositionRootContract(
                repositoryRoot,
                definitionErrors);
            definitionErrors.AddRange(ValidateCounterpartyCompositionRootEdges(
                counterpartyContract,
                observedCounterpartyCompositionRootEdges));
        }

        return new BackendSourceInventory(
            sources,
            projectPaths.Order(StringComparer.Ordinal).ToArray(),
            definitionErrors);
    }

    private static CounterpartyCompositionRootContract ReadCounterpartyCompositionRootContract(
        string repositoryRoot,
        ICollection<string> errors)
    {
        var contractPath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            CounterpartyCompositionRootContractPath.Replace(
                '/',
                Path.DirectorySeparatorChar)));
        if (!IsUnderDirectory(repositoryRoot, contractPath) || !File.Exists(contractPath))
        {
            errors.Add(
                "Counterparty composition-root contract is missing or outside the repository: "
                + CounterpartyCompositionRootContractPath);
            return new CounterpartyCompositionRootContract([]);
        }

        CounterpartyCompositionRootContract contract;
        try
        {
            contract = ParseCounterpartyCompositionRootContract(
                File.ReadAllText(contractPath),
                errors);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException)
        {
            errors.Add(
                "Counterparty composition-root contract could not be read as strict JSON: "
                + exception.Message);
            return new CounterpartyCompositionRootContract([]);
        }

        var contractSourcePath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            CounterpartyContractSourcePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnderDirectory(repositoryRoot, contractSourcePath)
            || !File.Exists(contractSourcePath))
        {
            errors.Add(
                "Counterparty composition-root contract source is missing or outside the "
                + $"repository: {CounterpartyContractSourcePath}");
        }

        return contract;
    }

    private static CounterpartyCompositionRootContract ParseCounterpartyCompositionRootContract(
        string json,
        ICollection<string> errors)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Counterparty composition-root contract root must be an object.");
            return new CounterpartyCompositionRootContract([]);
        }

        ValidateExactJsonProperties(
            root,
            [
                "schema_version",
                "owner",
                "module",
                "excluded_source_prefix",
                "contract_source",
                "composition_root_edges"
            ],
            "Counterparty composition-root contract",
            errors);

        ValidateExactJsonInteger(
            root,
            "schema_version",
            expected: 1,
            "Counterparty composition-root contract",
            errors);
        ValidateExactJsonString(
            root,
            "owner",
            "CHATGPT-CARI",
            "Counterparty composition-root contract",
            errors);
        ValidateExactJsonString(
            root,
            "module",
            "Counterparty",
            "Counterparty composition-root contract",
            errors);
        ValidateExactJsonString(
            root,
            "excluded_source_prefix",
            ExcludedCounterpartyPrefix,
            "Counterparty composition-root contract",
            errors);
        ValidateExactJsonString(
            root,
            "contract_source",
            CounterpartyContractSourcePath,
            "Counterparty composition-root contract",
            errors);

        if (!TryGetSingleJsonProperty(
                root,
                "composition_root_edges",
                JsonValueKind.Array,
                "Counterparty composition-root contract",
                errors,
                out var edgeArray))
        {
            return new CounterpartyCompositionRootContract([]);
        }

        var edgeElements = edgeArray.EnumerateArray().ToArray();
        if (edgeElements.Length != 2)
        {
            errors.Add(
                "Counterparty composition-root contract must declare exactly two canonical "
                + $"edges, but found {edgeElements.Length}.");
        }

        var edges = new List<CompositionRootEdge>();
        var layers = new List<string>();
        for (var index = 0; index < edgeElements.Length; index++)
        {
            var edge = edgeElements[index];
            var context = $"Counterparty composition-root edge[{index}]";
            if (edge.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{context} must be an object.");
                continue;
            }

            ValidateExactJsonProperties(
                edge,
                ["source", "target", "layer", "reason"],
                context,
                errors);
            if (!TryReadJsonString(edge, "source", context, errors, out var source)
                || !TryReadJsonString(edge, "target", context, errors, out var target)
                || !TryReadJsonString(edge, "layer", context, errors, out var layer)
                || !TryReadJsonString(edge, "reason", context, errors, out var reason))
            {
                continue;
            }

            if (!IsCanonicalContractRepositoryPath(source))
            {
                errors.Add($"{context} source is not a canonical repository path: {source}");
            }

            if (!IsCanonicalContractRepositoryPath(target))
            {
                errors.Add($"{context} target is not a canonical repository path: {target}");
            }

            if (!string.Equals(source, ApiProjectPath, StringComparison.Ordinal))
            {
                errors.Add(
                    $"{context} caller must be the exact API composition root: {source}");
            }

            var expectedTarget = layer switch
            {
                "Application" =>
                    ExcludedCounterpartyPrefix
                    + "GoymenERP.Modules.Counterparty.Application/"
                    + "GoymenERP.Modules.Counterparty.Application.csproj",
                "Infrastructure" =>
                    ExcludedCounterpartyPrefix
                    + "GoymenERP.Modules.Counterparty.Infrastructure/"
                    + "GoymenERP.Modules.Counterparty.Infrastructure.csproj",
                _ => null
            };
            if (expectedTarget is null)
            {
                errors.Add(
                    $"{context} layer must be Application or Infrastructure: {layer}");
            }
            else if (!string.Equals(target, expectedTarget, StringComparison.Ordinal))
            {
                errors.Add(
                    $"{context} target does not match its exact canonical {layer} project: "
                    + target);
            }

            if (string.IsNullOrWhiteSpace(reason) || reason.Length != reason.Trim().Length)
            {
                errors.Add($"{context} reason must be non-empty and trimmed.");
            }

            layers.Add(layer);
            edges.Add(new CompositionRootEdge(source, target));
        }

        if (edges.Distinct().Count() != edges.Count)
        {
            errors.Add("Counterparty composition-root contract contains duplicate edges.");
        }

        if (layers.Count(layer => string.Equals(
                layer,
                "Application",
                StringComparison.Ordinal)) != 1
            || layers.Count(layer => string.Equals(
                layer,
                "Infrastructure",
                StringComparison.Ordinal)) != 1)
        {
            errors.Add(
                "Counterparty composition-root contract must contain exactly one Application "
                + "and one Infrastructure edge.");
        }

        return new CounterpartyCompositionRootContract(edges);
    }

    private static IReadOnlyList<string> ValidateCounterpartyCompositionRootEdges(
        CounterpartyCompositionRootContract contract,
        IReadOnlyCollection<CompositionRootEdge> observedEdges)
    {
        var errors = new List<string>();
        var duplicateObservedEdges = observedEdges
            .GroupBy(edge => edge)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target, StringComparer.Ordinal)
            .ToArray();
        foreach (var duplicate in duplicateObservedEdges)
        {
            errors.Add(
                "Duplicate Counterparty composition-root ProjectReference was observed: "
                + $"{duplicate.Source} -> {duplicate.Target}");
        }

        var declared = contract.Edges.ToHashSet();
        var observed = observedEdges.ToHashSet();
        foreach (var missing in declared.Except(observed)
                     .OrderBy(edge => edge.Source, StringComparer.Ordinal)
                     .ThenBy(edge => edge.Target, StringComparer.Ordinal))
        {
            errors.Add(
                "Stale Counterparty composition-root declaration has no matching "
                + $"ProjectReference: {missing.Source} -> {missing.Target}");
        }

        foreach (var extra in observed.Except(declared)
                     .OrderBy(edge => edge.Source, StringComparer.Ordinal)
                     .ThenBy(edge => edge.Target, StringComparer.Ordinal))
        {
            errors.Add(
                "A non-Counterparty project cannot reference the excluded Counterparty "
                + "ownership scope without an exact module-owned declaration: "
                + $"{extra.Target} ({extra.Source})");
        }

        return errors;
    }

    private static void ValidateExactJsonProperties(
        JsonElement element,
        IReadOnlyCollection<string> expectedProperties,
        string context,
        ICollection<string> errors)
    {
        var properties = element.EnumerateObject().ToArray();
        foreach (var duplicate in properties
                     .GroupBy(property => property.Name, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key)
                     .Order(StringComparer.Ordinal))
        {
            errors.Add($"{context} contains duplicate property '{duplicate}'.");
        }

        var expected = expectedProperties.ToHashSet(StringComparer.Ordinal);
        foreach (var unknown in properties
                     .Select(property => property.Name)
                     .Where(property => !expected.Contains(property))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            errors.Add($"{context} contains unknown property '{unknown}'.");
        }

        var actual = properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var missing in expected
                     .Where(property => !actual.Contains(property))
                     .Order(StringComparer.Ordinal))
        {
            errors.Add($"{context} is missing property '{missing}'.");
        }
    }

    private static void ValidateExactJsonInteger(
        JsonElement element,
        string propertyName,
        int expected,
        string context,
        ICollection<string> errors)
    {
        if (!TryGetSingleJsonProperty(
                element,
                propertyName,
                JsonValueKind.Number,
                context,
                errors,
                out var value))
        {
            return;
        }

        if (!value.TryGetInt32(out var actual) || actual != expected)
        {
            errors.Add(
                $"{context} property '{propertyName}' must equal {expected}, found "
                + value.GetRawText());
        }
    }

    private static void ValidateExactJsonString(
        JsonElement element,
        string propertyName,
        string expected,
        string context,
        ICollection<string> errors)
    {
        if (!TryReadJsonString(
                element,
                propertyName,
                context,
                errors,
                out var actual))
        {
            return;
        }

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            errors.Add(
                $"{context} property '{propertyName}' must equal '{expected}', found "
                + $"'{actual}'.");
        }
    }

    private static bool TryReadJsonString(
        JsonElement element,
        string propertyName,
        string context,
        ICollection<string> errors,
        out string value)
    {
        if (!TryGetSingleJsonProperty(
                element,
                propertyName,
                JsonValueKind.String,
                context,
                errors,
                out var property))
        {
            value = string.Empty;
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetSingleJsonProperty(
        JsonElement element,
        string propertyName,
        JsonValueKind expectedKind,
        string context,
        ICollection<string> errors,
        out JsonElement value)
    {
        var matches = element.EnumerateObject()
            .Where(property => string.Equals(
                property.Name,
                propertyName,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            value = default;
            return false;
        }

        value = matches[0].Value;
        if (value.ValueKind == expectedKind)
        {
            return true;
        }

        errors.Add(
            $"{context} property '{propertyName}' must be {expectedKind}, found "
            + $"{value.ValueKind}.");
        return false;
    }

    private static bool IsCanonicalContractRepositoryPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != value.Trim().Length
            || value.StartsWith("/", StringComparison.Ordinal)
            || value.Contains('\\')
            || value.IndexOfAny(['*', '?', '[', ']', ':']) >= 0)
        {
            return false;
        }

        var segments = value.Split('/');
        return segments.Length > 1
               && segments.All(segment => segment.Length > 0
                                          && !string.Equals(
                                              segment,
                                              ".",
                                              StringComparison.Ordinal)
                                          && !string.Equals(
                                              segment,
                                              "..",
                                              StringComparison.Ordinal));
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
        ICollection<CompositionRootEdge> observedCounterpartyCompositionRootEdges,
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
                observedCounterpartyCompositionRootEdges.Add(new CompositionRootEdge(
                    repositoryProjectPath,
                    targetRepositoryPath));
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
                var propertyName = property.Name.LocalName;
                var isReviewedOpenApiProperty = string.Equals(
                        repositoryProjectPath,
                        ApiProjectPath,
                        StringComparison.Ordinal)
                    && ReviewedApiOpenApiProperties.TryGetValue(
                        propertyName,
                        out var reviewedOpenApiValue)
                    && string.Equals(
                        property.Value.Trim(),
                        reviewedOpenApiValue,
                        StringComparison.Ordinal);
                if (!ReviewedProjectProperties.Contains(propertyName)
                    && !isReviewedOpenApiProperty)
                {
                    errors.Add(
                        $"Unreviewed project property {propertyName} can alter "
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

        if (string.Equals(repositoryProjectPath, ApiProjectPath, StringComparison.Ordinal))
        {
            foreach (var (propertyName, reviewedValue) in ReviewedApiOpenApiProperties)
            {
                var matches = root.Descendants()
                    .Where(element => string.Equals(
                        element.Name.LocalName,
                        propertyName,
                        StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length != 1
                    || !string.Equals(
                        matches[0].Value.Trim(),
                        reviewedValue,
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        $"API OpenAPI property {propertyName} must occur exactly once with "
                        + $"the reviewed value '{reviewedValue}': {repositoryProjectPath}");
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
                var includedContent = item.Attribute("Include")?.Value;
                var isReviewedPdfFontContent = string.Equals(
                        repositoryProjectPath,
                        PdfProjectPath,
                        StringComparison.Ordinal)
                    && string.Equals(item.Name.LocalName, "Content", StringComparison.Ordinal)
                    && includedContent is not null
                    && ReviewedPdfContentPaths.Contains(includedContent)
                    && string.Equals(
                        item.Attribute("CopyToOutputDirectory")?.Value,
                        "PreserveNewest",
                        StringComparison.Ordinal)
                    && string.Equals(
                        item.Attribute("CopyToPublishDirectory")?.Value,
                        "PreserveNewest",
                        StringComparison.Ordinal)
                    && item.Attributes().Count() == 3
                    && !item.Elements().Any();
                if (!ReviewedProjectItems.Contains(item.Name.LocalName)
                    && !isReviewedPdfFontContent)
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

        if (string.Equals(repositoryProjectPath, PdfProjectPath, StringComparison.Ordinal))
        {
            var pdfsharpReferences = root.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Where(element => string.Equals(
                    element.Attribute("Include")?.Value,
                    "PDFsharp",
                    StringComparison.Ordinal))
                .ToArray();
            if (pdfsharpReferences.Length != 1)
            {
                errors.Add("The PDF component must reference PDFsharp exactly once.");
            }

            var contentElements = root.Descendants()
                .Where(element => element.Name.LocalName == "Content")
                .ToArray();
            var reviewedContent = contentElements
                .Select(element => element.Attribute("Include")?.Value)
                .Where(path => path is not null)
                .ToHashSet(StringComparer.Ordinal);
            if (contentElements.Length != ReviewedPdfContentPaths.Count
                || reviewedContent.Count != ReviewedPdfContentPaths.Count
                || !reviewedContent.SetEquals(ReviewedPdfContentPaths))
            {
                errors.Add("The PDF component content inventory must match the reviewed font and license assets.");
            }
        }


        if (string.Equals(repositoryProjectPath, ApiProjectPath, StringComparison.Ordinal))
        {
            var apiPackageNames = root.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element => element.Attribute("Include")?.Value.Trim())
                .Where(name => name is not null)
                .ToArray();
            foreach (var packageName in RequiredApiOpenApiPackages)
            {
                if (apiPackageNames.Count(name => string.Equals(
                        name,
                        packageName,
                        StringComparison.Ordinal)) != 1)
                {
                    errors.Add(
                        $"API must reference exact OpenAPI package {packageName} once: "
                        + repositoryProjectPath);
                }
            }
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

        if (string.Equals(include, "PDFsharp", StringComparison.Ordinal)
            && !string.Equals(repositoryProjectPath, PdfProjectPath, StringComparison.Ordinal))
        {
            errors.Add("PDFsharp is allowed only in the isolated PDF component project.");
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

        if (string.Equals(
                include,
                "Microsoft.Extensions.ApiDescription.Server",
                StringComparison.Ordinal))
        {
            var includeAssets = packageReference.Elements()
                .SingleOrDefault(element => element.Name.LocalName == "IncludeAssets")
                ?.Value.Trim();
            var privateAssets = packageReference.Elements()
                .SingleOrDefault(element => element.Name.LocalName == "PrivateAssets")
                ?.Value.Trim();
            if (!string.Equals(
                    includeAssets,
                    "runtime; build; native; contentfiles; analyzers",
                    StringComparison.Ordinal)
                || !string.Equals(privateAssets, "all", StringComparison.Ordinal))
            {
                errors.Add(
                    "Microsoft.Extensions.ApiDescription.Server must remain a private, "
                    + "exact build-time dependency with its reviewed IncludeAssets set: "
                    + repositoryProjectPath);
            }
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
            return;
        }

        current = new DirectoryInfo(repositoryRoot).Parent;
        while (current is not null)
        {
            foreach (var fileName in new[]
                     {
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

            current = current.Parent;
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
            var normalizedPath = NormalizeRepositoryPath(path);
            foreach (Match directive in ConditionalCompilationDirectiveRegex().Matches(source))
            {
                errors.Add(
                    "Conditional compilation is forbidden in the platform tenant-guard "
                    + $"source inventory because disabled text can hide reviewed code: "
                    + $"{NormalizeRepositoryPath(path)}:"
                    + GetLineNumber(source, directive.Index));
            }

            if (string.Equals(
                    normalizedPath,
                    OpenApiGenerationModePath,
                    StringComparison.Ordinal))
            {
                var fingerprint = ComputeReviewedSourceFingerprint(source);
                if (!string.Equals(
                        fingerprint,
                        ReviewedOpenApiGenerationModeFingerprint,
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        "OpenAPI build-time entry-assembly detection left its exact reviewed "
                        + "source fingerprint. Expected "
                        + $"{ReviewedOpenApiGenerationModeFingerprint}, found {fingerprint}: "
                        + normalizedPath);
                }
                else
                {
                    continue;
                }
            }

            if (string.Equals(
                    normalizedPath,
                    ReviewedSqlServerRcsiToolProgramPath,
                    StringComparison.Ordinal))
            {
                var fingerprint = ComputeReviewedSourceFingerprint(source);
                var reviewedFingerprint = ReviewedRuntimeDatabaseBypassSources[
                    ReviewedSqlServerRcsiToolProgramPath].ReviewedFingerprint;
                if (!string.Equals(
                        fingerprint,
                        reviewedFingerprint,
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        "SQL Server RCSI tool reflection surface left its exact reviewed "
                        + $"source fingerprint. Expected {reviewedFingerprint}, found "
                        + $"{fingerprint}: {normalizedPath}");
                }
                else
                {
                    continue;
                }
            }

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
        IReadOnlyDictionary<string, string> sources,
        bool requireReviewedSources = false)
    {
        var errors = new List<string>();
        var occurrences = FindDirectDatabaseBypassOccurrences(sources);

        foreach (var occurrence in occurrences)
        {
            var isReviewed = ReviewedRuntimeDatabaseBypassSources.TryGetValue(
                                 occurrence.RepositoryPath,
                                 out var reviewed)
                             && reviewed.ExpectedApiOccurrences.ContainsKey(
                                 occurrence.ApiName);
            if (!isReviewed)
            {
                errors.Add(
                    "Forbidden direct database/query-filter bypass capability "
                    + $"'{occurrence.ApiName}' in "
                    + $"{DescribeSourceCategory(occurrence.SourceCategory)} source at "
                    + $"{occurrence.RepositoryPath}:{occurrence.LineNumber}");
            }
        }

        foreach (var (reviewedPath, reviewed) in ReviewedRuntimeDatabaseBypassSources)
        {
            if (!sources.TryGetValue(reviewedPath, out var source))
            {
                if (requireReviewedSources)
                {
                    errors.Add(
                        "Reviewed runtime database bypass source is missing: "
                        + reviewedPath);
                }

                continue;
            }

            if (ClassifySource(reviewedPath) != PlatformSourceCategory.RuntimeApplication)
            {
                errors.Add(
                    "Reviewed runtime database bypass source left the runtime application "
                    + $"category: {reviewedPath}");
            }

            var actualFingerprint = ComputeReviewedSourceFingerprint(source);
            if (!string.Equals(
                    actualFingerprint,
                    reviewed.ReviewedFingerprint,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    "Reviewed runtime database bypass source does not match the exact "
                    + $"fingerprint. Expected {reviewed.ReviewedFingerprint}, found "
                    + $"{actualFingerprint}: {reviewedPath}");
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
                        $"Expected exactly {expectedCount} reviewed runtime '{apiName}' "
                        + $"occurrence(s), but found {actualCount}: {reviewedPath}");
                }
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateReviewedSqlServerRcsiToolProject(
        string projectSource)
    {
        var errors = new List<string>();
        var fingerprint = ComputeReviewedSourceFingerprint(projectSource);
        if (!string.Equals(
                fingerprint,
                ReviewedSqlServerRcsiToolProjectFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                "SQL Server RCSI tool project does not match its exact reviewed fingerprint. "
                + $"Expected {ReviewedSqlServerRcsiToolProjectFingerprint}, found "
                + $"{fingerprint}: {ReviewedSqlServerRcsiToolProjectPath}");
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(projectSource, LoadOptions.SetLineInfo);
        }
        catch (System.Xml.XmlException exception)
        {
            errors.Add(
                $"SQL Server RCSI tool project is not valid XML: {exception.Message}");
            return errors;
        }

        var exactPackages = document.Descendants("PackageReference")
            .Where(element => string.Equals(
                element.Attribute("Include")?.Value,
                "Microsoft.Data.SqlClient",
                StringComparison.Ordinal)
                && string.Equals(
                    element.Attribute("Version")?.Value,
                    "6.1.1",
                    StringComparison.Ordinal))
            .ToArray();
        if (exactPackages.Length != 1)
        {
            errors.Add(
                "SQL Server RCSI tool must retain exactly one exact "
                + "Microsoft.Data.SqlClient package reference at version 6.1.1.");
        }

        var exactProjectReferences = document.Descendants("ProjectReference")
            .Where(element => string.Equals(
                NormalizeRepositoryPath(element.Attribute("Include")?.Value ?? string.Empty),
                "../../src/backend/BuildingBlocks/"
                + "GoymenERP.BuildingBlocks.SqlServer/"
                + "GoymenERP.BuildingBlocks.SqlServer.csproj",
                StringComparison.Ordinal))
            .ToArray();
        if (exactProjectReferences.Length != 1)
        {
            errors.Add(
                "SQL Server RCSI tool must retain exactly one exact shared SQL Server "
                + "project reference.");
        }

        var exactRuntimeIdentifiers = document.Descendants("RuntimeIdentifiers")
            .Where(element => !element.HasAttributes
                              && !element.Elements().Any()
                              && string.Equals(
                                  element.Value.Trim(),
                                  "$(NETCoreSdkRuntimeIdentifier)",
                                  StringComparison.Ordinal))
            .ToArray();
        if (exactRuntimeIdentifiers.Length != 1)
        {
            errors.Add(
                "SQL Server RCSI tool must bind self-contained publication to the "
                + "current reviewed SDK runtime identifier.");
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateReviewedSqlServerRcsiWrapper(
        string wrapperSource)
    {
        var errors = new List<string>();
        var fingerprint = ComputeReviewedSourceFingerprint(wrapperSource);
        if (!string.Equals(
                fingerprint,
                ReviewedSqlServerRcsiWrapperFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                "SQL Server RCSI wrapper does not match its exact reviewed fingerprint. "
                + $"Expected {ReviewedSqlServerRcsiWrapperFingerprint}, found {fingerprint}: "
                + ReviewedSqlServerRcsiWrapperPath);
        }

        foreach (var required in new[]
                 {
                     "#requires -Version 7.2",
                     "[Parameter(Mandatory)][string]$ExpectedWrapperScriptSha256",
                      "Get-VerifiedWrapperScriptIdentity",
                      "$finalWrapperScript = Get-VerifiedWrapperScriptIdentity",
                      "if ($evidenceValidated -and $null -ne $validatedStdoutBytes)",
                      "'--expected-wrapper-script-sha256'",
                     "'--observed-wrapper-script-sha256'",
                     "'expectedWrapperScriptSha256'",
                     "'observedWrapperScriptSha256'",
                     "[Diagnostics.ProcessStartInfo]::new($verifiedPayload.EntryPath)",
                     "$startInfo.Environment.Clear()",
                     "$startInfo.RedirectStandardInput = $true",
                     "'--connection-secret-stdin', 'true'",
                     "[GoymenErpRcsiProcessContainment]::CreateWindowsJob()",
                     "[GoymenErpRcsiProcessContainment]::KillUnixProcessGroup",
                     "$overallDeadlineStopwatch",
                     "[Array]::Clear($probeSecretBytes",
                     "-ccontains",
                     "$process.Kill($true)",
                     "exit $scriptExitCode",
                 })
        {
            if (!wrapperSource.Contains(required, StringComparison.Ordinal))
            {
                errors.Add($"SQL Server RCSI wrapper lost required fail-closed surface: {required}");
            }
        }

        foreach (var (pattern, description) in new[]
                 {
                     (@"(?i)\bALTER\s+DATABASE\b", "ALTER DATABASE"),
                     (@"(?i)\bInvoke-Expression\b", "Invoke-Expression"),
                     ("(?i)ProcessStartInfo\\]\\s*::\\s*new\\s*\\(\\s*['\"]dotnet['\"]", "PATH dotnet host"),
                     (@"(?i)\b(?:Password|Pwd|User\s+ID|Server|Data\s+Source)\s*=", "plaintext connection value"),
                 })
        {
            if (Regex.IsMatch(wrapperSource, pattern, RegexOptions.CultureInvariant))
            {
                errors.Add($"SQL Server RCSI wrapper contains forbidden {description} surface.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateSqlServerRcsiPublishAndDiscoveryContracts(
        string solutionSource,
        string integrationProjectSource,
        string relationalAcceptanceSource,
        string workflowSource)
    {
        var errors = new List<string>();
        var workflowFingerprint = ComputeReviewedSourceFingerprint(workflowSource);
        if (!string.Equals(
                workflowFingerprint,
                ReviewedContinuousIntegrationWorkflowFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                "RCSI CI workflow left its exact reviewed fingerprint. Expected "
                + $"{ReviewedContinuousIntegrationWorkflowFingerprint}, found "
                + $"{workflowFingerprint}: {ContinuousIntegrationWorkflowPath}");
        }

        XDocument solution;
        XDocument project;
        try
        {
            solution = XDocument.Parse(solutionSource, LoadOptions.SetLineInfo);
            project = XDocument.Parse(integrationProjectSource, LoadOptions.SetLineInfo);
        }
        catch (System.Xml.XmlException exception)
        {
            return [$"RCSI publish/discovery XML is invalid: {exception.Message}"];
        }

        var integrationProjectFingerprint =
            ComputeReviewedSourceFingerprint(integrationProjectSource);
        if (!string.Equals(
                integrationProjectFingerprint,
                ReviewedSqlServerRcsiIntegrationProjectFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                "RCSI integration publish project left its exact reviewed fingerprint. "
                + $"Expected {ReviewedSqlServerRcsiIntegrationProjectFingerprint}, found "
                + $"{integrationProjectFingerprint}: {SqlServerRcsiIntegrationProjectPath}");
        }

        foreach (var requiredProject in new[]
                 {
                     "tools/GoymenERP.SqlServerRcsiReadiness/"
                     + "GoymenERP.SqlServerRcsiReadiness.csproj",
                     "tests/GoymenERP.SqlServerRcsiReadiness.Tests/"
                     + "GoymenERP.SqlServerRcsiReadiness.Tests.csproj",
                     SqlServerRcsiIntegrationProjectPath,
                 })
        {
            var matches = solution.Descendants("Project")
                .Count(element => string.Equals(
                    NormalizeRepositoryPath(element.Attribute("Path")?.Value ?? string.Empty),
                    requiredProject,
                    StringComparison.Ordinal));
            if (matches != 1)
            {
                errors.Add(
                    "RCSI solution membership must contain exactly one reviewed project: "
                    + requiredProject);
            }
        }

        var publishDirectoryProperties = project.Descendants("RcsiToolSelfContainedPublishDir")
            .Where(element => !element.HasAttributes
                              && !element.Elements().Any()
                              && string.Equals(
                element.Value.Trim(),
                "$(MSBuildProjectDirectory)\\bin\\$(Configuration)\\"
                + "rcsi-tool-self-contained\\$(NETCoreSdkRuntimeIdentifier)\\",
                StringComparison.Ordinal))
            .ToArray();
        if (publishDirectoryProperties.Length != 1)
        {
            errors.Add(
                "RCSI self-contained publish directory must remain runtime-specific and reviewed.");
        }

        var toolProjectProperties = project.Descendants("RcsiToolProject")
            .Where(element => !element.HasAttributes
                              && !element.Elements().Any()
                              && string.Equals(
                                  element.Value.Trim(),
                                  "$(MSBuildProjectDirectory)\\..\\..\\tools\\"
                                  + "GoymenERP.SqlServerRcsiReadiness\\"
                                  + "GoymenERP.SqlServerRcsiReadiness.csproj",
                                  StringComparison.Ordinal))
            .ToArray();
        if (toolProjectProperties.Length != 1)
        {
            errors.Add("RCSI publish caller must retain the exact reviewed tool project path.");
        }

        var publishTargets = project.Descendants("Target")
            .Where(element => string.Equals(
                element.Attribute("Name")?.Value,
                "PublishSelfContainedSqlServerRcsiReadinessTool",
                StringComparison.Ordinal))
            .ToArray();
        if (publishTargets.Length != 1)
        {
            errors.Add("RCSI integration project must retain exactly one reviewed publish target.");
        }
        else
        {
            var target = publishTargets[0];
            var tasks = target.Elements().ToArray();
            if (target.Attributes().Count() != 3
                || !string.Equals(
                    target.Attribute("AfterTargets")?.Value,
                    "Build",
                    StringComparison.Ordinal)
                || !string.Equals(
                    target.Attribute("Condition")?.Value,
                    "'$(DesignTimeBuild)' != 'true' and "
                    + "'$(IsCrossTargetingBuild)' != 'true'",
                    StringComparison.Ordinal))
            {
                errors.Add("RCSI self-contained publish target scheduling drifted.");
            }

            if (tasks.Length != 2
                || !string.Equals(tasks[0].Name.LocalName, "RemoveDir", StringComparison.Ordinal)
                || tasks[0].Attributes().Count() != 1
                || !string.Equals(
                    tasks[0].Attribute("Directories")?.Value,
                    "$(RcsiToolSelfContainedPublishDir)",
                    StringComparison.Ordinal))
            {
                errors.Add("RCSI publish directory must be cleaned before publication.");
            }

            var publishTask = tasks.Length == 2
                && string.Equals(tasks[1].Name.LocalName, "MSBuild", StringComparison.Ordinal)
                    ? tasks[1]
                    : null;
            const string reviewedPublishProperties =
                "Configuration=$(Configuration);"
                + "RuntimeIdentifier=$(NETCoreSdkRuntimeIdentifier);"
                + "SelfContained=true;PublishSingleFile=false;PublishTrimmed=false;"
                + "PublishDir=$(RcsiToolSelfContainedPublishDir);NoRestore=true";
            if (publishTask is null
                || publishTask.Attributes().Count() != 4
                || !string.Equals(
                    publishTask.Attribute("Projects")?.Value,
                    "$(RcsiToolProject)",
                    StringComparison.Ordinal)
                || !string.Equals(
                    publishTask.Attribute("Targets")?.Value,
                    "Publish",
                    StringComparison.Ordinal)
                || !string.Equals(
                    publishTask.Attribute("BuildInParallel")?.Value,
                    "false",
                    StringComparison.Ordinal)
                || !string.Equals(
                    publishTask.Attribute("Properties")?.Value,
                    reviewedPublishProperties,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    "RCSI publish caller must retain exact RuntimeIdentifier and "
                    + "SelfContained=true semantics.");
            }
        }

        if (Regex.IsMatch(
                relationalAcceptanceSource,
                @"(?m)^\s*#\s*(?:if|elif|else|endif)\b",
                RegexOptions.CultureInvariant)
            || Regex.Matches(
                relationalAcceptanceSource,
                @"\bpublic\s+sealed\s+class\s+"
                + "SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests\\b",
                RegexOptions.CultureInvariant).Count != 1
            || Regex.Matches(
                relationalAcceptanceSource,
                Regex.Escape("[SqlServerFact]"),
                RegexOptions.CultureInvariant).Count != 10)
        {
            errors.Add(
                "RCSI relational discovery must retain the reviewed class name and ten tests.");
        }

        if (Regex.Matches(
                relationalAcceptanceSource,
                @"\bpublic\s+sealed\s+class\s+"
                + "SqlServerRcsiReadinessProcessAcceptanceTests\\b",
                RegexOptions.CultureInvariant).Count != 1
            || Regex.Matches(
                relationalAcceptanceSource,
                Regex.Escape("[Fact]"),
                RegexOptions.CultureInvariant).Count != 3)
        {
            errors.Add(
                "RCSI process acceptance discovery must retain the reviewed "
                + "non-SQL class and three tests.");
        }

        var normalizedWorkflow = workflowSource.Replace("\r\n", "\n", StringComparison.Ordinal);
        var backendJobStart = normalizedWorkflow.IndexOf("\n  backend:\n", StringComparison.Ordinal);
        var backendJobEnd = backendJobStart < 0
            ? -1
            : normalizedWorkflow.IndexOf("\n  frontend:\n", backendJobStart, StringComparison.Ordinal);
        var backendJob = backendJobStart >= 0 && backendJobEnd > backendJobStart
            ? normalizedWorkflow[backendJobStart..backendJobEnd]
            : string.Empty;
        var backendTestStepStart = backendJob.IndexOf(
            "\n      - name: Test\n",
            StringComparison.Ordinal);
        var backendTestStep = ExtractYamlStep(backendJob, backendTestStepStart);
        var activeBackendTestLines = ReadActiveYamlLines(backendTestStep);
        var expectedBackendTestLines = new[]
        {
            "- name: Test",
            "run: |",
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py",
            "dotnet test GoymenERP.slnx -noAutoResponse --configuration Release "
            + "--no-build --logger \"console;verbosity=normal\"",
        };
        if (backendTestStepStart < 0
            || !activeBackendTestLines.SequenceEqual(
                expectedBackendTestLines,
                StringComparer.Ordinal))
        {
            errors.Add(
                "RCSI CLI/policy tests require the exact unconditional backend solution test step.");
        }

        var relationalJobStart = normalizedWorkflow.IndexOf(
            "\n  relational:\n",
            StringComparison.Ordinal);
        var relationalJobEnd = relationalJobStart < 0
            ? -1
            : normalizedWorkflow.IndexOf(
                "\n  contract-drift:\n",
                relationalJobStart,
                StringComparison.Ordinal);
        var relationalJob = relationalJobStart >= 0 && relationalJobEnd > relationalJobStart
            ? normalizedWorkflow[relationalJobStart..relationalJobEnd]
            : string.Empty;
        var activeRelationalJobLines = ReadActiveYamlLines(relationalJob);
        var broadStepStart = relationalJob.IndexOf(
            "\n      - name: Run broad SQL Server relational acceptance verifier\n",
            StringComparison.Ordinal);
        var broadStep = ExtractYamlStep(relationalJob, broadStepStart);
        var activeBroadStepLines = ReadActiveYamlLines(broadStep);
        var expectedBroadStepLines = new[]
        {
            "- name: Run broad SQL Server relational acceptance verifier",
            "run: |",
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py",
            "python3 -I -S -B -m unittest discover -s "
            + "tests/rcsi_relational_acceptance_verifier -p \"test_*.py\" -v",
            "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py --broad",
        };
        var rcsiStepStart = relationalJob.IndexOf(
            "\n      - name: Run exact RCSI relational acceptance verifier\n",
            StringComparison.Ordinal);
        var rcsiStep = ExtractYamlStep(relationalJob, rcsiStepStart);
        var activeRcsiStepLines = ReadActiveYamlLines(rcsiStep);
        var expectedRcsiStepLines = new[]
        {
            "- name: Run exact RCSI relational acceptance verifier",
            "run: |",
            "python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py",
            "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py --rcsi",
        };
        if (broadStepStart < 0
            || rcsiStepStart < 0
            || !activeBroadStepLines.SequenceEqual(
                expectedBroadStepLines,
                StringComparer.Ordinal)
            || !activeRcsiStepLines.SequenceEqual(
                expectedRcsiStepLines,
                StringComparer.Ordinal))
        {
            errors.Add("RCSI relational CI verifier step must remain exact and non-vacuous.");
        }

        var expectedSqlEnvironmentPrefix =
            "\n    env:\n      GOYMEN_ERP_TEST_SQLSERVER_CONNECTION: >-\n        Server=";
        var sqlEnvironmentStart = relationalJob.IndexOf(
            expectedSqlEnvironmentPrefix,
            StringComparison.Ordinal);
        var sqlEnvironmentLine = sqlEnvironmentStart < 0
            ? string.Empty
            : relationalJob[(sqlEnvironmentStart + expectedSqlEnvironmentPrefix.Length -
                "Server=".Length)..].Split('\n')[0].Trim();
        if (sqlEnvironmentStart < 0
            || !sqlEnvironmentLine.StartsWith("Server=", StringComparison.Ordinal)
            || !sqlEnvironmentLine.Contains("Database=master", StringComparison.Ordinal)
            || !sqlEnvironmentLine.Contains("Password=", StringComparison.Ordinal)
            || !activeRelationalJobLines.Contains("services:", StringComparer.Ordinal)
            || !activeRelationalJobLines.Contains("sqlserver:", StringComparer.Ordinal)
            || ContainsConditionalOrSoftFailure(
                ReadActiveYamlLines(relationalJob.Split("\n    steps:\n")[0])))
        {
            errors.Add(
                "RCSI relational CI job must retain a non-empty SQL Server fixture binding.");
        }

        return errors;
    }

    private static string ExtractYamlStep(string jobSource, int stepStart)
    {
        if (stepStart < 0)
        {
            return string.Empty;
        }

        var step = jobSource[stepStart..];
        var nextStep = step.IndexOf("\n      - name:", 2, StringComparison.Ordinal);
        return nextStep > 0 ? step[..nextStep] : step;
    }

    private static string[] ReadActiveYamlLines(string source) =>
        source.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

    private static bool ContainsConditionalOrSoftFailure(IEnumerable<string> lines) =>
        lines.Any(line => line.StartsWith("if:", StringComparison.Ordinal)
                          || line.StartsWith("continue-on-error:", StringComparison.Ordinal));

    private static BackendSourceInventory
        ReadSqlServerRcsiToolSourceInventory(string repositoryRoot)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var errors = new List<string>();
        var toolRoot = Path.Combine(
            repositoryRoot,
            ReviewedSqlServerRcsiToolRootPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        if (!Directory.Exists(toolRoot))
        {
            return new BackendSourceInventory(sources, [], ["RCSI tool root is missing."]);
        }

        foreach (var path in Directory.EnumerateFiles(
                     toolRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            AddPhysicalSource(repositoryRoot, path, sources, errors);
        }

        ValidateNoUnsupportedBackendInputs(repositoryRoot, toolRoot, errors);
        var projectPath = Path.Combine(
            repositoryRoot,
            ReviewedSqlServerRcsiToolProjectPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        var automaticDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath, LoadOptions.SetLineInfo);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Xml.XmlException)
        {
            errors.Add($"Could not parse RCSI tool project: {exception.Message}");
            return new BackendSourceInventory(sources, [], errors);
        }

        CollectAutomaticBuildDefinitionPaths(
            repositoryRoot,
            projectPath,
            automaticDefinitions,
            errors);
        var analysis = AnalyzeReviewedSqlServerRcsiToolProjectDefinition(document);
        errors.AddRange(analysis.Errors);
        AddExplicitCompileIncludes(
            repositoryRoot,
            projectPath,
            analysis.CompileDirectives,
            sources,
            errors);
        ValidateAutomaticBuildDefinitions(repositoryRoot, automaticDefinitions, errors);
        ValidateGeneratedViewFiles(toolRoot, repositoryRoot, errors);

        foreach (var requiredSource in new[]
                 {
                     ReviewedSqlServerRcsiToolProgramPath,
                     ReviewedSqlServerRcsiToolRootPath + "/AssemblyInfo.cs",
                 })
        {
            if (!sources.ContainsKey(requiredSource))
            {
                errors.Add($"Required RCSI tool compile source was not scanned: {requiredSource}");
            }
        }

        return new BackendSourceInventory(
            sources,
            [ReviewedSqlServerRcsiToolProjectPath],
            errors);
    }

    private static IReadOnlyList<string> ValidateSqlServerRcsiToolMutationSurface(
        IReadOnlyDictionary<string, string> toolSources)
    {
        var errors = new List<string>();
        foreach (var (path, source) in toolSources)
        {
            if (Regex.IsMatch(
                    source,
                    @"(?<![\p{L}\p{Nd}_])ALTER\s+DATABASE(?![\p{L}\p{Nd}_])",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            {
                errors.Add(
                    "SQL Server RCSI tool must not contain an ALTER DATABASE mutation "
                    + $"surface: {path}");
            }
        }

        return errors;
    }

    private static ProjectDefinitionAnalysis
        AnalyzeReviewedSqlServerRcsiToolProjectDefinition(XDocument document)
    {
        var reviewedDocument = new XDocument(document);
        var exactOutputType = reviewedDocument.Descendants()
            .Where(element => element.Name.LocalName == "OutputType")
            .Where(element => !element.HasAttributes
                              && !element.Elements().Any()
                              && string.Equals(
                                  element.Value.Trim(),
                                  "Exe",
                                  StringComparison.Ordinal))
            .ToArray();
        if (exactOutputType.Length == 1)
        {
            exactOutputType[0].Remove();
        }

        var exactRuntimeIdentifiers = reviewedDocument.Descendants()
            .Where(element => element.Name.LocalName == "RuntimeIdentifiers")
            .Where(element => !element.HasAttributes
                              && !element.Elements().Any()
                              && string.Equals(
                                  element.Value.Trim(),
                                  "$(NETCoreSdkRuntimeIdentifier)",
                                  StringComparison.Ordinal))
            .ToArray();
        if (exactRuntimeIdentifiers.Length == 1)
        {
            exactRuntimeIdentifiers[0].Remove();
        }

        var exactSqlClientPackage = reviewedDocument.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Where(element => element.Attributes().Count() == 2
                              && !element.Elements().Any()
                              && string.Equals(
                                  element.Attribute("Include")?.Value,
                                  "Microsoft.Data.SqlClient",
                                  StringComparison.Ordinal)
                              && string.Equals(
                                  element.Attribute("Version")?.Value,
                                  "6.1.1",
                                  StringComparison.Ordinal))
            .ToArray();
        if (exactSqlClientPackage.Length == 1)
        {
            exactSqlClientPackage[0].Remove();
        }

        var analysis = AnalyzeProjectDefinition(
            reviewedDocument,
            ReviewedSqlServerRcsiToolProjectPath);
        var errors = analysis.Errors.ToList();
        foreach (var directive in analysis.CompileDirectives)
        {
            if (directive.Kind is CompileDirectiveKind.Remove or CompileDirectiveKind.Update)
            {
                errors.Add(
                    $"RCSI tool Compile {directive.Kind} is forbidden because the exact "
                    + "compile inventory must remain additive and reviewable.");
            }

            if (directive.Excludes.Count != 0)
            {
                errors.Add(
                    "RCSI tool Compile Exclude is forbidden because it can hide reviewed "
                    + "compile inputs.");
            }
        }

        return new ProjectDefinitionAnalysis(analysis.CompileDirectives, errors);
    }

    private static IReadOnlyList<DirectDatabaseBypassOccurrence>
        FindDirectDatabaseBypassOccurrences(IReadOnlyDictionary<string, string> sources)
    {
        var occurrences = new List<DirectDatabaseBypassOccurrence>();
        foreach (var (path, source) in sources)
        {
            var normalizedPath = NormalizeRepositoryPath(path);
            var searchableSource = NormalizeExecutableIdentifiers(
                MaskCommentsAndNonInterpolatedLiterals(source));
            foreach (Match match in DirectDatabaseBypassCapabilityRegex().Matches(
                         searchableSource))
            {
                occurrences.Add(new DirectDatabaseBypassOccurrence(
                    normalizedPath,
                    match.Value,
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

            // The existing company-context construction prohibition is unchanged.
            // Only these exact, fingerprinted global design-time factories gain a
            // construction counter; this is not a runtime construction exemption.
            if (ReviewedPlatformContextFactories.TryGetValue(normalizedPath, out var globalFactory)
                && globalFactory.ContextTypeName is "GroupSharedCommandDbContext" or "ProductDbContext")
            {
                foreach (Match construction in Regex.Matches(searchableSource,
                             @"\bnew\s+" + Regex.Escape(globalFactory.ContextTypeName) + @"\s*\(",
                             RegexOptions.CultureInvariant))
                    constructions.Add(new ReviewedContextConstruction(normalizedPath, globalFactory.ContextTypeName,
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

    [Theory]
    [InlineData("src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/GroupSharedCommandDesignTimeDbContextFactory.cs", "GroupSharedCommandDbContext")]
    [InlineData("src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/ProductDesignTimeDbContextFactory.cs", "ProductDbContext")]
    public void Global_design_time_factories_require_exact_constructors_and_do_not_authorize_other_paths(string path, string contextType)
    {
        var sources = ReadBackendSources(FindRepositoryRoot());
        var source = sources[path];
        var constructor = "new " + contextType + "(";
        Assert.Equal(1, CountAuditOccurrences(source, constructor));
        sources[path] = source.Replace(constructor, "new UnapprovedContext(", StringComparison.Ordinal);
        Assert.Contains(ValidateReviewedPlatformContextConstructions(sources),
            error => error.Contains("Expected exactly one '" + contextType + "' construction", StringComparison.Ordinal));
        sources[path] = source;
        sources["src/backend/GoymenERP.Api/Unapproved" + contextType + "DesignTimeDbContextFactory.cs"] = source;
        Assert.Contains(ValidateReviewedPlatformContextConstructions(sources),
            error => error.Contains("Unauthorized design-time DbContext factory reference", StringComparison.Ordinal));
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

    private static IReadOnlyList<string> ValidateOpenApiBuildTimeNetworkIsolation(
        IReadOnlyDictionary<string, string> sources)
    {
        var errors = new List<string>();
        if (!sources.TryGetValue(OpenApiStartupPath, out var programSource))
        {
            errors.Add($"OpenAPI startup source is missing: {OpenApiStartupPath}");
            return errors;
        }

        var identityPath = ReviewedRuntimeDatabaseBypassSources.Keys.Single(path =>
            path.EndsWith("IdentityModuleExtensions.cs", StringComparison.Ordinal));
        if (!sources.TryGetValue(identityPath, out var identitySource))
        {
            errors.Add($"OpenAPI hosted-service source is missing: {identityPath}");
            return errors;
        }

        var startupFingerprint = ComputeReviewedSourceFingerprint(programSource);
        if (!string.Equals(
                startupFingerprint,
                ReviewedOpenApiStartupFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                "OpenAPI startup fingerprint does not match the reviewed network-isolated "
                + $"composition. Expected {ReviewedOpenApiStartupFingerprint}, found "
                + $"{startupFingerprint}: {OpenApiStartupPath}");
        }

        var reviewedIdentity = ReviewedRuntimeDatabaseBypassSources[identityPath];
        var identityFingerprint = ComputeReviewedSourceFingerprint(identitySource);
        if (!string.Equals(
                identityFingerprint,
                reviewedIdentity.ReviewedFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                "OpenAPI hosted-service gate fingerprint does not match the reviewed "
                + $"composition. Expected {reviewedIdentity.ReviewedFingerprint}, found "
                + $"{identityFingerprint}: {identityPath}");
        }

        if (!identitySource.Contains(
                "if (!OpenApiDocumentGenerationMode.IsActive)",
                StringComparison.Ordinal))
        {
            errors.Add(
                "OpenAPI hosted-service gate must remain fail-closed under the exact "
                + "GetDocument.Insider entry assembly signal.");
        }

        var hostedServiceRegistrations = sources
            .Where(source => source.Value.Contains("AddHostedService<", StringComparison.Ordinal))
            .Select(source => source.Key)
            .ToArray();
        if (hostedServiceRegistrations.Length != 1
            || !string.Equals(
                hostedServiceRegistrations[0],
                identityPath,
                StringComparison.Ordinal))
        {
            errors.Add(
                "OpenAPI build-time composition must have exactly one reviewed custom "
                + "hosted-service registration, guarded in IdentityModuleExtensions: "
                + string.Join(", ", hostedServiceRegistrations));
        }

        const string initializerGuard =
            "if (!isOpenApiDocumentGeneration\n"
            + "    && (app.Environment.IsDevelopment() || app.Environment.IsEnvironment(\"Testing\")))";
        var normalizedProgram = programSource.Replace("\r\n", "\n", StringComparison.Ordinal);
        var guardIndex = normalizedProgram.IndexOf(initializerGuard, StringComparison.Ordinal);
        var runIndex = normalizedProgram.IndexOf("await app.RunAsync();", StringComparison.Ordinal);
        if (guardIndex < 0 || runIndex <= guardIndex)
        {
            errors.Add(
                "OpenAPI module-initializer gate must precede application RunAsync under "
                + "the exact entry-assembly signal.");
        }

        foreach (var initializer in new[]
                 {
                     "InitializeOrganizationModuleAsync",
                     "InitializeIdentityModuleAsync",
                     "InitializeAuditModuleAsync",
                     "InitializeIdempotencyModuleAsync",
                     "InitializeCounterpartyModuleAsync",
                 })
        {
            var occurrences = Regex.Matches(
                normalizedProgram,
                Regex.Escape(initializer),
                RegexOptions.CultureInvariant);
            if (occurrences.Count != 1
                || occurrences[0].Index <= guardIndex
                || occurrences[0].Index >= runIndex)
            {
                errors.Add(
                    $"OpenAPI build-time initializer {initializer} must occur exactly once "
                    + "inside the reviewed guarded startup block.");
            }
        }

        if (sources.Any(source => source.Value.Contains("MapOpenApi", StringComparison.Ordinal)))
        {
            errors.Add("Runtime MapOpenApi exposure is forbidden in the reviewed backend surface.");
        }

        return errors;
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
            if (source[index] is '\r' or '\n')
            {
                // A regular C# string cannot cross a physical line. Stopping here also
                // prevents malformed text in a disabled preprocessor branch from masking
                // executable source that follows the branch.
                return index;
            }

            if (source[index] == '\\')
            {
                if (index + 1 < source.Length
                    && (source[index + 1] is '\r' or '\n'))
                {
                    return index + 1;
                }

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
        @"^[^\S\r\n]*#[^\S\r\n]*(?:if|elif|else|endif|define|undef)"
        + @"(?![\p{L}\p{Nd}_])",
        RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex ConditionalCompilationDirectiveRegex();

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
        + @"ExecuteNonQuery(?:Async)?|"
        + @"EnsureCreated(?:Async)?|EnsureDeleted(?:Async)?|Migrate(?:Async)?|"
        + @"GetDbConnection|CreateDbCommand|CreateCommand|"
        + @"DbConnection|IDbConnection|DbCommand|IDbCommand|DbDataReader|"
        + @"DbDataAdapter|IDbDataAdapter|DbProviderFactory|DbProviderFactories|"
        + @"DbDataSource|DbBatch|DbBatchCommand|"
        + @"SqlConnection|SqlCommand|SqlDataReader|SqlDataAdapter|"
        + @"SqlDataSource|SqlBatch|SqlBatchCommand|SqlBulkCopy|"
        + @"IRelationalConnection|RelationalConnection|"
        + @"IDatabaseCreator|IRelationalDatabaseCreator|RelationalDatabaseCreator|"
        + @"IMigrator|Migrator|"
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

    private sealed record ReviewedRuntimeDatabaseBypassSource(
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

    private sealed record DirectDatabaseBypassOccurrence(
        string RepositoryPath,
        string ApiName,
        int LineNumber,
        PlatformSourceCategory SourceCategory);

    private sealed record CompositionRootEdge(string Source, string Target);

    private sealed record CounterpartyCompositionRootContract(
        IReadOnlyCollection<CompositionRootEdge> Edges);

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
