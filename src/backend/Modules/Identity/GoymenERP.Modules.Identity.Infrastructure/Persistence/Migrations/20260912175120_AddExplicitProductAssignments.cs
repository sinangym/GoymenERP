using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExplicitProductAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductMasterAssignments",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<int>(type: "int", nullable: false),
                    IssuerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuerExternalIdentityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecisionReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RevocationDecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevocationDecisionReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductMasterAssignments", x => x.Id);
                    table.CheckConstraint("CK_ProductMasterAssignments_Identities", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [UserId] <> '00000000-0000-0000-0000-000000000000' AND [RoleId] <> '00000000-0000-0000-0000-000000000000' AND [DecisionId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ProductMasterAssignments_Operation", "[Operation] IN (1, 2, 3)");
                    table.CheckConstraint("CK_ProductMasterAssignments_Revocation", "([IsActive] = 1 AND [RevocationDecisionId] IS NULL AND [RevocationDecisionReference] IS NULL AND [RevokedAtUtc] IS NULL) OR ([IsActive] = 0 AND [RevocationDecisionId] IS NOT NULL AND [RevocationDecisionId] <> [DecisionId] AND [RevocationDecisionId] <> '00000000-0000-0000-0000-000000000000' AND [RevocationDecisionReference] IS NOT NULL AND [RevokedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET, [RevokedAtUtc]) = 0)");
                    table.CheckConstraint("CK_ProductMasterAssignments_Validity", "DATEPART(TZOFFSET, [ValidFromUtc]) = 0 AND ([ValidUntilUtc] IS NULL OR ([ValidUntilUtc] > [ValidFromUtc] AND DATEPART(TZOFFSET, [ValidUntilUtc]) = 0))");
                    table.ForeignKey(
                        name: "FK_ProductMasterAssignments_ExternalIdentities_IssuerExternalIdentityId",
                        column: x => x.IssuerExternalIdentityId,
                        principalSchema: "identity",
                        principalTable: "ExternalIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductMasterAssignments_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "identity",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductMasterAssignments_Users_IssuerUserId",
                        column: x => x.IssuerUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductMasterAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductMasterAssignments_DecisionId",
                schema: "identity",
                table: "ProductMasterAssignments",
                column: "DecisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductMasterAssignments_IssuerExternalIdentityId",
                schema: "identity",
                table: "ProductMasterAssignments",
                column: "IssuerExternalIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductMasterAssignments_IssuerUserId",
                schema: "identity",
                table: "ProductMasterAssignments",
                column: "IssuerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductMasterAssignments_RevocationDecisionId",
                schema: "identity",
                table: "ProductMasterAssignments",
                column: "RevocationDecisionId",
                unique: true,
                filter: "[RevocationDecisionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductMasterAssignments_RoleId",
                schema: "identity",
                table: "ProductMasterAssignments",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductMasterAssignments_UserId_Operation",
                schema: "identity",
                table: "ProductMasterAssignments",
                columns: new[] { "UserId", "Operation" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [identity].[ProductMasterAssignments])
                    THROW 51042, 'Product assignment provenance must be retained; use a forward fix.', 1;
                """);
            migrationBuilder.DropTable(
                name: "ProductMasterAssignments",
                schema: "identity");
        }
    }
}
