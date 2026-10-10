using GoymenERP.Modules.Documents.Application.Contracts;
using static GoymenERP.Documents.UnitTests.Harness;

namespace GoymenERP.Documents.UnitTests;

public sealed class DocumentVersionServiceTests
{
    private const string Read = DocumentsPermissionManifestV1.FilesRead;
    private const string Upload = DocumentsPermissionManifestV1.FilesUpload;
    private static readonly byte[] V1 = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31];
    private static readonly byte[] V2 = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x32];
    private static readonly byte[] V3 = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x33];
    private static readonly Guid Party = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Append_is_immutable_history_and_version_specific_content_is_preserved()
    {
        var world = World();
        var service = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload, Read);
        var original = (await service.Service.UploadAsync(Harness.Upload(Party, V1, "Sözleşme-v1.pdf"), service.Metadata("doc-v1"))).Document;

        var appended = await service.Service.AppendVersionAsync(original.Id,
            new AppendDocumentVersionRequest(1, "Sözleşme-v2.pdf", "application/pdf", V2), service.Metadata("doc-v2"));

        Assert.False(appended.IsReplay);
        Assert.Equal(2, appended.Document.VersionNo);
        var history = await service.Service.ListVersionsAsync(original.Id, null, null);
        Assert.Equal([2, 1], history.Versions.Select(version => version.VersionNo));
        Assert.Equal(["Sözleşme-v2.pdf", "Sözleşme-v1.pdf"], history.Versions.Select(version => version.FileName));
        Assert.Equal(V1, (await service.Service.GetVersionContentAsync(original.Id, 1)).Content);
        Assert.Equal(V2, (await service.Service.GetVersionContentAsync(original.Id, 2)).Content);
        Assert.Equal(V2, (await service.Service.GetContentAsync(original.Id)).Content);
        Assert.Single(world.Documents);
        Assert.Equal(2, world.Versions.Count);
    }

    [Fact]
    public async Task Replay_pins_the_created_version_even_after_a_later_version_exists()
    {
        var world = World();
        var first = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload, Read);
        var document = (await first.Service.UploadAsync(Harness.Upload(Party, V1), first.Metadata("create"))).Document;
        var v2 = await first.Service.AppendVersionAsync(document.Id,
            new AppendDocumentVersionRequest(1, "v2.pdf", "application/pdf", V2), first.Metadata("append-v2"));
        await first.Service.AppendVersionAsync(document.Id,
            new AppendDocumentVersionRequest(2, "v3.pdf", "application/pdf", V3), first.Metadata("append-v3"));

        var uploadReplay = await first.Service.UploadAsync(Harness.Upload(Party, V1), first.Metadata("create"));
        Assert.True(uploadReplay.IsReplay);
        Assert.Equal(1, uploadReplay.Document.VersionNo);

        var replay = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload, Read);
        var answer = await replay.Service.AppendVersionAsync(document.Id,
            new AppendDocumentVersionRequest(1, "v2.pdf", "application/pdf", V2), replay.Metadata("append-v2"));

        Assert.True(answer.IsReplay);
        Assert.Equal(v2.Document.Sha256, answer.Document.Sha256);
        Assert.Equal(2, answer.Document.VersionNo);
        Assert.Equal(3, (await replay.Service.ListVersionsAsync(document.Id, null, null)).Versions.Count);
        Assert.Equal(3, world.Attempts.Count);
    }

    [Fact]
    public async Task Stale_expected_version_is_conflict_without_a_second_effect()
    {
        var world = World();
        var service = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload);
        var document = (await service.Service.UploadAsync(Harness.Upload(Party, V1), service.Metadata("create"))).Document;
        await service.Service.AppendVersionAsync(document.Id,
            new AppendDocumentVersionRequest(1, "v2.pdf", "application/pdf", V2), service.Metadata("append-v2"));
        var versionCount = world.Versions.Count;
        var attemptCount = world.Attempts.Count;

        var refusal = await Assert.ThrowsAsync<DocumentServiceException>(() => service.Service.AppendVersionAsync(document.Id,
            new AppendDocumentVersionRequest(1, "stale.pdf", "application/pdf", V3), service.Metadata("append-stale")));

        Assert.Equal(409, refusal.Status);
        Assert.Equal(DocumentProblemCodesV1.ConcurrentChange, refusal.Code);
        Assert.Equal(versionCount, world.Versions.Count);
        Assert.Equal(attemptCount, world.Attempts.Count);
    }

    [Fact]
    public async Task Append_revalidates_permission_and_owner_immediately_before_write()
    {
        var world = World();
        var service = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload);
        var document = (await service.Service.UploadAsync(Harness.Upload(Party, V1), service.Metadata("create"))).Document;
        service.Store.BeforeWrite = () => world.OwnerOutcomes[(CompanyA, Party)] = DocumentOwnerAccessV1.Denied;

        var refusal = await Assert.ThrowsAsync<DocumentServiceException>(() => service.Service.AppendVersionAsync(document.Id,
            new AppendDocumentVersionRequest(1, "v2.pdf", "application/pdf", V2), service.Metadata("append-owner-revoked")));

        Assert.Equal(404, refusal.Status);
        Assert.Single(world.Versions);
        Assert.Single(world.Attempts);

        world.OwnerOutcomes.Remove((CompanyA, Party));
        var authority = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload);
        authority.Store.BeforeWrite = () => authority.Authority.Grants.Clear();
        var denied = await Assert.ThrowsAsync<DocumentServiceException>(() => authority.Service.AppendVersionAsync(document.Id,
            new AppendDocumentVersionRequest(1, "v2.pdf", "application/pdf", V2), authority.Metadata("append-permission-revoked")));
        Assert.Equal(403, denied.Status);
        Assert.Single(world.Versions);
        Assert.Single(world.Attempts);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task Append_revalidates_captured_actor_and_company_immediately_before_write(string drift)
    {
        var world = World();
        var service = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload);
        var document = (await service.Service.UploadAsync(Harness.Upload(Party, V1), service.Metadata("create"))).Document;
        service.Store.BeforeWrite = () =>
        {
            if (drift == "actor")
            {
                service.User.CurrentUserId = "SENTETIK-other-actor";
            }
            else
            {
                service.User.MutableAllowedCompanyIds.Remove(CompanyA);
            }
        };

        var refusal = await Assert.ThrowsAsync<DocumentServiceException>(() => service.Service.AppendVersionAsync(document.Id,
            new AppendDocumentVersionRequest(1, "v2.pdf", "application/pdf", V2), service.Metadata($"append-{drift}")));

        Assert.Equal(403, refusal.Status);
        Assert.Single(world.Versions);
        Assert.Single(world.Attempts);
    }

    [Fact]
    public async Task History_keyset_pages_traverse_every_version_in_order_without_duplicates()
    {
        var world = World();
        var service = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload, Read);
        var document = (await service.Service.UploadAsync(Harness.Upload(Party, V1), service.Metadata("page-v1"))).Document;
        for (var expected = 1; expected < 5; expected++)
        {
            await service.Service.AppendVersionAsync(document.Id,
                new AppendDocumentVersionRequest(expected, $"v{expected + 1}.pdf", "application/pdf",
                    [0x25, 0x50, 0x44, 0x46, 0x2D, checked((byte)(0x31 + expected))]),
                service.Metadata($"page-v{expected + 1}"));
        }

        var visited = new List<int>();
        string? cursor = null;
        do
        {
            var page = await service.Service.ListVersionsAsync(document.Id, 2, cursor);
            visited.AddRange(page.Versions.Select(version => version.VersionNo));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal([5, 4, 3, 2, 1], visited);
        Assert.Equal(visited.Count, visited.Distinct().Count());
    }

    [Theory]
    [InlineData("permission", 403)]
    [InlineData("actor", 403)]
    [InlineData("company", 403)]
    [InlineData("owner", 404)]
    public async Task Scanner_boundary_drift_is_refused_before_object_or_metadata_write(string drift, int status)
    {
        var world = World();
        var scanner = new FixedScanner(DocumentScanVerdictV1.Clean);
        var service = new Harness(CompanyA, scanner, world: world).Grant(Upload);
        var document = (await service.Service.UploadAsync(Harness.Upload(Party, V1), service.Metadata("scan-create"))).Document;
        var puts = world.Objects.Puts;
        var versions = world.Versions.Count;
        var attempts = world.Attempts.Count;
        scanner.AfterScan = () =>
        {
            switch (drift)
            {
                case "permission": service.Authority.Grants.Clear(); break;
                case "actor": service.User.CurrentUserId = "SENTETIK-other-actor"; break;
                case "company": service.User.MutableAllowedCompanyIds.Remove(CompanyA); break;
                case "owner": world.OwnerOutcomes[(CompanyA, Party)] = DocumentOwnerAccessV1.Denied; break;
            }
        };

        var refusal = await Assert.ThrowsAsync<DocumentServiceException>(() => service.Service.AppendVersionAsync(document.Id,
            new AppendDocumentVersionRequest(1, "scan-v2.pdf", "application/pdf", V2), service.Metadata($"scan-{drift}")));

        Assert.Equal(status, refusal.Status);
        Assert.Equal(puts, world.Objects.Puts);
        Assert.Equal(versions, world.Versions.Count);
        Assert.Equal(attempts, world.Attempts.Count);
    }

    [Theory]
    [InlineData("permission", DocumentScanVerdictV1.Clean, 403)]
    [InlineData("actor", DocumentScanVerdictV1.Clean, 403)]
    [InlineData("company", DocumentScanVerdictV1.Clean, 403)]
    [InlineData("owner", DocumentScanVerdictV1.Clean, 404)]
    [InlineData("permission", DocumentScanVerdictV1.Infected, 403)]
    [InlineData("actor", DocumentScanVerdictV1.Infected, 403)]
    [InlineData("company", DocumentScanVerdictV1.Infected, 403)]
    [InlineData("owner", DocumentScanVerdictV1.Infected, 404)]
    public async Task Upload_scanner_boundary_drift_wins_before_verdict_and_has_no_effect(
        string drift, DocumentScanVerdictV1 verdict, int status)
    {
        var world = World();
        var scanner = new FixedScanner(verdict);
        var service = new Harness(CompanyA, scanner, world: world).Grant(Upload);
        scanner.AfterScan = () =>
        {
            switch (drift)
            {
                case "permission": service.Authority.Grants.Clear(); break;
                case "actor": service.User.CurrentUserId = "SENTETIK-other-actor"; break;
                case "company": service.User.MutableAllowedCompanyIds.Remove(CompanyA); break;
                case "owner": world.OwnerOutcomes[(CompanyA, Party)] = DocumentOwnerAccessV1.Denied; break;
            }
        };

        var refusal = await Assert.ThrowsAsync<DocumentServiceException>(() => service.Service.UploadAsync(
            Harness.Upload(Party, V1), service.Metadata($"upload-scan-{drift}-{verdict}")));

        Assert.Equal(status, refusal.Status);
        Assert.Equal(0, world.Objects.Puts);
        Assert.Empty(world.Documents);
        Assert.Empty(world.Versions);
        Assert.Empty(world.Attempts);
    }

    [Fact]
    public async Task Foreign_company_and_unreadable_owner_are_not_disclosed()
    {
        var world = World();
        var writer = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload);
        var document = (await writer.Service.UploadAsync(Harness.Upload(Party, V1), writer.Metadata("create"))).Document;

        var foreign = new Harness(CompanyB, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload, Read);
        Assert.Equal(404, (await Refused(foreign.Service.AppendVersionAsync(document.Id,
            new AppendDocumentVersionRequest(1, "foreign.pdf", "application/pdf", V2), foreign.Metadata("foreign")))).Status);
        Assert.Equal(404, (await Refused(foreign.Service.ListVersionsAsync(document.Id, null, null))).Status);

        world.OwnerOutcomes[(CompanyA, Party)] = DocumentOwnerAccessV1.Denied;
        var denied = new Harness(CompanyA, new FixedScanner(DocumentScanVerdictV1.Clean), world: world).Grant(Upload, Read);
        Assert.Equal(404, (await Refused(denied.Service.AppendVersionAsync(document.Id,
            new AppendDocumentVersionRequest(1, "denied.pdf", "application/pdf", V2), denied.Metadata("denied")))).Status);
        Assert.Equal(404, (await Refused(denied.Service.GetVersionContentAsync(document.Id, 1))).Status);
        Assert.Single(world.Versions);
    }

    private static Harness.SharedWorld World()
    {
        var world = new Harness.SharedWorld();
        world.Readable.Add((CompanyA, Party));
        return world;
    }

    private static async Task<DocumentServiceException> Refused<T>(Task<T> call) =>
        await Assert.ThrowsAsync<DocumentServiceException>(() => call);
}
