using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace GoymenERP.ArchitectureTests;

public sealed class Erp000mEvidenceInputPackGuardTests
{
    private static readonly IReadOnlyDictionary<string, string> ReviewedFiles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["docs/evidence/erp-000m/README.md"] =
                "253484D5CF85393DE16BFD3B6F81BE947F7FECA9EB70187F07DF29F850BE6190",
            ["docs/evidence/erp-000m/ENVIRONMENT_INVENTORY_TEMPLATE.md"] =
                "31972E106E1F3C9A0912AA2A33F2311A5EE2377D477657BECF735B77D4FBCC23",
            ["docs/evidence/erp-000m/WORKLOAD_PROFILE_TEMPLATE.json"] =
                "16E8AB632B24EDB8EF8EF9C61D823EC457DF4E996DF7E8F131A4FA66893389EE",
            ["docs/evidence/erp-000m/EVIDENCE_MANIFEST_TEMPLATE.json"] =
                "B04A191FF137512071ACE705056059A2314DC2F71072DF8BDFB671042290E97E",
            ["tests/erp000m_evidence/validate_erp000m_evidence.py"] =
                "0B3DEA99E0642A3E6E72425DC884E9DB87DAA8545015001C17838880A26E31B4",
            ["tests/erp000m_evidence/test_validate_erp000m_evidence.py"] =
                "BE376175CA2B21DF7C7C314732F4DB532CD0E217BCFE0DC40CFECFB29A248F5A"
        };

    [Fact]
    public void Reviewed_evidence_input_pack_matches_exact_file_set_and_hashes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var actualFiles = ReadActualReviewedFiles(repositoryRoot);

        var errors = ValidateReviewedFiles(ReviewedFiles, actualFiles);

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void Unreviewed_file_drift_is_rejected()
    {
        var actualFiles = new Dictionary<string, string>(ReviewedFiles, StringComparer.Ordinal)
        {
            ["docs/evidence/erp-000m/unreviewed-input.txt"] = new string('A', 64)
        };

        var errors = ValidateReviewedFiles(ReviewedFiles, actualFiles);

        Assert.Contains(
            errors,
            error => error.Contains("Unexpected reviewed-surface files", StringComparison.Ordinal));
    }

    [Fact]
    public void Canonical_hash_drift_is_rejected()
    {
        var actualFiles = new Dictionary<string, string>(ReviewedFiles, StringComparer.Ordinal)
        {
            ["docs/evidence/erp-000m/README.md"] = new string('0', 64)
        };

        var errors = ValidateReviewedFiles(ReviewedFiles, actualFiles);

        Assert.Contains(
            errors,
            error => error.Contains("Canonical SHA-256 drift", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_reviewed_file_is_rejected()
    {
        var actualFiles = new Dictionary<string, string>(ReviewedFiles, StringComparer.Ordinal);
        actualFiles.Remove("tests/erp000m_evidence/validate_erp000m_evidence.py");

        var errors = ValidateReviewedFiles(ReviewedFiles, actualFiles);

        Assert.Contains(
            errors,
            error => error.Contains("Missing reviewed-surface files", StringComparison.Ordinal));
    }

    [Fact]
    public void Tracked_ignored_python_cache_path_is_rejected()
    {
        var actualFiles = new Dictionary<string, string>(ReviewedFiles, StringComparer.Ordinal)
        {
            ["tests/erp000m_evidence/__pycache__/tracked.pyc"] = new string('A', 64)
        };

        var errors = ValidateReviewedFiles(ReviewedFiles, actualFiles);

        Assert.Contains(
            errors,
            error => error.Contains("Unexpected reviewed-surface files", StringComparison.Ordinal));
    }

    [Fact]
    public void Case_drift_is_rejected_as_missing_and_unexpected()
    {
        var actualFiles = new Dictionary<string, string>(ReviewedFiles, StringComparer.Ordinal);
        var canonicalPath = "docs/evidence/erp-000m/README.md";
        var hash = actualFiles[canonicalPath];
        actualFiles.Remove(canonicalPath);
        actualFiles["docs/evidence/erp-000m/readme.md"] = hash;

        var errors = ValidateReviewedFiles(ReviewedFiles, actualFiles);

        Assert.Contains(
            errors,
            error => error.Contains("Missing reviewed-surface files", StringComparison.Ordinal));
        Assert.Contains(
            errors,
            error => error.Contains("Unexpected reviewed-surface files", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> ValidateReviewedFiles(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        var errors = new List<string>();
        var missing = expected.Keys.Except(actual.Keys, StringComparer.Ordinal).Order().ToArray();
        var unexpected = actual.Keys.Except(expected.Keys, StringComparer.Ordinal).Order().ToArray();
        var mismatched = expected.Keys
            .Intersect(actual.Keys, StringComparer.Ordinal)
            .Where(path => !string.Equals(expected[path], actual[path], StringComparison.Ordinal))
            .Order()
            .ToArray();

        if (missing.Length > 0)
        {
            errors.Add(
                "Missing reviewed-surface files:\n"
                + string.Join("\n", missing.Select(path => $"- {path}")));
        }

        if (unexpected.Length > 0)
        {
            errors.Add(
                "Unexpected reviewed-surface files:\n"
                + string.Join("\n", unexpected.Select(path => $"- {path}")));
        }

        if (mismatched.Length > 0)
        {
            errors.Add(
                "Canonical SHA-256 drift:\n"
                + string.Join(
                    "\n",
                    mismatched.Select(
                        path => $"- {path}: expected {expected[path]}, got {actual[path]}")));
        }

        return errors;
    }

    private static IReadOnlyDictionary<string, string> ReadActualReviewedFiles(
        string repositoryRoot)
    {
        var trackedEntries = ReadTrackedIndexEntries(repositoryRoot);
        var untrackedEntries = ReadUntrackedNonIgnoredEntries(repositoryRoot);
        var repositoryPaths = trackedEntries.Keys
            .Concat(untrackedEntries)
            .ToHashSet(StringComparer.Ordinal);
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var repositoryPath in repositoryPaths)
        {
            if (trackedEntries.TryGetValue(repositoryPath, out var gitMode)
                && gitMode != "100644")
            {
                actual[repositoryPath] = $"<NON_REGULAR_GIT_MODE:{gitMode}>";
                continue;
            }

            var fullPath = Path.Combine(
                repositoryRoot,
                repositoryPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                actual[repositoryPath] = "<MISSING_WORKTREE_FILE>";
                continue;
            }

            var attributes = File.GetAttributes(fullPath);
            if (attributes.HasFlag(FileAttributes.Directory)
                || attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                actual[repositoryPath] = "<NON_REGULAR_WORKTREE_ENTRY>";
                continue;
            }

            actual[repositoryPath] = ComputeCanonicalSha256(fullPath);
        }

        return actual;
    }

    private static IReadOnlyDictionary<string, string> ReadTrackedIndexEntries(
        string repositoryRoot)
    {
        var output = RunGit(
            repositoryRoot,
            "ls-files",
            "--cached",
            "--stage",
            "-z");
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tabIndex = record.IndexOf('\t');
            if (tabIndex <= 0)
            {
                throw new InvalidDataException($"Unexpected git ls-files record: {record}");
            }

            var metadata = record[..tabIndex];
            var metadataParts = metadata.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadataParts.Length != 3)
            {
                throw new InvalidDataException($"Unexpected git index metadata: {metadata}");
            }

            var repositoryPath = NormalizeRepositoryPath(record[(tabIndex + 1)..]);
            if (!IsOwnedSurfaceCandidate(repositoryPath))
            {
                continue;
            }

            if (!entries.TryAdd(repositoryPath, metadataParts[0]))
            {
                throw new InvalidDataException(
                    $"Duplicate git index path on reviewed surface: {repositoryPath}");
            }
        }

        return entries;
    }

    private static IReadOnlyCollection<string> ReadUntrackedNonIgnoredEntries(
        string repositoryRoot)
    {
        var output = RunGit(
            repositoryRoot,
            "ls-files",
            "--others",
            "--exclude-standard",
            "-z");
        return output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeRepositoryPath)
            .Where(IsOwnedSurfaceCandidate)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsOwnedSurfaceCandidate(string repositoryPath)
    {
        return IsPathAtOrBelow(repositoryPath, "docs/evidence/erp-000m")
               || IsPathAtOrBelow(repositoryPath, "tests/erp000m_evidence");
    }

    private static bool IsPathAtOrBelow(string repositoryPath, string root)
    {
        return repositoryPath.Equals(root, StringComparison.OrdinalIgnoreCase)
               || repositoryPath.StartsWith($"{root}/", StringComparison.OrdinalIgnoreCase);
    }

    private static string RunGit(string repositoryRoot, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Failed to start git.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed ({process.ExitCode}): "
                + standardError.Trim());
        }

        return standardOutput;
    }

    private static string ComputeCanonicalSha256(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var text = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetString(bytes);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static string NormalizeRepositoryPath(string path)
    {
        return path.Replace('\\', '/').Trim();
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

        throw new DirectoryNotFoundException("GoymenERP repository root was not found.");
    }
}
