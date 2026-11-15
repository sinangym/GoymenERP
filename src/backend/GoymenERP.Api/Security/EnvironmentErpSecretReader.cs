using GoymenERP.BuildingBlocks.Security.SecretStore;

namespace GoymenERP.Api.Security;

/// <summary>
/// Reads secrets from the process environment only (the deployment injects them from its secret store). It never
/// reads <c>appsettings*.json</c>: configuration carries only the secret's reference name, so no secret value can be
/// committed with it (AI_KARAR_PAKETI_2026-11 A and E8). Which secrets exist, who owns them and how they rotate is a
/// human gate; with no value set every consumer stays closed.
/// </summary>
public sealed class EnvironmentErpSecretReader : IErpSecretReaderV1
{
    public ErpSecretV1? TryRead(string secretName)
    {
        if (!ErpSecretNamesV1.IsValid(secretName))
        {
            return null;
        }

        var value = Environment.GetEnvironmentVariable(secretName);
        return string.IsNullOrWhiteSpace(value) ? null : new ErpSecretV1(secretName, value);
    }
}
