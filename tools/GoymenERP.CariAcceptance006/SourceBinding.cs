using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoymenERP.CariAcceptance006;

public sealed record SourceSnapshot(
    string Worktree, string AcceptanceHead, string CompiledHead,
    string BackendDependency, string BackendTree, string OpenApiSha256, string MigrationSourcesSha256,
    string FrontendWorktree, string FrontendHead, string FrontendDiffSha256,
    string FixtureSha256, string RunId, string InstanceId, string DatabaseName,
    string ApiBaseUrl, string SqlEndpoint, bool SourcesClean,
    SortedDictionary<string, string> DiskArtifacts);

public static partial class SourceBinding
{
    public const string ContractPath = RunSettings.ApprovedRoot + @"\artifacts\cari-acceptance-006\account-status-20260913\c006-7f12ed40ff9a-contract.json";
    public const string FrontendRoot = @"C:\src\GoymenERP-claude";
    public const string FrontendHead = "fbaeaa4c4560f607a10b71ad8adb03cc0099d045";
    public const string FrontendBase = "3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f";
    public const string FrontendDiff = "6f4f733f98bb0ff56137fe41177efdbf5641ba8e0832bc54c2b56caea36c5094";
    public const string BackendTree = "330b62e0d1f5f86a8aeff5d52308fccc6bcf7272";
    public const string OpenApiHash = "baa0fdde0dcffe31b9fdf1e0e3960b2ecaf0f8ad38ae595622497c8796511fa3";
    public const string MigrationHash = "c4fc980e3d58c564f8818caee4f7523e28256be9e7e6aecea6f0a266c32e3183";
    public const string ClientContractHeader = "X-ERP-Counterparty-Contract";
    public const string ClientContractVersion = "optional-trade-roles-v1";

    // Existing b9 browser verifier recomputes this exact three-file recipe.
    // Other sources remain bound by the clean commit and tested runtime DLL hashes.
    public static IReadOnlyList<string> CariFixturePaths { get; } = Array.AsReadOnly(new[]
    {
        "tools/GoymenERP.CariAcceptance006/AcceptanceFactory.cs",
        "tools/GoymenERP.CariAcceptance006/AcceptanceProfile.cs",
        "tools/cari-acceptance-006/run.json",
    });

