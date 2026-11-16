using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeighingDifferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeighingDifferences",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DispatchNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TheoreticalKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ScaleKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DifferenceKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    BandLevel = table.Column<int>(type: "int", nullable: false),
                    WarningBandPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    ApprovalBandPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    BlockBandPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    TotalExcessKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ApprovalRatePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    EnteredByComplete = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeighingDifferences", x => new { x.CompanyId, x.RecordId });
                    table.CheckConstraint("CK_WeighingDifferences_Actors", "LEN([RecordedBy]) > 0 AND ([ApprovedBy] IS NULL OR LEN([ApprovedBy]) > 0)");
                    table.CheckConstraint("CK_WeighingDifferences_Bands", "([BandLevel] = 0 AND [WarningBandPercent] IS NULL AND [ApprovalBandPercent] IS NULL AND [BlockBandPercent] IS NULL) OR ([BandLevel] BETWEEN 1 AND 4 AND [WarningBandPercent] > 0 AND [ApprovalBandPercent] > [WarningBandPercent] AND [BlockBandPercent] > [ApprovalBandPercent])");
                    table.CheckConstraint("CK_WeighingDifferences_Decision", "([Decision] = 1 AND [TotalExcessKg] = 0) OR ([Decision] = 2 AND [TotalExcessKg] > 0 AND [ApprovalRatePercent] IS NOT NULL) OR ([Decision] = 3 AND [TotalExcessKg] > 0)");
                    table.CheckConstraint("CK_WeighingDifferences_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [RecordId] <> '00000000-0000-0000-0000-000000000000' AND [InstructionId] <> '00000000-0000-0000-0000-000000000000' AND [DispatchNoteId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_WeighingDifferences_Quantities", "[TheoreticalKg] > 0 AND [ScaleKg] >= 0 AND [TotalExcessKg] >= 0 AND [DifferenceKg] = [ScaleKg] - [TheoreticalKg]");
                    table.CheckConstraint("CK_WeighingDifferences_Rate", "[ApprovalRatePercent] IS NULL OR ([ApprovalRatePercent] > 0 AND [ApprovalRatePercent] <= 100)");
                    table.CheckConstraint("CK_WeighingDifferences_State", "([Status] = 1 AND [Version] = 1 AND [Decision] IN (1, 2) AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([Status] = 2 AND [Version] = 1 AND [Decision] = 3 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([Status] = 3 AND [Version] = 2 AND [Decision] = 3 AND [EnteredByComplete] = 1 AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_WeighingDifferences_DispatchNotes_CompanyId_InstructionId_DispatchNoteId",
                        columns: x => new { x.CompanyId, x.InstructionId, x.DispatchNoteId },
                        principalSchema: "logistics",
                        principalTable: "DispatchNotes",
                        principalColumns: new[] { "CompanyId", "InstructionId", "DispatchNoteId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeighingDifferenceAttempts",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeighingDifferenceAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_WeighingDifferenceAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_WeighingDifferenceAttempts_Identity", "[RecordId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_WeighingDifferenceAttempts_Result", "ISJSON([ResultJson]) = 1");
                    table.ForeignKey(
                        name: "FK_WeighingDifferenceAttempts_WeighingDifferences_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalSchema: "logistics",
                        principalTable: "WeighingDifferences",
                        principalColumns: new[] { "CompanyId", "RecordId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeighingDifferenceEnteredBy",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeighingDifferenceEnteredBy", x => new { x.CompanyId, x.RecordId, x.ActorId });
                    table.CheckConstraint("CK_WeighingDifferenceEnteredBy_Actor", "[RecordId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ActorId]) > 0");
                    table.ForeignKey(
                        name: "FK_WeighingDifferenceEnteredBy_WeighingDifferences_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalSchema: "logistics",
                        principalTable: "WeighingDifferences",
                        principalColumns: new[] { "CompanyId", "RecordId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeighingDifferenceProducts",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NoteKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ScaleKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DifferenceKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    RemainingPhysicalKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PhysicallyCoveredKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ExcessKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    TargetLineNo = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeighingDifferenceProducts", x => new { x.CompanyId, x.RecordId, x.Ordinal });
                    table.CheckConstraint("CK_WeighingDifferenceProducts_Identity", "[RecordId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [Ordinal] >= 1 AND [Ordinal] <= 200");
                    table.CheckConstraint("CK_WeighingDifferenceProducts_Quantities", "[NoteKg] > 0 AND [ScaleKg] >= 0 AND [RemainingPhysicalKg] >= 0 AND [PhysicallyCoveredKg] >= 0 AND [ExcessKg] >= 0 AND [DifferenceKg] = [ScaleKg] - [NoteKg] AND [PhysicallyCoveredKg] <= [RemainingPhysicalKg] AND [PhysicallyCoveredKg] + [ExcessKg] = CASE WHEN [DifferenceKg] > 0 THEN [DifferenceKg] ELSE 0 END");
                    table.CheckConstraint("CK_WeighingDifferenceProducts_Target", "([ExcessKg] = 0 AND [TargetLineNo] IS NULL) OR ([ExcessKg] > 0 AND [TargetLineNo] >= 1)");
                    table.ForeignKey(
                        name: "FK_WeighingDifferenceProducts_WeighingDifferences_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalSchema: "logistics",
                        principalTable: "WeighingDifferences",
                        principalColumns: new[] { "CompanyId", "RecordId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeighingDifferenceAttempts_CompanyId_RecordId",
                schema: "logistics",
                table: "WeighingDifferenceAttempts",
                columns: new[] { "CompanyId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "UX_WeighingDifferenceProducts_ProductId",
                schema: "logistics",
                table: "WeighingDifferenceProducts",
                columns: new[] { "CompanyId", "RecordId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeighingDifferences_CompanyId_InstructionId_DispatchNoteId",
                schema: "logistics",
                table: "WeighingDifferences",
                columns: new[] { "CompanyId", "InstructionId", "DispatchNoteId" });

            migrationBuilder.CreateIndex(
                name: "UX_WeighingDifferences_DispatchNoteId",
                schema: "logistics",
                table: "WeighingDifferences",
                columns: new[] { "CompanyId", "DispatchNoteId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a stored weighing-difference record (and its approval) is the sale's evidence; dropping it would
            // erase who approved what. An empty store still rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [logistics].[WeighingDifferences])
                    OR EXISTS (SELECT 1 FROM [logistics].[WeighingDifferenceProducts])
                    OR EXISTS (SELECT 1 FROM [logistics].[WeighingDifferenceEnteredBy])
                    OR EXISTS (SELECT 1 FROM [logistics].[WeighingDifferenceAttempts])
                BEGIN
                    THROW 51761, 'Weighing-difference records exist; AddWeighingDifferences is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "WeighingDifferenceAttempts",
                schema: "logistics");

            migrationBuilder.DropTable(
                name: "WeighingDifferenceEnteredBy",
                schema: "logistics");

            migrationBuilder.DropTable(
                name: "WeighingDifferenceProducts",
                schema: "logistics");

            migrationBuilder.DropTable(
                name: "WeighingDifferences",
                schema: "logistics");
        }
    }
}
