using GoymenERP.Api.Ai.External;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.Api.Ai.Knowledge;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security.SecretStore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai;

public static class AiRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the AI-3 READ runtime and the platform-owned <c>decisions.lookup.v1</c> READ tool (Y-A1). Module READ
    /// tools are added by their owning modules (e.g. Logistics <c>logistics.pending-shipments.v1</c>); every tool still
    /// needs a configuration allowlist entry before the catalog admits it.
    /// </summary>
    public static IServiceCollection AddAiRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiRuntimeOptions>().Bind(configuration.GetSection(AiRuntimeOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<FakeAiModelProvider>();

        // External provider (Issue #66 NS-06): adapter code only. No provider is selected, no key is configured and
        // the registered transport has no egress, so the external provider stays closed (OPEN-020, AI-D5..AI-D7).
        services.AddOptions<AiExternalOptions>().Bind(configuration.GetSection(AiExternalOptions.SectionName));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAiExternalProtocolAdapterV1, OpenAiChatCompletionsProtocolAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAiExternalProtocolAdapterV1, AnthropicMessagesProtocolAdapter>());
        services.TryAddSingleton<IAiExternalTransportV1, NoEgressAiExternalTransport>();
        services.TryAddSingleton<IErpSecretReaderV1, EnvironmentErpSecretReader>();
        services.TryAddScoped<IAiExternalEgressPolicySourceV1, UnconfiguredAiExternalEgressPolicySource>();
        services.TryAddScoped<ExternalAiModelProvider>();
        services.TryAddScoped<IAiModelProviderV1>(ResolveModelProvider);
        // Y-A1: the platform-owned decision index READ tool. Registration only: the catalog admits it solely when
        // Ai:AllowedTools lists decisions.lookup.v1@1, a call still needs ai.panel.use + ai.decisions.read, and with no
        // Ai:Knowledge:DecisionIndexPath configured the tool answers unavailable.
        services.TryAddSingleton<IAiDecisionIndexSource, ConfiguredAiDecisionIndexSource>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAiOwnerToolV1, DecisionLookupAiTool>());
        services.AddScoped<AiToolCatalog>();
        services.AddScoped<AiCapabilityService>();
        // WP-A1 gateway: the Settings-owned policy port and usage ledger come from the Settings module. Only the in-process
        // Fake composers are registered (one per placement); the runtime never admits the Fake kind in Production, and no
        // external or local composer exists until the provider and hardware decisions (OPEN-020, OPEN-014).
        services.AddSingleton<IAiAnswerComposerV1>(_ => new FakeAiAnswerComposer(AiModelPlacementV1.Cloud));
        services.AddSingleton<IAiAnswerComposerV1>(_ => new FakeAiAnswerComposer(AiModelPlacementV1.OnPremises));
        services.AddScoped<IAiGatewayV1, AiGateway>();
        services.AddScoped<AiAskService>();
        return services;
    }

    private static IAiModelProviderV1 ResolveModelProvider(IServiceProvider services) =>
        string.Equals(
            services.GetRequiredService<IOptions<AiRuntimeOptions>>().Value.Provider,
            AiProviderKindsV1.External,
            StringComparison.Ordinal)
            ? services.GetRequiredService<ExternalAiModelProvider>()
            : services.GetRequiredService<FakeAiModelProvider>();
}
