using System.Text;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KLN-D87 (STK-SC33-b) on a REAL SQL Server: the database holds the shape and text rules of the new section families (a welded
/// mesh whose wire is not thinner than its spacing and a structural profile of an unknown series are refused), a structural
/// profile card takes its kg/m from the series-and-size rows of the extended table file (and is saved without one when the table
/// has no such row), an ERW / painted ERW profile follows the profile rule on the profile rows (refused without a row), and the
/// forward-fix rollback of <see cref="AddProductSectionFamilies"/> passes on an empty store and refuses (51737) while a card of a
/// new family or a structural weight line exists, leaving the rows in place. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class ProductPriceListSqlServerRelationalAcceptanceTests
{
    private const int SectionFamiliesRollbackRefusal = 51737;
    private const string SectionFamiliesWeightFile =
        ProductProfileWeightFileFormatV1.ExtendedHeader + "\n"
        + "Profile;40;20;;2;1.72;;\n"
        + "StructuralProfile;;;;;22.4;IPE;200\n";

    [SqlServerFact]
    public async Task The_section_families_migrations_Down_passes_on_an_empty_store_and_refuses_while_a_new_family_card_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddProductSectionFamilies().DownOperations[0]).Sql;
        var company = TestCompanyIds.CompanyA;

        await using (var scope = run.Request())
        {
            await scope.Context.Database.ExecuteSqlRawAsync(guard);

            var wire = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductCardDimensions (ProductId, SectionKind, ThicknessMm, WidthMm, LengthMm, HeightMm,
                    OuterDiameterMm, WallThicknessMm, KgPerMetre, MeshSpacingMm, Version, UpdatedAtUtc, UpdatedBy, UpdatedInCompanyId)
                VALUES ({ProductId}, 'WeldedMesh', NULL, 2150.0, 5000.0, NULL, 150.0, NULL, NULL, 150.0, 1, SYSDATETIMEOFFSET(),
                    N'sql', {company})
                """));
            Assert.Equal(CheckConstraintViolation, wire.Number);

            var series = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductCardDimensions (ProductId, SectionKind, ProfileSeries, ProfileSize, Version, UpdatedAtUtc,
                    UpdatedBy, UpdatedInCompanyId)
                VALUES ({ProductId}, 'StructuralProfile', 'NPU', '200', 1, SYSDATETIMEOFFSET(), N'sql', {company})
                """));
            Assert.Equal(CheckConstraintViolation, series.Number);

            await scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductCardDimensions (ProductId, SectionKind, ProfileSeries, ProfileSize, Version, UpdatedAtUtc,
                    UpdatedBy, UpdatedInCompanyId)
                VALUES ({ProductId}, 'StructuralProfile', 'IPE', '200', 1, SYSDATETIMEOFFSET(), N'sql', {company})
                """);

            var refusal = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(SectionFamiliesRollbackRefusal, refusal.Number);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.ProductCardDimensions WHERE ProductId = @id AND SectionKind = 'StructuralProfile'",
            ProductId));
    }

    [SqlServerFact]
    public async Task A_structural_profile_card_takes_its_weight_from_the_extended_table_and_an_erw_profile_needs_a_profile_row()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddProductSectionFamilies().DownOperations[0]).Sql;
        var vault = new SwitchableVault { Outcome = ProductProfileWeightFileStoreOutcomeV1.Stored };

        await using (var scope = run.Request())
        {
            var weights = WeightService(scope, run, vault);
            var imported = await weights.ImportAsync(new ImportProductProfileWeightTableRequestV1("0", "SENTETIK KATALOG",
                "SENTETIK gerekçe", "katalog.csv", Convert.ToBase64String(Encoding.UTF8.GetBytes(SectionFamiliesWeightFile))),
                WeightMetadata(run, "families-import"));
            Assert.Equal(204, imported.Answer.StatusCode);
            var table = await weights.GetCurrentAsync();
            Assert.Equal(new ProductProfileWeightRowV1("StructuralProfile", null, null, null, null, 22.4m, "IPE", "200"),
                table.Rows[1]);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.ProductProfileWeightLines WHERE SectionKind = 'StructuralProfile' AND WallThicknessMm IS NULL"));

        await using (var scope = run.Request())
        {
            var dimensions = DimensionService(scope, run);

            var structural = await dimensions.SetAsync(ProductId, Structural("0", "IPE", "200"), DimensionMetadata(run, "families-1"));
            Assert.Equal(204, structural.Answer.StatusCode);
            var view = await dimensions.GetAsync(ProductId);
            Assert.Equal(("IPE", "200", 22.4m, "SENTETIK KATALOG", "1"),
                (view.ProfileSeries, view.ProfileSize, view.KgPerMetre, view.KgPerMetreSourceLabel, view.KgPerMetreTableVersion));

            // KLN-D87 leaves the standard table open: a structural size the table does not hold is saved, its weight reads as none.
            var unlisted = await dimensions.SetAsync(ProductId, Structural("1", "UPN", "300"), DimensionMetadata(run, "families-2"));
            Assert.Equal(204, unlisted.Answer.StatusCode);
            view = await dimensions.GetAsync(ProductId);
            Assert.Equal(("UPN", "300", (decimal?)null), (view.ProfileSeries, view.ProfileSize, view.KgPerMetre));

            var missing = await dimensions.SetAsync(ProductId, PaintedErwProfile("2", 30m), DimensionMetadata(run, "families-3"));
            Assert.Equal((409, ProductDimensionProblemCodesV1.WeightSourceMissing), (missing.Answer.StatusCode, missing.Answer.Code));

            var painted = await dimensions.SetAsync(ProductId, PaintedErwProfile("2", 40m), DimensionMetadata(run, "families-4"));
            Assert.Equal(204, painted.Answer.StatusCode);
            view = await dimensions.GetAsync(ProductId);
            Assert.Equal(("PaintedErwProfile", "RAL 7016", (string?)null, 1.72m),
                (view.SectionKind, view.SurfaceFinish, view.ProfileSeries, view.KgPerMetre));

            var mesh = await dimensions.SetAsync(ProductId, new SetProductDimensionRequestV1("3",
                ProductDimensionSectionKindsV1.WeldedMesh, null, 2150m, 5000m, null, 5m, null, "SENTETIK gerekçe",
                MeshSpacingMm: 150m), DimensionMetadata(run, "families-5"));
            Assert.Equal(204, mesh.Answer.StatusCode);
            view = await dimensions.GetAsync(ProductId);
            Assert.Equal((150m, (string?)null, (decimal?)null), (view.MeshSpacingMm!.Value, view.SurfaceFinish, view.KgPerMetre));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.ProductCardDimensions WHERE ProductId = @id AND SectionKind = 'WeldedMesh' AND Version = 4",
            ProductId));

        await using (var scope = run.Request())
        {
            var refusal = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(SectionFamiliesRollbackRefusal, refusal.Number);
        }
    }

    private static SetProductDimensionRequestV1 Structural(string expectedVersion, string series, string size) =>
        new(expectedVersion, ProductDimensionSectionKindsV1.StructuralProfile, null, null, null, null, null, null, "SENTETIK gerekçe",
            series, size);

    private static SetProductDimensionRequestV1 PaintedErwProfile(string expectedVersion, decimal width) =>
        new(expectedVersion, ProductDimensionSectionKindsV1.PaintedErwProfile, null, width, null, 20m, null, 2m, "SENTETIK gerekçe",
            SurfaceFinish: "RAL 7016");
}
