using System.Security.Claims;

namespace GoymenERP.Api.Security;

public interface IAuthenticationSessionBindingResolver
{
    string? Resolve(ClaimsPrincipal principal, Guid userId);
}
