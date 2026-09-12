using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.Shell;

public sealed partial class ShellCapabilityService(
    ShellCapabilityRegistry registry,
    IActorSessionBindingResolver sessionBindingResolver,
    ShellCapabilityGenerationProtector protector,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorizationService authorizationService,
    IOrganizationService organizationService,
    IGroupSharedMasterAuthorizationService? productAuthorizationService = null)
{
    public async Task<ShellCapabilitySnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(currentUser.UserId, out var userId))
        {
            throw new ShellCapabilityUnavailableException();
        }

        var sessionBinding = await sessionBindingResolver.ResolveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(sessionBinding))
        {
            throw new ShellCapabilityUnavailableException();
        }

        var activeAuthorizedCompanyIds = await GetActiveAuthorizedCompanyIdsAsync(
            cancellationToken);
        if (activeAuthorizedCompanyIds.Length == 0)
        {
            throw new ShellCapabilityUnavailableException();
        }

        var isSingleCompany = companyContext.IsResolved;
        var selectedCompanyId = isSingleCompany
            ? companyContext.CompanyId
            : (Guid?)null;
        if (selectedCompanyId is Guid selected
            && (selected == Guid.Empty
                || !currentUser.CanAccessCompany(selected)
                || !activeAuthorizedCompanyIds.Contains(selected)))
        {
            throw new ShellCapabilityUnavailableException();
        }

        var candidateCompanyIds = selectedCompanyId is Guid selectedCompany
            ? [selectedCompany]
            : activeAuthorizedCompanyIds;
        var authorizations = new List<CompanyAuthorization>(candidateCompanyIds.Length);
        foreach (var candidateCompanyId in candidateCompanyIds)
        {
            var snapshot = await authorizationService.GetAuthorizationSnapshotAsync(
                candidateCompanyId,
                ErpPermissions.All.ToArray(),
                cancellationToken);
            if (snapshot.AllowedCompanyIds.Contains(candidateCompanyId))
            {
                authorizations.Add(new CompanyAuthorization(candidateCompanyId, snapshot));
            }
        }

        if (authorizations.Count == 0
            || isSingleCompany && authorizations.Count != 1)
        {
            throw new ShellCapabilityUnavailableException();
        }

        var productAuthorities = await ReadProductAuthoritiesAsync(userId, selectedCompanyId, cancellationToken);
        var capabilities = registry.Registrations
            .Where(registration => registration.Declaration.ImplementationAvailable)
            .Where(registration => IsContextCompatible(
                registration.Declaration,
                isSingleCompany))
            .Where(registration => registration.Declaration.GlobalProductOperation is { } operation
                ? productAuthorities.TryGetValue(operation, out var decision) && decision.IsGranted
                : authorizations.Any(authorization =>
                IsAllowed(
                    registration.Declaration,
                    authorization.Snapshot.PermissionScopes)))
            .Select(registration => registration.Declaration.CapabilityId)
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToArray();

        var generationComponents = new List<string>
        {
            ShellCapabilityRegistry.ContractVersion,
            registry.Revision,
            userId.ToString("D", CultureInfo.InvariantCulture),
            sessionBinding,
            isSingleCompany ? "single-company" : "authorized-multi-company-read",
            selectedCompanyId?.ToString("D", CultureInfo.InvariantCulture) ?? "-",
        };
        generationComponents.AddRange(activeAuthorizedCompanyIds
            .Select(id => $"company:{id:D}"));
        foreach (var authorization in authorizations.OrderBy(item => item.CompanyId))
        {
            generationComponents.Add($"authorization-company:{authorization.CompanyId:D}");
            generationComponents.Add($"authorization-revision:{authorization.Snapshot.Revision}");
            foreach (var (permission, scope) in authorization.Snapshot.PermissionScopes
                         .OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                generationComponents.Add($"permission:{permission}");
                generationComponents.Add(scope.IsGranted ? "granted:1" : "granted:0");
                generationComponents.Add(
                    scope.IsCompanyWide ? "company-wide:1" : "company-wide:0");
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
        }

        foreach (var (operation, decision) in productAuthorities.OrderBy(item => item.Key))
        {
            generationComponents.Add($"global-product-operation:{operation}");
            generationComponents.Add(decision.IsGranted ? "global-granted:1" : "global-granted:0");
            generationComponents.Add($"global-revision:{decision.AuthorityRevision}");
        }
        generationComponents.AddRange(capabilities.Select(x => $"capability:{x}"));
        var generation = protector.Protect(generationComponents);
        var readonlyCapabilities = Array.AsReadOnly(capabilities);
        var revalidatedActiveAuthorizedCompanyIds =
            await GetActiveAuthorizedCompanyIdsAsync(cancellationToken);
        if (!activeAuthorizedCompanyIds.SequenceEqual(
                revalidatedActiveAuthorizedCompanyIds))
        {
            throw new ShellCapabilityUnavailableException();
        }
        var revalidatedProduct = await ReadProductAuthoritiesAsync(userId, selectedCompanyId, cancellationToken);
        if (productAuthorities.Count != revalidatedProduct.Count
            || productAuthorities.Any(item => !revalidatedProduct.TryGetValue(item.Key, out var value) || item.Value != value))
            throw new ShellCapabilityUnavailableException();

        return new ShellCapabilitySnapshot(
            ShellCapabilityRegistry.ContractVersion,
            generation,
            readonlyCapabilities);
    }

    private async Task<Guid[]> GetActiveAuthorizedCompanyIdsAsync(
        CancellationToken cancellationToken)
    {
        var authorizedCompanies = await organizationService.GetAuthorizedCompaniesAsync(
            cancellationToken);
        return authorizedCompanies
            .Select(company => company.Id)
            .Where(currentUser.CanAccessCompany)
            .Distinct()
            .Order()
            .ToArray();
    }

    private static bool IsContextCompatible(
        ShellCapabilityDeclaration declaration,
        bool isSingleCompany)
    {
        return isSingleCompany
            || declaration.ContextRequirement
            == ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead;
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

    private sealed record CompanyAuthorization(
        Guid CompanyId,
        AuthorizationSnapshot Snapshot);
}

public sealed record ShellCapabilitySnapshot(
    string ContractVersion,
    string Generation,
    IReadOnlyList<string> Capabilities);

public sealed class ShellCapabilityUnavailableException : Exception;
