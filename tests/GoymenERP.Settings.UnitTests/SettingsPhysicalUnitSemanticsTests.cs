using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

public sealed class SettingsPhysicalUnitSemanticsTests
{
    [Fact]
    public async Task Only_explicit_mass_identities_have_a_global_mass_factor_and_every_found_answer_has_its_source_version()
    {
        var provider = new ApprovedSettingsPhysicalUnitSemanticsV1(new ApprovedSettingsUnitIdentityCatalog());
        var kg = await provider.ReadAsync(SettingsUnitIdentitiesV1.Kilogram);
        var tonne = await provider.ReadAsync(SettingsUnitIdentitiesV1.Tonne);
        Assert.Equal((SettingsPhysicalUnitStatusV1.Found, SettingsPhysicalUnitKindV1.Kilogram, 1m),
            (kg.Status, kg.Kind!.Value, kg.KgPerUnit!.Value));
        Assert.Equal((SettingsPhysicalUnitStatusV1.Found, SettingsPhysicalUnitKindV1.Tonne, 1000m),
            (tonne.Status, tonne.Kind!.Value, tonne.KgPerUnit!.Value));
        foreach (var (id, kind) in new[] { (SettingsUnitIdentitiesV1.Metre, SettingsPhysicalUnitKindV1.Metre),
            (SettingsUnitIdentitiesV1.Piece, SettingsPhysicalUnitKindV1.Piece) })
        {
            var answer = await provider.ReadAsync(id);
            Assert.Equal(SettingsPhysicalUnitStatusV1.Found, answer.Status); Assert.Equal(kind, answer.Kind);
            Assert.Null(answer.KgPerUnit); Assert.Equal("settings.standard-unit-semantics.v1", answer.SourceVersion);
        }
        Assert.Equal(kg.SourceVersion, tonne.SourceVersion); Assert.NotNull(kg.SourceVersion);
    }

    [Fact]
    public async Task Unknown_identity_is_not_found_while_a_kg_named_foreign_identity_or_inconsistent_source_is_unavailable()
    {
        var id = Guid.NewGuid();
        var unknown = await new ApprovedSettingsPhysicalUnitSemanticsV1(new Source(null)).ReadAsync(id);
        Assert.Equal(SettingsPhysicalUnitStatusV1.NotFound, unknown.Status);
        var named = await new ApprovedSettingsPhysicalUnitSemanticsV1(new Source(new(id, "kg", "weight", true))).ReadAsync(id);
        Assert.Equal(SettingsPhysicalUnitStatusV1.Unavailable, named.Status);
        var changed = await new ApprovedSettingsPhysicalUnitSemanticsV1(new Source(new(SettingsUnitIdentitiesV1.Kilogram, "t", "weight", true)))
            .ReadAsync(SettingsUnitIdentitiesV1.Kilogram);
        Assert.Equal(SettingsPhysicalUnitStatusV1.Unavailable, changed.Status);
        foreach (var answer in new[] { unknown, named, changed })
        { Assert.Null(answer.Kind); Assert.Null(answer.KgPerUnit); Assert.Null(answer.SourceVersion); }
    }

    [Fact]
    public async Task Owner_failure_is_unavailable_and_cancellation_never_becomes_an_empty_or_found_answer()
    {
        var failed = await new ApprovedSettingsPhysicalUnitSemanticsV1(new Source(null, fail: true)).ReadAsync(Guid.NewGuid());
        Assert.Equal(SettingsPhysicalUnitStatusV1.Unavailable, failed.Status);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ApprovedSettingsPhysicalUnitSemanticsV1(new Source(null))
            .ReadAsync(Guid.NewGuid(), cancellation.Token));
    }

    private sealed class Source(UnitIdentityV1? identity, bool fail = false) : ISettingsUnitIdentityCatalogV1
    {
        public Task<UnitIdentityV1?> ReadAsync(Guid unitId, CancellationToken cancellationToken = default) => fail
            ? throw new InvalidOperationException("Synthetic owner failure") : Task.FromResult(identity);
        public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
