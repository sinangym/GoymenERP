using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace GoymenERP.CariAcceptance006;

// A narrow adapter for the existing owner's three-file Product contract. No
// second run manifest, ambient endpoint fallback, production grant or lifecycle.
public sealed class ProductEnvironmentSettings
{
    public const string OwnerRoot = @"C:\Users\goyme\.codex\worktrees\dd1f\GoymenERP-codex\artifacts\product-u01-u02\acceptance";
    public const string Actor = "group-bootstrap-admin";
    public const string UnauthorizedActor = "company-a-user";
    public static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    public static readonly Guid ActorExternalId = Guid.Parse("21000000-0000-0000-0000-000000000003");
    public static readonly Guid UnauthorizedActorId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid SystemRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    public static readonly Guid CompanyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid CodeGroupId = Guid.Parse("82000000-0000-0000-0000-000000000001");
    public const string ApiBaseUrl = "http://127.0.0.1:26080";
    public const string SqlEndpoint = "127.0.0.1,26433";
    public const int ApiPort = 26080;
    public const int SqlPort = 26433;
    public static readonly string[] FixturePaths =
    [
        "tests/GoymenERP.Product.UnitTests/SqlAcceptance/ProductSqlFixture.cs",
        "tests/GoymenERP.Product.UnitTests/SqlAcceptance/ProductSqlSnapshot.cs",
        "tools/GoymenERP.CariAcceptance006/ProductAcceptanceFactory.cs",
        "tools/GoymenERP.CariAcceptance006/ProductEnvironment.cs",
        "tools/GoymenERP.CariAcceptance006/ProductEnvironmentEvidence.cs",
        "tools/GoymenERP.CariAcceptance006/ProductEnvironmentSettings.cs",
    ];

    private ProductEnvironmentSettings(byte[] bytes, string hash, JsonElement contract)
    {
        ContractBytes = bytes.ToArray(); ContractHash = hash; Contract = contract.Clone();
    }
    public byte[] ContractBytes { get; }
    public string ContractHash { get; }
    public JsonElement Contract { get; }
    public string RunId => Contract.GetProperty("runId").GetString()!;
    public string InstanceId => Contract.GetProperty("instanceId").GetString()!;
    public string DatabaseName => Contract.GetProperty("databaseName").GetString()!;
    public string EvidenceRoot => Path.Combine(SourceBinding.ProductWorktree, "artifacts/product-u01-u02/acceptance", RunId);
    public string PrivateRoot => Path.Combine(SourceBinding.ProductWorktree, "secrets/product-u01-u02", RunId);
    public string BrowserPrefix => Contract.GetProperty("browserPrefix").GetString()!;
    public JsonElement Source => Contract.GetProperty("source");

