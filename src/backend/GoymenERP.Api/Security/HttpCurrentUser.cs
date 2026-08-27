using System.Security.Claims;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.Security;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal Principal => httpContextAccessor.HttpContext?.User
        ?? new ClaimsPrincipal();

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    public string? UserId => Principal.FindFirstValue(ClaimTypes.NameIdentifier);

    public IReadOnlySet<Guid> AllowedCompanyIds => Principal
        .FindAll(GoymenClaimTypes.CompanyAccess)
        .Select(x => Guid.TryParse(x.Value, out var companyId) ? companyId : Guid.Empty)
        .Where(x => x != Guid.Empty)
        .ToHashSet();

    public bool CanAccessCompany(Guid companyId)
    {
        return companyId != Guid.Empty && AllowedCompanyIds.Contains(companyId);
    }
}
