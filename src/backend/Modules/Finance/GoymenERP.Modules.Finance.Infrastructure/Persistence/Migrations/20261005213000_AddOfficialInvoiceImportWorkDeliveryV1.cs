using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficialInvoiceImportWorkDeliveryV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProducerSchemaVersion",
                schema: "finance",
                table: "OfficialInvoiceImportCommands",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OfficialInvoiceImportCommands_ProducerSchema",
                schema: "finance",
                table: "OfficialInvoiceImportCommands",
                sql: "[ProducerSchemaVersion] IS NULL OR [ProducerSchemaVersion] = 1");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OfficialInvoiceImportCommands_CompanyId_Id",
                schema: "finance",
                table: "OfficialInvoiceImportCommands",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateTable(
                name: "OfficialInvoiceImportWorkDeliveries",
                schema: "finance",
                columns: table => new
                {
                    DeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKey = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    ScopeKey = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    ObservationOrdinal = table.Column<long>(type: "bigint", nullable: false),
                    DesiredState = table.Column<int>(type: "int", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadSchemaVersion = table.Column<int>(type: "int", nullable: false),
                    PayloadUtf8 = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    PayloadSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialInvoiceImportWorkDeliveries", x => x.DeliveryId);
                    table.CheckConstraint("CK_OfficialInvoiceImportWorkDeliveries_Content", "[SourceKey] = 'finance.official-invoice-import.v1' AND [ScopeKey] = 'batch:' + LOWER(CONVERT(char(36), [BatchId])) AND DATALENGTH([PayloadUtf8]) > 0 AND DATALENGTH([PayloadSha256]) = 64 AND [PayloadSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_OfficialInvoiceImportWorkDeliveries_Identity", "[DeliveryId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [CommandReceiptId] = [DeliveryId] AND [BatchId] <> '00000000-0000-0000-0000-000000000000' AND [EvidenceId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_OfficialInvoiceImportWorkDeliveries_State", "[PayloadSchemaVersion] = 1 AND (([ObservationOrdinal] = 1 AND [DesiredState] = 1 AND [EvidenceId] = [BatchId]) OR ([ObservationOrdinal] = 2 AND [DesiredState] = 2))");
                    table.ForeignKey(
                        name: "FK_OfficialInvoiceImportWorkDeliveries_OfficialInvoiceImportBatches_CompanyId_BatchId",
                        columns: x => new { x.CompanyId, x.BatchId },
                        principalSchema: "finance",
                        principalTable: "OfficialInvoiceImportBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfficialInvoiceImportWorkDeliveries_OfficialInvoiceImportCommands_CompanyId_CommandReceiptId",
                        columns: x => new { x.CompanyId, x.CommandReceiptId },
                        principalSchema: "finance",
                        principalTable: "OfficialInvoiceImportCommands",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialInvoiceImportWorkDeliveries_CompanyId_BatchId_ObservationOrdinal",
                schema: "finance",
                table: "OfficialInvoiceImportWorkDeliveries",
                columns: new[] { "CompanyId", "BatchId", "ObservationOrdinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfficialInvoiceImportWorkDeliveries_CompanyId_CommandReceiptId",
                schema: "finance",
                table: "OfficialInvoiceImportWorkDeliveries",
                columns: new[] { "CompanyId", "CommandReceiptId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [finance].[OfficialInvoiceImportWorkDeliveries])
                   OR EXISTS (SELECT 1 FROM [finance].[OfficialInvoiceImportCommands] WHERE [ProducerSchemaVersion] IS NOT NULL)
                BEGIN
                    THROW 51051, N'Finance Work teslim geçmişi varken migration geri alınamaz; forward-fix uygulayın.', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "OfficialInvoiceImportWorkDeliveries",
                schema: "finance");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OfficialInvoiceImportCommands_ProducerSchema",
                schema: "finance",
                table: "OfficialInvoiceImportCommands");

            migrationBuilder.DropColumn(
                name: "ProducerSchemaVersion",
                schema: "finance",
                table: "OfficialInvoiceImportCommands");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OfficialInvoiceImportCommands_CompanyId_Id",
                schema: "finance",
                table: "OfficialInvoiceImportCommands");
        }
    }
}