    public static ProductEnvironmentSettings Parse(byte[] bytes, string externallyAcceptedHash)
    {
        if (!Regex.IsMatch(externallyAcceptedHash, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)
            || SourceBinding.Hash(bytes) != externallyAcceptedHash)
            throw new InvalidOperationException("Product accepted-contract bytes do not match the external coordinator hash.");
        using var document = JsonDocument.Parse(bytes);
        var c = document.RootElement;
        var names = c.EnumerateObject().Select(p => p.Name).ToArray();
        string[] expected = ["contractId", "taskId", "environmentName", "runId", "instanceId", "acceptanceRoot",
            "apiBaseUrl", "sqlEndpoint", "uiOrigin", "databaseName", "composeProject", "containerName", "volumeName",
            "networkName", "actor", "actorId", "unauthorizedActor", "unauthorizedActorId", "companyId", "codeGroupId",
            "browserPrefix", "source", "erpAccepted", "productionActivated"];
        if (!names.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("Product accepted contract has missing, duplicate or unknown fields.");
        string S(string key) => c.GetProperty(key).GetString() ?? "";
        var run = S("runId");
        if (!Regex.IsMatch(run, "\\Aproduct-u0102-[0-9a-f]{12}\\z", RegexOptions.CultureInvariant)
            || !Guid.TryParseExact(S("instanceId"), "D", out var instance) || instance == Guid.Empty)
            throw new InvalidOperationException("Product run/instance is invalid.");
        if (S("contractId") != "PRODUCT-U0102-L-v1" || S("taskId") != "U-01-U-02"
            || S("environmentName") != "Testing" || S("acceptanceRoot") != Path.Combine(OwnerRoot, run)
            || S("apiBaseUrl") != ApiBaseUrl || S("sqlEndpoint") != SqlEndpoint || S("uiOrigin") != "http://127.0.0.1:26173"
            || S("databaseName") != "GoymenERP_Product_U0102_" + run[^12..]
            || S("composeProject") != run || S("containerName") != run + "-sql" || S("volumeName") != run + "-data"
            || S("networkName") != run + "-network" || S("actor") != Actor || S("actorId") != ActorId.ToString("D")
            || S("unauthorizedActor") != UnauthorizedActor || S("unauthorizedActorId") != UnauthorizedActorId.ToString("D")
            || S("companyId") != CompanyId.ToString("D") || S("codeGroupId") != CodeGroupId.ToString("D")
            || S("browserPrefix") != "PRODUCT-U0102-" + run[^12..]
            || c.GetProperty("erpAccepted").ValueKind != JsonValueKind.False
            || c.GetProperty("productionActivated").ValueKind != JsonValueKind.False)
            throw new InvalidOperationException("Product contract differs from the isolated source profile.");
        var source = c.GetProperty("source");
        if (source.GetProperty("worktree").GetString() != SourceBinding.ProductWorktree
            || !source.GetProperty("fixturePaths").EnumerateArray().Select(p => p.GetString()).SequenceEqual(FixturePaths))
            throw new InvalidOperationException("Product fixture source inventory differs from the reviewed profile.");
        return new(bytes, externallyAcceptedHash, c);
    }

    public static ProductEnvironmentSettings Load()
    {
        if (!string.Equals(Path.GetFullPath(Environment.CurrentDirectory), SourceBinding.ProductWorktree, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Product operation requires the exact Shared Backend worktree.");
        var path = Environment.GetEnvironmentVariable("GOYMEN_PRODUCT_ACCEPTED_CONTRACT") ?? "";
        var hash = Environment.GetEnvironmentVariable("GOYMEN_PRODUCT_ACCEPTED_CONTRACT_SHA256") ?? "";
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("Product accepted contract path must be explicit.");
        var settings = Parse(File.ReadAllBytes(path), hash);
        if (Path.GetFullPath(path) != Path.Combine(OwnerRoot, settings.RunId, "accepted-contract.json")
            && Path.GetFullPath(path) != Path.Combine(settings.EvidenceRoot, "accepted-contract.json"))
            throw new InvalidOperationException("Product accepted contract must be at its fixed owner or publisher path.");
        return settings;
    }

    public JsonElement VerifySource()
    {
        // This known historical frontend omits origin headers. It may be captured
        // for source analysis, never activated as a browser-ready environment.
        if (Source.GetProperty("frontendHead").GetString() == "1c48f609b83caa1118b0b274261678b6857088fa")
            throw new InvalidOperationException("Product frontend transport correction and its final admitted pin are still required.");
        var observed = SourceBinding.CaptureProductSource(Source.GetProperty("backendDependency").GetString()!,
            Source.GetProperty("frontendWorktree").GetString()!, Source.GetProperty("frontendDiffBase").GetString()!, FixturePaths);
        SourceBinding.RequireProductSourceMatch(Source, observed);
        return observed;
    }

    public RunSettings LifecycleIdentity => new()
    {
        RunId = RunId, InstanceId = InstanceId, ApiBaseUrl = ApiBaseUrl, ApiPort = ApiPort,
    }; // Used only by the existing pure lifecycle reader, never Cari Validate/DB setup.

    public string DatabaseConnection()
    {
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(PrivateRoot, "password.dpapi")), null, DataProtectionScope.CurrentUser);
        try { return new SqlConnectionStringBuilder { DataSource = SqlEndpoint, InitialCatalog = DatabaseName,
            UserID = "sa", Password = Encoding.UTF8.GetString(bytes), Encrypt = true,
            TrustServerCertificate = true, ConnectTimeout = 15, PersistSecurityInfo = false }.ConnectionString; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Write(string name, object value) => WriteBytes(name, JsonSerializer.SerializeToUtf8Bytes(value, Evidence.Json));
    public void WriteBytes(string name, byte[] bytes)
    {
        if (Path.GetFileName(name) != name) throw new InvalidOperationException("Product evidence filename is not a basename.");
        Directory.CreateDirectory(EvidenceRoot);
        var path = Path.Combine(EvidenceRoot, name);
        using (var stream = new FileStream(path + ".tmp", FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(bytes); stream.Flush(true); }
        File.Move(path + ".tmp", path, overwrite: false);
    }
}
