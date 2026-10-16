using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryPhysicalHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhysicalHolds",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelVersionAtPlacement = table.Column<long>(type: "bigint", nullable: false),
                    OwnerKind = table.Column<int>(type: "int", nullable: false),
                    SourceKind = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PositionOwnerKind = table.Column<int>(type: "int", nullable: false),
                    PositionOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustodianKind = table.Column<int>(type: "int", nullable: false),
                    CustodianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    PlacementPayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    BlockMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedCorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReleaseDispositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReleasePayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ReleaseMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReleaseReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReleaseCorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhysicalHolds", x => x.Id);
                    table.UniqueConstraint("AK_PhysicalHolds_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PhysicalHolds_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [LabelId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND [PositionOwnerId] <> '00000000-0000-0000-0000-000000000000' AND [CustodianId] <> '00000000-0000-0000-0000-000000000000' AND [LocationId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PhysicalHolds_State", "[OwnerKind] >= 1 AND [PositionOwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [LabelVersionAtPlacement] >= 1 AND [QuantityKg] > 0 AND [Version] >= 1 AND (([Status] = 1 AND [ReleasedAtUtc] IS NULL AND [ReleasedBy] IS NULL AND [ReleaseDispositionId] IS NULL AND [ReleasePayloadHash] IS NULL AND [ReleaseMovementId] IS NULL AND [ReleaseReason] IS NULL AND [ReleaseCorrelationId] IS NULL) OR ([Status] = 2 AND [ReleasedAtUtc] IS NOT NULL AND [ReleasedBy] IS NOT NULL AND [ReleaseDispositionId] IS NOT NULL AND [ReleasePayloadHash] IS NOT NULL AND [ReleaseReason] IS NOT NULL AND [ReleaseCorrelationId] IS NOT NULL))");
                    table.CheckConstraint("CK_PhysicalHolds_Text", "LEN([SourceKind]) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([PlacementPayloadHash]) = 64 AND LEN([CreatedBy]) > 0 AND LEN([CreatedCorrelationId]) > 0 AND ([ReleasePayloadHash] IS NULL OR LEN([ReleasePayloadHash]) = 64)");
                    table.ForeignKey(
                        name: "FK_PhysicalHolds_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhysicalHolds_Labels_CompanyId_LabelId",
                        columns: x => new { x.CompanyId, x.LabelId },
                        principalSchema: "inventory",
                        principalTable: "Labels",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhysicalHolds_Locations_CompanyId_LocationId",
                        columns: x => new { x.CompanyId, x.LocationId },
                        principalSchema: "organization",
                        principalTable: "Locations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhysicalHolds_Movements_CompanyId_BlockMovementId",
                        columns: x => new { x.CompanyId, x.BlockMovementId },
                        principalSchema: "inventory",
                        principalTable: "Movements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhysicalHolds_Movements_CompanyId_ReleaseMovementId",
                        columns: x => new { x.CompanyId, x.ReleaseMovementId },
                        principalSchema: "inventory",
                        principalTable: "Movements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalHolds_CompanyId_BlockMovementId",
                schema: "inventory",
                table: "PhysicalHolds",
                columns: new[] { "CompanyId", "BlockMovementId" },
                unique: true,
                filter: "[BlockMovementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalHolds_CompanyId_LabelId_Status_Id",
                schema: "inventory",
                table: "PhysicalHolds",
                columns: new[] { "CompanyId", "LabelId", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalHolds_CompanyId_LocationId",
                schema: "inventory",
                table: "PhysicalHolds",
                columns: new[] { "CompanyId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalHolds_CompanyId_OwnerKind_SourceKind_SourceId",
                schema: "inventory",
                table: "PhysicalHolds",
                columns: new[] { "CompanyId", "OwnerKind", "SourceKind", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalHolds_CompanyId_ReleaseDispositionId",
                schema: "inventory",
                table: "PhysicalHolds",
                columns: new[] { "CompanyId", "ReleaseDispositionId" },
                unique: true,
                filter: "[ReleaseDispositionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalHolds_CompanyId_ReleaseMovementId",
                schema: "inventory",
                table: "PhysicalHolds",
                columns: new[] { "CompanyId", "ReleaseMovementId" },
                unique: true,
                filter: "[ReleaseMovementId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A typed hold is immutable stock provenance. Even a released row still owns its movement,
            // command replay and audit history. Refuse before the table is dropped if the feature has ever
            // carried business truth; an unused installation remains reversibly removable.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [inventory].[PhysicalHolds])
                    OR EXISTS
                    (
                        SELECT 1
                        FROM [inventory].[Movements]
                        WHERE [SourceKind] IN (N'quality.inspection-hold', N'quality.disposition')
                    )
                    OR EXISTS
                    (
                        SELECT 1
                        FROM [inventory].[Commands]
                        WHERE [CommandName] IN
                            (N'inventory.physical-hold.place.v1', N'inventory.physical-hold.release.v1')
                    )
                BEGIN
                    THROW 51065, 'Inventory physical-hold history exists; use a reviewed forward fix.', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "PhysicalHolds",
                schema: "inventory");
        }
    }
}
