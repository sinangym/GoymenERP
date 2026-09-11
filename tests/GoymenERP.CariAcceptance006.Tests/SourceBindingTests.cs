using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using GoymenERP.CariAcceptance006;

namespace GoymenERP.CariAcceptance006.Tests;

public sealed class SourceBindingTests
{
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    [InlineData("")]
    public void JsonRoundTrip_CultureSortedArtifactsPreserveExactBinding(string culture)
    {
        var current = WithPrefixArtifacts();
        var recorded = RoundTrip(current, culture);
        Assert.False(recorded.DiskArtifacts.Keys.SequenceEqual(current.DiskArtifacts.Keys, StringComparer.Ordinal));
        Assert.NotEqual(JsonSerializer.Serialize(recorded, Evidence.Json), JsonSerializer.Serialize(current, Evidence.Json));

        SourceBinding.Validate(recorded, current);
        SourceBinding.Validate(current, recorded);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("missing")]
    [InlineData("renamed")]
    [InlineData("extra")]
    public void JsonRoundTrip_ArtifactDriftStillRejectsBothDirections(string change)
    {
        var current = WithPrefixArtifacts();
        var recorded = RoundTrip(current, "tr-TR");
        var changed = current with { DiskArtifacts = new(current.DiskArtifacts, StringComparer.Ordinal) };
        const string name = "GoymenERP.BuildingBlocks.dll";
        switch (change)
        {
            case "hash":
                changed.DiskArtifacts[name] = new string('d', 64);
                break;
            case "missing":
                changed.DiskArtifacts.Remove(name);
                break;
            case "renamed":
                changed.DiskArtifacts.Remove(name);
                changed.DiskArtifacts["GoymenERP.BuildingBlocks.Renamed.dll"] = current.DiskArtifacts[name];
                break;
            case "extra":
                changed.DiskArtifacts["GoymenERP.Extra.dll"] = new string('e', 64);
                break;
            default:
                throw new InvalidOperationException("Unknown artifact drift fixture.");
        }

        Assert.Throws<InvalidOperationException>(() => SourceBinding.Validate(recorded, changed));
        Assert.Throws<InvalidOperationException>(() => SourceBinding.Validate(changed, recorded));
    }

    private static SourceSnapshot WithPrefixArtifacts() => Valid() with
    {
        DiskArtifacts = new(StringComparer.Ordinal)
        {
            ["GoymenERP.CariAcceptance006.dll"] = new string('c', 64),
            ["GoymenERP.BuildingBlocks.SqlServer.dll"] = new string('a', 64),
            ["GoymenERP.BuildingBlocks.dll"] = new string('b', 64),
        },
    };

    private static SourceSnapshot RoundTrip(SourceSnapshot source, string culture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            return JsonSerializer.Deserialize<SourceSnapshot>(JsonSerializer.Serialize(source, Evidence.Json))
                ?? throw new InvalidOperationException("Round-trip fixture did not deserialize.");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("src/backend/GoymenERP.Api/Injected.cs")]
    [InlineData("src/backend/GoymenERP.Api/Injected.local.cs")]
    [InlineData("tools/GoymenERP.CariAcceptance006/Injected.cs")]
    [InlineData("tests/GoymenERP.CariAcceptance006.Tests/Injected.cs")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("Directory.Build.local.props")]
    [InlineData("build/Injected.targets")]
    public void UntrackedBuildInputs_AreRejectedEvenOutsideOwnedPathsOrWhenIgnored(string path)
    {
        using var checkout = new SourceCheckout();
        Assert.True(SourceBinding.IsBackendSourceClean(checkout.Root));
        checkout.Write(path, "untracked build input");
        Assert.False(SourceBinding.IsBackendSourceClean(checkout.Root));
    }

    [Fact]
    public void TrackedAndStagedSourceChanges_AreRejected()
    {
        using var checkout = new SourceCheckout();
        checkout.Write("src/backend/Tracked.cs", "changed tracked input");
        Assert.False(SourceBinding.IsBackendSourceClean(checkout.Root));
        checkout.Git("add", "--", "src/backend/Tracked.cs");
        Assert.False(SourceBinding.IsBackendSourceClean(checkout.Root));
    }

    [Fact]
    public void IgnoredBuildOutputsAndExternalEvidence_DoNotDirtySourceInputs()
    {
        using var checkout = new SourceCheckout();
        foreach (var path in new[]
        {
            "src/backend/GoymenERP.Api/bin/generated.cs", "src/backend/GoymenERP.Api/obj/generated.cs",
            "tools/GoymenERP.CariAcceptance006/bin/generated.cs",
            "tests/GoymenERP.CariAcceptance006.Tests/obj/generated.cs", "artifacts/probe.json",
        }) checkout.Write(path, "generated output");
        Assert.True(SourceBinding.IsBackendSourceClean(checkout.Root));
    }

    [Fact]
    public void GitObservationFailure_IsNotAcceptedAsClean()
    {
        using var checkout = new SourceCheckout(empty: true);
        Assert.Throws<InvalidOperationException>(() => SourceBinding.IsBackendSourceClean(checkout.Root));
    }

    private static SourceSnapshot Valid()
    {
        var run = new RunSettings();
        return new(RunSettings.ApprovedRoot, new string('a', 40), new string('a', 40),
            RunSettings.ExpectedHead, SourceBinding.BackendTree, SourceBinding.OpenApiHash, SourceBinding.MigrationHash,
            SourceBinding.FrontendRoot, SourceBinding.FrontendHead, SourceBinding.FrontendDiff, new string('b', 64),
            run.RunId, run.InstanceId, run.DatabaseName, run.ApiBaseUrl, run.SqlEndpoint, true,
            new(StringComparer.Ordinal) { ["GoymenERP.CariAcceptance006.dll"] = new string('c', 64) });
    }

