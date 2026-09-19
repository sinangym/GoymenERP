using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;

/// <summary>
/// Logistics's first schema (LOG-4a; ADR-002, ADR-009, ADR-011; the Sales SALES-P2 shape): the loading
/// instruction header, its lines with the four share totals and the Sales binding, the append-only share
/// list (load / shipment / delivery) and the ADR-011 attempt record. All are tenant tables; companies are
/// Organization's, referenced by key only, so this runs after Organization's migrations. The attempt
/// record carries the §7 outcome from the start (a stored instruction or a recorded terminal refusal),
/// and its "one create attempt per stored instruction" index is filtered to the create command as the
/// Sales lesson taught (the mutation commands' attempts share the instruction id).
/// </summary>
[DbContext(typeof(LogisticsDbContext))]
[Migration("20260919150000_InitialLogistics")]
public sealed class InitialLogistics : Migration
{
    private const string NotEmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "logistics");

        migrationBuilder.CreateTable(
            name: "LoadingInstructions",
            schema: "logistics",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CustomerPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LoadingInstructions", x => x.Id);
                table.UniqueConstraint("AK_LoadingInstructions_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_LoadingInstructions_Identity",
                    "[Id] <> " + NotEmptyGuid + " AND [CompanyId] <> " + NotEmptyGuid + " "
                    + "AND [CustomerPartyId] <> " + NotEmptyGuid + " AND [CompanyAccountId] <> " + NotEmptyGuid);
                table.CheckConstraint("CK_LoadingInstructions_Status", "[Status] IN (1, 2)");
                table.CheckConstraint("CK_LoadingInstructions_Version", "[Version] >= 1");
                table.CheckConstraint("CK_LoadingInstructions_Text", "LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.ForeignKey(
                    name: "FK_LoadingInstructions_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LoadingInstructionLines",
            schema: "logistics",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LineNo = table.Column<int>(type: "int", nullable: false),
                LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceOrderRevision = table.Column<int>(type: "int", nullable: false),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitIsKilogram = table.Column<bool>(type: "bit", nullable: false),
                AllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Instructed = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Loaded = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Shipped = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Delivered = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                IsBlocked = table.Column<bool>(type: "bit", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LoadingInstructionLines", x => new { x.CompanyId, x.InstructionId, x.LineNo });
                table.CheckConstraint("CK_LoadingInstructionLines_LineNo", "[LineNo] >= 1");
                table.CheckConstraint("CK_LoadingInstructionLines_Identity",
                    "[LineId] <> " + NotEmptyGuid + " AND [SourceOrderId] <> " + NotEmptyGuid + " AND [SourceOrderLineId] <> " + NotEmptyGuid + " "
                    + "AND [ProductId] <> " + NotEmptyGuid + " AND [UnitId] <> " + NotEmptyGuid + " AND ([AllocationId] IS NULL OR [AllocationId] <> " + NotEmptyGuid + ")");
                table.CheckConstraint("CK_LoadingInstructionLines_Revision", "[SourceOrderRevision] >= 0");
                // §7.4 / §13: instructed ≥ loaded ≥ shipped ≥ delivered ≥ 0, never counted twice.
                table.CheckConstraint("CK_LoadingInstructionLines_Shares",
                    "[Instructed] > 0 AND [Loaded] >= 0 AND [Shipped] >= 0 AND [Delivered] >= 0 "
                    + "AND [Loaded] <= [Instructed] AND [Shipped] <= [Loaded] AND [Delivered] <= [Shipped]");
                table.ForeignKey(
                    name: "FK_LoadingInstructionLines_LoadingInstructions_CompanyId_InstructionId",
                    columns: x => new { x.CompanyId, x.InstructionId },
                    principalSchema: "logistics",
                    principalTable: "LoadingInstructions",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LoadingShares",
            schema: "logistics",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ShareNo = table.Column<int>(type: "int", nullable: false),
                ShareId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Measurement = table.Column<int>(type: "int", nullable: true),
                SourceShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LoadingShares", x => new { x.CompanyId, x.InstructionId, x.ShareNo });
                table.CheckConstraint("CK_LoadingShares_ShareNo", "[ShareNo] >= 1");
                table.CheckConstraint("CK_LoadingShares_Identity", "[ShareId] <> " + NotEmptyGuid + " AND [LineId] <> " + NotEmptyGuid);
                table.CheckConstraint("CK_LoadingShares_Quantity", "[Quantity] > 0");
                // A load names how it was measured; a delivery names its source shipment; a shipment names neither.
                table.CheckConstraint("CK_LoadingShares_Kind",
                    "([Kind] = 1 AND [Measurement] IN (1, 2) AND [SourceShipmentId] IS NULL) "
                    + "OR ([Kind] = 2 AND [Measurement] IS NULL AND [SourceShipmentId] IS NULL) "
                    + "OR ([Kind] = 3 AND [Measurement] IS NULL AND [SourceShipmentId] IS NOT NULL AND [SourceShipmentId] <> " + NotEmptyGuid + ")");
                table.ForeignKey(
                    name: "FK_LoadingShares_LoadingInstructions_CompanyId_InstructionId",
                    columns: x => new { x.CompanyId, x.InstructionId },
                    principalSchema: "logistics",
                    principalTable: "LoadingInstructions",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LoadingAttempts",
            schema: "logistics",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Outcome = table.Column<int>(type: "int", nullable: false),
                InstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LoadingAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                table.CheckConstraint("CK_LoadingAttempts_Identity",
                    "[CompanyId] <> " + NotEmptyGuid + " AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                table.CheckConstraint("CK_LoadingAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                // ADR-011 §7: a stored result or a recorded terminal refusal, never both.
                table.CheckConstraint("CK_LoadingAttempts_Outcome",
                    "([Outcome] = 1 AND [InstructionId] IS NOT NULL AND [InstructionId] <> " + NotEmptyGuid + " AND [RefusalJson] IS NULL) "
                    + "OR ([Outcome] = 2 AND [InstructionId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                table.ForeignKey(
                    name: "FK_LoadingAttempts_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LoadingAttempts_LoadingInstructions_CompanyId_InstructionId",
                    columns: x => new { x.CompanyId, x.InstructionId },
                    principalSchema: "logistics",
                    principalTable: "LoadingInstructions",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LoadingInstructions_CompanyId_CreatedAtUtc_Id",
            schema: "logistics",
            table: "LoadingInstructions",
            columns: new[] { "CompanyId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_LoadingInstructions_CompanyId_Status_CreatedAtUtc_Id",
            schema: "logistics",
            table: "LoadingInstructions",
            columns: new[] { "CompanyId", "Status", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "UX_LoadingInstructionLines_LineId",
            schema: "logistics",
            table: "LoadingInstructionLines",
            columns: new[] { "CompanyId", "InstructionId", "LineId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_LoadingInstructionLines_SourceOrderLine",
            schema: "logistics",
            table: "LoadingInstructionLines",
            columns: new[] { "CompanyId", "SourceOrderId", "SourceOrderLineId" });

        migrationBuilder.CreateIndex(
            name: "UX_LoadingShares_ShareId",
            schema: "logistics",
            table: "LoadingShares",
            columns: new[] { "CompanyId", "InstructionId", "ShareId" },
            unique: true);

        // One create attempt per stored instruction; the mutation commands' attempts share the instruction id.
        migrationBuilder.CreateIndex(
            name: "UX_LoadingAttempts_InstructionId",
            schema: "logistics",
            table: "LoadingAttempts",
            columns: new[] { "CompanyId", "InstructionId" },
            unique: true,
            filter: "[InstructionId] IS NOT NULL AND [CommandName] = N'logistics.loading-instruction.create.v1'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LoadingAttempts", schema: "logistics");
        migrationBuilder.DropTable(name: "LoadingShares", schema: "logistics");
        migrationBuilder.DropTable(name: "LoadingInstructionLines", schema: "logistics");
        migrationBuilder.DropTable(name: "LoadingInstructions", schema: "logistics");
    }
}
