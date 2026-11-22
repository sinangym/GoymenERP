using GoymenERP.Api.Ai.External;
using GoymenERP.Api.Ai.Knowledge;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security.SecretStore;
using GoymenERP.Modules.AiKnowledge.Domain;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai;

public static class AiRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the AI-3 READ runtime and the platform-owned knowledge READ tools <c>decisions.lookup.v1</c> (Y-A1) and
    /// <c>release-knowledge.lookup.v1</c> (WP-A4). Module READ
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
        // Y-A1 / WP-A4: the platform-owned knowledge READ tools over the packaged (API content) knowledge files. Registration
        // only: the catalog admits a tool solely when Ai:AllowedTools lists it, a call still needs its permissions, and a
        // package is a source only after ai.knowledge.approve approved its exact bytes for the company (AI-D32). Without the
        // AiKnowledge module the approval reader answers unavailable, so the tools stay closed.
        services.TryAddSingleton<IAiDecisionIndexSource, PackagedAiDecisionIndexSource>();
        services.TryAddSingleton<IAiReleaseKnowledgeSource, PackagedAiReleaseKnowledgeSource>();
        services.TryAddSingleton<IAiKnowledgePackageCatalogV1, PackagedAiKnowledgeCatalog>();
        services.TryAddScoped<IAiKnowledgeApprovalReaderV1, UnconfiguredAiKnowledgeApprovalReader>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAiOwnerToolV1, DecisionLookupAiTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAiOwnerToolV1, ReleaseKnowledgeLookupAiTool>());
        services.AddScoped<AiToolCatalog>();
        services.AddScoped<AiCapabilityService>();
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
