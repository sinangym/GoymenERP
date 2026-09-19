using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// PRD-UNIT-1: a global card's unit reference is a Settings unit IDENTITY (company-independent). The
/// catalogue accepts a known identity; the stock role (Ö1 kg→kg) requires the kilogram identity; an
/// unknown id, or no Settings identity catalogue at all, is refused. Whether a company may use the unit
/// is its profile, checked at eligibility. The identities here are SYNTHETIC test doubles, not Settings'
/// approved data.
/// </summary>
public sealed class ProductRegistryUnitReferenceTests
{
    private static readonly Guid Kilogram = Guid.Parse("9d6c0b1e-7a0f-4c7e-9a51-3c2d1e0f0a01");
    private static readonly Guid Piece = Guid.Parse("9d6c0b1e-7a0f-4c7e-9a51-3c2d1e0f0a02");

    [Fact]
    public async Task A_known_identity_is_accepted_and_the_stock_role_requires_the_kilogram_identity()
    {
        var catalog = Catalog(new Identities());

        Assert.True(await catalog.IsUnitValidAsync(Kilogram, requiresKilogram: true, default));
        Assert.True(await catalog.IsUnitValidAsync(Kilogram, requiresKilogram: false, default));
        Assert.False(await catalog.IsUnitValidAsync(Piece, requiresKilogram: true, default));
        Assert.True(await catalog.IsUnitValidAsync(Piece, requiresKilogram: false, default));
    }

    [Fact]
    public async Task An_unknown_or_empty_id_and_an_identity_answered_for_another_id_are_refused()
    {
        var identities = new Identities();
        Assert.False(await Catalog(identities).IsUnitValidAsync(Guid.NewGuid(), requiresKilogram: false, default));
        Assert.False(await Catalog(identities).IsUnitValidAsync(Guid.Empty, requiresKilogram: false, default));

        // A catalogue that answers with another identity is not trusted for this id.
        Assert.False(await Catalog(new Identities { AnswerWith = Kilogram }).IsUnitValidAsync(Piece, requiresKilogram: false, default));
    }

    [Fact]
    public async Task Without_the_Settings_identity_catalogue_every_unit_is_refused()
    {
        Assert.False(await Catalog(null).IsUnitValidAsync(Kilogram, requiresKilogram: true, default));
        Assert.False(await Catalog(null).IsUnitValidAsync(Kilogram, requiresKilogram: false, default));
    }

    private static ProductRegistryReferenceCatalog Catalog(ISettingsUnitIdentityCatalogV1? units) => new(
        new ProductDbContext(new DbContextOptionsBuilder<ProductDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options),
        GroupSharedMasterCommandContext.FromServerDecision(new GroupSharedMasterAuthorizationSnapshot(true,
            GroupSharedMasterKind.Product, GroupSharedMasterOperation.DraftCreate, Guid.Parse("4e3c1a8b-0b58-441c-a2cc-a5768d0dfef0"),
            Guid.Parse("50e650f1-5546-438c-8d6c-3a867faf16f6"), "fixture-authority-1"), GroupSharedMasterOperation.DraftCreate),
        units);

    private sealed class Identities : ISettingsUnitIdentityCatalogV1
    {
        private static readonly UnitIdentityV1[] All =
        [
            new(Kilogram, "syn-kg", "syn-weight", true),
            new(Piece, "syn-piece", "syn-count", false),
        ];

        public Guid? AnswerWith { get; init; }

        public Task<UnitIdentityV1?> ReadAsync(Guid unitId, CancellationToken cancellationToken = default) =>
            Task.FromResult(All.SingleOrDefault(identity => identity.UnitId == (AnswerWith ?? unitId)));

        public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UnitIdentityV1>>(All);
    }
}