    private static readonly string[] BackendBuildInputs =
    [
        "src/backend", "tools/GoymenERP.CariAcceptance006", "tests/GoymenERP.CariAcceptance006.Tests",
        "tests/GoymenERP.Product.UnitTests/SqlAcceptance",
        ":(top,glob)*.props", ":(top,glob)*.targets", ":(top,glob)*.rsp", "global.json", "NuGet.Config",
        ":(exclude,glob)**/bin/**", ":(exclude,glob)**/obj/**",
    ];

    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("C006 git did not start.");
        using var bytes = new MemoryStream();
        // Drain both streams and bound even a broken local Git process.
        var output = process.StandardOutput.BaseStream.CopyToAsync(bytes);
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new InvalidOperationException("C006 source observation timed out.");
        }
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException("C006 source observation failed.");
        return bytes.ToArray();
    }

    private static string GitText(string root, params string[] arguments) =>
        Encoding.UTF8.GetString(Git(root, arguments)).TrimEnd('\r', '\n');

    public static bool IsBackendSourceClean(string root) =>
        Git(root, "diff", "--name-only", "HEAD", "--").Length == 0
        // Ownership is not a build boundary: also reject untracked inputs outside this lane.
        && Git(root, "ls-files", "--others", "--exclude-standard", "--").Length == 0
        // Git-ignore does not exclude SDK compile/import inputs. Only standard build outputs
        // are omitted from this second observation of the C006 backend build roots.
        && Git(root, ["ls-files", "--others", "--", .. BackendBuildInputs]).Length == 0;

    public static SourceSnapshot Capture(RunSettings identity)
    {
        identity.Validate("Testing");
        if (!string.Equals(Path.GetFullPath(Environment.CurrentDirectory), RunSettings.ApprovedRoot,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("C006 source observation requires its exact worktree.");
        var head = GitText(RunSettings.ApprovedRoot, "rev-parse", "HEAD");
        var compiledHead = typeof(SourceBinding).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "C006SourceHead")?.Value ?? "";
        var backendTree = GitText(RunSettings.ApprovedRoot, "rev-parse", "HEAD:src/backend");
        var frontendHead = GitText(FrontendRoot, "rev-parse", "HEAD");
        var contractBytes = File.ReadAllBytes(ContractPath);
        _ = ValidateAndHashExternalContract(contractBytes, head, compiledHead);
        var frontendDiff = Hash(Git(FrontendRoot, "diff", "--binary", "--full-index", "--no-color",
            "--no-ext-diff", "--no-textconv", FrontendBase, frontendHead, "--",
            "src/frontend/goymen-erp-web", "tests/e2e"));
        var migrations = GitText(RunSettings.ApprovedRoot, "ls-tree", "-r", "HEAD", "--", "src/backend")
            .Split('\n').Where(line => line.Contains("/Migrations/", StringComparison.Ordinal));
        var fixture = string.Join('\n', CariFixturePaths.Select(path => path + "="
            + Hash(File.ReadAllBytes(Path.Combine(RunSettings.ApprovedRoot, path))))) + "\n";
        var clean = IsBackendSourceClean(RunSettings.ApprovedRoot)
            && Git(FrontendRoot, "diff", "--name-only", "HEAD", "--", "src/frontend/goymen-erp-web", "tests/e2e").Length == 0
            && Git(FrontendRoot, "ls-files", "--others", "--exclude-standard", "--", "src/frontend/goymen-erp-web", "tests/e2e").Length == 0;
        var artifacts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(AppContext.BaseDirectory, "GoymenERP*.dll"))
            artifacts.Add(Path.GetFileName(path), Hash(File.ReadAllBytes(path)));
        var snapshot = new SourceSnapshot(RunSettings.ApprovedRoot, head, compiledHead,
            RunSettings.ExpectedHead, backendTree,
            Hash(File.ReadAllBytes(Path.Combine(RunSettings.ApprovedRoot, "contracts/openapi/goymen-erp-v1.json"))),
            Hash(Encoding.UTF8.GetBytes(string.Join('\n', migrations) + "\n")),
            FrontendRoot, frontendHead, frontendDiff, Hash(Encoding.UTF8.GetBytes(fixture)),
            identity.RunId, identity.InstanceId, identity.DatabaseName, identity.ApiBaseUrl, identity.SqlEndpoint,
            clean, artifacts);
        Validate(snapshot, snapshot);
        return snapshot;
    }

    public static void Validate(SourceSnapshot recorded, SourceSnapshot current)
    {
        var identity = new RunSettings();
        foreach (var value in new[] { recorded, current })
        {
            if (value.Worktree != RunSettings.ApprovedRoot || value.FrontendWorktree != FrontendRoot
                || !Regex.IsMatch(value.AcceptanceHead, "\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant)
                || value.CompiledHead != value.AcceptanceHead || value.BackendDependency != RunSettings.ExpectedHead
                || value.BackendTree != BackendTree || value.OpenApiSha256 != OpenApiHash
                || value.MigrationSourcesSha256 != MigrationHash || value.FrontendHead != FrontendHead
                || value.FrontendDiffSha256 != FrontendDiff || !value.SourcesClean
                || value.RunId != identity.RunId || value.InstanceId != identity.InstanceId
                || value.DatabaseName != identity.DatabaseName || value.ApiBaseUrl != identity.ApiBaseUrl
                || value.SqlEndpoint != identity.SqlEndpoint
                || !Regex.IsMatch(value.FixtureSha256, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)
                || value.DiskArtifacts.Count == 0
                || value.DiskArtifacts.Any(item => !item.Key.StartsWith("GoymenERP", StringComparison.Ordinal)
                    || Path.GetFileName(item.Key) != item.Key || !item.Key.EndsWith(".dll", StringComparison.Ordinal)
                    || !Regex.IsMatch(item.Value, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)))
                throw new InvalidOperationException("C006 source/run/build/fixture binding is not the exact preparation candidate.");
        }
        // JSON deserialization recreates SortedDictionary with the current-culture comparer.
        // Canonicalize ordering only; every source field, DLL name and hash still must match.
        var recordedCanonical = recorded with
        {
            DiskArtifacts = new(recorded.DiskArtifacts, StringComparer.Ordinal),
        };
        var currentCanonical = current with
        {
            DiskArtifacts = new(current.DiskArtifacts, StringComparer.Ordinal),
        };
        if (JsonSerializer.Serialize(recordedCanonical, Evidence.Json) != JsonSerializer.Serialize(currentCanonical, Evidence.Json))
            throw new InvalidOperationException("C006 source or build changed since binding; preserve the previous run.");
    }

    public static void ValidateExternalContract(JsonElement contract, string head, string compiledHead)
    {
        var codex = contract.GetProperty("codex");
        var claude = contract.GetProperty("claude");
        var ports = contract.GetProperty("suggestedPorts");
        if (contract.GetProperty("id").GetString() != "CARI-AB-20260905-006-009"
            || codex.GetProperty("taskId").GetString() != "C-DESKTOP-006"
            || codex.GetProperty("cwd").GetString() != RunSettings.ApprovedRoot
            || codex.GetProperty("baseHead").GetString() != RunSettings.ExpectedHead
            || !Regex.IsMatch(head, "\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant)
            || codex.GetProperty("acceptanceHead").GetString() != head
            || codex.GetProperty("compiledHead").GetString() != compiledHead || compiledHead != head
            || claude.GetProperty("baseHead").GetString() != FrontendHead
            || claude.GetProperty("frontendDiffBase").GetString() != FrontendBase
            || claude.GetProperty("frontendDiffSha256").GetString() != FrontendDiff
            || ports.GetProperty("api").GetInt32() != 25080 || ports.GetProperty("sql").GetInt32() != 25433
            || ports.GetProperty("ui").GetInt32() != 25173)
            throw new InvalidOperationException("C006 external contract differs from actual source/build pins.");
    }

    public static string ValidateAndHashExternalContract(byte[] bytes, string head, string compiledHead)
    {
        using var contract = JsonDocument.Parse(bytes);
        ValidateExternalContract(contract.RootElement, head, compiledHead);
        return Hash(bytes); // The exact validated snapshot, never a second filesystem read.
    }

    public static SourceSnapshot Verify(RunSettings identity)
    {
        var recorded = JsonSerializer.Deserialize<SourceSnapshot>(File.ReadAllText(
            Path.Combine(RunSettings.ApprovedEvidence, "source-binding.json")))
            ?? throw new InvalidOperationException("C006 source binding is missing.");
        Validate(recorded, Capture(identity));
        return recorded;
    }

    public static void Bind(RunSettings identity)
    {
        var snapshot = Capture(identity);
        if (Directory.Exists(RunSettings.ApprovedEvidence))
            throw new InvalidOperationException("C006 evidence directory already exists; no previous run is reused.");
        Directory.CreateDirectory(RunSettings.ApprovedEvidence);
        Evidence.Write("source-binding.json", snapshot);
    }
}
