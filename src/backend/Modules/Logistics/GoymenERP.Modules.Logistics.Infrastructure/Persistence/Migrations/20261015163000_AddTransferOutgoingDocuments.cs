using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferOutgoingDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransferOutgoingDocuments",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    EDispatchNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EffectiveAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferOutgoingDocuments", x => new { x.CompanyId, x.DocumentId });
                    table.CheckConstraint("CK_TransferOutgoingDocuments_Actors", "LEN([CreatedBy]) > 0 AND ([EffectiveBy] IS NULL OR LEN([EffectiveBy]) > 0) AND ([CancelledBy] IS NULL OR LEN([CancelledBy]) > 0) AND ([CancellationReason] IS NULL OR LEN([CancellationReason]) > 0) AND ([EDispatchNumber] IS NULL OR LEN([EDispatchNumber]) > 0)");
                    table.CheckConstraint("CK_TransferOutgoingDocuments_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [TransferId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_TransferOutgoingDocuments_State", "([Status] = 1 AND [Version] = 1 AND [EffectiveBy] IS NULL AND [EffectiveAtUtc] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([Status] = 2 AND [Version] = 2 AND [EffectiveBy] IS NOT NULL AND [EffectiveAtUtc] IS NOT NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([Status] = 3 AND [Version] IN (2, 3) AND [CancelledBy] IS NOT NULL AND [CancelledAtUtc] IS NOT NULL AND [CancellationReason] IS NOT NULL AND (([Version] = 2 AND [EffectiveBy] IS NULL AND [EffectiveAtUtc] IS NULL) OR ([Version] = 3 AND [EffectiveBy] IS NOT NULL AND [EffectiveAtUtc] IS NOT NULL)))");
                    table.ForeignKey(
                        name: "FK_TransferOutgoingDocuments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferOutgoingDocumentAttempts",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferOutgoingDocumentAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_TransferOutgoingDocumentAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_TransferOutgoingDocumentAttempts_Identity", "[DocumentId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_TransferOutgoingDocumentAttempts_Result", "ISJSON([ResultJson]) = 1");
                    table.ForeignKey(
                        name: "FK_TransferOutgoingDocumentAttempts_TransferOutgoingDocuments_CompanyId_DocumentId",
                        columns: x => new { x.CompanyId, x.DocumentId },
                        principalSchema: "logistics",
                        principalTable: "TransferOutgoingDocuments",
                        principalColumns: new[] { "CompanyId", "DocumentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferOutgoingDocumentLines",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    TransferLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferOutgoingDocumentLines", x => new { x.CompanyId, x.DocumentId, x.Ordinal });
                    table.CheckConstraint("CK_TransferOutgoingDocumentLines_Identity", "[DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [TransferLineId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_TransferOutgoingDocumentLines_Ordinal", "[Ordinal] >= 1 AND [Ordinal] <= 200");
                    table.ForeignKey(
                        name: "FK_TransferOutgoingDocumentLines_TransferOutgoingDocuments_CompanyId_DocumentId",
                        columns: x => new { x.CompanyId, x.DocumentId },
                        principalSchema: "logistics",
                        principalTable: "TransferOutgoingDocuments",
                        principalColumns: new[] { "CompanyId", "DocumentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransferOutgoingDocumentAttempts_CompanyId_DocumentId",
                schema: "logistics",
                table: "TransferOutgoingDocumentAttempts",
                columns: new[] { "CompanyId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "UX_TransferOutgoingDocumentLines_LineId",
                schema: "logistics",
                table: "TransferOutgoingDocumentLines",
                columns: new[] { "CompanyId", "DocumentId", "TransferLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TransferOutgoingDocuments_TransferId",
                schema: "logistics",
                table: "TransferOutgoingDocuments",
                columns: new[] { "CompanyId", "TransferId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "IF OBJECT_ID(N'[logistics].[TransferOutgoingDocuments]', N'U') IS NOT NULL "
                + "AND EXISTS (SELECT 1 FROM [logistics].[TransferOutgoingDocuments]) "
                + "THROW 51075, 'Transfer outgoing-document history exists; use an explicit forward fix.', 1;");

            migrationBuilder.DropTable(
                name: "TransferOutgoingDocumentAttempts",
                schema: "logistics");

            migrationBuilder.DropTable(
                name: "TransferOutgoingDocumentLines",
                schema: "logistics");

            migrationBuilder.DropTable(
                name: "TransferOutgoingDocuments",
                schema: "logistics");
        }
    }
}
