using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

/// <summary>
/// Manufacturing's first schema (MFG-5): plans with their inputs, orders with their input snapshot,
/// and realization facts. Every table is a tenant table; companies and work centers are
/// Organization's, referenced by key only, so this runs after Organization's migrations. Parent
/// rolls and outputs are Inventory label identities with no foreign key (T15 integration).
/// </summary>
[DbContext(typeof(ManufacturingDbContext))]
[Migration("20260918200000_InitialManufacturing")]
public sealed class InitialManufacturing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "manufacturing");

        migrationBuilder.CreateTable(
            name: "Plans",
            schema: "manufacturing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Process = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                TargetBasis = table.Column<int>(type: "int", nullable: true),
                TargetKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Plans", x => x.Id);
                table.UniqueConstraint("AK_Plans_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_Plans_Approval",
                    "([Status] = 1 AND [ApprovedByUserId] IS NULL AND [ApprovedAtUtc] IS NULL) "
                    + "OR ([Status] = 2 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_Plans_Enums", "[Process] IN (1) AND [Status] IN (1, 2)");
                table.CheckConstraint("CK_Plans_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [WorkCenterId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Plans_Target",
                    "([TargetBasis] IS NULL AND [TargetKg] IS NULL) OR ([TargetBasis] IN (1, 2) AND [TargetKg] > 0)");
                table.CheckConstraint("CK_Plans_Version", "[Version] >= 1");
                table.ForeignKey(
                    name: "FK_Plans_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Plans_WorkCenters_CompanyId_WorkCenterId",
                    columns: x => new { x.CompanyId, x.WorkCenterId },
                    principalSchema: "organization",
                    principalTable: "WorkCenters",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PlanInputs",
            schema: "manufacturing",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ParentRollId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                PlannedKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlanInputs", x => new { x.CompanyId, x.PlanId, x.ParentRollId });
                table.CheckConstraint("CK_PlanInputs_Mass", "[PlannedKg] > 0");
                table.CheckConstraint("CK_PlanInputs_Roll", "[ParentRollId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PlanInputs_Sequence", "[Sequence] >= 1");
                table.ForeignKey(
                    name: "FK_PlanInputs_Plans_CompanyId_PlanId",
                    columns: x => new { x.CompanyId, x.PlanId },
                    principalSchema: "manufacturing",
                    principalTable: "Plans",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Orders",
            schema: "manufacturing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Process = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                TargetBasis = table.Column<int>(type: "int", nullable: true),
                TargetKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Orders", x => x.Id);
                table.UniqueConstraint("AK_Orders_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_Orders_Close",
                    "([Status] = 1 AND [ClosedByUserId] IS NULL AND [ClosedAtUtc] IS NULL) "
                    + "OR ([Status] = 2 AND [ClosedByUserId] IS NOT NULL AND [ClosedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_Orders_Enums", "[Process] IN (1) AND [Status] IN (1, 2)");
                table.CheckConstraint("CK_Orders_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [PlanId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [WorkCenterId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Orders_Target",
                    "([TargetBasis] IS NULL AND [TargetKg] IS NULL) OR ([TargetBasis] IN (1, 2) AND [TargetKg] > 0)");
                table.CheckConstraint("CK_Orders_Version", "[Version] >= 1");
                table.ForeignKey(
                    name: "FK_Orders_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Orders_Plans_CompanyId_PlanId",
                    columns: x => new { x.CompanyId, x.PlanId },
                    principalSchema: "manufacturing",
                    principalTable: "Plans",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Orders_WorkCenters_CompanyId_WorkCenterId",
                    columns: x => new { x.CompanyId, x.WorkCenterId },
                    principalSchema: "organization",
                    principalTable: "WorkCenters",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "OrderInputs",
            schema: "manufacturing",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ParentRollId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                PlannedKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderInputs", x => new { x.CompanyId, x.OrderId, x.ParentRollId });
                table.CheckConstraint("CK_OrderInputs_Mass", "[PlannedKg] > 0");
                table.CheckConstraint("CK_OrderInputs_Roll", "[ParentRollId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_OrderInputs_Sequence", "[Sequence] >= 1");
                table.ForeignKey(
                    name: "FK_OrderInputs_Orders_CompanyId_OrderId",
                    columns: x => new { x.CompanyId, x.OrderId },
                    principalSchema: "manufacturing",
                    principalTable: "Orders",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Facts",
            schema: "manufacturing",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ParentRollId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MassKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                OutputClass = table.Column<int>(type: "int", nullable: true),
                CostStatus = table.Column<int>(type: "int", nullable: true),
                PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Facts", x => new { x.CompanyId, x.Kind, x.Id });
                table.CheckConstraint("CK_Facts_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [OrderId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [ParentRollId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Facts_Kind", "[Kind] IN (1, 2, 3, 4)");
                table.CheckConstraint("CK_Facts_Mass", "([Kind] = 4 AND [MassKg] >= 0) OR ([Kind] IN (1, 2, 3) AND [MassKg] > 0)");
                table.CheckConstraint("CK_Facts_Output",
                    "([Kind] = 3 AND [OutputClass] IN (1, 2, 3, 4, 5) AND [CostStatus] IN (1, 2, 3)) "
                    + "OR ([Kind] <> 3 AND [OutputClass] IS NULL AND [CostStatus] IS NULL)");
                table.CheckConstraint("CK_Facts_Text", "LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.ForeignKey(
                    name: "FK_Facts_Orders_CompanyId_OrderId",
                    columns: x => new { x.CompanyId, x.OrderId },
                    principalSchema: "manufacturing",
                    principalTable: "Orders",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Facts_CompanyId_OrderId",
            schema: "manufacturing",
            table: "Facts",
            columns: new[] { "CompanyId", "OrderId" });

        migrationBuilder.CreateIndex(
            name: "IX_OrderInputs_CompanyId_OrderId_Sequence",
            schema: "manufacturing",
            table: "OrderInputs",
            columns: new[] { "CompanyId", "OrderId", "Sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_CompanyId_PlanId",
            schema: "manufacturing",
            table: "Orders",
            columns: new[] { "CompanyId", "PlanId" });

        migrationBuilder.CreateIndex(
            name: "IX_Orders_CompanyId_WorkCenterId",
            schema: "manufacturing",
            table: "Orders",
            columns: new[] { "CompanyId", "WorkCenterId" });

        migrationBuilder.CreateIndex(
            name: "IX_PlanInputs_CompanyId_PlanId_Sequence",
            schema: "manufacturing",
            table: "PlanInputs",
            columns: new[] { "CompanyId", "PlanId", "Sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Plans_CompanyId_WorkCenterId",
            schema: "manufacturing",
            table: "Plans",
            columns: new[] { "CompanyId", "WorkCenterId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Facts", schema: "manufacturing");
        migrationBuilder.DropTable(name: "OrderInputs", schema: "manufacturing");
        migrationBuilder.DropTable(name: "PlanInputs", schema: "manufacturing");
        migrationBuilder.DropTable(name: "Orders", schema: "manufacturing");
        migrationBuilder.DropTable(name: "Plans", schema: "manufacturing");
    }
}
