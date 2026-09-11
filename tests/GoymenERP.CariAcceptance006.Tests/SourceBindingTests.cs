using GoymenERP.CariAcceptance006;

namespace GoymenERP.CariAcceptance006.Tests;

public sealed class SourceBindingTests
{
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
}
