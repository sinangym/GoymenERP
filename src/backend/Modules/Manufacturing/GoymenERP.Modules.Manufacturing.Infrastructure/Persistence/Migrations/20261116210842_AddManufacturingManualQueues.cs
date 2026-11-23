using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingManualQueues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkCenterQueueVersions",
                schema: "manufacturing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    IsManual = table.Column<bool>(type: "bit", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCenterQueueVersions", x => new { x.CompanyId, x.WorkCenterId, x.Revision });
                    table.CheckConstraint("CK_WorkCenterQueueVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [WorkCenterId] <> '00000000-0000-0000-0000-000000000000' AND [Revision] > 0");
                    table.CheckConstraint("CK_WorkCenterQueueVersions_Text", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0");
                    table.ForeignKey(
                        name: "FK_WorkCenterQueueVersions_WorkCenters_CompanyId_WorkCenterId",
                        columns: x => new { x.CompanyId, x.WorkCenterId },
                        principalSchema: "organization",
                        principalTable: "WorkCenters",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkCenterQueueEntries",
                schema: "manufacturing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCenterQueueEntries", x => new { x.CompanyId, x.WorkCenterId, x.Revision, x.Sequence });
                    table.CheckConstraint("CK_WorkCenterQueueEntries_Sequence", "[Sequence] > 0");
                    table.ForeignKey(
                        name: "FK_WorkCenterQueueEntries_Plans_CompanyId_PlanId",
                        columns: x => new { x.CompanyId, x.PlanId },
                        principalSchema: "manufacturing",
                        principalTable: "Plans",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkCenterQueueEntries_WorkCenterQueueVersions_CompanyId_WorkCenterId_Revision",
                        columns: x => new { x.CompanyId, x.WorkCenterId, x.Revision },
                        principalSchema: "manufacturing",
                        principalTable: "WorkCenterQueueVersions",
                        principalColumns: new[] { "CompanyId", "WorkCenterId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkCenterQueueEntries_CompanyId_PlanId",
                schema: "manufacturing",
                table: "WorkCenterQueueEntries",
                columns: new[] { "CompanyId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkCenterQueueEntries_CompanyId_WorkCenterId_Revision_PlanId",
                schema: "manufacturing",
                table: "WorkCenterQueueEntries",
                columns: new[] { "CompanyId", "WorkCenterId", "Revision", "PlanId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [manufacturing].[WorkCenterQueueVersions])
                    OR EXISTS (SELECT 1 FROM [manufacturing].[WorkCenterQueueEntries])
                BEGIN
                    THROW 51946, 'Recorded work-center queue history exists; AddManufacturingManualQueues is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "WorkCenterQueueEntries",
                schema: "manufacturing");

            migrationBuilder.DropTable(
                name: "WorkCenterQueueVersions",
                schema: "manufacturing");
        }
    }
}
