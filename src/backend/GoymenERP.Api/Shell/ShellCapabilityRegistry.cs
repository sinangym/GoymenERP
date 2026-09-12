using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Api.Shell;

public sealed partial class ShellCapabilityRegistry
{
    public const string ContractVersion = "SHELL-CAPABILITY-V1";

    private readonly IReadOnlyList<ShellCapabilityRegistration> _registrations;

    public ShellCapabilityRegistry(IEnumerable<IShellCapabilityProvider> providers)
        : this(providers, new ShellCapabilityAuthorityCatalog([]))
    {
    }

    public ShellCapabilityRegistry(
        IEnumerable<IShellCapabilityProvider> providers,
        ShellCapabilityAuthorityCatalog authorityCatalog)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(authorityCatalog);

        _registrations = BuildValidatedRegistrations(providers);
        Revision = ComputeRevision(_registrations);
        if (_registrations.Count > 0 && !authorityCatalog.Accepts(Revision))
        {
            throw new InvalidOperationException(
                "Shell capability provider seti kabul edilmiş owner kaynağıyla eşleşmiyor.");
        }
    }

    public IReadOnlyList<ShellCapabilityRegistration> Registrations => _registrations;

    public string Revision { get; }

    internal static string ComputeCandidateRevision(
        IEnumerable<IShellCapabilityProvider> providers)
    {
        var registrations = BuildValidatedRegistrations(providers);
        return ComputeRevision(registrations);
    }

    private static IReadOnlyList<ShellCapabilityRegistration> BuildValidatedRegistrations(
        IEnumerable<IShellCapabilityProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var providerArray = providers.ToArray();
        var ownerNames = providerArray
            .Select(provider => provider is null
                ? throw new InvalidOperationException("Shell capability provider boş olamaz.")
                : RequiredMatch(provider.Owner, OwnerPattern(), "Capability owner"))
            .ToArray();
        if (ownerNames.Distinct(StringComparer.Ordinal).Count() != ownerNames.Length)
        {
            throw new InvalidOperationException(
                "Shell registry yinelenen capability owner içeriyor.");
        }

        var registrations = providerArray
            .SelectMany(BuildProviderRegistrations)
            .ToArray();
        ValidateUnique(
            registrations,
            registration => registration.Declaration.CapabilityId,
            "capability kimliği");
        ValidateUnique(
            registrations,
            registration => registration.Declaration.RouteId,
            "route kimliği");
        ValidateRoutePaths(registrations);
        ValidateUnique(
            registrations,
            registration => registration.Declaration.ScreenId,
            "ekran kimliği");
        return Array.AsReadOnly(registrations
            .OrderBy(x => x.Declaration.CapabilityId, StringComparer.Ordinal)
            .ToArray());
    }

    private static IEnumerable<ShellCapabilityRegistration> BuildProviderRegistrations(
        IShellCapabilityProvider provider)
    {
        if (provider is null)
        {
            throw new InvalidOperationException("Shell capability provider boş olamaz.");
        }

        var owner = RequiredMatch(
            provider.Owner,
            OwnerPattern(),
            "Capability owner");
        var contractVersion = RequiredMatch(
            provider.ContractVersion,
            ContractVersionPattern(),
            "Capability owner contract version");
        var declarations = provider.GetDeclarations()
            ?? throw new InvalidOperationException(
                $"'{owner}' capability provider boş declaration koleksiyonu döndürdü.");

        foreach (var declaration in declarations)
        {
            if (declaration is null)
            {
                throw new InvalidOperationException(
                    $"'{owner}' capability provider null declaration içeriyor.");
            }

            ValidateDeclaration(owner, declaration);
            var immutableDeclaration = declaration with
            {
                RequiredPermissions = Array.AsReadOnly(
                    declaration.RequiredPermissions.ToArray()),
            };
            yield return new ShellCapabilityRegistration(
                owner,
                contractVersion,
                immutableDeclaration);
        }
    }

    private static void ValidateDeclaration(
        string owner,
        ShellCapabilityDeclaration declaration)
    {
        _ = RequiredMatch(
            declaration.CapabilityId,
            CapabilityIdPattern(),
            "Capability kimliği");
        _ = RequiredMatch(
            declaration.RouteId,
            RouteIdPattern(),
            "Route kimliği");
        _ = RequiredMatch(
            declaration.ScreenId,
            ScreenIdPattern(),
            "Ekran kimliği");

        var routePath = declaration.RoutePath?.Trim() ?? string.Empty;
        if (!string.Equals(routePath, declaration.RoutePath, StringComparison.Ordinal)
            || routePath.Contains("//", StringComparison.Ordinal)
            || routePath.Contains("..", StringComparison.Ordinal)
            || routePath.EndsWith("/", StringComparison.Ordinal)
            || !IsValidRoutePath(routePath))
        {
            throw new InvalidOperationException(
                $"'{owner}' provider geçersiz route yolu ilan etti.");
        }

        var hasRouteParameter = routePath
            .Split('/', StringSplitOptions.None)
            .Skip(1)
            .Any(segment => ParameterRouteSegmentPattern().IsMatch(segment));
        if (!Enum.IsDefined(declaration.RoutePrecedence)
            || hasRouteParameter
                != (declaration.RoutePrecedence
                    == ShellCapabilityRoutePrecedence.ParameterizedAfterStatic))
        {
            throw new InvalidOperationException(
                $"'{owner}' provider route yapısıyla uyumsuz öncelik ilan etti.");
        }

        if (declaration.ContextRequirement
            is not ShellCapabilityContextRequirement.SingleCompany
            and not ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead)
        {
            throw new InvalidOperationException(
                $"'{owner}' provider desteklenmeyen shell context ilan etti.");
        }

        if (!Enum.IsDefined(declaration.ScopeRequirement))
        {
            throw new InvalidOperationException(
                $"'{owner}' provider desteklenmeyen scope gereksinimi ilan etti.");
        }

        var permissions = declaration.RequiredPermissions?.ToArray()
            ?? throw new InvalidOperationException(
                $"'{owner}' provider permission koleksiyonu sağlamadı.");
        if (declaration.GlobalProductOperation is { } productOperation)
        {
            var expected = declaration.CapabilityId switch
            {
                "product.items.list" => (GroupSharedMasterOperation.Read, "/urun"),
                "product.items.detail" => (GroupSharedMasterOperation.Read, "/urun/:productId"),
                "product.items.create" => (GroupSharedMasterOperation.DraftCreate, "/urun/yeni"),
                "product.items.edit" => (GroupSharedMasterOperation.DraftBasicEdit, "/urun/:productId/duzenle"),
                _ => throw new InvalidOperationException("Unknown global Product capability."),
            };
            if (owner != "PRODUCT" || productOperation != expected.Item1
                || declaration.RoutePath != expected.Item2 || permissions.Length != 0
                || declaration.ContextRequirement != ShellCapabilityContextRequirement.SingleCompany
                || declaration.ScopeRequirement != ShellCapabilityScopeRequirement.AnyGrantedScope)
                throw new InvalidOperationException("Global Product capabilities require their exact operation and explicit origin, not company permissions.");
        }
        else if (declaration.CapabilityId.StartsWith("product.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Product capability cannot alias a company permission.");
        }
        if (declaration.ImplementationAvailable && permissions.Length == 0
            && declaration.GlobalProductOperation is null)
        {
            throw new InvalidOperationException(
                $"'{owner}' provider yetkisiz bir capability'yi kullanılabilir ilan etti.");
        }

        if (permissions.Any(permission =>
                string.IsNullOrWhiteSpace(permission)
                || !string.Equals(permission, permission.Trim(), StringComparison.Ordinal)
                || !ErpPermissions.IsKnown(permission)))
        {
            throw new InvalidOperationException(
                $"'{owner}' provider bilinmeyen permission ilan etti.");
        }

        if (permissions
            .Select(permission => permission.Trim())
            .Distinct(StringComparer.Ordinal)
            .Count() != permissions.Length)
        {
            throw new InvalidOperationException(
                $"'{owner}' provider yinelenen permission ilan etti.");
        }
    }

    private static string RequiredMatch(
        string? value,
        Regex pattern,
        string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!string.Equals(normalized, value, StringComparison.Ordinal)
            || !pattern.IsMatch(normalized))
        {
            throw new InvalidOperationException($"{fieldName} geçersiz.");
        }

        return normalized;
    }

    private static void ValidateUnique(
        IReadOnlyCollection<ShellCapabilityRegistration> registrations,
        Func<ShellCapabilityRegistration, string> selector,
        string fieldName,
        IEqualityComparer<string>? comparer = null)
    {
        comparer ??= StringComparer.Ordinal;
        if (registrations
            .GroupBy(selector, comparer)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                $"Shell registry yinelenen {fieldName} içeriyor.");
        }
    }

    private static void ValidateRoutePaths(
        IReadOnlyList<ShellCapabilityRegistration> registrations)
    {
        for (var leftIndex = 0; leftIndex < registrations.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1;
                 rightIndex < registrations.Count;
                 rightIndex++)
            {
                if (RoutesAreAmbiguous(
                        registrations[leftIndex].Declaration,
                        registrations[rightIndex].Declaration))
                {
                    throw new InvalidOperationException(
                        "Shell registry çakışan route yolu içeriyor.");
                }
            }
        }
    }

    private static bool RoutesAreAmbiguous(
        ShellCapabilityDeclaration left,
        ShellCapabilityDeclaration right)
    {
        var leftPath = left.RoutePath;
        var rightPath = right.RoutePath;
        var leftSegments = leftPath.Split('/', StringSplitOptions.None).Skip(1).ToArray();
        var rightSegments = rightPath.Split('/', StringSplitOptions.None).Skip(1).ToArray();
        if (leftSegments.Length != rightSegments.Length)
        {
            return false;
        }

        for (var index = 0; index < leftSegments.Length; index++)
        {
            var leftIsParameter = ParameterRouteSegmentPattern().IsMatch(
                leftSegments[index]);
            var rightIsParameter = ParameterRouteSegmentPattern().IsMatch(
                rightSegments[index]);
            if (!leftIsParameter
                && !rightIsParameter
                && !string.Equals(
                    leftSegments[index],
                    rightSegments[index],
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return left.RoutePrecedence == right.RoutePrecedence;
    }

    private static string ComputeRevision(
        IReadOnlyCollection<ShellCapabilityRegistration> registrations)
    {
        var canonical = new StringBuilder();
        Append(canonical, ContractVersion);
        foreach (var registration in registrations)
        {
            var declaration = registration.Declaration;
            Append(canonical, registration.Owner);
            Append(canonical, registration.OwnerContractVersion);
            Append(canonical, declaration.CapabilityId);
            Append(canonical, declaration.RouteId);
            Append(canonical, declaration.RoutePath);
            Append(canonical, declaration.ScreenId);
            Append(canonical, declaration.RoutePrecedence.ToString());
            Append(canonical, declaration.ContextRequirement.ToString());
            Append(canonical, declaration.ScopeRequirement.ToString());
            Append(canonical, declaration.ImplementationAvailable ? "1" : "0");
            foreach (var permission in declaration.RequiredPermissions
                         .OrderBy(x => x, StringComparer.Ordinal))
            {
                Append(canonical, permission);
            }
            // Existing company declarations retain byte-identical revisions.
            if (declaration.GlobalProductOperation is { } productOperation)
            {
                Append(canonical, "global-product-operation");
                Append(canonical, productOperation.ToString());
            }
        }

        return $"sha256:{Base64Url(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical.ToString())))}";
    }

    internal static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length)
            .Append(':')
            .Append(value);
    }

    internal static string Base64Url(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool IsValidRoutePath(string routePath)
    {
        if (!routePath.StartsWith("/", StringComparison.Ordinal)
            || routePath.Length < 2)
        {
            return false;
        }

        return routePath.Split('/', StringSplitOptions.None)
            .Skip(1)
            .All(segment => LiteralRouteSegmentPattern().IsMatch(segment)
                || ParameterRouteSegmentPattern().IsMatch(segment));
    }

    [GeneratedRegex(
        "^[A-Z][A-Z0-9-]{1,63}(?:/[A-Z][A-Z0-9-]{1,63})*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex OwnerPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContractVersionPattern();

    [GeneratedRegex("^[a-z][a-z0-9.-]{2,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CapabilityIdPattern();

    [GeneratedRegex(
        "^(?:[a-z][a-z0-9.-]{2,127}|[A-Z][A-Z0-9-]{2,63})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RouteIdPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScreenIdPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex LiteralRouteSegmentPattern();

    [GeneratedRegex(
        "^(?:\\{[a-z][a-z0-9-]*\\}|:[a-z][A-Za-z0-9]*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ParameterRouteSegmentPattern();
}

public sealed record ShellCapabilityRegistration(
    string Owner,
    string OwnerContractVersion,
    ShellCapabilityDeclaration Declaration);
