using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Api.Shell;

public sealed class ShellCapabilityService(
    ShellCapabilityRegistry registry,
    IActorSessionBindingResolver sessionBindingResolver,
    ShellCapabilityGenerationProtector protector,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorizationService authorizationService)
{
    public async Task<ShellCapabilitySnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty
            || !Guid.TryParse(currentUser.UserId, out var userId)
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new ShellCapabilityUnavailableException();
        }

        var sessionBinding = await sessionBindingResolver.ResolveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(sessionBinding))
        {
            throw new ShellCapabilityUnavailableException();
        }

        var scopes = new Dictionary<string, PermissionScope>(StringComparer.Ordinal);
        foreach (var permission in ErpPermissions.All.OrderBy(x => x, StringComparer.Ordinal))
        {
            scopes[permission] = await authorizationService.GetPermissionScopeAsync(
                permission,
                companyContext.CompanyId,
                cancellationToken);
        }

        var capabilities = registry.Registrations
            .Where(registration => registration.Declaration.ImplementationAvailable)
            .Where(registration => IsAllowed(registration.Declaration, scopes))
            .Select(registration => registration.Declaration.CapabilityId)
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToArray();

        var generationComponents = new List<string>
        {
            ShellCapabilityRegistry.ContractVersion,
            registry.Revision,
            userId.ToString("D", CultureInfo.InvariantCulture),
            sessionBinding,
            companyContext.CompanyId.ToString("D", CultureInfo.InvariantCulture),
        };
        generationComponents.AddRange(currentUser.AllowedCompanyIds
            .OrderBy(id => id)
            .Select(id => $"company:{id:D}"));
        foreach (var (permission, scope) in scopes.OrderBy(
                     entry => entry.Key,
                     StringComparer.Ordinal))
        {
            generationComponents.Add($"permission:{permission}");
            generationComponents.Add(scope.IsGranted ? "granted:1" : "granted:0");
            generationComponents.Add(scope.IsCompanyWide ? "company-wide:1" : "company-wide:0");
            generationComponents.AddRange(scope.RoleCodes
                .OrderBy(x => x, StringComparer.Ordinal)
                .Select(role => $"role:{role}"));
            generationComponents.AddRange(scope.SiteIds
                .OrderBy(x => x)
                .Select(id => $"site:{id:D}"));
            generationComponents.AddRange(scope.LocationIds
                .OrderBy(x => x)
                .Select(id => $"location:{id:D}"));
            generationComponents.AddRange(scope.WorkCenterIds
                .OrderBy(x => x)
                .Select(id => $"work-center:{id:D}"));
        }

        generationComponents.AddRange(capabilities.Select(x => $"capability:{x}"));
        return new ShellCapabilitySnapshot(
            ShellCapabilityRegistry.ContractVersion,
            protector.Protect(generationComponents),
            Array.AsReadOnly(capabilities));
    }

    private static bool IsAllowed(
        ShellCapabilityDeclaration declaration,
        IReadOnlyDictionary<string, PermissionScope> scopes)
    {
        return declaration.RequiredPermissions.All(permission =>
        {
            var scope = scopes[permission];
            return scope.IsGranted
                && (declaration.ScopeRequirement
                    != ShellCapabilityScopeRequirement.CompanyWide
                    || scope.IsCompanyWide);
        });
    }
}

public sealed record ShellCapabilitySnapshot(
    string ContractVersion,
    string Generation,
    IReadOnlyList<string> Capabilities);

public sealed class ShellCapabilityUnavailableException : Exception;
