using Microsoft.AspNetCore.Authorization;

namespace GoymenERP.Api.Security;

public sealed record ErpPermissionRequirement(string Permission) : IAuthorizationRequirement;
