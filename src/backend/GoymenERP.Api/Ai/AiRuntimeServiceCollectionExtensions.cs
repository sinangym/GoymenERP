using GoymenERP.Api.Ai.External;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security.SecretStore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai;

public static class AiRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the AI-3 READ runtime. No owner module tool is registered here: owner READ tools are added by their
    /// owning modules (AI-6) and still need a configuration allowlist entry before the catalog admits them.
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
