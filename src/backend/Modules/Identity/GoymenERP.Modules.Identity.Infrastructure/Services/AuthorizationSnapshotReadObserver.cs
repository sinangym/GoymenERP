namespace GoymenERP.Modules.Identity.Infrastructure.Services;

internal interface IAuthorizationSnapshotReadObserver
{
    void ObserveDatabaseRead();
}

internal sealed class NullAuthorizationSnapshotReadObserver
    : IAuthorizationSnapshotReadObserver
{
    public void ObserveDatabaseRead()
    {
    }
}
