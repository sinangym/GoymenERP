using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTransferReceiptDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransferReceiptDocumentVersions",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    PreviousVersion = table.Column<long>(type: "bigint", nullable: true),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceTransferVersion = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LinesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 64000, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinalizationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferReceiptDocumentVersions", x => new { x.CompanyId, x.DocumentId, x.Version });
                    table.CheckConstraint("CK_TransferReceiptDocumentVersions_State", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [TransferId] <> '00000000-0000-0000-0000-000000000000' AND [Version] IN (1,2) AND [SourceTransferVersion] > 0 AND [Status] = [Version] AND (([Version]=1 AND [PreviousVersion] IS NULL) OR ([Version]=2 AND [PreviousVersion] IS NOT NULL AND [PreviousVersion]=1)) AND ISJSON([LinesJson]) = 1 AND JSON_QUERY([LinesJson],'$[0]') IS NOT NULL AND DATEPART(TZOFFSET,[ReceivedAtUtc]) = 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0 AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND (([Version]=1 AND [FinalizationReason] IS NULL) OR ([Version]=2 AND [FinalizationReason] IS NOT NULL AND LEN(LTRIM(RTRIM([FinalizationReason])))>0))");
                    table.ForeignKey(
                        name: "FK_TransferReceiptDocumentVersions_TransferReceiptDocumentVersions_CompanyId_DocumentId_PreviousVersion",
                        columns: x => new { x.CompanyId, x.DocumentId, x.PreviousVersion },
                        principalSchema: "inventory",
                        principalTable: "TransferReceiptDocumentVersions",
                        principalColumns: new[] { "CompanyId", "DocumentId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferReceiptDocumentVersions_Transfers_CompanyId_TransferId",
                        columns: x => new { x.CompanyId, x.TransferId },
                        principalSchema: "inventory",
                        principalTable: "Transfers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransferReceiptDocumentVersions_CompanyId_DocumentId_PreviousVersion",
                schema: "inventory",
                table: "TransferReceiptDocumentVersions",
                columns: new[] { "CompanyId", "DocumentId", "PreviousVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferReceiptDocumentVersions_CompanyId_TransferId_DocumentId_Version",
                schema: "inventory",
                table: "TransferReceiptDocumentVersions",
                columns: new[] { "CompanyId", "TransferId", "DocumentId", "Version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[TransferReceiptDocumentVersions])
                BEGIN
                    THROW 51999, 'Transfer receipt document history exists; AddInventoryTransferReceiptDocuments is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "TransferReceiptDocumentVersions",
                schema: "inventory");
        }
    }
}
