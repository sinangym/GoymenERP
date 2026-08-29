using System.Text.Json;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyAccountOperatorSeedContractTests
{
    private const string IdentitySeedPath =
        "src/backend/GoymenERP.Api/Seed/identity-seed.json";
    private const string SystemAdministratorCode = "SYSTEM_ADMINISTRATOR";
    private const string OperatorCode = "COUNTERPARTY_ACCOUNT_OPERATOR";
    private const string OperatorName = "Cari Hesap Yönetimi";
    private const string AccountCreatePermission = "counterparty.accounts.create";
    private static readonly Guid OperatorId =
        Guid.Parse("30000000-0000-0000-0000-000000000006");

    private static readonly string[] ExpectedOperatorPermissions =
    [
        "counterparty.accounts.create",
        "counterparty.accounts.status.update",
        "counterparty.parties.read",
    ];

    private static readonly string[] DefaultDeniedRoleCodes =
    [
        "ORGANIZATION_OPERATOR",
        "ORGANIZATION_READER",
        "SECURITY_ADMINISTRATOR",
        "SECURITY_APPROVER",
    ];

    [Fact]
    public void Fresh_install_seed_defines_operator_without_automatic_assignment()
    {
        using var document = ReadSeed();
        var identitySeed = document.RootElement.GetProperty("IdentitySeed");
        var roles = identitySeed.GetProperty("Roles")
            .EnumerateArray()
            .Select(ReadRole)
            .ToArray();
        var assignments = identitySeed.GetProperty("Assignments")
            .EnumerateArray()
            .Select(element => new Assignment(
                element.GetProperty("RoleId").GetGuid(),
                element.GetProperty("UserId").GetGuid(),
                element.GetProperty("CompanyId").GetGuid()))
            .ToArray();

        Assert.Equal(roles.Length, roles.Select(role => role.Id).Distinct().Count());
        Assert.Equal(
            roles.Length,
            roles.Select(role => role.Code).Distinct(StringComparer.Ordinal).Count());

        var operatorRole = Assert.Single(roles.Where(role => string.Equals(
            role.Code,
            OperatorCode,
            StringComparison.Ordinal)));
        Assert.Equal(OperatorId, operatorRole.Id);
        Assert.Equal(OperatorName, operatorRole.Name);
        Assert.True(operatorRole.IsSystem);
        Assert.Equal(
            ExpectedOperatorPermissions,
            operatorRole.Permissions.Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(
            assignments,
            assignment => assignment.RoleId == OperatorId);

        var systemAdministrator = Assert.Single(roles.Where(role => string.Equals(
            role.Code,
            SystemAdministratorCode,
            StringComparison.Ordinal)));
        Assert.Contains(
            AccountCreatePermission,
            systemAdministrator.Permissions,
            StringComparer.Ordinal);

        foreach (var deniedRoleCode in DefaultDeniedRoleCodes)
        {
            var deniedRole = Assert.Single(roles.Where(role => string.Equals(
                role.Code,
                deniedRoleCode,
                StringComparison.Ordinal)));
            Assert.DoesNotContain(
                AccountCreatePermission,
                deniedRole.Permissions,
                StringComparer.Ordinal);
        }
    }

    private static JsonDocument ReadSeed()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            IdentitySeedPath.Replace('/', Path.DirectorySeparatorChar));
        return JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
    }

    private static SeedRole ReadRole(JsonElement element)
    {
        return new SeedRole(
            element.GetProperty("Id").GetGuid(),
            element.GetProperty("Code").GetString()
                ?? throw new InvalidOperationException("Role code is missing."),
            element.GetProperty("Name").GetString()
                ?? throw new InvalidOperationException("Role name is missing."),
            element.GetProperty("IsSystem").GetBoolean(),
            element.GetProperty("Permissions")
                .EnumerateArray()
                .Select(permission => permission.GetString()
                    ?? throw new InvalidOperationException(
                        "Role permission is missing."))
                .ToArray());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Göymen ERP repository root was not found.");
    }

    private sealed record SeedRole(
        Guid Id,
        string Code,
        string Name,
        bool IsSystem,
        IReadOnlyList<string> Permissions);

    private sealed record Assignment(
        Guid RoleId,
        Guid UserId,
        Guid CompanyId);
}
