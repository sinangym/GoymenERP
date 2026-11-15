using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// BS-9 "Ebat/kalınlık" without a database: the permission name is registered (not granted, not Critical), the shared-card row
/// and its command answers are mapped as decided (product schema, no company filter on the shared row, a filter on the answers,
/// the shape and measure checks equal to the context constants, millimetre / kg-per-metre precision), the forward-fix rollback
/// refuses first, the write guard refuses before any connection, and the service answers its fences and malformed input before
/// any read. What SQL Server enforces is proven by <c>ProductPriceListSqlServerRelationalAcceptanceTests</c>. SENTETIK / NOT_ENV.
/// </summary>
public sealed class ProductDimensionTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("55555555-5555-4555-8555-555555555555");

    [Fact]
    public void The_dimension_permission_is_registered_once_company_wide_not_critical_and_seeded_to_nobody()
    {
        Assert.Equal("product.dimension.manage", ProductDimensionPermissionsV1.Manage);
        Assert.Equal(ErpPermissions.ProductDimensionManage, ProductDimensionPermissionsV1.Manage);
        Assert.True(ErpPermissions.IsKnown(ProductDimensionPermissionsV1.Manage));
        Assert.Single(ErpPermissions.All, name => name == ProductDimensionPermissionsV1.Manage);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ProductDimensionPermissionsV1.Manage));
        Assert.False(ErpPermissions.IsCritical(ProductDimensionPermissionsV1.Manage));
        var seed = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "backend", "GoymenERP.Api", "Seed", "identity-seed.json"));
        Assert.DoesNotContain(ProductDimensionPermissionsV1.Manage, seed, StringComparison.Ordinal);
    }

    [Fact]
    public void The_profile_weight_import_permission_is_registered_once_company_wide_not_critical_and_seeded_to_nobody()
    {
        Assert.Equal("product.profile-weights.import", ProductProfileWeightPermissionsV1.Import);
        Assert.Equal(ErpPermissions.ProductProfileWeightsImport, ProductProfileWeightPermissionsV1.Import);
        Assert.True(ErpPermissions.IsKnown(ProductProfileWeightPermissionsV1.Import));
        Assert.Single(ErpPermissions.All, name => name == ProductProfileWeightPermissionsV1.Import);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ProductProfileWeightPermissionsV1.Import));
        Assert.False(ErpPermissions.IsCritical(ProductProfileWeightPermissionsV1.Import));
        var seed = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "backend", "GoymenERP.Api", "Seed", "identity-seed.json"));
        Assert.DoesNotContain(ProductProfileWeightPermissionsV1.Import, seed, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shared_row_and_its_answers_are_mapped_with_the_decided_checks_precision_and_filters()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model;
        var row = design.FindEntityType(typeof(ProductCardDimensionRow))!;
        Assert.Equal(("product", "ProductCardDimensions"), (row.GetSchema(), row.GetTableName()));
        Assert.Equal(["ProductId"], row.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Empty(context.Model.FindEntityType(typeof(ProductCardDimensionRow))!.GetDeclaredQueryFilters());
        Assert.Contains(row.GetCheckConstraints(), check => check.Name == "CK_ProductCardDimensions_Shape"
            && check.Sql == ProductPriceListDbContext.DimensionShapeCheck);
        Assert.Contains(row.GetCheckConstraints(), check => check.Name == "CK_ProductCardDimensions_Measures"
            && check.Sql == ProductPriceListDbContext.DimensionMeasureCheck);
        Assert.Contains(row.GetCheckConstraints(), check => check.Name == "CK_ProductCardDimensions_Text"
            && check.Sql == ProductPriceListDbContext.DimensionTextCheck);
        foreach (var millimetre in new[]
                 {
                     nameof(ProductCardDimensionRow.ThicknessMm), nameof(ProductCardDimensionRow.WidthMm),
                     nameof(ProductCardDimensionRow.LengthMm), nameof(ProductCardDimensionRow.HeightMm),
                     nameof(ProductCardDimensionRow.OuterDiameterMm), nameof(ProductCardDimensionRow.WallThicknessMm),
                     nameof(ProductCardDimensionRow.MeshSpacingMm),
                 })
        {
            var property = row.FindProperty(millimetre)!;
            Assert.True(property.IsNullable);
            Assert.Equal((18, ProductDimensionInputLimitsV1.MillimetreDecimals), (property.GetPrecision(), property.GetScale()));
        }

        var kg = row.FindProperty(nameof(ProductCardDimensionRow.KgPerMetre))!;
        Assert.Equal((18, ProductDimensionInputLimitsV1.KgPerMetreDecimals), (kg.GetPrecision(), kg.GetScale()));
        Assert.True(row.FindProperty(nameof(ProductCardDimensionRow.Version))!.IsConcurrencyToken);
        Assert.Contains(row.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PriceListProductReference));
        Assert.Contains(row.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PriceListCompanyReference));

        var attempts = design.FindEntityType(typeof(ProductDimensionAttemptRow))!;
        Assert.Equal(("product", "ProductDimensionAttempts"), (attempts.GetSchema(), attempts.GetTableName()));
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"],
            attempts.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(context.Model.FindEntityType(typeof(ProductDimensionAttemptRow))!.GetDeclaredQueryFilters());
        foreach (var text in new[]
                 {
                     nameof(ProductCardDimensionRow.ProfileSeries), nameof(ProductCardDimensionRow.ProfileSize),
                     nameof(ProductCardDimensionRow.SurfaceFinish),
                 })
        {
            Assert.True(row.FindProperty(text)!.IsNullable);
        }

        Assert.Equal(ProductDimensionInputLimitsV1.ProfileSizeMaximumLength,
            row.FindProperty(nameof(ProductCardDimensionRow.ProfileSize))!.GetMaxLength());
        Assert.Equal(ProductDimensionInputLimitsV1.SurfaceFinishMaximumLength,
            row.FindProperty(nameof(ProductCardDimensionRow.SurfaceFinish))!.GetMaxLength());
    }

    [Fact]
    public void The_profile_weight_table_is_company_scoped_insert_only_per_version_and_checked()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model;
        var header = design.FindEntityType(typeof(ProductProfileWeightTableRow))!;
        Assert.Equal(("product", "ProductProfileWeightTables"), (header.GetSchema(), header.GetTableName()));
        Assert.Equal(["CompanyId", "Version"], header.FindPrimaryKey()!.Properties.Select(property => property.Name));

        var rows = design.FindEntityType(typeof(ProductProfileWeightRow))!;
        Assert.Equal(("product", "ProductProfileWeightRows"), (rows.GetSchema(), rows.GetTableName()));
        Assert.Equal(["CompanyId", "TableVersion", "ProfileSeries", "ProfileSize"],
            rows.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(rows.GetCheckConstraints(), check => check.Name == "CK_ProductProfileWeightRows_Row"
            && check.Sql == ProductPriceListDbContext.ProfileWeightRowCheck);
        var kg = rows.FindProperty(nameof(ProductProfileWeightRow.KgPerMetre))!;
        Assert.Equal((18, ProductDimensionInputLimitsV1.KgPerMetreDecimals), (kg.GetPrecision(), kg.GetScale()));

        var attempts = design.FindEntityType(typeof(ProductProfileWeightAttemptRow))!;
        Assert.Equal(("product", "ProductProfileWeightAttempts"), (attempts.GetSchema(), attempts.GetTableName()));
        foreach (var type in new[] { typeof(ProductProfileWeightTableRow), typeof(ProductProfileWeightRow), typeof(ProductProfileWeightAttemptRow) })
        {
            Assert.Single(context.Model.FindEntityType(type)!.GetDeclaredQueryFilters());
        }
    }

    [Fact]
    public void The_dimension_migration_Down_refuses_first_with_51290_and_created_the_four_original_kinds_only()
    {
        var migration = new AddProductCardDimensions();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51290", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductCardDimensions]", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductDimensionAttempts]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>(),
            operation => operation.Name == "ProductCardDimensions");
        var original = Assert.Single(table.CheckConstraints, check => check.Name == "CK_ProductCardDimensions_Shape").Sql;
        Assert.Contains("'RoundTube'", original, StringComparison.Ordinal);
        Assert.DoesNotContain("'StructuralProfile'", original, StringComparison.Ordinal);
    }

    [Fact]
    public void The_section_family_migration_is_the_last_one_the_snapshot_matches_and_its_Down_refuses_first_with_51391()
    {
        using var context = Context();
        Assert.Equal(["AddProductCardDimensions", "AddProductSectionFamilies"],
            context.Database.GetMigrations().TakeLast(2).Select(id => id.Split('_', 2)[1]));
        Assert.False(context.Database.HasPendingModelChanges());

        var migration = new AddProductSectionFamilies();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.UpOperations.OfType<AddCheckConstraintOperation>(), check =>
            check.Name == "CK_ProductCardDimensions_Shape" && check.Sql == ProductPriceListDbContext.DimensionShapeCheck);
        Assert.Contains(migration.UpOperations.OfType<CreateTableOperation>(), table => table.Name == "ProductProfileWeightTables");
        Assert.Contains(migration.UpOperations.OfType<CreateTableOperation>(), table => table.Name == "ProductProfileWeightRows");
        Assert.Contains(migration.UpOperations.OfType<CreateTableOperation>(), table => table.Name == "ProductProfileWeightAttempts");
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51391", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductCardDimensions]", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductProfileWeightTables]", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductProfileWeightAttempts]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }

    [Fact]
    public void The_database_shape_check_is_built_from_the_same_rule_table_and_names_every_kind()
    {
        Assert.Equal(ProductDimensionShape.SqlCheck(), ProductPriceListDbContext.DimensionShapeCheck);
        Assert.Equal(ProductDimensionSectionKindsV1.All, ProductDimensionShape.Rules.Select(rule => rule.SectionKind));
        foreach (var kind in ProductDimensionSectionKindsV1.All)
        {
            Assert.Contains($"'{kind}'", ProductPriceListDbContext.DimensionShapeCheck, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_write_guard_refuses_a_delete_and_a_write_in_the_name_of_another_company()
    {
        await using (var context = Context())
        {
            context.Attach(Row(CompanyA));
            context.Remove(context.ChangeTracker.Entries<ProductCardDimensionRow>().Single().Entity);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            context.Add(Row(CompanyB));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            var row = Row(CompanyA);
            context.Attach(row);
            context.Entry(row).Property(x => x.UpdatedInCompanyId).IsModified = true;
            row.UpdatedInCompanyId = CompanyB;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }

    [Theory]
    [InlineData("Sheet", "2", "1250", "2500", null, null, null, null, true)]
    [InlineData("Coil", "0.5", "1000", null, null, null, null, null, true)]
    [InlineData("RoundTube", null, null, null, null, "48.3", "2.6", null, true)]
    [InlineData("RoundTube", null, null, null, null, "48.3", "2.6", "2.93", true)]
    [InlineData("RoundTube", null, null, null, null, "10", "5", null, true)]
    [InlineData("Profile", null, "40", null, "20", null, "2", "1.72", true)]
    // Unknown kind, wrong case, missing / extra measures, zero, negative, over-scale, walls that do not fit.
    [InlineData("sheet", "2", "1250", "2500", null, null, null, null, false)]
    [InlineData("Plate", "2", "1250", "2500", null, null, null, null, false)]
    [InlineData(null, "2", "1250", "2500", null, null, null, null, false)]
    [InlineData("Sheet", "2", "1250", null, null, null, null, null, false)]
    [InlineData("Sheet", "2", "1250", "2500", null, null, null, "1", false)]
    [InlineData("Coil", "0.5", "1000", "2000", null, null, null, null, false)]
    [InlineData("Coil", "0", "1000", null, null, null, null, null, false)]
    [InlineData("Coil", "-1", "1000", null, null, null, null, null, false)]
    [InlineData("Coil", "0.5005", "1000", null, null, null, null, null, false)]
    [InlineData("RoundTube", null, null, null, null, "10", "5.001", null, false)]
    [InlineData("RoundTube", null, null, null, null, "48.3", null, null, false)]
    [InlineData("RoundTube", null, null, null, null, "48.3", "2.6", "0.0000001", false)]
    [InlineData("Profile", null, "40", null, "20", null, "2", null, false)]
    [InlineData("Profile", null, "40", null, "3", null, "2", "1.72", false)]
    [InlineData("Profile", "2", "40", null, "20", null, "2", "1.72", false)]
    public void The_shape_accepts_exactly_the_measures_of_the_original_section_kinds_and_never_rounds(string? kind, string? thickness,
        string? width, string? length, string? height, string? diameter, string? wall, string? kgPerMetre, bool accepted)
    {
        var request = new SetProductDimensionRequestV1("0", kind, D(thickness), D(width), D(length), D(height), D(diameter), D(wall),
            D(kgPerMetre), "SENTETIK gerekçe");

        var refusal = ProductDimensionShape.Refusal(request, out var measures);

        Assert.Equal(accepted, refusal is null);
        Assert.Equal(accepted, measures is not null);
    }

    // KLN-D48 families: kind, thickness, width, length, height, diameter, wall, kg/m, series, size, mesh, surface.
    [Theory]
    [InlineData("EqualAngle", "5", "50", null, null, null, null, null, null, null, null, null, true)]
    [InlineData("EqualAngle", "5", "50", "6000", null, null, null, null, null, null, null, null, true)]
    [InlineData("EqualAngle", "50", "50", null, null, null, null, null, null, null, null, null, false)]
    [InlineData("EqualAngle", "5", "50", null, null, null, null, "3.77", null, null, null, null, false)]
    [InlineData("UnequalAngle", "6", "80", null, "40", null, null, null, null, null, null, null, true)]
    [InlineData("UnequalAngle", "6", "80", null, "6", null, null, null, null, null, null, null, false)]
    [InlineData("FlatBar", "10", "10", null, null, null, null, null, null, null, null, null, true)]
    [InlineData("FlatBar", "11", "10", null, null, null, null, null, null, null, null, null, false)]
    [InlineData("NarrowFlatBar", "5", "20", "6000", null, null, null, null, null, null, null, null, true)]
    [InlineData("RoundBar", null, null, null, null, "20", null, null, null, null, null, null, true)]
    [InlineData("RoundBar", null, "20", null, null, null, null, null, null, null, null, null, false)]
    [InlineData("SquareBar", null, "20", "6000", null, null, null, null, null, null, null, null, true)]
    [InlineData("HexBar", null, "17", null, null, null, null, null, null, null, null, null, true)]
    [InlineData("TeeBar", "5", "40", null, "40", null, null, null, null, null, null, null, true)]
    [InlineData("StructuralProfile", null, null, "12000", null, null, null, null, "IPE", "200", null, null, true)]
    [InlineData("StructuralProfile", null, null, "12000", null, null, null, null, "HEA", "1000", null, null, true)]
    [InlineData("StructuralProfile", null, null, "12000", null, null, null, "22.4", "IPE", "200", null, null, false)]
    [InlineData("StructuralProfile", null, null, "12000", null, null, null, null, "IPE", null, null, null, false)]
    [InlineData("StructuralProfile", null, null, "12000", null, null, null, null, "ipe", "200", null, null, false)]
    [InlineData("StructuralProfile", null, null, "12000", null, null, null, null, "IPN", "200", null, null, false)]
    [InlineData("StructuralProfile", null, null, "12000", null, null, null, null, "IPE", " 200", null, null, false)]
    [InlineData("StructuralProfile", null, null, "12000", null, null, null, null, "IPE", "20000000000000000", null, null, false)]
    [InlineData("StructuralProfile", null, null, null, null, null, null, null, "IPE", "200", null, null, false)]
    [InlineData("Rebar", null, null, "12000", null, "12", null, null, null, null, null, null, true)]
    [InlineData("WireRod", null, null, null, null, "5.5", null, null, null, null, null, null, true)]
    [InlineData("WireRod", null, null, "6000", null, "5.5", null, null, null, null, null, null, false)]
    [InlineData("WeldedMesh", null, "2150", "5000", null, "5", null, null, null, null, "150", null, true)]
    [InlineData("WeldedMesh", null, "2150", "5000", null, "150", null, null, null, null, "150", null, false)]
    [InlineData("WeldedMesh", null, "2150", "5000", null, "5", null, null, null, null, null, null, false)]
    [InlineData("ChequeredSheet", "3", "1250", "2500", null, null, null, null, null, null, null, null, true)]
    [InlineData("PerforatedSheet", "1", "1000", "2000", null, null, null, null, null, null, null, null, true)]
    [InlineData("CorrugatedSheet", "0.5", "1000", "3000", null, null, null, null, null, null, null, null, true)]
    [InlineData("CorrugatedSheet", "0.5", "1000", "3000", null, null, null, null, null, null, null, "RAL 9002", false)]
    [InlineData("ErwTube", null, null, null, null, "48.3", "2", null, null, null, null, null, true)]
    [InlineData("ErwTube", null, null, null, null, "48.3", "2", "2.28", null, null, null, null, true)]
    [InlineData("ErwTube", null, null, null, null, "48.3", "2", null, null, null, null, "RAL 7016", false)]
    [InlineData("ErwProfile", null, "40", null, "40", null, "2", "2.31", null, null, null, null, true)]
    [InlineData("ErwProfile", null, "40", null, "40", null, "2", null, null, null, null, null, false)]
    [InlineData("PaintedErwTube", null, null, null, null, "48.3", "2", null, null, null, null, "RAL 7016 boyalı", true)]
    [InlineData("PaintedErwTube", null, null, null, null, "48.3", "2", null, null, null, null, null, false)]
    [InlineData("PaintedErwTube", null, null, null, null, "48.3", "2", null, null, null, null, " RAL 7016", false)]
    [InlineData("PaintedErwProfile", null, "40", null, "20", null, "2", "1.72", null, null, null, "Siyah boyalı", true)]
    [InlineData("PaintedErwProfile", null, "40", null, "20", null, "2", "1.72", null, null, null, null, false)]
    public void The_shape_accepts_exactly_the_fields_of_the_new_section_families(string kind, string? thickness, string? width,
        string? length, string? height, string? diameter, string? wall, string? kgPerMetre, string? series, string? size,
        string? mesh, string? surface, bool accepted)
    {
        var request = new SetProductDimensionRequestV1("0", kind, D(thickness), D(width), D(length), D(height), D(diameter), D(wall),
            D(kgPerMetre), "SENTETIK gerekçe", series, size, D(mesh), surface);

        var refusal = ProductDimensionShape.Refusal(request, out var measures);

        Assert.Equal(accepted, refusal is null);
        Assert.Equal(accepted, measures is not null);
    }

    [Fact]
    public void The_weight_table_rows_are_refused_with_the_row_number_and_accepted_sorted_and_canonical()
    {
        Assert.NotNull(ProductProfileWeightTableService.RowsRefusal(null, out _));
        Assert.NotNull(ProductProfileWeightTableService.RowsRefusal([], out _));
        Assert.Contains("2. satır", ProductProfileWeightTableService.RowsRefusal(
            [new("IPE", "200", 22.4m), new("IPE", "200", 22.4m)], out _), StringComparison.Ordinal);
        Assert.Contains("1. satır", ProductProfileWeightTableService.RowsRefusal([new("IPN", "200", 22.4m)], out _),
            StringComparison.Ordinal);
        Assert.Contains("1. satır", ProductProfileWeightTableService.RowsRefusal([new("IPE", "2 00", 22.4m)], out _),
            StringComparison.Ordinal);
        Assert.Contains("1. satır", ProductProfileWeightTableService.RowsRefusal([new("IPE", "200", null)], out _),
            StringComparison.Ordinal);
        Assert.Contains("1. satır", ProductProfileWeightTableService.RowsRefusal([new("IPE", "200", 0m)], out _),
            StringComparison.Ordinal);
        Assert.Contains("1. satır", ProductProfileWeightTableService.RowsRefusal([new("IPE", "200", 22.4000001m)], out _),
            StringComparison.Ordinal);
        var tooMany = Enumerable.Range(1, ProductProfileWeightInputLimitsV1.MaximumRows + 1)
            .Select(index => new ProductProfileWeightRowV1("IPE", index.ToString(System.Globalization.CultureInfo.InvariantCulture), 1m))
            .ToList();
        Assert.NotNull(ProductProfileWeightTableService.RowsRefusal(tooMany, out _));

        Assert.Null(ProductProfileWeightTableService.RowsRefusal(
            [new("IPE", "220", 26.200m), new("HEA", "100", 16.7m), new("IPE", "200", 22.4m)], out var ordered));
        Assert.Equal(["HEA 100", "IPE 200", "IPE 220"], ordered!.Select(row => $"{row.Series} {row.Size}"));
        Assert.Equal("26.2", ordered[2].KgPerMetre.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_canonical_measures_do_not_depend_on_trailing_zeros()
    {
        var typed = new SetProductDimensionRequestV1("0", ProductDimensionSectionKindsV1.Coil, 1.500m, 1000.000m, null, null, null,
            null, null, "r");
        var plain = typed with { ThicknessMm = 1.5m, WidthMm = 1000m };

        Assert.Null(ProductDimensionShape.Refusal(typed, out var first));
        Assert.Null(ProductDimensionShape.Refusal(plain, out var second));
        Assert.Equal(first!.ThicknessMm.ToString(), second!.ThicknessMm.ToString());
        Assert.Equal(first.WidthMm.ToString(), second.WidthMm.ToString());
    }

    [Fact]
    public async Task The_fences_and_malformed_input_answer_before_anything_is_read()
    {
        var anonymous = Service(new FakeUser(null), new FakeAuthority(granted: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anonymous.GetAsync(Guid.NewGuid()));

        var denied = new FakeAuthority(granted: false);
        var ungranted = Service(new FakeUser("user-1"), denied);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ungranted.SetAsync(Guid.NewGuid(), Coil("0"), Metadata()));
        Assert.Equal([ProductDimensionPermissionsV1.Manage], denied.Asked);

        var otherCompany = Service(new FakeUser("user-1", CompanyB), new FakeAuthority(granted: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherCompany.GetAsync(Guid.NewGuid()));

        var granted = new FakeAuthority(granted: true);
        var service = Service(new FakeUser("user-1"), granted);
        Assert.Equal(400, (await Assert.ThrowsAsync<ProductDimensionException>(() => service.GetAsync(Guid.Empty))).StatusCode);
        Assert.Equal(ProductDimensionProblemCodesV1.RequestInvalid,
            (await service.SetAsync(Guid.NewGuid(), Coil("01"), Metadata())).Answer.Code);
        Assert.Equal(ProductDimensionProblemCodesV1.RequestInvalid,
            (await service.SetAsync(Guid.Empty, Coil("0"), Metadata())).Answer.Code);
        var shape = await service.SetAsync(Guid.NewGuid(), Coil("0") with { LengthMm = 2000m }, Metadata());
        Assert.Equal((400, ProductDimensionProblemCodesV1.MeasureInvalid, false), (shape.Answer.StatusCode, shape.Answer.Code,
            shape.IsReplay));
        Assert.Equal(ProductDimensionProblemCodesV1.ReasonRequired,
            (await service.SetAsync(Guid.NewGuid(), Coil("0") with { Reason = "  " }, Metadata())).Answer.Code);
        Assert.Equal(ProductDimensionProblemCodesV1.ReasonTooLong, (await service.SetAsync(Guid.NewGuid(),
            Coil("0") with { Reason = new string('x', ProductDimensionInputLimitsV1.ReasonMaximumLength + 1) }, Metadata())).Answer.Code);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync(Guid.NewGuid(), Coil("0"),
            Metadata() with { ActorId = "user-2" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync(Guid.NewGuid(), Coil("0"),
            Metadata() with { CompanyId = CompanyB }));
    }

    [Fact]
    public async Task The_read_port_denies_an_actor_outside_the_company_without_reading()
    {
        IProductDimensionReaderV1 reader = Service(new FakeUser("user-1", CompanyB), new FakeAuthority(granted: true));
        Assert.Equal(ProductDimensionOutcomeV1.Denied, (await reader.GetAsync(Guid.NewGuid())).Outcome);

        IProductDimensionReaderV1 anonymous = Service(new FakeUser(null), new FakeAuthority(granted: true));
        var answer = await anonymous.GetAsync(Guid.NewGuid());
        Assert.Equal(ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.Denied), answer);
    }

    private static decimal? D(string? text) =>
        text is null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private static SetProductDimensionRequestV1 Coil(string version) =>
        new(version, ProductDimensionSectionKindsV1.Coil, 0.5m, 1000m, null, null, null, null, null, "SENTETIK gerekçe");

    private static ProductCardDimensionRow Row(Guid company) => new()
    {
        ProductId = Guid.NewGuid(), SectionKind = ProductDimensionSectionKindsV1.Coil, ThicknessMm = 0.5m, WidthMm = 1000m,
        Version = 1, UpdatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedBy = "user-1", UpdatedInCompanyId = company,
    };

    private static ProductDimensionCommandMetadataV1 Metadata() =>
        new(CompanyA, "user-1", "corr-1", null, "Web", null, null, "key-1");

    private static ProductDimensionService Service(ICurrentUser user, IErpAuthorityRevalidationService authority)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var context = new ProductPriceListDbContext(new DbContextOptionsBuilder<ProductPriceListDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        return new ProductDimensionService(context, company, user, authority, new NoAudit(), TimeProvider.System);
    }

    private static ProductPriceListDbContext Context()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        return new ProductPriceListDbContext(new DbContextOptionsBuilder<ProductPriceListDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class FakeUser(string? userId, Guid? company = null) : ICurrentUser
    {
        public bool IsAuthenticated => userId is not null;

        public string? UserId => userId;

        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { company ?? CompanyA };

        public bool CanAccessCompany(Guid companyId) => companyId == (company ?? CompanyA);
    }

    private sealed class FakeAuthority(bool granted) : IErpAuthorityRevalidationService
    {
        public List<string> Asked { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(permission);
            return Task.FromResult(granted
                ? new PermissionScope(true, true, new HashSet<string> { "ROLE" }, new HashSet<Guid>(), new HashSet<Guid>(),
                    new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }

    private sealed class NoAudit : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent) => throw new InvalidOperationException("No audit is expected in this test.");
    }
}
