using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.Ai.Pilot;

/// <summary>C8 admission metadata check ONLY: never calls a tool, emits its data, dispatches or activates anything.
/// Future dispatch still requires the existing runtime/owner/schema/knowledge/gateway/egress guards, and must
/// validate every actual output field against the reviewed field policy. A true answer is not an egress permit.</summary>
public sealed class AiExternalReadPilotAdmissionV1(AiToolCatalog catalog, IAiExternalReadPilotPolicyV1 policies,
    ICompanyContext company, ICurrentUser user, IActorSessionBindingResolver sessions,
    IErpAuthorityRevalidationService authority, IAiExternalReadPilotAdmissionAuditV1? audit = null)
{
    public async Task<bool> CheckAsync(AiServerContextV1 context, string toolId, string version, bool release,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context) || audit is not { IsAvailable: true }) return false;
        try
        {
            // Catalog remains the sole tool registry; a pilot entry cannot admit an unregistered/unpinned tool.
            var descriptor = catalog.Find(toolId, version)?.Descriptor;
            var policy = Snapshot(await policies.ReadApprovedAsync(context.CompanyId, context.ActorId, cancellationToken));
            if (descriptor is null || !Eligible(policy, descriptor, context)) return false;
            // Snapshot the exact approved metadata before subsequent awaits (not a second persistent ledger).
            var digest = Digest(policy!);
            if (!await Fresh()) return false;
            cancellationToken.ThrowIfCancellationRequested();
            // No fabricated AiToolCalled: this adapter has no implementation until a real admission audit owner exists.
            var recorded = await audit.RecordAdmissionAsync(new(context.CompanyId, context.ActorId, context.SessionId,
                context.CorrelationId, policy!.PolicyId, policy.Version, digest, descriptor.ToolId, descriptor.Version, release), cancellationToken);
            if (!recorded || !await Fresh()) return false;
            var current = Snapshot(await policies.ReadApprovedAsync(context.CompanyId, context.ActorId, cancellationToken));
            if (!Eligible(current, descriptor, context) || Digest(current!) != digest || !await Fresh()) return false;
            cancellationToken.ThrowIfCancellationRequested();
            return true;

            async Task<bool> Fresh()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SameContext(context) || await sessions.ResolveAsync(cancellationToken) != context.SessionId
                    || !SameContext(context)) return false;
                foreach (var permission in descriptor.RequiredPermissions.Prepend(ErpPermissions.AiPanelUse).Distinct(StringComparer.Ordinal))
                {
                    var scope = await authority.RevalidatePermissionScopeAsync(permission, context.CompanyId, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (scope is not { IsGranted: true, IsCompanyWide: true } || !SameContext(context)) return false;
                }
                var session = await sessions.ResolveAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return session == context.SessionId && SameContext(context);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException) { return false; }
    }

    private bool SameContext(AiServerContextV1 context) => context.CompanyId != Guid.Empty && company.IsResolved
        && company.CompanyId == context.CompanyId && user.IsAuthenticated && !string.IsNullOrWhiteSpace(context.ActorId)
        && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId) && !string.IsNullOrWhiteSpace(context.SessionId);

    private bool Eligible(AiExternalReadPilotPolicyV1? policy, AiToolDescriptorV1 tool, AiServerContextV1 context)
    {
        if (policy is not { Approved: true, Tools.Count: >= 3 and <= 5 } || policy.CompanyId != context.CompanyId
            || policy.ActorId != context.ActorId || string.IsNullOrWhiteSpace(policy.PolicyId) || policy.Version <= 0
            || policy.Tools.Any(x => x is null) || policy.Tools.Select(x => x.ToolId).Distinct(StringComparer.Ordinal).Count() != policy.Tools.Count
            || tool.Risk != AiToolRiskClassV1.Read || tool.OwnerCommandName is not null
            || tool.DataClass is not (AiDataClassV1.Public or AiDataClassV1.Internal)) return false;
        if (!policy.Tools.Any(x => x.ToolId == tool.ToolId && x.Version == tool.Version)) return false;
        return policy.Tools.All(entry =>
        {
            var registered = catalog.Find(entry.ToolId, entry.Version)?.Descriptor;
            return registered is { Risk: AiToolRiskClassV1.Read, OwnerCommandName: null }
                && registered.DataClass is AiDataClassV1.Public or AiDataClassV1.Internal
                && entry.NonSensitiveDataApproved && AiToolDescriptionPinV1.Matches(registered, entry.DescriptionSha256)
                && entry.FieldPolicyRevision == registered.Contract.FieldPolicy && entry.OutputFields is { Count: > 0 }
                && entry.OutputFields.All(field => field is not null && !field.NeverEgress
                    && field.SensitiveType == AiSensitiveDataTypeV1.None
                    && field.DataClass is AiDataClassV1.Public or AiDataClassV1.Internal)
                && entry.OutputFields.Select(x => x.FieldName).Distinct(StringComparer.Ordinal).Count() == entry.OutputFields.Count;
        });
    }

    private static AiExternalReadPilotPolicyV1? Snapshot(AiExternalReadPilotPolicyV1? policy) => policy is null ? null
        : policy with { Tools = policy.Tools.Select(entry => entry with { OutputFields = entry.OutputFields.ToArray() }).ToArray() };

    private static string Digest(AiExternalReadPilotPolicyV1 policy) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(policy)));
}
