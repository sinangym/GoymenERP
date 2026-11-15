using GoymenERP.BuildingBlocks.Security.SecretStore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.Security;

public static class ErpSecretStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the secret store reader and the E8 login identifier hasher. Neither holds a value by default: the
    /// hasher produces nothing until <see cref="LoginIdentifierHashOptions"/> names a key that the secret store holds
    /// (E8 key owner and rotation are a human gate).
    /// </summary>
    public static IServiceCollection AddErpSecretStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IErpSecretReaderV1, EnvironmentErpSecretReader>();
        services.AddOptions<LoginIdentifierHashOptions>().Bind(configuration.GetSection(LoginIdentifierHashOptions.SectionName));
        services.TryAddSingleton<ILoginIdentifierHashServiceV1, LoginIdentifierHashService>();
        return services;
    }
}
