using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReferenceListEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReferenceListEntries",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceListEntries", x => new { x.CompanyId, x.EntryId });
                    table.CheckConstraint("CK_SettingsReferenceListEntries_Code", "LEN([Code]) BETWEEN 1 AND 32 AND [Code] NOT LIKE '%[^A-Z0-9._-]%' AND LEFT([Code], 1) LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_SettingsReferenceListEntries_Created", "LEN(LTRIM(RTRIM([CreatedBy]))) > 0");
                    table.CheckConstraint("CK_SettingsReferenceListEntries_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EntryId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsReferenceListEntries_Kind", "[Kind] IN ('ExpenseType','ServiceType','PaintColor','ScrapKind')");
                });

            migrationBuilder.CreateTable(
                name: "ReferenceListEntryVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceListEntryVersions", x => new { x.CompanyId, x.EntryId, x.Version });
                    table.CheckConstraint("CK_SettingsReferenceListEntryVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EntryId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsReferenceListEntryVersions_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_SettingsReferenceListEntryVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.ForeignKey(
                        name: "FK_ReferenceListEntryVersions_ReferenceListEntries_CompanyId_EntryId",
                        columns: x => new { x.CompanyId, x.EntryId },
                        principalSchema: "settings",
                        principalTable: "ReferenceListEntries",
                        principalColumns: new[] { "CompanyId", "EntryId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceListEntries_CompanyId_Kind_Code",
                schema: "settings",
                table: "ReferenceListEntries",
                columns: new[] { "CompanyId", "Kind", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix (KIM-D49/KIM-D51): an entry or version is recorded configuration and is never dropped by a rollback.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[ReferenceListEntries])
                    OR EXISTS (SELECT 1 FROM [settings].[ReferenceListEntryVersions])
                BEGIN
                    THROW 51886, 'Reference list entries or versions exist; AddReferenceListEntries is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "ReferenceListEntryVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "ReferenceListEntries",
                schema: "settings");
        }
    }
}
