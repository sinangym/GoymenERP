using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations
{
    /// <inheritdoc />
    public partial class AddProductSectionFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ProductProfileWeightLines_Measures",
                schema: "product",
                table: "ProductProfileWeightLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductProfileWeightLines_Shape",
                schema: "product",
                table: "ProductProfileWeightLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductProfileWeightLines_Values",
                schema: "product",
                table: "ProductProfileWeightLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCardDimensions_Measures",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCardDimensions_Shape",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.AlterColumn<decimal>(
                name: "WallThicknessMm",
                schema: "product",
                table: "ProductProfileWeightLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)",
                oldPrecision: 18,
                oldScale: 3);

            migrationBuilder.AlterColumn<string>(
                name: "SectionKind",
                schema: "product",
                table: "ProductProfileWeightLines",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "varchar(16)",
                oldUnicode: false,
                oldMaxLength: 16,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "ProfileSeries",
                schema: "product",
                table: "ProductProfileWeightLines",
                type: "varchar(8)",
                unicode: false,
                maxLength: 8,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "ProfileSize",
                schema: "product",
                table: "ProductProfileWeightLines",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "SectionKind",
                schema: "product",
                table: "ProductCardDimensions",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "varchar(16)",
                oldUnicode: false,
                oldMaxLength: 16,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<decimal>(
                name: "MeshSpacingMm",
                schema: "product",
                table: "ProductCardDimensions",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileSeries",
                schema: "product",
                table: "ProductCardDimensions",
                type: "varchar(8)",
                unicode: false,
                maxLength: 8,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "ProfileSize",
                schema: "product",
                table: "ProductCardDimensions",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "SurfaceFinish",
                schema: "product",
                table: "ProductCardDimensions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                collation: "Turkish_100_CI_AS");

            migrationBuilder.CreateIndex(
                name: "UX_ProductProfileWeightLines_Measures",
                schema: "product",
                table: "ProductProfileWeightLines",
                columns: new[] { "CompanyId", "ImportVersion", "SectionKind", "WidthMm", "HeightMm", "OuterDiameterMm", "WallThicknessMm", "ProfileSeries", "ProfileSize" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductProfileWeightLines_Shape",
                schema: "product",
                table: "ProductProfileWeightLines",
                sql: "([SectionKind] = 'Profile' AND [WidthMm] IS NOT NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NOT NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [WidthMm] > 0 AND [HeightMm] > 0 AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm]) OR ([SectionKind] = 'RoundTube' AND [WidthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [OuterDiameterMm] > 0 AND 2 * [WallThicknessMm] <= [OuterDiameterMm]) OR ([SectionKind] = 'StructuralProfile' AND [WidthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [ProfileSeries] IN ('UPN', 'NPI', 'IPE', 'HEA', 'HEB', 'HEM') AND LEN([ProfileSize]) > 0 AND [ProfileSize] NOT LIKE '%[^A-Z0-9.]%')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductProfileWeightLines_Values",
                schema: "product",
                table: "ProductProfileWeightLines",
                sql: "[LineNo] >= 1 AND ([WallThicknessMm] IS NULL OR [WallThicknessMm] > 0) AND [KgPerMetre] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCardDimensions_Measures",
                schema: "product",
                table: "ProductCardDimensions",
                sql: "([ThicknessMm] IS NULL OR [ThicknessMm] > 0) AND ([WidthMm] IS NULL OR [WidthMm] > 0) AND ([LengthMm] IS NULL OR [LengthMm] > 0) AND ([HeightMm] IS NULL OR [HeightMm] > 0) AND ([OuterDiameterMm] IS NULL OR [OuterDiameterMm] > 0) AND ([WallThicknessMm] IS NULL OR [WallThicknessMm] > 0) AND ([KgPerMetre] IS NULL OR [KgPerMetre] > 0) AND ([MeshSpacingMm] IS NULL OR [MeshSpacingMm] > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCardDimensions_Shape",
                schema: "product",
                table: "ProductCardDimensions",
                sql: "([SectionKind] = 'Sheet' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'Coil' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'RoundTube' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND 2 * [WallThicknessMm] <= [OuterDiameterMm]) OR ([SectionKind] = 'Profile' AND [ThicknessMm] IS NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NOT NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm]) OR ([SectionKind] = 'EqualAngle' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL AND [ThicknessMm] < [WidthMm]) OR ([SectionKind] = 'UnequalAngle' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL AND [ThicknessMm] < [WidthMm] AND [ThicknessMm] < [HeightMm]) OR ([SectionKind] = 'FlatBar' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL AND [ThicknessMm] <= [WidthMm]) OR ([SectionKind] = 'NarrowFlatBar' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL AND [ThicknessMm] <= [WidthMm]) OR ([SectionKind] = 'RoundBar' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'SquareBar' AND [ThicknessMm] IS NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'HexBar' AND [ThicknessMm] IS NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'TeeBar' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL AND [ThicknessMm] < [WidthMm] AND [ThicknessMm] < [HeightMm]) OR ([SectionKind] = 'StructuralProfile' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NOT NULL AND [ProfileSize] IS NOT NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'Rebar' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'WireRod' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'WeldedMesh' AND [ThicknessMm] IS NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NOT NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL AND [OuterDiameterMm] < [MeshSpacingMm]) OR ([SectionKind] = 'ChequeredSheet' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'PerforatedSheet' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'CorrugatedSheet' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'ErwTube' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL AND 2 * [WallThicknessMm] <= [OuterDiameterMm]) OR ([SectionKind] = 'PaintedErwTube' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NOT NULL AND [KgPerMetre] IS NULL AND 2 * [WallThicknessMm] <= [OuterDiameterMm]) OR ([SectionKind] = 'ErwProfile' AND [ThicknessMm] IS NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NOT NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [KgPerMetre] IS NULL AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm]) OR ([SectionKind] = 'PaintedErwProfile' AND [ThicknessMm] IS NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NOT NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NOT NULL AND [KgPerMetre] IS NULL AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCardDimensions_Texts",
                schema: "product",
                table: "ProductCardDimensions",
                sql: "([ProfileSeries] IS NULL OR [ProfileSeries] IN ('UPN', 'NPI', 'IPE', 'HEA', 'HEB', 'HEM')) AND ([ProfileSize] IS NULL OR (LEN([ProfileSize]) > 0 AND [ProfileSize] NOT LIKE '%[^A-Z0-9.]%')) AND ([SurfaceFinish] IS NULL OR LEN([SurfaceFinish]) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a card of a KLN-D87 section kind (or with one of its fields) and a structural or wall-less weight
            // line have no place in the old four-kind shape; while any exists the rollback is refused and nothing changes.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [product].[ProductCardDimensions]
                           WHERE [SectionKind] NOT IN ('Sheet', 'Coil', 'RoundTube', 'Profile')
                              OR [MeshSpacingMm] IS NOT NULL OR [ProfileSeries] IS NOT NULL OR [ProfileSize] IS NOT NULL
                              OR [SurfaceFinish] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [product].[ProductProfileWeightLines]
                              WHERE [SectionKind] NOT IN ('Profile', 'RoundTube') OR [WallThicknessMm] IS NULL
                                 OR [ProfileSeries] IS NOT NULL OR [ProfileSize] IS NOT NULL)
                BEGIN
                    THROW 51737, 'A product card dimension or a profile weight line of a KLN-D87 section kind exists; AddProductSectionFamilies is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "UX_ProductProfileWeightLines_Measures",
                schema: "product",
                table: "ProductProfileWeightLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductProfileWeightLines_Shape",
                schema: "product",
                table: "ProductProfileWeightLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductProfileWeightLines_Values",
                schema: "product",
                table: "ProductProfileWeightLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCardDimensions_Measures",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCardDimensions_Shape",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCardDimensions_Texts",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.DropColumn(
                name: "ProfileSeries",
                schema: "product",
                table: "ProductProfileWeightLines");

            migrationBuilder.DropColumn(
                name: "ProfileSize",
                schema: "product",
                table: "ProductProfileWeightLines");

            migrationBuilder.DropColumn(
                name: "MeshSpacingMm",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.DropColumn(
                name: "ProfileSeries",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.DropColumn(
                name: "ProfileSize",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.DropColumn(
                name: "SurfaceFinish",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.AlterColumn<decimal>(
                name: "WallThicknessMm",
                schema: "product",
                table: "ProductProfileWeightLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)",
                oldPrecision: 18,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SectionKind",
                schema: "product",
                table: "ProductProfileWeightLines",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldUnicode: false,
                oldMaxLength: 32,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "SectionKind",
                schema: "product",
                table: "ProductCardDimensions",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldUnicode: false,
                oldMaxLength: 32,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.CreateIndex(
                name: "UX_ProductProfileWeightLines_Measures",
                schema: "product",
                table: "ProductProfileWeightLines",
                columns: new[] { "CompanyId", "ImportVersion", "SectionKind", "WidthMm", "HeightMm", "OuterDiameterMm", "WallThicknessMm" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductProfileWeightLines_Shape",
                schema: "product",
                table: "ProductProfileWeightLines",
                sql: "([SectionKind] = 'Profile' AND [WidthMm] IS NOT NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL AND [WidthMm] > 0 AND [HeightMm] > 0 AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm]) OR ([SectionKind] = 'RoundTube' AND [WidthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [OuterDiameterMm] > 0 AND 2 * [WallThicknessMm] <= [OuterDiameterMm])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductProfileWeightLines_Values",
                schema: "product",
                table: "ProductProfileWeightLines",
                sql: "[LineNo] >= 1 AND [WallThicknessMm] > 0 AND [KgPerMetre] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCardDimensions_Measures",
                schema: "product",
                table: "ProductCardDimensions",
                sql: "([ThicknessMm] IS NULL OR [ThicknessMm] > 0) AND ([WidthMm] IS NULL OR [WidthMm] > 0) AND ([LengthMm] IS NULL OR [LengthMm] > 0) AND ([HeightMm] IS NULL OR [HeightMm] > 0) AND ([OuterDiameterMm] IS NULL OR [OuterDiameterMm] > 0) AND ([WallThicknessMm] IS NULL OR [WallThicknessMm] > 0) AND ([KgPerMetre] IS NULL OR [KgPerMetre] > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCardDimensions_Shape",
                schema: "product",
                table: "ProductCardDimensions",
                sql: "([SectionKind] = 'Sheet' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'Coil' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'RoundTube' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND 2 * [WallThicknessMm] <= [OuterDiameterMm]) OR ([SectionKind] = 'Profile' AND [ThicknessMm] IS NULL AND [LengthMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm])");
        }
    }
}
