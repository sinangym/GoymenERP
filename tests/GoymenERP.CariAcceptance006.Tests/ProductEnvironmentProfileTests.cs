using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.CariAcceptance006;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.CariAcceptance006.Tests;

// Controlled contract tests, not a published run, SQL proof or browser receipt.
public sealed class ProductEnvironmentProfileTests
{
    [Fact]
    public void IsolatedProviderReplacement_PreservesBothAcceptedRegistryRevisions()
    {
        var services = CanonicalProviders();
        using (var normal = services.BuildServiceProvider())
        {
            var providers = normal.GetServices<IShellCapabilityProvider>().ToArray();
            Assert.Equal(2, providers.Length);
            Assert.IsType<CounterpartyShellCapabilityProvider>(providers[0]);
            Assert.IsType<OrganizationShellCapabilityProvider>(providers[1]);
        }
        ProductAcceptanceFactory.ReplaceCanonicalProviders(services);
        using var isolated = services.BuildServiceProvider();
        Assert.IsType<ProductAcceptanceProvider>(Assert.Single(isolated.GetServices<IShellCapabilityProvider>()));
        var productRegistry = new ShellCapabilityRegistry(isolated.GetServices<IShellCapabilityProvider>(),
            isolated.GetRequiredService<ShellCapabilityAuthorityCatalog>());
        Assert.Equal(ProductAcceptanceFactory.Revision, productRegistry.Revision);
        Assert.Equal(4, productRegistry.Registrations.Count);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("transient")]
    [InlineData("instance")]
    [InlineData("factory")]
    public void UnexpectedProviderSet_IsRejectedBeforeAnyRegistrationChanges(string defect)
    {
        var services = CanonicalProviders();
        if (defect == "duplicate") services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
        else
        {
            services.RemoveAt(1);
            switch (defect)
            {
                case "missing": break;
                case "foreign": services.AddSingleton<IShellCapabilityProvider, ProductAcceptanceProvider>(); break;
                case "transient": services.AddTransient<IShellCapabilityProvider, OrganizationShellCapabilityProvider>(); break;
                case "instance": services.AddSingleton<IShellCapabilityProvider>(new OrganizationShellCapabilityProvider()); break;
                case "factory": services.AddSingleton<IShellCapabilityProvider>(_ => new OrganizationShellCapabilityProvider()); break;
                default: throw new ArgumentOutOfRangeException(nameof(defect));
            }
        }
        var before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => ProductAcceptanceFactory.ReplaceCanonicalProviders(services));
        Assert.Equal(before, services.ToArray());
    }

    private static IServiceCollection CanonicalProviders()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
        return services;
    }

    [Fact]
    public void ExistingPythonValidatorPipe_ReceivesUtf8JsonWithoutABom()
    {
        // Exercise only the local byte transport used by the existing build
        // validator. No run files, SQL, HTTP, publication or source claim.
        var evidence = typeof(ProductEnvironment).Assembly.GetType("GoymenERP.CariAcceptance006.ProductEnvironmentEvidence")!;
        var run = evidence.GetMethod("RunWithInput", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var output = (string)run.Invoke(null, ["{\"value\":\"\u0131\"}", "python", new[]
        { "-I", "-S", "-B", "-X", "utf8", "-c", "import sys,json; assert json.load(sys.stdin)['value'] == chr(305); print('passed')" }])!;
        Assert.Equal("passed", output.Trim());
    }

    [Fact]
    public void DeniedSmoke_UsesTheActualStrictCreateDtoGroupId()
    {
        var request = ProductEnvironment.DeniedCreateRequest();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonSerializerOptions.Web);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(ProductEnvironmentSettings.CodeGroupId, json.RootElement.GetProperty("groupId").GetGuid());
        Assert.False(json.RootElement.TryGetProperty("codeGroupId", out _));
        Assert.Equal(request, JsonSerializer.Deserialize<GoymenERP.Modules.Product.Application.Contracts.CreateProductDraftRequest>(bytes, JsonSerializerOptions.Web));
    }

    [Theory]
    [InlineData("contractId")]
    [InlineData("taskId")]
    [InlineData("environmentName")]
    [InlineData("runId")]
    [InlineData("instanceId")]
    [InlineData("apiBaseUrl")]
    [InlineData("actor")]
    [InlineData("unauthorizedActor")]
    [InlineData("companyId")]
    [InlineData("codeGroupId")]
    [InlineData("browserPrefix")]
    [InlineData("erpAccepted")]
    [InlineData("productionActivated")]
    [InlineData("readyForBrowser")]
    [InlineData("verifiedAtUtc")]
    [InlineData("acceptedContractSha256")]
    [InlineData("sourceBindingSha256")]
    public void EveryReadinessField_IsBoundEvenWhenTheTwoHashReferencesArePreserved(string field)
    {
        var settings = Parse(Candidate());
        var binding = Binding(settings);
        var bindingHash = SourceBinding.Hash(JsonSerializer.SerializeToUtf8Bytes(binding));
        var bytes = ProductEnvironment.CreateReadyBytes(settings.Contract, binding, settings.ContractHash, bindingHash);
        ProductEnvironment.ValidateReadyBytes(settings.Contract, binding, bytes, settings.ContractHash, bindingHash);
        var changed = JsonNode.Parse(bytes)!.AsObject(); changed[field] = "changed";
        Assert.Throws<InvalidOperationException>(() => ProductEnvironment.ValidateReadyBytes(settings.Contract, binding,
            JsonSerializer.SerializeToUtf8Bytes(changed, Evidence.Json), settings.ContractHash, bindingHash));
        changed.Remove(field);
        Assert.Throws<InvalidOperationException>(() => ProductEnvironment.ValidateReadyBytes(settings.Contract, binding,
            JsonSerializer.SerializeToUtf8Bytes(changed, Evidence.Json), settings.ContractHash, bindingHash));
    }

    [Fact]
    public void ReadinessBytes_RejectUnknownMembersWhitespaceAndChangedBindingTime()
    {
        var settings = Parse(Candidate()); var binding = Binding(settings);
        var hash = SourceBinding.Hash(JsonSerializer.SerializeToUtf8Bytes(binding));
        var bytes = ProductEnvironment.CreateReadyBytes(settings.Contract, binding, settings.ContractHash, hash);
        var changed = JsonNode.Parse(bytes)!.AsObject(); changed["extra"] = false;
        Assert.Throws<InvalidOperationException>(() => ProductEnvironment.ValidateReadyBytes(settings.Contract, binding,
            JsonSerializer.SerializeToUtf8Bytes(changed, Evidence.Json), settings.ContractHash, hash));
        Assert.Throws<InvalidOperationException>(() => ProductEnvironment.ValidateReadyBytes(settings.Contract, binding,
            [.. bytes, (byte)' '], settings.ContractHash, hash));
        var other = JsonNode.Parse(binding.GetRawText())!.AsObject(); other["verifiedAtUtc"] = "2026-09-12T21:01:00+00:00";
        Assert.Throws<InvalidOperationException>(() => ProductEnvironment.ValidateReadyBytes(settings.Contract,
            JsonSerializer.SerializeToElement(other), bytes, settings.ContractHash, hash));
    }

    private static JsonElement Binding(ProductEnvironmentSettings settings) => JsonSerializer.SerializeToElement(new
    {
        contractId = "PRODUCT-U0102-L-v1", runId = settings.RunId, instanceId = settings.InstanceId,
        acceptedContractSha256 = settings.ContractHash, source = settings.Source,
        ownership = new { controlled = true }, verifiedAtUtc = DateTimeOffset.Parse("2026-09-12T21:00:00+00:00"),
    });

    [Fact]
    public void ExactProfile_BindsOnlyProductResourcesAndSyntheticActors()
    {
        var settings = Parse(Candidate());
        Assert.Equal("product-u0102-0123456789ab", settings.RunId);
        Assert.Equal("GoymenERP_Product_U0102_0123456789ab", settings.DatabaseName);
        Assert.StartsWith(SourceBinding.ProductWorktree + "\\artifacts", settings.EvidenceRoot);
        Assert.Equal(26080, settings.LifecycleIdentity.ApiPort);
        Assert.Equal(settings.RunId, settings.LifecycleIdentity.RunId);
        Assert.Throws<InvalidOperationException>(() => settings.LifecycleIdentity.Validate("Testing"));
        Assert.Equal("c006-5b7b30940e9a", RunSettings.ExpectedRun);
        Assert.Equal(25080, new RunSettings().ApiPort);
    }

    [Theory]
    [InlineData("apiBaseUrl", "http://127.0.0.1:25080")]
    [InlineData("sqlEndpoint", "127.0.0.1,25433")]
    [InlineData("uiOrigin", "http://127.0.0.1:25173")]
    [InlineData("environmentName", "Production")]
    [InlineData("runId", "product-u0102-../outside")]
    [InlineData("instanceId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("actor", "Sinan")]
    [InlineData("unauthorizedActor", "group-bootstrap-admin")]
    [InlineData("actorId", "20000000-0000-0000-0000-000000000001")]
    [InlineData("companyId", "10000000-0000-0000-0000-000000000002")]
    [InlineData("codeGroupId", "82000000-0000-0000-0000-000000000099")]
    [InlineData("databaseName", "ExistingCustomerDatabase")]
    [InlineData("composeProject", "existing-project")]
    [InlineData("volumeName", "existing-data")]
    [InlineData("networkName", "existing-network")]
    [InlineData("acceptanceRoot", "C:\\foreign")]
    public void RedirectedOrUnapprovedProfile_IsRejected(string name, string value)
    {
        var candidate = Candidate(); candidate[name] = value;
        Assert.Throws<InvalidOperationException>(() => Parse(candidate));
    }

    [Fact]
    public void MissingExtraDuplicateFieldsAndSelfChosenHash_AreRejected()
    {
        var candidate = Candidate(); candidate.Remove("actor");
        Assert.Throws<InvalidOperationException>(() => Parse(candidate));
        candidate = Candidate(); candidate["allowProduction"] = false;
        Assert.Throws<InvalidOperationException>(() => Parse(candidate));
        candidate = Candidate(); candidate["productionActivated"] = true;
        Assert.Throws<InvalidOperationException>(() => Parse(candidate));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Candidate());
        Assert.Throws<InvalidOperationException>(() => ProductEnvironmentSettings.Parse(bytes, new string('0', 64)));
        var duplicate = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes).Replace("{", "{\"actor\":\"duplicate\",", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => ProductEnvironmentSettings.Parse(duplicate, SourceBinding.Hash(duplicate)));
    }

    [Fact]
    public void FixtureListMustIncludeTheWholeActualProfile_NotAnArbitrarySubset()
    {
        var candidate = Candidate(); candidate["source"]!["fixturePaths"] = new JsonArray("tools/GoymenERP.CariAcceptance006/SourceBinding.Product.cs");
        Assert.Throws<InvalidOperationException>(() => Parse(candidate));
    }

    [Fact]
    public void KnownOldFrontend_IsRejectedBeforeAnySourceOrRuntimeProbe()
    {
        var settings = Parse(Candidate());
        var error = Assert.Throws<InvalidOperationException>(() => settings.VerifySource());
        Assert.Contains("frontend transport correction", error.Message);
    }

    [Fact]
    public void TestOnlyProvider_HasExactGlobalOperationsAndNoCompanyPermissionAlias()
    {
        var declarations = new ProductAcceptanceProvider().GetDeclarations().ToArray();
        Assert.Equal(4, declarations.Length);
        foreach (var item in declarations) Assert.Empty(item.RequiredPermissions);
        Assert.Equal(GroupSharedMasterOperation.Read, declarations.Single(x => x.CapabilityId == "product.items.list").GlobalProductOperation);
        Assert.Equal(GroupSharedMasterOperation.Read, declarations.Single(x => x.CapabilityId == "product.items.detail").GlobalProductOperation);
        Assert.Equal(GroupSharedMasterOperation.DraftCreate, declarations.Single(x => x.CapabilityId == "product.items.create").GlobalProductOperation);
        Assert.Equal(GroupSharedMasterOperation.DraftBasicEdit, declarations.Single(x => x.CapabilityId == "product.items.edit").GlobalProductOperation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CapabilitySmoke_ConsumesActualStringArraySchema(bool authorized)
    {
        var response = JsonSerializer.SerializeToElement(new { contractVersion = ShellCapabilityRegistry.ContractVersion,
            generation = "controlled-test-generation", capabilities = authorized
                ? new[] { "product.items.create", "product.items.detail", "product.items.edit", "product.items.list" } : [] });
        ProductEnvironment.ValidateCapabilities(response, authorized);
        Assert.Throws<InvalidOperationException>(() => ProductEnvironment.ValidateCapabilities(response, !authorized));
    }

    private static ProductEnvironmentSettings Parse(JsonObject candidate)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(candidate);
        return ProductEnvironmentSettings.Parse(bytes, SourceBinding.Hash(bytes));
    }

    private static JsonObject Candidate() => JsonSerializer.SerializeToNode(new
    {
        contractId = "PRODUCT-U0102-L-v1", taskId = "U-01-U-02", environmentName = "Testing",
        runId = "product-u0102-0123456789ab", instanceId = "11111111-1111-1111-1111-111111111111",
        acceptanceRoot = ProductEnvironmentSettings.OwnerRoot + "\\product-u0102-0123456789ab",
        apiBaseUrl = "http://127.0.0.1:26080", sqlEndpoint = "127.0.0.1,26433", uiOrigin = "http://127.0.0.1:26173",
        databaseName = "GoymenERP_Product_U0102_0123456789ab", composeProject = "product-u0102-0123456789ab",
        containerName = "product-u0102-0123456789ab-sql", volumeName = "product-u0102-0123456789ab-data", networkName = "product-u0102-0123456789ab-network",
        actor = ProductEnvironmentSettings.Actor, actorId = ProductEnvironmentSettings.ActorId.ToString("D"),
        unauthorizedActor = ProductEnvironmentSettings.UnauthorizedActor, unauthorizedActorId = ProductEnvironmentSettings.UnauthorizedActorId.ToString("D"),
        companyId = ProductEnvironmentSettings.CompanyId.ToString("D"), codeGroupId = ProductEnvironmentSettings.CodeGroupId.ToString("D"),
        browserPrefix = "PRODUCT-U0102-0123456789ab", erpAccepted = false, productionActivated = false,
        source = new { worktree = SourceBinding.ProductWorktree, fixturePaths = ProductEnvironmentSettings.FixturePaths,
            frontendHead = "1c48f609b83caa1118b0b274261678b6857088fa" },
    })!.AsObject();
}
