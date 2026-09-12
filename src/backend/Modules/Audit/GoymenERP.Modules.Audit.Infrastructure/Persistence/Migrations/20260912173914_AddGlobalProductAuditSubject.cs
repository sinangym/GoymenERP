using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalProductAuditSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                schema: "audit",
                table: "AuditEvents",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "AuthorityRevision",
                schema: "audit",
                table: "AuditEvents",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<Guid>(
                name: "OriginCompanyId",
                schema: "audit",
                table: "AuditEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectKind",
                schema: "audit",
                table: "AuditEvents",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Company",
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_SubjectKind_EntityType_EntityId_OccurredAtUtc_Id",
                schema: "audit",
                table: "AuditEvents",
                columns: new[] { "SubjectKind", "EntityType", "EntityId", "OccurredAtUtc", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditEvents_Subject",
                schema: "audit",
                table: "AuditEvents",
                sql: "([SubjectKind] = N'Company' AND [CompanyId] IS NOT NULL AND [OriginCompanyId] IS NULL AND [AuthorityRevision] IS NULL AND [SchemaVersion] = 1) OR ([SubjectKind] = N'GroupSharedProduct' AND [CompanyId] IS NULL AND [SchemaVersion] = 2 AND [OriginCompanyId] IS NOT NULL AND [OriginCompanyId] <> '00000000-0000-0000-0000-000000000000' AND [AuthorityRevision] IS NOT NULL AND LEN([AuthorityRevision]) > 0 AND [EntityType] = N'Product.Product' AND LEN([EntityId]) = 36 AND TRY_CONVERT(uniqueidentifier, [EntityId]) IS NOT NULL AND TRY_CONVERT(uniqueidentifier, [EntityId]) <> '00000000-0000-0000-0000-000000000000')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never erase V2 history or fabricate a company owner during rollback.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [audit].[AuditEvents]
                    WHERE [SubjectKind] <> N'Company' OR [CompanyId] IS NULL OR [SchemaVersion] <> 1)
                    THROW 51041, 'Global Product audit history requires a forward fix; rollback cannot discard or reassign it.', 1;
                """);
            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_SubjectKind_EntityType_EntityId_OccurredAtUtc_Id",
                schema: "audit",
                table: "AuditEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditEvents_Subject",
                schema: "audit",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "AuthorityRevision",
                schema: "audit",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "OriginCompanyId",
                schema: "audit",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "SubjectKind",
                schema: "audit",
                table: "AuditEvents");

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                schema: "audit",
                table: "AuditEvents",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
