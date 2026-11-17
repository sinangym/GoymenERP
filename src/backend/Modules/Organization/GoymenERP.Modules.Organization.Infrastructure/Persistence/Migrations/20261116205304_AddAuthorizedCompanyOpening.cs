using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizedCompanyOpening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyOpeningExecutions",
                schema: "organization",
                columns: table => new
                {
                    IdempotencyKeyHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalCorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyOpeningExecutions", x => x.IdempotencyKeyHash);
                    table.CheckConstraint("CK_CompanyOpeningExecutions_Shape", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ActorUserId] <> '00000000-0000-0000-0000-000000000000' AND [StatusCode] IN (201,409) AND ISJSON([ResponseJson]) = 1 AND LEN([IdempotencyKeyHash]) = 64 AND [IdempotencyKeyHash] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND LEN([RequestHash]) = 64 AND [RequestHash] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND LEN([OriginalCorrelationId]) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyOpeningExecutions_CompanyId_RecordedAtUtc",
                schema: "organization",
                table: "CompanyOpeningExecutions",
                columns: new[] { "CompanyId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[CompanyOpeningExecutions])
                BEGIN
                    THROW 51948, 'AddAuthorizedCompanyOpening history exists; rollback is refused (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CompanyOpeningExecutions",
                schema: "organization");
        }
    }
}
