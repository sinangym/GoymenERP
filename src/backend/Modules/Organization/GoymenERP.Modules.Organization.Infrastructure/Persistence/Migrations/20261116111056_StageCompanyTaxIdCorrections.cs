using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StageCompanyTaxIdCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyTaxIdCorrections",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StagedVkn = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CorrectedVkn = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CompanyVersionAtStaging = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StagedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AppliedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyTaxIdCorrections", x => x.Id);
                    table.CheckConstraint("CK_CompanyTaxIdCorrections_Applied", "([Status] = N'Pending' AND [AppliedBy] IS NULL AND [AppliedAtUtc] IS NULL) OR ([Status] = N'Applied' AND [AppliedBy] IS NOT NULL AND [AppliedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_CompanyTaxIdCorrections_CorrectedVkn", "[CorrectedVkn] IS NULL OR (LEN([CorrectedVkn]) = 10 AND [CorrectedVkn] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%' AND [CorrectedVkn] <> [StagedVkn])");
                    table.CheckConstraint("CK_CompanyTaxIdCorrections_StagedVkn", "LEN([StagedVkn]) = 10 AND [StagedVkn] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%'");
                    table.CheckConstraint("CK_CompanyTaxIdCorrections_Status", "[Status] IN (N'Pending', N'Applied')");
                    table.CheckConstraint("CK_CompanyTaxIdCorrections_Version", "[Version] >= 1 AND [CompanyVersionAtStaging] >= 1");
                    table.ForeignKey(
                        name: "FK_CompanyTaxIdCorrections_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyTaxIdCorrections_CompanyId_Pending",
                schema: "organization",
                table: "CompanyTaxIdCorrections",
                column: "CompanyId",
                unique: true,
                filter: "[Status] = N'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyTaxIdCorrections_CompanyId_StagedAtUtc",
                schema: "organization",
                table: "CompanyTaxIdCorrections",
                columns: new[] { "CompanyId", "StagedAtUtc" });

            // KIM-D41 + KIM-D48: the stand-in "VKN" (ten repeated ones) the fiscal seed wrote on the placeholder company is
            // not rewritten here. One pending correction per affected company is staged with the old value, the new value
            // (NULL = "VKN girilmemiş"), this migration as its source and the company version it was read at. The VKN
            // moves only when a person holding the approve permission applies the row; that apply writes the single audit
            // event (system actor = this source, approver = the person). A company that already has a pending
            // correction is skipped, so a re-run stages nothing twice.
            migrationBuilder.Sql("""
                INSERT INTO [organization].[CompanyTaxIdCorrections]
                    ([Id], [CompanyId], [Source], [StagedVkn], [CorrectedVkn], [CompanyVersionAtStaging], [Reason],
                     [Status], [StagedAtUtc], [AppliedBy], [AppliedAtUtc], [Version])
                SELECT NEWID(), [c].[Id], N'organization.migration.stage-company-tax-id-corrections', [c].[Vkn], NULL,
                       [c].[Version],
                       N'Yer tutucu VKN işaretlendi: mali tohum kaydının yazdığı on adet 1 gerçek bir VKN değildir; onaylandığında şirket "VKN girilmemiş" olur.',
                       N'Pending', SYSDATETIMEOFFSET(), NULL, NULL, 1
                FROM [organization].[Companies] AS [c]
                WHERE [c].[IsPlaceholder] = 1
                  AND [c].[InvoiceCapable] = 0
                  AND [c].[Vkn] = N'1111111111'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [organization].[CompanyTaxIdCorrections] AS [p]
                      WHERE [p].[CompanyId] = [c].[Id]
                        AND [p].[Status] = N'Pending');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: an applied correction is the approval trail next to its audit event; dropping it would lose who
            // approved which VKN change. Pending rows only describe a change that has not happened and may be dropped.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[CompanyTaxIdCorrections] WHERE [Status] = N'Applied')
                BEGIN
                    THROW 51877, 'An applied company tax id correction exists; StageCompanyTaxIdCorrections is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CompanyTaxIdCorrections",
                schema: "organization");
        }
    }
}
