using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations
{
    /// <summary>
    /// KLN-D48/KLN-D54 (STK-S37): the new section families of the shared product card (long products, structural profile by
    /// standard series and size, rebar/wire rod/mesh, sheet derivatives, ERW tube/profile, painted ERW with its surface
    /// finish) widen <c>CK_ProductCardDimensions_Shape</c>, and the company-scoped, versioned structural profile weight table
    /// (<c>product.ProductProfileWeightTables</c>/<c>ProductProfileWeightRows</c>) with its command answers
    /// (<c>product.ProductProfileWeightAttempts</c>) is added. <see cref="Down"/> is forward-fix: it refuses with THROW 51391
    /// while a new-family row, a weight table or a command answer exists; otherwise it rolls back freely.
    /// </summary>
    public partial class AddProductSectionFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCardDimensions_Measures",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCardDimensions_Shape",
                schema: "product",
                table: "ProductCardDimensions");

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
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductProfileWeightAttempts",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    AnswerJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductProfileWeightAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_ProductProfileWeightAttempts_Answer", "[StatusCode] BETWEEN 200 AND 599 AND ISJSON([AnswerJson]) = 1");
                    table.CheckConstraint("CK_ProductProfileWeightAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_ProductProfileWeightAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.ForeignKey(
                        name: "FK_ProductProfileWeightAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductProfileWeightTables",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    SourceLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ImportedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductProfileWeightTables", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_ProductProfileWeightTables_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1");
                    table.CheckConstraint("CK_ProductProfileWeightTables_RowCount", "[RowCount] >= 1");
                    table.CheckConstraint("CK_ProductProfileWeightTables_Text", "LEN([SourceLabel]) > 0 AND LEN([Reason]) > 0 AND LEN([ImportedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_ProductProfileWeightTables_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductProfileWeightRows",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TableVersion = table.Column<long>(type: "bigint", nullable: false),
                    ProfileSeries = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProfileSize = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    KgPerMetre = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductProfileWeightRows", x => new { x.CompanyId, x.TableVersion, x.ProfileSeries, x.ProfileSize });
                    table.CheckConstraint("CK_ProductProfileWeightRows_Row", "[ProfileSeries] IN ('UPN', 'NPI', 'IPE', 'HEA', 'HEB', 'HEM') AND LEN([ProfileSize]) > 0 AND [ProfileSize] NOT LIKE '%[^A-Z0-9.]%' AND [KgPerMetre] > 0");
                    table.ForeignKey(
                        name: "FK_ProductProfileWeightRows_ProductProfileWeightTables_CompanyId_TableVersion",
                        columns: x => new { x.CompanyId, x.TableVersion },
                        principalSchema: "product",
                        principalTable: "ProductProfileWeightTables",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCardDimensions_Measures",
                schema: "product",
                table: "ProductCardDimensions",
                sql: "([ThicknessMm] IS NULL OR [ThicknessMm] > 0) AND ([WidthMm] IS NULL OR [WidthMm] > 0) AND ([LengthMm] IS NULL OR [LengthMm] > 0) AND ([HeightMm] IS NULL OR [HeightMm] > 0) AND ([OuterDiameterMm] IS NULL OR [OuterDiameterMm] > 0) AND ([WallThicknessMm] IS NULL OR [WallThicknessMm] > 0) AND ([KgPerMetre] IS NULL OR [KgPerMetre] > 0) AND ([MeshSpacingMm] IS NULL OR [MeshSpacingMm] > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCardDimensions_Shape",
                schema: "product",
                table: "ProductCardDimensions",
                sql: "([SectionKind] = 'Sheet' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL) OR ([SectionKind] = 'Coil' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL) OR ([SectionKind] = 'RoundTube' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND 2 * [WallThicknessMm] <= [OuterDiameterMm]) OR ([SectionKind] = 'Profile' AND [ThicknessMm] IS NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NOT NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NOT NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm]) OR ([SectionKind] = 'EqualAngle' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [ThicknessMm] < [WidthMm]) OR ([SectionKind] = 'UnequalAngle' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [ThicknessMm] < [WidthMm] AND [ThicknessMm] < [HeightMm]) OR ([SectionKind] = 'FlatBar' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [ThicknessMm] <= [WidthMm]) OR ([SectionKind] = 'NarrowFlatBar' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [ThicknessMm] <= [WidthMm]) OR ([SectionKind] = 'RoundBar' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL) OR ([SectionKind] = 'SquareBar' AND [ThicknessMm] IS NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL) OR ([SectionKind] = 'HexBar' AND [ThicknessMm] IS NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL) OR ([SectionKind] = 'TeeBar' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [ThicknessMm] < [WidthMm] AND [ThicknessMm] < [HeightMm]) OR ([SectionKind] = 'StructuralProfile' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NOT NULL AND [ProfileSize] IS NOT NULL AND [SurfaceFinish] IS NULL) OR ([SectionKind] = 'Rebar' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL) OR ([SectionKind] = 'WireRod' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL) OR ([SectionKind] = 'WeldedMesh' AND [ThicknessMm] IS NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NOT NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND [OuterDiameterMm] < [MeshSpacingMm]) OR ([SectionKind] = 'ChequeredSheet' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL) OR ([SectionKind] = 'PerforatedSheet' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL) OR ([SectionKind] = 'CorrugatedSheet' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL) OR ([SectionKind] = 'ErwTube' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND 2 * [WallThicknessMm] <= [OuterDiameterMm]) OR ([SectionKind] = 'ErwProfile' AND [ThicknessMm] IS NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NOT NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NOT NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NULL AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm]) OR ([SectionKind] = 'PaintedErwTube' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND [MeshSpacingMm] IS NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NOT NULL AND 2 * [WallThicknessMm] <= [OuterDiameterMm]) OR ([SectionKind] = 'PaintedErwProfile' AND [ThicknessMm] IS NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NOT NULL AND [MeshSpacingMm] IS NULL AND [KgPerMetre] IS NOT NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL AND [SurfaceFinish] IS NOT NULL AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCardDimensions_Text",
                schema: "product",
                table: "ProductCardDimensions",
                sql: "([ProfileSeries] IS NULL OR [ProfileSeries] IN ('UPN', 'NPI', 'IPE', 'HEA', 'HEB', 'HEM')) AND ([ProfileSize] IS NULL OR (LEN([ProfileSize]) > 0 AND [ProfileSize] NOT LIKE '%[^A-Z0-9.]%')) AND ([SurfaceFinish] IS NULL OR LEN([SurfaceFinish]) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [product].[ProductCardDimensions] WHERE [SectionKind] NOT IN (N'Sheet', N'Coil', N'RoundTube', N'Profile') OR [ProfileSeries] IS NOT NULL OR [ProfileSize] IS NOT NULL OR [MeshSpacingMm] IS NOT NULL OR [SurfaceFinish] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [product].[ProductProfileWeightTables])
                    OR EXISTS (SELECT 1 FROM [product].[ProductProfileWeightAttempts])
                BEGIN
                    THROW 51391, 'Section family rows or profile weight tables exist; AddProductSectionFamilies is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "ProductProfileWeightAttempts",
                schema: "product");

            migrationBuilder.DropTable(
                name: "ProductProfileWeightRows",
                schema: "product");

            migrationBuilder.DropTable(
                name: "ProductProfileWeightTables",
                schema: "product");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCardDimensions_Measures",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCardDimensions_Shape",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCardDimensions_Text",
                schema: "product",
                table: "ProductCardDimensions");

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

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCardDimensions_Measures",
                schema: "product",
                table: "ProductCardDimensions",
                sql: "([ThicknessMm] IS NULL OR [ThicknessMm] > 0) AND ([WidthMm] IS NULL OR [WidthMm] > 0) AND ([LengthMm] IS NULL OR [LengthMm] > 0) AND ([HeightMm] IS NULL OR [HeightMm] > 0) AND ([OuterDiameterMm] IS NULL OR [OuterDiameterMm] > 0) AND ([WallThicknessMm] IS NULL OR [WallThicknessMm] > 0) AND ([KgPerMetre] IS NULL OR [KgPerMetre] > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCardDimensions_Shape",
                schema: "product",
                table: "ProductCardDimensions",
                sql: "([SectionKind] = 'Sheet' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'Coil' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'RoundTube' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND 2 * [WallThicknessMm] <= [OuterDiameterMm]) OR ([SectionKind] = 'Profile' AND [ThicknessMm] IS NULL AND [LengthMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND [KgPerMetre] IS NOT NULL AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm])");
        }
    }
}
