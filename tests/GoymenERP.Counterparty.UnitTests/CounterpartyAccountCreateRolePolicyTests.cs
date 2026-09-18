using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyAccountCreateRolePolicyTests
{
    private const string ContractPath =
        "src/backend/Modules/Counterparty/COUNTERPARTY_ACCOUNT_CREATE_ROLE_POLICY.json";
    private const string ContractSourcePath =
        "docs/features/CARI_ACCOUNT_CREATE_ROLE_POLICY_v1.md";
    private const string DecisionSource =
        "https://github.com/sinangym/GoymenERP/issues/43#issuecomment-5461694092";
    private const string AccountCreatePermission = "counterparty.accounts.create";
    private static readonly Guid AdministratorId =
        Guid.Parse("30000000-0000-0000-0000-000000000003");

    [Fact]
    public void Accepted_option_a_matches_exact_contract()
    {
        var root = FindRepositoryRoot();
        var errors = Validate(root, ReadContract(root));
        Assert.True(errors.Count == 0, string.Join("\n\n", errors));
    }

    [Fact]
    public void Strict_json_rejects_ambiguity_and_missing_required_fields()
    {
        const string duplicateOwner = """
            {
              "schema_version": 1,
              "owner": "CHATGPT-CARI",
              "owner": "OTHER",
              "module": "Counterparty",
              "decision_id": "CARI-PERM-001",
              "decision_source": "source",
              "contract_source": "contract",
              "permission_code": "counterparty.accounts.create",
              "roles": [],
              "default_denied_role_codes": [],
              "automatic_user_assignments": [],
              "existing_install_reconciliation": {
                "add_missing_roles": false,
                "add_missing_permissions": true,
                "preserve_custom_permissions": true,
                "preserve_existing_assignments": true,
                "reject_id_code_collisions": true,
                "transactional": true
              }
            }
            """;
        Assert.Throws<InvalidOperationException>(() => Parse(duplicateOwner));

        const string unknownProperty = """
            {
              "schema_version": 1,
              "owner": "CHATGPT-CARI",
              "module": "Counterparty",
              "decision_id": "CARI-PERM-001",
              "decision_source": "source",
              "contract_source": "contract",
              "permission_code": "counterparty.accounts.create",
              "roles": [],
              "default_denied_role_codes": [],
              "automatic_user_assignments": [],
              "existing_install_reconciliation": {
                "add_missing_roles": false,
                "add_missing_permissions": true,
                "preserve_custom_permissions": true,
                "preserve_existing_assignments": true,
                "reject_id_code_collisions": true,
                "transactional": true
              },
              "unexpected": true
            }
            """;
        Assert.Throws<JsonException>(() => Parse(unknownProperty));

        const string missingPermissionCode = """
            {
              "schema_version": 1,
              "owner": "CHATGPT-CARI",
              "module": "Counterparty",
              "decision_id": "CARI-PERM-001",
              "decision_source": "source",
              "contract_source": "contract",
              "roles": [],
              "default_denied_role_codes": [],
              "automatic_user_assignments": [],
              "existing_install_reconciliation": {
                "add_missing_roles": false,
                "add_missing_permissions": true,
                "preserve_custom_permissions": true,
                "preserve_existing_assignments": true,
                "reject_id_code_collisions": true,
                "transactional": true
              }
            }
            """;
        Assert.Throws<JsonException>(() => Parse(missingPermissionCode));

        Assert.ThrowsAny<JsonException>(() => Parse("{ // forbidden\n\"schema_version\": 1 }"));
        Assert.ThrowsAny<JsonException>(() => Parse("{ \"schema_version\": 1, }"));
    }

    [Fact]
    public void Validation_rejects_role_grant_assignment_and_reconciliation_drift()
    {
        var root = FindRepositoryRoot();
        var accepted = ReadContract(root);
        Assert.Empty(Validate(root, accepted));
        var admin = Assert.Single(accepted.Roles!);

        var missingPermission = accepted with
        {
            Roles = [admin with { PermissionsToEnsure = [] }],
        };
        AssertError(root, missingPermission, "permission bundle");

        var extraRole = accepted with
        {
            Roles =
            [
                admin,
                new RoleEntry(
                    Guid.Parse("30000000-0000-0000-0000-000000000006"),
                    "COUNTERPARTY_ACCOUNT_OPERATOR",
                    "Cari Hesap Yönetimi",
                    true,
                    true,
                    [AccountCreatePermission]),
            ],
        };
        AssertError(root, extraRole, "exactly one authorized role");

        // Must stay unregistered, or this case would stop exercising the "unknown permission" error.
        const string unregisteredPermission = "counterparty.accounts.unknown";
        Assert.DoesNotContain(unregisteredPermission, ErpPermissions.All);
        var unknownPermission = accepted with
        {
            Roles =
            [
                admin with
                {
                    PermissionsToEnsure =
                    [AccountCreatePermission, unregisteredPermission],
                },
            ],
        };
        AssertError(root, unknownPermission, "unknown permission");

        var duplicatePermission = accepted with
        {
            Roles =
            [
                admin with
                {
                    PermissionsToEnsure =
                    [AccountCreatePermission, AccountCreatePermission],
                },
            ],
        };
        AssertError(root, duplicatePermission, "duplicate permissions");

        var automaticAssignment = accepted with
        {
            AutomaticUserAssignments =
            [new AutomaticAssignment(Guid.NewGuid(), AdministratorId, Guid.NewGuid())],
        };
        AssertError(root, automaticAssignment, "automatic user assignment");

        var createMissingRole = accepted with
        {
            Roles = [admin with { CreateIfMissing = true }],
            Reconciliation = accepted.Reconciliation! with { AddMissingRoles = true },
        };
        AssertError(root, createMissingRole, "must not create missing roles");

        var broadGrant = accepted with
        {
            DefaultDeniedRoleCodes =
            ["ORGANIZATION_READER", "SECURITY_ADMINISTRATOR", "SECURITY_APPROVER"],
        };
        AssertError(root, broadGrant, "default-denied roles");
    }

    private static void AssertError(string root, PolicyContract contract, string text)
    {
        Assert.Contains(
            Validate(root, contract),
            error => error.Contains(text, StringComparison.Ordinal));
    }

    private static PolicyContract ReadContract(string root) =>
        DeserializeStrict(File.ReadAllBytes(ToFullPath(root, ContractPath)));

    private static PolicyContract Parse(string json) =>
        DeserializeStrict(Encoding.UTF8.GetBytes(json));

    private static PolicyContract DeserializeStrict(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });
        var duplicates = new List<string>();
        FindDuplicates(document.RootElement, "$", duplicates);
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", duplicates));
        }

        return JsonSerializer.Deserialize<PolicyContract>(bytes, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        }) ?? throw new InvalidOperationException("Role policy is empty.");
    }

    private static IReadOnlyList<string> Validate(string root, PolicyContract contract)
    {
        var errors = new List<string>();
        Equal(contract.SchemaVersion, 1, "schema_version", errors);
        Equal(contract.Owner, "CHATGPT-CARI", "owner", errors);
        Equal(contract.Module, "Counterparty", "module", errors);
        Equal(contract.DecisionId, "CARI-PERM-001", "decision_id", errors);
        Equal(contract.DecisionSource, DecisionSource, "decision_source", errors);
        Equal(Normalize(contract.ContractSource), ContractSourcePath, "contract_source", errors);
        Equal(contract.PermissionCode, AccountCreatePermission, "permission_code", errors);
        ValidateSource(root, contract.ContractSource, errors);

        var roles = contract.Roles ?? [];
        if (roles.Count != 1)
        {
            errors.Add($"Option A must contain exactly one authorized role; found {roles.Count}.");
        }
        if (roles.Select(role => role.Id).Distinct().Count() != roles.Count)
        {
            errors.Add("Authorized role IDs must be unique.");
        }
        if (roles.Select(role => role.Code).Distinct(StringComparer.Ordinal).Count() != roles.Count)
        {
            errors.Add("Authorized role codes must be unique.");
        }

        var admin = roles.SingleOrDefault(role => role.Code == "SYSTEM_ADMINISTRATOR");
        if (admin is null)
        {
            errors.Add("SYSTEM_ADMINISTRATOR role is missing.");
        }
        else
        {
            if (admin.Id != AdministratorId
                || !string.Equals(admin.Name, "Sistem Yöneticisi", StringComparison.Ordinal)
                || !admin.IsSystem
                || admin.CreateIfMissing)
            {
                errors.Add("Unexpected system administrator identity or lifecycle flags.");
            }
            if (!(admin.PermissionsToEnsure ?? []).SequenceEqual(
                    [AccountCreatePermission], StringComparer.Ordinal))
            {
                errors.Add("Unexpected system administrator permission bundle.");
            }
        }

        foreach (var role in roles)
        {
            var permissions = role.PermissionsToEnsure ?? [];
            if (role.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(role.Code)
                || string.IsNullOrWhiteSpace(role.Name)
                || !role.IsSystem)
            {
                errors.Add($"Role identity is invalid: {role.Code}.");
            }
            if (permissions.Count == 0
                || permissions.Any(permission =>
                    string.IsNullOrWhiteSpace(permission)
                    || !(string.Equals(permission, AccountCreatePermission, StringComparison.Ordinal)
                         || ErpPermissions.IsKnown(permission))))
            {
                errors.Add($"Role contains blank or unknown permission: {role.Code}.");
            }
            if (permissions.Distinct(StringComparer.Ordinal).Count() != permissions.Count)
            {
                errors.Add($"Role contains duplicate permissions: {role.Code}.");
            }
        }

        var expectedDenied = new[]
        {
            "ORGANIZATION_READER",
            "ORGANIZATION_OPERATOR",
            "SECURITY_ADMINISTRATOR",
            "SECURITY_APPROVER",
        };
        var denied = contract.DefaultDeniedRoleCodes ?? [];
        if (!denied.SequenceEqual(expectedDenied, StringComparer.Ordinal)
            || denied.Distinct(StringComparer.Ordinal).Count() != denied.Count)
        {
            errors.Add("The exact default-denied roles are not preserved.");
        }
        if (contract.AutomaticUserAssignments is null
            || contract.AutomaticUserAssignments.Count != 0)
        {
            errors.Add("The policy must not create any automatic user assignment.");
        }

        if (contract.Reconciliation is null)
        {
            errors.Add("Existing-install reconciliation policy is missing.");
        }
        else
        {
            if (contract.Reconciliation.AddMissingRoles)
            {
                errors.Add("Option A reconciliation must not create missing roles.");
            }
            if (!contract.Reconciliation.AddMissingPermissions
                || !contract.Reconciliation.PreserveCustomPermissions
                || !contract.Reconciliation.PreserveExistingAssignments
                || !contract.Reconciliation.RejectIdCodeCollisions
                || !contract.Reconciliation.Transactional)
            {
                errors.Add("Existing-install fail-closed invariants are incomplete.");
            }
        }
        return errors;
    }

    private static void FindDuplicates(JsonElement element, string path, ICollection<string> errors)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    errors.Add($"Duplicate JSON property at {path}: {property.Name}.");
                }
                FindDuplicates(property.Value, $"{path}.{property.Name}", errors);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                FindDuplicates(item, $"{path}[{index++}]", errors);
            }
        }
    }

    private static void ValidateSource(string root, string? path, ICollection<string> errors)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains('*')
            || normalized.Contains('?'))
        {
            errors.Add($"Invalid contract source path: {path}.");
            return;
        }
        var fullPath = ToFullPath(root, normalized);
        var relative = Path.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || !File.Exists(fullPath))
        {
            errors.Add($"Contract source is missing or outside repository: {normalized}.");
        }
    }

    private static void Equal<T>(T actual, T expected, string field, ICollection<string> errors)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            errors.Add($"Unexpected {field}: {actual}.");
        }
    }

    private static string ToFullPath(string root, string path) =>
        Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));

    private static string Normalize(string? path) =>
        (path ?? string.Empty).Replace('\\', '/').Trim();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Göymen ERP repository root was not found.");
    }

    private sealed record PolicyContract(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("owner")] string? Owner,
        [property: JsonPropertyName("module")] string? Module,
        [property: JsonPropertyName("decision_id")] string? DecisionId,
        [property: JsonPropertyName("decision_source")] string? DecisionSource,
        [property: JsonPropertyName("contract_source")] string? ContractSource,
        [property: JsonPropertyName("permission_code")] string? PermissionCode,
        [property: JsonPropertyName("roles")] IReadOnlyList<RoleEntry>? Roles,
        [property: JsonPropertyName("default_denied_role_codes")]
        IReadOnlyList<string>? DefaultDeniedRoleCodes,
        [property: JsonPropertyName("automatic_user_assignments")]
        IReadOnlyList<AutomaticAssignment>? AutomaticUserAssignments,
        [property: JsonPropertyName("existing_install_reconciliation")]
        Reconciliation? Reconciliation);

    private sealed record RoleEntry(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("is_system")] bool IsSystem,
        [property: JsonPropertyName("create_if_missing")] bool CreateIfMissing,
        [property: JsonPropertyName("permissions_to_ensure")]
        IReadOnlyList<string>? PermissionsToEnsure);

    private sealed record AutomaticAssignment(
        [property: JsonPropertyName("user_id")] Guid UserId,
        [property: JsonPropertyName("role_id")] Guid RoleId,
        [property: JsonPropertyName("company_id")] Guid CompanyId);

    private sealed record Reconciliation(
        [property: JsonPropertyName("add_missing_roles")] bool AddMissingRoles,
        [property: JsonPropertyName("add_missing_permissions")] bool AddMissingPermissions,
        [property: JsonPropertyName("preserve_custom_permissions")]
        bool PreserveCustomPermissions,
        [property: JsonPropertyName("preserve_existing_assignments")]
        bool PreserveExistingAssignments,
        [property: JsonPropertyName("reject_id_code_collisions")]
        bool RejectIdCodeCollisions,
        [property: JsonPropertyName("transactional")] bool Transactional);
}
