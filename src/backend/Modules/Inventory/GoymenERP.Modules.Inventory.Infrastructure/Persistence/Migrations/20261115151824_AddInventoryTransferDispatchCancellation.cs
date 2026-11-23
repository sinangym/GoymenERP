using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTransferDispatchCancellation : Migration
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

            migrationBuilder.AddColumn<int>(
                name: "SourceScaleSettingAtDispatch",
                schema: "inventory",
                table: "TransferDispatches",
                type: "int",
                nullable: false,
                defaultValue: 1);

            // KLN-D25 (7) / LOJ-D10: a dispatch recorded before this column existed never stored its "kantardan geçer" value.
            // Backfilling Off ("undefined") would invent a value it never had; it is a read error (Unreadable) instead,
            // so the cancel window closes fail-closed and the dispatch surfaces in the scale-check review list.
            migrationBuilder.Sql(
                "UPDATE [inventory].[TransferDispatches] SET [SourceScaleSettingAtDispatch] = 3;");

            migrationBuilder.CreateTable(
                name: "TransferDispatchCancelClosures",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferDispatchCancelClosures", x => new { x.CompanyId, x.TransferId });
                    table.CheckConstraint("CK_TransferDispatchCancelClosures_Basis", "[Basis] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_TransferDispatchCancelClosures_Text", "LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_TransferDispatchCancelClosures_TransferDispatches_CompanyId_TransferId",
                        columns: x => new { x.CompanyId, x.TransferId },
                        principalSchema: "inventory",
                        principalTable: "TransferDispatches",
                        principalColumns: new[] { "CompanyId", "TransferId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferDispatchCancellations",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LogisticsConfirmationVersion = table.Column<long>(type: "bigint", nullable: true),
                    EDispatchConfirmationVersion = table.Column<long>(type: "bigint", nullable: true),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferDispatchCancellations", x => new { x.CompanyId, x.TransferId });
                    table.CheckConstraint("CK_TransferDispatchCancellations_Confirmations", "([LogisticsConfirmationVersion] IS NULL OR [LogisticsConfirmationVersion] >= 1) AND ([EDispatchConfirmationVersion] IS NULL OR [EDispatchConfirmationVersion] >= 1)");
                    table.CheckConstraint("CK_TransferDispatchCancellations_Identity", "[MovementId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_TransferDispatchCancellations_Text", "LEN([Reason]) > 0 AND LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_TransferDispatchCancellations_Movements_CompanyId_MovementId",
                        columns: x => new { x.CompanyId, x.MovementId },
                        principalSchema: "inventory",
                        principalTable: "Movements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferDispatchCancellations_TransferDispatches_CompanyId_TransferId",
                        columns: x => new { x.CompanyId, x.TransferId },
                        principalSchema: "inventory",
                        principalTable: "TransferDispatches",
                        principalColumns: new[] { "CompanyId", "TransferId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transfers_State",
                schema: "inventory",
                table: "Transfers",
                sql: "[Version] >= 1 AND [Status] BETWEEN 1 AND 5 AND [SourceCustodianKind] IN (1, 2) AND [DestinationCustodianKind] IN (1, 2) AND NOT ([SourceLocationId] = [DestinationLocationId] AND [SourceCustodianKind] = [DestinationCustodianKind] AND [SourceCustodianId] = [DestinationCustodianId])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferDispatches_SourceScaleSetting",
                schema: "inventory",
                table: "TransferDispatches",
                sql: "[SourceScaleSettingAtDispatch] IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements",
                sql: "[Kind] BETWEEN 1 AND 11");

            migrationBuilder.CreateIndex(
                name: "IX_TransferDispatchCancellations_CompanyId_MovementId",
                schema: "inventory",
                table: "TransferDispatchCancellations",
                columns: new[] { "CompanyId", "MovementId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix only: a cancelled dispatch (reverse movement kind 11, cancellation row, cancelled status), a
            // persisted window closure or a stored non-default "kantardan geçer" value would be lost or orphaned by
            // dropping the tables, the column or narrowing the checks, so Down refuses while any of them exists.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [inventory].[Movements] WHERE [Kind] = 11)
                    OR EXISTS (SELECT 1 FROM [inventory].[TransferDispatchCancellations])
                    OR EXISTS (SELECT 1 FROM [inventory].[TransferDispatchCancelClosures])
                    OR EXISTS (SELECT 1 FROM [inventory].[TransferDispatches] WHERE [SourceScaleSettingAtDispatch] <> 1)
                    OR EXISTS (SELECT 1 FROM [inventory].[Transfers] WHERE [Status] = 5)
                BEGIN
                    THROW 51823, 'A transfer dispatch cancellation, cancel window closure or stored source scale setting exists; AddInventoryTransferDispatchCancellation is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "TransferDispatchCancelClosures",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "TransferDispatchCancellations",
                schema: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transfers_State",
                schema: "inventory",
                table: "Transfers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferDispatches_SourceScaleSetting",
                schema: "inventory",
                table: "TransferDispatches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "SourceScaleSettingAtDispatch",
                schema: "inventory",
                table: "TransferDispatches");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transfers_State",
                schema: "inventory",
                table: "Transfers",
                sql: "[Version] >= 1 AND [Status] BETWEEN 1 AND 4 AND [SourceCustodianKind] IN (1, 2) AND [DestinationCustodianKind] IN (1, 2) AND NOT ([SourceLocationId] = [DestinationLocationId] AND [SourceCustodianKind] = [DestinationCustodianKind] AND [SourceCustodianId] = [DestinationCustodianId])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements",
                sql: "[Kind] BETWEEN 1 AND 10");
        }
    }
}
