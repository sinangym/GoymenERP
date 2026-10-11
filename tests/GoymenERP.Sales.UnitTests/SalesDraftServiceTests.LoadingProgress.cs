using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// DEC-SAL-005 over a SYNTHETIC Logistics owner: the Sales detail reads each order line's exact loading facts afresh,
/// under the existing draft-read authority, and writes nothing. An absent, failing or malformed owner fails closed.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Loading_facts_are_exact_per_line_read_afresh_and_the_read_writes_nothing()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("loading-1"))).Detail;
        var (first, second) = (created.Lines[0], created.Lines[1]);
        var owner = new FakeLoadings(order => new(Company, order,
        [
            new(first.LineId, KgUnit, 2, 1.25m, 1m, 0.75m, 0.5m),
        ]));
        world.Loadings = owner;
        var attempts = world.Store.Attempts;
        var envelopes = world.Store.Envelopes.Count;
        world.Authority.Measured.Clear();

        var progress = await world.Service().GetDraftLoadingProgressAsync(created.Draft.Id);

        Assert.Equal(created.Draft.Version, progress.DraftVersion);
        Assert.Equal(
        [
            new SalesDraftLineLoadingProgressV1(first.LineId, 1, KgUnit, "1.25", 2, "1.25", "1", "0.75", "0.5"),
            new SalesDraftLineLoadingProgressV1(second.LineId, 2, KgUnit, "2.25", 0, "0", "0", "0", "0"),
        ], progress.Lines);
        Assert.Equal([created.Draft.Id], owner.Asked);
        Assert.Equal([ReadPermission, ReadPermission, ReadPermission], world.Authority.Measured);
        Assert.Equal((attempts, envelopes), (world.Store.Attempts, world.Store.Envelopes.Count));
        Assert.Empty(world.Store.Transitions);
        Assert.Empty(world.Store.Refusals);

        owner.Answer = order => new(Company, order,
            [new(first.LineId, KgUnit, 2, 1.25m, 1.25m, 1.25m, 0.75m)]);
        var reopened = await world.Service().GetDraftLoadingProgressAsync(created.Draft.Id);
        Assert.Equal(("1.25", "1.25", "1.25", "0.75"),
            (reopened.Lines[0].Instructed, reopened.Lines[0].Loaded, reopened.Lines[0].Shipped, reopened.Lines[0].Delivered));
    }

    [Fact]
    public async Task Unknown_foreign_or_unauthorized_drafts_never_reach_the_loading_owner()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("loading-2"))).Detail;
        var owner = new FakeLoadings(order => new(Company, order, []));
        world.Loadings = owner;

        Assert.Equal((404, "not_found"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftLoadingProgressAsync(Guid.NewGuid()))));
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftLoadingProgressAsync(Guid.Empty))).Status);

        world.Authority.Denied.Add(ReadPermission);
        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftLoadingProgressAsync(created.Draft.Id))));
        Assert.Empty(owner.Asked);
    }

    [Fact]
    public async Task A_lost_read_authority_or_an_unavailable_owner_releases_no_loading_facts()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("loading-3"))).Detail;

        Assert.Equal((503, SalesDraftProblemCodesV1.LoadingProviderUnavailable),
            Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                world.Service().GetDraftLoadingProgressAsync(created.Draft.Id))));

        var owner = new FakeLoadings(order => new(Company, order,
            [new(created.Lines[0].LineId, KgUnit, 1, 1m, 1m, 0m, 0m)]));
        world.Loadings = owner;
        world.Authority.LostLater = (ReadPermission, world.Authority.Calls + 2);
        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftLoadingProgressAsync(created.Draft.Id))));
        Assert.Single(owner.Asked);
    }

    public static TheoryData<string> MalformedLoadingAnswers => new()
    {
        "owner-throws", "another-company", "another-order", "null-lines", "unknown-line", "another-unit",
        "negative", "wrong-order", "above-scale", "zero-count-with-quantity",
    };

    [Theory]
    [MemberData(nameof(MalformedLoadingAnswers))]
    public async Task A_malformed_loading_answer_fails_closed_instead_of_showing_a_number(string fault)
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("loading-4"))).Detail;
        var line = created.Lines[0].LineId;
        world.Loadings = new FakeLoadings(order => fault switch
        {
            "owner-throws" => throw new InvalidOperationException("SENTETIK owner outage"),
            "another-company" => new(Guid.NewGuid(), order, []),
            "another-order" => new(Company, Guid.NewGuid(), []),
            "null-lines" => new(Company, order, null!),
            "unknown-line" => new(Company, order, [new(Guid.NewGuid(), KgUnit, 1, 1m, 0m, 0m, 0m)]),
            "another-unit" => new(Company, order, [new(line, Guid.NewGuid(), 1, 1m, 0m, 0m, 0m)]),
            "negative" => new(Company, order, [new(line, KgUnit, 1, -1m, 0m, 0m, 0m)]),
            "wrong-order" => new(Company, order, [new(line, KgUnit, 1, 1m, 0.5m, 0.75m, 0m)]),
            "above-scale" => new(Company, order, [new(line, KgUnit, 1, 1.0001m, 0m, 0m, 0m)]),
            "zero-count-with-quantity" => new(Company, order, [new(line, KgUnit, 0, 1m, 0m, 0m, 0m)]),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        });

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftLoadingProgressAsync(created.Draft.Id));

        Assert.Equal((503, SalesDraftProblemCodesV1.LoadingProviderUnavailable), Status(refused));
    }
}
