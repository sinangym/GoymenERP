using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.Startup;

internal enum DatabaseStartupMode
{
    Initialize,
    ValidateOnly,
}

internal static class DatabaseStartupModeResolver
{
    internal const string ConfigurationKey = "DatabaseStartup:Mode";

    internal static DatabaseStartupMode Resolve(
        IConfiguration configuration,
        string environmentName)
    {
        var configured = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DatabaseStartupMode.Initialize;
        }

        if (!Enum.TryParse<DatabaseStartupMode>(configured, ignoreCase: true, out var mode)
            || !Enum.IsDefined(mode))
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} yalnız Initialize veya ValidateOnly olabilir.");
        }

        if (mode == DatabaseStartupMode.ValidateOnly
            && !string.Equals(
                environmentName,
                Environments.Development,
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                environmentName,
                "Testing",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "DatabaseStartup:Mode=ValidateOnly yalnız Development ve Testing "
                + "ortamlarında kullanılabilir.");
        }

        return mode;
    }
}
