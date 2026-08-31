namespace GoymenERP.BuildingBlocks.Shell;

public sealed record ShellCapabilityDeclaration(
    string CapabilityId,
    string RouteId,
    string RoutePath,
    string ScreenId,
    ShellCapabilityContextRequirement ContextRequirement,
    ShellCapabilityScopeRequirement ScopeRequirement,
    IReadOnlyCollection<string> RequiredPermissions,
    bool ImplementationAvailable);
