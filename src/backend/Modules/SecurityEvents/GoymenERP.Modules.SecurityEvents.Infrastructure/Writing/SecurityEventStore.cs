using GoymenERP.Modules.SecurityEvents.Domain.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;

internal interface ISecurityEventStore
{
    Task AppendAsync(SecurityEventRecord securityEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Standalone store: each append runs in a fresh scope with its own <see cref="SecurityEventsDbContext"/>, so it
/// uses its own connection and its own transaction, independent of the caller's unit of work.
/// </summary>
internal sealed class StandaloneSecurityEventStore(IServiceScopeFactory scopeFactory) : ISecurityEventStore
{
    public async Task AppendAsync(SecurityEventRecord securityEvent, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SecurityEventsDbContext>();
        await dbContext.AppendAsync(securityEvent, cancellationToken);
    }
}
