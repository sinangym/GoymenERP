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
        ValidateUnique(
            registrations,
            registration => CanonicalizeRoutePath(
                registration.Declaration.RoutePath),
            "route yolu",
            StringComparer.OrdinalIgnoreCase);
        ValidateUnique(
            registrations,
            registration => registration.Declaration.ScreenId,
            "ekran kimliği");

        _registrations = Array.AsReadOnly(registrations
            .OrderBy(x => x.Declaration.CapabilityId, StringComparer.Ordinal)
            .ToArray());
        Revision = ComputeRevision(_registrations);
    }

    public IReadOnlyList<ShellCapabilityRegistration> Registrations => _registrations;

    public string Revision { get; }

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
            RouteAndScreenIdPattern(),
            "Route kimliği");
        _ = RequiredMatch(
            declaration.ScreenId,
            RouteAndScreenIdPattern(),
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

        if (declaration.ContextRequirement
            != ShellCapabilityContextRequirement.SingleCompany)
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
        if (declaration.ImplementationAvailable && permissions.Length == 0)
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
            Append(canonical, declaration.ContextRequirement.ToString());
            Append(canonical, declaration.ScopeRequirement.ToString());
            Append(canonical, declaration.ImplementationAvailable ? "1" : "0");
            foreach (var permission in declaration.RequiredPermissions
                         .OrderBy(x => x, StringComparer.Ordinal))
            {
                Append(canonical, permission);
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

    private static string CanonicalizeRoutePath(string routePath)
    {
        return string.Join(
            '/',
            routePath.Split('/', StringSplitOptions.None)
                .Select(segment => ParameterRouteSegmentPattern().IsMatch(segment)
                    ? "{}"
                    : segment));
    }

    [GeneratedRegex("^[A-Z][A-Z0-9-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnerPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9.-]{2,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContractVersionPattern();

    [GeneratedRegex("^[a-z][a-z0-9.-]{2,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CapabilityIdPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex RouteAndScreenIdPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex LiteralRouteSegmentPattern();

    [GeneratedRegex("^\\{[a-z][a-z0-9-]*\\}$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterRouteSegmentPattern();
}

public sealed record ShellCapabilityRegistration(
    string Owner,
    string OwnerContractVersion,
    ShellCapabilityDeclaration Declaration);
