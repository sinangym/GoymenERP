using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTransferReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transfers_State",
                schema: "inventory",
                table: "Transfers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements");

            migrationBuilder.CreateTable(
                name: "TransferLossRequests",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferLossRequests", x => new { x.CompanyId, x.TransferId, x.LineId });
                    table.CheckConstraint("CK_TransferLossRequests_Text", "LEN(LTRIM(RTRIM([Explanation]))) > 0 AND LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_TransferLossRequests_TransferLines_CompanyId_TransferId_LineId",
                        columns: x => new { x.CompanyId, x.TransferId, x.LineId },
                        principalSchema: "inventory",
                        principalTable: "TransferLines",
                        principalColumns: new[] { "CompanyId", "TransferId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferReceipts",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DispatchedKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ReceivedKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ShortageKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    OverageKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ShortageMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LabelVersionAfterReceipt = table.Column<long>(type: "bigint", nullable: false),
                    OverageReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferReceipts", x => new { x.CompanyId, x.TransferId, x.LineId });
                    table.CheckConstraint("CK_TransferReceipts_Overage", "([OverageKg] = 0 AND [OverageReason] IS NULL) OR ([OverageKg] > 0 AND [OverageReason] IS NOT NULL AND LEN(LTRIM(RTRIM([OverageReason]))) > 0)");
                    table.CheckConstraint("CK_TransferReceipts_Quantities", "[DispatchedKg] > 0 AND [ReceivedKg] > 0 AND [ShortageKg] >= 0 AND [OverageKg] >= 0 AND ([ShortageKg] = 0 OR [OverageKg] = 0) AND [ReceivedKg] + [ShortageKg] - [OverageKg] = [DispatchedKg]");
                    table.CheckConstraint("CK_TransferReceipts_Shortage", "([ShortageKg] = 0 AND [ShortageMovementId] IS NULL) OR ([ShortageKg] > 0 AND [ShortageMovementId] IS NOT NULL AND [ShortageMovementId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_TransferReceipts_State", "[LabelVersionAfterReceipt] >= 3");
                    table.CheckConstraint("CK_TransferReceipts_Text", "LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_TransferReceipts_Movements_CompanyId_ShortageMovementId",
                        columns: x => new { x.CompanyId, x.ShortageMovementId },
                        principalSchema: "inventory",
                        principalTable: "Movements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferReceipts_TransferAcceptances_CompanyId_TransferId_LineId",
                        columns: x => new { x.CompanyId, x.TransferId, x.LineId },
                        principalSchema: "inventory",
                        principalTable: "TransferAcceptances",
                        principalColumns: new[] { "CompanyId", "TransferId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferLossDecisions",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferLossDecisions", x => new { x.CompanyId, x.TransferId, x.LineId });
                    table.CheckConstraint("CK_TransferLossDecisions_Decision", "([Decision] = 1 AND [MovementId] IS NOT NULL AND [MovementId] <> '00000000-0000-0000-0000-000000000000') OR ([Decision] = 2 AND [MovementId] IS NULL)");
                    table.CheckConstraint("CK_TransferLossDecisions_Text", "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([PayloadHash]) = 64 AND LEN([DecidedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_TransferLossDecisions_Movements_CompanyId_MovementId",
                        columns: x => new { x.CompanyId, x.MovementId },
                        principalSchema: "inventory",
                        principalTable: "Movements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferLossDecisions_TransferLossRequests_CompanyId_TransferId_LineId",
                        columns: x => new { x.CompanyId, x.TransferId, x.LineId },
                        principalSchema: "inventory",
                        principalTable: "TransferLossRequests",
                        principalColumns: new[] { "CompanyId", "TransferId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferOverageDecisions",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferOverageDecisions", x => new { x.CompanyId, x.TransferId, x.LineId });
                    table.CheckConstraint("CK_TransferOverageDecisions_Decision", "([Decision] = 1 AND [MovementId] IS NOT NULL AND [MovementId] <> '00000000-0000-0000-0000-000000000000') OR ([Decision] = 2 AND [MovementId] IS NULL)");
                    table.CheckConstraint("CK_TransferOverageDecisions_Text", "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([PayloadHash]) = 64 AND LEN([DecidedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_TransferOverageDecisions_Movements_CompanyId_MovementId",
                        columns: x => new { x.CompanyId, x.MovementId },
                        principalSchema: "inventory",
                        principalTable: "Movements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferOverageDecisions_TransferReceipts_CompanyId_TransferId_LineId",
                        columns: x => new { x.CompanyId, x.TransferId, x.LineId },
                        principalSchema: "inventory",
                        principalTable: "TransferReceipts",
                        principalColumns: new[] { "CompanyId", "TransferId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transfers_State",
                schema: "inventory",
                table: "Transfers",
                sql: "[Version] >= 1 AND [Status] BETWEEN 1 AND 6 AND [SourceCustodianKind] IN (1, 2) AND [DestinationCustodianKind] IN (1, 2) AND NOT ([SourceLocationId] = [DestinationLocationId] AND [SourceCustodianKind] = [DestinationCustodianKind] AND [SourceCustodianId] = [DestinationCustodianId])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements",
                sql: "[Kind] BETWEEN 1 AND 14");

            migrationBuilder.CreateIndex(
                name: "IX_TransferLossDecisions_CompanyId_MovementId",
                schema: "inventory",
                table: "TransferLossDecisions",
                columns: new[] { "CompanyId", "MovementId" },
                unique: true,
                filter: "[MovementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOverageDecisions_CompanyId_MovementId",
                schema: "inventory",
                table: "TransferOverageDecisions",
                columns: new[] { "CompanyId", "MovementId" },
                unique: true,
                filter: "[MovementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TransferReceipts_CompanyId_ShortageMovementId",
                schema: "inventory",
                table: "TransferReceipts",
                columns: new[] { "CompanyId", "ShortageMovementId" },
                unique: true,
                filter: "[ShortageMovementId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix only: a recorded receipt (with its automatic shortage fire, kind 12), an overage decision
            // (kind 13), a loss request or decision (transit loss, kind 14) or a closed transfer (status 6) would be lost
            // or orphaned by dropping the tables or narrowing the checks, so Down refuses while any of them exists.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [inventory].[Movements] WHERE [Kind] IN (12, 13, 14))
                    OR EXISTS (SELECT 1 FROM [inventory].[TransferReceipts])
                    OR EXISTS (SELECT 1 FROM [inventory].[TransferOverageDecisions])
                    OR EXISTS (SELECT 1 FROM [inventory].[TransferLossRequests])
                    OR EXISTS (SELECT 1 FROM [inventory].[TransferLossDecisions])
                    OR EXISTS (SELECT 1 FROM [inventory].[Transfers] WHERE [Status] = 6)
                BEGIN
                    THROW 51646, 'A transfer receipt, overage decision, loss request or decision, or closed transfer exists; AddInventoryTransferReceipts is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "TransferLossDecisions",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "TransferOverageDecisions",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "TransferLossRequests",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "TransferReceipts",
                schema: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transfers_State",
                schema: "inventory",
                table: "Transfers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transfers_State",
                schema: "inventory",
                table: "Transfers",
                sql: "[Version] >= 1 AND [Status] BETWEEN 1 AND 5 AND [SourceCustodianKind] IN (1, 2) AND [DestinationCustodianKind] IN (1, 2) AND NOT ([SourceLocationId] = [DestinationLocationId] AND [SourceCustodianKind] = [DestinationCustodianKind] AND [SourceCustodianId] = [DestinationCustodianId])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements",
                sql: "[Kind] BETWEEN 1 AND 11");
        }
    }
}
