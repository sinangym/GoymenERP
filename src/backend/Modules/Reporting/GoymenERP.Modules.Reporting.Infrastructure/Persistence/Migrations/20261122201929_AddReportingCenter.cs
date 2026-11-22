using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reporting");

            migrationBuilder.CreateTable(
                name: "CommandAttempts",
                schema: "reporting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKeySha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    Command = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RefusalCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandAttempts", x => new { x.CompanyId, x.ActorUserId, x.IdempotencyKeySha256 });
                    table.CheckConstraint("CK_ReportingCommandAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ActorUserId] <> '00000000-0000-0000-0000-000000000000' AND [TargetId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Command]) > 0 AND LEN([CorrelationId]) > 0 AND (([Status] IN (200, 201) AND [RefusalCode] IS NULL) OR ([Status] = 409 AND [RefusalCode] IS NOT NULL AND LEN([RefusalCode]) > 0))");
                    table.CheckConstraint("CK_ReportingCommandAttempts_KeyHash", "DATALENGTH([IdempotencyKeySha256]) = 64 AND [IdempotencyKeySha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_ReportingCommandAttempts_RequestHash", "DATALENGTH([RequestSha256]) = 64 AND [RequestSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.ForeignKey(
                        name: "FK_CommandAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExportRecords",
                schema: "reporting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ReportId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Format = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ParametersJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ColumnsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Page = table.Column<int>(type: "int", nullable: true),
                    PageSize = table.Column<int>(type: "int", nullable: true),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    ByteLength = table.Column<int>(type: "int", nullable: false),
                    ContentSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportRecords", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_ReportingExportRecords_Hash", "DATALENGTH([ContentSha256]) = 64 AND [ContentSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_ReportingExportRecords_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [ActorUserId] <> '00000000-0000-0000-0000-000000000000' AND [Kind] IN (1, 2) AND LEN([ReportId]) > 0 AND [Format] IN ('csv', 'xlsx', 'zip') AND (([Kind] = 1 AND [Format] IN ('csv', 'xlsx') AND [Page] IS NOT NULL AND [Page] >= 1 AND [PageSize] IS NOT NULL AND [PageSize] >= 1) OR ([Kind] = 2 AND [Format] = 'zip' AND [Page] IS NULL AND [PageSize] IS NULL)) AND [RowCount] >= 0 AND [ByteLength] > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ParametersJson]) = 1 AND ISJSON([ColumnsJson]) = 1");
                    table.ForeignKey(
                        name: "FK_ExportRecords_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SavedViews",
                schema: "reporting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ColumnsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedViews", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_ReportingSavedViews_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [OwnerUserId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ReportId]) > 0 AND LEN([Name]) > 0 AND [Version] >= 1 AND [UpdatedAtUtc] >= [CreatedAtUtc] AND ISJSON([ParametersJson]) = 1 AND ISJSON([ColumnsJson]) = 1");
                    table.ForeignKey(
                        name: "FK_SavedViews_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExportRecords_CompanyId_ActorUserId_RecordedAtUtc",
                schema: "reporting",
                table: "ExportRecords",
                columns: new[] { "CompanyId", "ActorUserId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExportRecords_CompanyId_RecordedAtUtc_Id",
                schema: "reporting",
                table: "ExportRecords",
                columns: new[] { "CompanyId", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_ReportingSavedViews_ActiveName",
                schema: "reporting",
                table: "SavedViews",
                columns: new[] { "CompanyId", "OwnerUserId", "ReportId", "Name" },
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WP14 forward fix: an export record is the proof of what left the system, a saved view is a person's own record
            // and an attempt is an idempotency answer; while any exists the tables are not dropped. An empty store rolls back.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [reporting].[ExportRecords])
                    OR EXISTS (SELECT 1 FROM [reporting].[SavedViews])
                    OR EXISTS (SELECT 1 FROM [reporting].[CommandAttempts])
                BEGIN
                    THROW 52270, 'Reporting export records, saved views or command attempts exist; AddReportingCenter is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CommandAttempts",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "ExportRecords",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "SavedViews",
                schema: "reporting");
        }
    }
}
