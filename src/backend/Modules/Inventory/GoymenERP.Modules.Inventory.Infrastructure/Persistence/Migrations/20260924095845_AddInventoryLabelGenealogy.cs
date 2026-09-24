using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryLabelGenealogy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LabelTransformations",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelTransformations", x => x.Id);
                    table.UniqueConstraint("AK_LabelTransformations_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_LabelTransformations_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_LabelTransformations_Kind", "[Kind] IN (1, 2)");
                    table.CheckConstraint("CK_LabelTransformations_Text", "LEN([Reason]) > 0 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([PayloadHash]) = 64");
                    table.ForeignKey(
                        name: "FK_LabelTransformations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LabelTransformationInputs",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransformationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelVersion = table.Column<long>(type: "bigint", nullable: false),
                    QuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelTransformationInputs", x => new { x.CompanyId, x.TransformationId, x.Sequence });
                    table.CheckConstraint("CK_LabelTransformationInputs_Sequence", "[Sequence] >= 1");
                    table.CheckConstraint("CK_LabelTransformationInputs_State", "[LabelVersion] >= 1 AND [QuantityKg] > 0");
                    table.ForeignKey(
                        name: "FK_LabelTransformationInputs_LabelTransformations_CompanyId_TransformationId",
                        columns: x => new { x.CompanyId, x.TransformationId },
                        principalSchema: "inventory",
                        principalTable: "LabelTransformations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LabelTransformationInputs_Labels_CompanyId_LabelId",
                        columns: x => new { x.CompanyId, x.LabelId },
                        principalSchema: "inventory",
                        principalTable: "Labels",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LabelTransformationOutputs",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransformationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelVersion = table.Column<long>(type: "bigint", nullable: false),
                    QuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelTransformationOutputs", x => new { x.CompanyId, x.TransformationId, x.Sequence });
                    table.CheckConstraint("CK_LabelTransformationOutputs_Sequence", "[Sequence] >= 1");
                    table.CheckConstraint("CK_LabelTransformationOutputs_State", "[LabelVersion] >= 1 AND [QuantityKg] > 0");
                    table.ForeignKey(
                        name: "FK_LabelTransformationOutputs_LabelTransformations_CompanyId_TransformationId",
                        columns: x => new { x.CompanyId, x.TransformationId },
                        principalSchema: "inventory",
                        principalTable: "LabelTransformations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LabelTransformationOutputs_Labels_CompanyId_LabelId",
                        columns: x => new { x.CompanyId, x.LabelId },
                        principalSchema: "inventory",
                        principalTable: "Labels",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LabelTransformationInputs_CompanyId_LabelId_TransformationId",
                schema: "inventory",
                table: "LabelTransformationInputs",
                columns: new[] { "CompanyId", "LabelId", "TransformationId" });

            migrationBuilder.CreateIndex(
                name: "IX_LabelTransformationInputs_CompanyId_TransformationId_LabelId",
                schema: "inventory",
                table: "LabelTransformationInputs",
                columns: new[] { "CompanyId", "TransformationId", "LabelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabelTransformationOutputs_CompanyId_LabelId_TransformationId",
                schema: "inventory",
                table: "LabelTransformationOutputs",
                columns: new[] { "CompanyId", "LabelId", "TransformationId" });

            migrationBuilder.CreateIndex(
                name: "IX_LabelTransformationOutputs_CompanyId_TransformationId_LabelId",
                schema: "inventory",
                table: "LabelTransformationOutputs",
                columns: new[] { "CompanyId", "TransformationId", "LabelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabelTransformations_CompanyId_OccurredAtUtc_Id",
                schema: "inventory",
                table: "LabelTransformations",
                columns: new[] { "CompanyId", "OccurredAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [inventory].[LabelTransformations])
                   OR EXISTS (SELECT 1 FROM [inventory].[LabelTransformationInputs])
                   OR EXISTS (SELECT 1 FROM [inventory].[LabelTransformationOutputs])
                    THROW 51053, 'Inventory label genealogy exists; use a reviewed forward fix.', 1;
                """);

            migrationBuilder.DropTable(
                name: "LabelTransformationInputs",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "LabelTransformationOutputs",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "LabelTransformations",
                schema: "inventory");
        }
    }
}
