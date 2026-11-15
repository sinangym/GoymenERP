using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoymenERP.ArchitectureTests;

/// <summary>
/// The default-credential ban (identity spec F-001/F-002 "Varsayılan kimlik bilgisi yasağı", items 1 and 5; KIM-D6).
/// Code, <c>appsettings*.json</c>, seed files, <c>.env.example</c>, <c>infra/**</c> compose files and test fixture
/// data hold no usable user name + password pair, no local credential row and no weak-password exception key;
/// tests make their passwords at run time. The only exception is <see cref="DocumentationExceptionGlob"/>: a
/// Markdown document that names the ban is not a credential. Configuration-shaped files under <c>docs/</c> are
/// scanned like any other configuration file.
/// </summary>
public sealed partial class DefaultCredentialGuardTests
{
    /// <summary>The single reviewed exception (spec item 5: narrow, visible in review).</summary>
    private const string DocumentationExceptionGlob = "docs/**/*.md";

    /// <summary>
    /// Source files allowed to construct a <c>LocalCredential</c>. None in ID-1; ID-2 adds the one-time
    /// <c>bootstrap-founder</c> command, whose password is typed by the operator and always marked
    /// <c>MustChangePassword</c>.
    /// </summary>
    private static readonly string[] ReviewedLocalCredentialCreationSites =
    [
        "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Bootstrap/FounderBootstrapService.cs",
    ];

    /// <summary>Well-known default passwords that are never a configuration value or a backend string literal.</summary>
    private static readonly string[] KnownDefaultPasswords =
    [
        "password", "passw0rd", "p@ssw0rd", "p@ssword", "password1", "password123", "admin", "admin1", "admin123",
        "admin@123", "administrator", "changeme", "letmein", "qwerty", "welcome1", "123456", "12345678", "123456789",
        "root", "toor", "sa", "test", "test123", "demo", "goymen", "goymen123",
    ];

    private static readonly string[] ExcludedDirectoryNames =
        [".git", "bin", "obj", "node_modules", "dist", "coverage", "test-results", "playwright-report", "artifacts", "TestResults"];

    [Fact]
    public void The_documentation_exception_is_markdown_under_docs_only()
    {
        Assert.Equal("docs/**/*.md", DocumentationExceptionGlob);
        Assert.Equal(
            ["src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Bootstrap/FounderBootstrapService.cs"],
            ReviewedLocalCredentialCreationSites);
        Assert.False(IsDocumentationException("docs/operations/example.json"));
        Assert.False(IsDocumentationException("src/backend/README.md"));
        Assert.True(IsDocumentationException("docs/features/example.md"));
    }

    [Fact]
    public void Configuration_seed_compose_and_fixture_files_hold_no_credential_value()
    {
        var root = FindRepositoryRoot();
        var files = RepositoryFiles(root).Where(IsConfigurationFile).ToArray();

        // The scan must see the files the ban names; an empty scope would pass silently.
        Assert.Contains(".env.example", files);
        Assert.Contains("src/backend/GoymenERP.Api/appsettings.json", files);
        Assert.Contains("src/backend/GoymenERP.Api/Seed/identity-seed.json", files);
        Assert.Contains(files, file => file.StartsWith("infra/", StringComparison.Ordinal) && file.Contains("compose", StringComparison.Ordinal));

        var findings = files
            .SelectMany(file => ConfigurationFindings(file, File.ReadAllText(Path.Combine(root, file))))
            .ToArray();

        Assert.Empty(findings);
    }

    [Fact]
    public void Backend_code_creates_no_local_credential_and_carries_no_default_password_literal()
    {
        var root = FindRepositoryRoot();
        var findings = RepositoryFiles(root)
            .Where(file => file.StartsWith("src/backend/", StringComparison.Ordinal)
                           && (file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".json", StringComparison.Ordinal)))
            .SelectMany(file => BackendCodeFindings(file, File.ReadAllText(Path.Combine(root, file))))
            .ToArray();

