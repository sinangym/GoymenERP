using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements");

            migrationBuilder.CreateTable(
                name: "Transfers",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceCustodianKind = table.Column<int>(type: "int", nullable: false),
                    SourceCustodianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationCustodianKind = table.Column<int>(type: "int", nullable: false),
                    DestinationCustodianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlannedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                    table.UniqueConstraint("AK_Transfers_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_Transfers_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceLocationId] <> '00000000-0000-0000-0000-000000000000' AND [DestinationLocationId] <> '00000000-0000-0000-0000-000000000000' AND [SourceCustodianId] <> '00000000-0000-0000-0000-000000000000' AND [DestinationCustodianId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Transfers_State", "[Version] >= 1 AND [Status] BETWEEN 1 AND 4 AND [SourceCustodianKind] IN (1, 2) AND [DestinationCustodianKind] IN (1, 2) AND NOT ([SourceLocationId] = [DestinationLocationId] AND [SourceCustodianKind] = [DestinationCustodianKind] AND [SourceCustodianId] = [DestinationCustodianId])");
                    table.CheckConstraint("CK_Transfers_Text", "LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_Transfers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Locations_CompanyId_DestinationLocationId",
                        columns: x => new { x.CompanyId, x.DestinationLocationId },
                        principalSchema: "organization",
                        principalTable: "Locations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Locations_CompanyId_SourceLocationId",
                        columns: x => new { x.CompanyId, x.SourceLocationId },
                        principalSchema: "organization",
                        principalTable: "Locations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferDispatches",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DispatchedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferDispatches", x => new { x.CompanyId, x.TransferId });
                    table.CheckConstraint("CK_TransferDispatches_Identity", "[MovementId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND ([DocumentLineId] IS NULL OR [DocumentLineId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_TransferDispatches_Text", "LEN([DocumentKind]) > 0 AND LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_TransferDispatches_Movements_CompanyId_MovementId",
                        columns: x => new { x.CompanyId, x.MovementId },
                        principalSchema: "inventory",
                        principalTable: "Movements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferDispatches_Transfers_CompanyId_TransferId",
                        columns: x => new { x.CompanyId, x.TransferId },
                        principalSchema: "inventory",
                        principalTable: "Transfers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferLines",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedLabelVersion = table.Column<long>(type: "bigint", nullable: false),
                    PlannedKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferLines", x => new { x.CompanyId, x.TransferId, x.LineId });
                    table.CheckConstraint("CK_TransferLines_Identity", "[LineId] <> '00000000-0000-0000-0000-000000000000' AND [LabelId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_TransferLines_State", "[Sequence] >= 1 AND [ExpectedLabelVersion] >= 1 AND [PlannedKg] > 0");
                    table.ForeignKey(
                        name: "FK_TransferLines_Labels_CompanyId_LabelId",
                        columns: x => new { x.CompanyId, x.LabelId },
                        principalSchema: "inventory",
                        principalTable: "Labels",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferLines_Transfers_CompanyId_TransferId",
                        columns: x => new { x.CompanyId, x.TransferId },
                        principalSchema: "inventory",
                        principalTable: "Transfers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferAcceptances",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferAcceptances", x => new { x.CompanyId, x.TransferId, x.LineId });
                    table.CheckConstraint("CK_TransferAcceptances_Identity", "[MovementId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND ([DocumentLineId] IS NULL OR [DocumentLineId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_TransferAcceptances_Text", "LEN([DocumentKind]) > 0 AND LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_TransferAcceptances_Movements_CompanyId_MovementId",
                        columns: x => new { x.CompanyId, x.MovementId },
                        principalSchema: "inventory",
                        principalTable: "Movements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferAcceptances_TransferLines_CompanyId_TransferId_LineId",
                        columns: x => new { x.CompanyId, x.TransferId, x.LineId },
                        principalSchema: "inventory",
                        principalTable: "TransferLines",
                        principalColumns: new[] { "CompanyId", "TransferId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferBuckets",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    OwnerKind = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustodianKind = table.Column<int>(type: "int", nullable: false),
                    CustodianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Block = table.Column<int>(type: "int", nullable: false),
                    QuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferBuckets", x => new { x.CompanyId, x.TransferId, x.LineId, x.Sequence });
                    table.CheckConstraint("CK_TransferBuckets_Identity", "[OwnerId] <> '00000000-0000-0000-0000-000000000000' AND [CustodianId] <> '00000000-0000-0000-0000-000000000000' AND [LabelId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_TransferBuckets_State", "[Sequence] >= 1 AND [OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] IN (1, 2) AND [QuantityKg] > 0");
                    table.ForeignKey(
                        name: "FK_TransferBuckets_Labels_CompanyId_LabelId",
                        columns: x => new { x.CompanyId, x.LabelId },
                        principalSchema: "inventory",
                        principalTable: "Labels",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferBuckets_Locations_CompanyId_LocationId",
                        columns: x => new { x.CompanyId, x.LocationId },
                        principalSchema: "organization",
                        principalTable: "Locations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferBuckets_TransferLines_CompanyId_TransferId_LineId",
                        columns: x => new { x.CompanyId, x.TransferId, x.LineId },
                        principalSchema: "inventory",
                        principalTable: "TransferLines",
                        principalColumns: new[] { "CompanyId", "TransferId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferDispatchLines",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelVersionAfterDispatch = table.Column<long>(type: "bigint", nullable: false),
                    ActualKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DifferenceKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferDispatchLines", x => new { x.CompanyId, x.TransferId, x.LineId });
                    table.CheckConstraint("CK_TransferDispatchLines_State", "[LabelVersionAfterDispatch] >= 2 AND [ActualKg] > 0");
                    table.ForeignKey(
                        name: "FK_TransferDispatchLines_TransferLines_CompanyId_TransferId_LineId",
                        columns: x => new { x.CompanyId, x.TransferId, x.LineId },
                        principalSchema: "inventory",
                        principalTable: "TransferLines",
                        principalColumns: new[] { "CompanyId", "TransferId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements",
                sql: "[Kind] BETWEEN 1 AND 9");

            migrationBuilder.CreateIndex(
                name: "IX_TransferAcceptances_CompanyId_MovementId",
                schema: "inventory",
                table: "TransferAcceptances",
                columns: new[] { "CompanyId", "MovementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferBuckets_CompanyId_LabelId",
                schema: "inventory",
                table: "TransferBuckets",
                columns: new[] { "CompanyId", "LabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferBuckets_CompanyId_LocationId",
                schema: "inventory",
                table: "TransferBuckets",
                columns: new[] { "CompanyId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferBuckets_CompanyId_TransferId_LineId_Block",
                schema: "inventory",
                table: "TransferBuckets",
                columns: new[] { "CompanyId", "TransferId", "LineId", "Block" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferDispatches_CompanyId_MovementId",
                schema: "inventory",
                table: "TransferDispatches",
                columns: new[] { "CompanyId", "MovementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferLines_CompanyId_LabelId",
                schema: "inventory",
                table: "TransferLines",
                columns: new[] { "CompanyId", "LabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferLines_CompanyId_TransferId_LabelId",
                schema: "inventory",
                table: "TransferLines",
                columns: new[] { "CompanyId", "TransferId", "LabelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferLines_CompanyId_TransferId_Sequence",
                schema: "inventory",
                table: "TransferLines",
                columns: new[] { "CompanyId", "TransferId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_CompanyId_DestinationLocationId",
                schema: "inventory",
                table: "Transfers",
                columns: new[] { "CompanyId", "DestinationLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_CompanyId_SourceLocationId",
                schema: "inventory",
                table: "Transfers",
                columns: new[] { "CompanyId", "SourceLocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [inventory].[Transfers])
                   OR EXISTS (SELECT 1 FROM [inventory].[Movements] WHERE [Kind] IN (8, 9))
                    THROW 51053, 'Inventory transfers or transfer movements exist; use a reviewed forward fix.', 1;
                """);

            migrationBuilder.DropTable(
                name: "TransferAcceptances",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "TransferBuckets",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "TransferDispatches",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "TransferDispatchLines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "TransferLines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "Transfers",
                schema: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements",
                sql: "[Kind] BETWEEN 1 AND 7");
        }
    }
}
