using GoymenERP.Modules.Identity.Infrastructure.Services;

namespace GoymenERP.Api.ProductRuntime;

public sealed record ProductRuntimeOptions(string ConnectionString, ProductPilotIssuerBinding? Issuer,
    byte[]? CursorSigningKey, int MaximumAttempts = 3, int CursorLifetimeSeconds = 900)
{
    public bool IsEnabled => Issuer is not null;
    public override string ToString() => nameof(ProductRuntimeOptions);
}
