using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.Api.Ai.Tools;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiAskServiceTests
{
    [Fact]
    public async Task B4_runtime_registration_without_Sales_keeps_existing_platform_tools_resolvable_and_the_margin_tool_closed()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:Enabled"] = "false" }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment("Development"));
        services.AddScoped<IErpAuthorityRevalidationService>(_ => new FakeAuthority());
        services.AddAiRuntime(configuration);
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var registered = scope.ServiceProvider.GetServices<IAiOwnerToolV1>().ToArray();
        var tool = Assert.IsType<SalesLineMarginExplanationAiToolV1>(Assert.Single(registered,
            x => x.Descriptor.ToolId == SalesLineMarginExplanationAiToolV1.ToolId));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AiToolCatalog>().Descriptors);
        var input = new AiSalesLineMarginExplanationToolTests.Fixture();
        var answer = await tool.ReadAsync(new(input.Arguments), input.Context);
        Assert.Equal(AiToolOutcomeV1.Unavailable, answer.Outcome); Assert.Null(answer.ContentJson); Assert.Null(answer.Source);
    }

    [Fact]
    public async Task B4_margin_trace_uses_one_existing_owner_read_and_the_same_local_grounded_gateway()
    {
        var rig = new MarginRig();
        var result = await rig.Service().AskAsync(rig.Request());
        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        var answer = Assert.IsType<AiAnswerV1>(result.Response!.Answer);
        Assert.Equal("Marj 20.00 ve oran 20.00 [1]", answer.Text);
        Assert.Equal(InMemoryAiGatewayStore.LocalDeployment, answer.ModelDeployment);
        var citation = Assert.Single(answer.Citations);
        Assert.Equal((SalesLineMarginExplanationAiToolV1.ToolId, rig.Owner.Draft), (citation.ToolId, citation.TargetId));
        Assert.Equal(1, rig.Owner.Owner.Calls);
        Assert.Equal(1, rig.Common.Provider.Calls);
        Assert.Equal(AiTaskKindV1.Answer, Assert.Single(rig.Composer.Requests).TaskKind);
        var source = Assert.Single(rig.Composer.Requests[0].Sources);
        Assert.Contains("20.00", source.ContentJson);
        Assert.DoesNotContain("SENTETIK-PRIVATE", source.ContentJson);
        rig.Common.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task B4_margin_text_with_a_model_invented_number_is_not_released_as_a_supported_answer()
    {
        var rig = new MarginRig(); rig.Composer.Text = "Marj 99999999.123 [1]";
        var result = await rig.Service().AskAsync(rig.Request());
        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        Assert.Equal(AiAskOutcomesV1.CannotAnswer, result.Response!.Outcome);
        Assert.Null(result.Response.Answer);
        Assert.Single(result.Response.ToolResults);
        Assert.Equal(1, rig.Owner.Owner.Calls);
    }

    [Fact]
    public async Task B4_unknown_measure_is_invalid_before_the_existing_owner_read()
    {
        var rig = new MarginRig();
        var request = rig.Request();
        using var arguments = JsonDocument.Parse(rig.Owner.Arguments.Insert(1, "\"measure\":\"profit-forecast\","));
        var result = await rig.Service().AskAsync(request with { Arguments = arguments.RootElement.Clone() });
        Assert.Equal(AiAskStatusV1.Invalid, result.Status);
        Assert.Equal(0, rig.Owner.Owner.Calls);
        Assert.Empty(rig.Composer.Requests);
    }

    [Fact]
    public async Task B4_registration_without_a_valid_pin_is_refused_without_an_owner_read_or_model_composition()
    {
        var rig = new MarginRig(); rig.Options.AllowedTools[0].DescriptionSha256 = null;
        var result = await rig.Service().AskAsync(rig.Request());
        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        Assert.Equal(0, rig.Owner.Owner.Calls);
        Assert.Empty(rig.Composer.Requests);
        Assert.Empty(rig.Common.GatewayStore.Reservations);
    }

    [Fact]
    public async Task B4_price_disclosure_revoked_during_composition_suppresses_the_trace_and_text_at_response_release()
    {
        var rig = new MarginRig();
        rig.Composer.After = () => rig.Authority.Revoke(SalesPermissionManifestV1.OrdersPriceRead);
        var result = await rig.Service().AskAsync(rig.Request());
        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        Assert.Null(result.Response);
        Assert.Equal(1, rig.Owner.Owner.Calls);
        Assert.Single(rig.Composer.Requests);
    }

    private sealed class MarginRig
    {
        public Harness Common { get; } = new();
        public AiSalesLineMarginExplanationToolTests.Fixture Owner { get; } = new();
        public FakeAuthority Authority { get; } = new(ErpPermissions.AiPanelUse, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersPriceRead, SalesPermissionManifestV1.OrdersMarginRead);
        public MarginComposer Composer { get; } = new();
        public AiRuntimeOptions Options { get; } = Harness.ReadyOptions();
        private readonly SalesLineMarginExplanationAiToolV1 tool;
        public MarginRig()
        {
            tool = new(Owner.Service, Common.CompanyScope, new TestUser(Actor, Company), Authority, TimeProvider.System);
            Options.MaxDataClass = AiDataClassV1.Confidential;
            // Synthetic reviewed card only, like the existing ask harness. No shipped allowlist/pin or flag is changed.
            Options.AllowedTools = [new() { ToolId = SalesLineMarginExplanationAiToolV1.ToolId, Version = "1",
                DescriptionSha256 = AiToolDescriptionPinV1.Compute(tool.Descriptor) }];
        }
        public AiAskRequestV1 Request()
        {
            using var arguments = JsonDocument.Parse(Owner.Arguments);
            return new("Bu satırın marjını hesap iziyle açıkla", SalesLineMarginExplanationAiToolV1.ToolId, "1", arguments.RootElement.Clone());
        }
        public AiAskService Service() => new(Microsoft.Extensions.Options.Options.Create(Options), new TestHostEnvironment("Development"),
            Common.Provider, new AiToolCatalog([tool], Microsoft.Extensions.Options.Options.Create(Options)),
            new TestUser(Actor, Company), Common.CompanyScope, Authority, new FixedSessionResolver(), Common.Correlation,
            Common.Writer, TimeProvider.System, AiGatewayTestDoubles.Gateway(Options, Common.GatewayStore, [Composer]));
    }

    /// <summary>Only an injected untrusted-composition fixture; no new production model/provider.</summary>
    private sealed class MarginComposer : IAiAnswerComposerV1
    {
        public string ProviderKind => AiProviderKindsV1.Fake;
        public AiModelPlacementV1 Placement => AiModelPlacementV1.OnPremises;
        public string Text { get; set; } = "Marj 20.00 ve oran 20.00 [1]";
        public List<AiComposeRequestV1> Requests { get; } = [];
        public Action? After { get; set; }
        public Task<AiComposedTextV1> ComposeAsync(AiComposeRequestV1 request, CancellationToken cancellationToken = default)
        { Requests.Add(request); After?.Invoke(); return Task.FromResult(new AiComposedTextV1(Text, [1], 1)); }
    }
}
