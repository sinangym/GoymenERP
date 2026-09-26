using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// R19 / AT-038 through the REAL host: <c>POST /api/v1/inventory/label-transformations</c> with the host's DI
/// (<c>AddInventoryLabelGenealogy</c>), the route policy and the command's own fresh authority fences, over real
/// SQL. Split and merge commit once, keep the mass (no stock movement and no double quantity), replay the same key
/// and refuse everything else without an effect. The grant is a per-test synthetic role (the disposable-actor rule
/// of the 2026-09-27 decision); no seeded role holds the name. These cases need the central publication.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private const string LabelTransformationCommand = "inventory.label-transformation.record.v1";

    [SqlServerFact]
    public async Task Label_transformation_HTTP_splits_and_merges_once_through_the_real_host_replays_and_keeps_the_mass()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-R19-HTTP-SOURCE", 100m);
        var actor = await GrantAllocationAsync(run, TestUsers.CompanyA, CompanyA,
            InventoryPermissionManifestV1.LabelTransformationRecord,
            ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead);
        var movementsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM [inventory].[Movements]");
        Assert.Equal((0, 0, 0, 0, 0), await TransformationEffectCountsAsync(run));

        var splitA = Guid.NewGuid();
        var splitB = Guid.NewGuid();
        var split = new TransformInventoryLabelsV1(
            Guid.NewGuid(),
            InventoryLabelTransformationKindV1.Split,
            [new(source.LabelId, source.LabelVersion.ToString())],
            [new(splitA, "SYN-R19-HTTP-A", "40"), new(splitB, "SYN-R19-HTTP-B", "60")],
            At,
            "Sentetik R19 HTTP bolme kabul testi.");
        const string splitKey = "SYN-R19-HTTP-split";

        string firstCorrelation;
        using (var client = ClientFor(run.Factory, actor, CompanyA))
        {
            firstCorrelation = Assert.Single(client.DefaultRequestHeaders.GetValues("X-Correlation-Id"));
            using var request = TransformationPost(split, splitKey);
            using var created = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.False(created.Headers.Contains("Idempotency-Replayed"));
            AssertDescribes(split, (await created.Content.ReadFromJsonAsync<InventoryLabelTransformationViewV1>())!);
        }

        Assert.Equal((1, 1, 2, 1, 1), await TransformationEffectCountsAsync(run));
        using (var replayClient = ClientFor(run.Factory, actor, CompanyA))
        {
            using var replayRequest = TransformationPost(split, splitKey);
            using var replay = await replayClient.SendAsync(replayRequest);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(firstCorrelation, Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            AssertDescribes(split, (await replay.Content.ReadFromJsonAsync<InventoryLabelTransformationViewV1>())!);

            using var changedRequest = TransformationPost(split with { Reason = "Sentetik baska icerik." }, splitKey);
            using var changed = await replayClient.SendAsync(changedRequest);
            Assert.Equal((HttpStatusCode.Conflict, InventoryLabelGenealogyCodesV1.IdempotencyConflict),
                (changed.StatusCode, await ProblemCodeAsync(changed)));

            // A new attempt at the version the split consumed is refused: the source is no longer at it.
            using var staleRequest = TransformationPost(split with
            {
                TransformationId = Guid.NewGuid(),
                Outputs = [new(Guid.NewGuid(), "SYN-R19-HTTP-STALE-A", "40"), new(Guid.NewGuid(), "SYN-R19-HTTP-STALE-B", "60")],
            }, "SYN-R19-HTTP-stale");
            using var stale = await replayClient.SendAsync(staleRequest);
            Assert.Equal((HttpStatusCode.Conflict, InventoryLabelGenealogyCodesV1.LabelVersionConflict),
                (stale.StatusCode, await ProblemCodeAsync(stale)));
        }

        Assert.Equal((1, 1, 2, 1, 1), await TransformationEffectCountsAsync(run));
        var merged = Guid.NewGuid();
        var merge = new TransformInventoryLabelsV1(
            Guid.NewGuid(),
            InventoryLabelTransformationKindV1.Merge,
            [new(splitA, "1"), new(splitB, "1")],
            [new(merged, "SYN-R19-HTTP-MERGED", "100")],
            At.AddMinutes(1),
            "Sentetik R19 HTTP birlestirme kabul testi.");
        using (var client = ClientFor(run.Factory, actor, CompanyA))
        {
            using var request = TransformationPost(merge, "SYN-R19-HTTP-merge");
            using var created = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            AssertDescribes(merge, (await created.Content.ReadFromJsonAsync<InventoryLabelTransformationViewV1>())!);

            // The merged label reads back through the real detail route with its whole genealogy and its mass.
            using var detail = await client.GetAsync($"/api/v1/inventory/labels/{merged:D}/detail");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var body = await detail.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("100", body.GetProperty("balances").GetProperty("physicalKg").GetString());
            var edges = body.GetProperty("genealogy").GetProperty("edges").EnumerateArray().ToArray();
            Assert.Equal(4, edges.Length);
            Assert.Equal(["Merge", "Split"],
                edges.Select(edge => edge.GetProperty("kind").GetString() ?? string.Empty).Distinct().Order().ToArray());
        }

        Assert.Equal((2, 3, 3, 2, 2), await TransformationEffectCountsAsync(run));
        // A transformation is no stock movement, and the mass is the source's 100 kg exactly once.
        Assert.Equal(movementsBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [inventory].[Movements]"));
        Assert.Equal(100m, await run.ScalarAsync<decimal>(
            "SELECT SUM([QuantityKg]) FROM [inventory].[Balances] WHERE [CompanyId] = @company "
            + "AND [LabelId] IN (@source, @a, @b, @merged)",
            ("@company", CompanyA), ("@source", source.LabelId), ("@a", splitA), ("@b", splitB), ("@merged", merged)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [inventory].[Balances] WHERE [LabelId] = @merged AND [QuantityKg] = 100",
            ("@merged", merged)));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [inventory].[Balances] WHERE [LabelId] IN (@source, @a, @b) AND [QuantityKg] = 0",
            ("@source", source.LabelId), ("@a", splitA), ("@b", splitB)));
    }

    [SqlServerFact]
    public async Task Label_transformation_HTTP_refuses_denied_foreign_stale_allocated_and_malformed_requests_without_effect()
    {
        await using var run = await Run.StartAsync();
        var own = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-R19-REF-OWN", 50m);
        var foreign = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-R19-REF-FOREIGN", 50m);
        var actor = await GrantAllocationAsync(run, TestUsers.CompanyA, CompanyA,
            InventoryPermissionManifestV1.LabelTransformationRecord);
        var before = await TransformationEffectCountsAsync(run);
        TransformInventoryLabelsV1 SplitOf(PhysicalSource source, string prefix, long? version = null) => new(
            Guid.NewGuid(),
            InventoryLabelTransformationKindV1.Split,
            [new(source.LabelId, (version ?? source.LabelVersion).ToString())],
            [new(Guid.NewGuid(), $"{prefix}-A", "20"), new(Guid.NewGuid(), $"{prefix}-B", "30")],
            At,
            "Sentetik R19 HTTP ret kabul testi.");

        // No holder of the name: the viewer is refused by the route policy, an anonymous caller by authentication.
        using (var viewer = ClientFor(run.Factory, TestUsers.CompanyAViewer, CompanyA))
        using (var request = TransformationPost(SplitOf(own, "SYN-R19-REF-VIEWER"), "SYN-R19-REF-viewer"))
        using (var denied = await viewer.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using (var anonymous = run.Factory.CreateClient())
        {
            anonymous.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
            using var request = TransformationPost(SplitOf(own, "SYN-R19-REF-ANON"), "SYN-R19-REF-anonymous");
            using var unauthenticated = await anonymous.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        }

        // The company fence: the holder has no membership in B, and B's label does not exist from A.
        using (var elsewhere = ClientFor(run.Factory, actor, CompanyB))
        using (var request = TransformationPost(SplitOf(foreign, "SYN-R19-REF-ELSEWHERE"), "SYN-R19-REF-elsewhere"))
        using (var outside = await elsewhere.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.Forbidden, outside.StatusCode);
        }

        using var client = ClientFor(run.Factory, actor, CompanyA);
        using (var request = TransformationPost(SplitOf(foreign, "SYN-R19-REF-HIDDEN"), "SYN-R19-REF-hidden"))
        using (var hidden = await client.SendAsync(request))
        {
            Assert.Equal((HttpStatusCode.NotFound, InventoryLabelGenealogyCodesV1.LabelNotFound),
                (hidden.StatusCode, await ProblemCodeAsync(hidden)));
            Assert.DoesNotContain("SYN-R19-REF-FOREIGN", await hidden.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        // Transport and body refusals answered before the writer.
        using (var request = TransformationPost(SplitOf(own, "SYN-R19-REF-NOKEY"), idempotencyKey: null))
        using (var noKey = await client.SendAsync(request))
        {
            Assert.Equal((HttpStatusCode.BadRequest, InventoryLabelGenealogyCodesV1.ValidationFailed),
                (noKey.StatusCode, await ProblemCodeAsync(noKey)));
        }

        using (var request = new HttpRequestMessage(HttpMethod.Post, InventoryLabelTransformationApiV1.Path)
               {
                   Content = new StringContent("{}", Encoding.UTF8, "text/plain"),
               })
        {
            request.Headers.Add("Idempotency-Key", "SYN-R19-REF-media");
            using var media = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, media.StatusCode);
        }

        using (var request = new HttpRequestMessage(HttpMethod.Post, InventoryLabelTransformationApiV1.Path)
               {
                   Content = new StringContent("{\"transformationId\":\"not-a-guid\"}", Encoding.UTF8, "application/json"),
               })
        {
            request.Headers.Add("Idempotency-Key", "SYN-R19-REF-malformed");
            using var malformed = await client.SendAsync(request);
            Assert.Equal((HttpStatusCode.BadRequest, InventoryLabelGenealogyCodesV1.ValidationFailed),
                (malformed.StatusCode, await ProblemCodeAsync(malformed)));
        }

        // Writer refusals: stale version, a taken output code, a single-output split, an active allocation.
        using (var request = TransformationPost(SplitOf(own, "SYN-R19-REF-STALE", own.LabelVersion + 1), "SYN-R19-REF-stale"))
        using (var stale = await client.SendAsync(request))
        {
            Assert.Equal((HttpStatusCode.Conflict, InventoryLabelGenealogyCodesV1.LabelVersionConflict),
                (stale.StatusCode, await ProblemCodeAsync(stale)));
        }

        var taken = SplitOf(own, "SYN-R19-REF-TAKEN") with
        {
            Outputs = [new(Guid.NewGuid(), "SYN-R19-REF-OWN", "20"), new(Guid.NewGuid(), "SYN-R19-REF-TAKEN-B", "30")],
        };
        using (var request = TransformationPost(taken, "SYN-R19-REF-taken"))
        using (var codeTaken = await client.SendAsync(request))
        {
            Assert.Equal((HttpStatusCode.Conflict, InventoryLabelGenealogyCodesV1.LabelCodeTaken),
                (codeTaken.StatusCode, await ProblemCodeAsync(codeTaken)));
        }

        var single = SplitOf(own, "SYN-R19-REF-SINGLE") with
        {
            Outputs = [new(Guid.NewGuid(), "SYN-R19-REF-SINGLE-A", "50")],
        };
        using (var request = TransformationPost(single, "SYN-R19-REF-single"))
        using (var invalid = await client.SendAsync(request))
        {
            Assert.Equal((HttpStatusCode.BadRequest, InventoryLabelGenealogyCodesV1.ValidationFailed),
                (invalid.StatusCode, await ProblemCodeAsync(invalid)));
        }

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(own, "10"), Metadata(run));
        }

        var allocatedVersion = await run.ScalarAsync<long>(
            "SELECT [Version] FROM [inventory].[Labels] WHERE [Id] = @id", ("@id", own.LabelId));
        using (var request = TransformationPost(SplitOf(own, "SYN-R19-REF-ALLOC", allocatedVersion), "SYN-R19-REF-allocated"))
        using (var allocated = await client.SendAsync(request))
        {
            Assert.Equal((HttpStatusCode.Conflict, InventoryLabelGenealogyCodesV1.ActiveAllocationConflict),
                (allocated.StatusCode, await ProblemCodeAsync(allocated)));
        }

        // Nothing any refusal sent left a transformation, label, balance, command or audit row.
        Assert.Equal(before, await TransformationEffectCountsAsync(run));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [inventory].[Labels] WHERE [Code] LIKE N'SYN-R19-REF-%' AND [Id] NOT IN (@own, @foreign)",
            ("@own", own.LabelId), ("@foreign", foreign.LabelId)));
        Assert.Equal(50m, await run.ScalarAsync<decimal>(
            "SELECT SUM([QuantityKg]) FROM [inventory].[Balances] WHERE [LabelId] = @own", ("@own", own.LabelId)));
    }

    private static HttpRequestMessage TransformationPost(TransformInventoryLabelsV1 body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, InventoryLabelTransformationApiV1.Path)
        {
            Content = JsonContent.Create(body),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    private static void AssertDescribes(TransformInventoryLabelsV1 request, InventoryLabelTransformationViewV1 view)
    {
        Assert.Equal((request.TransformationId, CompanyA, request.Kind.ToString(), request.Reason),
            (view.TransformationId, view.CompanyId, view.Kind, view.Reason));
        Assert.Equal(request.OccurredAtUtc, view.OccurredAtUtc);
        Assert.Equal(request.Sources.Select(source => source.LabelId), view.SourceLabelIds);
        Assert.Equal(request.Outputs.Select(output => output.LabelId), view.OutputLabelIds);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<(int Transformations, int Inputs, int Outputs, int Commands, int Audits)>
        TransformationEffectCountsAsync(Run run) =>
        (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [inventory].[LabelTransformations]"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [inventory].[LabelTransformationInputs]"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [inventory].[LabelTransformationOutputs]"),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [inventory].[Commands] WHERE [CommandName] = N'" + LabelTransformationCommand + "'"),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Inventory.LabelTransformation'")
        );
}
