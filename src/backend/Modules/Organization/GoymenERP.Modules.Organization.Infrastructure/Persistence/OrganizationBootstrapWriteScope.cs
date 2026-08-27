namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// Marks the narrowly-scoped organization bootstrap operation that must write
/// records for more than one company before a request company exists.
/// </summary>
public sealed class OrganizationBootstrapWriteScope
{
    private int _activeLeaseCount;

    internal bool IsActive => Volatile.Read(ref _activeLeaseCount) > 0;

    internal IDisposable BeginBootstrapWrite()
    {
        Interlocked.Increment(ref _activeLeaseCount);
        return new BootstrapWriteLease(this);
    }

    private void EndBootstrapWrite()
    {
        if (Interlocked.Decrement(ref _activeLeaseCount) < 0)
        {
            Interlocked.Exchange(ref _activeLeaseCount, 0);
            throw new InvalidOperationException("Bootstrap yazma kapsamı dengesiz kapatıldı.");
        }
    }

    private sealed class BootstrapWriteLease(OrganizationBootstrapWriteScope owner)
        : IDisposable
    {
        private OrganizationBootstrapWriteScope? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.EndBootstrapWrite();
        }
    }
}
