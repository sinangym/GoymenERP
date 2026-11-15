using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.TryAddSingleton<IAiModelProviderV1, FakeAiModelProvider>();
        services.AddScoped<AiToolCatalog>();
        services.AddScoped<AiCapabilityService>();
        services.AddScoped<AiAskService>();
        return services;
    }
}