    [Fact]
    public void ExactSourceRunFixtureAndBuild_AreAccepted()
    {
        var source = Valid();
        SourceBinding.Validate(source, source);
    }

    public static TheoryData<SourceSnapshot> WrongBindings => new()
    {
        Valid() with { Worktree = @"C:\src\another-worktree" },
        Valid() with { AcceptanceHead = "not-a-commit" },
        Valid() with { CompiledHead = new string('d', 40) },
        Valid() with { BackendDependency = new string('d', 40) },
        Valid() with { BackendTree = new string('d', 40) },
        Valid() with { OpenApiSha256 = new string('d', 64) },
        Valid() with { MigrationSourcesSha256 = new string('d', 64) },
        Valid() with { FrontendHead = new string('d', 40) },
        Valid() with { FrontendDiffSha256 = new string('d', 64) },
        Valid() with { FrontendWorktree = @"C:\src\another-frontend" },
        Valid() with { SourcesClean = false },
        Valid() with { RunId = "old-run" },
        Valid() with { InstanceId = "old-instance" },
        Valid() with { DatabaseName = "GoymenERP" },
        Valid() with { ApiBaseUrl = "http://0.0.0.0:25080" },
        Valid() with { SqlEndpoint = "localhost,1433" },
        Valid() with { FixtureSha256 = "missing" },
        Valid() with { DiskArtifacts = new(StringComparer.Ordinal) },
        Valid() with { DiskArtifacts = new(StringComparer.Ordinal) { ["../GoymenERP.Api.dll"] = new string('c', 64) } },
    };

    [Theory]
    [MemberData(nameof(WrongBindings))]
    public void WrongSourceOrRun_RejectsBothRecordedAndObservedEvidence(SourceSnapshot wrong)
    {
        Assert.Throws<InvalidOperationException>(() => SourceBinding.Validate(Valid(), wrong));
        Assert.Throws<InvalidOperationException>(() => SourceBinding.Validate(wrong, Valid()));
    }

    [Fact]
    public void ChangedFixtureOrBuild_RejectsPreviouslyValidBinding()
    {
        var valid = Valid();
        Assert.Throws<InvalidOperationException>(() =>
            SourceBinding.Validate(valid, valid with { FixtureSha256 = new string('d', 64) }));
        Assert.Throws<InvalidOperationException>(() => SourceBinding.Validate(valid,
            valid with { DiskArtifacts = new(StringComparer.Ordinal) { ["GoymenERP.CariAcceptance006.dll"] = new string('d', 64) } }));
    }

    [Fact]
    public void NewHeadRequiresNewBinding_EvenWhenOtherSourcesAreUnchanged()
    {
        var current = Valid() with { AcceptanceHead = new string('d', 40), CompiledHead = new string('d', 40) };
        Assert.Throws<InvalidOperationException>(() => SourceBinding.Validate(Valid(), current));
    }

    [Fact]
    public void BrowserJourney_RequiresRolelessAccountsInBothCompanies()
    {
        var identity = new RunSettings();
        var before = new DatabaseSnapshot(identity.DatabaseName, true, new(0, 0, 0, 0, 0, 0), []);
        var partyId = Guid.NewGuid();
        var rows = new[]
        {
            new AccountRow(partyId, "CARI-TEST", identity.BrowserPrefix + " journey", Guid.Parse(identity.CompanyAId), 0, "1"),
            new AccountRow(partyId, "CARI-TEST", identity.BrowserPrefix + " journey", Guid.Parse(identity.CompanyBId), 0, "1"),
        };
        var after = new DatabaseSnapshot(identity.DatabaseName, true, before.Effects.AddJourney(1), rows);
        DatabaseEvidence.VerifyBrowserJourney(identity, before, after, 1);
        Assert.Throws<InvalidOperationException>(() => DatabaseEvidence.VerifyBrowserJourney(identity, before,
            after with { Rows = [rows[0], rows[1] with { Roles = 2 }] }, 1));
        Assert.Throws<InvalidOperationException>(() => DatabaseEvidence.VerifyBrowserJourney(identity, before,
            after with { Rows = [rows[0], rows[1] with { CompanyId = rows[0].CompanyId }] }, 1));
    }

    private sealed class SourceCheckout : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "goymen-c006-source-" + Guid.NewGuid().ToString("N"));

        public SourceCheckout(bool empty = false)
        {
            Directory.CreateDirectory(Root);
            Git("init", "--quiet");
            if (empty) return;
            Write(".gitignore", "**/bin/\n**/obj/\nartifacts/\n*.local.cs\n*.local.props\n");
            Write("src/backend/Tracked.cs", "// tracked fixture\n");
            Git("add", "--", ".gitignore", "src/backend/Tracked.cs");
            Git("-c", "user.name=C006 test", "-c", "user.email=c006@example.invalid",
                "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "C006 source fixture");
        }

        public void Write(string path, string contents)
        {
            var fullPath = Path.Combine(Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, contents);
        }

        public void Git(params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Fixture Git did not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new InvalidOperationException("Fixture Git timed out.");
            }
            Task.WhenAll(output, error).GetAwaiter().GetResult();
            Assert.True(process.ExitCode == 0, "Fixture Git failed: " + error.Result);
        }

        public void Dispose()
        {
            var root = Path.GetFullPath(Root);
            var ownedPrefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "goymen-c006-source-");
            if (!root.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing cleanup outside this test's temporary checkout.");
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }
}
