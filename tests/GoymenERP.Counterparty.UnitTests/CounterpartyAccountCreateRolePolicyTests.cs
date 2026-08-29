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
    private const string ExpectedDecisionSource =
        "https://github.com/sinangym/GoymenERP/issues/43#issuecomment-5461694092";
    private const string PendingAccountCreatePermission =
        "counterparty.accounts.create";

    private static readonly Guid SystemAdministratorId =
        Guid.Parse("30000000-0000-0000-0000-000000000003");

    [Fact]
    public void Accepted_option_a_matches_exact_role_and_reconciliation_contract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var contract = ReadContract(repositoryRoot);
        var errors = ValidateContract(repositoryRoot, contract);

        Assert.True(errors.Count == 0, string.Join("\n\n", errors));
    }

    [Fact]
    public void Strict_json_rejects_duplicate_unknown_missing_and_noncanonical_json()
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
        Assert.Throws<InvalidOperationException>(() =>
            DeserializeStrict(Encoding.UTF8.GetBytes(duplicateOwner)));

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
        Assert.Throws<JsonException>(() =>
            DeserializeStrict(Encoding.UTF8.GetBytes(unknownProperty)));

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
        Assert.Throws<JsonException>(() =>
            DeserializeStrict(Encoding.UTF8.GetBytes(missingPermissionCode)));

        const string commented = """
            {
              // comments are forbidden
              "schema_version": 1
            }
            """;
        Assert.Throws<JsonException>(() =>
            DeserializeStrict(Encoding.UTF8.GetBytes(commented)));

        const string trailingComma = """
            {
              "schema_version": 1,
            }
            """;
        Assert.Throws<JsonException>(() =>
            DeserializeStrict(Encoding.UTF8.GetBytes(trailingComma)));
    }

    [Fact]
    public void Validation_rejects_role_grant_assignment_identity_and_reconciliation_drift()
    {
        var repositoryRoot = FindRepositoryRoot();
        var accepted = ReadContract(repositoryRoot);
        Assert.Empty(ValidateContract(repositoryRoot, accepted));
        var administrator = Assert.Single(accepted.Roles!);

        var missingPermission = accepted with
        {
            Roles = [administrator with { PermissionsToEnsure = [] }],
        };
        Assert.Contains(
            ValidateContract(repositoryRoot, missingPermission),
            error => error.Contains(
                "system administrator permission bundle",
                StringComparison.Ordinal));

        var extraOperatorRole = accepted with
        {
            Roles =
            [
                administrator,
                new RolePolicyEntry(
                    Guid.Parse("30000000-0000-0000-0000-000000000006"),
                    "COUNTERPARTY_ACCOUNT_OPERATOR",
                    "Cari Hesap Yönetimi",
                    true,
                    true,
                    [PendingAccountCreatePermission]),
            ],
        };
        Assert.Contains(
            ValidateContract(repositoryRoot, extraOperatorRole),
            error => error.Contains("exactly one authorized role", StringComparison.Ordinal));

        var unknownPermission = accepted with
        {
            Roles =
            [
                administrator with
                {
                    PermissionsToEnsure =
                    [
                        PendingAccountCreatePermission,
                        "counterparty.accounts.unknown",
                    ],
                },
            ],
        };
        Assert.Contains(
            ValidateContract(repositoryRoot, unknownPermission),
            error => error.Contains("blank or unknown permission", StringComparison.Ordinal));

        var duplicatePermission = accepted with
        {
            Roles =
            [
                administrator with
                {
                    PermissionsToEnsure =
                    [
                        PendingAccountCreatePermission,
                        PendingAccountCreatePermission,
                    ],
                },
            ],
        };
        Assert.Contains(
            ValidateContract(repositoryRoot, duplicatePermission),
            error => error.Contains("duplicate permissions", StringComparison.Ordinal));

        var automaticAssignment = accepted with
        {
            AutomaticUserAssignments =
            [
                new AutomaticAssignment(
                    Guid.NewGuid(),
                    SystemAdministratorId,
                    Guid.NewGuid()),
            ],
        };
        Assert.Contains(
            ValidateContract(repositoryRoot, automaticAssignment),
            error => error.Contains("automatic user assignment", StringComparison.Ordinal));

        var createMissingAdmin = accepted with
        {
            Roles = [administrator with { CreateIfMissing = true }],
            ExistingInstallReconciliation =
                accepted.ExistingInstallReconciliation! with { AddMissingRoles = true },
        };
        var createErrors = ValidateContract(repositoryRoot, createMissingAdmin);
        Assert.Contains(
            createErrors,
            error => error.Contains("role lifecycle flags", StringComparison.Ordinal));
        Assert.Contains(
            createErrors,
            error => error.Contains("must not create missing roles", StringComparison.Ordinal));

        var broadGrant = accepted with
        {
            DefaultDeniedRoleCodes =
            [
                "ORGANIZATION_READER",
                "SECURITY_ADMINISTRATOR",
                "SECURITY_APPROVER",
            ],
        };
        Assert.Contains(
            ValidateContract(repositoryRoot, broadGrant),
            error => error.Contains("default-denied roles", StringComparison.Ordinal));
    }

    private static RolePolicyContract ReadContract(string repositoryRoot)
    {
        var path = ToFullPath(repositoryRoot, ContractPath);
        return DeserializeStrict(File.ReadAllBytes(path));
    }

    private static RolePolicyContract DeserializeStrict(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });

        var duplicateErrors = new List<string>();
        FindDuplicateProperties(document.RootElement, "$", duplicateErrors);
        if (duplicateErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", duplicateErrors));
        }

        return JsonSerializer.Deserialize<RolePolicyContract>(
                   bytes,
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = false,
                       RespectRequiredConstructorParameters = true,
                       UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                   })
               ?? throw new InvalidOperationException(
                   "Counterparty account-create role policy is empty.");
    }

    private static IReadOnlyList<string> ValidateContract(
        string repositoryRoot,
        RolePolicyContract contract)
    {
        var errors = new List<string>();

        RequireEqual(contract.SchemaVersion, 1, "schema_version", errors);
        RequireEqual(contract.Owner, "CHATGPT-CARI", "owner", errors);
        RequireEqual(contract.Module, "Counterparty", "module", errors);
        RequireEqual(contract.DecisionId, "CARI-PERM-001", "decision_id", errors);
        RequireEqual(
            contract.DecisionSource,
            ExpectedDecisionSource,
            "decision_source",
            errors);
        RequireEqual(
            NormalizePath(contract.ContractSource),
            ContractSourcePath,
            "contract_source",
            errors);
        RequireEqual(
            contract.PermissionCode,
            PendingAccountCreatePermission,
            "permission_code",
            errors);
        ValidateRepositoryFile(repositoryRoot, contract.ContractSource, errors);

        var roles = contract.Roles ?? [];
        if (roles.Count != 1)
        {
            errors.Add(
                $"Option A must contain exactly one authorized role; found {roles.Count}.");
        }

        if (roles.Select(role => role.Id).Distinct().Count() != roles.Count)
        {
            errors.Add("Authorized role IDs must be unique.");
        }

        if (roles.Select(role => role.Code)
            .Distinct(StringComparer.Ordinal).Count() != roles.Count)
        {
            errors.Add("Authorized role codes must be unique.");
        }

        var administrator = roles.SingleOrDefault(role =>
            role.Code == "SYSTEM_ADMINISTRATOR");
        if (administrator is null)
        {
            errors.Add("SYSTEM_ADMINISTRATOR role is missing.");
        }
        else
        {
            ValidateRole(
                administrator,
                SystemAdministratorId,
                "Sistem Yöneticisi",
                createIfMissing: false,
                [PendingAccountCreatePermission],
                "system administrator",
                errors);
        }

        foreach (var role in roles)
        {
            if (role.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(role.Code)
                || string.IsNullOrWhiteSpace(role.Name)
                || !role.IsSystem)
            {
                errors.Add($"Role identity is invalid: {role.Code}.");
            }

            var permissions = role.PermissionsToEnsure ?? [];
            if (permissions.Count == 0
                || permissions.Any(permission =>
                    string.IsNullOrWhiteSpace(permission)
                    || !IsKnownOrAcceptedPendingPermission(permission)))
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

        var reconciliation = contract.ExistingInstallReconciliation;
        if (reconciliation is null)
        {
            errors.Add("Existing-install reconciliation policy is missing.");
        }
        else
        {
            if (reconciliation.AddMissingRoles)
            {
                errors.Add("Option A reconciliation must not create missing roles.");
            }

            if (!reconciliation.AddMissingPermissions
                || !reconciliation.PreserveCustomPermissions
                || !reconciliation.PreserveExistingAssignments
                || !reconciliation.RejectIdCodeCollisions
                || !reconciliation.Transactional)
            {
                errors.Add(
                    "Existing-install reconciliation must keep all accepted fail-closed invariants enabled.");
            }
        }

        return errors;
    }

    private static bool IsKnownOrAcceptedPendingPermission(string permission)
    {
        return string.Equals(
                   permission,
                   PendingAccountCreatePermission,
                   StringComparison.Ordinal)
               || ErpPermissions.IsKnown(permission);
    }

    private static void ValidateRole(
        RolePolicyEntry role,
        Guid expectedId,
        string expectedName,
        bool createIfMissing,
        IReadOnlyList<string> expectedPermissions,
        string label,
        ICollection<string> errors)
    {
        if (role.Id != expectedId)
        {
            errors.Add($"Unexpected {label} role ID: {role.Id}.");
        }

        if (!string.Equals(role.Name, expectedName, StringComparison.Ordinal))
        {
            errors.Add($"Unexpected {label} role name: {role.Name}.");
        }

        if (!role.IsSystem || role.CreateIfMissing != createIfMissing)
        {
            errors.Add($"Unexpected {label} role lifecycle flags.");
        }

        if (!(role.PermissionsToEnsure ?? []).SequenceEqual(
                expectedPermissions,
                StringComparer.Ordinal))
        {
            errors.Add($"Unexpected {label} permission bundle.");
        }
    }

    private static void RequireEqual<T>(
        T actual,
        T expected,
        string field,
        ICollection<string> errors)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            errors.Add($"Unexpected {field}: {actual}.");
        }
    }

    private static void FindDuplicateProperties(
        JsonElement element,
        string location,
        ICollection<string> errors)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    errors.Add($"Duplicate JSON property at {location}: {property.Name}.");
                }

                FindDuplicateProperties(
                    property.Value,
                    $"{location}.{property.Name}",
                    errors);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                FindDuplicateProperties(item, $"{location}[{index}]", errors);
                index++;
            }
        }
    }

    private static void ValidateRepositoryFile(
        string repositoryRoot,
        string? repositoryPath,
        ICollection<string> errors)
    {
        var normalized = NormalizePath(repositoryPath);
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains('*')
            || normalized.Contains('?'))
        {
            errors.Add($"Invalid contract source path: {repositoryPath}.");
            return;
        }

        var fullPath = ToFullPath(repositoryRoot, normalized);
        var relativePath = Path.GetRelativePath(repositoryRoot, fullPath);
        if (Path.IsPathRooted(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || !File.Exists(fullPath))
        {
            errors.Add($"Contract source is missing or outside repository: {normalized}.");
        }
    }

    private static string ToFullPath(string repositoryRoot, string repositoryPath)
    {
        return Path.GetFullPath(Path.Combine(
            repositoryRoot,
            repositoryPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string NormalizePath(string? path)
    {
        return (path ?? string.Empty).Replace('\\', '/').Trim();
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

    private sealed record RolePolicyContract(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("owner")] string? Owner,
        [property: JsonPropertyName("module")] string? Module,
        [property: JsonPropertyName("decision_id")] string? DecisionId,
        [property: JsonPropertyName("decision_source")] string? DecisionSource,
        [property: JsonPropertyName("contract_source")] string? ContractSource,
        [property: JsonPropertyName("permission_code")] string? PermissionCode,
        [property: JsonPropertyName("roles")] IReadOnlyList<RolePolicyEntry>? Roles,
        [property: JsonPropertyName("default_denied_role_codes")]
        IReadOnlyList<string>? DefaultDeniedRoleCodes,
        [property: JsonPropertyName("automatic_user_assignments")]
        IReadOnlyList<AutomaticAssignment>? AutomaticUserAssignments,
        [property: JsonPropertyName("existing_install_reconciliation")]
        ReconciliationPolicy? ExistingInstallReconciliation);

    private sealed record RolePolicyEntry(
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

    private sealed record ReconciliationPolicy(
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
