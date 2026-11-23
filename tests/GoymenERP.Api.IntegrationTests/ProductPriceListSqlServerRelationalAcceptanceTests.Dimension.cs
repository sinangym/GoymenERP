using GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// BS-9 "Ebat/kalınlık" on a REAL SQL Server: the database holds the section shape (a sheet without its length is refused, a
/// valid sheet is stored once for the shared card), and the forward-fix rollback of <see cref="AddProductCardDimensions"/>
/// passes on an empty store and refuses (51290) while a dimension row exists, leaving the row in place. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class ProductPriceListSqlServerRelationalAcceptanceTests
{
    private const int DimensionRollbackRefusal = 51290;
    private const int CheckConstraintViolation = 547;

    [SqlServerFact]
    public async Task The_dimension_migrations_Down_passes_on_an_empty_store_and_refuses_while_a_dimension_row_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddProductCardDimensions().DownOperations[0]).Sql;
        var company = TestCompanyIds.CompanyA;

        await using (var scope = run.Request())
        {
            await scope.Context.Database.ExecuteSqlRawAsync(guard);

            var shape = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductCardDimensions (ProductId, SectionKind, ThicknessMm, WidthMm, LengthMm, HeightMm,
                    OuterDiameterMm, WallThicknessMm, KgPerMetre, Version, UpdatedAtUtc, UpdatedBy, UpdatedInCompanyId)
                VALUES ({ProductId}, N'Sheet', 2.0, 1250.0, NULL, NULL, NULL, NULL, NULL, 1, SYSDATETIMEOFFSET(), N'sql', {company})
                """));
            Assert.Equal(CheckConstraintViolation, shape.Number);

            await scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductCardDimensions (ProductId, SectionKind, ThicknessMm, WidthMm, LengthMm, HeightMm,
                    OuterDiameterMm, WallThicknessMm, KgPerMetre, Version, UpdatedAtUtc, UpdatedBy, UpdatedInCompanyId)
                VALUES ({ProductId}, N'Sheet', 2.0, 1250.0, 2500.0, NULL, NULL, NULL, NULL, 1, SYSDATETIMEOFFSET(), N'sql', {company})
                """);

            var refusal = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(DimensionRollbackRefusal, refusal.Number);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductCardDimensions WHERE ProductId = @id", ProductId));
    }
}
