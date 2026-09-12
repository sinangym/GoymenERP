using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoymenERP.CariAcceptance006;

public static partial class SourceBinding
{
    public const string ProductWorktree = @"C:\src\GoymenERP-product-owner-boundary-20260912";
    public const string ProductFrontendWorktree = @"C:\src\GoymenERP\.claude\worktrees\urun-frontend-02";
    public const string ProductFrontendHead = "e1646e2b8204c823e79478a6a28ba6d07da53e13";
    public const string ProductDependency = "c765e629c669698d41e6b32ac760c08dbf9caf45";

    // The source object of the EXISTING PRODUCT-U0102-L-v1 three-file contract.
    // Reuses C006's Git process, clean-source guard and inventory hash algorithms.
    // This read-only entry point is not a second bind/setup/start/stop lifecycle.
    public static JsonElement CaptureProductSource(string backendDependency, string frontendWorktree,
        string frontendDiffBase, IReadOnlyList<string> fixturePaths)
    {
        var fixtures = ValidateProductFixturePaths(fixturePaths);
        if (Path.GetFullPath(Environment.CurrentDirectory) != ProductWorktree
            || Path.GetFullPath(frontendWorktree) != ProductFrontendWorktree
            || !IsCommit(backendDependency) || !IsCommit(frontendDiffBase))
            throw new InvalidOperationException("Product source capture requires its exact worktrees and explicit commit pins.");

        var head = GitText(ProductWorktree, "rev-parse", "HEAD");
        var compiledHead = typeof(SourceBinding).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "C006SourceHead")?.Value ?? "";
        if (!IsCommit(head) || compiledHead != head)
            throw new InvalidOperationException("Product acceptance source and the locally compiled executable differ.");
        _ = Git(ProductWorktree, "merge-base", "--is-ancestor", backendDependency, head);
        var frontendHead = GitText(ProductFrontendWorktree, "rev-parse", "HEAD");
        if (frontendHead != ProductFrontendHead)
            throw new InvalidOperationException("Product frontend source differs from the coordinator's candidate.");
        _ = Git(ProductFrontendWorktree, "merge-base", "--is-ancestor", frontendDiffBase, frontendHead);
        var clean = IsBackendSourceClean(ProductWorktree)
            && Git(ProductFrontendWorktree, "diff", "--name-only", "HEAD", "--", "src/frontend/goymen-erp-web", "tests/e2e").Length == 0
            && Git(ProductFrontendWorktree, "ls-files", "--others", "--exclude-standard", "--", "src/frontend/goymen-erp-web", "tests/e2e").Length == 0;
        if (!clean) throw new InvalidOperationException("Product source/build inputs are not clean; preserve the candidate.");

        var migrationRows = GitText(ProductWorktree, "ls-tree", "-r", "HEAD", "--", "src/backend")
            .Split('\n').Where(line => line.Contains("/Migrations/", StringComparison.Ordinal)).ToArray();
        if (migrationRows.Length == 0) throw new InvalidOperationException("Product migration source inventory is empty.");
        foreach (var path in fixtures)
            _ = Git(ProductWorktree, "ls-files", "--error-unmatch", "--", path);
        var fixtureInventory = string.Join('\n', fixtures.Select(path => path + "="
            + Hash(File.ReadAllBytes(Path.Combine(ProductWorktree, path))))) + "\n";
        var artifacts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(AppContext.BaseDirectory, "GoymenERP*.dll"))
            artifacts.Add(Path.GetFileName(path), Hash(File.ReadAllBytes(path)));
        if (artifacts.Count == 0) throw new InvalidOperationException("Product local build has no owned DLL artifacts.");

        return JsonSerializer.SerializeToElement(new
        {
            worktree = ProductWorktree,
            acceptanceHead = head,
            compiledHead,
            backendDependency,
            productDependency = ProductDependency,
            backendTree = GitText(ProductWorktree, "rev-parse", "HEAD:src/backend"),
            openApiSha256 = Hash(File.ReadAllBytes(Path.Combine(ProductWorktree, "contracts/openapi/goymen-erp-v1.json"))),
            migrationSourcesSha256 = Hash(Encoding.UTF8.GetBytes(string.Join('\n', migrationRows) + "\n")),
            frontendWorktree = ProductFrontendWorktree,
            frontendHead,
            frontendDiffBase,
            frontendDiffSha256 = Hash(Git(ProductFrontendWorktree, "diff", "--binary", "--full-index", "--no-color",
                "--no-ext-diff", "--no-textconv", frontendDiffBase, frontendHead, "--", "src/frontend/goymen-erp-web", "tests/e2e")),
            fixtureSha256 = Hash(Encoding.UTF8.GetBytes(fixtureInventory)),
            fixturePaths = fixtures,
            sourcesClean = clean,
            diskArtifacts = artifacts,
        });
    }

    public static string[] ValidateProductFixturePaths(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var frozen = paths.ToArray();
        if (frozen.Length == 0 || !frozen.SequenceEqual(frozen.Order(StringComparer.Ordinal))
            || frozen.Distinct(StringComparer.Ordinal).Count() != frozen.Length
            || frozen.Any(path => string.IsNullOrWhiteSpace(path) || path.Contains('\\')
                || path.Contains(':') || path.Split('/').Any(part => part is "" or "." or "..")
                || !(path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".json", StringComparison.Ordinal))
                || !(path.StartsWith("tools/GoymenERP.CariAcceptance006/", StringComparison.Ordinal)
                    || path.StartsWith("tools/product-u01-u02/", StringComparison.Ordinal)
                    || path.StartsWith("tests/GoymenERP.Product.UnitTests/SqlAcceptance/", StringComparison.Ordinal)
                    || path == "tests/GoymenERP.Api.IntegrationTests/ProductGlobalRuntimeSqlServerRelationalAcceptanceTests.cs")))
            throw new InvalidOperationException("Product fixture paths must be the nonempty, ordinal-sorted, distinct reviewed source inventory.");
        return frozen;
    }

    public static void RequireProductSourceMatch(JsonElement accepted, JsonElement observed)
    {
        // Property order is immaterial; missing/extra fields, any changed source
        // value and any changed DLL name/hash remain a mismatch.
        if (!JsonElement.DeepEquals(accepted, observed))
            throw new InvalidOperationException("Product source/build differs from the accepted contract.");
    }

    private static bool IsCommit(string value) =>
        Regex.IsMatch(value, "\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant);
}
