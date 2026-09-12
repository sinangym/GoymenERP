using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.CariAcceptance006;

namespace GoymenERP.CariAcceptance006.Tests;

// Controlled contract tests, not a published run, SQL proof or browser receipt.
public sealed class ProductEnvironmentProfileTests
{
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
        Assert.Equal("c006-db61f4b0aac9", RunSettings.ExpectedRun);
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
