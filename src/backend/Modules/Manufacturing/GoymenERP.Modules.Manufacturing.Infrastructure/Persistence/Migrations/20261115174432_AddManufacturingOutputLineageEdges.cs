using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingOutputLineageEdges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutputLineageEdges",
                schema: "manufacturing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutputId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputLabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutputKind = table.Column<int>(type: "int", nullable: false),
                    OutputLabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GenealogyTransformationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumedKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CalculatorVersion = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutputLineageEdges", x => new { x.CompanyId, x.OutputId, x.InputLabelId });
                    table.CheckConstraint("CK_OutputLineageEdges_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [OutputId] <> '00000000-0000-0000-0000-000000000000' AND [InputLabelId] <> '00000000-0000-0000-0000-000000000000' AND [OutputLabelId] <> '00000000-0000-0000-0000-000000000000' AND [GenealogyTransformationId] <> '00000000-0000-0000-0000-000000000000' AND [InputLabelId] <> [OutputLabelId]");
                    table.CheckConstraint("CK_OutputLineageEdges_Mass", "[OutputKind] = 3 AND [ConsumedKg] > 0 AND [CalculatorVersion] >= 1");
                    table.CheckConstraint("CK_OutputLineageEdges_Text", "LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_OutputLineageEdges_Facts_CompanyId_OutputKind_OutputId",
                        columns: x => new { x.CompanyId, x.OutputKind, x.OutputId },
                        principalSchema: "manufacturing",
                        principalTable: "Facts",
                        principalColumns: new[] { "CompanyId", "Kind", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutputLineageEdges_Orders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "manufacturing",
                        principalTable: "Orders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutputLineageEdges_CompanyId_InputLabelId",
                schema: "manufacturing",
                table: "OutputLineageEdges",
                columns: new[] { "CompanyId", "InputLabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutputLineageEdges_CompanyId_OrderId",
                schema: "manufacturing",
                table: "OutputLineageEdges",
                columns: new[] { "CompanyId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutputLineageEdges_CompanyId_OutputKind_OutputId",
                schema: "manufacturing",
                table: "OutputLineageEdges",
                columns: new[] { "CompanyId", "OutputKind", "OutputId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutputLineageEdges_CompanyId_OutputLabelId",
                schema: "manufacturing",
                table: "OutputLineageEdges",
                columns: new[] { "CompanyId", "OutputLabelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [manufacturing].[OutputLineageEdges]) BEGIN THROW 51543, 'Output lineage edges exist; AddManufacturingOutputLineageEdges is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "OutputLineageEdges",
                schema: "manufacturing");
        }
    }
}
