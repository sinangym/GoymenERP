using GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KLN-D48 / KLN-D54 (STK-S37) on a REAL SQL Server: the database holds the new section families (a structural profile names a
/// known series and size and never stores a typed kg/m; a painted ERW product needs its surface), the weight table refuses an
/// unknown series or a non-positive kg/m, and the forward-fix rollback of <see cref="AddProductSectionFamilies"/> passes on a
/// store with only the original kinds and refuses (51391) while a new-family row or a weight table exists. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class ProductPriceListSqlServerRelationalAcceptanceTests
{
    private const int SectionFamilyRollbackRefusal = 51391;

    [SqlServerFact]
    public async Task The_section_family_rows_are_held_by_the_database_check()
    {
        await using var run = await Run.StartAsync();
        var company = TestCompanyIds.CompanyA;

        await using (var scope = run.Request())
        {
            var typedWeight = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductCardDimensions (ProductId, SectionKind, LengthMm, KgPerMetre, ProfileSeries, ProfileSize,
                    Version, UpdatedAtUtc, UpdatedBy, UpdatedInCompanyId)
                VALUES ({ProductId}, N'StructuralProfile', 12000.0, 22.4, 'IPE', '200', 1, SYSDATETIMEOFFSET(), N'sql', {company})
                """));
            Assert.Equal(CheckConstraintViolation, typedWeight.Number);

            var unknownSeries = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductCardDimensions (ProductId, SectionKind, LengthMm, ProfileSeries, ProfileSize,
                    Version, UpdatedAtUtc, UpdatedBy, UpdatedInCompanyId)
                VALUES ({ProductId}, N'StructuralProfile', 12000.0, 'IPN', '200', 1, SYSDATETIMEOFFSET(), N'sql', {company})
                """));
            Assert.Equal(CheckConstraintViolation, unknownSeries.Number);

            var unpainted = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductCardDimensions (ProductId, SectionKind, OuterDiameterMm, WallThicknessMm,
                    Version, UpdatedAtUtc, UpdatedBy, UpdatedInCompanyId)
                VALUES ({ProductId}, N'PaintedErwTube', 48.3, 2.0, 1, SYSDATETIMEOFFSET(), N'sql', {company})
                """));
            Assert.Equal(CheckConstraintViolation, unpainted.Number);

            var wideWire = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductCardDimensions (ProductId, SectionKind, WidthMm, LengthMm, OuterDiameterMm, MeshSpacingMm,
                    Version, UpdatedAtUtc, UpdatedBy, UpdatedInCompanyId)
                VALUES ({ProductId}, N'WeldedMesh', 2150.0, 5000.0, 150.0, 150.0, 1, SYSDATETIMEOFFSET(), N'sql', {company})
                """));
            Assert.Equal(CheckConstraintViolation, wideWire.Number);

            await scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductCardDimensions (ProductId, SectionKind, LengthMm, ProfileSeries, ProfileSize,
                    Version, UpdatedAtUtc, UpdatedBy, UpdatedInCompanyId)
                VALUES ({ProductId}, N'StructuralProfile', 12000.0, 'IPE', '200', 1, SYSDATETIMEOFFSET(), N'sql', {company})
                """);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.ProductCardDimensions WHERE ProductId = @id AND KgPerMetre IS NULL", ProductId));
    }

    [SqlServerFact]
    public async Task The_profile_weight_rows_refuse_an_unknown_series_and_a_non_positive_weight()
    {
        await using var run = await Run.StartAsync();
        var company = TestCompanyIds.CompanyA;

        await using (var scope = run.Request())
        {
            await InsertWeightTableAsync(scope.Context, company);

            foreach (var (series, size, kg) in new[] { ("IPN", "200", 22.4m), ("IPE", "2 00", 22.4m), ("IPE", "220", 0m) })
            {
                var refused = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO product.ProductProfileWeightRows (CompanyId, TableVersion, ProfileSeries, ProfileSize, KgPerMetre)
                    VALUES ({company}, 1, {series}, {size}, {kg})
                    """));
                Assert.Equal(CheckConstraintViolation, refused.Number);
            }
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.ProductProfileWeightRows WHERE CompanyId = @id", TestCompanyIds.CompanyA));
    }

    [SqlServerFact]
    public async Task The_section_family_migrations_Down_passes_with_original_kinds_and_refuses_while_a_new_family_or_table_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddProductSectionFamilies().DownOperations[0]).Sql;
        var company = TestCompanyIds.CompanyA;

        await using (var scope = run.Request())
        {
            await scope.Context.Database.ExecuteSqlRawAsync(guard);

            await scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductCardDimensions (ProductId, SectionKind, ThicknessMm, WidthMm, LengthMm,
                    Version, UpdatedAtUtc, UpdatedBy, UpdatedInCompanyId)
                VALUES ({ProductId}, N'Sheet', 2.0, 1250.0, 2500.0, 1, SYSDATETIMEOFFSET(), N'sql', {company})
                """);
            await scope.Context.Database.ExecuteSqlRawAsync(guard);

            await InsertWeightTableAsync(scope.Context, company);
            var table = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(SectionFamilyRollbackRefusal, table.Number);

            await scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM product.ProductProfileWeightRows WHERE CompanyId = {company}");
            await scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM product.ProductProfileWeightTables WHERE CompanyId = {company}");
            await scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE product.ProductCardDimensions
                SET SectionKind = N'RoundBar', ThicknessMm = NULL, WidthMm = NULL, LengthMm = NULL, OuterDiameterMm = 20.0
                WHERE ProductId = {ProductId}
                """);
            var family = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(SectionFamilyRollbackRefusal, family.Number);
        }

        Assert.Equal("RoundBar", await run.ScalarAsync<string>(
            "SELECT SectionKind FROM product.ProductCardDimensions WHERE ProductId = @id", ProductId));
    }

    private static Task InsertWeightTableAsync(DbContext context, Guid company) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO product.ProductProfileWeightTables (CompanyId, Version, SourceLabel, Reason, [RowCount], ImportedAtUtc,
                ImportedBy, CorrelationId)
            VALUES ({company}, 1, N'SENTETIK tablo', N'SENTETIK gerekçe', 1, SYSDATETIMEOFFSET(), N'sql', N'corr-sql');
            INSERT INTO product.ProductProfileWeightRows (CompanyId, TableVersion, ProfileSeries, ProfileSize, KgPerMetre)
            VALUES ({company}, 1, 'IPE', '200', 22.4);
            """);
}
