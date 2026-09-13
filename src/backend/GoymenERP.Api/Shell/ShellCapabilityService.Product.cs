using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.Shell;

public sealed partial class ShellCapabilityService
{
    private async Task<Dictionary<GroupSharedMasterOperation, GroupSharedMasterAuthorizationSnapshot>> ReadProductAuthoritiesAsync(
        Guid actorId, Guid? originCompanyId, CancellationToken cancellationToken)
    {
        var results = new Dictionary<GroupSharedMasterOperation, GroupSharedMasterAuthorizationSnapshot>();
        if (originCompanyId is not { } origin || productAuthorizationService is null) return results;
        foreach (var operation in registry.Registrations
                     .Where(item => item.Declaration.ImplementationAvailable && item.Declaration.GlobalProductOperation.HasValue)
                     .Select(item => item.Declaration.GlobalProductOperation!.Value).Distinct().Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await productAuthorizationService.GetSnapshotAsync(GroupSharedMasterKind.Product, operation, cancellationToken);
            if (snapshot.Subject != GroupSharedMasterKind.Product || snapshot.Operation != operation)
                throw new ShellCapabilityUnavailableException();
            if (snapshot.IsGranted)
            {
                try
                {
                    if (operation == GroupSharedMasterOperation.Read)
                        _ = GroupSharedMasterReadContext.FromServerDecision(snapshot);
                    else
                        _ = GroupSharedMasterCommandContext.FromServerDecision(snapshot, operation);
                }
                catch (UnauthorizedAccessException) { throw new ShellCapabilityUnavailableException(); }
                if (snapshot.LocalActorId != actorId || snapshot.OriginCompanyId != origin)
                    throw new ShellCapabilityUnavailableException();
                results.Add(operation, snapshot);
            }
            else
            {
                results.Add(operation, GroupSharedMasterAuthorizationSnapshot.Denied(GroupSharedMasterKind.Product, operation));
            }
        }
        return results;
    }
}
