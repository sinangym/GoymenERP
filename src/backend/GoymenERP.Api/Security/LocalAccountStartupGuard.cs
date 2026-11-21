using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

namespace GoymenERP.Api.Security;

/// <summary>
/// Startup checks for the local account surface that run in every authentication mode (spec "Varsayılan kimlik
/// bilgisi yasağı" item 4). The development weak-password exception may come only from the local user-secrets file;
/// the key in any other configuration source, or <c>true</c> outside Development, stops the host.
/// </summary>
internal static class LocalAccountStartupGuard
{
    public const string DevelopmentAllowWeakPasswordKey = "Identity:LocalAccount:DevelopmentAllowWeakPassword";

    private const string UserSecretsFileName = "secrets.json";
    private const string UserSecretsDirectoryName = "usersecrets";

    public static void EnsureDevelopmentWeakPasswordSource(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var providers = configuration is IConfigurationRoot root ? root.Providers : [];
        string? value = null;
        foreach (var provider in providers)
        {
            if (!provider.TryGet(DevelopmentAllowWeakPasswordKey, out var providedValue))
            {
                continue;
            }

            if (!IsUserSecretsProvider(provider))
            {
                throw new InvalidOperationException(
                    $"{DevelopmentAllowWeakPasswordKey} yalnız yerel kullanıcı-gizli dosyasında (user-secrets) tanımlanabilir; başka bir yapılandırma kaynağında bulunamaz.");
            }

            value = providedValue;
        }

        if (value is null && configuration[DevelopmentAllowWeakPasswordKey] is not null)
        {
            // A configuration whose sources cannot be inspected cannot prove where the key came from.
            throw new InvalidOperationException(
                $"{DevelopmentAllowWeakPasswordKey} kaynağı doğrulanamadı; ayar kullanılamaz.");
        }

        if (value is null)
        {
            return;
        }

        if (!bool.TryParse(value, out var allowWeakPassword))
        {
            throw new InvalidOperationException(
                $"{DevelopmentAllowWeakPasswordKey} yalnız true veya false olabilir.");
        }

        if (allowWeakPassword && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{DevelopmentAllowWeakPasswordKey} yalnız Development ortamında true olabilir.");
        }
    }

    private static bool IsUserSecretsProvider(IConfigurationProvider provider)
    {
        if (provider is not JsonConfigurationProvider json
            || !string.Equals(json.Source.Path, UserSecretsFileName, StringComparison.Ordinal)
            || json.Source.FileProvider is not PhysicalFileProvider fileProvider)
        {
            return false;
        }

        // dotnet user-secrets keeps the file at <user profile>/.../usersecrets/<UserSecretsId>/secrets.json.
        var directory = new DirectoryInfo(Path.TrimEndingDirectorySeparator(fileProvider.Root));
        return string.Equals(directory.Parent?.Name, UserSecretsDirectoryName, StringComparison.OrdinalIgnoreCase);
    }
}
