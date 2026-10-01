using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficialInvoiceImportApprovalV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OfficialInvoiceImportBatches",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    BatchChecksum = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    TemplateVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    FileBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SourceRowsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialInvoiceImportBatches", x => x.Id);
                    table.UniqueConstraint("AK_OfficialInvoiceImportBatches_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_OfficialInvoiceImportBatches_Content", "LEN([BatchVersion]) > 0 AND LEN([BatchChecksum]) = 64 AND LEN([TemplateVersion]) > 0 AND DATALENGTH([FileBytes]) > 0 AND ISJSON([SourceRowsJson]) = 1 AND [RowCount] > 0 AND LEN([UploadedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_OfficialInvoiceImportBatches_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_OfficialInvoiceImportBatches_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfficialInvoiceImportCommands",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialInvoiceImportCommands", x => x.Id);
                    table.CheckConstraint("CK_OfficialInvoiceImportCommands_Content", "LEN([CommandName]) > 0 AND LEN([IdempotencyKeyHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([PayloadHash]) = 64 AND ISJSON([ResultJson]) = 1 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_OfficialInvoiceImportCommands_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [BatchId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_OfficialInvoiceImportCommands_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfficialInvoiceImportApprovals",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialInvoiceImportApprovals", x => x.Id);
                    table.CheckConstraint("CK_OfficialInvoiceImportApprovals_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [BatchId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_OfficialInvoiceImportApprovals_MakerChecker", "LEN([UploadedBy]) > 0 AND LEN([ApprovedBy]) > 0 AND [UploadedBy] <> [ApprovedBy] AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_OfficialInvoiceImportApprovals_OfficialInvoiceImportBatches_CompanyId_BatchId",
                        columns: x => new { x.CompanyId, x.BatchId },
                        principalSchema: "finance",
                        principalTable: "OfficialInvoiceImportBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialInvoiceImportApprovals_CompanyId_BatchId",
                schema: "finance",
                table: "OfficialInvoiceImportApprovals",
                columns: new[] { "CompanyId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfficialInvoiceImportCommands_CompanyId_CommandName_IdempotencyKeyHash",
                schema: "finance",
                table: "OfficialInvoiceImportCommands",
                columns: new[] { "CompanyId", "CommandName", "IdempotencyKeyHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [finance].[OfficialInvoiceImportBatches])
                   OR EXISTS (SELECT 1 FROM [finance].[OfficialInvoiceImportApprovals])
                   OR EXISTS (SELECT 1 FROM [finance].[OfficialInvoiceImportCommands])
                BEGIN
                    THROW 51051, N'Resmî fatura import geçmişi varken migration geri alınamaz; forward-fix uygulayın.', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "OfficialInvoiceImportApprovals",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "OfficialInvoiceImportCommands",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "OfficialInvoiceImportBatches",
                schema: "finance");
        }
    }
}
