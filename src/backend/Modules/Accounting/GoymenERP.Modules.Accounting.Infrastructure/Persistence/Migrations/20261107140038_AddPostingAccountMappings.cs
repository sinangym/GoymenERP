using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPostingAccountMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PostingAccountMappingVersions",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MappingVersion = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    AccountPlanSourceVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AccountPlanSourceSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActivatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActivatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingAccountMappingVersions", x => new { x.CompanyId, x.MappingVersion });
                    table.CheckConstraint("CK_PostingAccountMappingVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [MappingVersion] >= 1 AND [PreparedByUserId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PostingAccountMappingVersions_MakerChecker", "[ActivatedByUserId] IS NULL OR [ActivatedByUserId] <> [PreparedByUserId]");
                    table.CheckConstraint("CK_PostingAccountMappingVersions_Plan", "LEN([AccountPlanSourceVersion]) > 0 AND LEN([AccountPlanSourceSha256]) = 64");
                    table.CheckConstraint("CK_PostingAccountMappingVersions_Status", "([Status] = 1 AND [ActivatedByUserId] IS NULL AND [ActivatedAtUtc] IS NULL) OR ([Status] IN (2, 3) AND [ActivatedByUserId] IS NOT NULL AND [ActivatedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [ActivatedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_PostingAccountMappingVersions_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_PostingAccountMappingVersions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PostingAccountMappingRows",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MappingVersion = table.Column<long>(type: "bigint", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    MappingKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ItemType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    LinePurpose = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    IsExport = table.Column<bool>(type: "bit", nullable: true),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    VatRateCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingAccountMappingRows", x => new { x.CompanyId, x.MappingVersion, x.LineNumber });
                    table.CheckConstraint("CK_PostingAccountMappingRows_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [MappingVersion] >= 1 AND [LineNumber] >= 1 AND [AccountId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PostingAccountMappingRows_Key", "([Purpose] = N'sales-invoice' AND [MappingKey] IN (N'receivable', N'revenue', N'vat-output', N'withholding-receivable')) OR ([Purpose] = N'purchase-invoice' AND [MappingKey] IN (N'payable', N'inventory', N'vat-input', N'withholding-payable', N'expense'))");
                    table.CheckConstraint("CK_PostingAccountMappingRows_Text", "LEN([AccountCode]) > 0 AND ([ItemType] IS NULL OR LEN([ItemType]) > 0) AND ([LinePurpose] IS NULL OR LEN([LinePurpose]) > 0)");
                    table.CheckConstraint("CK_PostingAccountMappingRows_VatRate", "[VatRateCode] IS NULL OR (LEN([VatRateCode]) > 0 AND [MappingKey] IN (N'vat-output', N'vat-input'))");
                    table.ForeignKey(
                        name: "FK_PostingAccountMappingRows_PostingAccountMappingVersions_CompanyId_MappingVersion",
                        columns: x => new { x.CompanyId, x.MappingVersion },
                        principalSchema: "accounting",
                        principalTable: "PostingAccountMappingVersions",
                        principalColumns: new[] { "CompanyId", "MappingVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_PostingAccountMappingRows_Selector",
                schema: "accounting",
                table: "PostingAccountMappingRows",
                columns: new[] { "CompanyId", "MappingVersion", "Purpose", "MappingKey", "ItemType", "LinePurpose", "IsExport", "VatRateCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PostingAccountMappingVersions_Active",
                schema: "accounting",
                table: "PostingAccountMappingVersions",
                column: "CompanyId",
                unique: true,
                filter: "[Status] = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a stored mapping version (prepared, active or superseded) is configuration history that a
            // drop would lose; while one exists the rollback refuses (51212, used by no other migration).
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [accounting].[PostingAccountMappingVersions])
                BEGIN
                    ;THROW 51212, N'Accounting posting account mappings exist; AddPostingAccountMappings is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "PostingAccountMappingRows",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "PostingAccountMappingVersions",
                schema: "accounting");
        }
    }
}
