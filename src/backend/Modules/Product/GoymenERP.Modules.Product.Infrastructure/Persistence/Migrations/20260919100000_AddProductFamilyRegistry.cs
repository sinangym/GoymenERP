using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// PRD-FAM-1: the Product-owned family registry (families, template versions and the families they
    /// serve, template fields, controlled dictionary entries with their approved/candidate status and
    /// source) and the card's reference to a released family. No row is written: an empty registry is
    /// "no released definition" and stays 503 unavailable; a release is loaded only by the decided route.
    /// No card held a family before this (every family id was refused), so the new foreign key has no
    /// existing row to check. Down refuses while any Product or registry row exists (forward fix).
    /// </summary>
    public partial class AddProductFamilyRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DictionaryEntries",
                schema: "product",
                columns: table => new
                {
                    Dictionary = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Turkish_100_CI_AS"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceRef = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ParentCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    MassGm2 = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    IsExplicitNone = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DictionaryEntries", x => new { x.Dictionary, x.Code });
                    table.CheckConstraint("CK_DictionaryEntries_ExplicitNone", "[IsExplicitNone] = 0 OR [Status] = 1");
                    table.CheckConstraint("CK_DictionaryEntries_Mass", "[MassGm2] IS NULL OR [MassGm2] > 0");
                    table.CheckConstraint("CK_DictionaryEntries_Status", "[Status] IN (1, 2)");
                    table.CheckConstraint("CK_DictionaryEntries_Text", "LEN([Dictionary]) > 0 AND LEN([Code]) > 0 AND LEN([DisplayName]) > 0 AND LEN([SourceRef]) > 0 AND ([ParentCode] IS NULL OR LEN([ParentCode]) > 0)");
                });

            migrationBuilder.CreateTable(
                name: "Families",
                schema: "product",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Turkish_100_CI_AS"),
                    Cins = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Turkish_100_CI_AS"),
                    Tur = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Turkish_100_CI_AS"),
                    Tip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Turkish_100_CI_AS"),
                    RuleSet = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceRef = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Families", x => x.Id);
                    table.CheckConstraint("CK_Families_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Families_Text", "LEN([Code]) > 0 AND LEN([Name]) > 0 AND LEN([Cins]) > 0 AND LEN([Tur]) > 0 AND LEN([Tip]) > 0 AND LEN([RuleSet]) > 0 AND LEN([SourceRef]) > 0");
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                schema: "product",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Turkish_100_CI_AS"),
                    SourceRef = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => new { x.Id, x.Version });
                    table.CheckConstraint("CK_Templates_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1");
                    table.CheckConstraint("CK_Templates_Text", "LEN([Code]) > 0 AND LEN([Name]) > 0 AND LEN([SourceRef]) > 0");
                });

            migrationBuilder.CreateTable(
                name: "TemplateFamilies",
                schema: "product",
                columns: table => new
                {
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersion = table.Column<long>(type: "bigint", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateFamilies", x => new { x.TemplateId, x.TemplateVersion, x.FamilyId });
                    table.ForeignKey(
                        name: "FK_TemplateFamilies_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalSchema: "product",
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemplateFamilies_Templates_TemplateId_TemplateVersion",
                        columns: x => new { x.TemplateId, x.TemplateVersion },
                        principalSchema: "product",
                        principalTable: "Templates",
                        principalColumns: new[] { "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemplateFields",
                schema: "product",
                columns: table => new
                {
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersion = table.Column<long>(type: "bigint", nullable: false),
                    Key = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    Dictionary = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    AllowsCandidate = table.Column<bool>(type: "bit", nullable: false),
                    AppliesWhenField = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    AppliesWhenCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ParentField = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateFields", x => new { x.TemplateId, x.TemplateVersion, x.Key });
                    table.CheckConstraint("CK_TemplateFields_Candidate", "[Required] = 0 OR [AllowsCandidate] = 0");
                    table.CheckConstraint("CK_TemplateFields_Condition", "([AppliesWhenField] IS NULL AND [AppliesWhenCode] IS NULL) OR ([AppliesWhenField] IS NOT NULL AND [AppliesWhenCode] IS NOT NULL)");
                    table.CheckConstraint("CK_TemplateFields_Key", "LEN([Key]) > 0 AND [Position] >= 1");
                    table.CheckConstraint("CK_TemplateFields_Kind", "([Kind] = 1 AND [Dictionary] IS NULL AND [AllowsCandidate] = 0 AND [ParentField] IS NULL) OR ([Kind] = 2 AND [Dictionary] IS NOT NULL AND LEN([Dictionary]) > 0)");
                    table.ForeignKey(
                        name: "FK_TemplateFields_Templates_TemplateId_TemplateVersion",
                        columns: x => new { x.TemplateId, x.TemplateVersion },
                        principalSchema: "product",
                        principalTable: "Templates",
                        principalColumns: new[] { "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_FamilyId",
                schema: "product",
                table: "Products",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_Families_Code",
                schema: "product",
                table: "Families",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemplateFamilies_FamilyId",
                schema: "product",
                table: "TemplateFamilies",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateFields_TemplateId_TemplateVersion_Position",
                schema: "product",
                table: "TemplateFields",
                columns: new[] { "TemplateId", "TemplateVersion", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Templates_Code_Version",
                schema: "product",
                table: "Templates",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Families_FamilyId",
                schema: "product",
                table: "Products",
                column: "FamilyId",
                principalSchema: "product",
                principalTable: "Families",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [product].[Products])
                    OR EXISTS (SELECT 1 FROM [product].[Families])
                    OR EXISTS (SELECT 1 FROM [product].[Templates])
                    OR EXISTS (SELECT 1 FROM [product].[DictionaryEntries])
                    THROW 51044, 'Product identity and the family registry require a forward fix.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Families_FamilyId",
                schema: "product",
                table: "Products");

            migrationBuilder.DropTable(
                name: "DictionaryEntries",
                schema: "product");

            migrationBuilder.DropTable(
                name: "TemplateFamilies",
                schema: "product");

            migrationBuilder.DropTable(
                name: "TemplateFields",
                schema: "product");

            migrationBuilder.DropTable(
                name: "Families",
                schema: "product");

            migrationBuilder.DropTable(
                name: "Templates",
                schema: "product");

            migrationBuilder.DropIndex(
                name: "IX_Products_FamilyId",
                schema: "product",
                table: "Products");
        }
    }
}
