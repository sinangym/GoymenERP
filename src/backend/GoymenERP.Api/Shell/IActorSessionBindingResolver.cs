namespace GoymenERP.Api.Shell;

public interface IActorSessionBindingResolver
{
    Task<string?> ResolveAsync(CancellationToken cancellationToken = default);
}
