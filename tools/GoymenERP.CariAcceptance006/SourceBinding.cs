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

public static class SourceBinding
{
    public const string FrontendRoot = @"C:\src\GoymenERP-claude";
    public const string FrontendHead = "6bfc21eec43f02f3c8f2e0d86fd124c0f387bff4";
    public const string FrontendBase = "060da63f2644cb271d78d9eab8b7a6985d544ad1";
    public const string FrontendDiff = "b9e9354e971da26bbd5a0743dcefc78c2f3b4a7e4cbb6d59d74793c7c3b25c11";
    public const string BackendTree = "4e0b1a5d6a1116ee2ba76df132aeec6e8b9c99d5";
    public const string OpenApiHash = "062c06175916295bc97a438adea6fd01cdb608413c58501aa62208f87038a829";
    public const string MigrationHash = "1ec8222d998cb59891f437fc1ce4f62f954203508f3b2cffd4a00d97b1933e44";
    public const string ClientContractHeader = "X-ERP-Counterparty-Contract";
    public const string ClientContractVersion = "optional-trade-roles-v1";

    private static readonly string[] BackendBuildInputs =
    [
        "src/backend", "tools/GoymenERP.CariAcceptance006", "tests/GoymenERP.CariAcceptance006.Tests",
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
        var frontendDiff = Hash(Git(FrontendRoot, "diff", "--binary", "--full-index", "--no-color",
            "--no-ext-diff", "--no-textconv", FrontendBase, frontendHead, "--",
            "src/frontend/goymen-erp-web", "tests/e2e"));
        var migrations = GitText(RunSettings.ApprovedRoot, "ls-tree", "-r", "HEAD", "--", "src/backend")
            .Split('\n').Where(line => line.Contains("/Migrations/", StringComparison.Ordinal));
        var fixture = string.Join('\n', new[]
        {
            "tools/GoymenERP.CariAcceptance006/AcceptanceFactory.cs",
            "tools/GoymenERP.CariAcceptance006/AcceptanceProfile.cs",
            "tools/cari-acceptance-006/run.json",
        }.Select(path => path + "=" + Hash(File.ReadAllBytes(Path.Combine(RunSettings.ApprovedRoot, path))))) + "\n";
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