        Assert.Empty(findings);
    }

    [Fact]
    public void Tests_hash_only_passwords_made_at_run_time()
    {
        var root = FindRepositoryRoot();
        var findings = RepositoryFiles(root)
            .Where(file => file.StartsWith("tests/", StringComparison.Ordinal) && file.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(file => TestCodeFindings(file, File.ReadAllText(Path.Combine(root, file))))
            .ToArray();

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData(".env.example", "MSSQL_SA_PASSWORD=CHANGE_ME_USE_A_LONG_RANDOM_PASSWORD\nDB_CONNECTION_STRING=Server=x;User Id=sa;Password=CHANGE_ME;")]
    [InlineData("infra/docker-compose.yml", "    environment:\n      MSSQL_SA_PASSWORD: ${MSSQL_SA_PASSWORD:?must be set}\n      DB: Server=x;Password=${MSSQL_SA_PASSWORD};")]
    [InlineData("src/backend/GoymenERP.Api/appsettings.json", """{ "Identity": { "LocalAccount": { "PasswordHashing": { "Pbkdf2Iterations": 210000 } } } }""")]
    [InlineData("src/backend/GoymenERP.Api/Seed/identity-seed.json", """{ "Users": [ { "DisplayName": "SENTETIK", "Email": null } ] }""")]
    public void Placeholders_and_settings_without_a_credential_pass(string file, string content)
    {
        Assert.Empty(ConfigurationFindings(file, content));
    }

    [Theory]
    [InlineData(".env.example", "ADMIN_PASSWORD=Gizli-Parola")]
    [InlineData(".env.example", "DB_CONNECTION_STRING=Server=x;User Id=sa;Password=Gizli-Parola;")]
    [InlineData("infra/docker-compose.yml", "      MSSQL_SA_PASSWORD: \"Gizli-Parola\"")]
    [InlineData("infra/docker-compose.yml", "      - IDENTITY__LOCALACCOUNT__DEVELOPMENTALLOWWEAKPASSWORD=false")]
    [InlineData("src/backend/GoymenERP.Api/appsettings.Development.json", """{ "Identity": { "LocalAccount": { "DevelopmentAllowWeakPassword": true } } }""")]
    [InlineData("src/backend/GoymenERP.Api/appsettings.json", """{ "Admin": { "UserName": "kurucu", "Password": "Gizli-Parola" } }""")]
    [InlineData("src/backend/GoymenERP.Api/Seed/identity-seed.json", """{ "Users": [ { "UserName": "kurucu", "PasswordHash": "AQAAAAIAAYagAAAAE" } ] }""")]
    [InlineData("src/backend/GoymenERP.Api/Seed/identity-seed.json", """{ "LocalCredentials": [] }""")]
    [InlineData("tests/e2e-fixtures/example.json", """{ "login": { "user": "admin", "secret": "admin" } }""")]
    [InlineData("docs/operations/example.yaml", "admin_password: admin123")]
    public void A_credential_value_or_row_in_configuration_is_found(string file, string content)
    {
        Assert.NotEmpty(ConfigurationFindings(file, content));
    }

    [Theory]
    [InlineData("src/backend/Modules/Identity/Bootstrap.cs", "var credential = new LocalCredential(userId, name, hash, 3, true, now, stamp);")]
    [InlineData("src/backend/GoymenERP.Api/Program.cs", "var fallback = \"admin123\";")]
    [InlineData("src/backend/Modules/Identity/Migrations/X.cs", "migrationBuilder.InsertData(schema: \"identity\", table: \"LocalCredentials\", columns: new[] { \"UserId\" }, values: new object[] { id });")]
    public void Backend_credential_creation_or_default_literals_are_found(string file, string content)
    {
        Assert.NotEmpty(BackendCodeFindings(file, content));
    }

    [Theory]
    [InlineData("var hash = hasher.Hash(\"Sabit-Parola\");")]
    [InlineData("var hash = FakePasswordHasher.HashWithCost(@\"Sabit-Parola\", 2);")]
    [InlineData("var hash = hasher.HashPassword(user, \"Sabit-Parola\");")]
    public void A_test_that_hashes_a_fixed_password_is_found(string content)
    {
        Assert.NotEmpty(TestCodeFindings("tests/Example/ExampleTests.cs", content));
        Assert.Empty(TestCodeFindings("tests/Example/ExampleTests.cs", "var hash = hasher.Hash(NewPassword());"));
        Assert.Empty(TestCodeFindings("tests/Example/ExampleTests.cs", "await store.SaveAsync(envelope, Hash(\"f-submit\"));"));
    }

    private static IEnumerable<string> ConfigurationFindings(string file, string content)
    {
        var findings = new List<string>();
        foreach (Match match in ConnectionStringPassword().Matches(content))
        {
            if (!IsPlaceholder(match.Groups["value"].Value))
            {
                findings.Add($"{file}: connection string password is not a placeholder");
            }
        }

        if (WeakPasswordKey().IsMatch(Regex.Replace(content, "[^A-Za-z]", string.Empty)))
        {
            findings.Add($"{file}: the development weak-password key is allowed only in local user-secrets");
        }

        if (file.EndsWith(".json", StringComparison.Ordinal))
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(content, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
            }
            catch (JsonException)
            {
                findings.Add($"{file}: not readable JSON; the scan cannot prove it holds no credential");
                return findings;
            }

            using (document)
            {
                VisitJson(file, document.RootElement, isSeed: IsSeedFile(file), findings);
            }

            return findings;
        }

        foreach (var line in content.Split('\n'))
        {
            var pair = KeyValueLine().Match(line);
            if (pair.Success && IsSecretKey(pair.Groups["key"].Value) && !IsPlaceholder(pair.Groups["value"].Value))
            {
                findings.Add($"{file}: {pair.Groups["key"].Value} is not a placeholder");
            }
        }

        return findings;
    }

    private static void VisitJson(string file, JsonElement element, bool isSeed, List<string> findings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (isSeed && LocalCredentialSeedKey().IsMatch(property.Name))
                    {
                        findings.Add($"{file}: seed carries a local credential field '{property.Name}'");
                    }

                    if (IsSecretKey(property.Name)
                        && !(property.Value.ValueKind is JsonValueKind.Null
                             || (property.Value.ValueKind == JsonValueKind.String && IsPlaceholder(property.Value.GetString()!))))
                    {
                        findings.Add($"{file}: '{property.Name}' is not a placeholder");
                    }

                    VisitJson(file, property.Value, isSeed, findings);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    VisitJson(file, item, isSeed, findings);
                }

                break;
            case JsonValueKind.String:
                var value = element.GetString()!;
                if (KnownDefaultPasswords.Contains(value, StringComparer.OrdinalIgnoreCase) && isSeed)
                {
                    findings.Add($"{file}: seed carries a known default password value");
                }

                break;
        }
    }

    private static IEnumerable<string> BackendCodeFindings(string file, string content)
    {
        if (NewLocalCredential().IsMatch(content) && !ReviewedLocalCredentialCreationSites.Contains(file, StringComparer.Ordinal))
        {
            yield return $"{file}: creates a LocalCredential outside a reviewed creation site";
        }

        if (InsertLocalCredential().IsMatch(content))
        {
            yield return $"{file}: a migration inserts a local credential row";
        }

        foreach (Match literal in StringLiteral().Matches(content))
        {
            var value = literal.Groups["value"].Value;
            if (KnownDefaultPasswords.Contains(value, StringComparer.OrdinalIgnoreCase) && LooksLikePasswordOnly(value))
            {
                yield return $"{file}: carries the known default password literal of length {value.Length}";
            }
        }
    }

    private static IEnumerable<string> TestCodeFindings(string file, string content)
    {
        foreach (Match match in FixedPasswordHash().Matches(content))
        {
            var line = content[..match.Index].Count(character => character == '\n') + 1;
            yield return $"{file}:{line}: hashes a fixed password; make it at run time";
        }
    }

    /// <summary>Short words such as "sa", "root", "test" are also names in code; only password-shaped ones count there.</summary>
    private static bool LooksLikePasswordOnly(string value) =>
        value.Length >= 6 && !value.Equals("administrator", StringComparison.OrdinalIgnoreCase);

    private static bool IsSecretKey(string key)
    {
        var normalized = Regex.Replace(key, "[^A-Za-z]", string.Empty).ToUpperInvariant();
        return normalized.EndsWith("PASSWORD", StringComparison.Ordinal)
               || normalized.EndsWith("PASSWORDHASH", StringComparison.Ordinal)
               || normalized.EndsWith("PASSWD", StringComparison.Ordinal)
               || normalized.EndsWith("PWD", StringComparison.Ordinal)
               || normalized.EndsWith("SECRET", StringComparison.Ordinal)
               || normalized.EndsWith("SECRETKEY", StringComparison.Ordinal)
               || normalized.EndsWith("ACCESSKEY", StringComparison.Ordinal);
    }

    private static bool IsPlaceholder(string value)
    {
        var trimmed = value.Trim().Trim('"', '\'').Trim();
        return trimmed.Length == 0
               || trimmed.StartsWith("${", StringComparison.Ordinal)
               || trimmed.StartsWith("$${", StringComparison.Ordinal)
               || trimmed.StartsWith("CHANGE_ME", StringComparison.Ordinal)
               || (trimmed.StartsWith('<') && trimmed.EndsWith('>'));
    }

    private static bool IsConfigurationFile(string file)
    {
        if (IsDocumentationException(file))
        {
            return false;
        }

        var name = Path.GetFileName(file);
        var extension = Path.GetExtension(file);
        if (name.Equals(".env.example", StringComparison.Ordinal) || name.EndsWith(".env.example", StringComparison.Ordinal))
        {
            return true;
        }

        if (name.StartsWith("appsettings", StringComparison.Ordinal) && extension == ".json")
        {
            return true;
        }

        if (IsSeedFile(file))
        {
            return true;
        }

        if (file.StartsWith("infra/", StringComparison.Ordinal)
            && name.Contains("compose", StringComparison.Ordinal)
            && extension is ".yml" or ".yaml")
        {
            return true;
        }

        if (file.StartsWith("tests/", StringComparison.Ordinal)
            && extension == ".json"
            && (file.StartsWith("tests/e2e-fixtures/", StringComparison.Ordinal)
                || file.Contains("/Fixtures/", StringComparison.Ordinal)
                || file.Contains("/fixtures/", StringComparison.Ordinal)))
        {
            return true;
        }

        return file.StartsWith("docs/", StringComparison.Ordinal) && extension is ".json" or ".yml" or ".yaml";
    }

    private static bool IsSeedFile(string file) =>
        file.StartsWith("src/", StringComparison.Ordinal)
        && file.Contains("/Seed/", StringComparison.Ordinal)
        && file.EndsWith(".json", StringComparison.Ordinal);

    private static bool IsDocumentationException(string file) =>
        file.StartsWith("docs/", StringComparison.Ordinal) && file.EndsWith(".md", StringComparison.Ordinal);

    private static IEnumerable<string> RepositoryFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!ExcludedDirectoryNames.Contains(Path.GetFileName(child), StringComparer.Ordinal))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                yield return Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            }
        }
    }

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

    [GeneratedRegex(@"(?i)\b(?:password|pwd)\s*=\s*(?<value>[^;""'\r\n]*)")]
    private static partial Regex ConnectionStringPassword();

    [GeneratedRegex("(?i)DEVELOPMENTALLOWWEAKPASSWORD")]
    private static partial Regex WeakPasswordKey();

    [GeneratedRegex(@"^\s*(?:-\s*)?(?:export\s+)?(?<key>[A-Za-z0-9_.:\-]+)\s*[:=]\s*(?<value>.*?)\s*$")]
    private static partial Regex KeyValueLine();

    [GeneratedRegex("(?i)^(?:password|passwordhash|localcredentials?|securitystamp)$")]
    private static partial Regex LocalCredentialSeedKey();

    [GeneratedRegex(@"\bnew\s+LocalCredential\s*\(")]
    private static partial Regex NewLocalCredential();

    [GeneratedRegex(@"InsertData\([^;]*table:\s*""LocalCredentials""", RegexOptions.Singleline)]
    private static partial Regex InsertLocalCredential();

    [GeneratedRegex(@"""(?<value>[^""\\\r\n]{1,64})""")]
    private static partial Regex StringLiteral();

    // Only password-hasher calls: plain "Hash(" is also the request-hash helper of many relational tests.
    [GeneratedRegex(@"(?i:\b\w*hasher\w*)\s*\.\s*(?:Hash|HashWithCost)\(\s*@?""|\bHashPassword\([^,()]*,\s*@?""")]
    private static partial Regex FixedPasswordHash();
}
